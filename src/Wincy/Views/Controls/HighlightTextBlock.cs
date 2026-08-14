using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Wincy.Services;

namespace Wincy.Views.Controls;

/// <summary>
/// The row title.
///
/// Two things WPF's TextBlock cannot do together are needed here, so the text is
/// drawn directly: search matches are emphasised in place, and over-long titles are
/// truncated in the *middle* — which is what makes a list of long paths or URLs
/// readable, and is how Maccy renders its rows.
/// </summary>
public sealed class HighlightTextBlock : FrameworkElement
{
    private const string Ellipsis = "…";

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(HighlightTextBlock),
        new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RangesProperty = DependencyProperty.Register(
        nameof(Ranges), typeof(IReadOnlyList<TextRange>), typeof(HighlightTextBlock),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightStyleProperty = DependencyProperty.Register(
        nameof(HighlightStyle), typeof(HighlightMatch), typeof(HighlightTextBlock),
        new FrameworkPropertyMetadata(HighlightMatch.Bold, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.Register(
        nameof(HighlightBrush), typeof(Brush), typeof(HighlightTextBlock),
        new FrameworkPropertyMetadata(Brushes.Yellow, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty =
        System.Windows.Controls.TextBlock.ForegroundProperty.AddOwner(typeof(HighlightTextBlock),
            new FrameworkPropertyMetadata(Brushes.Black,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty FontFamilyProperty =
        System.Windows.Controls.TextBlock.FontFamilyProperty.AddOwner(typeof(HighlightTextBlock),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty FontSizeProperty =
        System.Windows.Controls.TextBlock.FontSizeProperty.AddOwner(typeof(HighlightTextBlock),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontSize,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.Inherits));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IReadOnlyList<TextRange>? Ranges
    {
        get => (IReadOnlyList<TextRange>?)GetValue(RangesProperty);
        set => SetValue(RangesProperty, value);
    }

    public HighlightMatch HighlightStyle
    {
        get => (HighlightMatch)GetValue(HighlightStyleProperty);
        set => SetValue(HighlightStyleProperty, value);
    }

    public Brush HighlightBrush
    {
        get => (Brush)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Text;
        if (string.IsNullOrEmpty(text))
        {
            return new Size(0, LineHeight());
        }

        var formatted = Build(text);
        var width = double.IsInfinity(availableSize.Width)
            ? formatted.Width
            : Math.Min(formatted.Width, availableSize.Width);

        return new Size(width, Math.Max(formatted.Height, LineHeight()));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var text = Text;
        if (string.IsNullOrEmpty(text) || ActualWidth <= 0)
        {
            return;
        }

        var (display, ranges) = Fit(text, Ranges ?? [], ActualWidth);
        var formatted = Build(display);

        ApplyHighlights(formatted, ranges, display.Length);

        var y = (ActualHeight - formatted.Height) / 2;
        drawingContext.DrawText(formatted, new Point(0, Math.Max(0, y)));
    }

    private void ApplyHighlights(FormattedText formatted, IReadOnlyList<TextRange> ranges, int length)
    {
        foreach (var range in ranges)
        {
            var start = Math.Clamp(range.Start, 0, length);
            var count = Math.Clamp(range.Length, 0, length - start);
            if (count == 0)
            {
                continue;
            }

            switch (HighlightStyle)
            {
                case HighlightMatch.Bold:
                    formatted.SetFontWeight(FontWeights.Bold, start, count);
                    break;
                case HighlightMatch.Italic:
                    formatted.SetFontStyle(FontStyles.Italic, start, count);
                    break;
                case HighlightMatch.Underline:
                    formatted.SetTextDecorations(TextDecorations.Underline, start, count);
                    break;
                case HighlightMatch.Highlight:
                    // FormattedText cannot paint a background run, so the emphasis is
                    // carried by the foreground colour instead.
                    formatted.SetForegroundBrush(HighlightBrush, start, count);
                    formatted.SetFontWeight(FontWeights.SemiBold, start, count);
                    break;
            }
        }
    }

    /// <summary>
    /// Shortens the text to fit, keeping the head and the tail and eliding the middle,
    /// then remaps the highlight ranges onto the shortened string.
    /// </summary>
    private (string Display, List<TextRange> Ranges) Fit(string text, IReadOnlyList<TextRange> ranges, double available)
    {
        var full = Build(text);
        if (full.Width <= available)
        {
            return (text, [.. ranges]);
        }

        // Estimate how many characters fit, then walk down until it really does.
        var averageWidth = full.Width / Math.Max(1, text.Length);
        var budget = Math.Max(1, (int)(available / Math.Max(0.001, averageWidth)) - 1);

        string display;
        int head, tail;

        while (true)
        {
            head = budget / 2;
            tail = budget - head;

            if (head + tail >= text.Length)
            {
                return (text, [.. ranges]);
            }

            display = string.Concat(text.AsSpan(0, head), Ellipsis, text.AsSpan(text.Length - tail));

            if (Build(display).Width <= available || budget <= 2)
            {
                break;
            }

            budget -= Math.Max(1, budget / 8);
        }

        // Ranges before the cut keep their offsets; ranges after it shift left by the
        // number of elided characters. Ranges straddling the cut are trimmed away.
        var elided = text.Length - head - tail;
        var shift = elided - Ellipsis.Length;
        var tailStart = text.Length - tail;

        var mapped = new List<TextRange>();
        foreach (var range in ranges)
        {
            var start = range.Start;
            var end = range.End;

            if (end <= head)
            {
                mapped.Add(range);
            }
            else if (start >= tailStart)
            {
                mapped.Add(new TextRange(start - shift, range.Length));
            }
            else
            {
                // Keep whichever side of the ellipsis is still visible.
                if (start < head)
                {
                    mapped.Add(new TextRange(start, head - start));
                }

                if (end > tailStart)
                {
                    mapped.Add(new TextRange(tailStart - shift, end - tailStart));
                }
            }
        }

        return (display, mapped);
    }

    private FormattedText Build(string text) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        FontSize,
        Foreground,
        VisualTreeHelper.GetDpi(this).PixelsPerDip)
    {
        MaxLineCount = 1,
        Trimming = TextTrimming.None
    };

    private double LineHeight() => FontSize * 1.35;
}
