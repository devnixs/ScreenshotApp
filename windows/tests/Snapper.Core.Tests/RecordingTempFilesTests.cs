using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class RecordingTempFilesTests
{
    private static readonly Guid Id=Guid.Parse("dcdb6006-5594-43d9-8ce3-0aa6966e67f8");
    [Fact] public void PathsArePortableAndUnique()
    {
        var root=Path.Combine(Path.GetTempPath(),"test-root"); var paths=RecordingTempFiles.For(root,Id);
        Assert.Equal(Path.Combine(root,"Snapper"),RecordingTempFiles.Directory(root));
        Assert.Equal(Path.Combine(root,"Snapper",$"Snapper-{Id:N}.mp4"),paths.Capture);
        Assert.Equal(Path.Combine(root,"Snapper",$"Snapper-{Id:N}-out.mp4"),paths.Output);
        Assert.Equal(paths,RecordingTempFiles.For(root+Path.DirectorySeparatorChar,Id));
        Assert.NotEqual(paths,RecordingTempFiles.For(root,Guid.NewGuid()));
    }
    [Fact] public void RecoverySavesCaptureByCreationTimeDeletesEmptyAndOutputsIgnoresOthers()
    {
        var date=new DateTimeOffset(2026,11,3,12,3,4,TimeSpan.Zero); var paths=RecordingTempFiles.For("/temp",Id);
        var other=RecordingTempFiles.For("/temp",Guid.NewGuid());
        RecordingTempEntry[] entries=[new(paths.Capture,10,date),new(paths.Output,20,date),new(other.Capture,0,date),
            new("/temp/unrelated.mp4",10,date),new("/temp/Snapper-bad.mp4",10,date),new("/temp/Snapper-.mp4",10,date)];
        Assert.Equal<RecordingRecoveryAction>([new RecordingRecoveryAction.SaveOriginal(paths.Capture,date),
            new RecordingRecoveryAction.Delete(paths.Output),new RecordingRecoveryAction.Delete(other.Capture)],RecordingTempFiles.RecoveryPlan(entries,date.AddHours(1)));
    }
    [Fact] public void MissingCreationTimeUsesInjectedNow()
    {
        var paths=RecordingTempFiles.For("/temp",Id); var now=DateTimeOffset.UtcNow;
        Assert.Equal([new RecordingRecoveryAction.SaveOriginal(paths.Capture,now)],RecordingTempFiles.RecoveryPlan([new(paths.Capture,1,DateTimeOffset.MinValue)],now));
    }
    [Theory,InlineData(true,ForcedRecordingEndAction.CancelAndSaveOriginal),InlineData(false,ForcedRecordingEndAction.ExistingStopBehavior)]
    public void ForcedEndDecision(bool converting,ForcedRecordingEndAction action) => Assert.Equal(action,ForcedRecordingEndDecision.Decide(converting));
}
