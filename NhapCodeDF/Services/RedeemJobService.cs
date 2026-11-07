using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NhapCodeDF.Models;

namespace NhapCodeDF.Services;

/// <summary>
/// Vòng lặp nền: lấy code Pending -> đánh dấu Running -> gửi cho extension ->
/// nhận từng result -> ghi DB + báo UI -> nhận "done" thì lấy lô tiếp theo.
/// </summary>
public sealed class RedeemJobService
{
    private readonly ExtensionServer _server;
    private readonly CodeRepository _repo;
    private readonly Channel<ExtensionMessage> _inbox = Channel.CreateUnbounded<ExtensionMessage>();
    private CancellationTokenSource? _cts;

    public int BatchSize { get; set; } = 20;
    public int SecondsPerCode { get; set; } = 25;     // ngân sách thời gian tối đa cho 1 code
    public int BackoffSeconds { get; set; } = 10;     // nghỉ khi gặp lỗi môi trường
    public volatile bool Enabled = true;

    /// <summary>Gọi trên thread nền -> UI phải tự marshal về UI thread.</summary>
    public event Action<CodeUpdate>? CodeUpdated;

    public RedeemJobService(ExtensionServer server, CodeRepository repo)
    {
        _server = server;
        _repo = repo;
        _server.MessageReceived += m => _inbox.Writer.TryWrite(m);   // chỉ enqueue, xử lý tuần tự trong vòng lặp
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!Enabled || !_server.IsConnected)
                {
                    await Task.Delay(1000, ct);
                    continue;
                }

                var batch = await _repo.GetPendingAsync(BatchSize);
                if (batch.Count == 0)
                {
                    await Task.Delay(2000, ct);
                    continue;
                }

                var needBackoff = await RunBatchAsync(batch.Select(b => b.Code).ToList(), ct);
                if (needBackoff) await Task.Delay(TimeSpan.FromSeconds(BackoffSeconds), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RedeemJob] {ex}");
                try { await Task.Delay(3000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <returns>true nếu cần nghỉ (lỗi môi trường / mất kết nối).</returns>
    private async Task<bool> RunBatchAsync(List<string> codes, CancellationToken ct)
    {
        // bỏ message cũ còn sót trong hàng đợi
        while (_inbox.Reader.TryRead(out _)) { }

        var jobId = Guid.NewGuid().ToString("N");
        var waiting = new HashSet<string>(codes);   // code chưa nhận được kết quả
        var backoff = false;

        await _repo.MarkRunningAsync(codes);
        foreach (var c in codes) Raise(c, CodeStatus.Running, null, null);

        try
        {
            await _server.SendRedeemAsync(jobId, codes, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(codes.Count * SecondsPerCode + 30));

            while (true)
            {
                var msg = await _inbox.Reader.ReadAsync(timeoutCts.Token);

                if (msg.Type == "disconnected") { backoff = true; break; }
                if (msg.JobId != jobId) continue;          // message của job khác / cũ
                if (msg.Type == "done") break;
                if (msg.Type == "error") { backoff = true; break; }   // extension báo đang bận job khác
                if (msg.Type != "result" || msg.Code is null || !waiting.Remove(msg.Code)) continue;

                if (await HandleResultAsync(msg)) backoff = true;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            backoff = true;   // timeout cả lô
        }
        finally
        {
            // Code nào chưa có kết quả (mất kết nối, timeout, tắt app) -> trả về Pending để lần sau gửi lại
            foreach (var c in waiting)
            {
                await _repo.UpdateResultAsync(c, CodeStatus.Pending, null, null);
                Raise(c, CodeStatus.Pending, null, null);
            }
        }
        return backoff;
    }

    /// <returns>true nếu đây là lỗi môi trường (nên nghỉ).</returns>
    private async Task<bool> HandleResultAsync(ExtensionMessage msg)
    {
        var code = msg.Code!;

        if (msg.Status == "success")
        {
            await _repo.UpdateResultAsync(code, CodeStatus.Success, null, msg.Message);
            Raise(code, CodeStatus.Success, null, msg.Message);
            return false;
        }

        if (msg.Retryable == true)
        {
            // Chưa đăng nhập / mất DOM: không phải lỗi của code -> giữ Pending, thử lại sau
            await _repo.UpdateResultAsync(code, CodeStatus.Pending, null, msg.Message);
            Raise(code, CodeStatus.Pending, null, msg.Message);
            return true;
        }

        await _repo.UpdateResultAsync(code, CodeStatus.Failed, msg.Ret, msg.Message);
        Raise(code, CodeStatus.Failed, msg.Ret, msg.Message);
        return false;
    }

    private void Raise(string code, CodeStatus status, int? err, string? message)
        => CodeUpdated?.Invoke(new CodeUpdate(code, status, err, message));
}
