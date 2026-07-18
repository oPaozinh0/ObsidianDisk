using System.IO;
using ObsidianDisk.Services;
using Xunit;

namespace ObsidianDisk.Tests;

public class QuarantineStoreTests : IDisposable
{
    private readonly string _base;

    public QuarantineStoreTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "obsidiandisk_qt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_base);
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    [Fact]
    public void Quarantine_Then_Restore_RoundTrips()
    {
        var src = Path.Combine(_base, "victim.txt");
        File.WriteAllText(src, "keep me");

        Assert.True(QuarantineStore.Quarantine(src, 7));
        Assert.False(File.Exists(src)); // saiu do lugar

        var item = QuarantineStore.List()
            .FirstOrDefault(i => string.Equals(i.OriginalPath, src, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(item);
        Assert.Equal("victim.txt", item!.Name);

        try
        {
            Assert.True(QuarantineStore.Restore(item.Id));
            Assert.True(File.Exists(src)); // voltou ao lugar
            Assert.Equal("keep me", File.ReadAllText(src));
        }
        finally
        {
            QuarantineStore.Purge(item.Id); // garante que nada fica na quarentena real
        }
    }

    [Fact]
    public void Quarantine_Then_Purge_RemovesItem()
    {
        var src = Path.Combine(_base, "trash.txt");
        File.WriteAllText(src, "bye");

        Assert.True(QuarantineStore.Quarantine(src, 3));
        var item = QuarantineStore.List()
            .First(i => string.Equals(i.OriginalPath, src, StringComparison.OrdinalIgnoreCase));

        Assert.True(QuarantineStore.Purge(item.Id));
        Assert.DoesNotContain(QuarantineStore.List(), i => i.Id == item.Id);
    }
}
