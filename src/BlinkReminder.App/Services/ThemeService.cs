using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using BlinkReminder.Core;

namespace BlinkReminder.App.Services;

public static class ThemeService
{
    public static void Apply(AppTheme preference, ResourceDictionary? target = null)
    {
        var resources = target ?? System.Windows.Application.Current.Resources;
        bool dark = preference == AppTheme.Dark || preference == AppTheme.System && IsSystemDark();
        bool contrast = SystemParameters.HighContrast;
        resources["WindowBrush"] = contrast ? SystemColors.WindowBrush : Brush(dark ? "#151A24" : "#F3F5F8");
        resources["SurfaceBrush"] = contrast ? SystemColors.WindowBrush : Brush(dark ? "#202735" : "#FFFFFF");
        resources["TextBrush"] = contrast ? SystemColors.WindowTextBrush : Brush(dark ? "#F3F6FC" : "#192438");
        resources["MutedBrush"] = contrast ? SystemColors.WindowTextBrush : Brush(dark ? "#B8C5D8" : "#46556D");
        resources["BorderBrush"] = contrast ? SystemColors.WindowTextBrush : Brush(dark ? "#526078" : "#BAC6D6");
        resources["AccentBrush"] = contrast ? SystemColors.HighlightBrush : Brush(dark ? "#6CA9FF" : "#175BB4");
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException) { return false; }
    }
}
