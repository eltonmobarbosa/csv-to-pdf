using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace CsvToPdf;

public enum PaperSize { A4, A3 }

public sealed record ExportOptions(PaperSize Paper = PaperSize.A4, bool Landscape = true);

/// <summary>ColumnGroups holds the column indexes printed together. Column 0 repeats in every group.</summary>
public sealed record ExportResult(int Pages, double FontSize, IReadOnlyList<int[]> ColumnGroups);

public static class PdfExporter
{
    private const string FontName = "Arial";
    private const double Margin = 36;
    private const double TitleHeight = 26;
    private const double FooterHeight = 16;
    private const double Padding = 3;
    private const double LineSpacing = 1.2;
    private const double MinColumnWidth = 34;
    private const double MaxColumnWidthPerPoint = 21; // about 170pt at font size 8
    private const double SplitFontSize = 7;
    private static readonly double[] FontSizes = [8, 7, 6];

    // shortcut: uses installed Windows fonts, add a FontResolver to run on Linux/macOS.
    static PdfExporter() => GlobalFontSettings.UseWindowsFontsUnderWindows = true;

    public static ExportResult Export(
        IReadOnlyList<string[]> rows,
        string outputPath,
        string title,
        ExportOptions? options = null,
        IProgress<int>? progress = null,
        CancellationToken cancellation = default)
    {
        if (rows.Count == 0 || rows[0].Length == 0)
            throw new InvalidDataException("O CSV está vazio.");

        options ??= new ExportOptions();
        var (shortSide, longSide) = options.Paper == PaperSize.A3 ? (842d, 1191d) : (595d, 842d);
        var (pageWidth, pageHeight) = options.Landscape ? (longSide, shortSide) : (shortSide, longSide);

        var measurer = new TextMeasurer(FontName);
        var (fontSize, widths, groups) = PlanColumns(rows, measurer, pageWidth - 2 * Margin);

        using var document = new PdfDocument();
        document.Info.Title = title;
        var writer = new TableWriter(document, measurer, title, pageWidth, pageHeight, fontSize);

        var totalRows = Math.Max(1, (rows.Count - 1) * groups.Count);
        var doneRows = 0;
        var lastPercent = -1;
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var groupWidths = Stretch(group.Select(c => widths[c]).ToArray(), pageWidth - 2 * Margin);
            var label = groups.Count == 1 ? "" :
                $"Colunas {group[g == 0 ? 0 : 1] + 1} a {group[^1] + 1} de {rows[0].Length}" + (g == 0 ? "" : " (coluna 1 repetida)");
            writer.BeginGroup(group.Select(c => rows[0][c]).ToArray(), groupWidths, label);

            for (var r = 1; r < rows.Count; r++)
            {
                cancellation.ThrowIfCancellationRequested();
                var row = rows[r];
                writer.WriteRow(group.Select(c => row[c]).ToArray(), r % 2 == 0);

                var percent = ++doneRows * 95 / totalRows;
                if (percent != lastPercent) progress?.Report(lastPercent = percent);
            }
        }

