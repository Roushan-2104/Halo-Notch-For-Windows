using System.Text;

namespace Shammy.Core.PhoneSend;

/// <summary>The request line and headers of one HTTP/1.1 request (the body is read separately).</summary>
public sealed class HttpRequestHead
{
    /// <summary>Anything bigger is not a phone browser talking to us; refuse rather than buffer it.</summary>
    public const int MaxHeadBytes = 16 * 1024;

    HttpRequestHead(string method, string target, string version, Dictionary<string, string> headers)
    {
        Method = method;
        Target = target;
        Version = version;
        Headers = headers;
    }

    public string Method { get; }

    /// <summary>The raw request target, e.g. "/abc?x=1".</summary>
    public string Target { get; }

    /// <summary>The target without its query string.</summary>
    public string Path => Target.IndexOf('?') is var q and >= 0 ? Target[..q] : Target;

    public string Version { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }

    public string? this[string name] => Headers.TryGetValue(name, out var v) ? v : null;

    /// <summary>Content-Length, or null when absent or malformed.</summary>
    public long? ContentLength =>
        this["Content-Length"] is { } s && long.TryParse(s.Trim(), System.Globalization.NumberStyles.None, null, out var n) ? n : null;

    public bool ExpectsContinue => string.Equals(this["Expect"]?.Trim(), "100-continue", StringComparison.OrdinalIgnoreCase);

    public bool IsChunked => this["Transfer-Encoding"]?.Contains("chunked", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Index just past the blank line that ends the head ("\r\n\r\n", or a bare "\n\n" from sloppy clients),
    /// or -1 when more bytes are needed.
    /// </summary>
    public static int FindHeadEnd(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != '\n') continue;
            if (i + 1 < data.Length && data[i + 1] == '\n') return i + 2;
            if (i + 2 < data.Length && data[i + 1] == '\r' && data[i + 2] == '\n') return i + 3;
        }
        return -1;
    }

    /// <summary>Parses a complete head (request line, headers, blank line). Header bytes are Latin-1 per RFC 9110.</summary>
    public static bool TryParse(ReadOnlySpan<byte> head, out HttpRequestHead? request)
    {
        request = null;
        if (head.Length == 0 || head.Length > MaxHeadBytes) return false;
        var lines = Encoding.Latin1.GetString(head).Split('\n');
        int first = 0;
        // RFC 9112 asks servers to ignore at least one empty line before the request line.
        while (first < lines.Length && lines[first].TrimEnd('\r').Length == 0) first++;
        if (first >= lines.Length) return false;

        var parts = lines[first].TrimEnd('\r').Split(' ');
        if (parts.Length != 3) return false;
        var (method, target, version) = (parts[0], parts[1], parts[2]);
        if (method.Length == 0 || !method.All(IsTokenChar)) return false;
        if (target.Length == 0 || (target[0] != '/' && target != "*")) return false;
        if (target.Any(c => c <= ' ' || c >= 0x7F)) return false;
        if (version is not ("HTTP/1.1" or "HTTP/1.0")) return false;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = first + 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0) break;
            int colon = line.IndexOf(':');
            if (colon <= 0) return false;
            var name = line[..colon];
            // Whitespace before the colon is a request-smuggling vector; RFC 9112 says reject.
            if (!name.All(IsTokenChar)) return false;
            var value = line[(colon + 1)..].Trim(' ', '\t');
            headers[name] = headers.TryGetValue(name, out var prev) ? prev + ", " + value : value;
        }
        request = new HttpRequestHead(method, target, version, headers);
        return true;
    }

    static bool IsTokenChar(char c) =>
        c is > ' ' and < (char)0x7F && "()<>@,;:\\\"/[]?={}".IndexOf(c) < 0;
}

/// <summary>A single "Range: bytes=a-b" request. Multiple ranges are deliberately treated as no range.</summary>
public static class ByteRange
{
    public enum Kind { None, Satisfiable, Unsatisfiable }

    /// <summary>
    /// Interprets a Range header against a resource of <paramref name="length"/> bytes. Syntax we don't
    /// support (multi-range, other units, garbage) yields <see cref="Kind.None"/>, which RFC 9110 allows:
    /// the server simply sends the whole thing with 200.
    /// </summary>
    public static Kind Parse(string? header, long length, out long start, out long end)
    {
        start = 0;
        end = length - 1;
        if (string.IsNullOrWhiteSpace(header)) return Kind.None;
        var h = header.Trim();
        if (!h.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return Kind.None;
        var spec = h[6..].Trim();
        if (spec.Contains(',')) return Kind.None;
        int dash = spec.IndexOf('-');
        if (dash < 0) return Kind.None;
        var a = spec[..dash].Trim();
        var b = spec[(dash + 1)..].Trim();
        const System.Globalization.NumberStyles digits = System.Globalization.NumberStyles.None;

        if (a.Length == 0)
        {
            // Suffix range: the last N bytes.
            if (!long.TryParse(b, digits, null, out var suffix)) return Kind.None;
            if (suffix == 0 || length == 0) return Kind.Unsatisfiable;
            start = Math.Max(0, length - suffix);
            end = length - 1;
            return Kind.Satisfiable;
        }

        if (!long.TryParse(a, digits, null, out var s)) return Kind.None;
        long e = length - 1;
        if (b.Length > 0)
        {
            if (!long.TryParse(b, digits, null, out e)) return Kind.None;
            if (e < s) return Kind.None;
        }
        if (s >= length) return Kind.Unsatisfiable;
        start = s;
        end = Math.Min(e, length - 1);
        return Kind.Satisfiable;
    }
}
