using System.Runtime.InteropServices;
using System.Text;
using IStream = System.Runtime.InteropServices.ComTypes.IStream;

namespace ZeroFileCommander.Services;

/// <summary>
/// Windows önizleme işleyicilerini (Dosya Gezgini'nin önizleme bölmesindekiyle aynı) barındırır.
/// Word, Excel, PowerPoint, Visio, Outlook vb. kendi işleyicilerini kaydettiği için
/// belgeler sayfa sayfa/slayt slayt kendi araçlarıyla gösterilir.
/// İşleyici, ana pencerenin içine yerleştirilen bir alt Win32 penceresinde çizer.
/// </summary>
public sealed class PreviewHandlerHost : IDisposable
{
    private static readonly Guid PreviewHandlerIid = new("8895b1c6-b41f-4c1c-a562-0d564250836f");

    private readonly IntPtr _parent;
    private IntPtr _child;
    private object? _handler;
    private IPreviewHandler? _preview;
    private IStream? _stream;
    private RECT _bounds;
    private bool _visible;

    public PreviewHandlerHost(IntPtr parent)
    {
        _parent = parent;
    }

    public bool IsActive => _preview is not null;

    /// <summary>Uzantı için kayıtlı önizleme işleyicisinin CLSID'si.</summary>
    public static Guid? FindHandler(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return null;
        if (!extension.StartsWith('.')) extension = "." + extension;
        try
        {
            uint size = 64;
            var buffer = new StringBuilder((int)size);
            int hr = AssocQueryString(ASSOCF_INIT_DEFAULTTOSTAR, ASSOCSTR_SHELLEXTENSION, extension,
                PreviewHandlerIid.ToString("B"), buffer, ref size);
            return hr == 0 && Guid.TryParse(buffer.ToString(), out var clsid) ? clsid : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Dosyayı işleyiciyle açar. Konum, ana pencerenin istemci alanına göre fiziksel piksel.</summary>
    public bool Open(string path, Guid clsid, int x, int y, int width, int height)
    {
        Close();
        EnsureChild();
        _bounds = new RECT { left = x, top = y, right = x + width, bottom = y + height };

        try
        {
            _handler = Create(clsid);
            if (_handler is null) return false;

            if (_handler is IInitializeWithFile withFile)
            {
                withFile.Initialize(path, STGM_READ);
            }
            else if (_handler is IInitializeWithStream withStream)
            {
                int hr = SHCreateStreamOnFileEx(path, STGM_READ | STGM_SHARE_DENY_NONE, 0, false, IntPtr.Zero, out _stream);
                if (hr != 0 || _stream is null) { Close(); return false; }
                withStream.Initialize(_stream, STGM_READ);
            }
            else
            {
                Close();
                return false;
            }

            _preview = (IPreviewHandler)_handler;
            ApplyBounds();
            var client = new RECT { right = width, bottom = height };
            _preview.SetWindow(_child, ref client);
            _preview.DoPreview();
            UpdateVisibility();
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("PreviewHandler.Open " + Path.GetExtension(path), ex);
            Close();
            return false;
        }
    }

    public void SetBounds(int x, int y, int width, int height)
    {
        var next = new RECT { left = x, top = y, right = x + Math.Max(1, width), bottom = y + Math.Max(1, height) };
        if (next.Equals(_bounds)) return;
        _bounds = next;
        ApplyBounds();
        if (_preview is null) return;
        try
        {
            var client = new RECT { right = next.right - next.left, bottom = next.bottom - next.top };
            _preview.SetRect(ref client);
        }
        catch
        {
            // işleyici kapanmış olabilir
        }
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        UpdateVisibility();
    }

    public void Close()
    {
        if (_preview is not null)
        {
            try { _preview.Unload(); } catch { /* yoksay */ }
        }
        _preview = null;
        if (_handler is not null && Marshal.IsComObject(_handler))
        {
            try { Marshal.FinalReleaseComObject(_handler); } catch { /* yoksay */ }
        }
        _handler = null;
        if (_stream is not null && Marshal.IsComObject(_stream))
        {
            try { Marshal.FinalReleaseComObject(_stream); } catch { /* yoksay */ }
        }
        _stream = null;
        if (_child != IntPtr.Zero) ShowWindow(_child, SW_HIDE);
    }

    public void Dispose()
    {
        Close();
        if (_child != IntPtr.Zero)
        {
            DestroyWindow(_child);
            _child = IntPtr.Zero;
        }
    }

    private void UpdateVisibility()
    {
        if (_child == IntPtr.Zero) return;
        ShowWindow(_child, _visible && _preview is not null ? SW_SHOWNA : SW_HIDE);
    }

    private void ApplyBounds()
    {
        if (_child == IntPtr.Zero) return;
        SetWindowPos(_child, HWND_TOP, _bounds.left, _bounds.top,
            _bounds.right - _bounds.left, _bounds.bottom - _bounds.top, SWP_NOACTIVATE);
    }

    private void EnsureChild()
    {
        if (_child != IntPtr.Zero) return;
        _child = CreateWindowEx(0, "Static", string.Empty, WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0, 1, 1, _parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    private static object? Create(Guid clsid)
    {
        var iid = PreviewHandlerIid;
        // Önce ayrı süreçte (prevhost.exe) dene; çökse bile uygulamamız etkilenmez.
        if (CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_LOCAL_SERVER, ref iid, out var instance) == 0) return instance;
        if (CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out instance) == 0) return instance;
        return null;
    }

    // ---------------------------------------------------------------- COM / Win32

    private const uint STGM_READ = 0x0;
    private const uint STGM_SHARE_DENY_NONE = 0x40;
    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const int ASSOCF_INIT_DEFAULTTOSTAR = 0x4;
    private const int ASSOCSTR_SHELLEXTENSION = 16;
    private const uint WS_CHILD = 0x40000000, WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;
    private const int SW_HIDE = 0, SW_SHOWNA = 8;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x, y;
    }

    [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPreviewHandler
    {
        void SetWindow(IntPtr hwnd, ref RECT rect);
        void SetRect(ref RECT rect);
        void DoPreview();
        void Unload();
        void SetFocus();
        void QueryFocus(out IntPtr phwnd);
        [PreserveSig] uint TranslateAccelerator(ref MSG pmsg);
    }

    [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithFile
    {
        void Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
    }

    [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithStream
    {
        void Initialize(IStream pstream, uint grfMode);
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(int flags, int str, string assoc, string? extra, StringBuilder? output, ref uint size);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateStreamOnFileEx(string file, uint mode, uint attributes, bool create, IntPtr template, out IStream stream);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
