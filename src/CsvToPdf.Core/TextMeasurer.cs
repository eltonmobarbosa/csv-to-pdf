using PdfSharp.Drawing;
using PdfSharp.Fonts;

namespace CsvToPdf;

/// <summary>Measures and wraps text. Widths are in em, so they scale with any font size.</summary>
internal sealed class TextMeasurer
{
    private const double BaseSize = 100;

    private readonly XGraphics _graphics;
    private readonly XFont _regular;
    private readonly XFont _bold;
    private readonly Dictionary<char, double> _regularWidths = [];
    private readonly Dictionary<char, double> _boldWidths = [];

    static TextMeasurer() => GlobalFontSettings.UseWindowsFontsUnderWindows = true;

    public TextMeasurer(string fontName)
    {
        _graphics = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
        _regular = new XFont(fontName, BaseSize, XFontStyleEx.Regular);
        _bold = new XFont(fontName, BaseSize, XFontStyleEx.Bold);
    }

    public double LongestLine(string text, bool bold)
    {
        double longest = 0, current = 0;
        foreach (var c in text)
        {
            if (c is '\n' or '\r') { longest = Math.Max(longest, current); current = 0; }
            else current += CharWidth(c, bold);
        }
        return Math.Max(longest, current);
    }

    public double LongestWord(string text, bool bold)
    {
        double longest = 0, current = 0;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c == '-') { longest = Math.Max(longest, current); current = 0; }
            else current += CharWidth(c, bold);
        }
        return Math.Max(longest, current);
    }

    /// <summary>Wraps at spaces and hyphens. Longer words break between characters, adding nothing.</summary>
    public string[] Wrap(string text, bool bold, double fontSize, double maxWidth)
    {
        if (text.Length == 0) return [""];

        var limit = maxWidth / fontSize;
        var lines = new List<string>();
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var paragraph = string.Concat(raw.Select(c => char.IsControl(c) ? ' ' : c));
            int start = 0, lastBreak = -1;
            double width = 0;
            for (var i = 0; i < paragraph.Length; i++)
            {
                var charWidth = CharWidth(paragraph[i], bold);
                if (width + charWidth > limit && i > start)
                {
                    var cut = lastBreak > start ? lastBreak : i;
                    if (char.IsLowSurrogate(paragraph[cut]) && cut - 1 > start) cut--;
                    lines.Add(paragraph[start..cut].TrimEnd());
                    start = cut;
                    lastBreak = -1;
                    width = 0;
                    for (var j = start; j < i; j++) width += CharWidth(paragraph[j], bold);
                }
                if (paragraph[i] is ' ' or '-') lastBreak = i + 1;
                width += charWidth;
            }
            lines.Add(paragraph[start..].TrimEnd());
        }
        return lines.ToArray();
    }

    private double CharWidth(char c, bool bold)
    {
        if (char.IsSurrogate(c)) return 0.6;
        var cache = bold ? _boldWidths : _regularWidths;
        if (!cache.TryGetValue(c, out var width))
        {
            width = _graphics.MeasureString(c.ToString(), bold ? _bold : _regular).Width / BaseSize;
            cache[c] = width;
        }
        return width;
    }
}
