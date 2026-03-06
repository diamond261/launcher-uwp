using Flarial.Launcher.Functions;
using Flarial.Launcher.Managers;
using Flarial.Launcher.Pages;
using Flarial.Launcher.Animations;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Windows.ApplicationModel;
using static System.StringComparison;
using Flarial.Launcher.Services.Client;
using Flarial.Launcher.Services.Modding;
using Flarial.Launcher.Services.SDK;
using Flarial.Launcher.Services.Management;
using Flarial.Launcher.Services.Core;
using Flarial.Launcher.Styles;
using Flarial.Launcher.Services.Networking;

namespace Flarial.Launcher;

public partial class MainWindow
{
    public static bool Reverse, isDownloadingVersion;

    public static ImageBrush PFP;

    public static TextBlock StatusLabel, versionLabel, Username;

    public static DialogBox MainWindowDialogBox;

    private static StackPanel mbGrid;

    internal readonly WindowInteropHelper WindowInteropHelper;

    public bool IsLaunchEnabled
    {
        get { return (bool)GetValue(IsLaunchEnabledProperty); }
        set { SetValue(IsLaunchEnabledProperty, value); }
    }

    public static readonly DependencyProperty IsLaunchEnabledProperty =
        DependencyProperty.Register("IsLaunchEnabled", typeof(bool),
            typeof(MainWindow), new PropertyMetadata(true));

    readonly TextBlock _launchButtonTextBlock;

    readonly Forms.NotifyIcon _trayIcon;

    bool _exitRequested;

    bool _autoVoidDisabled;

    internal bool IsAutoVoidDisabled => _autoVoidDisabled;

    readonly MediaPlayer _startupSoundPlayer = new();
    readonly MediaPlayer _autoVoidSoundPlayer = new();
    string _autoVoidLoadedPath = string.Empty;
    bool _autoVoidSoundReady;

    static readonly string[] s_autoVoidSoundCandidates =
    [
        Path.Combine(AppContext.BaseDirectory, "viod.ogg"),
        Path.Combine(Environment.CurrentDirectory, "viod.ogg"),
        Path.Combine(AppContext.BaseDirectory, "1271923498243325953.ogg"),
        Path.Combine(Environment.CurrentDirectory, "1271923498243325953.ogg")
    ];

    readonly Settings _settings = Settings.Current;

