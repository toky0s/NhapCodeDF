using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NhapCodeDF.Models;

namespace NhapCodeDF.Services;

/// <summary>
/// WebSocket server chạy local. Extension (content script) là client và tự kết nối vào.
/// Chỉ giữ MỘT client đang hoạt động (kết nối mới sẽ thay kết nối cũ).
/// </summary>
public sealed class ExtensionServer : IDisposable
{
    public const string AllowedOrigin = "https://redeem.df.garena.sg";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpListener _listener = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private WebSocket? _client;

    public bool IsConnected => _client?.State == WebSocketState.Open;

    /// <summary>Gọi trên thread nền. Handler nên xử lý nhanh (đẩy vào queue).</summary>
    public event Action<ExtensionMessage>? MessageReceived;
    public event Action? ConnectionChanged;

    public void Start(int port = 8765)
    {
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }   // listener đã bị đóng
            _ = Task.Run(() => HandleAsync(ctx, ct));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        // 1) Chỉ nhận WebSocket từ đúng trang đổi quà (chặn website khác dò cổng localhost)
        if (!ctx.Request.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = 400; ctx.Response.Close(); return;
        }
        var origin = ctx.Request.Headers["Origin"];
        if (!string.Equals(origin, AllowedOrigin, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = 403; ctx.Response.Close(); return;
        }

        WebSocket ws;
        try { ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket; }
        catch { ctx.Response.Close(); return; }

        // 2) Kết nối mới thay kết nối cũ
        var old = Interlocked.Exchange(ref _client, ws);
        if (old != null)
        {
            try { await old.CloseAsync(WebSocketCloseStatus.PolicyViolation, "replaced", CancellationToken.None); } catch { }
        }
        ConnectionChanged?.Invoke();

        try { await ReceiveLoopAsync(ws, ct); }
        catch { /* client đóng đột ngột */ }
        finally
        {
            // Chỉ báo disconnected nếu đây là client đang active
            if (Interlocked.CompareExchange(ref _client, null, ws) == ws)
            {
                ConnectionChanged?.Invoke();
                MessageReceived?.Invoke(new ExtensionMessage { Type = "disconnected" });
            }
            ws.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(WebSocket ws, CancellationToken ct)
    {
        var buf = new byte[8192];
        using var ms = new MemoryStream();

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            ms.SetLength(0);
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(buf, ct);
                if (r.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buf, 0, r.Count);
            } while (!r.EndOfMessage);

            ExtensionMessage? msg = null;
            try { msg = JsonSerializer.Deserialize<ExtensionMessage>(ms.ToArray(), JsonOpts); } catch { }
            if (msg != null) MessageReceived?.Invoke(msg);
        }
    }

    // ---------- Gửi lệnh xuống extension ----------

    public async Task SendAsync(object payload, CancellationToken ct = default)
    {
        var ws = _client;
        if (ws is not { State: WebSocketState.Open })
            throw new InvalidOperationException("Extension chưa kết nối");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        await _sendLock.WaitAsync(ct);
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
        finally { _sendLock.Release(); }
    }

    public Task SendRedeemAsync(string jobId, System.Collections.Generic.IReadOnlyList<string> codes, CancellationToken ct = default)
        => SendAsync(new { type = "redeem", jobId, codes }, ct);

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener.Close(); } catch { }
        _client?.Dispose();
    }
}
