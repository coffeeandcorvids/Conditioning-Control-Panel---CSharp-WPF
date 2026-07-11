using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using LibVLC = LibVLCSharp.Shared;

namespace ConditioningControlPanel.Shell.Effects;

/// <summary>
/// Fullscreen mandatory-video window. LibVLC renders directly into this
/// window's native X11 surface (MediaPlayer.XWindow = the window's XID) —
/// the same handle-embedding approach upstream's WPF VideoView used, and
/// the robust path on Linux since no LibVLCSharp.Avalonia integration
/// package is installed. Uses its own video-enabled LibVLC instance,
/// separate from VlcAudioPlayer's "--no-video" audio instance.
///
/// ── Z-ORDER POLICY (designed in from day one — the upstream v6.2.11 bug
///    was spiral/pink overlays burying a mandatory video) ──
/// Every fullscreen surface in this app is a Topmost borderless window, so
/// stacking among them is governed by ONE rule, tied to the focus
/// discipline established in OverlayEffectWindow:
///   • Tier 1 — passive effect overlays (spiral, pink filter): Topmost,
///     but NEVER Activate() (they're click-through filters). They sit at
///     the "topmost-but-unfocused" layer.
///   • Tier 2 — mandatory video (this window): Topmost AND Activate() on
///     show. A focused topmost window stacks ABOVE unfocused topmost
///     windows under X11/labwc, so the video reliably sits over any
///     effect overlay — even one toggled on mid-playback (re-asserting
///     Topmost on an overlay doesn't lift it past the focused video).
/// This makes z-order fall out of the focus rule instead of needing
/// fragile XRaiseWindow races. If the app ever gains a surface that must
/// sit above the mandatory video, give it Tier 3 and document it here.
/// </summary>
internal sealed class VlcVideoWindow : Window
{
    private readonly LibVLC.LibVLC _vlc;
    private readonly LibVLC.MediaPlayer _mp;
    private bool _handleBound;
    private int _volume = 78;   // 0–100; re-applied on each Play (LibVLC resets per-media)

    /// <summary>Raised (on the UI thread) when playback ends on its own or is stopped.</summary>
    public event EventHandler? Finished;

    public VlcVideoWindow()
    {
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        // Black, opaque backdrop: until LibVLC's first frame lands the user
        // sees black rather than desktop garbage or a transparent hole.
        Background = Brushes.Black;

        LibVLC.Core.Initialize();
        _vlc = new LibVLC.LibVLC();               // video enabled (no --no-video)
        _mp = new LibVLC.MediaPlayer(_vlc);

        _mp.EndReached += (_, _) => Dispatcher.UIThread.Post(() => Finished?.Invoke(this, EventArgs.Empty));
        _mp.EncounteredError += (_, _) => Dispatcher.UIThread.Post(() => Finished?.Invoke(this, EventArgs.Empty));

        // Mandatory video is an attention grab, so it IS allowed to take
        // focus (unlike passive overlays) — that focus is exactly what puts
        // it above them. ESC still dismisses it so it can never trap the
        // user, matching the panic-key contract elsewhere.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; StopAndClose(); }
        };
    }

    /// <summary>Place on the given screen and start playback of a local path or URL.</summary>
    public void Play(PixelRect screen, string pathOrUrl)
    {
        Position = new PixelPoint(screen.X, screen.Y);
        Width = screen.Width;
        Height = screen.Height;

        if (!IsVisible) Show();
        // Tier-2 stacking: focusing raises us above the passive overlays.
        Activate();
        Topmost = true;

        BindNativeHandle();

        try
        {
            var kind = Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && !uri.IsFile
                ? LibVLC.FromType.FromLocation
                : LibVLC.FromType.FromPath;
            using var media = new LibVLC.Media(_vlc, pathOrUrl, kind);
            _mp.Play(media);
            try { _mp.Volume = _volume; } catch { }   // re-assert; LibVLC resets volume per media
        }
        catch
        {
            Dispatcher.UIThread.Post(() => Finished?.Invoke(this, EventArgs.Empty));
        }
    }

    public bool IsPlaying => _mp.IsPlaying;

    /// <summary>Playback volume, 0–100. Persists across Play() calls.</summary>
    public int Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 100); try { _mp.Volume = _volume; } catch { } }
    }

    public void StopAndClose()
    {
        try { _mp.Stop(); } catch { }
        if (IsVisible) Hide();
        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void BindNativeHandle()
    {
        if (_handleBound) return;
        var handle = this.TryGetPlatformHandle();
        if (handle is null || handle.HandleDescriptor != "XID") return;
        // LibVLC draws its video output into this X11 window directly.
        _mp.XWindow = (uint)handle.Handle.ToInt64();
        _handleBound = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        try { _mp.Stop(); } catch { }
        _mp.Dispose();
        _vlc.Dispose();
        base.OnClosed(e);
    }
}
