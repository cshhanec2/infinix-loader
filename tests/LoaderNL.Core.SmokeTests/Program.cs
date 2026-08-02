using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using LoaderNL.Core.Models;
using LoaderNL.Core.Parsing;
using LoaderNL.Core.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Valve KeyValues parser", TestValveKeyValuesAsync),
    ("Launch profiles", TestLaunchProfilesAsync),
    ("Settings round-trip", TestSettingsRoundTripAsync),
    ("CS2 Legacy discovery", TestCs2LegacyDiscoveryAsync),
    ("CS2 Legacy ignores stale fallback", TestCs2LegacyRejectsStaleFallbackAsync),
    ("CS2 Legacy requires mounted beta", TestCs2LegacyRequiresMountedBetaAsync),
    ("Standalone CS:GO discovery", TestStandaloneCsgoDiscoveryAsync),
    ("Neverlose profile seeds missing cache", TestNeverloseProfileSeedAsync),
    ("Neverlose profile preserves existing data", TestNeverloseProfilePreservesDataAsync),
    ("Neverlose libraries seed archive", TestNeverloseLibrariesSeedAsync),
    ("Neverlose libraries update managed files", TestNeverloseLibrariesUpdateAsync),
    ("Neverlose libraries reject path traversal", TestNeverloseLibrariesRejectTraversalAsync),
};

var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Smoke-test failures:");

    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine();
Console.WriteLine($"All {tests.Length} smoke tests passed.");
return 0;

static Task TestValveKeyValuesAsync()
{
    const string source =
        """
        // Steam library test data
        "libraryfolders"
        {
            "0"
            {
                "path" "C:\\Program Files (x86)\\Steam"
            }
            "1"
            {
                "path" "D:\\SteamLibrary"
            }
        }
        """;

    var libraries = ValveKeyValues.FindValues(source, "path").ToArray();

    Assert(libraries.Length == 2, "Expected two library paths.");
    Assert(libraries[1] == @"D:\SteamLibrary", "Escaped path was parsed incorrectly.");
    return Task.CompletedTask;
}

