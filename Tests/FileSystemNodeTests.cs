using ObsidianDisk.Models;
using Xunit;

namespace ObsidianDisk.Tests;

public class FileSystemNodeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    public void FormatSize_Formats(long bytes, string expected) =>
        Assert.Equal(expected, FileSystemNode.FormatSize(bytes));

    [Fact]
    public void FormatSize_UsesFraction_ForNonRoundValues()
    {
        // O separador decimal depende da cultura ("1.5 KB" ou "1,5 KB"): testa o essencial.
        var text = FileSystemNode.FormatSize(1536);
        Assert.StartsWith("1", text);
        Assert.EndsWith(" KB", text);
        Assert.Contains("5", text);
    }

    [Fact]
    public void AddSizeUpwards_PropagatesToAncestors()
    {
        var root = new FileSystemNode { Name = "root", FullPath = @"C:\", IsDirectory = true };
        var mid = new FileSystemNode { Name = "mid", FullPath = @"C:\mid", IsDirectory = true, Parent = root };
        root.Children.Add(mid);
        var leaf = new FileSystemNode { Name = "f", FullPath = @"C:\mid\f", Parent = mid };
        mid.Children.Add(leaf);

        leaf.AddSizeUpwards(100);

        Assert.Equal(100, leaf.Size);
        Assert.Equal(100, mid.Size);
        Assert.Equal(100, root.Size);
    }

    [Fact]
    public void FindByPath_ReturnsDescendant_OrNull()
    {
        var root = NodeBuilder.Dir(@"C:\",
            NodeBuilder.Dir(@"C:\a",
                NodeBuilder.Dir(@"C:\a\b")));

        Assert.NotNull(root.FindByPath(@"C:\a\b"));
        Assert.Equal(@"C:\a\b", root.FindByPath(@"C:\a\b")!.FullPath);
        Assert.Null(root.FindByPath(@"C:\a\z"));
    }

    [Fact]
    public void SortBySizeDescending_OrdersChildrenRecursively()
    {
        var root = NodeBuilder.Dir(@"C:\",
            NodeBuilder.File("small.txt", 1),
            NodeBuilder.File("big.txt", 100),
            NodeBuilder.File("mid.txt", 10));

        root.SortBySizeDescending();

        Assert.Equal(new[] { "big.txt", "mid.txt", "small.txt" },
            root.Children.Select(c => c.Name).ToArray());
    }
}
