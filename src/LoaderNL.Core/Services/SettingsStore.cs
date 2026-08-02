using System.Text.Json;
using LoaderNL.Core.Models;

namespace LoaderNL.Core.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public SettingsStore(string? settingsDirectory = null)
    {
        SettingsDirectory = settingsDirectory ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LoaderNL");
        SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");
    }

    public string SettingsDirectory { get; }
    public string SettingsFilePath { get; }

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new LauncherSettings();
        }

        await using var stream = File.OpenRead(SettingsFilePath);
        return await JsonSerializer.DeserializeAsync<LauncherSettings>(
                   stream,
                   SerializerOptions,
                   cancellationToken)
               ?? new LauncherSettings();
    }

    public async Task SaveAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(SettingsDirectory);

        await using var stream = File.Create(SettingsFilePath);
        await JsonSerializer.SerializeAsync(
            stream,
            settings,
            SerializerOptions,
            cancellationToken);
    }
}
