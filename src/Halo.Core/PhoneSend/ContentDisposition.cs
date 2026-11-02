using System.Text;

namespace Halo.Core.PhoneSend;

/// <summary>Building the download header and reading the one multipart parts carry.</summary>
public static class ContentDisposition
{
    /// <summary>
    /// "attachment" with an ASCII fallback filename plus an RFC 5987 filename* so phones keep
    /// non-ASCII names (emoji, accents, CJK) intact.
    /// </summary>
    public static string Attachment(string fileName)
    {
        var sb = new StringBuilder("attachment; filename=\"");
        foreach (var r in fileName.EnumerateRunes())
        {
            char c = r.IsAscii ? (char)r.Value : '_';
            // Quotes, backslashes and controls would break or confuse the quoted-string.
            sb.Append(c is '"' or '\\' || c < ' ' || c == 0x7F ? '_' : c);
        }
        sb.Append("\"; filename*=UTF-8''");
        foreach (var b in Encoding.UTF8.GetBytes(fileName))
        {
            if (IsAttrChar(b)) sb.Append((char)b);
            else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    // RFC 5987 attr-char: ALPHA / DIGIT / "!" / "#" / "$" / "&" / "+" / "-" / "." / "^" / "_" / "`" / "|" / "~"
    static bool IsAttrChar(byte b) =>
        b is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9'
        || "!#$&+-.^_`|~".IndexOf((char)b) >= 0;

    /// <summary>
    /// Parameters of a Content-Disposition value (keys lower-cased). Quoted strings honour backslash
    /// escapes; filename* is decoded from RFC 5987 form.
    /// </summary>
    public static Dictionary<string, string> Parameters(string? value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(value)) return result;
        int i = value.IndexOf(';');
        if (i < 0) return result;
        i++;
        while (i < value.Length)
        {
            while (i < value.Length && (value[i] is ' ' or '\t' or ';')) i++;
            int nameStart = i;
            while (i < value.Length && value[i] is not ('=' or ';')) i++;
            var name = value[nameStart..i].Trim();
            if (i >= value.Length || value[i] == ';')
            {
                continue;
            }
            i++; // '='
            while (i < value.Length && value[i] is ' ' or '\t') i++;
            string v;
            if (i < value.Length && value[i] == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < value.Length && value[i] != '"')
                {
                    if (value[i] == '\\' && i + 1 < value.Length) i++;
                    sb.Append(value[i++]);
                }
                i++; // closing quote (or past the end)
                v = sb.ToString();
                while (i < value.Length && value[i] != ';') i++;
            }
            else
            {
                int vs = i;
                while (i < value.Length && value[i] != ';') i++;
                v = value[vs..i].Trim();
            }
            if (name.Length == 0) continue;
            if (name.EndsWith('*'))
            {
                if (DecodeExtended(v) is { } decoded) result[name[..^1]] = decoded;
                continue;
            }
            // filename* wins over filename regardless of order.
            result.TryAdd(name, v);
        }
        return result;
    }

    /// <summary>The filename a multipart part (or a download) declares, or null.</summary>
    public static string? FileName(string? value) =>
        Parameters(value).TryGetValue("filename", out var f) ? f : null;

    static string? DecodeExtended(string v)
    {
        // charset'language'percent-encoded
        int a = v.IndexOf('\''), b = a < 0 ? -1 : v.IndexOf('\'', a + 1);
        if (b < 0) return null;
        var charset = v[..a];
        Encoding enc;
        try { enc = Encoding.GetEncoding(charset.Length == 0 ? "utf-8" : charset); }
        catch (ArgumentException) { return null; }
        var bytes = new List<byte>(v.Length);
        for (int i = b + 1; i < v.Length; i++)
        {
            if (v[i] == '%' && i + 2 < v.Length && byte.TryParse(v.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var x))
            {
                bytes.Add(x);
                i += 2;
            }
            else if (v[i] < 0x80)
            {
                bytes.Add((byte)v[i]);
            }
            else
            {
                return null;
            }
        }
        return enc.GetString(bytes.ToArray());
    }
}
