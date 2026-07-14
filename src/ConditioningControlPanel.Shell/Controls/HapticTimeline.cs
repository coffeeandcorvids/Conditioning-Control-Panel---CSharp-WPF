using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Core.Models.Authoring;

namespace ConditioningControlPanel.Shell.Controls;

/// <summary>
/// The Haptics DAW timeline surface — a lane-based view over a <see cref="HapticProject"/>.
/// Draws a time ruler, the envelope automation lane (effective base amount over time),
/// and a cue-window lane (draggable-in-a-later-pass rectangles per authored cue), plus a
/// scrub playhead. Click a cue to select it; click the ruler/envelope to move the playhead.
/// Code-behind app (no MVVM) — state is set via plain properties that invalidate the visual.
/// </summary>
public sealed class HapticTimeline : Control
{
    private HapticProject? _project;
    private long _playheadMs;
    private AuthoredCue? _selected;
    private float[]? _waveform;

    public HapticProject? Project { get => _project; set { _project = value; InvalidateVisual(); } }
    public long PlayheadMs { get => _playheadMs; set { _playheadMs = value; InvalidateVisual(); } }
    public AuthoredCue? Selected { get => _selected; set { _selected = value; InvalidateVisual(); } }

    /// <summary>Normalized audio peaks (0..1) across the clip; null hides the waveform lane.</summary>
    public float[]? Waveform { get => _waveform; set { _waveform = value; InvalidateVisual(); } }

    /// <summary>Raised when a cue is clicked (arg is the cue, or null when the click hit empty cue-lane space).</summary>
    public event EventHandler<AuthoredCue?>? CueSelected;
    /// <summary>Raised when the ruler/envelope lane is clicked to scrub (arg is the position in ms).</summary>
    public event EventHandler<long>? Scrubbed;

    private const double RulerH = 22;
    private const double EnvH = 96;
    private const double Pad = 8;
    private const double CueLabelH = 14;

    private double MsToX(long ms, double w)
        => _project is { DurationMs: > 0 } p ? Pad + (double)ms / p.DurationMs * (w - 2 * Pad) : Pad;

    private long XToMs(double x, double w)
        => _project is { DurationMs: > 0 } p
            ? (long)Math.Clamp((x - Pad) / (w - 2 * Pad) * p.DurationMs, 0, p.DurationMs)
            : 0;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        ctx.DrawRectangle(Brush("#0D0D18"), null, new Rect(0, 0, w, h));

        if (_project is null || _project.DurationMs <= 0)
        {
            DrawText(ctx, "load an audio clip or open a project to begin",
                     new Point(Pad + 4, RulerH + 12), "#7A7A9C", 13);
            return;
        }

        double envTop = RulerH, envBot = RulerH + EnvH;
        double cueTop = envBot + 6, cueBot = h - Pad;

        // ── time ruler + vertical gridlines ──
        var gridPen = new Pen(Brush("#22223A"), 1);
        long stepMs = ChooseStep(_project.DurationMs);
        for (long t = 0; t <= _project.DurationMs; t += stepMs)
        {
            double x = MsToX(t, w);
            ctx.DrawLine(gridPen, new Point(x, RulerH), new Point(x, cueBot));
            DrawText(ctx, FormatMs(t), new Point(x + 2, 3), "#6A6A8C", 10);
        }

        // ── envelope automation lane (effective base = base × envelope) ──
        ctx.DrawRectangle(Brush("#12121F"), null, new Rect(0, envTop, w, EnvH));

        // audio waveform behind the automation, mirrored around the lane centre
        if (_waveform is { Length: > 0 } wf)
        {
            double midY = envTop + EnvH / 2.0;
            double wamp = EnvH / 2.0 - 3;
            var wfPen = new Pen(new SolidColorBrush(Color.Parse("#3A4A7A"), 0.6), 1);
            for (double px = Pad; px <= w - Pad; px += 1)
            {
                double frac = (px - Pad) / (w - 2 * Pad);
                int idx = (int)Math.Clamp(frac * wf.Length, 0, wf.Length - 1);
                double hh = wf[idx] * wamp;
                ctx.DrawLine(wfPen, new Point(px, midY - hh), new Point(px, midY + hh));
            }
        }

