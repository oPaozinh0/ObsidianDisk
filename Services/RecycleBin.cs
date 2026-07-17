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
