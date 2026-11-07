namespace NhapCodeDF.Models;

/// <summary>Message extension gửi lên: hello | result | done | error (và "disconnected" do server tự tạo).</summary>
public class ExtensionMessage
{
    public string Type { get; set; } = "";
    public string? JobId { get; set; }
    public string? Code { get; set; }
    public string? Status { get; set; }      // "success" | "failure"
    public int? Ret { get; set; }            // mã lỗi (parse từ error_hint_xxx)
    public string? Message { get; set; }
    public string? Source { get; set; }      // diaTips | superTips | dom | none
    public bool? Retryable { get; set; }     // true: lỗi môi trường (chưa đăng nhập, mất DOM...), không phải lỗi của code
}

public record CodeUpdate(string Code, CodeStatus Status, int? ErrorCode, string? Message);
