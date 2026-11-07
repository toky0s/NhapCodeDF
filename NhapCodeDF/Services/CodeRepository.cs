using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NhapCodeDF.Data;
using NhapCodeDF.Models;

namespace NhapCodeDF.Services;

/// <summary>Mỗi thao tác dùng một DbContext ngắn hạn -> an toàn khi gọi từ background job.</summary>
public class CodeRepository
{
    public async Task EnsureCreatedAsync()
    {
        await using var db = new CodeDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task<List<CodeEntry>> GetAllAsync()
    {
        await using var db = new CodeDbContext();
        return await db.Codes.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
    }

    /// <summary>Thêm các code chưa tồn tại, trả về danh sách entity đã thêm.</summary>
    public async Task<List<CodeEntry>> AddRangeAsync(IReadOnlyCollection<string> codes)
    {
        await using var db = new CodeDbContext();

        var existing = await db.Codes
            .Where(c => codes.Contains(c.Code))
            .Select(c => c.Code)
            .ToListAsync();

        var toAdd = codes.Except(existing)
            .Select(c => new CodeEntry { Code = c })
            .ToList();

        db.Codes.AddRange(toAdd);
        await db.SaveChangesAsync();
        return toAdd;
    }

    // ---- Dành cho background job ở bước sau ----

    public async Task<List<CodeEntry>> GetPendingAsync(int take)
    {
        await using var db = new CodeDbContext();
        return await db.Codes.AsNoTracking()
            .Where(c => c.Status == CodeStatus.Pending)
            .OrderBy(c => c.Id).Take(take).ToListAsync();
    }

    public async Task UpdateResultAsync(string code, CodeStatus status, int? errorCode, string? message)
    {
        await using var db = new CodeDbContext();
        await db.Codes.Where(c => c.Code == code).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Status, status)
            .SetProperty(c => c.ErrorCode, errorCode)
            .SetProperty(c => c.ErrorMessage, message));
    }

    public async Task MarkRunningAsync(IReadOnlyCollection<string> codes)
    {
        await using var db = new CodeDbContext();
        await db.Codes.Where(c => codes.Contains(c.Code))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CodeStatus.Running));
    }

    /// <summary>Gọi lúc khởi động: code còn kẹt Running (app tắt giữa chừng) -> Pending.</summary>
    public async Task ResetRunningAsync()
    {
        await using var db = new CodeDbContext();
        await db.Codes.Where(c => c.Status == CodeStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CodeStatus.Pending));
    }
}
