using System.Text;
using Snapper.Core.Settings;
namespace Snapper.Core.Tests;
public sealed class JsonSettingsStoreFileTests : IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"SnapperTests",Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory,"nested","settings.json");
    [Fact] public void MissingFileGivesDefaultsAndCreatesNothing()
    {
        Assert.Equal(SettingsValues.Defaults,new JsonSettingsStore(FilePath).Load()); Assert.False(Directory.Exists(directory));
    }
    [Fact] public void SaveCreatesFullAtomicFileWithoutTemp()
    {
        var value=SettingsValues.Defaults with {RecordMicrophone=true};var store=new JsonSettingsStore(FilePath);store.Save(value);
        Assert.Equal(JsonSettingsStore.Serialize(value),File.ReadAllText(FilePath));Assert.Equal(value,store.Load());Assert.False(File.Exists(FilePath+".tmp"));
    }
    [Fact] public void SaveReplacesExistingFileAndStaleTemp()
    {
        var store=new JsonSettingsStore(FilePath);store.Save(SettingsValues.Defaults);File.WriteAllText(FilePath+".tmp","garbage");
        var next=SettingsValues.Defaults with {PlayShutterSound=false};store.Save(next);Assert.Equal(next,store.Load());Assert.False(File.Exists(FilePath+".tmp"));
    }
    [Fact] public void SaveFailureWrapsCauseAndPreservesPreviousFile()
    {
        Directory.CreateDirectory(directory);var blocker=Path.Combine(directory,"blocker");File.WriteAllText(blocker,"existing");
        var error=Assert.Throws<SettingsSaveException>(()=>new JsonSettingsStore(Path.Combine(blocker,"settings.json")).Save(SettingsValues.Defaults)); Assert.IsAssignableFrom<IOException>(error.InnerException);
        var store=new JsonSettingsStore(FilePath);store.Save(SettingsValues.Defaults);var old=File.ReadAllText(FilePath);Directory.CreateDirectory(FilePath+".tmp");
        Assert.Throws<SettingsSaveException>(()=>store.Save(SettingsValues.Defaults with {RecordMicrophone=true}));Assert.Equal(old,File.ReadAllText(FilePath));
    }
    [Fact] public void CorruptFileAndUnreadablePathDoNotGetRepaired()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);File.WriteAllText(FilePath,"garbage");Assert.Equal(SettingsValues.Defaults,new JsonSettingsStore(FilePath).Load());Assert.Equal("garbage",File.ReadAllText(FilePath));
        Assert.Equal(SettingsValues.Defaults,new JsonSettingsStore(directory).Load());
    }
    [Fact] public void Utf8WithoutBomKeepsNonAsciiReadable()
    {
        var value=SettingsValues.Defaults with {SaveFolder=@"D:\Écrans"};var store=new JsonSettingsStore(FilePath);store.Save(value);var bytes=File.ReadAllBytes(FilePath);
        Assert.Equal((byte)'{',bytes[0]);Assert.Contains("Écrans",Encoding.UTF8.GetString(bytes));Assert.Equal(value,store.Load());
    }
    public void Dispose() { if(Directory.Exists(directory))Directory.Delete(directory,true); }
}
