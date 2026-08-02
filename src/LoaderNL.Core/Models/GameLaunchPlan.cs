namespace LoaderNL.Core.Models;

public sealed record GameLaunchPlan(
    string FileName,
    string Arguments,
    string WorkingDirectory,
    bool LaunchViaDesktopShell);
