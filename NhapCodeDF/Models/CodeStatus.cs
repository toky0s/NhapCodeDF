namespace NhapCodeDF.Models;

public enum CodeStatus
{
    Pending = 0,   // Chưa nhập
    Running = 1,   // Đang nhập
    Success = 2,   // Thành công
    Failed  = 3    // Thất bại
}
