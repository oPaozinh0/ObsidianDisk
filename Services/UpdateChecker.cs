using System.Net.Http;
using System.Text.Json;

namespace ObsidianDisk.Services;

public sealed record UpdateInfo(string Tag, string Url);

/// <summary>Consulta a release mais recente no GitHub e compara com a versão atual.</summary>
public static class UpdateChecker
{
    private const string ApiUrl = "https://api.github.com/repos/oPaozinh0/ObsidianDisk/releases/latest";

    public static async Task<UpdateInfo?> CheckAsync(Version current)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ObsidianDisk-UpdateCheck");

            var json = await http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);

            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            string url = doc.RootElement.GetProperty("html_url").GetString() ?? "";

            if (Version.TryParse(tag.TrimStart('v', 'V'), out var latest) && latest > current)
                return new UpdateInfo(tag, url);
        }
        catch
        {
            // offline / rate limit / etc — silêncio, tenta de novo na próxima execução
        }
        return null;
    }
}
