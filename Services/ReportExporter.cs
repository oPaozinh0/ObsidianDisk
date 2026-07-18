using System.Net;
using System.Text;
using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

/// <summary>
/// Gera um relatório HTML autossuficiente (CSS embutido, sem dependências externas) a partir
/// do histórico de um caminho e do último snapshot. Feito para abrir no navegador ou compartilhar.
/// </summary>
public static class ReportExporter
{
    private const int TopFoldersShown = 25;

    public static string BuildHtml(string path, IReadOnlyList<ScanRecord> records, TreeSnapshot? snapshot,
        FileSystemNode? root = null)
    {
        var ordered = records.OrderBy(r => r.Timestamp).ToList();
        var latest = ordered.Count > 0 ? ordered[^1] : null;

        long totalBytes = snapshot?.TotalBytes ?? latest?.TotalBytes ?? 0;
        long fileCount = snapshot?.FileCount ?? latest?.FileCount ?? 0;
        var scanTime = snapshot?.Timestamp ?? latest?.Timestamp;

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"").Append(L.T("Rep.Lang")).Append("\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append(E(L.T("Rep.Title"))).Append(" — ").Append(E(path)).Append("</title>");
        sb.Append("<style>").Append(Css()).Append("</style></head><body><div class=\"wrap\">");

        // Cabeçalho
        sb.Append("<header><div class=\"brand\">ObsidianDisk</div><h1>").Append(E(L.T("Rep.Title"))).Append("</h1>");
        sb.Append("<div class=\"path\">").Append(E(path)).Append("</div>");
        if (scanTime is { } t)
            sb.Append("<div class=\"gen\">").Append(E(L.F("Rep.ScannedAt", t.ToString("dd/MM/yyyy HH:mm")))).Append("</div>");
        sb.Append("</header>");

        // Cartões-resumo
        sb.Append("<section class=\"cards\">");
        Card(sb, L.T("Rep.TotalSize"), FileSystemNode.FormatSize(totalBytes));
        Card(sb, L.T("Rep.FileCount"), fileCount.ToString("N0"));
        Card(sb, L.T("Rep.ScanCount"), ordered.Count.ToString("N0"));
        if (ordered.Count >= 2)
        {
            long variation = ordered[^1].TotalBytes - ordered[0].TotalBytes;
            string v = (variation >= 0 ? "+" : "−") + FileSystemNode.FormatSize(Math.Abs(variation));
            Card(sb, L.T("Rep.Change"), v, warm: variation > 0);
        }
        sb.Append("</section>");

        // Maiores pastas (do snapshot)
        if (snapshot is { TopFolders.Count: > 0 })
        {
            var folders = snapshot.TopFolders.OrderByDescending(f => f.Size).Take(TopFoldersShown).ToList();
            long max = Math.Max(1, folders[0].Size);

            sb.Append("<section><h2>").Append(E(L.T("Rep.TopFolders"))).Append("</h2><table class=\"folders\">");
            sb.Append("<thead><tr><th class=\"num\">#</th><th>").Append(E(L.T("Rep.Folder")))
              .Append("</th><th class=\"size\">").Append(E(L.T("Rep.Size"))).Append("</th><th class=\"bar\"></th></tr></thead><tbody>");
            int rank = 1;
            foreach (var f in folders)
            {
                double pct = f.Size * 100.0 / max;
                sb.Append("<tr><td class=\"num\">").Append(rank++).Append("</td><td class=\"folder\">")
                  .Append(E(f.FullPath)).Append("</td><td class=\"size\">").Append(E(FileSystemNode.FormatSize(f.Size)))
                  .Append("</td><td class=\"bar\"><div class=\"track\"><div class=\"fill\" style=\"width:")
                  .Append(pct.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append("%\"></div></div></td></tr>");
            }
            sb.Append("</tbody></table></section>");
        }

        // Descobertas (só quando a árvore do scan está disponível)
        AppendDiscoveries(sb, root);

        // Histórico
        if (ordered.Count > 0)
        {
            sb.Append("<section><h2>").Append(E(L.T("Rep.History"))).Append("</h2><table class=\"history\">");
            sb.Append("<thead><tr><th>").Append(E(L.T("Rep.Date"))).Append("</th><th class=\"size\">")
              .Append(E(L.T("Rep.FileCount"))).Append("</th><th class=\"size\">").Append(E(L.T("Rep.Size")))
              .Append("</th><th class=\"size\">").Append(E(L.T("Rep.Delta"))).Append("</th></tr></thead><tbody>");
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                var r = ordered[i];
                string delta = "—";
                string cls = "muted";
                if (i > 0)
                {
                    long d = r.TotalBytes - ordered[i - 1].TotalBytes;
                    delta = (d >= 0 ? "+" : "−") + FileSystemNode.FormatSize(Math.Abs(d));
                    cls = d > 0 ? "grow" : d < 0 ? "shrink" : "muted";
                }
                sb.Append("<tr><td>").Append(E(r.Timestamp.ToString("dd/MM/yyyy HH:mm")))
                  .Append("</td><td class=\"size\">").Append(E(r.FileCount.ToString("N0")))
                  .Append("</td><td class=\"size\">").Append(E(FileSystemNode.FormatSize(r.TotalBytes)))
                  .Append("</td><td class=\"size ").Append(cls).Append("\">").Append(E(delta)).Append("</td></tr>");
            }
            sb.Append("</tbody></table></section>");
        }

        var version = typeof(ReportExporter).Assembly.GetName().Version?.ToString(3) ?? "";
        sb.Append("<footer>").Append(E(L.F("Rep.Footer", version))).Append("</footer>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    /// <summary>Resumo das análises de descobertas (categoria, quantidade, espaço recuperável).</summary>
    private static void AppendDiscoveries(StringBuilder sb, FileSystemNode? root)
    {
        if (root is null) return;

        var cutoff180 = DateTime.UtcNow.AddDays(-180);
        var cutoff365 = DateTime.UtcNow.AddDays(-365);
        var groups = new (string Label, List<DiscoveryItem> Items)[]
        {
            (L.T("Dc.ModeDevJunk"), DiscoveryAnalyzer.DevJunk(root)),
            (L.T("Dc.ModeInstallers"), DiscoveryAnalyzer.Installers(root)),
            (L.T("Dc.ModeGhost"), DiscoveryAnalyzer.GhostFolders(root, cutoff180, 50L << 20)),
            (L.T("Dc.ModeOldFiles"), DiscoveryAnalyzer.LargeFilesByAge(root, cutoff365, 100L << 20)),
            (L.T("Dc.ModeEmpty"), DiscoveryAnalyzer.EmptyFolders(root)),
        };

        var rows = groups
            .Select(g => (g.Label, Count: g.Items.Count, Bytes: g.Items.Sum(i => i.Size)))
            .Where(r => r.Count > 0)
            .OrderByDescending(r => r.Bytes)
            .ToList();
        if (rows.Count == 0) return;

        long max = Math.Max(1, rows[0].Bytes);
        sb.Append("<section><h2>").Append(E(L.T("Rep.Discoveries"))).Append("</h2><table class=\"folders\">");
        sb.Append("<thead><tr><th>").Append(E(L.T("Rep.Category"))).Append("</th><th class=\"size\">")
          .Append(E(L.T("Rep.Count"))).Append("</th><th class=\"size\">").Append(E(L.T("Rep.Size")))
          .Append("</th><th class=\"bar\"></th></tr></thead><tbody>");
        foreach (var r in rows)
        {
            double pct = r.Bytes * 100.0 / max;
            sb.Append("<tr><td class=\"folder\">").Append(E(r.Label)).Append("</td><td class=\"size\">")
              .Append(r.Count.ToString("N0")).Append("</td><td class=\"size\">").Append(E(FileSystemNode.FormatSize(r.Bytes)))
              .Append("</td><td class=\"bar\"><div class=\"track\"><div class=\"fill\" style=\"width:")
              .Append(pct.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append("%\"></div></div></td></tr>");
        }
        sb.Append("</tbody></table></section>");
    }

    private static void Card(StringBuilder sb, string label, string value, bool warm = false)
    {
        sb.Append("<div class=\"card\"><div class=\"label\">").Append(E(label))
          .Append("</div><div class=\"value").Append(warm ? " warm" : "").Append("\">").Append(E(value)).Append("</div></div>");
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string Css() => """
        *{box-sizing:border-box;margin:0;padding:0}
        body{background:#0e1117;color:#c9d1e0;font:14px/1.5 'Segoe UI',system-ui,sans-serif;padding:32px 16px}
        .wrap{max-width:960px;margin:0 auto}
        header{border-bottom:1px solid #262c3a;padding-bottom:20px;margin-bottom:24px}
        .brand{color:#a88bff;font-weight:700;letter-spacing:.5px;font-size:13px}
        h1{font-size:26px;margin:6px 0;color:#f0f2f7}
        h2{font-size:16px;margin:28px 0 12px;color:#f0f2f7}
        .path{color:#8b93a7;word-break:break-all}
        .gen{color:#6b7280;font-size:12px;margin-top:4px}
        .cards{display:flex;flex-wrap:wrap;gap:12px}
        .card{flex:1 1 160px;background:#161b26;border:1px solid #262c3a;border-radius:12px;padding:14px 16px}
        .card .label{color:#8b93a7;font-size:12px}
        .card .value{font-size:22px;font-weight:700;color:#f0f2f7;margin-top:4px}
        .card .value.warm{color:#e08c5a}
        table{width:100%;border-collapse:collapse;font-size:13px}
        th,td{text-align:left;padding:8px 10px;border-bottom:1px solid #1e2430}
        th{color:#8b93a7;font-weight:600;font-size:12px}
        td.num,.num{color:#6b7280;width:32px;text-align:right}
        td.size,th.size{text-align:right;white-space:nowrap}
        td.folder{color:#c9d1e0;word-break:break-all}
        .grow{color:#e06c75}.shrink{color:#5fd38a}.muted{color:#6b7280}
        th.bar,td.bar{width:180px}
        .track{background:#1e2430;border-radius:4px;height:8px;overflow:hidden}
        .fill{background:#7c5cff;height:100%;border-radius:4px}
        footer{margin-top:32px;padding-top:16px;border-top:1px solid #262c3a;color:#6b7280;font-size:12px;text-align:center}
        """;
}
