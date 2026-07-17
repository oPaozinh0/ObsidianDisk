namespace ObsidianDisk.Services;

/// <summary>Projeção de esgotamento: ritmo de crescimento e quando o espaço livre acaba.</summary>
public sealed record DiskForecast(double SlopeBytesPerDay, double DaysUntilFull, DateTime FullDate);

/// <summary>
/// Regressão linear (bytes/dia) sobre o histórico de scans de um caminho e projeção de
/// quando o espaço livre se esgota. Fonte única usada pela página History e pelo alerta
/// de disco cheio.
/// </summary>
public static class DiskForecaster
{
    private const double MinRelevantSlope = 1024 * 1024; // <1 MB/dia: ruído, não projeta
    private const double MaxHorizonDays = 365 * 2;       // além de ~2 anos: irrelevante

    /// <summary>Ritmo de crescimento em bytes/dia (0 se dados insuficientes).</summary>
    public static double SlopeBytesPerDay(IReadOnlyList<ScanRecord> history)
    {
        if (history.Count < 2) return 0;

        double x0 = history[0].Timestamp.Ticks;
        var points = history.Select(r => (
            X: (r.Timestamp.Ticks - x0) / (double)TimeSpan.TicksPerDay,
            Y: (double)r.TotalBytes)).ToList();

        double meanX = points.Average(p => p.X), meanY = points.Average(p => p.Y);
        double denom = points.Sum(p => (p.X - meanX) * (p.X - meanX));
        return denom > 0.0001 ? points.Sum(p => (p.X - meanX) * (p.Y - meanY)) / denom : 0;
    }

    /// <summary>
    /// Projeção de esgotamento a partir do histórico e do espaço livre atual. Retorna null
    /// se o crescimento for irrelevante (&lt;1 MB/dia) ou o horizonte passar de ~2 anos.
    /// </summary>
    public static DiskForecast? Project(IReadOnlyList<ScanRecord> history, long freeBytes, DateTime now)
    {
        double slope = SlopeBytesPerDay(history);
        if (slope <= MinRelevantSlope) return null;

        double daysLeft = freeBytes / slope;
        if (daysLeft >= MaxHorizonDays) return null;

        return new DiskForecast(slope, daysLeft, now.AddDays(daysLeft));
    }
}
