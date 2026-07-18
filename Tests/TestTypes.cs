namespace ObsidianDisk.Services;

/// <summary>
/// Cópia do record real (definido em AppStorage.cs, que não é linkado nos testes para
/// não arrastar dependências de I/O). Mantém a mesma forma para o DiskForecaster compilar.
/// </summary>
public sealed record ScanRecord(DateTime Timestamp, string Path, long TotalBytes, long FileCount);
