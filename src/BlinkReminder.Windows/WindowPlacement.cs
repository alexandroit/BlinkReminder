using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BlinkReminder.Windows;

public enum MonitorPreference { ActiveWindow, Primary, Named }
public enum BannerPosition { TopCenter, TopLeft, TopRight }
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
public sealed record MonitorSnapshot(string DeviceName, PixelRect WorkArea, uint Dpi, bool IsPrimary);

public static class WindowPlacement
{
    public static IReadOnlyList<MonitorSnapshot> GetMonitors()
    {
        var monitors = new List<MonitorSnapshot>();
        if (!NativeMethods.EnumDisplayMonitors(0, 0,
                (nint handle, nint dc, ref NativeMethods.Rect bounds, nint data) =>
                { monitors.Add(ReadMonitor(handle)); return true; }, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return monitors;
    }

    public static MonitorSnapshot ResolveMonitor(MonitorPreference preference, string? deviceName = null)
    {
        if (preference == MonitorPreference.Named && !string.IsNullOrWhiteSpace(deviceName))
        {
            var match = GetMonitors().FirstOrDefault(x => string.Equals(x.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        var window = preference == MonitorPreference.ActiveWindow ? NativeMethods.GetForegroundWindow() : 0;
        // MONITOR_DEFAULTTOPRIMARY also handles disconnected or missing foreground windows.
        return ReadMonitor(NativeMethods.MonitorFromWindow(window, 1));
    }

    public static PixelRect Calculate(PixelRect workArea, int width, int height, int margin, BannerPosition position)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workArea.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workArea.Height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(margin);
        int actualWidth = Math.Min(width, workArea.Width), actualHeight = Math.Min(height, workArea.Height);
        int horizontalMargin = Math.Min(margin, (workArea.Width - actualWidth) / 2);
        int x = position switch
        {
            BannerPosition.TopLeft => workArea.X + horizontalMargin,
            BannerPosition.TopRight => workArea.X + workArea.Width - actualWidth - horizontalMargin,
            _ => workArea.X + (workArea.Width - actualWidth) / 2
        };
        return new(x, workArea.Y + Math.Min(margin, workArea.Height - actualHeight), actualWidth, actualHeight);
    }

    private static MonitorSnapshot ReadMonitor(nint handle)
    {
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(), DeviceName = string.Empty };
        if (!NativeMethods.GetMonitorInfo(handle, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        uint dpi = NativeMethods.GetDpiForMonitor(handle, 0, out uint horizontal, out _) >= 0 ? horizontal : 96;
        var area = info.Work;
        return new(info.DeviceName, new(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top), dpi, (info.Flags & 1) != 0);
    }
}

public static class PassiveWindow
{
    /// <summary>Call after creating the HWND, before showing an AllowsTransparency WPF window.</summary>
    public static void Configure(nint window, bool clickThrough = true)
    {
        long style = NativeMethods.GetWindowLongPtr(window, NativeMethods.ExtendedStyle).ToInt64();
        style = (style | NativeMethods.NoActivate | NativeMethods.ToolWindow) & ~NativeMethods.AppWindow;
        if (clickThrough) style |= NativeMethods.Layered | NativeMethods.Transparent;
        else style &= ~NativeMethods.Transparent;
        Marshal.SetLastPInvokeError(0);
        if (NativeMethods.SetWindowLongPtr(window, NativeMethods.ExtendedStyle, (nint)style) == 0 && Marshal.GetLastWin32Error() != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static void Position(nint window, PixelRect bounds)
    {
        // HWND_TOPMOST, SWP_NOACTIVATE; never call SetForegroundWindow here.
        if (!NativeMethods.SetWindowPos(window, -1, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static uint GetDpi(nint window) => Math.Max(96, NativeMethods.GetDpiForWindow(window));

    public static nint HandleMessage(int message, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return 3; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        if (message == 0x0084) { handled = true; return -1; } // WM_NCHITTEST / HTTRANSPARENT
        return 0;
    }
}
