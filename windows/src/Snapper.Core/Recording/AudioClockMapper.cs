namespace Snapper.Core.Recording;
public enum AudioClockDomain { SystemTime, Relative }
public sealed class AudioClockMapper(long toleranceTicks = 10_000_000)
{
    private Int128 offset;
    public AudioClockDomain? Domain { get; private set; }
    public long ToQpc(long readerTimestamp, long durationTicks, long qpcNowAtReceipt)
    {
        if (Domain is null)
        {
            var delta = (Int128)readerTimestamp - qpcNowAtReceipt;
            Domain = Int128.Abs(delta) <= toleranceTicks ? AudioClockDomain.SystemTime : AudioClockDomain.Relative;
            offset = Domain == AudioClockDomain.SystemTime ? 0 : (Int128)qpcNowAtReceipt - readerTimestamp - durationTicks;
        }
        return checked((long)((Int128)readerTimestamp + offset));
    }
}