    static string[] ParseEnabledCustomLibraries(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return [];

        return path
            .Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim().Trim('"'))
            .Where(value => value.Length > 0)
            .Select(value =>
            {
                var index = value.IndexOf('|');
                if (index <= 0)
                    return (enabled: true, path: value);

                var prefix = value.Substring(0, index).Trim();
                var actualPath = value.Substring(index + 1).Trim();

                if (actualPath.Length == 0)
                    return (enabled: false, path: string.Empty);

                return prefix switch
                {
                    "1" => (enabled: true, path: actualPath),
                    "0" => (enabled: false, path: actualPath),
                    _ => (enabled: true, path: value)
                };
            })
            .Where(item => item.enabled)
            .Select(item => item.path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public MainWindow()
    {
        InitializeComponent();

        _trayIcon = InitializeTrayIcon();

        LaunchButton.ApplyTemplate();
        _launchButtonTextBlock = (TextBlock)LaunchButton.Template.FindName("LaunchText", LaunchButton);
        _launchButtonTextBlock.Text = "Updating...";

        WindowInteropHelper = new(this);

        Icon = EmbeddedResources.GetImageSource("app.ico");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SnapsToDevicePixels = UseLayoutRounding = true;

        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        ContentRendered += MainWindow_ContentRendered;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        LauncherVersion.Text = version is null
            ? "v0.0.0"
            : $"v{version.Major}.{version.Minor}.{version.Build}";

        Dispatcher.InvokeAsync(MinecraftGame.Init);

        StatusLabel = statusLabel;
        versionLabel = VersionLabel;
        Username = username;
        mbGrid = MbGrid;
        PFP = pfp;
        MainWindowDialogBox = DialogControl;
        SettingsPage.MainGrid = MainGrid;
        SettingsPage.b1 = MainBorder;

        Dispatcher.InvokeAsync(RPCManager.Initialize);
        Application.Current.MainWindow = this;

        SetGreetingLabel();

        SetAutoVoidDisabled(_settings.DisableAutoVoid);
        _autoVoidSoundPlayer.MediaOpened += (_, _) => _autoVoidSoundReady = true;
        _autoVoidSoundPlayer.MediaFailed += (_, _) => _autoVoidSoundReady = false;

        IsLaunchEnabled = false;
    }

    Forms.NotifyIcon InitializeTrayIcon()
    {
        Forms.NotifyIcon trayIcon = new()
        {
            Text = "Flarial Launcher",
            Visible = false
        };

        var icon = Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
        if (icon is not null)
            trayIcon.Icon = icon;

        Forms.ContextMenuStrip menu = new();
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        menu.Items.Add("Exit", null, (_, _) => ExitFromTray());

        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (_, _) => ShowFromTray();

        return trayIcon;
    }

    void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    void ExitFromTray()
    {
        _exitRequested = true;
        _trayIcon.Visible = false;
        Close();
    }

    void MinimizeToTray()
    {
        _trayIcon.Visible = true;
        ShowInTaskbar = false;
        Hide();
    }

    readonly PackageCatalog PackageCatalog = PackageCatalog.OpenForCurrentUser();

    void UpdateGameVersionText(Package package) { if (package.Id.FamilyName.Equals("Microsoft.MinecraftUWP_8wekyb3d8bbwe", OrdinalIgnoreCase)) UpdateGameVersionText(); }

    void UpdateGameVersionText() => Dispatcher.BeginInvoke(() =>
    {
        try
        {
            VersionLabel.Text = $"{Minecraft.Version} ({(Minecraft.UsingGameDevelopmentKit ? "GDK" : "UWP")})";
        }
        catch
        {
            VersionLabel.Text = "0.0.0 (UWP)";
        }
    });

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _ = CheckLicenseAsync();
        CreateMessageBox("Join our Discord: https://flarial.xyz/discord");

        if (!_settings.HardwareAcceleration) CreateMessageBox("Hardware acceleration is disabled.");
    }

    static readonly SolidColorBrush _darkRed = new(Colors.DarkRed);

    static readonly SolidColorBrush _darkGreen = new(Colors.DarkGreen);

    static readonly SolidColorBrush _darkGoldenrod = new(Colors.DarkGoldenrod);

    async Task CheckLicenseAsync()
    {
        try { LicenseText.Foreground = await LicensingService.VerifyAsync() ? _darkGreen : _darkRed; }
        catch { LicenseText.Foreground = _darkGoldenrod; }
    }

    private async void MainWindow_ContentRendered(object sender, EventArgs e)
    {
        PlayStartupSound();

        _launchButtonTextBlock.Text = "Preparing...";

        PackageCatalog.PackageInstalling += (_, args) => { if (args.IsComplete) UpdateGameVersionText(args.Package); };
        PackageCatalog.PackageUninstalling += (_, args) => { if (args.IsComplete) UpdateGameVersionText(args.Package); };
        PackageCatalog.PackageUpdating += (_, args) => { if (args.IsComplete) UpdateGameVersionText(args.TargetPackage); };

        UpdateGameVersionText();
        _launchButtonTextBlock.Text = "Launch";
        IsLaunchEnabled = true;

    }

    public static void CreateMessageBox(string text)
    {
        mbGrid.Children.Add(new Styles.MessageBox { Text = text });
    }

    private void MoveWindow(object sender, MouseButtonEventArgs e) => DragMove();

    private void WindowMinimize(object sender, RoutedEventArgs e)
    {
        if (_settings.SaveOnTray)
        {
            MinimizeToTray();
            return;
        }

        WindowState = WindowState.Minimized;
    }

