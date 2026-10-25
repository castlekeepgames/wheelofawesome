using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WheelOfAwesome.Native;

public sealed record AppEntry(string Name, string AppId, string? ExePath);

/// <summary>Shell COM interop: shell items, high-res icons, the AppsFolder (installed apps) and window AUMIDs.</summary>
internal static class Shell
{
    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid IID_IShellItem2 = new("7e9fb0d3-919f-4307-ab2e-9b1860310c93");
    private static readonly Guid IID_IEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
    private static readonly Guid IID_IPropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
    private static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    private static readonly Guid FOLDERID_AppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");

    public const uint SIGDN_NORMALDISPLAY = 0;
    public const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;
    public const uint SIGDN_FILESYSPATH = 0x80058000;

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
        public PROPERTYKEY(string g, uint p) { fmtid = new Guid(g); pid = p; }
    }

    public static readonly PROPERTYKEY PKEY_Link_TargetParsingPath = new("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25", 2);
    public static readonly PROPERTYKEY PKEY_AppUserModel_ID = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 5);

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem2
    {
        // IShellItem
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
        // IShellItem2
        [PreserveSig] int GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyStoreWithCreateObject(int flags, IntPtr punkCreateObject, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyStoreForKeys(IntPtr rgKeys, uint cKeys, int flags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyDescriptionList(ref PROPERTYKEY keyType, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Update(IntPtr pbc);
        [PreserveSig] int GetProperty(ref PROPERTYKEY key, IntPtr ppropvar);
        [PreserveSig] int GetCLSID(ref PROPERTYKEY key, out Guid pclsid);
        [PreserveSig] int GetFileTime(ref PROPERTYKEY key, out long pft);
        [PreserveSig] int GetInt32(ref PROPERTYKEY key, out int pi);
        [PreserveSig] int GetString(ref PROPERTYKEY key, [MarshalAs(UnmanagedType.LPWStr)] out string ppsz);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumShellItems
    {
        [PreserveSig] int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem rgelt, out uint pceltFetched);
        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems ppenum);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPVARIANT
    {
        public ushort vt;
        public ushort r1, r2, r3;
        public IntPtr p;
        public IntPtr p2;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetKnownFolderItem(ref Guid rfid, uint flags, IntPtr hToken, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint flags, IntPtr hToken, out IntPtr path);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    public static object? CreateItem(string parsingName)
    {
        var iid = IID_IShellItem;
        return SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var obj) == 0 ? obj : null;
    }

    /// <summary>Returns a shell icon at up to <paramref name="size"/> px for a path or "shell:AppsFolder\AUMID".</summary>
    public static BitmapSource? GetIcon(string parsingName, int size)
    {
        var item = CreateItem(parsingName);
        if (item == null) return null;
        try { return GetIcon(item, size); }
        finally { Marshal.ReleaseComObject(item); }
    }

    public static BitmapSource? GetIcon(object shellItem, int size)
    {
        if (shellItem is not IShellItemImageFactory factory) return null;
        // SIIGBF_BIGGERSIZEOK (0x1) | SIIGBF_ICONONLY (0x4): an icon, never a document thumbnail.
        if (factory.GetImage(new SIZE { cx = size, cy = size }, 0x1 | 0x4, out var hbmp) != 0 || hbmp == IntPtr.Zero)
            return null;
        try { return HBitmapToSource(hbmp); }
        finally { Win32.DeleteObject(hbmp); }
    }

    private static BitmapSource? HBitmapToSource(IntPtr hbmp)
    {
        if (Win32.GetObject(hbmp, Marshal.SizeOf<Win32.BITMAP>(), out var bm) == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[w * h * 4];
        var hdc = Win32.GetDC(IntPtr.Zero);
        try
        {
            if (Win32.GetDIBits(hdc, hbmp, 0, (uint)h, pixels, ref header, 0) == 0) return null;
        }
        finally { Win32.ReleaseDC(IntPtr.Zero, hdc); }

        // Some icons come back without an alpha channel; treat those as opaque.
        bool anyAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { anyAlpha = true; break; }
        var format = anyAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;
        var source = BitmapSource.Create(w, h, 96, 96, format, null, pixels, w * 4);
        source.Freeze();
        return source;
    }

    public static string? GetString(object shellItem, PROPERTYKEY key)
    {
        if (shellItem is not IShellItem2 item2) return null;
        try { return item2.GetString(ref key, out var s) == 0 ? s : null; }
        catch { return null; }
    }

    public static string? GetDisplayName(object shellItem, uint sigdn)
    {
        if (shellItem is not IShellItem item) return null;
        return item.GetDisplayName(sigdn, out var name) == 0 ? name : null;
    }



    /// <summary>Enumerates the Start menu's app list (shell:AppsFolder): desktop and Store apps alike.</summary>
    public static List<AppEntry> EnumerateInstalledApps()
    {
        var result = new List<AppEntry>();
        var folderId = FOLDERID_AppsFolder;
        var iid = IID_IShellItem;
        if (SHGetKnownFolderItem(ref folderId, 0, IntPtr.Zero, ref iid, out var folderObj) != 0) return result;
        var folder = (IShellItem)folderObj;
        try
        {
            var bhid = BHID_EnumItems;
            var enumIid = IID_IEnumShellItems;
            if (folder.BindToHandler(IntPtr.Zero, ref bhid, ref enumIid, out var enumObj) != 0) return result;
            var enumerator = (IEnumShellItems)enumObj;
            while (enumerator.Next(1, out var child, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    var name = GetDisplayName(child, SIGDN_NORMALDISPLAY);
                    var id = GetDisplayName(child, SIGDN_PARENTRELATIVEPARSING);
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                    result.Add(new AppEntry(name, id, ResolveExe(child, id)));
                }
                finally { Marshal.ReleaseComObject(child); }
            }
            Marshal.ReleaseComObject(enumerator);
        }
        finally { Marshal.ReleaseComObject(folder); }
        return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Best-effort exe path for an AppsFolder entry, used to recognise its running windows.</summary>
    private static string? ResolveExe(object item, string appId)
    {
        var target = GetString(item, PKEY_Link_TargetParsingPath);
        if (IsExe(target)) return target;
        var path = ExpandKnownFolderPath(appId);
        if (IsExe(path)) return path;
        return null;

        static bool IsExe(string? p) => !string.IsNullOrEmpty(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p);
    }

    /// <summary>Turns "{known-folder-guid}\sub\app.exe" into a real path.</summary>
    public static string? ExpandKnownFolderPath(string appId)
    {
        if (appId.Length > 38 && appId[0] == '{' && appId[37] == '}' && Guid.TryParse(appId[..38], out var guid))
        {
            if (SHGetKnownFolderPath(ref guid, 0, IntPtr.Zero, out var p) == 0)
            {
                var root = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                return root + appId[38..];
            }
            return null;
        }
        return appId;
    }

    /// <summary>For shortcuts (.lnk), the exe they point at; otherwise null.</summary>
    public static string? ResolveShortcutTarget(string lnkPath)
    {
        var item = CreateItem(lnkPath);
        if (item == null) return null;
        try { return GetString(item, PKEY_Link_TargetParsingPath); }
        finally { Marshal.ReleaseComObject(item); }
    }

    public static string? GetWindowAppId(IntPtr hwnd)
    {
        var iid = IID_IPropertyStore;
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store == null) return null;
        try
        {
            var key = PKEY_AppUserModel_ID;
            if (store.GetValue(ref key, out var pv) != 0) return null;
            try { return pv.vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(pv.p) : null; }
            finally { PropVariantClear(ref pv); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }
}
