using Microsoft.EntityFrameworkCore;

using NhapCodeDF.Models;

using System;
using System.Diagnostics;
using System.IO;

namespace NhapCodeDF.Data;

public class CodeDbContext : DbContext
{
    public DbSet<CodeEntry> Codes => Set<CodeEntry>();

    public static string DbPath { get; } = BuildDbPath();

    private static string BuildDbPath()
    {
        // LocalApplicationData chạy được cho cả packaged (MSIX) và unpackaged
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NhapCodeDF");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "codes.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CodeEntry>(e =>
        {
            e.ToTable("Codes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.Code).IsUnique();   // không cho trùng code
            e.Property(x => x.Status).HasConversion<int>();
        });
    }
}
