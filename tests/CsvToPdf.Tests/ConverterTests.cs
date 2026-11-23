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
    public void RowTallerThanPageContinuesOnNextPages()
    {
        var text = string.Join(' ', Enumerable.Repeat("palavra", 12000));
        var rows = new List<string[]> { new[] { "id", "texto" }, new[] { "1", text } };

        var result = PdfExporter.Export(rows, Path.Combine(_dir, "out.pdf"), "t");

        Assert.True(result.Pages > 1);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(60)]
    [InlineData(300)]
    public void WideTableKeepsEveryColumn(int columns)
    {
        var header = Enumerable.Range(1, columns).Select(c => $"Coluna {c}").ToArray();
        var rows = new List<string[]> { header, header.Select(h => h + " valor bem comprido").ToArray() };
        var output = Path.Combine(_dir, "out.pdf");

        var result = PdfExporter.Export(rows, output, "t");

        Assert.Equal(Enumerable.Range(0, columns), result.ColumnGroups.SelectMany(g => g).Distinct().Order());
        Assert.All(result.ColumnGroups, g => Assert.Equal(0, g[0]));
        Assert.Equal(columns + result.ColumnGroups.Count - 1, result.ColumnGroups.Sum(g => g.Length));
        using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(result.Pages, pdf.PageCount);
    }

    [Theory]
    [InlineData("CodigoMuitoLongoSemEspacosQueNaoPodeGanharHifenNemPerderCaracteres")]
    [InlineData("texto com várias palavras e-um-hífen para quebrar em linhas")]
    public void WrapKeepsTextAndWidth(string text)
    {
        var measurer = new TextMeasurer("Arial");

        var lines = measurer.Wrap(text, false, 8, 40);

        Assert.True(lines.Length > 1);
        Assert.Equal(text.Replace(" ", ""), string.Concat(lines).Replace(" ", ""));
        Assert.All(lines, l => Assert.True(measurer.LongestLine(l, false) * 8 <= 40.01));
    }

    [Fact]
    public void CancelKeepsExistingPdf()
    {
        var output = Path.Combine(_dir, "out.pdf");
        File.WriteAllText(output, "original");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            PdfExporter.Export([["a"], ["1"]], output, "t", cancellation: cancelled.Token));

        Assert.Equal("original", File.ReadAllText(output));
        Assert.Single(Directory.GetFiles(_dir, "out.pdf*"));
    }

    [Fact]
    public void ReadsCsvLockedForWritingLikeExcel()
    {
        var path = WriteCsv("a,b\n1,2");
        using var excel = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        Assert.Equal(["1", "2"], CsvLoader.Load(path)[1]);
    }

    [Theory]
    [InlineData("dados.txt")]
    [InlineData("dados.tsv")]
    [InlineData("dados.pdf")]
    [InlineData("dados.csv.exe")]
    [InlineData("dados")]
    public void RejectsNonCsvExtension(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "a,b\n1,2");

        Assert.Throws<InvalidDataException>(() => CsvLoader.Load(path));
    }

    [Fact]
    public void RejectsBinaryRenamedToCsv()
    {
        var path = Path.Combine(_dir, "foto.csv");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D]);

        Assert.Throws<InvalidDataException>(() => CsvLoader.Load(path));
    }

    [Fact]
    public void AcceptsUppercaseExtensionAndUtf16()
    {
        var path = Path.Combine(_dir, "DADOS.CSV");
        File.WriteAllText(path, "a,b\n1,2", Encoding.Unicode);

        Assert.Equal(["1", "2"], CsvLoader.Load(path)[1]);
    }

    [Fact]
    public void RejectsEmptyCsv() =>
        Assert.Throws<InvalidDataException>(() => PdfExporter.Export([], Path.Combine(_dir, "out.pdf"), "t"));
}
