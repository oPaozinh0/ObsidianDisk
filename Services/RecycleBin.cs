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

    /// <summary>
    /// Move um arquivo ou pasta para dentro de <paramref name="destDir"/>, preservando o nome.
    /// Funciona entre volumes (copia + apaga quando não é o mesmo drive). Nunca sobrescreve um
    /// item já existente no destino. Retorna true em caso de sucesso.
    /// </summary>
    public static bool Move(string sourcePath, string destDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(destDir)) return false;
            Directory.CreateDirectory(destDir);

            string name = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string target = Path.Combine(destDir, name);

            // Destino não pode ser o próprio item nem estar dentro dele (moveria a si mesmo)
            string fullSource = Path.GetFullPath(sourcePath);
            string fullTarget = Path.GetFullPath(target);
            if (string.Equals(fullSource, fullTarget, StringComparison.OrdinalIgnoreCase)) return false;
            if (fullTarget.StartsWith(fullSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;
            if (File.Exists(target) || Directory.Exists(target)) return false; // não sobrescreve

            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, target); // File.Move já cruza volumes (copia + apaga)
                return true;
            }
            if (Directory.Exists(sourcePath))
            {
                MoveDirectory(sourcePath, target);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void MoveDirectory(string source, string target)
    {
        // Mesmo volume: renomear é instantâneo. Volumes diferentes: Directory.Move lança IOException.
        try
        {
            Directory.Move(source, target);
            return;
        }
        catch (IOException) { }

        CopyDirectory(source, target);
        Directory.Delete(source, recursive: true);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
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
