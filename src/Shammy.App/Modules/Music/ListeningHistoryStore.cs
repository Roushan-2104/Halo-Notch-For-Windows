using System.Security.Cryptography;
using System.Text.Json;
using Shammy.Core.Music;
using Shammy.Infrastructure;

namespace Shammy.Modules.Music;

/// <summary>
/// Keeps <see cref="ListeningHistory"/> in %LOCALAPPDATA%\Shammy\song-history.dat, encrypted for this Windows
/// account like the module stores. It never leaves the PC: voice reads it to expect song names, nothing else.
/// </summary>
internal sealed class ListeningHistoryStore
{
    static readonly byte[] Entropy = "Shammy song history v1"u8.ToArray();
    static string FilePath => Path.Combine(Paths.Root, "song-history.dat");

    readonly object _lock = new();
    int _version;

    public ListeningHistory Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
                return new ListeningHistory(JsonSerializer.Deserialize<List<HeardSong>>(json));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or UnauthorizedAccessException)
        {
            // Only a convenience for voice; starting over is fine (and the next save replaces the bad file).
            Log.Warn("Song history unreadable; starting fresh", ex);
        }
        return new ListeningHistory();
    }

    /// <summary>Removes the file (and cancels any save still queued).</summary>
    public void Delete()
    {
        Interlocked.Increment(ref _version);
        _ = Task.Run(() =>
        {
            lock (_lock)
            {
                try
                {
                    File.Delete(FilePath);
                    File.Delete(FilePath + ".tmp");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Warn("Deleting the song history failed", ex);
                }
            }
        });
    }

    /// <summary>Writes a snapshot off the UI thread; the latest snapshot wins.</summary>
    public void SaveInBackground(IReadOnlyList<HeardSong> songs)
    {
        var snapshot = songs.Select(s => new HeardSong { Title = s.Title, Artist = s.Artist, Plays = s.Plays, LastPlayed = s.LastPlayed }).ToList();
        int version = Interlocked.Increment(ref _version);
        _ = Task.Run(() =>
        {
            lock (_lock)
            {
                if (version != Volatile.Read(ref _version)) return; // a newer snapshot is queued
                try
                {
                    var tmp = FilePath + ".tmp";
                    var json = JsonSerializer.SerializeToUtf8Bytes(snapshot);
                    File.WriteAllBytes(tmp, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
                    File.Move(tmp, FilePath, true);
                }
                catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
                {
                    Log.Warn("Saving the song history failed", ex);
                }
            }
        });
    }
}
