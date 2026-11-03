using Snapper.Core.Settings;
namespace Snapper.Core.Tests;
public sealed class LaunchAtLoginStateTests
{
    private const string Exe=@"C:\Apps\Snapper\Snapper.exe";
    [Fact] public void RunValueQuotesPath() => Assert.Equal("\""+Exe+"\"",LaunchAtLoginState.RunValue(Exe));
    [Theory]
    [InlineData("\"C:\\x\\Snapper.exe\"", @"C:\x\Snapper.exe")]
    [InlineData("\"C:\\x\\Snapper.exe\" --tray", @"C:\x\Snapper.exe")]
    [InlineData(@"C:\Program Files\Snapper\Snapper.exe --x", @"C:\Program Files\Snapper\Snapper.exe")]
    [InlineData("  custom  ","custom")]
    [InlineData(null,null)] [InlineData("",null)] [InlineData("  ",null)]
    [InlineData("\"C:\\x\\Snapper.exe", @"C:\x\Snapper.exe")]
    [InlineData(@"C:\x\SNAPPER.EXE --x",@"C:\x\SNAPPER.EXE")]
    public void ParsesCommandPaths(string? value,string? expected)=>Assert.Equal(expected,LaunchAtLoginState.CommandPath(value));
    [Fact] public void OffAndOnStatesComeFromCommand()
    {
        Assert.Equal(new LaunchAtLoginState(false,null),LaunchAtLoginState.From(null,Exe,null));
        Assert.Equal(new LaunchAtLoginState(true,null),LaunchAtLoginState.From(LaunchAtLoginState.RunValue(Exe),Exe,null));
        Assert.True(LaunchAtLoginState.From(@"c:\apps\snapper\SNAPPER.EXE",Exe,null).IsOn);
        Assert.False(LaunchAtLoginState.From("\"D:\\Old\\Snapper.exe\"",Exe,null).IsOn);
        Assert.True(LaunchAtLoginState.PointsTo("  \"C:\\Apps\\Snapper\\Snapper.exe\" --tray  "," "+Exe+" "));
    }
    [Theory] [InlineData(0x03,true)] [InlineData(0x07,true)] [InlineData(0x02,false)] [InlineData(0x06,false)]
    public void StartupApprovalBit(int first,bool disabled)
    {
        var bytes=new[]{(byte)first,(byte)0};Assert.Equal(disabled,LaunchAtLoginState.IsStartupApprovedDisabled(bytes));
        var state=LaunchAtLoginState.From(LaunchAtLoginState.RunValue(Exe),Exe,bytes);
        Assert.True(state.IsOn);Assert.Equal(disabled?LaunchAtLoginNote.DisabledInStartupApps:null,state.Note);
    }
    [Fact] public void EmptyApprovalAndDisabledOtherCopyHaveNoNote()
    {
        Assert.False(LaunchAtLoginState.IsStartupApprovedDisabled(null));Assert.False(LaunchAtLoginState.IsStartupApprovedDisabled([]));
        Assert.Null(LaunchAtLoginState.From(LaunchAtLoginState.RunValue(Exe),Exe,[]).Note);
        Assert.Equal(new LaunchAtLoginState(false,null),LaunchAtLoginState.From(null,Exe,[3]));
    }
    [Fact] public void ErrorsOverrideNoteButKeepOsState()
    {
        Assert.Equal(new LaunchAtLoginState(true,LaunchAtLoginNote.Failed,"x"),LaunchAtLoginState.From(LaunchAtLoginState.RunValue(Exe),Exe,[3],"x"));
        Assert.Equal(new LaunchAtLoginState(false,LaunchAtLoginNote.Failed,"x"),LaunchAtLoginState.From(null,Exe,null,"x"));
    }
    [Fact] public void NotesMatchProductText()
    {
        Assert.Null(new LaunchAtLoginState(false,null).NoteText);
        Assert.Equal("Snapper is turned off in Startup apps.",new LaunchAtLoginState(true,LaunchAtLoginNote.DisabledInStartupApps).NoteText);
        Assert.Equal("Couldn't change launch at login: x",new LaunchAtLoginState(false,LaunchAtLoginNote.Failed,"x").NoteText);
    }
}
