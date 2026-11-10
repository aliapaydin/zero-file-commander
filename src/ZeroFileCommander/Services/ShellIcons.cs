using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ZeroFileCommander.Models;

namespace ZeroFileCommander.Services;

/// <summary>
/// Dosya Gezgini'nin kullandığı gerçek simgeler (SHGetFileInfo). Uzantı başına önbelleklenir;
/// .exe, .lnk, .ico gibi dosyaya özgü simgesi olanlar dosya başına alınır.
/// UI iş parçacığında çağrılmalı.
/// </summary>
public static class ShellIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PerFile = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico", ".url", ".msc", ".cpl", ".appref-ms", ".scr",
    };

    public static ImageSource? Get(FileItem item)
    {
        if (item.IsParent) return null;

        string key, path;
        uint attributes = 0;
        bool byType;
        if (item.IsDrive)
        {
            key = "drive:" + item.FullPath;
            path = item.FullPath;
            byType = false;
        }
        else if (item.IsDirectory)
        {
            key = "<folder>";
            path = "folder";
            attributes = FILE_ATTRIBUTE_DIRECTORY;
            byType = true;
        }
        else
        {
            var ext = Path.GetExtension(item.Name);
            if (PerFile.Contains(ext) && !item.IsInArchive)
            {
                key = item.FullPath;
                path = item.FullPath;
                byType = false;
            }
            else
            {
                key = ext.Length == 0 ? "<noext>" : ext;
                path = "file" + ext;
                attributes = FILE_ATTRIBUTE_NORMAL;
                byType = true;
            }
        }

        if (Cache.TryGetValue(key, out var cached)) return cached;
        ImageSource? icon = null;
        try
        {
            icon = Load(path, attributes, byType);
        }
        catch
        {
            // simge alınamazsa yazı tipi simgesi kalır
        }
        if (Cache.Count > 4000) Cache.Clear();
        Cache[key] = icon;
        return icon;
    }

    private static ImageSource? Load(string path, uint attributes, bool byType)
    {
        var info = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_LARGEICON | (byType ? SHGFI_USEFILEATTRIBUTES : 0);
        var result = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            return ToBitmap(info.hIcon);
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static WriteableBitmap? ToBitmap(IntPtr icon)
    {
        if (!GetIconInfo(icon, out var iconInfo)) return null;
        try
        {
            if (iconInfo.hbmColor == IntPtr.Zero) return null;
            var bitmap = new BITMAP();
            if (GetObject(iconInfo.hbmColor, Marshal.SizeOf<BITMAP>(), ref bitmap) == 0) return null;
            int width = bitmap.bmWidth, height = bitmap.bmHeight;
            if (width <= 0 || height <= 0 || width > 256 || height > 256) return null;

            var pixels = new byte[width * height * 4];
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // yukarıdan aşağı
                biPlanes = 1,
                biBitCount = 32,
            };

            IntPtr dc = GetDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(dc, iconInfo.hbmColor, 0, (uint)height, pixels, ref header, 0) == 0) return null;

                bool hasAlpha = false;
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    if (pixels[i] != 0) { hasAlpha = true; break; }
                }
                if (!hasAlpha && iconInfo.hbmMask != IntPtr.Zero)
                {
                    // Eski simgeler: saydamlık maskede
                    var mask = new byte[width * height * 4];
                    var maskHeader = header;
                    if (GetDIBits(dc, iconInfo.hbmMask, 0, (uint)height, mask, ref maskHeader, 0) != 0)
                    {
                        for (int i = 0; i < width * height; i++)
                            pixels[i * 4 + 3] = mask[i * 4] == 0 ? (byte)255 : (byte)0;
                    }
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }

            // WriteableBitmap önceden çarpılmış (premultiplied) BGRA bekler.
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int a = pixels[i + 3];
                if (a == 255) continue;
                pixels[i] = (byte)(pixels[i] * a / 255);
                pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
            }

            var result = new WriteableBitmap(width, height);
            using (var stream = result.PixelBuffer.AsStream())
            {
                stream.Write(pixels, 0, pixels.Length);
            }
            result.Invalidate();
            return result;
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
        }
    }

    // ---------------------------------------------------------------- Win32

    private const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, ref BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
}
