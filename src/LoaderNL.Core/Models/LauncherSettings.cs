namespace LoaderNL.Core.Models;

public sealed record LauncherSettings
{
    public string? ManualSteamExecutable { get; init; }
    public string? ManualGameExecutable { get; init; }
    public string SelectedGameId { get; init; } =
        GameProfile.CounterStrikeGlobalOffensiveStandalone.Id;
}
