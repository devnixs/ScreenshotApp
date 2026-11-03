using System.IO;
using Snapper.Core.Support;

namespace Snapper.Windows.Support;

internal static class SaveFolder
{
    private static readonly Lazy<string> defaultPath = new(() => SaveFolderPolicy.DefaultFolder(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures, Environment.SpecialFolderOption.DoNotVerify),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.GetTempPath()));
    public static string DefaultPath => defaultPath.Value;
    public static SaveFolderPolicy CreatePolicy() => new(new PhysicalSaveFolderFileSystem());
}
