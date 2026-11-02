using System.Text;

namespace Halo.Core.PhoneSend;

/// <summary>One part of a multipart/form-data body; its content is read with <see cref="MultipartReader"/>.</summary>
public sealed class MultipartSection
{
    public MultipartSection(IReadOnlyDictionary<string, string> headers)
    {
        Headers = headers;
        var cd = headers.TryGetValue("Content-Disposition", out var v) ? v : null;
        var p = ContentDisposition.Parameters(cd);
        Name = p.TryGetValue("name", out var n) ? n : null;
        FileName = p.TryGetValue("filename", out var f) ? f : null;
    }

    public IReadOnlyDictionary<string, string> Headers { get; }
    public string? Name { get; }

    /// <summary>The client's filename, untrusted and unsanitized. Null for plain form fields.</summary>
    public string? FileName { get; }

    public bool IsFile => FileName is not null;
}

/// <summary>
/// Reads multipart/form-data as a stream: part bodies are copied straight to their destination with only
/// a fixed buffer in memory, so a multi-gigabyte video upload costs 64 KB of RAM.
/// </summary>
public sealed class MultipartReader
{
    const int MaxSectionHeadBytes = 16 * 1024;
    static readonly byte[] Crlf = "\r\n"u8.ToArray();
    static readonly byte[] HeadEnd = "\r\n\r\n"u8.ToArray();

    enum State { Preamble, Body, AfterDelimiter, End }

    readonly Stream _input;
    readonly byte[] _delimiter;
    readonly byte[] _buf;
    int _start, _end;
    bool _eof;
    State _state = State.Preamble;

    public MultipartReader(Stream input, string boundary, int bufferSize = 64 * 1024)
    {
        _input = input;
        _delimiter = Encoding.ASCII.GetBytes("\r\n--" + boundary);
        _buf = new byte[Math.Max(bufferSize, _delimiter.Length * 2 + MaxSectionHeadBytes)];
        // The first boundary may sit at the very start of the body with no CRLF before it; pretending
        // there was one lets a single delimiter pattern find every boundary.
        _buf[0] = (byte)'\r';
        _buf[1] = (byte)'\n';
        _end = 2;
    }

    /// <summary>The boundary parameter of a multipart Content-Type, or null if missing or invalid.</summary>
    public static string? BoundaryFrom(string? contentType)
    {
        if (contentType is null) return null;
        var media = contentType.Split(';', 2)[0].Trim();
        if (!media.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)) return null;
        // Reuse the parameter parser; it only needs a leading "type;".
        var p = ContentDisposition.Parameters(contentType);
        if (!p.TryGetValue("boundary", out var b)) return null;
        // RFC 2046: 1-70 characters, and none that would need escaping in our delimiter search.
        if (b.Length is 0 or > 70 || b.Any(c => c is < ' ' or > '~')) return null;
        return b;
    }

    /// <summary>Moves to the next part, skipping any unread body. Null when the closing boundary is reached.</summary>
    public async Task<MultipartSection?> ReadNextSectionAsync(CancellationToken ct = default)
    {
        if (_state is State.Preamble or State.Body) await CopyBodyCoreAsync(null, long.MaxValue, null, ct);
        if (_state == State.End) return null;

        // After a delimiter: "--" closes the body; otherwise optional padding then CRLF and headers.
        if (!await EnsureAsync(2, ct)) throw new InvalidDataException("Multipart body ended after a boundary");
        if (_buf[_start] == '-' && _buf[_start + 1] == '-')
        {
            _state = State.End;
            return null;
        }
        while (true)
        {
            if (!await EnsureAsync(1, ct)) throw new InvalidDataException("Multipart body ended after a boundary");
            if (_buf[_start] is (byte)' ' or (byte)'\t') _start++;
            else break;
        }

        // Headers run from the CRLF after the boundary to the first blank line. If the CRLF is
        // immediately followed by another, the part has no headers at all.
        int headEnd;
        while ((headEnd = FindSectionHeadEnd()) < 0)
        {
            if (!await FillAsync(ct)) throw new InvalidDataException("Multipart body ended inside part headers");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headEnd > 0)
        {
            // Browsers send raw UTF-8 in filenames, so decode as UTF-8 rather than Latin-1.
            var text = Encoding.UTF8.GetString(_buf, _start + 2, headEnd - 2);
            foreach (var line in text.Split("\r\n"))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }
        _start += headEnd + HeadEnd.Length;
        _state = State.Body;
        return new MultipartSection(headers);
    }

    /// <summary>Offset of the blank line ending the part headers, or -1 if more bytes are needed.</summary>
    int FindSectionHeadEnd()
    {
        var avail = _buf.AsSpan(_start, _end - _start);
        if (avail.Length >= 2 && !avail.StartsWith(Crlf)) throw new InvalidDataException("Malformed multipart boundary line");
        int i = avail.IndexOf(HeadEnd);
        if (i >= 0) return i;
        if (avail.Length > MaxSectionHeadBytes) throw new InvalidDataException("Multipart headers too large");
        return -1;
    }

    /// <summary>
    /// Copies the current part's content to <paramref name="destination"/>. Throws
    /// <see cref="InvalidDataException"/> past <paramref name="maxBytes"/> or if the body is truncated.
    /// </summary>
    public Task<long> CopyBodyToAsync(Stream destination, long maxBytes = long.MaxValue, Action<long>? progress = null, CancellationToken ct = default)
    {
        if (_state != State.Body) throw new InvalidOperationException("No part is being read");
        return CopyBodyCoreAsync(destination, maxBytes, progress, ct);
    }

    async Task<long> CopyBodyCoreAsync(Stream? destination, long maxBytes, Action<long>? progress, CancellationToken ct)
    {
        long total = 0;
        while (true)
        {
            int avail = _end - _start;
            int idx = _buf.AsSpan(_start, avail).IndexOf(_delimiter);
            // Without a match, everything except a possible partial delimiter at the tail is content.
            int take = idx >= 0 ? idx : Math.Max(0, avail - (_delimiter.Length - 1));
            if (take > 0)
            {
                total += take;
                if (total > maxBytes) throw new InvalidDataException("Part is larger than allowed");
                if (destination is not null) await destination.WriteAsync(_buf.AsMemory(_start, take), ct);
                _start += take;
                progress?.Invoke(total);
            }
            if (idx >= 0)
            {
                _start += _delimiter.Length;
                _state = State.AfterDelimiter;
                return total;
            }
            if (!await FillAsync(ct)) throw new InvalidDataException("Multipart body ended before its closing boundary");
        }
    }

    async Task<bool> EnsureAsync(int count, CancellationToken ct)
    {
        while (_end - _start < count)
        {
            if (!await FillAsync(ct)) return false;
        }
        return true;
    }

    /// <summary>Compacts the buffer and reads more. False at end of input.</summary>
    async Task<bool> FillAsync(CancellationToken ct)
    {
        if (_eof) return false;
        if (_start > 0)
        {
            Buffer.BlockCopy(_buf, _start, _buf, 0, _end - _start);
            _end -= _start;
            _start = 0;
        }
        if (_end == _buf.Length) throw new InvalidDataException("Multipart buffer overflow");
        int n = await _input.ReadAsync(_buf.AsMemory(_end), ct);
        if (n == 0)
        {
            _eof = true;
            return false;
        }
        _end += n;
        return true;
    }
}
