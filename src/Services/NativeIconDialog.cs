using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace RightMgr.Services;

public static class NativeIconDialog
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "#62")]
    private static extern int PickIconDlg(nint hwnd, StringBuilder pszIconPath, uint cchIconPath, ref int piIconIndex);

    public static string? Show(Window owner, string? currentResource)
    {
        var currentPath = ShellResourceResolver.ExtractFilePathFromResource(currentResource) ?? "";
        var iconIndex = ShellResourceResolver.ExtractIconIndex(currentResource);
        var buffer = new StringBuilder(currentPath, 2048);
        var hwnd = new WindowInteropHelper(owner).Handle;

        return PickIconDlg(hwnd, buffer, (uint)buffer.Capacity, ref iconIndex) == 0
            ? null
            : $"{buffer},{iconIndex}";
    }
}
