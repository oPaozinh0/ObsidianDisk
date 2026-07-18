using ObsidianDisk.Services;
using Xunit;
using static ObsidianDisk.Tests.NodeBuilder;

namespace ObsidianDisk.Tests;

public class DiscoveryAnalyzerTests
{
    [Fact]
    public void Installers_InDownloads_AnyExeCounts_ElsewhereOnlyUnambiguous()
    {
        var downloads = Dir(@"C:\Users\Me\Downloads",
            File("setup_app.exe", 10),
            File("game.exe", 40),
            File("photo.jpg", 5),   // não é instalador
            File("driver.msi", 20));

        var programFiles = Dir(@"C:\Program Files\App",
            File("app.exe", 100),       // .exe fora de Downloads, sem "setup"/"installer" → ignora
            File("unins000.exe", 3),    // desinstalador comum → ignora
            File("installer.exe", 7),   // nome indica instalador → conta
            File("thing.msi", 8));      // .msi sempre conta

        var root = Dir(@"C:\", downloads, programFiles);

        var names = DiscoveryAnalyzer.Installers(root).Select(i => i.Name).ToHashSet();

        Assert.Equal(
            new HashSet<string> { "setup_app.exe", "game.exe", "driver.msi", "installer.exe", "thing.msi" },
            names);
    }

    [Fact]
    public void Installers_OrdersBySizeDescending()
    {
        var root = Dir(@"C:\Users\Me\Downloads",
            File("a.exe", 10),
            File("b.exe", 90),
            File("c.exe", 50));

        var items = DiscoveryAnalyzer.Installers(root);

        Assert.Equal(new[] { "b.exe", "c.exe", "a.exe" }, items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void EmptyFolders_FindsOnlyZeroSizedDirs()
    {
        var root = Dir(@"C:\",
            Dir(@"C:\empty"),                       // sem arquivos → vazia
            Dir(@"C:\full", File("a.txt", 10)));    // tem arquivo → não vazia

        var items = DiscoveryAnalyzer.EmptyFolders(root);

        Assert.Single(items);
        Assert.Equal("empty", items[0].Name);
    }

    [Fact]
    public void DevJunk_FindsBuildArtifactDirs_WithoutDescending()
    {
        var root = Dir(@"C:\proj",
            Dir(@"C:\proj\node_modules",
                Dir(@"C:\proj\node_modules\node_modules", File("x.js", 5))), // aninhado não vira item separado
            Dir(@"C:\proj\src", File("main.cs", 20)));

        var items = DiscoveryAnalyzer.DevJunk(root);

        Assert.Single(items);
        Assert.Equal("node_modules", items[0].Name);
    }
}
