using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections;
using System.Linq;
using System.Runtime.Serialization.Json;
using Flarial.Launcher.Services.Core;
using Flarial.Launcher.Services.Networking;

namespace Flarial.Launcher.Services.Management.Versions;

public sealed class VersionCatalog
{
    VersionCatalog(HashSet<string> supported, SortedDictionary<string, VersionEntry> entries, List<CatalogEntry> installableEntries)
        => (_supported, _entries, _installableEntries) = (supported, entries, installableEntries);

    static readonly Comparer s_comparer = new();
    const string SupportedUri = "https://cdn.flarial.xyz/launcher/Supported.json";
    static readonly DataContractJsonSerializer s_supportedSerializer = new(typeof(Dictionary<string, bool>), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

    static string WithCacheBust(string uri) => $"{uri}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

    static void Log(string message)
    {
        try
        {
            var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flarial", "Launcher", "Logs");
            Directory.CreateDirectory(basePath);
            var logPath = Path.Combine(basePath, "launcher.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] {message}{Environment.NewLine}");
        }
        catch { }
    }

    readonly HashSet<string> _supported;
    readonly SortedDictionary<string, VersionEntry> _entries;
    readonly List<CatalogEntry> _installableEntries;

    public VersionEntry this[string version] => _entries[version];
    public IEnumerable<string> InstallableVersions => _entries.Keys;
    public IEnumerable<CatalogEntry> InstallableEntries => _installableEntries;
    public bool IsSupported
    {
        get
        {
            var version = Minecraft.Version;
            return _supported.Contains(version) || _supported.Any(_ => version.StartsWith(_ + '.', StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class CatalogEntry
    {
        internal CatalogEntry(string version, VersionEntry entry)
            => (Version, Entry) = (version, entry);

        public string Version { get; }
        public VersionEntry Entry { get; }
    }

    static async Task<HashSet<string>> SupportedAsync()
    {
        HashSet<string> supported = [];

        static string Normalize(string value)
        {
            var dots = value.Count(_ => _ == '.');
            if (dots < 3)
                return value;

            var last = value.LastIndexOf('.');
            return last > 0 ? value.Substring(0, last) : value;
        }
        try
        {
            using var stream = await HttpService.GetAsync<Stream>(WithCacheBust(SupportedUri));
            var payload = s_supportedSerializer.ReadObject(stream);
            var values = (Dictionary<string, bool>)payload;

            foreach (var item in values)
            {
                if (!item.Value)
                    continue;

                var value = item.Key.Trim();
                if (value.Length == 0)
                    continue;

                supported.Add(Normalize(value));
            }
        }
        catch
        {
            // Supported.json is the only source.
        }

        return supported;
    }

    public static async Task<VersionCatalog> GetAsync()
    {
        var supportedTask = SupportedAsync();
        var tasks = new Task<Dictionary<string, VersionEntry>>[2];
        tasks[0] = UWPVersionEntry.GetAsync();
        tasks[1] = GDKVersionEntry.GetAsync();
        await Task.WhenAll(tasks[0], tasks[1], supportedTask);

        var supported = await supportedTask;

        List<CatalogEntry> installableEntries = [];
        SortedDictionary<string, VersionEntry> entries = new(s_comparer);
        foreach (var item in await tasks[0]) installableEntries.Add(new(item.Key, item.Value));
        foreach (var item in await tasks[1]) installableEntries.Add(new(item.Key, item.Value));

        foreach (var item in installableEntries)
            supported.Add(item.Version);

        installableEntries.Sort((x, y) => s_comparer.Compare(x.Version, y.Version));

        foreach (var item in installableEntries)
        {
            if (!entries.ContainsKey(item.Version))
                entries.Add(item.Version, item.Entry);
        }

        var uwpLatest = installableEntries.FirstOrDefault(_ => _.Entry is UWPVersionEntry)?.Version ?? "n/a";
        var gdkLatest = installableEntries.FirstOrDefault(_ => _.Entry is GDKVersionEntry)?.Version ?? "n/a";
        Log($"VersionCatalog loaded | UWP latest={uwpLatest} | GDK latest={gdkLatest} | supported={supported.Count} | installable={installableEntries.Count}");

        return new(supported, entries, installableEntries);
    }

    sealed class Comparer : IComparer<string>
    {
        public int Compare(string x, string y) => new Version(y).CompareTo(new Version(x));
    }
}
