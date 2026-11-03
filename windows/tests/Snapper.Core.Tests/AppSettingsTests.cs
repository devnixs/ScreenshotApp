using Snapper.Core.Annotate.Model;
using Snapper.Core.Settings;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;

internal sealed class InMemorySettingsStore : ISettingsStore
{
    public SettingsValues Values {get;set;}=SettingsValues.Defaults;
    public List<SettingsValues> Saves {get;}=[];
    public SettingsSaveException? Error {get;set;}
    public Action? OnSave {get;set;}
    public int Loads {get;private set;}
    public SettingsValues Load() {Loads++;return Values;}
    public void Save(SettingsValues values) {OnSave?.Invoke();if(Error is not null)throw Error;Saves.Add(values);Values=values;}
}
public sealed class AppSettingsTests : IDisposable
{
    private const string DefaultFolder=@"C:\Users\a\Pictures\Screenshots";
    private readonly string directory=Path.Combine(Path.GetTempPath(),"SnapperTests",Guid.NewGuid().ToString("N"));
    [Fact] public void DefaultsOnEmptyStore()
    {
        var store=new InMemorySettingsStore();var s=new AppSettings(store,DefaultFolder);
        Assert.False(s.LaunchAtLogin);Assert.Equal(DefaultFolder,s.SaveFolder);Assert.False(s.HasCustomSaveFolder);Assert.Equal(ScreenshotDestination.Both,s.ScreenshotDestination);Assert.True(s.PlayShutterSound);Assert.False(s.RecordMicrophone);
        Assert.Equal(RecordingFormat.Mp4,s.RecordingFormat);Assert.Equal(Mp4Quality.High,s.Mp4Quality);Assert.Equal(GifQuality.Medium,s.GifQuality);Assert.Equal(RgbaColor.Red,s.AnnotationColor);Assert.Equal(StrokeWidth.M,s.AnnotationStrokeWidth);
        Assert.Equal(SettingsValues.Defaults.ScreenshotShortcut,s.ScreenshotShortcut);Assert.Equal(SettingsValues.Defaults.RecordShortcut,s.RecordShortcut);Assert.Equal(SettingsValues.Defaults.AnnotateShortcut,s.AnnotateShortcut);Assert.Equal(SettingsValues.Defaults,s.Snapshot());
    }
    [Fact] public void ConstructionLoadsOnceAndWritesNothing()
    {
        var store=new InMemorySettingsStore();_=new AppSettings(store,DefaultFolder);Assert.Equal(1,store.Loads);Assert.Empty(store.Saves);
        _=new AppSettings(new JsonSettingsStore(Path.Combine(directory,"settings.json")),DefaultFolder);Assert.False(Directory.Exists(directory));
    }
    [Fact] public void EveryPropertyRoundTripsThroughRealFile()
    {
        Action<AppSettings>[] mutations=[s=>s.LaunchAtLogin=true,s=>s.SaveFolder=@"D:\Shots",s=>s.ScreenshotDestination=ScreenshotDestination.File,s=>s.ScreenshotDestination=ScreenshotDestination.Clipboard,s=>s.PlayShutterSound=false,s=>s.RecordMicrophone=true,s=>s.RecordingFormat=RecordingFormat.Gif,s=>s.Mp4Quality=Mp4Quality.Medium,s=>s.Mp4Quality=Mp4Quality.Low,s=>s.GifQuality=GifQuality.High,s=>s.GifQuality=GifQuality.Low,s=>s.AnnotationColor=RgbaColor.Blue,s=>s.AnnotationColor=new RgbaColor(0.1,0.2,0.3,0.5),s=>s.AnnotationStrokeWidth=StrokeWidth.S,s=>s.AnnotationStrokeWidth=StrokeWidth.L,s=>s.ScreenshotShortcut=KeyChord.Create(KeyModifiers.Alt,0x70),s=>s.RecordShortcut=null,s=>s.AnnotateShortcut=null];
        foreach(var change in mutations)
        {
            var store=new JsonSettingsStore(Path.Combine(directory,Guid.NewGuid().ToString("N"),"settings.json"));var first=new AppSettings(store,DefaultFolder);change(first);Assert.Equal(first.Snapshot(),new AppSettings(store,DefaultFolder).Snapshot());
        }
    }
    [Fact] public void SetterSavesBeforeSingleNamedNotification()
    {
        var log=new List<string>();var store=new InMemorySettingsStore {OnSave=()=>log.Add("save")};var s=new AppSettings(store,DefaultFolder);
        s.PropertyChanged+=(_,args)=>log.Add(args.PropertyName??"");s.RecordMicrophone=true;
        Assert.Equal(new[] {"save","RecordMicrophone"},log);Assert.Equal(SettingsValues.Defaults with {RecordMicrophone=true},Assert.Single(store.Saves));
    }
    [Fact] public void SameAssignmentsAreNoOpsForEveryProperty()
    {
        var store=new InMemorySettingsStore();var s=new AppSettings(store,DefaultFolder);var notifications=0;s.PropertyChanged+=(_,_)=>notifications++;
        s.LaunchAtLogin=s.LaunchAtLogin;s.ScreenshotDestination=s.ScreenshotDestination;s.PlayShutterSound=s.PlayShutterSound;s.RecordMicrophone=s.RecordMicrophone;s.RecordingFormat=s.RecordingFormat;s.Mp4Quality=s.Mp4Quality;s.GifQuality=s.GifQuality;s.AnnotationColor=s.AnnotationColor;s.AnnotationStrokeWidth=s.AnnotationStrokeWidth;s.ScreenshotShortcut=s.ScreenshotShortcut;s.RecordShortcut=s.RecordShortcut;s.AnnotateShortcut=s.AnnotateShortcut;
        Assert.Empty(store.Saves);Assert.Equal(0,notifications);
    }
    [Fact] public void DefaultFolderFollowsInjectedPathUntilChosen()
    {
        var store=new InMemorySettingsStore();var s=new AppSettings(store,DefaultFolder);s.PlayShutterSound=false;
        Assert.Null(Assert.Single(store.Saves).SaveFolder);Assert.Equal(@"D:\Redirected",new AppSettings(store,@"D:\Redirected").SaveFolder);
        s.SaveFolder=DefaultFolder;Assert.True(s.HasCustomSaveFolder);Assert.Equal(DefaultFolder,s.Snapshot().SaveFolder);Assert.Equal(DefaultFolder,new AppSettings(store,@"D:\Redirected").SaveFolder);
        var count=store.Saves.Count;s.SaveFolder=DefaultFolder;Assert.Equal(count,store.Saves.Count);
    }
    [Fact] public void InvalidFolderThrowsWithoutChangingOrSaving()
    {
        var store=new InMemorySettingsStore();var s=new AppSettings(store,DefaultFolder);
        Assert.Throws<ArgumentException>(()=>s.SaveFolder="Shots");Assert.Equal(DefaultFolder,s.SaveFolder);Assert.False(s.HasCustomSaveFolder);Assert.Empty(store.Saves);
    }
    [Fact] public void SaveFailureKeepsMemoryRaisesFailureAndStillNotifies()
    {
        var failure=new SettingsSaveException(new IOException("denied"));var store=new InMemorySettingsStore {Error=failure};var s=new AppSettings(store,DefaultFolder);var log=new List<string>();
        s.SaveFailed+=(_,args)=>{Assert.Same(failure,args.Exception);log.Add("failure");};s.PropertyChanged+=(_,args)=>log.Add(args.PropertyName??"");
        s.RecordMicrophone=true;Assert.True(s.RecordMicrophone);Assert.Equal(new[] {"failure","RecordMicrophone"},log);
        store.Error=null;s.PlayShutterSound=false;Assert.Equal(SettingsValues.Defaults with {RecordMicrophone=true,PlayShutterSound=false},Assert.Single(store.Saves));
    }
    [Fact] public void ShortcutCanBeClearedAndReloaded()
    {
        var store=new JsonSettingsStore(Path.Combine(directory,"settings.json"));var s=new AppSettings(store,DefaultFolder);s.AnnotateShortcut=null;Assert.Null(s.Snapshot().AnnotateShortcut);Assert.Null(new AppSettings(store,DefaultFolder).AnnotateShortcut);
    }
    public void Dispose() {if(Directory.Exists(directory))Directory.Delete(directory,true);}
}
