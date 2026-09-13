using System.Runtime.InteropServices;
using System.Text;

namespace BlinkReminder.Windows;

internal static class NativeMethods
{
    internal const int ExtendedStyle = -20;
    internal const long NoActivate = 0x08000000, ToolWindow = 0x80,
        Transparent = 0x20, Layered = 0x80000, AppWindow = 0x40000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LastInputInfo { public uint Size, Time; }

    internal delegate bool MonitorCallback(nint monitor, nint dc, ref Rect bounds, nint data);

    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("shell32.dll")] internal static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder info, uint length, out uint needed);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int GetCurrentPackageFullName(ref uint length, StringBuilder? name);
}
