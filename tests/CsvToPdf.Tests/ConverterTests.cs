using System.Text;
using PdfSharp.Pdf.IO;

namespace CsvToPdf.Tests;

public class ConverterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("csvtopdf").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string WriteCsv(string content, Encoding? encoding = null)
    {
        var path = Path.Combine(_dir, "data.csv");
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    [Theory]
    [InlineData("nome,valor\nAna,1", ',')]
    [InlineData("nome;valor\nAna;1", ';')]
    [InlineData("nome\tvalor\nAna\t1", '\t')]
    public void DetectsDelimiter(string csv, char _)
    {
        var rows = CsvLoader.Load(WriteCsv(csv));
        Assert.Equal(["Ana", "1"], rows[1]);
    }

    [Fact]
    public void DelimiterInsideQuotedHeaderIsIgnored()
    {
        var rows = CsvLoader.Load(WriteCsv("\"a,b,c\";d\n1;2"));
        Assert.Equal(["a,b,c", "d"], rows[0]);
    }

    [Fact]
    public void KeepsQuotesAndLineBreaksInsideFields()
    {
        var rows = CsvLoader.Load(WriteCsv("a,b\n\"x, \"\"y\"\"\",\"l1\nl2\""));
        Assert.Equal("x, \"y\"", rows[1][0]);
        Assert.Equal("l1\nl2", rows[1][1].ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ReadsWindows1252Accents()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var rows = CsvLoader.Load(WriteCsv("cidade\nSão Paulo", Encoding.GetEncoding(1252)));
        Assert.Equal("São Paulo", rows[1][0]);
    }

    [Fact]
    public void PadsShortRows()
    {
        var rows = CsvLoader.Load(WriteCsv("a,b,c\n1"));
        Assert.Equal(["1", "", ""], rows[1]);
    }

    [Fact]
    public void LargeCsvSpansMultiplePages()
    {
        var rows = new List<string[]> { new[] { "id", "texto" } };
        rows.AddRange(Enumerable.Range(1, 500).Select(i => new[] { i.ToString(), new string('x', 200) }));
        var output = Path.Combine(_dir, "out.pdf");

        PdfExporter.Export(rows, output, "teste");

        using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 1);
        Assert.True(pdf.Pages[0].Width.Point > pdf.Pages[0].Height.Point, "A tabela precisa de página paisagem.");
    }

    [Fact]
    public void RejectsRowTallerThanPage()
    {
        var text = string.Join(' ', Enumerable.Repeat("palavra", 12000));
        var rows = new List<string[]> { new[] { "id", "texto" }, new[] { "1", text } };
        var error = Assert.Throws<InvalidDataException>(() =>
            PdfExporter.Export(rows, Path.Combine(_dir, "out.pdf"), "t"));
        Assert.Contains("registro 2", error.Message);
    }

    [Fact]
    public void RejectsEmptyAndTooWideCsv()
    {
        var output = Path.Combine(_dir, "out.pdf");
        Assert.Throws<InvalidDataException>(() => PdfExporter.Export([], output, "t"));
        var wide = new[] { Enumerable.Repeat("x", PdfExporter.MaxColumns + 1).ToArray() };
        Assert.Throws<InvalidDataException>(() => PdfExporter.Export(wide, output, "t"));
    }
}
