using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BlinkReminder.App.Services;

/// <summary>Owns the shell icon and dispatches every command to the WPF UI thread.</summary>
public sealed class TrayService : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Action open;
    private readonly Action preview;
    private readonly Action<TimeSpan?> pause;
    private readonly Action tomorrow;
    private readonly Action resume;
    private readonly Action snooze;
    private readonly Action dismiss;
    private readonly Action exit;
    private readonly Dictionary<Forms.ToolStripMenuItem, string> labels = new();
    private Forms.NotifyIcon? notificationIcon;
    private Forms.ContextMenuStrip? menu;
    private Forms.ToolStripMenuItem? statusItem;
    private HwndSource? shellMessages;
    private Icon? activeIcon;
    private Icon? pausedIcon;
    private uint taskbarCreated;
    private string status = "BlinkReminder";
    private bool paused;
    private bool disposed;

    public bool Available { get; private set; }
    public event EventHandler? Unavailable;

    public TrayService(Action open, Action preview, Action<TimeSpan?> pause,
        Action tomorrow, Action resume, Action snooze, Action dismiss, Action exit)
    {
        dispatcher = System.Windows.Application.Current.Dispatcher;
        dispatcher.VerifyAccess();
        this.open = open;
        this.preview = preview;
        this.pause = pause;
        this.tomorrow = tomorrow;
        this.resume = resume;
        this.snooze = snooze;
        this.dismiss = dismiss;
        this.exit = exit;

        try
        {
            activeIcon = LoadApplicationIcon();
            pausedIcon = CreatePausedIcon(activeIcon);
            menu = new Forms.ContextMenuStrip { ShowImageMargin = false };
            statusItem = new Forms.ToolStripMenuItem(status) { Enabled = false };
            menu.Items.Add(statusItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            AddCommand("Open", this.open);
            AddCommand("Preview", this.preview);
            menu.Items.Add(new Forms.ToolStripSeparator());
            AddCommand("Pause15", () => this.pause(TimeSpan.FromMinutes(15)));
            AddCommand("Pause30", () => this.pause(TimeSpan.FromMinutes(30)));
            AddCommand("Pause60", () => this.pause(TimeSpan.FromMinutes(60)));
            AddCommand("PauseTomorrow", this.tomorrow);
            AddCommand("PauseForever", () => this.pause(null));
            AddCommand("Resume", this.resume);
            AddCommand("Snooze", this.snooze);
            AddCommand("Dismiss", this.dismiss);
            menu.Items.Add(new Forms.ToolStripSeparator());
            AddCommand("Exit", this.exit);

            notificationIcon = new Forms.NotifyIcon
            {
                Icon = activeIcon,
                Text = status,
                ContextMenuStrip = menu
            };
            notificationIcon.DoubleClick += OpenRequested;
            taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            if (taskbarCreated == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            shellMessages = new HwndSource(new HwndSourceParameters("BlinkReminder.ShellMessages")
            {
                WindowStyle = 0,
                ExtendedWindowStyle = 0x00000080 | 0x08000000, // TOOLWINDOW | NOACTIVATE
                Width = 0,
                Height = 0
            });
            shellMessages.AddHook(HandleShellMessage);
            notificationIcon.Visible = true;
            Available = true;
        }
        catch (Exception error) when (IsShellFailure(error))
        {
            MarkUnavailable(error);
        }
    }

    public void Update(string status, bool paused)
    {
        if (disposed) return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => Update(status, paused)));
            return;
        }
        this.status = string.IsNullOrWhiteSpace(status) ? "BlinkReminder" : status.Replace('\0', ' ');
        this.paused = paused;
        if (statusItem is not null)
        {
            statusItem.Text = this.status;
            statusItem.AccessibleName = this.status;
        }
        if (notificationIcon is null || !Available) return;
        try
        {
            notificationIcon.Text = Tooltip(this.status);
            notificationIcon.Icon = paused ? pausedIcon : activeIcon;
        }
        catch (Exception error) when (IsShellFailure(error)) { MarkUnavailable(error); }
    }

    public void RefreshLanguage()
    {
        dispatcher.VerifyAccess();
        foreach (var (item, key) in labels)
        {
            item.Text = Localizer.Current[key];
            item.AccessibleName = item.Text;
        }
    }

    private void AddCommand(string key, Action command)
    {
        var item = new Forms.ToolStripMenuItem(Localizer.Current[key]);
        item.AccessibleName = item.Text;
        item.Click += (_, _) => Dispatch(command);
        labels.Add(item, key);
        menu!.Items.Add(item);
    }

    private void OpenRequested(object? sender, EventArgs args) => Dispatch(open);

    private void Dispatch(Action command)
    {
        if (disposed || dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) command();
        else dispatcher.BeginInvoke(command);
    }

    private nint HandleShellMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == taskbarCreated && !disposed && notificationIcon is not null)
        {
            // Explorer has recreated its notification area. Re-register our icon once.
            try
            {
                notificationIcon.Visible = false;
                notificationIcon.Icon = paused ? pausedIcon : activeIcon;
                notificationIcon.Text = Tooltip(status);
                notificationIcon.Visible = true;
                Available = true;
            }
            catch (Exception error) when (IsShellFailure(error)) { MarkUnavailable(error); }
        }
        return 0;
    }

    private void MarkUnavailable(Exception error)
    {
        Available = false;
        Trace.TraceWarning("The notification area is unavailable: {0}.", error.GetType().Name);
        Unavailable?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsShellFailure(Exception error) =>
        error is ExternalException or InvalidOperationException or ArgumentException or IOException;

    private static string Tooltip(string text)
    {
        if (text.Length <= 63) return text;
        int count = char.IsHighSurrogate(text[62]) ? 62 : 63;
        return text[..count];
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty);
            if (icon is not null) return icon;
        }
        catch (Exception error) when (IsShellFailure(error))
        {
            Trace.TraceWarning("The application icon could not be loaded; using the system fallback.");
        }
        return (Icon)SystemIcons.Application.Clone();
    }

    private static Icon CreatePausedIcon(Icon original)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.DrawIcon(original, new Rectangle(0, 0, 32, 32));
            using var backdrop = new SolidBrush(Color.White);
            using var foreground = new SolidBrush(Color.FromArgb(15, 23, 42));
            graphics.FillRectangle(backdrop, 16, 16, 16, 16);
            graphics.FillRectangle(foreground, 19, 19, 4, 10);
            graphics.FillRectangle(foreground, 25, 19, 4, 10);
        }
        nint handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    public void Dispose()
    {
        if (disposed) return;
        dispatcher.VerifyAccess();
        disposed = true;
        Available = false;
        if (shellMessages is not null)
        {
            shellMessages.RemoveHook(HandleShellMessage);
            shellMessages.Dispose();
        }
        if (notificationIcon is not null)
        {
            notificationIcon.DoubleClick -= OpenRequested;
            notificationIcon.Visible = false;
            notificationIcon.Dispose();
        }
        menu?.Dispose();
        pausedIcon?.Dispose();
        activeIcon?.Dispose();
        labels.Clear();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
