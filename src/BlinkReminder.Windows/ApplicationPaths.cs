using System.Security.Principal;

namespace BlinkReminder.Windows;

public sealed record ProfilePaths(bool IsPackaged, string SettingsDirectory, string DiagnosticDirectory);

public static class ApplicationPaths
{
    public static bool HasPackageIdentity()
    {
        uint length = 0;
        int result = NativeMethods.GetCurrentPackageFullName(ref length, null);
        if (result == 15700) return false; // APPMODEL_ERROR_NO_PACKAGE
        if (result == 122) return true; // ERROR_INSUFFICIENT_BUFFER: a package name exists
        throw new System.ComponentModel.Win32Exception(result);
    }

    public static ProfilePaths Resolve()
    {
        bool packaged = HasPackageIdentity();
        string directory = packaged
            ? global::Windows.Storage.ApplicationData.Current.LocalFolder.Path
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlinkReminder");
        return Resolve(packaged, directory);
    }

    // Accept a known root, never an imported settings path. Useful for isolated filesystem tests.
    public static ProfilePaths Resolve(bool packaged, string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        if (!Path.IsPathFullyQualified(profileDirectory)) throw new ArgumentException("profile_path_not_absolute", nameof(profileDirectory));
        string directory = Path.GetFullPath(profileDirectory);
        return new(packaged, directory, Path.Combine(directory, "Diagnostics"));
    }
}

public static class ProcessSecurity
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
