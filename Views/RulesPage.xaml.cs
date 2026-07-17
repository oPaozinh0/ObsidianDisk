using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

public partial class RulesPage : UserControl
{
    private static readonly (string Label, long Bytes)[] Sizes =
    {
        ("", 0), ("≥ 10 MB", 10L << 20), ("≥ 100 MB", 100L << 20),
        ("≥ 500 MB", 500L << 20), ("≥ 1 GB", 1L << 30),
    };

    private static readonly (string LabelKey, int Days)[] Ages =
    {
        ("Ru.Any", 0), ("Ru.Days30", 30), ("Ru.Days90", 90),
        ("Ru.Days180", 180), ("Ru.Year1", 365), ("Ru.Year2", 730),
    };

    private List<CleanupRule> _rules = new();
    private readonly Dictionary<CleanupRule, RuleMatch> _matches = new();
    private FileSystemNode? _root;
    private string _folder = "";

    /// <summary>Pedido de exclusão: arquivos + se é permanente (regras só usam Lixeira).</summary>
    public event Action<IReadOnlyList<FileSystemNode>, bool>? DeleteRequested;

    /// <summary>Disparado ao fim de um scan quando regras encontram itens (nº de regras, bytes totais).</summary>
    public event Action<int, long>? MatchesFound;

    public RulesPage()
    {
        InitializeComponent();

        SizeCombo.Items.Add(new ComboBoxItem { Content = L.T("Ru.Any"), Tag = 0L });
        for (int i = 1; i < Sizes.Length; i++)
            SizeCombo.Items.Add(new ComboBoxItem { Content = Sizes[i].Label, Tag = Sizes[i].Bytes });
        SizeCombo.SelectedIndex = 2; // 100 MB

        foreach (var (key, days) in Ages)
            AgeCombo.Items.Add(new ComboBoxItem { Content = L.T(key), Tag = days });
        AgeCombo.SelectedIndex = 4; // 1 ano

        ActionCombo.Items.Add(new ComboBoxItem { Content = L.T("Ru.ActNotify"), Tag = RuleAction.Notify });
        ActionCombo.Items.Add(new ComboBoxItem { Content = L.T("Ru.ActRecycle"), Tag = RuleAction.Recycle });
        ActionCombo.SelectedIndex = 0;

        _rules = AppStorage.LoadRules();
        BuildList();
    }

