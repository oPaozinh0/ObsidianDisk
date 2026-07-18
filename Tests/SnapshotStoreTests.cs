using ObsidianDisk.Services;
using Xunit;

namespace ObsidianDisk.Tests;

public class SnapshotStoreTests
{
    private static TreeSnapshot Snap(params SnapshotEntry[] folders) =>
        new(new DateTime(2026, 1, 1), @"C:\", 0, 0, folders);

    [Fact]
    public void Diff_ReportsGrowthShrinkAndNewFolders_SortedByMagnitude()
    {
        var older = Snap(
            new SnapshotEntry(@"C:\A", 100),
            new SnapshotEntry(@"C:\B", 50));
        var newer = Snap(
            new SnapshotEntry(@"C:\A", 300),   // +200
            new SnapshotEntry(@"C:\C", 20));   // nova: +20 (B some: -50)

        var diff = SnapshotStore.Diff(older, newer);

        Assert.Equal(3, diff.Count);
        Assert.Equal(@"C:\A", diff[0].FullPath);   // maior variação em módulo
        Assert.Equal(200, diff[0].Delta);
        Assert.Equal(-50, diff.Single(d => d.FullPath == @"C:\B").Delta);
        Assert.Equal(20, diff.Single(d => d.FullPath == @"C:\C").Delta);
    }

    [Fact]
    public void Diff_IgnoresUnchangedFolders()
    {
        var older = Snap(new SnapshotEntry(@"C:\A", 100));
        var newer = Snap(new SnapshotEntry(@"C:\A", 100));

        Assert.Empty(SnapshotStore.Diff(older, newer));
    }
}
