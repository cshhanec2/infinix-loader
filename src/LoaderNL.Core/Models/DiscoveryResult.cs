namespace LoaderNL.Core.Models;

public sealed record DiscoveryResult(
    string? SteamExecutable,
    string? GameExecutable,
    string SteamSource,
    string GameSource,
    IReadOnlyList<string> SteamLibraries,
    string? AppManifestPath,
    string? InstalledBetaKey,
    string? RequiredBetaKey)
{
    public bool SteamFound => File.Exists(SteamExecutable);
    public bool GameFound => File.Exists(GameExecutable);
    public bool IsReady => SteamFound && GameFound;
    public bool BranchConfirmed =>
        string.IsNullOrWhiteSpace(RequiredBetaKey) ||
        string.Equals(
            InstalledBetaKey,
            RequiredBetaKey,
            StringComparison.OrdinalIgnoreCase);
}
