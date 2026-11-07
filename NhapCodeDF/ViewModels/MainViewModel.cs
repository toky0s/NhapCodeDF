using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using NhapCodeDF.Models;
using NhapCodeDF.Services;

namespace NhapCodeDF.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CodeRepository _repo = new();
    private readonly ExtensionServer _server = new();
    private readonly RedeemJobService _job;
    private readonly DispatcherQueue _dispatcher;

    public ObservableCollection<CodeEntry> Codes { get; } = new();

    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExtensionStatusText))]
    private bool _isExtensionConnected;

    [ObservableProperty] private bool _isAutoRunEnabled = true;

    public string ExtensionStatusText => IsExtensionConnected ? "Extension: đã kết nối" : "Extension: chưa kết nối";

    public MainViewModel()
    {
        // Được tạo trên UI thread (field initializer của MainWindow)
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _job = new RedeemJobService(_server, _repo);

        _server.ConnectionChanged += () =>
            _dispatcher.TryEnqueue(() => IsExtensionConnected = _server.IsConnected);

        _job.CodeUpdated += u => _dispatcher.TryEnqueue(() => ApplyUpdate(u));
    }

    partial void OnIsAutoRunEnabledChanged(bool value) => _job.Enabled = value;

    public async Task InitializeAsync()
    {
        await _repo.EnsureCreatedAsync();
        await _repo.ResetRunningAsync();

        Codes.Clear();
        foreach (var c in await _repo.GetAllAsync())
            Codes.Add(c);
        StatusMessage = $"Đã tải {Codes.Count} code.";

        try
        {
            _server.Start(8765);
            _job.Start();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Không mở được WebSocket server (cổng 8765 đang bị dùng?): {ex.Message}";
        }
    }

    public void Shutdown()
    {
        _job.Stop();
        _server.Dispose();
    }

    private void ApplyUpdate(CodeUpdate u)
    {
        var item = Codes.FirstOrDefault(c => c.Code == u.Code);
        if (item == null) return;
        item.Status = u.Status;
        item.ErrorCode = u.ErrorCode;
        item.ErrorMessage = u.Message;
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var codes = ParseCodes(InputText);
        if (codes.Count == 0)
        {
            StatusMessage = "Chưa có code nào để nhập.";
            return;
        }

        var added = await _repo.AddRangeAsync(codes);
        foreach (var e in added) Codes.Add(e);

        InputText = "";
        StatusMessage = $"Đã thêm {added.Count} code, bỏ qua {codes.Count - added.Count} code trùng.";
    }

    public static List<string> ParseCodes(string text) =>
        (text ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
