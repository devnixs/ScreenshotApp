using Snapper.Core.Support;

namespace Snapper.Core.App;

public enum HotkeyStatus { Unbound, Registered, InUseByAnotherApp, DuplicateBinding }
public sealed record HotkeySyncPlan(IReadOnlyList<CapturePurpose> ToUnregister,
    IReadOnlyList<(CapturePurpose Purpose, KeyChord Chord)> ToRegister);

/// <summary>Pure registration bookkeeping. Plan has no side effects; commit follows native operations.</summary>
public sealed class HotkeyRegistrationState
{
    private readonly Dictionary<CapturePurpose, HotkeyStatus> statuses = new();
    private readonly Dictionary<CapturePurpose, KeyChord?> chords = new();
    private bool synced;
    public static int IdOf(CapturePurpose p) => p switch
    {
        CapturePurpose.Screenshot => 1, CapturePurpose.Record => 2, CapturePurpose.Annotate => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(p))
    };
    public static CapturePurpose? PurposeForId(int id) => id switch
    { 1 => CapturePurpose.Screenshot, 2 => CapturePurpose.Record, 3 => CapturePurpose.Annotate, _ => null };
    public HotkeyStatus StatusOf(CapturePurpose p) => statuses.GetValueOrDefault(p, HotkeyStatus.Unbound);
    public KeyChord? ChordOf(CapturePurpose p) => chords.GetValueOrDefault(p);

    public HotkeySyncPlan Plan(IReadOnlyDictionary<CapturePurpose, KeyChord?> desired)
    {
        var unregister = new List<CapturePurpose>();
        var register = new List<(CapturePurpose, KeyChord)>();
        var seen = new HashSet<KeyChord>();
        foreach (var p in CapturePurposes.All)
        {
            var chord = desired[p];
            var duplicate = chord is { } c && !seen.Add(c);
            var changed = chord != ChordOf(p);
            if (StatusOf(p) == HotkeyStatus.Registered && (changed || chord is null || duplicate)) unregister.Add(p);
            if (chord is { } next && !duplicate && (changed || StatusOf(p) != HotkeyStatus.Registered)) register.Add((p, next));
        }
        return new HotkeySyncPlan(unregister.AsReadOnly(), register.AsReadOnly());
    }
    public IReadOnlyList<KeyChord> Commit(HotkeySyncPlan plan,
        IReadOnlyDictionary<CapturePurpose, bool> registered,
        IReadOnlyDictionary<CapturePurpose, KeyChord?> desired)
    {
        var failures = new List<KeyChord>();
        var seen = new HashSet<KeyChord>();
        var attempted = plan.ToRegister.Select(x => x.Purpose).ToHashSet();
        foreach (var p in CapturePurposes.All)
        {
            var next = desired[p];
            var previous = ChordOf(p);
            if (next is null) statuses[p] = HotkeyStatus.Unbound;
            else if (!seen.Add(next.Value)) statuses[p] = HotkeyStatus.DuplicateBinding;
            else if (attempted.Contains(p))
            {
                var succeeded = registered[p];
                statuses[p] = succeeded ? HotkeyStatus.Registered : HotkeyStatus.InUseByAnotherApp;
                if (!succeeded && (!synced || previous != next)) failures.Add(next.Value);
            }
            chords[p] = next;
        }
        synced = true;
        return failures.AsReadOnly();
    }
}
