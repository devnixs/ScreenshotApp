using System.Text.Json;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Settings;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class JsonSettingsStoreTests
{
    [Fact] public void DefaultsMatchPrd()
    {
        var v=SettingsValues.Defaults;
        Assert.False(v.LaunchAtLogin); Assert.Null(v.SaveFolder); Assert.Equal(ScreenshotDestination.Both,v.ScreenshotDestination); Assert.True(v.PlayShutterSound);
        Assert.False(v.RecordMicrophone); Assert.Equal(RecordingFormat.Mp4,v.RecordingFormat); Assert.Equal(Mp4Quality.High,v.Mp4Quality); Assert.Equal(GifQuality.Medium,v.GifQuality);
        Assert.Equal(RgbaColor.Red,v.AnnotationColor); Assert.Equal(StrokeWidth.M,v.AnnotationStrokeWidth);
        Assert.Equal(KeyChord.Create(KeyModifiers.Ctrl|KeyModifiers.Shift,0x34),v.ScreenshotShortcut);
        Assert.Equal(KeyChord.Create(KeyModifiers.Ctrl|KeyModifiers.Shift,0x35),v.RecordShortcut);
        Assert.Equal(KeyChord.Create(KeyModifiers.Ctrl|KeyModifiers.Shift,0x36),v.AnnotateShortcut);
    }
    [Fact] public void DefaultFilePathUsesSuppliedAppData() => Assert.Equal(Path.Combine(@"C:\Users\a\AppData\Roaming","Snapper","settings.json"),JsonSettingsStore.DefaultFilePath(@"C:\Users\a\AppData\Roaming"));
    [Theory] [InlineData("")] [InlineData("not json")] [InlineData("[]")] [InlineData("42")] [InlineData("null")] [InlineData("{}")]
    public void EmptyAndInvalidGiveDefaults(string json) => Assert.Equal(SettingsValues.Defaults,JsonSettingsStore.Parse(json));
    [Fact] public void SerializePinsSchemaAndCanonicalModifierOrder()
    {
        var v=SettingsValues.Defaults with { LaunchAtLogin=true, SaveFolder=@"D:\Shots", ScreenshotDestination=ScreenshotDestination.Clipboard, PlayShutterSound=false, RecordMicrophone=true, RecordingFormat=RecordingFormat.Gif, Mp4Quality=Mp4Quality.Low, GifQuality=GifQuality.High, AnnotationColor=new(0.1,0.2,0.3,0.5), AnnotationStrokeWidth=StrokeWidth.L, ScreenshotShortcut=KeyChord.Create(KeyModifiers.Ctrl|KeyModifiers.Alt|KeyModifiers.Shift,65), RecordShortcut=KeyChord.Create(KeyModifiers.Alt,0x70), AnnotateShortcut=null };
        var text=JsonSettingsStore.Serialize(v); using var d=JsonDocument.Parse(text); var r=d.RootElement;
        Assert.Equal(new[] {"version","launchAtLogin","saveFolder","screenshotDestination","playShutterSound","recordMicrophone","recordingFormat","mp4Quality","gifQuality","annotationColor","annotationStrokeWidth","shortcuts"},r.EnumerateObject().Select(p=>p.Name));
        Assert.Equal(1,r.GetProperty("version").GetInt32()); Assert.Equal("clipboard",r.GetProperty("screenshotDestination").GetString());
        Assert.Equal("gif",r.GetProperty("recordingFormat").GetString()); Assert.Equal("low",r.GetProperty("mp4Quality").GetString()); Assert.Equal("high",r.GetProperty("gifQuality").GetString()); Assert.Equal("l",r.GetProperty("annotationStrokeWidth").GetString());
        Assert.Equal(new[] {"red","green","blue","alpha"},r.GetProperty("annotationColor").EnumerateObject().Select(p=>p.Name));
        var chord=r.GetProperty("shortcuts").GetProperty("screenshot");
        Assert.Equal(new[] {"modifiers","key"},chord.EnumerateObject().Select(p=>p.Name));
        Assert.Equal(new[] {"Ctrl","Alt","Shift"},chord.GetProperty("modifiers").EnumerateArray().Select(p=>p.GetString())); Assert.Equal(65,chord.GetProperty("key").GetInt32());
        Assert.DoesNotContain('\r',text); Assert.Contains("\n  \"version\": 1",text); Assert.Equal(v,JsonSettingsStore.Parse(text));
    }
    [Fact] public void DefaultFolderIsOmittedAndClearedChordIsNull()
    {
        using var d=JsonDocument.Parse(JsonSettingsStore.Serialize(SettingsValues.Defaults with { AnnotateShortcut=null }));
        Assert.False(d.RootElement.TryGetProperty("saveFolder",out _)); Assert.Equal(JsonValueKind.Null,d.RootElement.GetProperty("shortcuts").GetProperty("annotate").ValueKind);
    }
    [Fact] public void RoundTripsEveryValueExactly()
    {
        var values=new List<SettingsValues> { SettingsValues.Defaults with {LaunchAtLogin=true}, SettingsValues.Defaults with {PlayShutterSound=false},SettingsValues.Defaults with {RecordMicrophone=true},SettingsValues.Defaults with {AnnotationColor=new(0.1,0.2,0.3,0.5)},SettingsValues.Defaults with {AnnotationColor=new(1.0/3,1.0/3,1.0/3)},SettingsValues.Defaults with {SaveFolder=@"D:\Écrans\Snapper"},SettingsValues.Defaults with {SaveFolder=@"\\nas\shots"},SettingsValues.Defaults with {ScreenshotShortcut=null,RecordShortcut=null,AnnotateShortcut=null},SettingsValues.Defaults with {ScreenshotShortcut=KeyChord.Create(KeyModifiers.Alt,0x70)} };
        values.AddRange(Enum.GetValues<ScreenshotDestination>().Select(v=>SettingsValues.Defaults with {ScreenshotDestination=v}));
        values.AddRange(Enum.GetValues<RecordingFormat>().Select(v=>SettingsValues.Defaults with {RecordingFormat=v}));
        values.AddRange(Enum.GetValues<Mp4Quality>().Select(v=>SettingsValues.Defaults with {Mp4Quality=v}));
        values.AddRange(Enum.GetValues<GifQuality>().Select(v=>SettingsValues.Defaults with {GifQuality=v}));
        values.AddRange(Enum.GetValues<StrokeWidth>().Select(v=>SettingsValues.Defaults with {AnnotationStrokeWidth=v}));
        values.AddRange(RgbaColor.Presets.Select(v=>SettingsValues.Defaults with {AnnotationColor=v}));
        foreach(var value in values) Assert.Equal(value,JsonSettingsStore.Parse(JsonSettingsStore.Serialize(value)));
    }
    [Theory] [InlineData("\"nope\"")] [InlineData("0")] [InlineData("1")] [InlineData("null")]
    public void InvalidBoolsFallBackIndividually(string raw)
    {
        var v=JsonSettingsStore.Parse("{\"playShutterSound\":"+raw+",\"launchAtLogin\":"+raw+",\"recordMicrophone\":true}");
        Assert.True(v.PlayShutterSound); Assert.False(v.LaunchAtLogin); Assert.True(v.RecordMicrophone);
    }
    [Theory]
    [InlineData("screenshotDestination","\"printer\"")] [InlineData("screenshotDestination","42")] [InlineData("screenshotDestination","\"Both\"")]
    [InlineData("annotationStrokeWidth","\"xl\"")] [InlineData("annotationStrokeWidth","\"M\"")]
    [InlineData("recordingFormat","\"webm\"")] [InlineData("mp4Quality","\"ultra\"")] [InlineData("gifQuality","\"ultra\"")] [InlineData("gifQuality","2")]
    public void InvalidEnumsFallBackIndividually(string key,string raw) => Assert.Equal(SettingsValues.Defaults with {RecordMicrophone=true},JsonSettingsStore.Parse("{\""+key+"\":"+raw+",\"recordMicrophone\":true}"));
    [Theory]
    [InlineData("")] [InlineData("   ")] [InlineData(@"Pictures\Screenshots")] [InlineData(@"\Shots")] [InlineData("C:Shots")] [InlineData(@"~\Pictures")]
    [InlineData(@"%USERPROFILE%\Pictures")] [InlineData(@"C:\a<b")] [InlineData(@"C:\a>b")] [InlineData("C:\\a\"b")] [InlineData(@"C:\a|b")] [InlineData(@"C:\a?b")] [InlineData(@"C:\a*b")]
    [InlineData(@"C:\a:b")] [InlineData(@"\\server")] [InlineData(@"\\server\")] [InlineData(@"\\\share")] [InlineData("C:\\a\u0001b")]
    public void InvalidFoldersFallBack(string folder) => Assert.Null(JsonSettingsStore.Parse("{\"saveFolder\":"+JsonSerializer.Serialize(folder)+"}").SaveFolder);
    [Fact] public void NonStringFolderFallsBack()
    {
        Assert.Null(JsonSettingsStore.Parse("{\"saveFolder\":42}").SaveFolder); Assert.Null(JsonSettingsStore.Parse("{\"saveFolder\":null}").SaveFolder);
    }
    [Theory] [InlineData(@"C:\Users\a\Pictures\Screenshots")] [InlineData(@"d:\")] [InlineData(@"D:\Shots\")] [InlineData(@"\\nas\shots")] [InlineData(@"\\nas\shots\2026")] [InlineData(@"D:\Écrans\Snapper")]
    public void ValidFoldersRemainUnchanged(string folder) => Assert.Equal(folder,JsonSettingsStore.Parse("{\"saveFolder\":"+JsonSerializer.Serialize(folder)+"}").SaveFolder);
    [Theory]
    [InlineData("\"red\"")] [InlineData("[1,0,0,1]")]
    [InlineData("{\"red\":2,\"green\":0,\"blue\":0,\"alpha\":1}")] [InlineData("{\"red\":-0.1,\"green\":0,\"blue\":0,\"alpha\":1}")]
    [InlineData("{\"red\":0,\"green\":0,\"blue\":0}")] [InlineData("{\"red\":\"1\",\"green\":0,\"blue\":0,\"alpha\":1}")]
    [InlineData("{\"red\":1e9999,\"green\":0,\"blue\":0,\"alpha\":1}")]
    public void InvalidColorsFallBack(string raw) => Assert.Equal(RgbaColor.Red,JsonSettingsStore.Parse("{\"annotationColor\":"+raw+"}").AnnotationColor);
    [Fact] public void ExtraColorMembersAreIgnored() => Assert.Equal(RgbaColor.Black,JsonSettingsStore.Parse("{\"annotationColor\":{\"red\":0,\"green\":0,\"blue\":0,\"alpha\":1,\"extra\":5}}").AnnotationColor);
    [Theory]
    [InlineData("{\"modifiers\":[\"Ctrl\",\"Hyper\"],\"key\":65}")] [InlineData("{\"modifiers\":[\"Ctrl\",\"Ctrl\"],\"key\":65}")]
    [InlineData("{\"modifiers\":\"Ctrl\",\"key\":65}")] [InlineData("{\"modifiers\":[\"Ctrl\"],\"key\":\"A\"}")]
    [InlineData("{\"modifiers\":[\"Ctrl\"],\"key\":16}")] [InlineData("{\"modifiers\":[\"Ctrl\"],\"key\":300}")] [InlineData("{\"modifiers\":[\"Ctrl\"],\"key\":0}")] [InlineData("{\"modifiers\":[\"Ctrl\"],\"key\":255}")]
    [InlineData("{\"modifiers\":[\"Ctrl\",\"Win\"],\"key\":65}")] [InlineData("{\"modifiers\":[\"Shift\"],\"key\":65}")]
    [InlineData("{\"modifiers\":[1],\"key\":65}")] [InlineData("{\"modifiers\":[\"Ctrl\"],\"key\":65.1}")]
    public void InvalidChordFallsBack(string raw) => Assert.Equal(SettingsValues.Defaults.AnnotateShortcut,JsonSettingsStore.Parse("{\"shortcuts\":{\"annotate\":"+raw+"}}").AnnotateShortcut);
    [Fact] public void ShortcutAbsenceAndNullAreDistinctAndOrderIsIgnored()
    {
        Assert.Equal(SettingsValues.Defaults,JsonSettingsStore.Parse("{\"shortcuts\":5}"));
        var value=JsonSettingsStore.Parse("{\"shortcuts\":{\"record\":null,\"annotate\":{\"modifiers\":[\"Shift\",\"Ctrl\"],\"key\":65}}}");
        Assert.Equal(SettingsValues.Defaults.ScreenshotShortcut,value.ScreenshotShortcut); Assert.Null(value.RecordShortcut); Assert.Equal(KeyChord.Create(KeyModifiers.Ctrl|KeyModifiers.Shift,65),value.AnnotateShortcut);
    }
    [Theory]
    [InlineData("{\"screenshot\":{\"modifiers\":[\"Ctrl\",\"Shift\"],\"key\":52},\"record\":{\"modifiers\":[\"Ctrl\",\"Shift\"],\"key\":52}}",false)]
    [InlineData("{\"screenshot\":{\"modifiers\":[\"Ctrl\",\"Shift\"],\"key\":53},\"record\":{\"modifiers\":[\"Ctrl\",\"Shift\"],\"key\":53},\"annotate\":{\"modifiers\":[\"Ctrl\",\"Shift\"],\"key\":52}}",true)]
    public void DuplicatesResolveInScreenshotRecordAnnotateOrder(string raw,bool cleared)
    {
        var value=JsonSettingsStore.Parse("{\"shortcuts\":"+raw+"}");
        Assert.Equal(cleared?null:SettingsValues.Defaults.RecordShortcut,value.RecordShortcut);
        if(cleared) Assert.Equal(SettingsValues.Defaults.ScreenshotShortcut,value.AnnotateShortcut);
    }
    [Fact] public void ClearedShortcutsAreNeverDuplicates()
    {
        var v=JsonSettingsStore.Parse("{\"shortcuts\":{\"screenshot\":null,\"record\":null,\"annotate\":null}}"); Assert.Null(v.ScreenshotShortcut);Assert.Null(v.RecordShortcut);Assert.Null(v.AnnotateShortcut);
    }
    [Fact] public void HandEditsAndFutureVersionsAreToleratedWithoutCaseFolding()
    {
        Assert.False(JsonSettingsStore.Parse("{ // c\n \"playShutterSound\":false, }").PlayShutterSound);
        Assert.True(JsonSettingsStore.Parse("{\"PlayShutterSound\":false}").PlayShutterSound);
        Assert.Equal(SettingsValues.Defaults with {RecordMicrophone=true},JsonSettingsStore.Parse("{\"version\":99,\"foo\":1,\"recordMicrophone\":true}"));
    }
}
