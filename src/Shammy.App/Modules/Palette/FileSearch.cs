using System.Diagnostics;
using System.Runtime.InteropServices;
using Shammy.Core.Palette;

namespace Shammy.Modules.Palette;

/// <summary>
/// Asks the Windows Search index (the one Explorer and Start use) for files, through ADO and the
/// Search.CollatorDSO OLE DB provider, both part of Windows. Queries run one at a time on a worker
/// thread that keeps its connection open; only the newest request waits, so fast typing never queues
/// up stale queries. If the index service is off there are simply no file results (no crawling).
/// </summary>
internal sealed class FileSearch : IDisposable
{
    const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows';";
    static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    readonly object _gate = new();
    readonly AutoResetEvent _wake = new(false);
    readonly Thread _thread;
    Request? _pending;
    volatile bool _disposed;
    DateTime _unavailableUntil;

    public sealed record Request(int Id, string Sql, Action<int, IReadOnlyList<FileHit>?, long> Done);

    public FileSearch()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Shammy file search", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>Null until the first query; false when the index couldn't be reached.</summary>
    public bool? Available { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Replaces any request still waiting. <c>Done(id, hits or null, ms)</c> runs on the worker thread.</summary>
    public void Submit(Request request)
    {
        lock (_gate) _pending = request;
        _wake.Set();
    }

    /// <summary>Drops a request that hasn't started (the text changed or the box closed).</summary>
    public void CancelPending()
    {
        lock (_gate) _pending = null;
    }

    void Loop()
    {
        dynamic? connection = null;
        while (!_disposed)
        {
            _wake.WaitOne();
            Request? request;
            lock (_gate)
            {
                request = _pending;
                _pending = null;
            }
            if (request is null || _disposed) continue;

            var sw = Stopwatch.StartNew();
            IReadOnlyList<FileHit>? hits = null;
            if (DateTime.UtcNow >= _unavailableUntil)
            {
                try
                {
                    connection ??= Open();
                    hits = Query(connection, request.Sql);
                    Available = true;
                    LastError = null;
                }
                catch (Exception ex)
                {
                    // Index service stopped or disabled, or the provider is missing: no file rows for a while.
                    Available = false;
                    LastError = ex.Message.Trim();
                    _unavailableUntil = DateTime.UtcNow + RetryAfter;
                    Infrastructure.Log.Info($"Windows Search unavailable ({LastError})");
                    Release(ref connection);
                }
            }
            try { request.Done(request.Id, hits, sw.ElapsedMilliseconds); }
            catch (Exception ex) { Infrastructure.Log.Warn("Delivering file results failed", ex); }
        }
        Release(ref connection);
    }

    static dynamic Open()
    {
        var type = Type.GetTypeFromProgID("ADODB.Connection") ?? throw new InvalidOperationException("ADO is not available");
        dynamic connection = Activator.CreateInstance(type)!;
        connection.CommandTimeout = 3;
        connection.Open(ConnectionString);
        return connection;
    }

    static List<FileHit> Query(dynamic connection, string sql)
    {
        var hits = new List<FileHit>();
        dynamic? rs = null;
        try
        {
            rs = connection.Execute(sql);
            while (!(bool)rs.EOF)
            {
                dynamic fields = rs.Fields;
                object? path = fields.Item("System.ItemPathDisplay").Value;
                object? type = fields.Item("System.ItemType").Value;
                Marshal.ReleaseComObject(fields);
                if (path is string p && p.Length > 0)
                    hits.Add(new FileHit(p, type is string t && t.Equals("Directory", StringComparison.OrdinalIgnoreCase)));
                rs.MoveNext();
            }
        }
        finally
        {
            if (rs is not null)
            {
                try { rs.Close(); } catch (Exception) { }
                Marshal.ReleaseComObject(rs);
            }
        }
        return hits;
    }

    static void Release(ref dynamic? connection)
    {
        if (connection is null) return;
        try { connection.Close(); } catch (Exception) { }
        try { Marshal.FinalReleaseComObject(connection); } catch (Exception) { }
        connection = null;
    }

    public void Dispose()
    {
        _disposed = true;
        _wake.Set();
    }
}
