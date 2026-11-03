using Snapper.Core.Capture;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class ScreenshotFileWriterTests
{
    [Fact] public void CreatesNestedFoldersAndPreservesCollidingBytes()
    {
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        try
        {
            var folder=Path.Combine(root,"nested","shots");var date=new DateTimeOffset(2026,11,3,13,14,15,TimeSpan.Zero);
            var first=ScreenshotFileWriter.Save([1,2],folder,date);var second=ScreenshotFileWriter.Save([3,4],folder,date);
            Assert.Equal(new FileNamer().FileName(CaptureKind.Screenshot,date),Path.GetFileName(first));
            Assert.Equal(Path.GetFileNameWithoutExtension(first)+" (2).png",Path.GetFileName(second));
            Assert.Equal(new byte[]{1,2},File.ReadAllBytes(first));Assert.Equal(new byte[]{3,4},File.ReadAllBytes(second));
            Assert.Equal(2,Directory.GetFileSystemEntries(folder).Length);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void FileInFolderPathThrowsClassifiedError(bool child)
    {
        var file=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());File.WriteAllBytes(file,[7]);
        try
        {
            var error=Assert.Throws<SaveFolderException>(()=>ScreenshotFileWriter.Save([1],child?Path.Combine(file,"shots"):file,DateTime.Now));
            Assert.Equal(SaveFailureAlert.SaveFolder,ScreenshotOutputPlan.ClassifySaveFailure(error));Assert.NotEmpty(error.Message);
            Assert.Equal(new byte[]{7},File.ReadAllBytes(file));
        }
        finally{File.Delete(file);}
    }
    [Fact] public void DirectoryAtNameIsNeverOverwritten()
    {
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(root);
        try
        {
            var date=DateTimeOffset.Now;var taken=Path.Combine(root,new FileNamer().FileName(CaptureKind.Screenshot,date));Directory.CreateDirectory(taken);
            var saved=ScreenshotFileWriter.Save([9],root,date);
            Assert.NotEqual(taken,saved);Assert.True(Directory.Exists(taken));Assert.Equal(new byte[]{9},File.ReadAllBytes(saved));
        }
        finally{Directory.Delete(root,true);}
    }
}
