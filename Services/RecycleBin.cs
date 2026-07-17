using System.IO;
using System.Runtime.InteropServices;

namespace ObsidianDisk.Services;

/// <summary>Exclusão de arquivos/pastas: para a Lixeira (com desfazer) ou permanente.</summary>
public static class FileDeletion
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);

    /// <summary>Move o caminho para a Lixeira. Retorna true em caso de sucesso.</summary>
    public static bool ToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCTW
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0", // o marshaling adiciona o segundo \0 terminador
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        return SHFileOperationW(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    /// <summary>
    /// Restaura da Lixeira o item que estava em <paramref name="originalPath"/>.
    /// Usa o verbo canônico "undelete" do shell — independe do idioma do Windows.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetLongPathNameW(string shortPath, System.Text.StringBuilder longPath, uint bufferSize);

    /// <summary>
    /// A Lixeira sempre reporta o caminho longo; o app pode ter recebido a forma
    /// curta (8.3). Normaliza a pasta (que ainda existe) para comparar de verdade.
    /// </summary>
    private static string NormalizePath(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            string? dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir)) return full;

            var buffer = new System.Text.StringBuilder(1024);
            uint length = GetLongPathNameW(dir, buffer, (uint)buffer.Capacity);
            string longDir = length is > 0 and < 1024 ? buffer.ToString() : dir;
            return Path.Combine(longDir, Path.GetFileName(full));
        }
        catch
        {
            return path;
        }
    }

    public static bool RestoreFromRecycleBin(string originalPath)
    {
        const int SsfBitBucket = 10;
        originalPath = NormalizePath(originalPath);
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return false;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null) return false;

            dynamic bin = shell.Namespace(SsfBitBucket);
            foreach (dynamic item in bin.Items())
            {
                // item.Path é o caminho interno ($R...); o original é DeletedFrom + Name
                string deletedFrom = item.ExtendedProperty("System.Recycle.DeletedFrom") ?? "";
                if (deletedFrom.Length == 0) continue;

                string restored = Path.Combine(deletedFrom, (string)item.Name);
                if (!string.Equals(restored, originalPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                // "undelete" é o nome canônico do "Restaurar" — não muda com o idioma
                item.InvokeVerb("undelete");
                return File.Exists(originalPath) || Directory.Exists(originalPath);
            }
        }
        catch
        {
            // COM indisponível / item já saiu da Lixeira
        }
        return false;
    }

    /// <summary>Exclui permanentemente (NÃO passa pela Lixeira). Retorna true em caso de sucesso.</summary>
    public static bool Permanently(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
            else
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
