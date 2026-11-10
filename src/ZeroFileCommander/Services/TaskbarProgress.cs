using System.Runtime.InteropServices;

namespace ZeroFileCommander.Services;

/// <summary>Görev çubuğu düğmesinde ilerleme çubuğu (ITaskbarList3).</summary>
public sealed class TaskbarProgress
{
    private enum TBPFLAG
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8,
    }

    private readonly IntPtr _hwnd;
    private readonly ITaskbarList3? _taskbar;

    public TaskbarProgress(IntPtr hwnd)
    {
        _hwnd = hwnd;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"));
            if (type is not null && Activator.CreateInstance(type) is ITaskbarList3 taskbar)
            {
                taskbar.HrInit();
                _taskbar = taskbar;
            }
        }
        catch
        {
            _taskbar = null; // eski sistem ya da kabuk yok
        }
    }

    /// <summary>0–1 arası ilerleme; null ise çubuk kaldırılır, NaN ise belirsiz gösterilir.</summary>
    public void Set(double? fraction, bool paused = false)
    {
        if (_taskbar is null) return;
        try
        {
            if (fraction is null)
            {
                _taskbar.SetProgressState(_hwnd, TBPFLAG.NoProgress);
                return;
            }
            if (double.IsNaN(fraction.Value))
            {
                _taskbar.SetProgressState(_hwnd, TBPFLAG.Indeterminate);
                return;
            }
            _taskbar.SetProgressState(_hwnd, paused ? TBPFLAG.Paused : TBPFLAG.Normal);
            _taskbar.SetProgressValue(_hwnd, (ulong)(Math.Clamp(fraction.Value, 0, 1) * 1000), 1000);
        }
        catch
        {
            // görev çubuğu düğmesi yokken (tepsideyken) hata verebilir
        }
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        // ITaskbarList3 (kullandığımız kadarı)
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, TBPFLAG flags);
    }
}
