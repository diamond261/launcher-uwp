using System;
using System.IO;

namespace Flarial.Launcher.Managers;

public static class VersionManagement
{
    public static string launcherPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Flarial",
        "Launcher");
}
