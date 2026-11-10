using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZeroFileCommander.Services;

/// <summary>Windows kabuğuna devredilen tekil eylemler: aç, birlikte aç, özellikler…</summary>
public static class ShellActions
{
    public static void Open(string path) =>
        Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public static void OpenWith(string path) =>
        Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}") { UseShellExecute = false });

    public static void RunAsAdmin(string path) =>
        Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });

    public static void ShowInExplorer(string path)
    {
        if (Directory.Exists(path) && !File.Exists(path))
            Start(new ProcessStartInfo("explorer.exe", $"\"{path}\""));
        else
            Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));
    }

    public static void OpenTerminal(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{folder}\"") { UseShellExecute = true });
        }
        catch
        {
            Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WorkingDirectory = folder });
        }
    }

    public static void EditInNotepad(string path) =>
        Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });

    /// <summary>Windows'un kendi Özellikler penceresini açar.</summary>
    public static void ShowWindowsProperties(string path)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            lpVerb = "properties",
            lpFile = path,
            nShow = 5, // SW_SHOW
            fMask = 0x0000000C, // SEE_MASK_INVOKEIDLIST
        };
        ShellExecuteEx(ref info);
    }

    private static void Start(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Kullanıcı UAC'yi reddetti ya da ilişkilendirme yok; sessizce geç.
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
}
