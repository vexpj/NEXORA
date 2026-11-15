using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NEXORA.Core.Servers;

public sealed class ServerEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    /// <summary>Профиль, с которым запускать этот сервер (необязательно).</summary>
    public string? ProfileId { get; set; }
}

public readonly record struct ServerEndpoint(string Host, int Port)
{
    public override string ToString() => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}

public static class ServerAddress
{
    private static readonly Regex HostRx = new(@"^[A-Za-z0-9]([A-Za-z0-9\-\.]{0,251}[A-Za-z0-9])?$", RegexOptions.Compiled);

    /// <summary>«host», «host:port», «1.2.3.4:25570», «[::1]:25565». Порт по умолчанию — 25565.</summary>
    public static bool TryParse(string? text, out ServerEndpoint endpoint)
    {
        endpoint = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        string host = text; var port = 25565;
        if (text.StartsWith('['))
        {
            var end = text.IndexOf(']');
            if (end < 0) return false;
            host = text[1..end];
            if (!IPAddress.TryParse(host, out _)) return false;
            if (end + 1 < text.Length)
            {
                if (text[end + 1] != ':' || !TryPort(text[(end + 2)..], out port)) return false;
            }
            endpoint = new ServerEndpoint(host, port);
            return true;
        }
        var colon = text.LastIndexOf(':');
        if (colon >= 0)
        {
            if (text.IndexOf(':') != colon) return false; // IPv6 без скобок — не поддерживаем
            host = text[..colon];
            if (!TryPort(text[(colon + 1)..], out port)) return false;
        }
        if (!HostRx.IsMatch(host)) return false;
        endpoint = new ServerEndpoint(host, port);
        return true;
    }

    private static bool TryPort(string s, out int port) => int.TryParse(s, out port) && port is >= 1 and <= 65535;
}

public sealed record ServerStatus(bool Online, string Motd, string Version, int Players, int MaxPlayers, int LatencyMs, string? Error)
{
    public static ServerStatus Offline(string error) => new(false, "", "", 0, 0, 0, error);
    public static ServerStatus Unknown => new(false, "", "", 0, 0, 0, null);
}

/// <summary>Протокол Server List Ping: кодирование запроса и разбор ответа.</summary>
public static class McProtocol
{
    public const int MaxStatusBytes = 512 * 1024;

    public static void WriteVarInt(Stream s, int value)
    {
        var v = (uint)value;
        while ((v & ~0x7Fu) != 0) { s.WriteByte((byte)((v & 0x7F) | 0x80)); v >>= 7; }
        s.WriteByte((byte)v);
    }

    public static async Task<int> ReadVarIntAsync(Stream s, CancellationToken ct)
    {
        var result = 0; var shift = 0; var buf = new byte[1];
        while (true)
        {
            if (await s.ReadAsync(buf.AsMemory(0, 1), ct) == 0) throw new EndOfStreamException("Сервер закрыл соединение.");
            result |= (buf[0] & 0x7F) << shift;
            if ((buf[0] & 0x80) == 0) return result;
            shift += 7;
            if (shift >= 35) throw new InvalidDataException("Некорректный ответ сервера (VarInt).");
        }
    }

    /// <summary>Пакет Handshake (следующее состояние — status) и сразу пакет Status Request.</summary>
    public static byte[] BuildStatusRequest(string host, int port)
    {
        var body = new MemoryStream();
        WriteVarInt(body, 0x00);
        WriteVarInt(body, -1);
        var h = Encoding.UTF8.GetBytes(host);
        WriteVarInt(body, h.Length); body.Write(h);
        body.WriteByte((byte)(port >> 8)); body.WriteByte((byte)port);
        WriteVarInt(body, 1);
        var pkt = new MemoryStream();
        WriteVarInt(pkt, (int)body.Length);
        pkt.Write(body.ToArray());
        pkt.WriteByte(1); pkt.WriteByte(0x00);
        return pkt.ToArray();
    }

    public static ServerStatus ParseStatusJson(string json, int latencyMs)
    {
        var n = JsonNode.Parse(json) ?? throw new InvalidDataException("Пустой ответ сервера.");
        var motd = Flatten(n["description"]);
        motd = Regex.Replace(motd, "§.", "").Replace('\n', ' ').Trim();
        if (motd.Length > 200) motd = motd[..200] + "…";
        return new ServerStatus(true, motd, n["version"]?["name"]?.GetValue<string>() ?? "",
            n["players"]?["online"]?.GetValue<int>() ?? 0, n["players"]?["max"]?.GetValue<int>() ?? 0, latencyMs, null);
    }

    private static string Flatten(JsonNode? d)
    {
        if (d is null) return "";
        if (d is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        var sb = new StringBuilder();
        if (d["text"]?.GetValue<string>() is { } t) sb.Append(t);
        if (d["extra"] is JsonArray extra) foreach (var e in extra) sb.Append(Flatten(e));
        return sb.ToString();
    }
}
