using System;
using Eto.Drawing;
using Eto.Forms;

namespace RhMcp;

// A rounded chat bubble. Eto has no rounded-corner Panel, so the control paints its own rounded
// background and lets a transparent Label render on top of it. The body is a non-editable Label
// (selection is replaced by the copy button) hosted in a Scrollable so an oversized message caps
// its height and scrolls instead of pushing the whole transcript tall.
//
// Width/height are pinned explicitly via Apply(): a wrapping Label reports its full single-line
// width if left unconstrained, which forces the surrounding Scrollable to grow sideways. Apply
// re-runs on viewport resize.
internal sealed class MessageBubble : Drawable
{
    private const float Radius = 9f;
    private const int Pad = 10;        // horizontal inset; kept >= Radius so a capped bubble's square
                                       // scroll viewport stays inside the rounded silhouette
    private const int PadV = 6;        // tighter vertical inset: text sits past the corner arc
    private const int MinInner = 40;   // floor so a tiny message still fits the copy button
    private const int CopyButtonSize = 22;   // square footprint snug around the 14px copy glyph

    private Color Fill { get; }
    private Font BodyFont { get; }
    private int MaxHeight { get; }

    private Label Body { get; }
    private Scrollable BodyScroll { get; }

    // Mutable so a streaming assistant delta can grow this bubble in place (see Update) instead of
    // tearing it down and rebuilding; NaturalWidth/MeasuredHeight read it on the next Apply.
    public string Text { get; private set; }

    // Last width budget Apply ran with, so an in-place text Update can re-pin against the same
    // viewport without the panel having to feed it back in.
    private int LastBudget { get; set; } = -1;

    public MessageBubble(string text, bool user, Font font, int maxHeight, Image? copyIcon)
    {
        Text = text;
        BodyFont = font;
        MaxHeight = maxHeight;
        Fill = user ? Color.FromArgb(0x33, 0x66, 0xCC) : SystemColors.ControlBackground;

        Body = new Label
        {
            Text = text,
            Wrap = WrapMode.Word,
            Font = font,
            TextColor = user ? Colors.White : SystemColors.ControlText,
            TextAlignment = user ? TextAlignment.Right : TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        BodyScroll = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = false,
            BackgroundColor = Fill,
            Content = Body,
        };

        Button copy = new()
        {
            Image = copyIcon,
            Text = copyIcon is null ? "Copy" : string.Empty,
            ToolTip = "Copy message",
        };
        if (copyIcon is not null)
        {
            // Without an explicit square size Eto pads the icon-only button taller than wide; pin it
            // snug around the 14px glyph so it renders square.
            copy.Size = new Size(CopyButtonSize, CopyButtonSize);
            copy.MinimumSize = new Size(CopyButtonSize, CopyButtonSize);
        }
        copy.Click += (_, _) =>
        {
            Clipboard.Instance.Text = Text;
            copy.ToolTip = "Copied!";
        };

        StackLayout footer = new()
        {
            Orientation = Orientation.Horizontal,
            Items = { new StackLayoutItem(null, true), new StackLayoutItem(copy, false) },
        };

        Padding = new Padding(Pad, PadV);
        Content = new StackLayout
        {
            Spacing = 2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { new StackLayoutItem(BodyScroll, false), new StackLayoutItem(footer, false) },
        };
    }

    // Re-wrap allowance once a capped bubble shows a vertical scrollbar. macOS overlay scrollbars
    // take ~0px and Windows ~17px, so a fixed reserve is platform-fragile; this errs toward the
    // wider (Windows) case. Over-reserving on macOS only leaves a thin gutter; under-reserving would
    // clip the right edge, the worse failure.
    private const int ScrollbarWidth = 17;

    // Default height budget used by an in-place Update that lands before the bubble was ever pinned
    // to a viewport (first turn, before first layout). Lets the body size to its natural wrap rather
    // than render at zero height; the next ApplyBubbleWidths re-pins it to the real viewport.
    private const int UnpinnedBudget = 320;

    // Grow this bubble in place for a streaming assistant delta: swap the body text and re-measure.
    // Re-pin against the last width budget so the row resizes without a teardown/rebuild; if a delta
    // arrives before the bubble was ever pinned (no layout yet), fall back to a default budget so it
    // still sizes rather than rendering at zero height.
    public void Update(string text)
    {
        if (text == Text)
            return;
        Text = text;
        Body.Text = text;
        Apply(LastBudget >= 0 ? LastBudget : UnpinnedBudget);
    }

    // Hug short messages, wrap long ones, and cap the height with an inner scroll. maxContentWidth
    // is the row's width budget (viewport minus margins + scrollbar).
    public void Apply(int maxContentWidth)
    {
        LastBudget = maxContentWidth;
        int maxLabel = Math.Max(MinInner, maxContentWidth - 2 * Pad);
        int inner = Math.Clamp(NaturalWidth(), MinInner, maxLabel);
        int contentHeight = MeasuredHeight(inner);

        if (contentHeight <= MaxHeight)
        {
            Body.Size = new Size(inner, contentHeight);
            BodyScroll.Size = new Size(inner, contentHeight);
        }
        else
        {
            // A scrollbar will appear and steal width, so the body re-wraps narrower (taller); size
            // the body for that width or its last lines scroll out of reach.
            int wrapped = Math.Max(MinInner, inner - ScrollbarWidth);
            Body.Size = new Size(wrapped, MeasuredHeight(wrapped));
            BodyScroll.Size = new Size(inner, MaxHeight);
        }
        Width = inner + 2 * Pad;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.AntiAlias = true;
        e.Graphics.FillPath(Fill, RoundedRect(new RectangleF(PointF.Empty, ClientSize), Radius));
        base.OnPaint(e);
    }

    private static IGraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float clamped = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
        float d = clamped * 2f;
        GraphicsPath path = new();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Longest hard line at the body font — the width the bubble would take unwrapped.
    private int NaturalWidth()
    {
        float max = 0;
        foreach (string line in Text.Replace("\r", string.Empty).Split('\n'))
            max = Math.Max(max, BodyFont.MeasureString(line).Width);
        return (int)Math.Ceiling(max) + 2;
    }

    // The Label has no auto-height, so derive one from the shared wrap measure (which already keeps
    // one line of safety margin against an off-by-one wrap clipping the last line).
    private int MeasuredHeight(int width) => TextMeasure.WrappedHeight(BodyFont, Text, width);
}
