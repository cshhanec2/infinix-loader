using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using LoaderNL.Core.Models;
using LoaderNL.Core.Services;

namespace LoaderNL.App.ViewModels;

public enum LoaderProfile
{
    Neverlose,
    Gamesense
}

public enum GameTarget
{
    Cs2Legacy,
    CsgoStandalone
}

public sealed class MainViewModel : ObservableObject
{
    private readonly LogService _logService = new();
    private readonly SteamLocator _steamLocator;
    private readonly GameLauncher _gameLauncher;
    private readonly DllInjector _dllInjector;
    private readonly NeverloseProfileSeeder _neverloseProfileSeeder;
    private readonly NeverloseLibrarySeeder _neverloseLibrarySeeder;
    private readonly Dictionary<GameTarget, DiscoveryResult> _discoveries = [];

    private DiscoveryResult? _discovery;
    private bool _isBusy = true;
    private bool _hasLaunchError;
    private bool _hasLaunchSucceeded;
    private string _statusText = "Поиск установленных версий…";
    private LoaderProfile _selectedProfile = LoaderProfile.Neverlose;
    private GameTarget _selectedGame = GameTarget.CsgoStandalone;

    public MainViewModel()
    {
        _steamLocator = new SteamLocator(_logService);
        _gameLauncher = new GameLauncher(_logService);
        _dllInjector = new DllInjector(_logService);
        _neverloseProfileSeeder = new NeverloseProfileSeeder(_logService);
        _neverloseLibrarySeeder = new NeverloseLibrarySeeder(_logService);

        LaunchCommand = new AsyncRelayCommand(
            LaunchAsync,
            () => IsReady && !IsBusy);
        RefreshCommand = new AsyncRelayCommand(
            InitializeAsync,
            () => !IsBusy);
    }

    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public LoaderProfile SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                HasLaunchError = false;
                HasLaunchSucceeded = false;
                OnPropertyChanged(nameof(ProfileDisplayName));
                OnPropertyChanged(nameof(ProfileCode));
                OnPropertyChanged(nameof(ProfileSummaryText));

