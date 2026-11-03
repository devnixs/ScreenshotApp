using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Snapper.Core.App;
using Snapper.Core.Settings;
using Snapper.Core.Support;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Snapper.Windows.App;

/// <summary>Owns thread-affine registrations and their message-only window on the WPF dispatcher.</summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly HotkeyRegistrationState state = new();
    private readonly HwndSource source;
    private bool disposed;

    public HotkeyManager()
    {
        source = new HwndSource(new HwndSourceParameters("SnapperHotkeys")
        {
            ParentWindow = new nint(-3), // WinUser.h HWND_MESSAGE pseudo-handle.
            WindowStyle = 0, Width = 0, Height = 0
        });
        source.AddHook(WndProc);
    }

    public event Action<CapturePurpose>? Pressed;
    public event Action<IReadOnlyList<KeyChord>>? RegistrationFailed;
    public event Action? StatusesChanged;
    public HotkeyStatus StatusOf(CapturePurpose p) { dispatcher.VerifyAccess(); return state.StatusOf(p); }

    public void Sync(AppSettings settings)
    {
        dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        var desired = HotkeyBindings.From(settings);
        var plan = state.Plan(desired);
        var hwnd = new HWND(source.Handle);
        foreach (var p in plan.ToUnregister) PInvoke.UnregisterHotKey(hwnd, HotkeyRegistrationState.IdOf(p));
        var registered = new Dictionary<CapturePurpose, bool>();
        foreach (var (p, chord) in plan.ToRegister)
        {
            var (modifiers, key) = HotkeyNativeChord.ToRegisterHotKeyArgs(chord);
            var success = PInvoke.RegisterHotKey(hwnd, HotkeyRegistrationState.IdOf(p), (HOT_KEY_MODIFIERS)modifiers, key);
            registered[p] = success;
            if (!success) Debug.WriteLine($"Snapper: RegisterHotKey {p} {chord.DisplayString} failed: {Marshal.GetLastWin32Error()}");
        }
        var failed = state.Commit(plan, registered, desired);
        foreach (var p in CapturePurposes.All)
            Debug.WriteLine($"Snapper: Hotkey {p} {state.ChordOf(p)?.DisplayString ?? "(unbound)"}: {state.StatusOf(p)}");
        StatusesChanged?.Invoke();
        if (failed.Count > 0) RegistrationFailed?.Invoke(failed);
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (!disposed && message == PInvoke.WM_HOTKEY && HotkeyRegistrationState.PurposeForId((int)wParam) is { } purpose)
        {
            handled = true;
            Pressed?.Invoke(purpose);
        }
        return 0;
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true;
        foreach (var p in CapturePurposes.All)
            if (state.StatusOf(p) == HotkeyStatus.Registered)
                PInvoke.UnregisterHotKey(new HWND(source.Handle), HotkeyRegistrationState.IdOf(p));
        source.RemoveHook(WndProc);
        source.Dispose();
    }
}
