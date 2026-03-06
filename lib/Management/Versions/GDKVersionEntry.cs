using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Flarial.Launcher.Services.Networking;

namespace Flarial.Launcher.Services.Management.Versions;


sealed class GDKVersionEntry : VersionEntry
{
    const string PackagesUri = "https://cdn.jsdelivr.net/gh/MinecraftBedrockArchiver/GdkLinks@latest/urls.json";

    static readonly DataContractJsonSerializer s_serializer = new(typeof(Dictionary<string, Dictionary<string, string[]>>), s_settings);

    readonly string[] _urls;

    static string WithCacheBust(string uri) => $"{uri}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

    GDKVersionEntry(string[] urls) : base() => _urls = urls;

    internal static async Task<Dictionary<string, VersionEntry>> GetAsync() => await Task.Run(async () =>
    {
        Dictionary<string, VersionEntry> entries = [];
        Dictionary<string, Dictionary<string, string[]>> collection;

        using (var stream = await HttpService.GetAsync<Stream>(WithCacheBust(PackagesUri)))
        {
            var @object = s_serializer.ReadObject(stream);
            collection = (Dictionary<string, Dictionary<string, string[]>>)@object;
        }

        foreach (var item in collection["release"])
        {
            var version = item.Key;
            var index = version.LastIndexOf('.');

            version = version.Substring(0, index);
            if (!entries.ContainsKey(version))
                entries.Add(version, new GDKVersionEntry(item.Value));
        }

        return entries;
    });

    internal override async Task<string> UriAsync() => _urls[0];

    internal override Task<string[]> UrisAsync()
    {
        var urls = _urls
            .Where(_ => !string.IsNullOrWhiteSpace(_))
            .ToArray();
        return Task.FromResult(urls);
    }
}