static Task TestLaunchProfilesAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var steamExecutable = Path.Combine(root, "steam.exe");
        var legacyExecutable = Path.Combine(root, "legacy", "csgo.exe");
        var standaloneExecutable = Path.Combine(root, "standalone", "csgo.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyExecutable)!);
        Directory.CreateDirectory(Path.GetDirectoryName(standaloneExecutable)!);
        File.WriteAllBytes(steamExecutable, []);
        File.WriteAllBytes(legacyExecutable, []);
        File.WriteAllBytes(standaloneExecutable, []);

        var legacy = GameProfile.CounterStrike2Legacy;
        var legacyPlan = GameLauncher.BuildLaunchPlan(
            new DiscoveryResult(
                steamExecutable,
                legacyExecutable,
                "test",
                "test",
                [],
                null,
                "csgo_legacy",
                "csgo_legacy"),
            legacy);

        Assert(legacy.SteamAppId == 730, "CS2 Legacy must use Steam AppID 730.");
        Assert(
            legacyPlan.FileName == legacyExecutable,
            "CS2 Legacy must launch the exact csgo.exe found in the beta installation.");
        Assert(
            legacyPlan.Arguments == "-insecure -steam",
            "Unexpected CS2 Legacy launch arguments.");
        Assert(
            legacyPlan.LaunchViaDesktopShell,
            "CS2 Legacy must launch through the non-elevated desktop shell.");
        Assert(
            !legacyPlan.Arguments.Contains("-applaunch", StringComparison.Ordinal),
            "CS2 Legacy must not let Steam select the default App 730 executable.");

        var standalone = GameProfile.CounterStrikeGlobalOffensiveStandalone;
        var standalonePlan = GameLauncher.BuildLaunchPlan(
            new DiscoveryResult(
                steamExecutable,
                standaloneExecutable,
                "test",
                "test",
                [],
                null,
                null,
                null),
            standalone);

        Assert(
            standalonePlan.FileName == steamExecutable,
            "Standalone CS:GO must be launched through Steam.");
        Assert(
            standalonePlan.Arguments ==
            "-applaunch 4465480 -steam -worldwide -insecure",
            "Unexpected standalone CS:GO Steam arguments.");
        Assert(
            !standalonePlan.LaunchViaDesktopShell,
            "Standalone CS:GO must retain the normal Steam launch path.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static async Task TestSettingsRoundTripAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var store = new SettingsStore(root);
        var expected = new LauncherSettings
        {
            ManualSteamExecutable = @"C:\Steam\steam.exe",
            ManualGameExecutable = @"D:\CSGO\csgo.exe",
            SelectedGameId =
                GameProfile.CounterStrikeGlobalOffensiveStandalone.Id,
        };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert(
            actual.ManualSteamExecutable == expected.ManualSteamExecutable,
            "Steam path did not round-trip.");
        Assert(
            actual.ManualGameExecutable == expected.ManualGameExecutable,
            "Game path did not round-trip.");
        Assert(
            actual.SelectedGameId == expected.SelectedGameId,
            "Game profile did not round-trip.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestCs2LegacyDiscoveryAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var steamExecutable = Path.Combine(root, "steam.exe");
        var steamApps = Path.Combine(root, "steamapps");
        var gameDirectory = Path.Combine(
            steamApps,
            "common",
            "Counter-Strike Global Offensive");
        var executable = Path.Combine(gameDirectory, "csgo.exe");
        var unrelatedExecutable = Path.Combine(root, "manual", "csgo.exe");

        Directory.CreateDirectory(gameDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(unrelatedExecutable)!);
        File.WriteAllBytes(steamExecutable, []);
        File.WriteAllBytes(executable, []);
        File.WriteAllBytes(unrelatedExecutable, []);
        File.WriteAllText(
            Path.Combine(steamApps, "appmanifest_730.acf"),
            """
            "AppState"
            {
                "appid"      "730"
                "installdir" "Counter-Strike Global Offensive"
                "UserConfig"
                {
                    "BetaKey" "csgo_legacy"
                }
                "MountedConfig"
                {
                    "BetaKey" "csgo_legacy"
                }
            }
            """);

        var logDirectory = Path.Combine(root, "logs");
        var locator = new SteamLocator(new LogService(logDirectory));
        var result = await locator.DiscoverAsync(
            GameProfile.CounterStrike2Legacy,
            new LauncherSettings
            {
                ManualSteamExecutable = steamExecutable,
                ManualGameExecutable = unrelatedExecutable,
            });

        Assert(result.GameExecutable == executable, "csgo.exe was not discovered.");
        Assert(
            result.GameExecutable != unrelatedExecutable,
            "A beta profile must ignore an unrelated manually selected csgo.exe.");
        Assert(result.BranchConfirmed, "csgo_legacy beta key was not confirmed.");
        Assert(result.IsReady, "Synthetic legacy installation should be ready.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestCs2LegacyRejectsStaleFallbackAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var steamExecutable = Path.Combine(root, "steam.exe");
        var steamApps = Path.Combine(root, "steamapps");
        var staleDirectory = Path.Combine(
            steamApps,
            "common",
            "Counter-Strike Global Offensive");

        Directory.CreateDirectory(staleDirectory);
        File.WriteAllBytes(steamExecutable, []);
        File.WriteAllBytes(Path.Combine(staleDirectory, "csgo.exe"), []);
        File.WriteAllText(
            Path.Combine(steamApps, "appmanifest_730.acf"),
            """
            "AppState"
            {
                "appid"      "730"
                "installdir" "Counter-Strike 2"
                "UserConfig" { "BetaKey" "csgo_legacy" }
                "MountedConfig" { "BetaKey" "csgo_legacy" }
            }
            """);

        var locator = new SteamLocator(
            new LogService(Path.Combine(root, "logs")));
        var result = await locator.DiscoverAsync(
            GameProfile.CounterStrike2Legacy,
            new LauncherSettings
            {
                ManualSteamExecutable = steamExecutable,
            });

        Assert(
            result.GameExecutable is null,
            "A stale csgo.exe outside the App 730 manifest directory was accepted.");
        Assert(
            !result.IsReady,
            "Legacy must stay unavailable when the manifest installation has no csgo.exe.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestCs2LegacyRequiresMountedBetaAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var steamExecutable = Path.Combine(root, "steam.exe");
        var steamApps = Path.Combine(root, "steamapps");
        var gameDirectory = Path.Combine(steamApps, "common", "legacy-transition");

        Directory.CreateDirectory(gameDirectory);
        File.WriteAllBytes(steamExecutable, []);
        File.WriteAllBytes(Path.Combine(gameDirectory, "csgo.exe"), []);
        File.WriteAllText(
            Path.Combine(steamApps, "appmanifest_730.acf"),
            """
            "AppState"
            {
                "appid"      "730"
                "installdir" "legacy-transition"
                "UserConfig" { "BetaKey" "csgo_legacy" }
                "MountedConfig" { "BetaKey" "public" }
            }
            """);

        var locator = new SteamLocator(
            new LogService(Path.Combine(root, "logs")));
        var result = await locator.DiscoverAsync(
            GameProfile.CounterStrike2Legacy,
            new LauncherSettings
            {
                ManualSteamExecutable = steamExecutable,
            });

        Assert(
            !result.BranchConfirmed,
            "Legacy was enabled before Steam mounted the csgo_legacy branch.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestStandaloneCsgoDiscoveryAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var steamExecutable = Path.Combine(root, "steam.exe");
        var steamApps = Path.Combine(root, "steamapps");
        var gameDirectory = Path.Combine(
            steamApps,
            "common",
            "csgo legacy");
        var executable = Path.Combine(gameDirectory, "csgo.exe");

        Directory.CreateDirectory(gameDirectory);
        File.WriteAllBytes(steamExecutable, []);
        File.WriteAllBytes(executable, []);
        File.WriteAllText(
            Path.Combine(steamApps, "appmanifest_4465480.acf"),
            """
            "AppState"
            {
                "appid"      "4465480"
                "name"       "Counter-Strike:Global Offensive"
                "installdir" "csgo legacy"
            }
            """);

        var logDirectory = Path.Combine(root, "logs");
        var locator = new SteamLocator(new LogService(logDirectory));
        var result = await locator.DiscoverAsync(
            GameProfile.CounterStrikeGlobalOffensiveStandalone,
            new LauncherSettings
            {
                ManualSteamExecutable = steamExecutable,
            });

        Assert(
            result.GameExecutable == executable,
            "Standalone csgo.exe was not discovered.");
        Assert(
            result.BranchConfirmed,
            "Standalone CS:GO must not require a beta branch.");
        Assert(
            result.IsReady,
            "Synthetic standalone CS:GO installation should be ready.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task TestNeverloseProfileSeedAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var gameExecutable = Path.Combine(root, "csgo.exe");
        File.WriteAllBytes(gameExecutable, []);

        var avatarBytes = CreateTestPng();
        using var avatar = new MemoryStream(avatarBytes);
        using var template = CreateGlobalDataTemplate("template-state");
        var seeder = new NeverloseProfileSeeder(
            new LogService(Path.Combine(root, "logs")));

        seeder.Prepare(gameExecutable, avatar, template, "obfuscate");

        var nlCloud = Path.Combine(root, "nl_cloud");
        Assert(
            File.ReadAllBytes(Path.Combine(nlCloud, "avatar.png"))
                .SequenceEqual(avatarBytes),
            "Seeded avatar bytes changed.");

        var globalData = ReadJsonObject(
            Path.Combine(nlCloud, "global_data.json"));
        Assert(
            globalData["username"]?.GetValue<string>() == "obfuscate",
            "Seeded username was not applied.");
        Assert(
            globalData["avatar"]?.GetValue<string>() ==
            "nl_cloud/avatar.png",
            "Seeded avatar path was not applied.");
        Assert(
            globalData["content"]?.GetValue<string>() == "template-state",
            "Template data was not preserved.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task TestNeverloseProfilePreservesDataAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var gameExecutable = Path.Combine(root, "csgo.exe");
        var nlCloud = Path.Combine(root, "nl_cloud");
        var globalDataPath = Path.Combine(nlCloud, "global_data.json");

        Directory.CreateDirectory(nlCloud);
        File.WriteAllBytes(gameExecutable, []);
        File.WriteAllText(
            globalDataPath,
            """
            {
              "avatar": "old/avatar.png",
              "content": "current-state",
              "expiration_date": 123,
              "last_config": 7,
              "last_style": 4,
              "steamid": "current-steamid",
              "username": "old-name",
              "custom_field": "keep-me"
            }
            """);

        using var avatar = new MemoryStream(CreateTestPng());
        using var template = CreateGlobalDataTemplate("template-state");
        var seeder = new NeverloseProfileSeeder(
            new LogService(Path.Combine(root, "logs")));

        seeder.Prepare(gameExecutable, avatar, template, "obfuscate");

        var globalData = ReadJsonObject(globalDataPath);
        Assert(
            globalData["username"]?.GetValue<string>() == "obfuscate",
            "Existing username was not replaced.");
        Assert(
            globalData["content"]?.GetValue<string>() == "current-state",
            "Existing content was overwritten.");
        Assert(
            globalData["last_config"]?.GetValue<int>() == 7,
            "Existing config selection was overwritten.");
        Assert(
            globalData["custom_field"]?.GetValue<string>() == "keep-me",
            "Unknown global-data fields were not preserved.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task TestNeverloseLibrariesSeedAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var gameExecutable = Path.Combine(root, "csgo.exe");
        File.WriteAllBytes(gameExecutable, []);

        using var archive = CreateLibraryArchive(
            ("clipboard.lua", "return { get = function() return '' end }"),
            ("neverlose/base64.lua", "return { encode = tostring }"),
            ("README.txt", "ignored"));
        var seeder = new NeverloseLibrarySeeder(
            new LogService(Path.Combine(root, "logs")));

        var result = seeder.Prepare(gameExecutable, archive);
        var libraries = Path.Combine(
            root,
            "nl_cloud",
            "scripts",
            "libraries");

        Assert(result.Installed == 2, "Expected two installed Lua libraries.");
        Assert(result.Updated == 0, "Fresh installation must not report updates.");
        Assert(result.Unchanged == 0, "Fresh installation must not report unchanged files.");
        Assert(
            File.Exists(Path.Combine(libraries, "clipboard.lua")),
            "Flat Lua library was not installed.");
        Assert(
            File.Exists(Path.Combine(libraries, "neverlose", "base64.lua")),
            "Nested Lua library was not installed.");
        Assert(
            !File.Exists(Path.Combine(libraries, "README.txt")),
            "Non-Lua archive entry must be ignored.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task TestNeverloseLibrariesUpdateAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var gameExecutable = Path.Combine(root, "csgo.exe");
        var libraries = Path.Combine(
            root,
            "nl_cloud",
            "scripts",
            "libraries");

        Directory.CreateDirectory(libraries);
        File.WriteAllBytes(gameExecutable, []);
        File.WriteAllText(Path.Combine(libraries, "clipboard.lua"), "old");
        File.WriteAllText(Path.Combine(libraries, "base64.lua"), "same");
        File.WriteAllText(Path.Combine(libraries, "user-library.lua"), "preserve");

        using var archive = CreateLibraryArchive(
            ("clipboard.lua", "new"),
            ("base64.lua", "same"),
            ("pui.lua", "return {}"));
        var seeder = new NeverloseLibrarySeeder(
            new LogService(Path.Combine(root, "logs")));

        var result = seeder.Prepare(gameExecutable, archive);

        Assert(result.Installed == 1, "Expected one newly installed library.");
        Assert(result.Updated == 1, "Expected one updated library.");
        Assert(result.Unchanged == 1, "Expected one unchanged library.");
        Assert(
            File.ReadAllText(Path.Combine(libraries, "clipboard.lua")) == "new",
            "Managed library was not updated.");
        Assert(
            File.ReadAllText(Path.Combine(libraries, "user-library.lua")) == "preserve",
            "Unmanaged user library was modified.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task TestNeverloseLibrariesRejectTraversalAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var gameExecutable = Path.Combine(root, "csgo.exe");
        File.WriteAllBytes(gameExecutable, []);

        using var archive = CreateLibraryArchive(
            ("valid.lua", "return true"),
            ("../escape.lua", "return false"));
        var seeder = new NeverloseLibrarySeeder(
            new LogService(Path.Combine(root, "logs")));
        var rejected = false;

        try
        {
            seeder.Prepare(gameExecutable, archive);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert(rejected, "Path traversal entry was not rejected.");
        Assert(
            !File.Exists(Path.Combine(root, "nl_cloud", "scripts", "libraries", "valid.lua")),
            "Archive was partially written before validation completed.");
        Assert(
            !File.Exists(Path.Combine(root, "nl_cloud", "scripts", "escape.lua")),
            "Path traversal entry escaped the library directory.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static byte[] CreateTestPng() =>
[
    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
    0x01, 0x02, 0x03, 0x04,
];

static MemoryStream CreateGlobalDataTemplate(string content)
{
    var json =
        $$"""
        {
          "avatar": "nl_cloud/avatar.png",
          "content": "{{content}}",
          "expiration_date": 1798722000,
          "last_config": 3,
          "last_style": 0,
          "steamid": "76561198403727043",
          "username": "template-name"
        }
        """;

    return new MemoryStream(Encoding.UTF8.GetBytes(json));
}

static MemoryStream CreateLibraryArchive(
    params (string Path, string Contents)[] files)
{
    var stream = new MemoryStream();

    using (var archive = new ZipArchive(
               stream,
               ZipArchiveMode.Create,
               leaveOpen: true))
    {
        foreach (var file in files)
        {
            var entry = archive.CreateEntry(file.Path);
            using var writer = new StreamWriter(
                entry.Open(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(file.Contents);
        }
    }

    stream.Position = 0;
    return stream;
}

static JsonObject ReadJsonObject(string path) =>
    JsonNode.Parse(File.ReadAllBytes(path)) as JsonObject ??
    throw new InvalidOperationException($"{path} is not a JSON object.");

static string CreateTemporaryDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), $"LoaderNL.Tests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
