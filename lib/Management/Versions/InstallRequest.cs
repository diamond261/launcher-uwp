using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Flarial.Launcher.Services.Networking;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace Flarial.Launcher.Services.Management.Versions;

public sealed class InstallRequest
{
    static readonly PackageManager s_manager = new();
    static readonly string s_path = Path.GetTempPath();
    static readonly ConcurrentDictionary<string, bool> s_paths = [];
    static readonly AddPackageOptions s_options = new() { ForceAppShutdown = true, ForceUpdateFromAnyVersion = true };

    static InstallRequest() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        foreach (var path in s_paths)
            try { File.Delete(path.Key); }
            catch { }
    };

    static string NormalizeUri(string uri) => uri;

    static async Task DeployAsync(string path, Action<int> action)
    {
        TaskCompletionSource<bool> source = new();
        var completed = false;
        var display = 90;

        var operation = s_manager.AddPackageByUriAsync(new(path), s_options);
        operation.Progress += (sender, args) =>
        {
            display = Math.Max(display, 90 + ((int)args.percentage * 10 / 100));
            action(display);
        };

        operation.Completed += (sender, args) =>
        {
            completed = true;
            if (sender.Status is AsyncStatus.Error) source.TrySetException(sender.ErrorCode);
            else
            {
                action(100);
                source.TrySetResult(new());
            }
        };

        _ = Task.Run(async () =>
        {
            while (!completed)
            {
                await Task.Delay(1200);
                if (completed)
                    break;

                if (display < 99)
                    display++;

                action(display);
            }
        });

        var finished = await Task.WhenAny(source.Task, Task.Delay(TimeSpan.FromMinutes(10))) == source.Task;
        if (!finished)
            throw new TimeoutException("Package install timed out. Windows may still be installing it in the background.");

        await source.Task;
        action(100);
    }

    static bool IsValidPackageFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            return false;

        if (info.Length < 100L * 1024 * 1024)
            return false;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = new byte[4];
        if (stream.Read(header, 0, header.Length) != header.Length)
            return false;

        return header[0] == (byte)'P' && header[1] == (byte)'K';
    }

    public static async Task InstallAsync(string[] uris, string path, Action<int> action)
    {
        Exception? lastException = null;

        var candidates = uris
            .Where(_ => !string.IsNullOrWhiteSpace(_))
            .Select(NormalizeUri)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException("No package URLs available.");

        foreach (var uri in candidates)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await HttpService.DownloadAsync(uri, path, (_) => action(_ * 90 / 100));
                    if (!IsValidPackageFile(path))
                        throw new InvalidDataException($"Downloaded package failed validation from {uri}");

                    await DeployAsync(path, action);
                    return;
                }
                catch (Exception exception) when (exception is IOException || exception is HttpRequestException)
                {
                    lastException = exception;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                }

                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception cleanupException)
                {
                    lastException = cleanupException;
                }

                await Task.Delay(600 + (attempt * 700));
            }
        }

        throw lastException ?? new Exception("Download failed.");
    }

    readonly Task _task;
    readonly string _path;

    internal InstallRequest(string[] uris, Action<int> action)
    {
        _path = Path.Combine(s_path, Path.GetRandomFileName() + ".msixvc");
        s_paths.TryAdd(_path, new());

        _task = InstallAsync(uris, _path, action);
        _task.ContinueWith(delegate
        {
            try
            {
                File.Delete(_path);
                s_paths.TryRemove(_path, out _);
            }
            catch { }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    public TaskAwaiter GetAwaiter() => _task.GetAwaiter();

    ~InstallRequest()
    {
        try
        {
            File.Delete(_path);
            s_paths.TryRemove(_path, out _);
        }
        catch { }
    }
}
