using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using BlinkReminder.Core;
using BlinkReminder.App.Services;
using BlinkReminder.Windows;
using Microsoft.Win32;

namespace BlinkReminder.App.Views;

public partial class BannerWindow : Window
{
    private readonly ReminderRequest request;
    private readonly AppearanceSettings appearance;
    private readonly string monitorDevice;
    private nint handle;
    private bool closed;
    public event EventHandler? Presented;

    public BannerWindow(ReminderRequest request, AppearanceSettings appearance)
    {
        InitializeComponent();
        this.request = request;
        this.appearance = appearance;
        ThemeService.Apply(appearance.Theme, Resources);
        MessageText.Text = request.Message;
        MessageText.FontSize = appearance.FontSize;
        monitorDevice = WindowPlacement.ResolveMonitor(appearance.Monitor switch
        {
            MonitorSelection.Primary => MonitorPreference.Primary,
            MonitorSelection.Selected => MonitorPreference.Named,
            _ => MonitorPreference.ActiveWindow
        }, appearance.MonitorDeviceName).DeviceName;
        SourceInitialized += OnSourceInitialized;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_, _) => { closed = true; SystemEvents.DisplaySettingsChanged -= DisplayChanged; };
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        handle = new WindowInteropHelper(this).Handle;
        PassiveWindow.Configure(handle);
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        Reposition();
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // WPF handles WM_DPICHANGED; position again after its layout has caught up.
        if (message == 0x02E0) Dispatcher.BeginInvoke(Reposition);
        return PassiveWindow.HandleMessage(message, ref handled);
    }

    private void DisplayChanged(object? sender, EventArgs args) => Dispatcher.BeginInvoke(Reposition);
    private void Reposition()
    {
        if (closed || handle == 0) return;
        var monitor = WindowPlacement.ResolveMonitor(MonitorPreference.Named, monitorDevice);
        double scale = Math.Max(monitor.Dpi / 96d, 1d);
        Width = Math.Max(1, Math.Min(440, monitor.WorkArea.Width / scale - appearance.Margin * 2));
        UpdateLayout();
        double height = ActualHeight > 0 ? ActualHeight : 150;
        var bounds = WindowPlacement.Calculate(monitor.WorkArea, (int)Math.Ceiling(Width * scale),
            (int)Math.Ceiling(height * scale), (int)Math.Round(appearance.Margin * scale),
            (BlinkReminder.Windows.BannerPosition)appearance.Position);
        PassiveWindow.Position(handle, bounds);
    }

    public async Task ShowAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            new WindowInteropHelper(this).EnsureHandle();
            Show();
            Reposition();
            Presented?.Invoke(this, EventArgs.Empty);
            if (appearance.SoundEnabled) System.Media.SystemSounds.Asterisk.Play();
            if (appearance.AnimationsEnabled && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            long started = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(started) < request.Duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = request.Duration - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero) break;
                if (request.ShowCountdown)
                {
                    CountdownText.Visibility = Visibility.Visible;
                    CountdownText.Text = $"{Math.Max(0, Math.Ceiling(remaining.TotalSeconds)):0} s";
                }
                await Task.Delay(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        finally { if (!closed) Close(); }
    }
}
