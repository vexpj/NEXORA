using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using NEXORA.Core.Abstractions;
using NEXORA.Core.Servers;
using NEXORA.Infrastructure.Storage;

namespace NEXORA.Infrastructure.Servers;

public sealed class JsonServerStore : IServerStore
{
    private readonly string _path;
    public JsonServerStore(string path) => _path = path;
    public List<ServerEntry> Load() => JsonFile.Read<List<ServerEntry>>(_path) ?? new();
    public void Save(IEnumerable<ServerEntry> servers) => JsonFile.WriteAtomic(_path, servers.ToList());
}

/// <summary>Проверка доступности сервера по протоколу Server List Ping. Записи SRV в DNS не используются: для нестандартного порта укажите его в адресе.</summary>
public sealed class ServerPinger
{
    public async Task<ServerStatus> PingAsync(ServerEndpoint ep, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var client = new TcpClient();
            var sw = Stopwatch.StartNew();
            await client.ConnectAsync(ep.Host, ep.Port, cts.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync(McProtocol.BuildStatusRequest(ep.Host, ep.Port), cts.Token);

            var length = await McProtocol.ReadVarIntAsync(stream, cts.Token);
            var packetId = await McProtocol.ReadVarIntAsync(stream, cts.Token);
            if (length <= 0 || packetId != 0x00) throw new InvalidDataException("Неожиданный ответ сервера.");
            var jsonLength = await McProtocol.ReadVarIntAsync(stream, cts.Token);
            if (jsonLength <= 0 || jsonLength > McProtocol.MaxStatusBytes) throw new InvalidDataException("Недопустимый размер ответа.");
            var buf = new byte[jsonLength];
            await stream.ReadExactlyAsync(buf, cts.Token);
            sw.Stop();
            return McProtocol.ParseStatusJson(System.Text.Encoding.UTF8.GetString(buf), (int)sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return ServerStatus.Offline("нет ответа (таймаут)"); }
        catch (SocketException) { return ServerStatus.Offline("недоступен"); }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or JsonException or IOException or FormatException)
        { return ServerStatus.Offline("некорректный ответ"); }
    }
}
