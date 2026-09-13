using System.Runtime.InteropServices;
using System.Xml.Linq;
using BlinkReminder.Windows;
using Xunit;

namespace BlinkReminder.Windows.Tests;

public sealed class IntegrationContractTests
{
    [Theory]
    [InlineData(BannerPosition.TopLeft, -1900)]
    [InlineData(BannerPosition.TopCenter, -1160)]
    [InlineData(BannerPosition.TopRight, -420)]
    public void PlacementRespectsNegativeCoordinatesAndWorkArea(BannerPosition position, int expectedX)
    {
        var result = WindowPlacement.Calculate(new(-1920, -100, 1920, 1040), 400, 120, 20, position);
        Assert.Equal(new PixelRect(expectedX, -80, 400, 120), result);
    }

    [Fact]
    public void OversizedBannerAndMarginStayInsideWorkArea()
    {
        var result = WindowPlacement.Calculate(new(-640, 0, 640, 480), 900, 600, 1000, BannerPosition.TopRight);
        Assert.Equal(new PixelRect(-640, 0, 640, 480), result);
    }

    [Fact]
    public void SettingsRootsAreExplicitAndSeparateForEachChannel()
    {
        var exe = ApplicationPaths.Resolve(false, @"C:\Users\Test\AppData\Local\BlinkReminder");
        var msix = ApplicationPaths.Resolve(true, @"C:\Users\Test\AppData\Local\Packages\Test_id\LocalState");
        Assert.NotEqual(exe.SettingsDirectory, msix.SettingsDirectory);
        Assert.False(exe.IsPackaged);
        Assert.True(msix.IsPackaged);
        Assert.StartsWith(msix.SettingsDirectory, msix.DiagnosticDirectory, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ApplicationPaths.Resolve(false, "relative"));
    }

    [Fact]
    public void NotificationPayloadEscapesUntrustedTextAndDefaultsToSilent()
    {
        const string message = "<action arguments=\"shell\">& text";
        var xml = XDocument.Parse(NativeNotifications.BuildPayload("BlinkReminder", message));
        Assert.Equal(message, xml.Descendants("text").Last().Value);
        Assert.Empty(xml.Descendants("action"));
        Assert.Equal("true", xml.Descendants("audio").Single().Attribute("silent")!.Value);
        Assert.Equal("action=openSettings", xml.Root!.Attribute("launch")!.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("action=openSettings&command=calc")]
    [InlineData("action=preview")]
    [InlineData("https://example.com")]
    public void NotificationActivationRejectsEverythingOutsideTheWhitelist(string? arguments) =>
        Assert.False(NativeNotifications.TryParseActivation(arguments, out _));

    [Fact]
    public void NotificationActivationAcceptsOnlySettings()
    {
        Assert.True(NativeNotifications.TryParseActivation("action=openSettings", out var command));
        Assert.Equal(InstanceCommand.OpenSettings, command);
    }

    [Fact]
    public void NotificationCommandLineOnlySelectsTheHandshakePath()
    {
        Assert.True(ApplicationActivation.IsNotificationLaunch(new[] { "----AppNotificationActivated:" }));
        Assert.False(ApplicationActivation.IsNotificationLaunch(new[] { "----AppNotificationActivated:action=openSettings" }));
        Assert.False(NativeNotifications.TryParseActivation("----AppNotificationActivated:", out _));
    }

    [Fact]
    public void StartupApprovalIsReadConservatively()
    {
        var disabled = new byte[12]; disabled[0] = 3;
        var enabled = new byte[12]; enabled[0] = 2;
        Assert.Equal(StartupRegistrationState.DisabledByUser, WindowsStartupAdapter.InterpretStartupApproval(disabled));
        Assert.Equal(StartupRegistrationState.Enabled, WindowsStartupAdapter.InterpretStartupApproval(enabled));
        Assert.Equal(StartupRegistrationState.Unavailable, WindowsStartupAdapter.InterpretStartupApproval(new byte[] { 2 }));
        Assert.Equal(StartupRegistrationState.Unavailable, WindowsStartupAdapter.InterpretStartupApproval(new byte[12]));
    }

    [Fact]
    public async Task StartupEnableRequiresConsentBeforeAnyWindowsMutation()
    {
        var adapter = new WindowsStartupAdapter(false, @"C:\Apps\BlinkReminder.exe");
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.SetEnabledAsync(true, false));
    }

    [Fact]
    public void InstanceNamesSeparateUsersAndSessionsButNotDistributionChannels()
    {
        string scope = SingleInstanceCoordinator.CreateScope("BlinkReminder", "S-1-5-21-1", 1);
        Assert.Equal(scope, SingleInstanceCoordinator.CreateScope("BlinkReminder", "S-1-5-21-1", 1));
        Assert.NotEqual(scope, SingleInstanceCoordinator.CreateScope("BlinkReminder", "S-1-5-21-2", 1));
        Assert.NotEqual(scope, SingleInstanceCoordinator.CreateScope("BlinkReminder", "S-1-5-21-1", 2));
    }

    [Fact]
    public async Task SecondInstanceForwardsAnAllowedCommandAndDoesNotOwnTheLease()
    {
        string id = "BlinkReminder.Tests." + Guid.NewGuid().ToString("N");
        await using var primary = SingleInstanceCoordinator.TryAcquire(id);
        var received = new TaskCompletionSource<InstanceCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.CommandReceived += (_, command) => received.TrySetResult(command);
        await using var secondary = SingleInstanceCoordinator.TryAcquire(id);
        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        Assert.True(await secondary.SendAsync(InstanceCommand.OpenSettings));
        Assert.Equal(InstanceCommand.OpenSettings, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(await secondary.SendAsync((InstanceCommand)255));
    }

    [Fact]
    public async Task LeaseCanBeReacquiredAfterShutdown()
    {
        string id = "BlinkReminder.Tests." + Guid.NewGuid().ToString("N");
        var first = SingleInstanceCoordinator.TryAcquire(id);
        Assert.True(first.IsPrimary);
        await first.DisposeAsync();
        await using var next = SingleInstanceCoordinator.TryAcquire(id);
        Assert.True(next.IsPrimary);
    }

    [Fact]
    public void NativeWindowStylesExcludeActivationAndEnableLayeredClickThrough()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var window = new TestWindow();
            try
            {
                window.CreateHandle(new System.Windows.Forms.CreateParams { Style = unchecked((int)0x80000000) });
                nint foreground = GetForegroundWindow();
                PassiveWindow.Configure(window.Handle);
                long style = GetWindowLongPtr(window.Handle, -20).ToInt64();
                Assert.Equal(0x080800a0L, style & 0x080800a0L);
                Assert.Equal(0L, style & 0x40000L);
                PassiveWindow.Position(window.Handle, new PixelRect(20, 20, 300, 100));
                Assert.Equal(foreground, GetForegroundWindow());
                bool handled = false;
                Assert.Equal((nint)3, PassiveWindow.HandleMessage(0x0021, ref handled));
                Assert.True(handled);
            }
            catch (Exception ex) { failure = ex; }
            finally { window.DestroyHandle(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class TestWindow : System.Windows.Forms.NativeWindow { }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
}
