using Microsoft.UI.Xaml.Controls;
using ZeroFileCommander.Models;
using ZeroFileCommander.Services;

namespace ZeroFileCommander.Controls;

/// <summary>"Yeni" menüsünün öğelerini üretir (klasör + kayıtlı dosya türleri).</summary>
public static class NewMenu
{
    public static void Populate(IList<MenuFlyoutItemBase> items, Action<ShellNewEntry?> onPick)
    {
        items.Clear();

        var folder = new MenuFlyoutItem
        {
            Text = Loc.T("Klasör"),
            Icon = new FontIcon { Glyph = "" },
            KeyboardAcceleratorTextOverride = "F7",
        };
        folder.Click += (_, _) => onPick(null);
        items.Add(folder);
        items.Add(new MenuFlyoutSeparator());

        foreach (var entry in ShellNew.Entries)
        {
            var kind = FileKinds.Of(new FileItem { Name = "x" + entry.Extension });
            var item = new MenuFlyoutItem
            {
                Text = entry.DisplayName,
                Icon = new FontIcon { Glyph = FileKinds.Glyph(kind) },
            };
            ToolTipService.SetToolTip(item, entry.Extension);
            var captured = entry;
            item.Click += (_, _) => onPick(captured);
            items.Add(item);
        }
    }
}
