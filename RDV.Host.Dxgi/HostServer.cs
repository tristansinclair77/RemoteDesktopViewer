using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDV.Host.Dxgi;

public sealed class HostServer : IDisposable
{
    private readonly Config _config;
    private HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _clientConnected;

    public event Action<string>? StatusChanged;

    public HostServer(Config config)
    {
        _config = config;
        _listener = NewListener();
    }

    private HttpListener NewListener()
    {
        var l = new HttpListener();
        l.Prefixes.Add($"http://+:{_config.Port}/rdv/");
        return l;
    }

    public void Start()
    {
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 32 || ex.ErrorCode == 183)
        {
            StatusChanged?.Invoke($"Port {_config.Port} in use — freeing it");
            if (!PortReclaimer.TryFreePort(_config.Port)) throw;
            Thread.Sleep(500);
            _listener = NewListener();
            _listener.Start();
        }
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

            using var capture = new DxgiScreenCapture(40);

            var descriptors = capture.EnumerateScreens();
            await SendText(ws, JsonSerializer.Serialize(new
            {
                type = "screen_list",
                screens = descriptors.Select(s => new
                {
                    index = s.Index,
                    name = s.Name,
                    width = s.Width,
                    height = s.Height,
                    primary = s.Primary
                }).ToArray(),
                selected = capture.SelectedIndex
            }), ct);

            await SendText(ws, JsonSerializer.Serialize(new
            {
                type = "screen_info",
                width = capture.ScreenWidth,
                height = capture.ScreenHeight,
                index = capture.SelectedIndex
            }), ct);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var streamTask = StreamFrames(ws, capture, linked.Token);
            var receiveTask = ReceiveInputs(ws, capture, linked.Token);

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

    private static async Task StreamFrames(WebSocket ws, DxgiScreenCapture capture, CancellationToken ct)
    {
        int lastW = -1, lastH = -1, lastIdx = -1;
        ulong lastHash = 0;
        var lastSentAt = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            try
            {
                var frame = await capture.CaptureFrameAsync(ct);

                if (frame.Width != lastW || frame.Height != lastH || frame.ScreenIndex != lastIdx)
                {
                    lastW = frame.Width; lastH = frame.Height; lastIdx = frame.ScreenIndex;
                    lastHash = 0;
                    await SendText(ws, JsonSerializer.Serialize(new
                    {
                        type = "screen_info",
                        width = lastW,
                        height = lastH,
                        index = lastIdx
                    }), ct);
                }

                var hash = frame.Data.Length > 0
                    ? System.IO.Hashing.XxHash64.HashToUInt64(frame.Data)
                    : 0;

                if (hash != lastHash)
                {
                    lastHash = hash;
                    lastSentAt = DateTime.UtcNow;
                    if (frame.Data.Length > 0)
                        await ws.SendAsync(frame.Data.AsMemory(), WebSocketMessageType.Binary, true, ct);
                }
                else if ((DateTime.UtcNow - lastSentAt).TotalSeconds >= 5)
                {
                    lastSentAt = DateTime.UtcNow;
                    await SendText(ws, JsonSerializer.Serialize(new { type = "heartbeat" }), ct);
                }

                await Task.Delay(33, ct);
            }
            catch { break; }
        }
    }

    private static async Task ReceiveInputs(WebSocket ws, DxgiScreenCapture capture, CancellationToken ct)
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
                        InputInjector.MouseMove(
                            obj!["x"]!.GetValue<int>(),
                            obj!["y"]!.GetValue<int>(),
                            capture.ScreenLeft,
                            capture.ScreenTop);
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
                    case "select_screen":
                        capture.SetSelectedScreen(obj!["index"]!.GetValue<int>());
                        break;
                    case "send_sas":
                        InputInjector.SendSas();
                        break;
                    case "restart":
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                            "shutdown", "/r /t 5") { UseShellExecute = false, CreateNoWindow = true });
                        break;
                    case "run":
                        HandleRunCommand(obj);
                        break;
                }
            }
            catch { break; }
        }
    }

    // Spawns a process as a child of the host (which runs as admin per its
    // requireAdministrator manifest). The child inherits the host's primary
    // token, so it runs elevated with no UAC prompt. Strong password on the
    // host is therefore critical: anyone who authenticates gets admin shell.
    private static void HandleRunCommand(JsonNode? obj)
    {
        if (obj == null) return;
        var file = obj["file"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(file)) return;
        var args = obj["args"]?.GetValue<string>() ?? "";

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RDV-Dxgi", "run.log");
        try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)!); } catch { }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            var p = System.Diagnostics.Process.Start(psi);
            try { File.AppendAllText(logPath, $"{DateTime.Now:s}\tOK\tpid={p?.Id}\t{file}\t{args}\n"); } catch { }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(logPath, $"{DateTime.Now:s}\tFAIL\t{ex.GetType().Name}\t{file}\t{args}\t{ex.Message}\n"); } catch { }
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
