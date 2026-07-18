using System.Windows;
using System.Windows.Controls;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

public sealed record QuarantineRow(
    string Id, string Name, string OriginalPath, string DeletedText, string RemainingText, string SizeText);

/// <summary>
/// Lixeira interna: lista os itens em quarentena, permite restaurar ao local original ou
/// apagar de vez, e mostra quantos dias faltam para a purga automática pela retenção.
/// </summary>
public partial class QuarantinePage : UserControl
{
    public QuarantinePage()
    {
        InitializeComponent();
        DescText.Text = L.F("Qt.Desc", QuarantineStore.RetentionDays);
    }

    public void Reload()
    {
        QuarantineStore.PurgeExpired(); // limpa expirados ao abrir
        ResultsList.Items.Clear();

        var items = QuarantineStore.List();
        foreach (var item in items)
        {
            int remaining = QuarantineStore.RetentionDays - (int)(DateTime.UtcNow - item.DeletedUtc).TotalDays;
            ResultsList.Items.Add(new QuarantineRow(
                item.Id,
                item.Name,
                item.OriginalPath,
                item.DeletedUtc.ToLocalTime().ToString("dd/MM/yyyy"),
                remaining <= 0 ? L.T("Qt.Expiring") : L.F("Qt.RemainingDays", remaining),
                FileSystemNode.FormatSize(item.Size)));
        }

        long total = items.Sum(i => i.Size);
        CountText.Text = items.Count == 0
            ? L.T("Qt.Empty")
            : L.F("Qt.Found", items.Count.ToString("N0"), FileSystemNode.FormatSize(total));
    }

    private List<QuarantineRow> Selected() => ResultsList.SelectedItems.Cast<QuarantineRow>().ToList();

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var rows = Selected();
        if (rows.Count == 0) return;
        foreach (var row in rows) QuarantineStore.Restore(row.Id);
        Reload();
    }

    private void Purge_Click(object sender, RoutedEventArgs e)
    {
        var rows = Selected();
        if (rows.Count == 0) return;
        if (!Controls.DarkDialog.Confirm(Window.GetWindow(this)!, L.T("Qt.PurgeTitle"),
                L.F("Qt.PurgeMsg", rows.Count), danger: true,
                confirmLabel: L.T("Qt.Purge"), cancelLabel: L.T("Del.Cancel")))
            return;
        foreach (var row in rows) QuarantineStore.Purge(row.Id);
        Reload();
    }

    private void EmptyAll_Click(object sender, RoutedEventArgs e)
    {
        var all = ResultsList.Items.Cast<QuarantineRow>().ToList();
        if (all.Count == 0) return;
        if (!Controls.DarkDialog.Confirm(Window.GetWindow(this)!, L.T("Qt.EmptyAllTitle"),
                L.F("Qt.EmptyAllMsg", all.Count), danger: true,
                confirmLabel: L.T("Qt.EmptyAll"), cancelLabel: L.T("Del.Cancel")))
            return;
        foreach (var row in all) QuarantineStore.Purge(row.Id);
        Reload();
    }
}
