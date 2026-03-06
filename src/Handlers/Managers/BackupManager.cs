using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Flarial.Launcher.Services.Core;

namespace Flarial.Launcher.Managers;

// Configuration
public class BackupConfiguration
{
    public DateTime BackupTime { get; set; }
    public string MinecraftVersion { get; set; }
    public Guid BackupId { get; set; }
}

// Actual Manager
public static class BackupManager
{
    public static string backupDirectory = Path.Combine(VersionManagement.launcherPath, "Backups");

    static string ToLongPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal))
            return full;

        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + full.Substring(2);

        return @"\\?\" + full;
    }

    static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (invalid.Contains(chars[index]))
                chars[index] = '_';
        }

        return new string(chars);
    }

    public static async Task<List<string>> FilterByName(string filterName)
    {
        var unfilteredBackups = await GetAllBackupsAsync();
        return unfilteredBackups.Where(backup => backup.StartsWith(filterName)).ToList();
    }

    public static async Task<List<string>> GetAllBackupsAsync()
    {
        return await Task.Run(() =>
        {
            var directories = Directory.GetDirectories(backupDirectory).Select(Path.GetFileName);
            var archives = Directory.GetFiles(backupDirectory, "*.zip").Select(Path.GetFileName);
            return directories.Concat(archives).OrderByDescending(_ => _).ToList();
        });
    }

    public static async Task LoadBackup(string backupName)
    {
        try
        {
            var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var packageRoot = Path.Combine(
                appDataRoot,
                "Packages",
                "Microsoft.MinecraftUWP_8wekyb3d8bbwe"
            );

            var localStatePath = Path.Combine(packageRoot, "LocalState");
            var roamingStatePath = Path.Combine(packageRoot, "RoamingState");
            var minecraftBedrockPath = Path.Combine(appDataRoot, "Minecraft Bedrock");

            var directoryPath = Path.Combine(backupDirectory, backupName);
            var zipPath = directoryPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? directoryPath
                : directoryPath + ".zip";

            string restoreRoot = directoryPath;
            string tempPath = string.Empty;

            if (File.Exists(zipPath))
            {
                tempPath = Path.Combine(backupDirectory, "_tmp_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempPath);
                ZipFile.ExtractToDirectory(zipPath, tempPath);
                restoreRoot = tempPath;
            }

            var newLocalState = Path.Combine(restoreRoot, "LS");
            var newRoamingState = Path.Combine(restoreRoot, "RS");
            var newMinecraftBedrock = Path.Combine(restoreRoot, "MB");

            if (Directory.Exists(newLocalState))
                await DirectoryCopyAsync(newLocalState, localStatePath, true);
            else
            {
                var legacyMojangPath = Path.Combine(restoreRoot, "com.mojang");
                if (Directory.Exists(legacyMojangPath))
                {
                    var legacyLocalState = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Packages",
                        "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
                        "LocalState",
                        "games"
                    );
                    await DirectoryCopyAsync(legacyMojangPath, legacyLocalState, true);
                }
                else
                {
                    MessageBox.Show("No Minecraft backups available with the given ID.", "Failed to Load Backup");
                    return;
                }
            }

            if (Directory.Exists(newRoamingState))
                await DirectoryCopyAsync(newRoamingState, roamingStatePath, true);
            else
            {
                var legacyRoamingPath = Path.Combine(restoreRoot, "RoamingState");
                if (Directory.Exists(legacyRoamingPath))
                    await DirectoryCopyAsync(legacyRoamingPath, roamingStatePath, true);
                else
                    MessageBox.Show("Roaming State backup data not found.", "Failed to Load Backup");
            }

            if (Directory.Exists(newMinecraftBedrock))
                await DirectoryCopyAsync(newMinecraftBedrock, minecraftBedrockPath, true);

            if (tempPath.Length > 0)
                await DeleteDirectoryAsync(tempPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error");
        }
        finally
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MainWindow.CreateMessageBox("Backup loaded.");
            });
        }
    }

    public static async Task<bool> CreateBackup(string backupName)
    {
        try
        {
            var backupDirectoryPath = Path.Combine(backupDirectory, backupName);
            if (Directory.Exists(backupDirectoryPath))
            {
                MessageBox.Show("Backup with the given name already exists.", "Failed to Create Backup");
                return false;
            }

            var mcPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
                "LocalState",
                "games"
            );
            var flarialPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
                "RoamingState"
            );

            if (!Directory.Exists(mcPath))
            {
                MessageBox.Show("Minecraft Data Path is invalid!", "Failed To Backup");
                return false;
            }

            Directory.CreateDirectory(backupDirectoryPath);

            if (!await BackupDirectoryAsync(mcPath, Path.Combine(backupDirectoryPath, "com.mojang"))) return false;

            if (Directory.Exists(flarialPath))
            {
                if (!await BackupDirectoryAsync(flarialPath, Path.Combine(backupDirectoryPath, "RoamingState")))
                    return false;
            }
            else
            {
                MessageBox.Show("Roaming State Data Path is invalid!", "Failed To Backup");
                return false;
            }

            var text = await CreateConfig();
            File.WriteAllText(Path.Combine(backupDirectoryPath, "BackupConfig.json"), text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error");
            return false;
        }

        return true;
    }

    public static async Task<string> CreateVersionSwitchBackupAsync()
    {
        var version = Minecraft.IsInstalled ? Minecraft.Version : "unknown";
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var compactVersion = version.Replace(".", string.Empty);
        var backupName = $"sw_{timestamp}_{compactVersion}";
        var backupPath = Path.Combine(backupDirectory, backupName);

        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var packageRoot = Path.Combine(
            appDataRoot,
            "Packages",
            "Microsoft.MinecraftUWP_8wekyb3d8bbwe"
        );

        var targets = Minecraft.UsingGameDevelopmentKit
            ? new (string Source, string Destination)[]
            {
                (Path.Combine(appDataRoot, "Minecraft Bedrock"), Path.Combine(backupPath, "MB"))
            }
            : new (string Source, string Destination)[]
            {
                (Path.Combine(packageRoot, "LocalState"), Path.Combine(backupPath, "LS")),
                (Path.Combine(packageRoot, "RoamingState"), Path.Combine(backupPath, "RS")),
                (Path.Combine(appDataRoot, "Minecraft Bedrock"), Path.Combine(backupPath, "MB"))
            };

        Directory.CreateDirectory(backupPath);

        var copied = 0;
        foreach (var target in targets)
        {
            if (!Directory.Exists(target.Source))
                continue;

            await DirectoryCopyAsync(target.Source, target.Destination, true);
            copied++;
        }

        if (copied == 0)
            throw new DirectoryNotFoundException("No Minecraft data directories were found to back up.");

        var metadata =
            $"BackupTime={DateTime.Now:O}{Environment.NewLine}" +
            $"MinecraftVersion={version}{Environment.NewLine}" +
            $"LocalState=%appdata%\\Packages\\Microsoft.MinecraftUWP_8wekyb3d8bbwe\\LocalState{Environment.NewLine}" +
            $"RoamingState=%appdata%\\Packages\\Microsoft.MinecraftUWP_8wekyb3d8bbwe\\RoamingState{Environment.NewLine}" +
            $"MinecraftBedrock=%appdata%\\Minecraft Bedrock{Environment.NewLine}";
        File.WriteAllText(Path.Combine(backupPath, "BackupInfo.txt"), metadata);

        var zipName = SanitizeFileName($"{version}_{timestamp}.zip");
        var zipPath = Path.Combine(backupDirectory, zipName);
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        await Task.Run(() => ZipFile.CreateFromDirectory(backupPath, zipPath, CompressionLevel.Optimal, false));
        await DeleteDirectoryAsync(backupPath);
        return zipName;
    }

    private static async Task<bool> BackupDirectoryAsync(string source, string destination)
    {
        var sourceDirectory = new DirectoryInfo(ToLongPath(source));
        if (!sourceDirectory.Exists)
        {
            throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + source);
        }

        var destinationDirectory = Directory.CreateDirectory(ToLongPath(destination));

        await Task.WhenAll(sourceDirectory.GetFiles().Select(async file =>
        {

            try
            {
                FileAttributes attributes = File.GetAttributes(file.FullName);

                if ((attributes & FileAttributes.Encrypted) == FileAttributes.Encrypted)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MainWindow.CreateMessageBox("Failed to install. Join our discord for help: https://flarial.xyz/discord");
                        MainWindow.CreateMessageBox("Files are encrypted!");
                    });
                    return false;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error checking file attributes: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MainWindow.CreateMessageBox("Failed to install. Join our discord for help: https://flarial.xyz/discord");
                    MainWindow.CreateMessageBox("Error checking for files.");
                });
                return false;
            }

            var tempPath = Path.Combine(destination, file.Name);
            await Task.Run(() => file.CopyTo(ToLongPath(tempPath), true));
            Trace.WriteLine($"Copying {file} to {tempPath}");

            return true;
        }));

        await Task.WhenAll(sourceDirectory.GetDirectories().Select(subdir =>
        {
            var tempPath = Path.Combine(destination, subdir.Name);
            return DirectoryCopyAsync(subdir.FullName, tempPath, true);
        }));
        return true;
    }

    public static async Task DeleteBackup(string backupName)
    {
        var directoryPath = Path.Combine(backupDirectory, backupName);
        var zipPath = directoryPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? directoryPath
            : directoryPath + ".zip";

        if (File.Exists(zipPath))
            File.Delete(zipPath);
        else if (Directory.Exists(directoryPath))
            await DeleteDirectoryAsync(directoryPath);
    }

    public static async Task<string> CreateConfig()
    {
        var version = MinecraftGame.GetVersion();
        await Task.CompletedTask;

        var backupConfig = new BackupConfiguration
        {
            BackupTime = DateTime.Now,
            MinecraftVersion = version.ToString(),
            BackupId = Guid.NewGuid(),
        };

        return JsonSerializer.Serialize(backupConfig);
    }

    public static async Task<BackupConfiguration> GetConfig(string backupName)
    {
        var path = Path.Combine(backupDirectory, backupName, "BackupConfig.json");

        if (!File.Exists(path))
        {
            return null;
        }

        using (var openStream = File.OpenRead(path))
        {
            return await JsonSerializer.DeserializeAsync<BackupConfiguration>(openStream).ConfigureAwait(false);
        }
    }

    private static async Task DeleteDirectoryAsync(string targetDir)
    {
        var files = Directory.GetFiles(ToLongPath(targetDir));
        var dirs = Directory.GetDirectories(ToLongPath(targetDir));

        await Task.WhenAll(files.Select(async file =>
        {
            File.SetAttributes(file, FileAttributes.Normal);
            await Task.Run(() => File.Delete(file));
        }));

        await Task.WhenAll(dirs.Select(DeleteDirectoryAsync));

        Directory.Delete(ToLongPath(targetDir), false);
    }
    private static async Task DirectoryCopyAsync(string sourceDirName, string destDirName, bool copySubDirs)
    {
        DirectoryInfo dir = new DirectoryInfo(ToLongPath(sourceDirName));
        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + sourceDirName);
        }

        Directory.CreateDirectory(ToLongPath(destDirName));

        var files = dir.GetFiles();
        await Task.WhenAll(files.Select(async file =>
        {
            string tempPath = Path.Combine(destDirName, file.Name);
            await Task.Run(() =>
            {
                try
                {
                    file.CopyTo(ToLongPath(tempPath), true);
                }
                catch (Exception e)
                {
                    Trace.WriteLine(e);
                }
            });
            Trace.WriteLine("Copying " + file + " to " + tempPath);
        }));

        if (copySubDirs)
        {
            var dirs = dir.GetDirectories();
            await Task.WhenAll(dirs.Select(subdir =>
            {
                string tempPath = Path.Combine(destDirName, subdir.Name);
                return DirectoryCopyAsync(subdir.FullName, tempPath, copySubDirs);
            }));
        }
    }

}
