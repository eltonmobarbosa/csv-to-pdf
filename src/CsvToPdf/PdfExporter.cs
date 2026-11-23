using System.Text.RegularExpressions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace CsvToPdf;

public static class PdfExporter
{
    private const double UsableWidth = 770; // A4 landscape (842pt) minus 36pt margins
    private const double MinColumnWidth = 30;
    private const double FontSize = 8;
    private const double CellPadding = 2;
    private const double LineHeight = 9.5;
    private const double MaxTableHeight = 480; // A4 landscape (595pt) minus margins and title
    public static readonly int MaxColumns = (int)(UsableWidth / MinColumnWidth);

    // shortcut: uses installed Windows fonts, add a FontResolver to run on Linux/macOS.
    static PdfExporter() => GlobalFontSettings.UseWindowsFontsUnderWindows = true;

    public static void Export(IReadOnlyList<string[]> rows, string outputPath, string title)
    {
        if (rows.Count == 0)
            throw new InvalidDataException("O CSV está vazio.");
        if (rows[0].Length > MaxColumns)
            throw new InvalidDataException($"O CSV tem {rows[0].Length} colunas. O máximo legível é {MaxColumns}.");

        var document = new Document();
        document.Info.Title = title;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = FontSize;

        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        section.PageSetup.PageWidth = Unit.FromPoint(842); // A4 landscape
        section.PageSetup.PageHeight = Unit.FromPoint(595);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(36);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(40);

        var heading = section.AddParagraph(title);
        heading.Format.Font.Size = 14;
        heading.Format.Font.Bold = true;
        heading.Format.SpaceAfter = Unit.FromPoint(8);

        var footer = section.Footers.Primary.AddParagraph("Página ");
        footer.AddPageField();
        footer.AddText(" de ");
        footer.AddNumPagesField();
        footer.Format.Alignment = ParagraphAlignment.Right;

        section.Add(BuildTable(rows));

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(outputPath);
    }

    private static Table BuildTable(IReadOnlyList<string[]> rows)
    {
        var table = new Table { Borders = { Width = 0.5, Color = Colors.Gray } };
        table.LeftPadding = table.RightPadding = Unit.FromPoint(CellPadding);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(CellPadding);

        var widths = ColumnWidths(rows);
        foreach (var width in widths)
            table.AddColumn(Unit.FromPoint(width));

        // MigraDoc cannot split a row across pages and would silently lose its content.
        var headerHeight = EstimateHeight(rows[0], widths);
        for (var r = 0; r < rows.Count; r++)
            if (headerHeight + EstimateHeight(rows[r], widths) > MaxTableHeight)
                throw new InvalidDataException($"O registro {r + 1} tem texto demais para caber em uma página.");

        for (var r = 0; r < rows.Count; r++)
        {
            var row = table.AddRow();
            for (var c = 0; c < widths.Length; c++)
            {
                var paragraph = row.Cells[c].AddParagraph();
                var lines = rows[r][c].ReplaceLineEndings("\n").Split('\n');
                for (var l = 0; l < lines.Length; l++)
                {
                    if (l > 0) paragraph.AddLineBreak();
                    if (lines[l].Length > 0) paragraph.AddText(BreakLongWords(lines[l], widths[c]));
                }
            }
            if (r > 0 && r % 2 == 0) row.Shading.Color = Color.FromRgb(242, 242, 242);
        }

        var header = table.Rows[0];
        header.HeadingFormat = true; // repeats on every page
        header.Format.Font.Bold = true;
        header.Shading.Color = Color.FromRgb(217, 225, 242);
        return table;
    }

    // Conservative: 0.6em is wider than average Arial text.
    private static double EstimateHeight(string[] row, double[] widths) =>
        row.Select((text, c) => text.ReplaceLineEndings("\n").Split('\n')
                .Sum(line => Math.Max(1, Math.Ceiling(line.Length * FontSize * 0.6 / (widths[c] - 2 * CellPadding)))))
            .Max() * LineHeight + 2 * CellPadding;

    // Every column gets the minimum, the rest is shared by content length.
    private static double[] ColumnWidths(IReadOnlyList<string[]> rows)
    {
        var columns = rows[0].Length;
        var weights = Enumerable.Range(0, columns)
            .Select(c => (double)Math.Clamp(rows.Max(r => r[c].Length), 4, 40))
            .ToArray();
        var spare = UsableWidth - columns * MinColumnWidth;
        return weights.Select(w => MinColumnWidth + spare * w / weights.Sum()).ToArray();
    }

    // MigraDoc only wraps at spaces and hyphens, so long tokens would overflow the cell.
    private static string BreakLongWords(string text, double columnWidth)
    {
        var maxChars = Math.Max(1, (int)((columnWidth - 2 * CellPadding) / (FontSize * 0.95)));
        return Regex.Replace(text, $@"\S{{{maxChars}}}(?=\S)", "$0­");
    }
}
