using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ZeroFileCommander.Services;

/// <summary>
/// Açık diyalog/menü sayısını izler. Önizleme panelindeki Win32 önizleyici (Office)
/// XAML'in üstünde çizildiği için, bir şey açıkken geçici olarak gizlenir.
/// </summary>
public static class Overlay
{
    private static int _count;

    public static event EventHandler? Changed;

    public static bool IsOpen => _count > 0;

    public static void Track(FlyoutBase flyout)
    {
        flyout.Opening += (_, _) => Push();
        flyout.Closed += (_, _) => Pop();
    }

    public static void Track(ContentDialog dialog)
    {
        dialog.Opened += (_, _) => Push();
        dialog.Closed += (_, _) => Pop();
    }

    public static void Push()
    {
        _count++;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Pop()
    {
        _count = Math.Max(0, _count - 1);
        Changed?.Invoke(null, EventArgs.Empty);
    }
}

/// <summary>Açılan menülerde imlecin normal ok olarak kalmasını sağlar.</summary>
public static class CursorHelper
{
    private static readonly PropertyInfo? ProtectedCursor =
        typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void SetArrow(UIElement element)
    {
        try
        {
            ProtectedCursor?.SetValue(element, InputSystemCursor.Create(InputSystemCursorShape.Arrow));
        }
        catch
        {
            // desteklenmiyorsa varsayılan imleç kalır
        }
    }

    /// <summary>XamlRoot'taki açık açılır pencerelerin içeriğine ok imleci uygular.</summary>
    public static void ApplyToOpenPopups(XamlRoot? root)
    {
        if (root is null) return;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child is UIElement child) SetArrow(child);
        }
    }
}