        var pages = writer.Finish();
        cancellation.ThrowIfCancellationRequested();
        SaveAtomically(document, outputPath);
        progress?.Report(100);
        return new ExportResult(pages, fontSize, groups);
    }

    // A failed or cancelled save must never damage an existing PDF.
    private static void SaveAtomically(PdfDocument document, string outputPath)
    {
        var temp = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            document.Save(temp);
            File.Move(temp, outputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static (double FontSize, double[] Widths, List<int[]> Groups) PlanColumns(
        IReadOnlyList<string[]> rows, TextMeasurer measurer, double usableWidth)
    {
        var columns = rows[0].Length;
        var natural = new double[columns];
        var headerWord = new double[columns];
        for (var c = 0; c < columns; c++)
        {
            headerWord[c] = measurer.LongestWord(rows[0][c], bold: true);
            natural[c] = measurer.LongestLine(rows[0][c], bold: true);
            for (var r = 1; r < rows.Count; r++)
                natural[c] = Math.Max(natural[c], measurer.LongestLine(rows[r][c], bold: false));
        }

        double[] WidthsAt(double size) => Enumerable.Range(0, columns).Select(c =>
        {
            var max = MaxColumnWidthPerPoint * size;
            var min = Math.Max(MinColumnWidth, Math.Min(headerWord[c] * size + 2 * Padding, max));
            return Math.Clamp(natural[c] * size + 2 * Padding + 1, min, max);
        }).ToArray();

        foreach (var size in FontSizes)
        {
            var widths = WidthsAt(size);
            if (widths.Sum() <= usableWidth)
                return (size, widths, [Enumerable.Range(0, columns).ToArray()]);
        }

        // Too wide even at the smallest font: print blocks of columns, repeating the first one.
        var splitWidths = WidthsAt(SplitFontSize);
        var groups = new List<int[]>();
        var current = new List<int> { 0 };
        var used = splitWidths[0];
        for (var c = 1; c < columns; c++)
        {
            if (used + splitWidths[c] > usableWidth && current.Count > 1)
            {
                groups.Add(current.ToArray());
                current = [0];
                used = splitWidths[0];
            }
            current.Add(c);
            used += splitWidths[c];
        }
        groups.Add(current.ToArray());
        return (SplitFontSize, splitWidths, groups);
    }

    private static double[] Stretch(double[] widths, double usableWidth)
    {
        var factor = usableWidth / widths.Sum();
        return widths.Select(w => w * factor).ToArray();
    }

    private sealed class TableWriter(
        PdfDocument document, TextMeasurer measurer, string title, double pageWidth, double pageHeight, double fontSize)
    {
        private static readonly XColor HeaderColor = XColor.FromArgb(217, 225, 242);
        private static readonly XColor StripeColor = XColor.FromArgb(244, 244, 244);

        private readonly XFont _regular = new(FontName, fontSize, XFontStyleEx.Regular);
        private readonly XFont _bold = new(FontName, fontSize, XFontStyleEx.Bold);
        private readonly XFont _titleFont = new(FontName, 14, XFontStyleEx.Bold);
        private readonly XFont _footerFont = new(FontName, 8, XFontStyleEx.Regular);
        private readonly XPen _border = new(XColors.Gray, 0.4);
        private readonly double _lineHeight = fontSize * LineSpacing;
        private readonly double _bottom = pageHeight - Margin - FooterHeight;
        private readonly List<(PdfPage Page, string Label)> _pages = [];

        private XGraphics? _graphics;
        private double[] _widths = [];
        private string[][] _headerLines = [];
        private bool _repeatHeader;
        private string _label = "";
        private double _y;
        private double _contentTop;

        public void BeginGroup(string[] header, double[] widths, string label)
        {
            _widths = widths;
            _label = label;
            _headerLines = WrapCells(header, bold: true);
            // A huge header cannot repeat on every page, so it prints once like a normal row.
            _repeatHeader = RowHeight(_headerLines.Max(l => l.Length)) <= (_bottom - Margin) / 3;
            NewPage();
            if (!_repeatHeader) WriteLines(_headerLines, _bold, HeaderColor);
        }

        public void WriteRow(string[] cells, bool striped) =>
            WriteLines(WrapCells(cells, bold: false), _regular, striped ? StripeColor : XColors.White);

        public int Finish()
        {
            _graphics?.Dispose();
            _graphics = null;
            for (var i = 0; i < _pages.Count; i++)
            {
                var (page, label) = _pages[i];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                var y = pageHeight - Margin;
                if (label.Length > 0)
                    gfx.DrawString(label, _footerFont, XBrushes.DimGray, Margin, y);
                var text = $"Página {i + 1} de {_pages.Count}";
                var x = pageWidth - Margin - gfx.MeasureString(text, _footerFont).Width;
                gfx.DrawString(text, _footerFont, XBrushes.Black, x, y);
            }
            return _pages.Count;
        }

        private string[][] WrapCells(string[] cells, bool bold) =>
            cells.Select((text, c) => measurer.Wrap(text, bold, fontSize, _widths[c] - 2 * Padding)).ToArray();

        private double RowHeight(int lines) => lines * _lineHeight + 2 * Padding;

        private void NewPage()
        {
            _graphics?.Dispose();
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(pageWidth);
            page.Height = XUnit.FromPoint(pageHeight);
            _pages.Add((page, _label));
            _graphics = XGraphics.FromPdfPage(page);

            _y = Margin;
            if (_pages.Count == 1)
            {
                var line = measurer.Wrap(title, true, 14, pageWidth - 2 * Margin)[0];
                _graphics.DrawString(line, _titleFont, XBrushes.Black, Margin, _y, XStringFormats.TopLeft);
                _y += TitleHeight;
            }
            if (_repeatHeader)
            {
                DrawFragment(_headerLines, new int[_headerLines.Length], _headerLines.Max(l => l.Length), _bold, HeaderColor);
            }
            _contentTop = _y;
        }

        // Rows taller than a page continue on the next one, so no text is ever lost.
        private void WriteLines(string[][] lines, XFont font, XColor fill)
        {
            var offsets = new int[lines.Length];
            while (true)
            {
                var remaining = lines.Select((l, c) => l.Length - offsets[c]).Max();
                if (remaining <= 0) return;

                var take = remaining;
                if (_y + RowHeight(remaining) > _bottom)
                {
                    var fullPage = _bottom - Margin - (_repeatHeader ? RowHeight(_headerLines.Max(l => l.Length)) : 0);
                    if (RowHeight(remaining) <= fullPage && _y > _contentTop)
                    {
                        NewPage();
                        continue;
                    }
                    take = (int)((_bottom - _y - 2 * Padding) / _lineHeight);
                    if (take < 1)
                    {
                        NewPage();
                        continue;
                    }
                }

                DrawFragment(lines, offsets, take, font, fill);
                for (var c = 0; c < offsets.Length; c++)
                    offsets[c] = Math.Min(lines[c].Length, offsets[c] + take);
            }
        }

        private void DrawFragment(string[][] lines, int[] offsets, int count, XFont font, XColor fill)
        {
            var height = RowHeight(count);
            var brush = new XSolidBrush(fill);
            var x = Margin;
            for (var c = 0; c < _widths.Length; c++)
            {
                _graphics!.DrawRectangle(_border, brush, x, _y, _widths[c], height);
                var end = Math.Min(lines[c].Length, offsets[c] + count);
                for (var l = offsets[c]; l < end; l++)
                {
                    var textY = _y + Padding + (l - offsets[c]) * _lineHeight;
                    if (lines[c][l].Length > 0)
                        _graphics.DrawString(lines[c][l], font, XBrushes.Black, x + Padding, textY, XStringFormats.TopLeft);
                }
                x += _widths[c];
            }
            _y += height;
        }
    }
}
