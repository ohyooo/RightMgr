using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace RightMgr.Services;

public static class ShellIconService
{
    private const string NullCacheValue = "\0";
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private static readonly ConcurrentDictionary<string, string> PngPathCache = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    public static string? GetPngIconPath(string? resourceOrFilePath)
    {
        var filePath = ShellResourceResolver.ExtractFilePathFromResource(resourceOrFilePath);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        return GetPngIconPathFromShell(filePath, 0, SHGFI_ICON | SHGFI_SMALLICON);
    }

    public static string? GetPngIconPathForTarget(string appliesTo, string? resourceOrFilePath)
    {
        var explicitIcon = GetPngIconPath(resourceOrFilePath);
        if (!string.IsNullOrWhiteSpace(explicitIcon))
            return explicitIcon;

        var extension = ExtractExtension(appliesTo);
        return string.IsNullOrWhiteSpace(extension) ? null : GetPngIconPathForExtension(extension);
    }

    public static string? GetPngIconPathForExtension(string extension)
    {
        if (!extension.StartsWith(".", StringComparison.Ordinal))
            extension = "." + extension;

        return GetPngIconPathFromShell(extension, FILE_ATTRIBUTE_NORMAL, SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
    }

    private static string? GetPngIconPathFromShell(string shellPath, uint attributes, uint flags)
    {
        var cacheKey = $"{shellPath}|{attributes}|{flags}";
        var cached = PngPathCache.GetOrAdd(cacheKey, _ => GetPngIconPathFromShellUncached(shellPath, attributes, flags, cacheKey) ?? NullCacheValue);
        return cached == NullCacheValue ? null : cached;
    }

    private static string? GetPngIconPathFromShellUncached(string shellPath, uint attributes, uint flags, string cacheKey)
    {
        nint iconHandle = nint.Zero;
        try
        {
            var cacheDir = Path.Combine(Path.GetTempPath(), "RightMgr", "icons");
            Directory.CreateDirectory(cacheDir);

            var cacheName = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(cacheKey))) + ".png";
            var output = Path.Combine(cacheDir, cacheName);
            if (File.Exists(output))
                return output;

            var info = new SHFILEINFO();
            var res = SHGetFileInfo(shellPath, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (res == nint.Zero || info.hIcon == nint.Zero)
                return null;

            iconHandle = info.hIcon;
            using var icon = Icon.FromHandle(info.hIcon);
            using var bitmap = icon.ToBitmap();
            bitmap.Save(output, ImageFormat.Png);

            return output;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (iconHandle != nint.Zero)
                DestroyIcon(iconHandle);
        }
    }

    private static string? ExtractExtension(string appliesTo)
    {
        const string prefix = "扩展名 ";
        if (!appliesTo.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var ext = appliesTo[prefix.Length..].Trim();
        return ext.StartsWith(".", StringComparison.Ordinal) ? ext : null;
    }
}
