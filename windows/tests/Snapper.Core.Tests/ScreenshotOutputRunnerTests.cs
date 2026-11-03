using Snapper.Core.Capture;
using Snapper.Core.Settings;
namespace Snapper.Core.Tests;
public sealed class ScreenshotOutputRunnerTests
{
    private sealed class Environment : IScreenshotOutputEnvironment
    {
        public List<string> Calls {get;}=[];
        public bool CopyResult {get;set;}=true;
        public Queue<Exception?> Saves {get;}=new();
        public Queue<string?> Folders {get;}=new();
        public bool CopyToClipboard(){Calls.Add("Copy");return CopyResult;}
        public string Save(string folder){Calls.Add("Save:"+folder);if(Saves.Count>0&&Saves.Dequeue() is {} error)throw error;return folder+"/shot.png";}
        public string? ShowSaveFolderFailure(string folder,Exception error){Calls.Add("Folder:"+folder);return Folders.Count>0?Folders.Dequeue():null;}
        public void ShowError(string title,string message)=>Calls.Add(title+"|"+message);
    }
    [Theory] [InlineData(ScreenshotDestination.Both)] [InlineData(ScreenshotDestination.File)] [InlineData(ScreenshotDestination.Clipboard)]
    public void DestinationOrder(ScreenshotDestination destination)
    {
        var e=new Environment();var r=ScreenshotOutputRunner.Run(new(destination),"A",e);
        Assert.Equal(destination!=ScreenshotDestination.File,r.Copied);
        Assert.Equal(destination==ScreenshotDestination.Clipboard?null:"A/shot.png",r.SavedPath);
        Assert.Equal(destination switch{ScreenshotDestination.Both=>new[]{"Copy","Save:A"},ScreenshotDestination.File=>["Save:A"],_=>["Copy"]},e.Calls);
    }
    [Fact] public void FailedCopyStillSaves()
    {
        var e=new Environment{CopyResult=false};var r=ScreenshotOutputRunner.Run(new(ScreenshotDestination.Both),"A",e);
        Assert.False(r.Copied);Assert.Equal("A/shot.png",r.SavedPath);
        Assert.Equal(new[]{"Copy","Couldn't copy to clipboard|The clipboard couldn't accept the screenshot.","Save:A"},e.Calls);
    }
    [Fact] public void FolderCancelRetainsClipboard()
    {
        var e=new Environment();e.Saves.Enqueue(new UnauthorizedAccessException());
        var r=ScreenshotOutputRunner.Run(new(ScreenshotDestination.Both),"A",e);
        Assert.True(r.Copied);Assert.Null(r.SavedPath);Assert.Equal(new[]{"Copy","Save:A","Folder:A"},e.Calls);
    }
    [Fact] public void RetryUsesChosenFolder()
    {
        var e=new Environment();e.Saves.Enqueue(new UnauthorizedAccessException());e.Folders.Enqueue("B");
        var r=ScreenshotOutputRunner.Run(new(ScreenshotDestination.File),"A",e);
        Assert.Equal("B/shot.png",r.SavedPath);Assert.Equal(new[]{"Save:A","Folder:A","Save:B"},e.Calls);
    }
    [Fact] public void RetryFailureShowsSecondAlertWithoutThirdSave()
    {
        var e=new Environment();e.Saves.Enqueue(new UnauthorizedAccessException());e.Saves.Enqueue(new DirectoryNotFoundException());e.Folders.Enqueue("B");e.Folders.Enqueue("C");
        Assert.Null(ScreenshotOutputRunner.Run(new(ScreenshotDestination.File),"A",e).SavedPath);
        Assert.Equal(new[]{"Save:A","Folder:A","Save:B","Folder:B"},e.Calls);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void GenericFailureNeverOffersFolder(bool duringRetry)
    {
        var e=new Environment();if(duringRetry){e.Saves.Enqueue(new UnauthorizedAccessException());e.Folders.Enqueue("B");}
        e.Saves.Enqueue(new IOException("full",unchecked((int)0x80070070)));
        ScreenshotOutputRunner.Run(new(ScreenshotDestination.File),"A",e);
        Assert.Equal(duringRetry?new[]{"Save:A","Folder:A","Save:B","Couldn't save screenshot|full"}:new[]{"Save:A","Couldn't save screenshot|full"},e.Calls);
    }
}
