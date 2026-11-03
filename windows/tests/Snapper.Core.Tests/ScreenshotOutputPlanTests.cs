using Snapper.Core.Capture;
using Snapper.Core.Settings;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class ScreenshotOutputPlanTests
{
    [Theory] [InlineData(ScreenshotDestination.File,true,false)] [InlineData(ScreenshotDestination.Clipboard,false,true)] [InlineData(ScreenshotDestination.Both,true,true)]
    public void Destinations(ScreenshotDestination destination,bool save,bool copy)
    {var p=new ScreenshotOutputPlan(destination);Assert.Equal(save,p.SavesToFile);Assert.Equal(copy,p.CopiesToClipboard);}
    [Theory] [InlineData(0x80070005)] [InlineData(0x80070013)] [InlineData(0x80070003)] [InlineData(0x80070015)] [InlineData(0x80070035)]
    public void FolderHResults(uint code)=>Assert.Equal(SaveFailureAlert.SaveFolder,ScreenshotOutputPlan.ClassifySaveFailure(new IOException("x",unchecked((int)code))));
    [Fact] public void FolderTypesAndInnerErrors()
    {
        Exception[] errors=[new SaveFolderException(SaveFolderFailure.NotADirectory,"/x"),new UnauthorizedAccessException(),new DirectoryNotFoundException(),new DriveNotFoundException(),new IOException("x",new UnauthorizedAccessException()),new Exception("x",new Exception("y",new IOException("z",unchecked((int)0x80070013))))];
        Assert.All(errors,e=>Assert.Equal(SaveFailureAlert.SaveFolder,ScreenshotOutputPlan.ClassifySaveFailure(e)));
    }
    [Fact] public void GenericErrorsAndDepthBound()
    {
        Exception[] errors=[new IOException("full",unchecked((int)0x80070070)),new IOException("full",unchecked((int)0x80070027)),new Exception("x"),new InvalidOperationException()];
        Assert.All(errors,e=>Assert.Equal(SaveFailureAlert.Generic,ScreenshotOutputPlan.ClassifySaveFailure(e)));
        Exception chain=new UnauthorizedAccessException();
        for(var i=0;i<16;i++)chain=new Exception("outer",chain);
        Assert.Equal(SaveFailureAlert.Generic,ScreenshotOutputPlan.ClassifySaveFailure(chain));
        Assert.Equal(SaveFailureAlert.SaveFolder,ScreenshotOutputPlan.ClassifySaveFailure(chain.InnerException!));
    }
    [Theory] [InlineData(1,96)] [InlineData(1.25,120)] [InlineData(1.5,144)] [InlineData(2,192)]
    public void Dpi(double scale,double dpi)=>Assert.Equal(dpi,ScreenshotOutputPlan.PngDpi(scale));
    [Fact] public void NearestExistingAncestorOrRoot()
    {
        var root=Path.GetPathRoot(Path.GetTempPath())!;var a=Path.Combine(root,"a");var child=Path.Combine(a,"b","c");
        Assert.Equal(child,ScreenshotOutputPlan.NearestExistingFolder(child,x=>x==child));
        Assert.Equal(a,ScreenshotOutputPlan.NearestExistingFolder(child,x=>x==a));
        Assert.Equal(root,ScreenshotOutputPlan.NearestExistingFolder(child,_=>false));
        Assert.Equal("relative",ScreenshotOutputPlan.NearestExistingFolder("relative",_=>false));
    }
}
