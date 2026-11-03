using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class RecordingFilePathTests
{
    [Fact] public void ReservesNameAndPreservesExistingEntries()
    {
        var folder=Path.Join(Path.GetTempPath(),$"SnapperRecording-{Guid.NewGuid():N}");
        try
        {
            var date=new DateTimeOffset(2026,11,3,12,13,14,TimeSpan.Zero);
            var first=RecordingFilePath.Reserve(folder,date); File.WriteAllText(first,"existing");
            var second=RecordingFilePath.Reserve(folder,date); Assert.EndsWith(" (2).mp4",second); Assert.Equal(0,new FileInfo(second).Length);
            Assert.Equal("existing",File.ReadAllText(first));
            Directory.CreateDirectory(second.Replace(" (2)"," (3)"));
            Assert.EndsWith(" (4).mp4",RecordingFilePath.Reserve(folder,date));
        }
        finally { if(Directory.Exists(folder)) Directory.Delete(folder,true); }
    }
    [Fact] public void FolderFailureDoesNotReserveFile()
    {
        var path=Path.Join(Path.GetTempPath(),$"SnapperRecording-{Guid.NewGuid():N}"); File.WriteAllText(path,"not folder");
        try { Assert.Throws<Support.SaveFolderException>(()=>RecordingFilePath.Reserve(path,DateTimeOffset.UtcNow)); Assert.Equal("not folder",File.ReadAllText(path)); }
        finally { File.Delete(path); }
    }
}
