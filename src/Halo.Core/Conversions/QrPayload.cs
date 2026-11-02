using System.Text;

namespace Halo.Core.Conversions;

public enum QrKind { Url, Wifi, Text }

/// <summary>What a decoded QR code or barcode holds, so a circle can act on it (open, join Wi-Fi, copy).</summary>
public sealed record QrPayload(QrKind Kind, string Text, string? Host = null, string? Ssid = null, string? Password = null)
{
    /// <summary>
    /// http(s) links (and bare "www." ones) are URLs; "WIFI:T:WPA;S:name;P:secret;;" is a network; everything
    /// else (other schemes like upi:, mailto:, tel:, plain text, product numbers) is just text to copy.
    /// </summary>
    public static QrPayload Parse(string raw)
    {
        var text = (raw ?? "").Trim();
        if (text.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase) && TryParseWifi(text, out var ssid, out var password))
            return new QrPayload(QrKind.Wifi, text, Ssid: ssid, Password: password);

        var candidate = text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + text : text;
        if (!candidate.Any(char.IsWhiteSpace)
            && Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.Host.Length > 0)
        {
            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            return new QrPayload(QrKind.Url, uri.AbsoluteUri, Host: host);
        }
        return new QrPayload(QrKind.Text, text);
    }

    // Fields are "K:value;" in any order, with \ escaping ; , : " and \ inside values.
    static bool TryParseWifi(string text, out string ssid, out string? password)
    {
        ssid = "";
        password = null;
        var fields = new Dictionary<char, string>();
        var body = text[5..];
        var value = new StringBuilder();
        char? key = null;
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (key is null)
            {
                if (c == ';') continue;
                if (i + 1 < body.Length && body[i + 1] == ':')
                {
                    key = char.ToUpperInvariant(c);
                    i++;
                }
                else return false;
                continue;
            }
            if (c == '\\' && i + 1 < body.Length)
            {
                value.Append(body[++i]);
                continue;
            }
            if (c == ';')
            {
                fields[key.Value] = value.ToString();
                value.Clear();
                key = null;
                continue;
            }
            value.Append(c);
        }
        if (key is not null) fields[key.Value] = value.ToString();

        if (!fields.TryGetValue('S', out var s) || s.Length == 0) return false;
        ssid = s;
        if (fields.TryGetValue('P', out var p) && p.Length > 0) password = p;
        return true;
    }
}
