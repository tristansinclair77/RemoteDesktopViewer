using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDV.Viewer;

public sealed record RemoteScreen(int Index, string Name, int Width, int Height, bool Primary);

public sealed class ViewerClient : IAsyncDisposable
{
    private readonly ClientWebSocket _ws = new();
    private readonly CancellationTokenSource _cts = new();

    public event Action<byte[]>? FrameReceived;
    public event Action<int, int>? ScreenInfoReceived;
    public event Action<RemoteScreen[], int>? ScreenListReceived;
    public event Action? Disconnected;
    public event Action? HeartbeatReceived;

    public int RemoteWidth { get; private set; } = 1920;
    public int RemoteHeight { get; private set; } = 1080;

    public async Task ConnectAsync(string host, int port, string password)
    {
        await _ws.ConnectAsync(new Uri($"ws://{host}:{port}/rdv/"), _cts.Token);

        // Challenge-response auth — password never sent in plain text
        var challengeMsg = await ReceiveTextAsync(_cts.Token);
        var challenge = JsonNode.Parse(challengeMsg ?? throw new Exception("No challenge received."));
        var nonce = challenge!["nonce"]!.GetValue<string>();

        var passwordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        var response = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash + nonce)));

        await SendTextAsync(JsonSerializer.Serialize(new { type = "auth", response }), _cts.Token);

        var authResult = await ReceiveTextAsync(_cts.Token);
        var authObj = JsonNode.Parse(authResult ?? "");
        if (authObj?["type"]?.GetValue<string>() != "auth_ok")
            throw new Exception("Authentication failed. Check your password.");

        _ = ReceiveLoopAsync(_cts.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buf = new byte[65536]; // 64 KB chunks; large frames span multiple reads
        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                WebSocketMessageType msgType = WebSocketMessageType.Binary;

                do
                {
                    result = await _ws.ReceiveAsync(buf, ct);
                    if (result.MessageType == WebSocketMessageType.Close) goto done;
                    msgType = result.MessageType;
                    ms.Write(buf, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var data = ms.ToArray();

                if (msgType == WebSocketMessageType.Binary)
                {
                    FrameReceived?.Invoke(data);
                }
                else if (msgType == WebSocketMessageType.Text)
                {
                    var obj = JsonNode.Parse(Encoding.UTF8.GetString(data));
                    var type = obj?["type"]?.GetValue<string>();
                    if (type == "screen_info")
                    {
                        RemoteWidth = obj!["width"]!.GetValue<int>();
                        RemoteHeight = obj!["height"]!.GetValue<int>();
                        ScreenInfoReceived?.Invoke(RemoteWidth, RemoteHeight);
                    }
                    else if (type == "heartbeat")
                    {
                        HeartbeatReceived?.Invoke();
                    }
                    else if (type == "screen_list")
                    {
                        var arr = obj!["screens"]!.AsArray();
                        var list = new RemoteScreen[arr.Count];
                        for (int i = 0; i < arr.Count; i++)
                        {
                            var s = arr[i]!;
                            list[i] = new RemoteScreen(
                                s["index"]!.GetValue<int>(),
                                s["name"]?.GetValue<string>() ?? $"Display {i + 1}",
                                s["width"]!.GetValue<int>(),
                                s["height"]!.GetValue<int>(),
                                s["primary"]?.GetValue<bool>() ?? false);
                        }
                        var selected = obj["selected"]?.GetValue<int>() ?? 0;
                        ScreenListReceived?.Invoke(list, selected);
                    }
                }
            }
            done:;
        }
        catch { }
        finally { Disconnected?.Invoke(); }
    }

    public Task SendMouseMoveAsync(int x, int y) =>
        SendTextAsync(JsonSerializer.Serialize(new { type = "input", kind = "mouse_move", x, y }), _cts.Token);

    public Task SendMouseButtonAsync(string button, bool down) =>
        SendTextAsync(JsonSerializer.Serialize(new { type = "input", kind = down ? "mouse_down" : "mouse_up", button }), _cts.Token);

    public Task SendMouseWheelAsync(int delta) =>
        SendTextAsync(JsonSerializer.Serialize(new { type = "input", kind = "mouse_wheel", delta }), _cts.Token);

    public Task SendKeyAsync(int vk, bool down) =>
        SendTextAsync(JsonSerializer.Serialize(new { type = "input", kind = down ? "key_down" : "key_up", vk }), _cts.Token);

    public Task SendSelectScreenAsync(int index) =>
        SendTextAsync(JsonSerializer.Serialize(new { type = "input", kind = "select_screen", index }), _cts.Token);

    public Task SendSasAsync() =>
        SendTextAsync(JsonSerializer.Serialize(new { type = "input", kind = "send_sas" }), _cts.Token);

    public Task SendRestartAsync() =>
        SendTextAsync(JsonSerializer.Serialize(new { type = "input", kind = "restart" }), _cts.Token);

    private async Task SendTextAsync(string text, CancellationToken ct)
    {
        if (_ws.State != WebSocketState.Open) return;
        await _ws.SendAsync(Encoding.UTF8.GetBytes(text).AsMemory(), WebSocketMessageType.Text, true, ct);
    }

    private async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        var buf = new byte[4096];
        var result = await _ws.ReceiveAsync(buf, ct);
        return result.MessageType == WebSocketMessageType.Close
            ? null
            : Encoding.UTF8.GetString(buf, 0, result.Count);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
        }
        catch { }
        _ws.Dispose();
        _cts.Dispose();
    }
}
