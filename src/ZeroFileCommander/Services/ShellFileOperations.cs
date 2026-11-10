using System.Runtime.InteropServices;

namespace ZeroFileCommander.Services;

/// <summary>
/// Kopyala / taşı / sil işlemlerini Windows kabuğuna (SHFileOperation) yaptırır.
/// Böylece sürücüler arası taşıma, çakışma soruları, ilerleme penceresi ve
/// Geri Dönüşüm Kutusu ilk sürümde ücretsiz gelir. İleride kendi transfer
/// kuyruğumuzla (docs/ROADMAP.md) değiştirilecek.
/// </summary>
public static class ShellFileOperations
{
    private const uint FO_MOVE = 0x0001;
    private const uint FO_COPY = 0x0002;
    private const uint FO_DELETE = 0x0003;

    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    public static Task<bool> CopyAsync(IntPtr owner, IEnumerable<string> sources, string destinationFolder) =>
        RunAsync(owner, FO_COPY, sources, destinationFolder, (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR));

    public static Task<bool> MoveAsync(IntPtr owner, IEnumerable<string> sources, string destinationFolder) =>
        RunAsync(owner, FO_MOVE, sources, destinationFolder, (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR));

    /// <summary>Geri Dönüşüm Kutusu'na gönderir (kalıcı silme değil).</summary>
    public static Task<bool> RecycleAsync(IntPtr owner, IEnumerable<string> sources) =>
        RunAsync(owner, FO_DELETE, sources, null, FOF_ALLOWUNDO);

    /// <summary>
    /// Kalıcı olarak siler (Geri Dönüşüm Kutusu'na gitmez). Yalnızca kullanıcı Shift ile
    /// açıkça istediğinde ve onay verdikten sonra çağrılır.
    /// </summary>
    public static Task<bool> DeletePermanentlyAsync(IntPtr owner, IEnumerable<string> sources) =>
        RunAsync(owner, FO_DELETE, sources, null, FOF_NOCONFIRMATION);

    private const ushort FOF_NOCONFIRMATION = 0x0010;

    private static Task<bool> RunAsync(IntPtr owner, uint func, IEnumerable<string> sources, string? destination, ushort flags)
    {
        var from = string.Join('\0', sources) + "\0\0";
        var to = destination is null ? null : destination + "\0\0";

        return Task.Run(() =>
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = owner,
                wFunc = func,
                pFrom = from,
                pTo = to,
                fFlags = flags,
            };
            int result = SHFileOperation(ref op);
            return result == 0 && op.fAnyOperationsAborted == 0;
        });
    }
}
