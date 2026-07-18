using System.IO;
using ObsidianDisk.Services;
using Xunit;

namespace ObsidianDisk.Tests;

public class FileDeletionTests : IDisposable
{
    private readonly string _base;

    public FileDeletionTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "obsidiandisk_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_base);
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    [Fact]
    public void Move_File_ToNewFolder_Succeeds()
    {
        var src = Path.Combine(_base, "file.txt");
        File.WriteAllText(src, "hi");
        var dest = Path.Combine(_base, "dest");

        Assert.True(FileDeletion.Move(src, dest));
        Assert.False(File.Exists(src));
        Assert.True(File.Exists(Path.Combine(dest, "file.txt")));
    }

    [Fact]
    public void Move_Directory_SameVolume_MovesWholeTree()
    {
        var srcDir = Path.Combine(_base, "folder");
        Directory.CreateDirectory(Path.Combine(srcDir, "sub"));
        File.WriteAllText(Path.Combine(srcDir, "sub", "a.txt"), "x");
        var dest = Path.Combine(_base, "dest");

        Assert.True(FileDeletion.Move(srcDir, dest));
        Assert.False(Directory.Exists(srcDir));
        Assert.True(File.Exists(Path.Combine(dest, "folder", "sub", "a.txt")));
    }

    [Fact]
    public void Move_DoesNotOverwriteExistingTarget()
    {
        var src = Path.Combine(_base, "file.txt");
        File.WriteAllText(src, "new");
        var dest = Path.Combine(_base, "dest");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "file.txt"), "existing");

        Assert.False(FileDeletion.Move(src, dest));
        Assert.True(File.Exists(src)); // origem preservada
        Assert.Equal("existing", File.ReadAllText(Path.Combine(dest, "file.txt")));
    }

    [Fact]
    public void Move_IntoOwnSubfolder_IsRejected()
    {
        var srcDir = Path.Combine(_base, "folder");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "a.txt"), "x");

        // Mover a pasta para dentro dela mesma se moveria em si — deve falhar
        Assert.False(FileDeletion.Move(srcDir, Path.Combine(srcDir, "inner")));
        Assert.True(Directory.Exists(srcDir));
    }
}