    private void WindowClose(object sender, RoutedEventArgs e) => Close();

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e) =>
        OpenSettings();

    void OpenSettings()
    {
        try
        {
            if (SettingsFrame.Source is null)
                SettingsFrame.Source = new Uri("Pages/SettingsPage.xaml", UriKind.Relative);

            SettingsPageTransition.SettingsEnterAnimation(MainBorder, MainGrid);
        }
        catch (Exception exception)
        {
            Logger.Error("Failed to open settings page", exception);
            CreateMessageBox($"Failed to open settings: {exception.Message}");
        }
    }

    private void UIElement_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        NewsPageTransition.Animation(Reverse, MainBorder, NewsBorder, NewsArrow);

    private void SetGreetingLabel()
    {
        int Time = int.Parse(DateTime.Now.ToString("HH", System.Globalization.DateTimeFormatInfo.InvariantInfo));

        if (Time >= 0 && Time < 12)
            GreetingLabel.Text = "Good Morning!";
        else if (Time >= 12 && Time < 18)
            GreetingLabel.Text = "Good Afternoon!";
        else if (Time >= 18 && Time <= 24)
            GreetingLabel.Text = "Good Evening!";
    }

    private async void Inject_Click(object sender, RoutedEventArgs e)
        => await LaunchClientAsync(false);

    async Task<bool> LaunchClientAsync(bool silentFailures)
    {
        try
        {
            PlayAutoVoidLaunchSound();
            IsLaunchEnabled = false;
            _launchButtonTextBlock.Text = "Verifying...";

            if (!Minecraft.IsInstalled)
            {
                if (!silentFailures)
                    CreateMessageBox(@"Please install the game.");
                return false;
            }

            var build = _settings.DllBuild;
            var path = _settings.CustomDllPath;
            var custom = build is DllBuild.Custom;
            var initialized = _settings.WaitForInitialization;
            var customTargetInjection = _settings.CustomTargetInjection;
            var customTargetProcessName = _settings.CustomTargetProcessName;
            var beta = build is DllBuild.Beta or DllBuild.Nightly;
            var client = beta ? FlarialClient.Beta : FlarialClient.Release;

            if (custom)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    if (!silentFailures)
                        CreateMessageBox("Please specify at least one custom DLL.");
                    return false;
                }

                var paths = ParseEnabledCustomLibraries(path);

                if (paths.Length == 0)
                {
                    if (!silentFailures)
                        CreateMessageBox("Please specify at least one custom DLL.");
                    return false;
                }

                var libraries = paths.Select(value => new ModificationLibrary(value)).ToArray();
                if (libraries.Any(library => !library.IsValid))
                {
                    if (!silentFailures)
                        CreateMessageBox("One or more custom libraries are invalid or don't exist.");
                    return false;
                }

                _launchButtonTextBlock.Text = "Launching...";
                uint? processId;

                if (customTargetInjection)
                {
                    processId = await Task.Run(() => Injector.Launch(libraries[0], customTargetProcessName));
                    if (processId is not { })
                    {
                        if (!silentFailures)
                            CreateMessageBox($"Please start '{customTargetProcessName}' and try again.");
                        return false;
                    }
                }
                else
                {
                    processId = await Task.Run(() => Injector.Launch(initialized, libraries[0]));

                    if (processId is not { })
                    {
                        if (!silentFailures)
                            CreateMessageBox("Please close the game and try again.");
                        return false;
                    }

                }

                foreach (var library in libraries.Skip(1))
                    if (!await Task.Run(() => Injector.Launch(processId.Value, library)))
                    {
                        if (!silentFailures)
                            CreateMessageBox($"Failed to inject '{library.FileName}'.");
                        return false;
                    }

                if (processId is not { })
                {
                    if (customTargetInjection)
                    {
                        if (!silentFailures)
                            CreateMessageBox($"Please start '{customTargetProcessName}' and try again.");
                    }
                    else
                    {
                        if (!silentFailures)
                            CreateMessageBox("Please close the game and try again.");
                    }
                    return false;
                }

                StatusLabel.Text = customTargetInjection
                    ? $"Launched {libraries.Length} custom librar{(libraries.Length == 1 ? "y" : "ies")} into {customTargetProcessName}."
                    : $"Launched {libraries.Length} custom DLL{(libraries.Length == 1 ? string.Empty : "s")}.";
                return true;
            }

            if (!silentFailures && beta && !await DialogBox.ShowAsync("Beta Usage", @"The beta build of the client might be potentially unstable. 

• Bugs & crashes might occur frequently during gameplay.
• The beta build is meant for reporting bugs & issues with the client.

Hence use at your own risk.", ("Cancel", false), ("Launch", true)))
                return false;

            if (!await client.DownloadAsync(ClientDownloadProgressAction))
            {
                if (!silentFailures)
                    await DialogBox.ShowAsync("Update Failed", @"A client update couldn't be downloaded.

• Try closing the game & see if the client updates.
• Try rebooting your machine & see if that resolves the issue.

If you need help, join our Discord.", ("OK", true));
                return false;
            }

            _launchButtonTextBlock.Text = "Launching...";
            var launched = await Task.Run(() => client.Launch(initialized));

            if (!launched)
            {
                if (!silentFailures)
                    await DialogBox.ShowAsync("Launch Failure", @"The client couldn't inject correctly.

• Try closing the game & try again.
• Remove & disable any 3rd party mods or tools.

If you need help, join our Discord.", ("OK", true));
                return false;
            }

            StatusLabel.Text = $"Launched {(beta ? "Beta" : "Release")} DLL.";
            return true;
        }
        finally
        {
            _launchButtonTextBlock.Text = "Launch";
            IsLaunchEnabled = true;
        }
    }

    void PlayStartupSound()
    {
        try
        {
            var soundPath = s_autoVoidSoundCandidates.FirstOrDefault(File.Exists);
            if (soundPath is null)
                return;

            _startupSoundPlayer.Open(new Uri(soundPath, UriKind.Absolute));
            _startupSoundPlayer.Volume = 1.0;
            _startupSoundPlayer.Play();
        }
        catch { }
    }

    void PlayAutoVoidLaunchSound()
    {
        try
        {
            if (!_autoVoidDisabled)
                return;

            var soundPath = s_autoVoidSoundCandidates.FirstOrDefault(File.Exists);
            if (soundPath is null)
                return;

            if (!_autoVoidSoundReady || !string.Equals(_autoVoidLoadedPath, soundPath, OrdinalIgnoreCase))
            {
                _autoVoidSoundReady = false;
                _autoVoidLoadedPath = soundPath;
                EventHandler handler = null;
                handler = (_, _) =>
                {
                    _autoVoidSoundPlayer.MediaOpened -= handler;
                    _autoVoidSoundPlayer.Stop();
                    _autoVoidSoundPlayer.Position = TimeSpan.Zero;
                    _autoVoidSoundPlayer.Volume = 1.0;
                    _autoVoidSoundPlayer.Play();
                };
                _autoVoidSoundPlayer.MediaOpened += handler;
                _autoVoidSoundPlayer.Open(new Uri(soundPath, UriKind.Absolute));
                return;
            }

            _autoVoidSoundPlayer.Stop();
            _autoVoidSoundPlayer.Position = TimeSpan.Zero;
            _autoVoidSoundPlayer.Volume = 1.0;
            _autoVoidSoundPlayer.Play();
        }
        catch { }
    }

    internal void SetAutoVoidDisabled(bool disabled)
    {
        _autoVoidDisabled = disabled;
        StatusLabel.Text = _autoVoidDisabled ? "Auto Void disabled." : "Auto Void enabled.";
    }

    public void ClientDownloadProgressAction(int value) => Dispatcher.Invoke(() =>
    {
        _launchButtonTextBlock.Text = "Downloading...";
        statusLabel.Text = $"Downloading... {value}%";
    });
    private void Window_OnClosing(object sender, CancelEventArgs e)
    {
        if (_settings.SaveOnTray && !_exitRequested)
        {
            e.Cancel = true;
            MinimizeToTray();
            return;
        }

        if (isDownloadingVersion)
        {
            e.Cancel = true;
            CreateMessageBox("The launcher cannot be closed because a Minecraft version is being downloaded.");
        }
    }

    protected override void OnClosed(EventArgs args)
    {
        base.OnClosed(args);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Settings.Current.Save();
        Environment.Exit(0);
    }

}
