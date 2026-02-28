using System.IO;
namespace Flarial.Launcher.Services.Modding;

public unsafe sealed class ModificationLibrary
{
    public readonly string FileName;

    public readonly bool IsValid;

    public readonly bool Exists;

    static bool IsSupportedExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".dll", System.StringComparison.OrdinalIgnoreCase);
    }

    public ModificationLibrary(string path)
    {
        FileName = Path.GetFullPath(path);
        Exists = File.Exists(FileName) && IsSupportedExtension(FileName);
        IsValid = Exists;
    }

    public static implicit operator ModificationLibrary(string @this) => new(@this);
}
