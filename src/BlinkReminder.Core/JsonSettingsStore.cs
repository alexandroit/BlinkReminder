using System.Text.Json;

namespace BlinkReminder.Core;

public sealed record SettingsLoadResult(AppSettings Settings, bool RecoveredFromCorruption = false,
    string? BackupPath = null, string? Warning = null);

public interface ISettingsStore
{
    SettingsLoadResult Load();
    void Save(AppSettings settings);
    AppSettings Import(string source);
    void Export(string destination, AppSettings settings);
    void Reset();
}

/// <summary>The caller resolves its own EXE or MSIX user directory; this class has no platform assumptions.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    public const int MaximumFileBytes = 256 * 1024;
    private readonly object gate = new();
    private readonly string directory;
    public string FilePath { get; }

    public JsonSettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
        FilePath = Path.Combine(this.directory, "settings.json");
    }

    public SettingsLoadResult Load()
    {
        lock (gate)
        {
            if (!File.Exists(FilePath)) return new(new AppSettings());
            try { return new(Read(FilePath)); }
            catch (Exception error) when (error is JsonException or SettingsValidationException)
            {
                // Keep one previous bad file for recovery without accumulating personal settings indefinitely.
                var backup = Path.Combine(directory, "settings.corrupt.json");
                try { File.Move(FilePath, backup, overwrite: true); }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
                {
                    return new(new AppSettings(), true, null, "Invalid settings could not be backed up. Defaults are in use.");
                }
                return new(new AppSettings(), true, backup, "Invalid settings were backed up. Defaults are in use.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return new(new AppSettings(), false, null, "Settings could not be read. Defaults are in use.");
            }
        }
    }

    public void Save(AppSettings settings)
    {
        var validated = SettingsValidator.Clone(settings);
        var content = JsonSerializer.SerializeToUtf8Bytes(validated, SettingsValidator.JsonOptions);
        lock (gate) AtomicFile.Write(FilePath, content);
    }

    /// <summary>Validates an explicit import without changing the saved or running preferences.</summary>
    public AppSettings Import(string source) => Read(source);

    public void Export(string destination, AppSettings settings)
    {
        var validated = SettingsValidator.Clone(settings);
        AtomicFile.Write(destination, JsonSerializer.SerializeToUtf8Bytes(validated, SettingsValidator.JsonOptions));
    }

    public void Reset() => Save(new AppSettings());

    private static AppSettings Read(string path)
    {
        var bytes = AtomicFile.ReadBounded(path, MaximumFileBytes);
        var settings = JsonSerializer.Deserialize<AppSettings>(bytes, SettingsValidator.JsonOptions)
            ?? throw new SettingsValidationException("Settings must be an object.");
        SettingsValidator.Validate(settings);
        return settings;
    }
}

internal static class AtomicFile
{
    internal static byte[] ReadBounded(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes) throw new SettingsValidationException("The file exceeds the size limit.");
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int count;
        while ((count = stream.Read(chunk)) > 0)
        {
            if (buffer.Length + count > maximumBytes) throw new SettingsValidationException("The file exceeds the size limit.");
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    internal static void Write(string destination, ReadOnlySpan<byte> content)
    {
        var fullPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