        DrawText(ctx, "waveform · envelope × base", new Point(Pad + 2, envTop + 2), "#4A4A6C", 10);
        double AmtToY(double amt) => envBot - Math.Clamp(amt / 2.0, 0, 1) * (EnvH - 6) - 3;
        var envPen = new Pen(Brush("#5CC8FF"), 2);
        Point? prev = null;
        for (double px = Pad; px <= w - Pad; px += 3)
        {
            double amt = _project.EffectiveBaseAt(XToMs(px, w));
            var p = new Point(px, AmtToY(amt));
            if (prev is Point pp) ctx.DrawLine(envPen, pp, p);
            prev = p;
        }
        // 1.0 reference line
        ctx.DrawLine(new Pen(Brush("#2E2E50"), 1) { DashStyle = DashStyle.Dash },
                     new Point(Pad, AmtToY(1.0)), new Point(w - Pad, AmtToY(1.0)));

        // ── cue-window lane ──
        // The colored block spans exactly [start, stop] — its width IS the duration. Labels
        // are drawn in the strip above and CLIPPED to end before the next cue, so a long
        // label on a short cue can never spill across and fake a wider block.
        DrawText(ctx, "cues", new Point(Pad + 2, cueTop + 2), "#4A4A6C", 10);
        var cues = _project.Cues;
        double blockTop = cueTop + CueLabelH;
        double blockH = Math.Max(6, cueBot - blockTop - 2);
        for (int i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            double x0 = MsToX(cue.StartMs, w), x1 = MsToX(cue.StopMs, w);
            double bw = Math.Max(2, x1 - x0);
            var rect = new Rect(x0, blockTop, bw, blockH);
            bool sel = ReferenceEquals(cue, _selected);
            var fill = new SolidColorBrush(Color.Parse(sel ? "#FF5CA8" : "#8B5CF6"), sel ? 0.85 : 0.5);
            var pen = sel ? new Pen(Brush("#FFB3E6"), 2) : new Pen(Brush("#A98BE0"), 1);
            ctx.DrawRectangle(fill, pen, new RoundedRect(rect, 3));

            // duration stamp inside the block when it's wide enough to hold it
            if (bw > 34)
                using (ctx.PushClip(rect))
                    DrawText(ctx, FormatDur(cue.StopMs - cue.StartMs), new Point(x0 + 3, blockTop + 2), "#FFFFFF", 10);

            // label above, clipped to its horizontal slot (up to the next cue's start)
            string lbl = cue.Trigger
                + (string.IsNullOrEmpty(cue.Personality) ? "" : " · " + cue.Personality)
                + (cue.Snap ? " ⚡" : "");
            double labelRight = (i + 1 < cues.Count ? MsToX(cues[i + 1].StartMs, w) : (w - Pad)) - 2;
            double labelW = labelRight - x0;
            if (labelW > 6)
                using (ctx.PushClip(new Rect(x0, cueTop, labelW, CueLabelH + 2)))
                    DrawText(ctx, lbl, new Point(x0 + 2, cueTop + 1), sel ? "#FFE6F5" : "#D8CCF5", 11);
        }

        // ── scrub playhead ──
        double phx = MsToX(_playheadMs, w);
        ctx.DrawLine(new Pen(Brush("#FFE05C"), 2), new Point(phx, RulerH), new Point(phx, cueBot));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_project is null || _project.DurationMs <= 0) return;
        var pos = e.GetPosition(this);
        long ms = XToMs(pos.X, Bounds.Width);
        double cueLaneTop = RulerH + EnvH + 6 + CueLabelH;
        if (pos.Y >= cueLaneTop)
        {
            AuthoredCue? hit = null;
            foreach (var cue in _project.Cues)
                if (ms >= cue.StartMs && ms <= cue.StopMs) { hit = cue; break; }
            Selected = hit;
            CueSelected?.Invoke(this, hit);
        }
        else
        {
            PlayheadMs = ms;
            Scrubbed?.Invoke(this, ms);
        }
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private void DrawText(DrawingContext ctx, string text, Point at, string colorHex, double size)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                   Typeface.Default, size, Brush(colorHex));
        ctx.DrawText(ft, at);
    }

    private static long ChooseStep(long dur)
    {
        long[] steps = { 1000, 2000, 5000, 10000, 15000, 30000, 60000, 120000, 300000 };
        foreach (var s in steps) if (dur / s <= 12) return s;
        return 600000;
    }

    private static string FormatMs(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    private static string FormatDur(long ms) => ms >= 1000 ? $"{ms / 1000.0:0.#}s" : $"{ms}ms";
}