    // ---------------- Nova regra ----------------

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = L.T("Ru.Folder") };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            _folder = dialog.FolderName;
            FolderButton.Content = System.IO.Path.GetFileName(_folder.TrimEnd('\\')) is { Length: > 0 } n ? n : _folder;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        long size = (long)(((ComboBoxItem)SizeCombo.SelectedItem).Tag ?? 0L);
        int age = (int)(((ComboBoxItem)AgeCombo.SelectedItem).Tag ?? 0);
        var action = (RuleAction)(((ComboBoxItem)ActionCombo.SelectedItem).Tag ?? RuleAction.Notify);

        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? DefaultName(size, age) : NameBox.Text.Trim();

        _rules.Add(new CleanupRule
        {
            Name = name, Enabled = true, Folder = _folder,
            MinSizeBytes = size, MinAgeDays = age, Action = action,
        });
        AppStorage.SaveRules(_rules);

        // reseta o formulário
        NameBox.Text = "";
        _folder = "";
        FolderButton.Content = L.T("Ru.AnyFolder");

        if (_root is not null) Evaluate();
        BuildList();
    }

    private static string DefaultName(long size, int age)
    {
        if (size > 0 && age > 0) return L.F("Ru.NameBoth", FileSystemNode.FormatSize(size), age);
        if (size > 0) return L.F("Ru.NameSize", FileSystemNode.FormatSize(size));
        if (age > 0) return L.F("Ru.NameAge", age);
        return L.T("Ru.NameGeneric");
    }

    // ---------------- Avaliação ----------------

    public void UpdateFromScan(FileSystemNode? root)
    {
        _root = root;
        Evaluate();
        BuildList();

        var withMatches = _matches.Keys.Count(r => r.Enabled);
        if (withMatches > 0)
            MatchesFound?.Invoke(withMatches, _matches.Where(m => m.Key.Enabled).Sum(m => m.Value.TotalBytes));
    }

    private void Evaluate()
    {
        _matches.Clear();
        if (_root is null) return;
        foreach (var match in RuleEngine.Evaluate(_rules, _root))
            _matches[match.Rule] = match;
    }

    // ---------------- Lista ----------------

    private void BuildList()
    {
        RulesPanel.Children.Clear();
        EmptyHint.Visibility = _rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var rule in _rules)
            RulesPanel.Children.Add(BuildRuleRow(rule));
    }

    private UIElement BuildRuleRow(CleanupRule rule)
    {
        var enabled = new CheckBox
        {
            Style = (Style)FindResource("DarkCheck"),
            IsChecked = rule.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        enabled.Checked += (_, _) => ToggleRule(rule, true);
        enabled.Unchecked += (_, _) => ToggleRule(rule, false);
        DockPanel.SetDock(enabled, Dock.Left);

        // Botões (declarados primeiro no DockPanel, à direita, para reservarem a largura)
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(buttons, Dock.Right);

        bool hasMatches = _matches.TryGetValue(rule, out var match);
        if (rule.Action == RuleAction.Recycle && hasMatches)
        {
            var apply = new Controls.ObsidianButton
            {
                Variant = Controls.ObsidianButtonVariant.Ghost,
                Height = 30, Margin = new Thickness(0, 0, 8, 0),
                Content = L.T("Ru.Apply"),
            };
            apply.Click += (_, _) => DeleteRequested?.Invoke(match!.Files, false);
            buttons.Children.Add(apply);
        }
        var delete = new Controls.ObsidianButton
        {
            Variant = Controls.ObsidianButtonVariant.Ghost, Height = 30,
            Content = L.T("Ru.Delete"),
        };
        delete.Click += (_, _) => DeleteRule(rule);
        buttons.Children.Add(delete);

        var texts = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = rule.Name, Foreground = (Brush)FindResource("Text"),
            FontSize = 13.5, FontWeight = FontWeights.SemiBold,
        });
        texts.Children.Add(new TextBlock
        {
            Text = SummaryOf(rule), Foreground = (Brush)FindResource("Muted"),
            FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        });
        if (hasMatches)
            texts.Children.Add(new TextBlock
            {
                Text = L.F("Ru.Matches", match!.Files.Count, FileSystemNode.FormatSize(match.TotalBytes)),
                Foreground = (Brush)FindResource("Accent"),
                FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0),
            });

        var dock = new DockPanel { LastChildFill = true };
        dock.Children.Add(enabled);
        dock.Children.Add(buttons);
        dock.Children.Add(texts);

        return new Border
        {
            Background = (Brush)FindResource("Panel2"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(4, 3, 4, 3),
            Opacity = rule.Enabled ? 1.0 : 0.55,
            Child = dock,
        };
    }

    private string SummaryOf(CleanupRule rule)
    {
        var parts = new List<string>
        {
            string.IsNullOrEmpty(rule.Folder)
                ? L.T("Ru.AnyFolder")
                : (System.IO.Path.GetFileName(rule.Folder.TrimEnd('\\')) is { Length: > 0 } n ? n : rule.Folder),
        };
        if (rule.MinSizeBytes > 0) parts.Add("≥ " + FileSystemNode.FormatSize(rule.MinSizeBytes));
        if (rule.MinAgeDays > 0) parts.Add(L.F("Ru.AgePart", rule.MinAgeDays));
        parts.Add(L.T(rule.Action == RuleAction.Recycle ? "Ru.ActRecycle" : "Ru.ActNotify"));
        return string.Join("  ·  ", parts);
    }

    private void ToggleRule(CleanupRule rule, bool enabled)
    {
        rule.Enabled = enabled;
        AppStorage.SaveRules(_rules);
        BuildList();
    }

    private void DeleteRule(CleanupRule rule)
    {
        _rules.Remove(rule);
        _matches.Remove(rule);
        AppStorage.SaveRules(_rules);
        BuildList();
    }
}
