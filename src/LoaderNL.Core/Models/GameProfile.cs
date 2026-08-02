namespace LoaderNL.Core.Models;

public sealed record GameProfile(
    string Id,
    string DisplayName,
    uint SteamAppId,
    IReadOnlyList<string> ExecutableCandidates,
    string LaunchArguments,
    string? RequiredBetaKey = null,
    bool LaunchDiscoveredExecutable = false)
{
    public static GameProfile CounterStrike2Legacy { get; } = new(
        "cs2-legacy",
        "CS2 Legacy",
        730,
        [
            "csgo.exe"
        ],
        "-insecure -steam",
        "csgo_legacy",
        LaunchDiscoveredExecutable: true);

    public static GameProfile CounterStrikeGlobalOffensiveStandalone { get; } = new(
        "csgo-standalone",
        "CS:GO",
        4465480,
        [
            "csgo.exe"
        ],
        "-steam -worldwide -insecure");

    public static GameProfile CounterStrikeGlobalOffensiveLegacy =>
        CounterStrike2Legacy;

    public string ExecutableName => Path.GetFileName(ExecutableCandidates[0]);

    public static IReadOnlyList<GameProfile> Available { get; } =
        [
            CounterStrike2Legacy,
            CounterStrikeGlobalOffensiveStandalone
        ];
}