                if (!IsBusy)
                {
                    SetStatus(
                        BuildStatusText(SelectedGameProfile, _discovery),
                        isError: _discovery is not null && !IsReady);
                }
            }
        }
    }

    public GameTarget SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                ApplySelectedGame();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(LaunchButtonText));
                OnPropertyChanged(nameof(StatusCaptionText));
                LaunchCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsReady =>
        _discovery?.IsReady == true &&
        _discovery.BranchConfirmed;

    public bool IsSteamReady => _discovery?.SteamFound == true;

    public bool IsGameReady =>
        _discovery?.GameFound == true &&
        _discovery.BranchConfirmed;

    public bool IsProfileReady => true;

    public bool HasLaunchError
    {
        get => _hasLaunchError;
        private set
        {
            if (SetProperty(ref _hasLaunchError, value))
            {
                OnPropertyChanged(nameof(StatusCaptionText));
            }
        }
    }

    public bool HasLaunchSucceeded
    {
        get => _hasLaunchSucceeded;
        private set
        {
            if (SetProperty(ref _hasLaunchSucceeded, value))
            {
                OnPropertyChanged(nameof(StatusCaptionText));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string TitleText =>
        SelectedGame == GameTarget.Cs2Legacy
            ? "CS2 · LEGACY"
            : "CS:GO";

    public string ProfileDisplayName =>
        SelectedProfile == LoaderProfile.Neverlose
            ? "NEVERLOSE"
            : "GAMESENSE";

    public string ProfileCode =>
        SelectedProfile == LoaderProfile.Neverlose
            ? "NL"
            : "GS";

    public string ProfileSummaryText =>
        $"{ProfileDisplayName} · PROFILE SELECTED";

    public string BuildText =>
        $"build {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(2) ?? "1.0"}";

    public string StatusCaptionText =>
        HasLaunchError
            ? "ТРЕБУЕТСЯ ДЕЙСТВИЕ"
            : HasLaunchSucceeded
                ? "ГОТОВО"
                : IsBusy
                    ? "ВЫПОЛНЕНИЕ"
                    : "СИСТЕМА ГОТОВА";

    public string CardTitleText =>
        SelectedGame == GameTarget.Cs2Legacy
            ? "Counter-Strike 2"
            : "Counter-Strike: Global Offensive";

    public string CardMetaText =>
        SelectedGame == GameTarget.Cs2Legacy
            ? "Steam · App 730 · csgo_legacy"
            : "Steam · App 4465480";

    public string LaunchButtonText =>
        IsBusy
            ? "ПОДОЖДИТЕ…"
            : SelectedGame == GameTarget.Cs2Legacy
                ? "ЗАПУСТИТЬ CS2 LEGACY"
                : "ЗАПУСТИТЬ CS:GO";

    public async Task InitializeAsync()
    {
        IsBusy = true;
        SetStatus("Поиск установленных версий…");

        try
        {
            var settings = new LauncherSettings();
            var cs2LegacyTask = _steamLocator.DiscoverAsync(
                GameProfile.CounterStrike2Legacy,
                settings);
            var csgoTask = _steamLocator.DiscoverAsync(
                GameProfile.CounterStrikeGlobalOffensiveStandalone,
                settings);

            await Task.WhenAll(cs2LegacyTask, csgoTask);

            _discoveries[GameTarget.Cs2Legacy] = await cs2LegacyTask;
            _discoveries[GameTarget.CsgoStandalone] = await csgoTask;

            if (!IsTargetReady(GameTarget.CsgoStandalone) &&
                IsTargetReady(GameTarget.Cs2Legacy))
            {
                SelectedGame = GameTarget.Cs2Legacy;
            }
            else
            {
                ApplySelectedGame();
            }
        }
        catch (Exception exception)
        {
            _discovery = null;
            SetStatus("Не удалось проверить установки Steam", isError: true);
            _logService.Error($"Ошибка поиска игр: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyGameStateChanged();
        }
    }

    private GameProfile SelectedGameProfile =>
        SelectedGame == GameTarget.Cs2Legacy
            ? GameProfile.CounterStrike2Legacy
            : GameProfile.CounterStrikeGlobalOffensiveStandalone;

    private bool IsTargetReady(GameTarget target) =>
        _discoveries.TryGetValue(target, out var result) &&
        result.IsReady &&
        result.BranchConfirmed;

    private void ApplySelectedGame()
    {
        _discoveries.TryGetValue(SelectedGame, out _discovery);

        SetStatus(
            BuildStatusText(SelectedGameProfile, _discovery),
            isError: _discovery is not null && !IsReady);
        NotifyGameStateChanged();
    }

    private static string BuildStatusText(
        GameProfile game,
        DiscoveryResult? discovery)
    {
        if (discovery is null)
        {
            return "Проверка установки…";
        }

        if (!discovery.SteamFound)
        {
            return "Steam не найден";
        }

        if (!discovery.BranchConfirmed)
        {
            return "В Steam выбери beta csgo_legacy";
        }

        if (!discovery.GameFound)
        {
            return $"{game.DisplayName} не установлена";
        }

        return $"{game.DisplayName} готова";
    }

    private void NotifyGameStateChanged()
    {
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsSteamReady));
        OnPropertyChanged(nameof(IsGameReady));
        OnPropertyChanged(nameof(IsProfileReady));
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(CardTitleText));
        OnPropertyChanged(nameof(CardMetaText));
        OnPropertyChanged(nameof(LaunchButtonText));
        OnPropertyChanged(nameof(StatusCaptionText));
        LaunchCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private async Task LaunchAsync()
    {
        if (_discovery is null)
        {
            return;
        }

        var game = SelectedGameProfile;
        IsBusy = true;

        try
        {
            if (SelectedProfile == LoaderProfile.Neverlose)
            {
                SetStatus("Подготовка профиля Neverlose…");
                PrepareNeverloseProfile(_discovery.GameExecutable!);
            }

            SetStatus($"Запускаем {game.DisplayName}…");
            await _gameLauncher.LaunchAsync(_discovery, game);

            SetStatus("Ожидание процесса игры…");
            bool processFound = await _dllInjector.WaitForProcessAsync("csgo", timeoutSeconds: 30);

            if (!processFound)
            {
                SetStatus("csgo.exe не запустился — открой Steam и повтори", isError: true);
                return;
            }

            SetStatus("Прогрузка модулей игры…");
            await Task.Delay(8000);

            bool injected;
            if (SelectedProfile == LoaderProfile.Neverlose)
            {
                SetStatus("Подключение Neverlose…");
                injected = await _dllInjector.InjectEmbeddedDllAsync("csgo", "LoaderNL.App.neverlose.dll");
            }
            else
            {
                SetStatus("Подключение Gamesense…");
                injected = await _dllInjector.InjectSkeetDllAsync("csgo", "LoaderNL.App.skeet.dll");
            }

            SetStatus(
                injected
                    ? $"{ProfileDisplayName} успешно подключён"
                    : $"Не удалось подключить {ProfileDisplayName}",
                isError: !injected,
                isSuccess: injected);
        }
        catch (Exception exception)
        {
            SetStatus($"Ошибка запуска {game.DisplayName}", isError: true);
            _logService.Error($"Ошибка LaunchAsync: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyGameStateChanged();
        }
    }

    private void SetStatus(
        string text,
        bool isError = false,
        bool isSuccess = false)
    {
        StatusText = text;
        HasLaunchError = isError;
        HasLaunchSucceeded = isSuccess;
    }

    private void PrepareNeverloseProfile(string gameExecutable)
    {
        var assembly = Assembly.GetEntryAssembly() ??
                       Assembly.GetExecutingAssembly();

        using var avatar = assembly.GetManifestResourceStream(
            "LoaderNL.App.neverlose-avatar.png") ??
            throw new InvalidOperationException(
                "Встроенный аватар Neverlose не найден.");
        using var globalDataTemplate = assembly.GetManifestResourceStream(
            "LoaderNL.App.neverlose-global-data.json") ??
            throw new InvalidOperationException(
                "Встроенный шаблон global_data.json не найден.");
        using var libraries = assembly.GetManifestResourceStream(
            "LoaderNL.App.neverlose-libraries.zip") ??
            throw new InvalidOperationException(
                "Встроенный архив библиотек Neverlose не найден.");

        _neverloseProfileSeeder.Prepare(
            gameExecutable,
            avatar,
            globalDataTemplate,
            "infinix");
        _neverloseLibrarySeeder.Prepare(
            gameExecutable,
            libraries);
    }
}
