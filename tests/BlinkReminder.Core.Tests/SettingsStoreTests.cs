using System.Text.Json;
using BlinkReminder.Core;

namespace BlinkReminder.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "BlinkReminder-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ValidSettingsRoundTripWithoutTemporaryFiles()
    {
        var store = new JsonSettingsStore(directory);
        var settings = new AppSettings { OnboardingCompleted = true, Language = "fr-CA" };
        settings.Schedule.QuietPeriods.Add(new() { Start = new(23, 0), End = new(1, 0) });
        store.Save(settings);
        settings.Blink.IntervalMinutes = 45;
        store.Save(settings);
        var loaded = store.Load();
        Assert.False(loaded.RecoveredFromCorruption);
        Assert.Equal("fr-CA", loaded.Settings.Language);
        Assert.Equal(45, loaded.Settings.Blink.IntervalMinutes);
        Assert.Equal(new TimeOnly(23, 0), Assert.Single(loaded.Settings.Schedule.QuietPeriods).Start);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":99}")]
    [InlineData("{\"schemaVersion\":1,\"unknown\":true}")]
    [InlineData("{\"schemaVersion\":1,\"blink\":null}")]
    [InlineData("{\"schemaVersion\":1,\"schedule\":{\"days\":[\"Monday\",\"Monday\"]}}")]
    [InlineData("{\"schemaVersion\":1,\"blink\":{\"intervalMinutes\":0}}")]
    [InlineData("{\"schemaVersion\":1,\"appearance\":{\"theme\":128}}")]
    [InlineData("{}")]
    public void CorruptOrUnsupportedSettingsAreBackedUpAndRecoverToSafeDefaults(string json)
    {
        Directory.CreateDirectory(directory);
        var store = new JsonSettingsStore(directory);
        File.WriteAllText(store.FilePath, json);
        var loaded = store.Load();
        Assert.True(loaded.RecoveredFromCorruption);
        Assert.False(loaded.Settings.OnboardingCompleted);
        Assert.Equal(json, File.ReadAllText(loaded.BackupPath!));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void ImportValidationDoesNotChangeSavedSettings()
    {
        var store = new JsonSettingsStore(directory);
        store.Save(new AppSettings());
        var previous = File.ReadAllText(store.FilePath);
        var path = Path.Combine(directory, "import.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"de\"}");
        Assert.Throws<SettingsValidationException>(() => store.Import(path));
        Assert.Equal(previous, File.ReadAllText(store.FilePath));
        File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"en\"}");
        Assert.Equal("en", store.Import(path).Language);
        Assert.Equal(previous, File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void OversizedImportIsRejectedBeforeParsing()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "import.json");
        File.WriteAllBytes(path, new byte[JsonSettingsStore.MaximumFileBytes + 1]);
        Assert.Throws<SettingsValidationException>(() => new JsonSettingsStore(directory).Import(path));
    }

    [Fact]
    public void InvalidSavePreservesPreviousFile()
    {
        var store = new JsonSettingsStore(directory);
        store.Save(new AppSettings());
        var previous = File.ReadAllText(store.FilePath);
        var settings = new AppSettings();
        settings.Blink.Message = "";
        Assert.Throws<SettingsValidationException>(() => store.Save(settings));
        Assert.Equal(previous, File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void ExportAndResetAffectOnlyExplicitUserSettings()
    {
        var store = new JsonSettingsStore(directory);
        var settings = new AppSettings { Language = "en", OnboardingCompleted = true };
        var export = Path.Combine(directory, "selected-export.json");
        store.Export(export, settings);
        Assert.Equal("en", store.Import(export).Language);
        Assert.False(File.Exists(store.FilePath));
        store.Save(settings);
        store.Reset();
        Assert.False(store.Load().Settings.OnboardingCompleted);
        Assert.True(File.Exists(export));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1441)]
    public void InvalidIntervalsCannotEnterScheduler(int interval)
    {
        var settings = new AppSettings();
        settings.Blink.IntervalMinutes = interval;
        Assert.Throws<SettingsValidationException>(() => new ReminderScheduler(settings));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
