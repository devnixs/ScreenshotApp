using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class RecordingOutputFilesTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"SnapperOutputTests-"+Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Date=new(2026,11,3,12,3,4,TimeSpan.Zero);
    public RecordingOutputFilesTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root,true);
    private string Source()
    { var path=Path.Combine(root,Guid.NewGuid()+".mp4"); File.WriteAllBytes(path,[1,2,3,4]); return path; }
    [Fact] public void ProposeCreatesFolderWithoutReservingEmptyFile()
    {
        var folder=Path.Combine(root,"new"); var proposed=RecordingOutputFiles.Propose(folder,Date);
        Assert.True(Directory.Exists(folder)); Assert.False(File.Exists(proposed)); Assert.Empty(Directory.EnumerateFiles(folder));
    }
    [Fact] public void PublishesCompleteFileAndRemovesSource()
    {
        var source=Source(); var folder=Path.Combine(root,"new"); var result=RecordingOutputFiles.SaveOriginal(source,folder,Date,token:TestContext.Current.CancellationToken);
        Assert.Equal(new byte[]{1,2,3,4},File.ReadAllBytes(result)); Assert.False(File.Exists(source)); Assert.Single(Directory.EnumerateFiles(folder));
    }
    [Fact] public void CollisionNeverOverwritesOriginalOrLeavesTemporaryFile()
    {
        var folder=Path.Combine(root,"save"); var proposed=RecordingOutputFiles.Propose(folder,Date); File.WriteAllBytes(proposed,[9]);
        var result=RecordingOutputFiles.SaveOriginal(Source(),folder,Date,proposed,TestContext.Current.CancellationToken);
        Assert.EndsWith(" (2).mp4",result); Assert.Equal(new byte[]{9},File.ReadAllBytes(proposed)); Assert.Equal(2,Directory.GetFiles(folder).Length);
    }
    [Fact] public void FailurePreservesSourceAndRemovesPartialSibling()
    {
        var source=Source(); var folder=Path.Combine(root,"save"); Directory.CreateDirectory(folder);
        Assert.ThrowsAny<Exception>(()=>RecordingOutputFiles.SaveOriginal(source,folder,Date,Path.Combine(root,"missing","file.mp4"),TestContext.Current.CancellationToken));
        Assert.True(File.Exists(source)); Assert.Empty(Directory.GetFiles(folder));
    }
    [Fact] public void CancelledPublishPreservesSourceWithoutPartialOutput()
    {
        var source=Source(); var folder=Path.Combine(root,"save"); using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>RecordingOutputFiles.SaveOriginal(source,folder,Date,token:cancellation.Token));
        Assert.True(File.Exists(source)); Assert.Empty(Directory.GetFiles(folder));
    }
}
