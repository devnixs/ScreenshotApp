using System.Text;
using Snapper.Core.Support.Imaging;
namespace Snapper.Core.Tests;
public sealed class Crc32Tests
{
    [Fact] public void StandardCheckValue() => Assert.Equal(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("123456789")));
    [Fact] public void EmptyCheckValue() => Assert.Equal(0u, Crc32.Compute([]));
}
