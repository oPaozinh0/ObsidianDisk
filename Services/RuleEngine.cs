using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

/// <summary>Arquivos que uma regra encontrou num scan.</summary>
public sealed record RuleMatch(CleanupRule Rule, List<FileSystemNode> Files, long TotalBytes);

/// <summary>
/// Avalia as regras automáticas contra a árvore escaneada. Só encontra e reporta — a ação
/// (avisar / enviar à Lixeira) é sempre disparada pelo usuário, nunca em silêncio.
/// </summary>
public static class RuleEngine
{
    public static List<RuleMatch> Evaluate(IEnumerable<CleanupRule> rules, FileSystemNode root)
    {
        var now = DateTime.UtcNow;
        var result = new List<RuleMatch>();

        foreach (var rule in rules)
        {
            if (!rule.Enabled) continue;

            var scope = string.IsNullOrEmpty(rule.Folder) ? root : root.FindByPath(rule.Folder);
            if (scope is null) continue; // pasta do escopo não está na árvore atual

            DateTime? accessCutoff = rule.MinAgeDays > 0 ? now.AddDays(-rule.MinAgeDays) : null;

            var files = new List<FileSystemNode>();
            long total = 0;

            void Walk(FileSystemNode dir)
            {
                foreach (var c in dir.Children)
                {
                    if (c.IsDirectory) { Walk(c); continue; }
                    if (rule.MinSizeBytes > 0 && c.Size < rule.MinSizeBytes) continue;
                    if (accessCutoff is { } cut && (c.LastAccessUtc == default || c.LastAccessUtc >= cut)) continue;
                    files.Add(c);
                    total += c.Size;
                }
            }

            Walk(scope);
            if (files.Count > 0)
                result.Add(new RuleMatch(rule, files, total));
        }

        return result;
    }
}
