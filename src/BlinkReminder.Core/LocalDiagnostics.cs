using System.Text;

namespace BlinkReminder.Core;

/// <summary>Bounded local event codes only: no exception text, application names, window titles, or input history.</summary>
public sealed class LocalDiagnostics(string directory, TimeProvider? timeProvider = null)
{
    public const int MaximumLogBytes = 64 * 1024;
    private readonly object gate = new();
    private readonly string filePath = Path.Combine(Path.GetFullPath(directory), "diagnostics.log");
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public void Write(string eventCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventCode);
        if (eventCode.Length > 64 || eventCode.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-'))
            throw new ArgumentException("Diagnostics accept short event codes only.", nameof(eventCode));
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var line = $"{clock.GetUtcNow():O} {eventCode}{Environment.NewLine}";
                if (File.Exists(filePath) && new FileInfo(filePath).Length + Encoding.UTF8.GetByteCount(line) > MaximumLogBytes)
                    File.Move(filePath, filePath + ".1", overwrite: true);
                File.AppendAllText(filePath, line, new UTF8Encoding(false));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Call only after the user explicitly chooses to export diagnostics.</summary>
    public void Export(string destination)
    {
        lock (gate)
        {
            using var content = new MemoryStream();
            foreach (var path in new[] { filePath + ".1", filePath })
                if (File.Exists(path)) content.Write(AtomicFile.ReadBounded(path, MaximumLogBytes));
            AtomicFile.Write(destination, content.ToArray());
        }
    }

    public void Clear()
    {
        lock (gate)
            foreach (var path in new[] { filePath, filePath + ".1" })
                if (File.Exists(path)) File.Delete(path);
    }
}
