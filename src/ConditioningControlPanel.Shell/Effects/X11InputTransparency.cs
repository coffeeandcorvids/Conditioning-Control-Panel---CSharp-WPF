using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace ConditioningControlPanel.Shell.Effects;

/// <summary>
/// True OS-level click-through for a topmost overlay window, via the X11
/// SHAPE extension's input-shape (XShapeCombineRectangles with an empty
/// rectangle list and ShapeInput). Avalonia has no managed API for this —
/// checked by reflection against 12.0.5's public surface, confirmed absent
/// on both Window/TopLevel and the X11-specific toplevel feature interface
/// — so this P/Invokes libX11/libXext directly, the same technique real
/// click-through overlay tools (screen annotators, conky, etc.) use.
///
/// This is X11/XWayland-specific: the Pi runs Avalonia.X11 (no
/// Avalonia.Wayland package is referenced), so windows are real X11
/// windows under XWayland and this extension is available. If the app
/// ever moves to a native Wayland backend, this needs a different
/// mechanism (wl_surface.set_input_region via wlr-layer-shell) — flagged
/// here so a future maintainer doesn't assume this silently still works.
/// </summary>
internal static class X11InputTransparency
{
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr display);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(
        IntPtr display, IntPtr window, int destKind, int xOff, int yOff,
        IntPtr rectangles, int nRects, int op, int ordering);
    [DllImport("libXext.so.6")] private static extern int XShapeQueryExtension(
        IntPtr display, out int eventBase, out int errorBase);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XInternAtom(
        IntPtr display, string atomName, bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern int XChangeProperty(
        IntPtr display, IntPtr window, IntPtr property, IntPtr type,
        int format, int mode, ref IntPtr data, int nElements);

    private const int PropModeReplace = 0;
    private const int XA_ATOM_FORMAT = 32;

    private const int ShapeInput = 2;   // ShapeKind: bounding=0, clip=1, input=2
    private const int ShapeSet = 0;     // ShapeOp: set (replace)
    private const int Unsorted = 0;

    private static IntPtr _display;
    private static bool _checkedExtension;
    private static bool _extensionAvailable;

    /// <summary>
    /// Make the window fully click-through: an empty input shape means the
    /// X server routes all pointer events to whatever's underneath, while
    /// the window still renders and receives no input itself. Call once
    /// the window is realized (after Show()) so its native X11 handle
    /// exists. No-op (logged, not thrown) if this isn't an X11 window or
    /// the SHAPE extension isn't present — overlay still works, just
    /// without click-through, rather than crashing the app.
    /// </summary>
    public static bool TryMakeClickThrough(Window window)
    {
        try
        {
            var handle = (window as TopLevel)?.TryGetPlatformHandle();
            if (handle is null || handle.HandleDescriptor != "XID") return false;

            EnsureDisplay();
            if (_display == IntPtr.Zero || !_extensionAvailable) return false;

            // empty rectangle list -> the window accepts input nowhere at all
            XShapeCombineRectangles(_display, handle.Handle, ShapeInput, 0, 0,
                IntPtr.Zero, 0, ShapeSet, Unsorted);
            XFlush(_display);
            return true;
        }
        catch
        {
            // Best-effort: a missing/failed click-through is a degraded
            // experience, not a crash. Never let this take the overlay down.
            return false;
        }
    }

    /// <summary>
    /// Set the EWMH _NET_WM_WINDOW_TYPE property to UTILITY directly via
    /// Xlib — the standard hint window managers (including labwc) use to
    /// exclude a window from the taskbar/pager/alt-tab list. Avalonia has
    /// an internal wrapper for this (IX11OptionsToplevelImplFeature) but
    /// gates it as a platform-only SPI the compiler refuses to let
    /// application code call directly (CS0122, confirmed — not a missing
    /// using, an intentional access restriction), so this sets the same
    /// EWMH atom ourselves. Best-effort/no-op if this isn't an X11 window.
    /// </summary>
    public static void TryHideFromTaskbar(Window window)
    {
        try
        {
            var handle = (window as TopLevel)?.TryGetPlatformHandle();
            if (handle is null || handle.HandleDescriptor != "XID") return;

            EnsureDisplay();
            if (_display == IntPtr.Zero) return;

            var wmWindowType = XInternAtom(_display, "_NET_WM_WINDOW_TYPE", false);
            var utility = XInternAtom(_display, "_NET_WM_WINDOW_TYPE_UTILITY", false);
            XChangeProperty(_display, handle.Handle, wmWindowType,
                (IntPtr)4 /* XA_ATOM */, XA_ATOM_FORMAT, PropModeReplace, ref utility, 1);
            XFlush(_display);
        }
        catch
        {
            // best-effort only; window still functions, just may show a
            // taskbar entry on window managers that don't respect the hint
        }
    }

    private static void EnsureDisplay()
    {
        if (_checkedExtension) return;
        _checkedExtension = true;
        try
        {
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero) { _extensionAvailable = false; return; }
            _extensionAvailable = XShapeQueryExtension(_display, out _, out _) != 0;
        }
        catch
        {
            _extensionAvailable = false;
        }
    }
}
