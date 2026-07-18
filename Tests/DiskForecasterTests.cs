using ObsidianDisk.Services;
using Xunit;

namespace ObsidianDisk.Tests;

public class DiskForecasterTests
{
    private const long MB = 1L << 20;
    private const long GB = 1L << 30;

    private static ScanRecord Rec(int day, long bytes) =>
        new(new DateTime(2026, 1, 1).AddDays(day), @"C:\", bytes, 0);

    [Fact]
    public void SlopeBytesPerDay_TwoPoints_IsExactRate()
    {
        var history = new List<ScanRecord> { Rec(0, 1000 * MB), Rec(10, 2000 * MB) };
        // +1000 MB em 10 dias = 100 MB/dia
        Assert.Equal(100.0 * MB, DiskForecaster.SlopeBytesPerDay(history), 3);
    }

    [Fact]
    public void SlopeBytesPerDay_SinglePoint_IsZero() =>
        Assert.Equal(0, DiskForecaster.SlopeBytesPerDay(new List<ScanRecord> { Rec(0, 100 * MB) }));

    [Fact]
    public void Project_GrowingHistory_ForecastsExhaustion()
    {
        var history = new List<ScanRecord> { Rec(0, 1000 * MB), Rec(10, 2000 * MB) }; // 100 MB/dia
        var now = new DateTime(2026, 1, 11);

        var forecast = DiskForecaster.Project(history, 500 * MB, now); // 500 MB livres

        Assert.NotNull(forecast);
        Assert.Equal(5.0, forecast!.DaysUntilFull, 1); // 500 / 100 = 5 dias
        Assert.Equal(now.AddDays(5), forecast.FullDate);
    }

    [Fact]
    public void Project_FlatHistory_ReturnsNull()
    {
        var history = new List<ScanRecord> { Rec(0, 1000 * MB), Rec(10, 1000 * MB) };
        Assert.Null(DiskForecaster.Project(history, 10 * GB, new DateTime(2026, 1, 11)));
    }

    [Fact]
    public void Project_HorizonBeyondTwoYears_ReturnsNull()
    {
        // ~2 MB/dia (acima do ruído), mas com 10 GB livres o esgotamento passa de 2 anos
        var history = new List<ScanRecord> { Rec(0, 1000 * MB), Rec(1, 1002 * MB) };
        Assert.Null(DiskForecaster.Project(history, 10 * GB, new DateTime(2026, 1, 2)));
    }

    [Fact]
    public void Project_NegligibleGrowth_ReturnsNull()
    {
        // Menos de 1 MB/dia é tratado como ruído
        var history = new List<ScanRecord> { Rec(0, 1000 * MB), Rec(10, 1000 * MB + 512 * 1024) };
        Assert.Null(DiskForecaster.Project(history, 1 * GB, new DateTime(2026, 1, 11)));
    }
}
