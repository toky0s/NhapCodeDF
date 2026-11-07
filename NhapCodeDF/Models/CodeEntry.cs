using System;
using System.ComponentModel.DataAnnotations.Schema;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NhapCodeDF.Models;

/// <summary>
/// Vừa là entity của EF Core, vừa là item hiển thị trên DataGrid.
/// Status/ErrorCode là observable để background job cập nhật thì bảng tự refresh.
/// </summary>
public partial class CodeEntry : ObservableObject
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private CodeStatus _status = CodeStatus.Pending;

    [ObservableProperty]
    private int? _errorCode;

    [ObservableProperty]
    private string? _errorMessage;   // chưa hiển thị trên bảng, lưu để debug

    [NotMapped]
    public string CreatedAtText => CreatedAt.ToString("dd/MM/yyyy HH:mm:ss");

    [NotMapped]
    public string StatusText => Status switch
    {
        CodeStatus.Pending => "Chưa nhập",
        CodeStatus.Running => "Đang nhập",
        CodeStatus.Success => "Thành công",
        CodeStatus.Failed  => "Thất bại",
        _ => ""
    };
}
