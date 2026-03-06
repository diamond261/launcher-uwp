using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Threading.Tasks;
using Flarial.Launcher.Managers;
using Flarial.Launcher.Services.Core;
using Flarial.Launcher.Services.Management.Versions;

namespace Flarial.Launcher.Pages;

public partial class SettingsSwitcherPage : Page
{
    sealed class VersionRow
    {
        public string Label { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public VersionEntry Entry { get; set; }
        public bool IsInstalled { get; set; }
    }

    readonly List<VersionRow> _rows = [];

    bool _loading;

    public SettingsSwitcherPage()
    {
        InitializeComponent();
        _ = LoadCatalogAsync();
    }

    async System.Threading.Tasks.Task LoadCatalogAsync()
    {
        if (_loading)
            return;

        _loading = true;
        ProgressText.Text = "Loading versions...";

        try
        {
            var catalog = await VersionCatalog.GetAsync();
            var installedVersion = Minecraft.IsInstalled ? NormalizeVersion(Minecraft.Version) : string.Empty;
            var installedPlatform = Minecraft.UsingGameDevelopmentKit ? "GDK" : "UWP";

            foreach (var item in catalog.InstallableEntries)
            {
                var entry = item.Entry;
                var platform = entry.GetType().Name.Contains("GDK", StringComparison.OrdinalIgnoreCase)
                    ? "GDK"
                    : "UWP";

                _rows.Add(new VersionRow
                {
                    Label = item.Version,
                    Platform = platform,
                    Entry = entry,
                    IsInstalled = platform == installedPlatform &&
                        string.Equals(NormalizeVersion(item.Version), installedVersion, StringComparison.OrdinalIgnoreCase)
                });
            }

            _rows.Sort((left, right) => new Version(right.Label).CompareTo(new Version(left.Label)));

            var uwpLatest = _rows.Where(_ => _.Platform == "UWP").Select(_ => _.Label).FirstOrDefault() ?? "n/a";
            var gdkLatest = _rows.Where(_ => _.Platform == "GDK").Select(_ => _.Label).FirstOrDefault() ?? "n/a";
            Logger.Info($"Switcher loaded UWP={_rows.Count(_ => _.Platform == "UWP")} latest={uwpLatest}, GDK={_rows.Count(_ => _.Platform == "GDK")} latest={gdkLatest}");

            ApplyFilter();
            ProgressText.Text = $"{_rows.Count} versions available";
        }
        catch (Exception ex)
        {
            Logger.Error("Switcher load failed", ex);
            ProgressText.Text = "Failed to load versions";
            MainWindow.CreateMessageBox($"Switcher load failed: {ex.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    static string NormalizeVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var dots = value.Count(_ => _ == '.');
        if (dots < 3)
            return value;

        var index = value.LastIndexOf('.');
        return index > 0 ? value.Substring(0, index) : value;
    }

    static bool IsRunningAsAdministrator()
    {
        var identity = WindowsIdentity.GetCurrent();
        if (identity is null)
            return false;

        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    bool IsLatestForPlatform(VersionRow row)
    {
        var latest = _rows
            .Where(_ => _.Platform == row.Platform)
            .Select(_ => _.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(_ => new Version(_))
            .FirstOrDefault();

        return latest is not null && string.Equals(latest, row.Label, StringComparison.OrdinalIgnoreCase);
    }

    void ApplyFilter()
    {
        if (VersionsList is null || AllTab is null || UwpTab is null || GdkTab is null)
            return;

        IEnumerable<VersionRow> rows = _rows;
        var selected = AllTab.IsChecked is true ? "All" : UwpTab.IsChecked is true ? "UWP" : "GDK";

        if (selected == "UWP")
            rows = rows.Where(row => row.Platform == "UWP");
        else if (selected == "GDK")
            rows = rows.Where(row => row.Platform == "GDK");

        VersionsList.ItemsSource = rows.ToList();
    }

    void PlatformTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tab)
            return;

        AllTab.IsChecked = ReferenceEquals(tab, AllTab);
        UwpTab.IsChecked = ReferenceEquals(tab, UwpTab);
        GdkTab.IsChecked = ReferenceEquals(tab, GdkTab);

        try { ApplyFilter(); }
        catch (Exception exception) { Logger.Error("Switcher filter apply failed", exception); }
    }

    async void VersionsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (VersionsList.SelectedItem is not VersionRow row)
        {
            MainWindow.CreateMessageBox("Please select a version first.");
            return;
        }

        try
        {
            if (IsLatestForPlatform(row))
            {
                MainWindow.CreateMessageBox($"{row.Label} is already the latest {row.Platform} version. Please use Microsoft Store to update.");
                ProgressText.Text = "Latest version - update via Microsoft Store";
                return;
            }

            if (!IsRunningAsAdministrator())
            {
                MainWindow.CreateMessageBox("Please run launcher as administrator first.");
                ProgressText.Text = "Run as administrator required";
                return;
            }

            ProgressText.Text = "Creating backup...";
            var backupName = await BackupManager.CreateVersionSwitchBackupAsync();
            MainWindow.CreateMessageBox($"Backup created: {backupName}");

            ProgressText.Text = $"Installing {row.Label} ({row.Platform})...";

            var request = await row.Entry.InstallAsync(value => Dispatcher.Invoke(() =>
            {
                ProgressText.Text = $"Installing {row.Label} ({row.Platform})... {value}%";
            }));

            await request;

            var verified = false;
            for (var index = 0; index < 14; index++)
            {
                try
                {
                    var installedVersion = Minecraft.IsInstalled ? Minecraft.Version : string.Empty;
                    var installedPlatform = Minecraft.UsingGameDevelopmentKit ? "GDK" : "UWP";

                    if (string.Equals(installedPlatform, row.Platform, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(installedVersion, row.Label, StringComparison.OrdinalIgnoreCase))
                    {
                        verified = true;
                        break;
                    }
                }
                catch (Exception verifyException)
                {
                    Logger.Error("Switcher install verification check failed", verifyException);
                }

                await Task.Delay(500);
            }

            if (!verified)
                throw new Exception($"Installation completed but version verification failed. Expected {row.Label} ({row.Platform}).");

            ProgressText.Text = $"{row.Label} Install Finished";
            MainWindow.CreateMessageBox($"{row.Label} Install Finished");
        }
        catch (Exception ex)
        {
            Logger.Error("Switcher install failed", ex);
            ProgressText.Text = "Install failed";
            MainWindow.CreateMessageBox($"Install failed: {ex.Message}");
        }
        finally
        {
        }
    }
}
