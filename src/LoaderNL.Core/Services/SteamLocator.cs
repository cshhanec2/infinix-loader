using Microsoft.Win32;
using LoaderNL.Core.Models;
using LoaderNL.Core.Parsing;

namespace LoaderNL.Core.Services;

public sealed class SteamLocator(LogService logService)
{
    private readonly LogService _logService = logService;

    public Task<DiscoveryResult> DiscoverAsync(
        GameProfile game,
        LauncherSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(settings);

        return Task.Run(
            () => Discover(game, settings, cancellationToken),
            cancellationToken);
    }

    public string? FindGameExecutable(
        GameProfile game,
        IEnumerable<string> libraryRoots,
        CancellationToken cancellationToken = default)
    {
        foreach (var libraryRoot in libraryRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var steamApps = Path.Combine(libraryRoot, "steamapps");
            var manifestPath = Path.Combine(steamApps, $"appmanifest_{game.SteamAppId}.acf");

            if (File.Exists(manifestPath))
            {
                var manifest = File.ReadAllText(manifestPath);
                var installDirectory = ValveKeyValues.FindValues(manifest, "installdir").FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(installDirectory))
                {
                    var gameRoot = Path.Combine(steamApps, "common", installDirectory);
                    var manifestCandidate = FindExecutableCandidate(game, gameRoot);
                    if (manifestCandidate is not null)
                    {
                        return manifestCandidate;
                    }
                }
            }

        }

        return null;
    }

    private DiscoveryResult Discover(
        GameProfile game,
        LauncherSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manualSteam = NormalizeExistingFile(settings.ManualSteamExecutable, "steam.exe");
        var steamExecutable = manualSteam ?? FindSteamExecutable();
        var steamSource = manualSteam is not null ? "Ручной путь" :
            steamExecutable is not null ? "Автоопределение" : "Не найден";

        var libraryRoots = steamExecutable is null
            ? Array.Empty<string>()
            : FindLibraryRoots(Path.GetDirectoryName(steamExecutable)!, cancellationToken).ToArray();

        var manualGame = game.RequiredBetaKey is null
            ? NormalizeExistingFile(
                settings.ManualGameExecutable,
                game.ExecutableName)
            : null;
        var gameExecutable = manualGame ??
            FindGameExecutable(game, libraryRoots, cancellationToken);
        var gameSource = manualGame is not null ? "Ручной путь" :
            gameExecutable is not null ? "Steam manifest" : "Не найдена";
        var appManifestPath = FindAppManifest(
            game.SteamAppId,
            libraryRoots,
            cancellationToken);
        var installedBetaKey = ReadBetaKey(appManifestPath);

        _logService.Info(
            steamExecutable is null
                ? "Steam не найден автоматически."
                : $"Steam найден: {steamExecutable}");
        _logService.Info(
            gameExecutable is null
                ? $"{game.DisplayName} не найдена автоматически."
                : $"{game.DisplayName} найдена: {gameExecutable}");
        if (!string.IsNullOrWhiteSpace(game.RequiredBetaKey))
        {
            _logService.Info(
                string.Equals(
                    installedBetaKey,
                    game.RequiredBetaKey,
                    StringComparison.OrdinalIgnoreCase)
                    ? $"Подтверждена beta-ветка: {installedBetaKey}."
                    : $"В manifest не подтверждена ветка {game.RequiredBetaKey}; " +
                      $"текущее значение: {installedBetaKey ?? "default"}.");
        }

        return new DiscoveryResult(
            steamExecutable,
            gameExecutable,
            steamSource,
            gameSource,
            libraryRoots,
            appManifestPath,
            installedBetaKey,
            game.RequiredBetaKey);
    }

    private static string? FindSteamExecutable()
    {
        var registryCandidates = new[]
        {
            ReadRegistryValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamExe"),
            ReadRegistryValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath")
        };

        foreach (var candidate in registryCandidates)
        {
            var executable = ResolveSteamExecutable(candidate);
            if (executable is not null)
            {
                return executable;
            }
        }

        var defaultCandidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam",
                "steam.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Steam",
                "steam.exe")
        };

        return defaultCandidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> FindLibraryRoots(
        string steamRoot,
        CancellationToken cancellationToken)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(steamRoot)
        };

        var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFile))
        {
            return roots;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var content = File.ReadAllText(libraryFile);

        foreach (var path in ValveKeyValues.FindValues(content, "path"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                roots.Add(Path.GetFullPath(path));
            }
        }

        return roots;
    }

    private static string? FindExecutableCandidate(GameProfile game, string gameRoot)
    {
        foreach (var relativePath in game.ExecutableCandidates)
        {
            var candidate = Path.Combine(gameRoot, relativePath);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static string? FindAppManifest(
        uint appId,
        IEnumerable<string> libraryRoots,
        CancellationToken cancellationToken)
    {
        foreach (var libraryRoot in libraryRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(
                libraryRoot,
                "steamapps",
                $"appmanifest_{appId}.acf");
            if (File.Exists(manifestPath))
            {
                return Path.GetFullPath(manifestPath);
            }
        }

        return null;
    }

    private static string? ReadBetaKey(string? appManifestPath)
    {
        if (string.IsNullOrWhiteSpace(appManifestPath) ||
            !File.Exists(appManifestPath))
        {
            return null;
        }

        try
        {
            var manifest = File.ReadAllText(appManifestPath);
            var betaKeys = ValveKeyValues
                .FindValues(manifest, "BetaKey")
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return betaKeys.Length switch
            {
                0 => null,
                1 => betaKeys[0],
                _ => string.Join(", ", betaKeys)
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadRegistryValue(RegistryKey root, string keyPath, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(keyPath);
            return key?.GetValue(valueName) as string;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ResolveSteamExecutable(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalized = candidate.Replace('/', Path.DirectorySeparatorChar);
        if (File.Exists(normalized) &&
            string.Equals(Path.GetFileName(normalized), "steam.exe", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(normalized);
        }

        var combined = Path.Combine(normalized, "steam.exe");
        return File.Exists(combined) ? Path.GetFullPath(combined) : null;
    }

    private static string? NormalizeExistingFile(string? candidate, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
        {
            return null;
        }

        if (!string.Equals(
                Path.GetFileName(candidate),
                expectedName,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetFullPath(candidate);
    }
}
