using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDV.Host;

public sealed class HostServer : IDisposable
{
    private readonly Config _config;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _clientConnected;

    public event Action<string>? StatusChanged;

    public HostServer(Config config)
    {
        _config = config;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{config.Port}/rdv/");
    }

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoop(_cts.Token);
        StatusChanged?.Invoke($"Listening on port {_config.Port}");
    }

    public void Stop()
    {
        _cts.Cancel();
        _listener.Stop();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                if (!ctx.Request.IsWebSocketRequest) { ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                var wsCtx = await ctx.AcceptWebSocketAsync(null);
                _ = HandleClient(wsCtx.WebSocket, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private async Task HandleClient(WebSocket ws, CancellationToken ct)
    {
        if (_clientConnected)
        {
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Already connected", ct);
            return;
        }

        _clientConnected = true;
        StatusChanged?.Invoke("Client connecting...");

        try
        {
            // Auth handshake
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            await SendText(ws, JsonSerializer.Serialize(new { type = "challenge", nonce }), ct);

            var msg = await ReceiveText(ws, ct);
            if (msg == null) return;

            var obj = JsonNode.Parse(msg);
            var response = obj?["response"]?.GetValue<string>() ?? "";

            if (!_config.VerifyResponse(nonce, response))
            {
                await SendText(ws, JsonSerializer.Serialize(new { type = "auth_fail" }), ct);
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth failed", ct);
                return;
            }

            await SendText(ws, JsonSerializer.Serialize(new { type = "auth_ok" }), ct);
            StatusChanged?.Invoke("Client connected");

            using var capture = new ScreenCapture(60);
            await SendText(ws, JsonSerializer.Serialize(new
            {
                type = "screen_info",
                width = capture.ScreenWidth,
                height = capture.ScreenHeight
            }), ct);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var streamTask = StreamFrames(ws, capture, linked.Token);
            var receiveTask = ReceiveInputs(ws, linked.Token);

            await Task.WhenAny(streamTask, receiveTask);
            linked.Cancel();
        }
        catch { }
        finally
        {
            _clientConnected = false;
            StatusChanged?.Invoke($"Listening on port {_config.Port}");
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
            ws.Dispose();
        }
    }

    private static async Task StreamFrames(WebSocket ws, ScreenCapture capture, CancellationToken ct)
    {
        int lastW = capture.ScreenWidth, lastH = capture.ScreenHeight;

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            try
            {
                var frame = capture.CaptureFrame();

                // Notify viewer if screen dimensions changed
                if (capture.ScreenWidth != lastW || capture.ScreenHeight != lastH)
                {
                    lastW = capture.ScreenWidth; lastH = capture.ScreenHeight;
                    await SendText(ws, JsonSerializer.Serialize(new
                    {
                        type = "screen_info",
                        width = lastW,
                        height = lastH
                    }), ct);
                }

                await ws.SendAsync(frame.AsMemory(), WebSocketMessageType.Binary, true, ct);
                await Task.Delay(50, ct); // ~20 fps
            }
            catch { break; }
        }
    }

    private static async Task ReceiveInputs(WebSocket ws, CancellationToken ct)
    {
        var buf = new byte[8192];
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            try
            {
                var result = await ws.ReceiveAsync(buf, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType != WebSocketMessageType.Text) continue;

                var json = Encoding.UTF8.GetString(buf, 0, result.Count);
                var obj = JsonNode.Parse(json);
                var kind = obj?["kind"]?.GetValue<string>();

                switch (kind)
                {
                    case "mouse_move":
                        InputInjector.MouseMove(obj!["x"]!.GetValue<int>(), obj!["y"]!.GetValue<int>());
                        break;
                    case "mouse_down":
                    case "mouse_up":
                        InputInjector.MouseButton(obj!["button"]!.GetValue<string>(), kind == "mouse_down");
                        break;
                    case "mouse_wheel":
                        InputInjector.MouseWheel(obj!["delta"]!.GetValue<int>());
                        break;
                    case "key_down":
                    case "key_up":
                        InputInjector.KeyEvent((ushort)obj!["vk"]!.GetValue<int>(), kind == "key_down");
                        break;
                }
            }
            catch { break; }
        }
    }

    private static async Task SendText(WebSocket ws, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await ws.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, ct);
    }

    private static async Task<string?> ReceiveText(WebSocket ws, CancellationToken ct)
    {
        var buf = new byte[4096];
        var result = await ws.ReceiveAsync(buf, ct);
        if (result.MessageType == WebSocketMessageType.Close) return null;
        return Encoding.UTF8.GetString(buf, 0, result.Count);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _listener.Close();
    }
}
