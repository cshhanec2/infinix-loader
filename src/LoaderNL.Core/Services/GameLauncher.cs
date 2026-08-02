using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using LoaderNL.Core.Models;

namespace LoaderNL.Core.Services;

public sealed class GameLauncher(LogService logService)
{
    private readonly LogService _logService = logService;

    public static string BuildGameArguments(GameProfile game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.LaunchArguments;
    }

    public static string BuildSteamArguments(GameProfile game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return $"-applaunch {game.SteamAppId} {BuildGameArguments(game)}";
    }

    public static GameLaunchPlan BuildLaunchPlan(
        DiscoveryResult discovery,
        GameProfile game)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(game);

        if (!discovery.IsReady || !discovery.BranchConfirmed)
        {
            throw new InvalidOperationException(
                $"Вариант {game.DisplayName} сейчас недоступен.");
        }

        if (game.LaunchDiscoveredExecutable)
        {
            var gameExecutable = discovery.GameExecutable!;
            if (!string.Equals(
                    Path.GetFileName(gameExecutable),
                    game.ExecutableName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Для {game.DisplayName} найден неверный исполняемый файл.");
            }

            return new GameLaunchPlan(
                gameExecutable,
                BuildGameArguments(game),
                Path.GetDirectoryName(gameExecutable) ?? string.Empty,
                LaunchViaDesktopShell: true);
        }

        var steamExecutable = discovery.SteamExecutable!;
        return new GameLaunchPlan(
            steamExecutable,
            BuildSteamArguments(game),
            Path.GetDirectoryName(steamExecutable) ?? string.Empty,
            LaunchViaDesktopShell: false);
    }

    public static string BuildCommandPreview(
        GameProfile game,
        DiscoveryResult? discovery)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (game.LaunchDiscoveredExecutable)
        {
            var executable = string.IsNullOrWhiteSpace(discovery?.GameExecutable)
                ? game.ExecutableName
                : discovery.GameExecutable;
            return $"\"{executable}\" {BuildGameArguments(game)}";
        }

        var steamExecutable = string.IsNullOrWhiteSpace(discovery?.SteamExecutable)
            ? "steam.exe"
            : discovery.SteamExecutable;
        return $"\"{steamExecutable}\" {BuildSteamArguments(game)}";
    }

    public Task LaunchAsync(
        DiscoveryResult discovery,
        GameProfile game,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = BuildLaunchPlan(discovery, game);

        try
        {
            if (plan.LaunchViaDesktopShell)
            {
                LaunchUsingDesktopShell(plan);
                _logService.Success(
                    $"Запущен точный исполняемый файл {game.DisplayName}: " +
                    $"{plan.FileName} {plan.Arguments}");
            }
            else
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = plan.FileName,
                    Arguments = plan.Arguments,
                    WorkingDirectory = plan.WorkingDirectory,
                    UseShellExecute = true
                }) ?? throw new InvalidOperationException(
                    "Steam не вернул информацию о процессе запуска.");

                _logService.Success(
                    $"Команда запуска {game.DisplayName} передана Steam: " +
                    plan.Arguments);
            }

            return Task.CompletedTask;
        }
        catch (Win32Exception exception)
        {
            _logService.Error(
                $"Не удалось запустить {game.DisplayName}: " +
                $"{exception.NativeErrorCode} — {exception.Message}");
            throw;
        }
        catch (TargetInvocationException exception)
        {
            var actualException = exception.InnerException ?? exception;
            _logService.Error(
                $"Не удалось запустить {game.DisplayName}: {actualException.Message}");
            throw actualException;
        }
    }

    private static void LaunchUsingDesktopShell(GameLaunchPlan plan)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application") ??
            throw new InvalidOperationException(
                "Системная оболочка Windows недоступна.");
        object? shell = null;

        try
        {
            shell = Activator.CreateInstance(shellType) ??
                throw new InvalidOperationException(
                    "Не удалось создать системную оболочку Windows.");
            shellType.InvokeMember(
                "ShellExecute",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args:
                [
                    plan.FileName,
                    plan.Arguments,
                    plan.WorkingDirectory,
                    "open",
                    1
                ]);
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
