using System.Runtime.InteropServices;

namespace ZeroFileCommander.Services;

public sealed record TrayMenuItem(string Text, Action? Action = null, bool IsSeparator = false, bool IsChecked = false, bool IsDefault = false)
{
    public static TrayMenuItem Separator { get; } = new(string.Empty, IsSeparator: true);
}

/// <summary>
/// Sistem tepsisi simgesi. Ana pencerenin HWND'sine bağlanır (pencere gizliyken de çalışır),
/// sol tık pencereyi açar, sağ tık menü gösterir.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint NIM_ADD = 0x0, NIM_MODIFY = 0x1, NIM_DELETE = 0x2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4;
    private const uint WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205;
    private const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800, MF_CHECKED = 0x8, MF_DEFAULT = 0x1000;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, TPM_BOTTOMALIGN = 0x20;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;

    private readonly IntPtr _hwnd;
    private readonly uint _callbackMessage;
    private readonly uint _taskbarCreatedMessage;
    private readonly SubclassProc _proc; // GC'den korunmalı
    private IntPtr _icon;
    private string _tooltip;
    private bool _added;

    public event EventHandler? OpenRequested;

    /// <summary>Sağ tıkta gösterilecek menüyü üretir.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }

    public TrayIcon(IntPtr hwnd, string iconPath, string tooltip)
    {
        _hwnd = hwnd;
        _tooltip = tooltip;
        _callbackMessage = RegisterWindowMessage("ZeroFileCommander.TrayCallback");
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _icon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
        _proc = WndProc;
        SetWindowSubclass(_hwnd, _proc, (UIntPtr)0x5A46, UIntPtr.Zero);
    }

    public bool IsVisible => _added;

    public void Show()
    {
        if (_added) return;
        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    public void Hide()
    {
        if (!_added) return;
        var data = CreateData(0);
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _added = false;
    }

    public void SetTooltip(string text)
    {
        _tooltip = text.Length > 127 ? text[..127] : text;
        if (!_added) return;
        var data = CreateData(NIF_TIP);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        Hide();
        RemoveWindowSubclass(_hwnd, _proc, (UIntPtr)0x5A46);
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private NOTIFYICONDATA CreateData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = _callbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
    {
        if (msg == _callbackMessage)
        {
            uint mouse = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouse is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
                OpenRequested?.Invoke(this, EventArgs.Empty);
            else if (mouse == WM_RBUTTONUP)
                ShowMenu();
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreatedMessage && _added)
        {
            // Explorer yeniden başladı; simgeyi tekrar ekle.
            _added = false;
            Show();
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var items = MenuProvider?.Invoke();
        if (items is null || items.Count == 0) return;

        IntPtr menu = CreatePopupMenu();
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.IsSeparator)
                {
                    AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }
                uint flags = MF_STRING;
                if (item.IsChecked) flags |= MF_CHECKED;
                if (item.IsDefault) flags |= MF_DEFAULT;
                AppendMenu(menu, flags, (UIntPtr)(uint)(i + 1), item.Text);
            }

            GetCursorPos(out var point);
            SetForegroundWindow(_hwnd);
            int command = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_BOTTOMALIGN, point.X, point.Y, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero); // WM_NULL: menünün düzgün kapanması için

            if (command > 0 && command <= items.Count) items[command - 1].Action?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    // ---------------------------------------------------------------- Win32

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr refData);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
