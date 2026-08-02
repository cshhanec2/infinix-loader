using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LoaderNL.App.ViewModels;

namespace LoaderNL.App;

public partial class MainWindow : Window
{
    private const string DiscordUrl = "https://discord.gg/infinixleague";
    private const double FinalShellWidth = 496;
    private const double FinalShellHeight = 406;
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        ApplyTheme(ProfileTheme.Nl, animate: false);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var initialization = _viewModel.InitializeAsync();
        var minimumSplashTime = Task.Delay(1600);
        await Task.WhenAll(initialization, minimumSplashTime);

        var expansion = ExpandWindowAsync();
        await Task.Delay(120);
        HideLoadingOverlay();
        await Task.Delay(360);
        RevealMainContent();
        await expansion;
    }

    private Task ExpandWindowAsync()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var duration = TimeSpan.FromMilliseconds(720);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var storyboard = new Storyboard
        {
            Duration = duration
        };
        var widthAnimation = ShellAnimation(
            WindowShell.ActualWidth,
            FinalShellWidth,
            duration,
            easing);
        var heightAnimation = ShellAnimation(
            WindowShell.ActualHeight,
            FinalShellHeight,
            duration,
            easing);

        Storyboard.SetTarget(widthAnimation, WindowShell);
        Storyboard.SetTargetProperty(
            widthAnimation,
            new PropertyPath(FrameworkElement.WidthProperty));
        Storyboard.SetTarget(heightAnimation, WindowShell);
        Storyboard.SetTargetProperty(
            heightAnimation,
            new PropertyPath(FrameworkElement.HeightProperty));
        storyboard.Children.Add(widthAnimation);
        storyboard.Children.Add(heightAnimation);

        storyboard.Completed += (_, _) =>
        {
            WindowShell.Width = FinalShellWidth;
            WindowShell.Height = FinalShellHeight;
            storyboard.Remove(this);
            completion.TrySetResult();
        };

        storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);

        return completion.Task;
    }

    private static DoubleAnimation ShellAnimation(
        double from,
        double to,
        TimeSpan duration,
        IEasingFunction easing) =>
        new()
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };

    private void RevealMainContent()
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var reveal = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(420),
            EasingFunction = easing
        };
        var scale = new DoubleAnimation
        {
            From = 0.985,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(420),
            EasingFunction = easing
        };
        MainContent.BeginAnimation(OpacityProperty, reveal);
        MainContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        MainContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
    }

    private void HideLoadingOverlay()
    {
        var fade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        fade.Completed += (_, _) =>
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            LoadingOverlay.IsHitTestVisible = false;
        };

        LoadingOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private void ProfileChoice_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton choice)
        {
            return;
        }

        var isGs = choice.Name == nameof(GsProfileChoice);
        if (_viewModel != null)
        {
            _viewModel.SelectedProfile = isGs
                ? LoaderProfile.Gamesense
                : LoaderProfile.Neverlose;
        }

        MoveProfileIndicator(isGs, animate: IsLoaded);
        ApplyTheme(isGs ? ProfileTheme.Gs : ProfileTheme.Nl, animate: IsLoaded);
    }

    private void GameChoice_Checked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not RadioButton choice)
        {
            return;
        }

        _viewModel.SelectedGame = choice.Name == nameof(Cs2LegacyChoice)
            ? GameTarget.Cs2Legacy
            : GameTarget.CsgoStandalone;
    }

    private void ApplyTheme(ProfileTheme theme, bool animate)
    {
        var resources = Application.Current.Resources;
        var palette = theme == ProfileTheme.Nl
            ? ThemePalette.Nl
            : ThemePalette.Gs;

        if (!animate)
        {
            SetTheme(resources, palette);
            return;
        }

        var duration = TimeSpan.FromMilliseconds(360);
        var easing = new SineEase { EasingMode = EasingMode.EaseInOut };

        AnimateSolidBrush(resources, "ThemeAccentBrush", palette.Accent, duration, easing);
        AnimateSolidBrush(resources, "ThemeAccentSoftBrush", palette.AccentSoft, duration, easing);
        AnimateSolidBrush(resources, "ThemeGlowBrush", palette.Glow, duration, easing);
        AnimateSolidBrush(resources, "ThemeWindowBrush", palette.Window, duration, easing);
        AnimateSolidBrush(resources, "ThemeSurfaceBrush", palette.Surface, duration, easing);
        AnimateSolidBrush(resources, "ThemeSurfaceHoverBrush", palette.SurfaceHover, duration, easing);
        AnimateSolidBrush(resources, "ThemeDividerBrush", palette.Divider, duration, easing);
        AnimateGradientBrush(
            resources,
            "ThemePrimaryBrush",
            palette.PrimaryStart,
            palette.PrimaryEnd,
            duration,
            easing);
        AnimateGradientBrush(
            resources,
            "ThemePrimaryHoverBrush",
            palette.PrimaryHoverStart,
            palette.PrimaryHoverEnd,
            duration,
            easing);
    }

    private void MoveProfileIndicator(bool isGs, bool animate)
    {
        const double segmentWidth = 94;
        var targetOffset = isGs ? segmentWidth : 0;

        if (!animate)
        {
            ProfileSelectionTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ProfileSelectionTransform.X = targetOffset;
            return;
        }

        var currentOffset = ProfileSelectionTransform.X;
        ProfileSelectionTransform.X = targetOffset;
        ProfileSelectionTransform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation
            {
                From = currentOffset,
                To = targetOffset,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void SetTheme(ResourceDictionary resources, ThemePalette palette)
    {
        resources["ThemeAccentBrush"] = Brush(palette.Accent);
        resources["ThemeAccentSoftBrush"] = Brush(palette.AccentSoft);
        resources["ThemeGlowBrush"] = Brush(palette.Glow);
        resources["ThemeWindowBrush"] = Brush(palette.Window);
        resources["ThemeSurfaceBrush"] = Brush(palette.Surface);
        resources["ThemeSurfaceHoverBrush"] = Brush(palette.SurfaceHover);
        resources["ThemeDividerBrush"] = Brush(palette.Divider);
        resources["ThemePrimaryBrush"] = Gradient(palette.PrimaryStart, palette.PrimaryEnd);
        resources["ThemePrimaryHoverBrush"] = Gradient(
            palette.PrimaryHoverStart,
            palette.PrimaryHoverEnd);
    }

    private static void AnimateSolidBrush(
        ResourceDictionary resources,
        string key,
        string target,
        TimeSpan duration,
        IEasingFunction easing)
    {
        var targetColor = ColorValue(target);
        var currentColor = resources[key] is SolidColorBrush currentBrush
            ? currentBrush.Color
            : targetColor;
        var animatedBrush = new SolidColorBrush(currentColor);

        AnimateColor(animatedBrush, SolidColorBrush.ColorProperty, targetColor, duration, easing);
        resources[key] = animatedBrush;
    }

    private static void AnimateGradientBrush(
        ResourceDictionary resources,
        string key,
        string start,
        string end,
        TimeSpan duration,
        IEasingFunction easing)
    {
        var targetStart = ColorValue(start);
        var targetEnd = ColorValue(end);
        var currentStart = targetStart;
        var currentEnd = targetEnd;

        if (resources[key] is LinearGradientBrush currentBrush &&
            currentBrush.GradientStops.Count >= 2)
        {
            currentStart = currentBrush.GradientStops[0].Color;
            currentEnd = currentBrush.GradientStops[1].Color;
        }

        var animatedBrush = new LinearGradientBrush(
            currentStart,
            currentEnd,
            new Point(0, 0),
            new Point(1, 1));
        AnimateColor(
            animatedBrush.GradientStops[0],
            GradientStop.ColorProperty,
            targetStart,
            duration,
            easing);
        AnimateColor(
            animatedBrush.GradientStops[1],
            GradientStop.ColorProperty,
            targetEnd,
            duration,
            easing);
        resources[key] = animatedBrush;
    }

    private static void AnimateColor(
        Animatable target,
        DependencyProperty property,
        Color targetColor,
        TimeSpan duration,
        IEasingFunction easing)
    {
        var currentColor = (Color)target.GetValue(property);
        target.BeginAnimation(
            property,
            new ColorAnimation
            {
                From = currentColor,
                To = targetColor,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private static SolidColorBrush Brush(string color) =>
        new(ColorValue(color));

    private static LinearGradientBrush Gradient(string start, string end) =>
        new(
            ColorValue(start),
            ColorValue(end),
            new Point(0, 0),
            new Point(1, 1));

    private static Color ColorValue(string color) =>
        (Color)ColorConverter.ConvertFromString(color);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Discord_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = DiscordUrl,
            UseShellExecute = true
        });

    private void Close_Click(object sender, RoutedEventArgs e) =>
        Close();

    private enum ProfileTheme
    {
        Nl,
        Gs
    }

    private readonly record struct ThemePalette(
        string Accent,
        string AccentSoft,
        string Glow,
        string Window,
        string Surface,
        string SurfaceHover,
        string Divider,
        string PrimaryStart,
        string PrimaryEnd,
        string PrimaryHoverStart,
        string PrimaryHoverEnd)
    {
        public static ThemePalette Nl { get; } = new(
            "#42BCE8",
            "#2A176985",
            "#2C159AC4",
            "#FE010811",
            "#DC03101B",
            "#F0081B29",
            "#B50A2638",
            "#1688B1",
            "#0B5D87",
            "#20A0CA",
            "#0D709C");

        public static ThemePalette Gs { get; } = new(
            "#7BE38A",
            "#30205A38",
            "#3040BD61",
            "#FE030A06",
            "#DC07140D",
            "#F00D2217",
            "#B518432C",
            "#3A9D61",
            "#246B45",
            "#49B972",
            "#2F8254");
    }
}
