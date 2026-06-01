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
    private const int Pad = 10;        // inner inset; kept >= Radius so a capped bubble's square
    private const float Radius = 9f;   // scroll viewport stays inside the rounded silhouette
    private const int MinInner = 40;   // floor so a tiny message still fits the copy button

    private Color Fill { get; }
    private Font BodyFont { get; }
    private int MaxHeight { get; }

    private Label Body { get; }
    private Scrollable BodyScroll { get; }

    public string Text { get; }

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

        Padding = new Padding(Pad);
        Content = new StackLayout
        {
            Spacing = 2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { new StackLayoutItem(BodyScroll, false), new StackLayoutItem(footer, false) },
        };
    }

    private const int ScrollbarWidth = 16;   // re-wrap allowance once a capped bubble shows a scrollbar

    // Hug short messages, wrap long ones, and cap the height with an inner scroll. maxContentWidth
    // is the row's width budget (viewport minus margins + scrollbar).
    public void Apply(int maxContentWidth)
    {
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

    // The Label has no auto-height, so derive one: count how many rows each hard line wraps into at
    // the given width and multiply by line height. Biased high (a slack line + inset) since extra
    // padding is harmless while under-shooting clips the last line.
    private int MeasuredHeight(int width)
    {
        float wrapWidth = Math.Max(20, width - 6);
        int lines = 0;
        foreach (string hardLine in Text.Replace("\r", string.Empty).Split('\n'))
            lines += WrappedLineCount(hardLine, wrapWidth);
        return (int)Math.Ceiling((lines + 1) * BodyFont.LineHeight) + 6;
    }

    private int WrappedLineCount(string line, float width)
    {
        if (line.Length == 0)
            return 1;

        int lines = 1;
        string current = string.Empty;
        foreach (string word in line.Split(' '))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (BodyFont.MeasureString(candidate).Width <= width)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
                lines++;

            float wordWidth = BodyFont.MeasureString(word).Width;
            if (wordWidth <= width)
            {
                current = word;
            }
            else
            {
                // A single token wider than the bubble char-wraps over several rows.
                lines += (int)Math.Ceiling(wordWidth / width) - 1;
                current = string.Empty;
            }
        }
        return lines;
    }
}
