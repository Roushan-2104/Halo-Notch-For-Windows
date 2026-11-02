using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Halo.Core.Launcher;
using static Halo.Modules.Launcher.SnippetNative;

namespace Halo.Modules.Launcher;

/// <summary>
/// Erases a typed trigger and types its expansion with SendInput, on its own thread so the keyboard hook
/// callback has long returned (and its key reached the app) before anything is injected.
/// </summary>
internal sealed class SnippetTyper : IDisposable
{
    const int ChunkInputs = 64;

    sealed record Job(int Erase, string Template);

    readonly BlockingCollection<Job> _jobs = new();
    readonly Func<string?> _clipboard;
    readonly Action<string, Exception?> _warn;

    public SnippetTyper(Func<string?> clipboard, Action<string, Exception?> warn)
    {
        _clipboard = clipboard;
        _warn = warn;
        new Thread(Run) { IsBackground = true, Name = "Halo snippet typer" }.Start();
    }

    public void Enqueue(int erase, string template)
    {
        try { _jobs.TryAdd(new Job(erase, template)); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    void Run()
    {
        try
        {
            foreach (var job in _jobs.GetConsumingEnumerable())
            {
                try { Type(job); }
                catch (Exception ex) { _warn("Typing a snippet failed", ex); }
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    void Type(Job job)
    {
        // Give the trigger's last key time to land, and wait out held modifiers: Ctrl+Backspace would erase words.
        Thread.Sleep(30);
        for (int i = 0; i < 50 && (IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN)); i++)
            Thread.Sleep(10);

        var text = SnippetText.Expand(job.Template, DateTime.Now, _clipboard);
        var inputs = new List<INPUT>(job.Erase * 2 + text.Length * 2);
        for (int i = 0; i < job.Erase; i++)
        {
            inputs.Add(Key(VK_BACK, false));
            inputs.Add(Key(VK_BACK, true));
        }
        foreach (var c in text)
        {
            switch (c)
            {
                case '\r':
                    break;
                // Unicode '\n' arrives as a raw character most editors ignore; Enter is what users mean.
                case '\n':
                    inputs.Add(Key(VK_RETURN, false));
                    inputs.Add(Key(VK_RETURN, true));
                    break;
                case '\t':
                    inputs.Add(Key(VK_TAB, false));
                    inputs.Add(Key(VK_TAB, true));
                    break;
                default:
                    inputs.Add(Unicode(c, false));
                    inputs.Add(Unicode(c, true));
                    break;
            }
        }
        Send(inputs);
    }

    /// <summary>Small batches with a breath between them; some apps (Electron, terminals) drop a huge burst.</summary>
    void Send(List<INPUT> inputs)
    {
        int size = Marshal.SizeOf<INPUT>();
        for (int i = 0; i < inputs.Count; i += ChunkInputs)
        {
            var chunk = inputs.GetRange(i, Math.Min(ChunkInputs, inputs.Count - i)).ToArray();
            if (SendInput((uint)chunk.Length, chunk, size) != chunk.Length)
            {
                // Blocked by UIPI (an elevated window) or the secure desktop; nothing more to do.
                _warn($"SendInput sent only part of a snippet: {Marshal.GetLastWin32Error()}", null);
                return;
            }
            if (i + ChunkInputs < inputs.Count) Thread.Sleep(4);
        }
    }

    public void Dispose() => _jobs.CompleteAdding();
}
