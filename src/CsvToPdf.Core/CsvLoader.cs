using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace CsvToPdf;

public static class CsvLoader
{
    private static readonly char[] Delimiters = [',', ';', '\t', '|'];

    static CsvLoader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static bool IsCsvPath(string path) =>
        Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads a CSV file into rows that all have the same number of columns.</summary>
    public static List<string[]> Load(string path)
    {
        if (!IsCsvPath(path))
            throw new InvalidDataException("Apenas arquivos .csv são aceitos.");

        var text = ReadText(path);
        // Decoded text never has NUL, so this catches binaries renamed to .csv.
        if (text.Contains('\0'))
            throw new InvalidDataException("O arquivo não é um CSV válido.");

        using var parser = new TextFieldParser(new StringReader(text))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
        };
        parser.SetDelimiters(DetectDelimiter(text).ToString());

        var rows = new List<string[]>();
        while (!parser.EndOfData)
            rows.Add(parser.ReadFields() ?? []);

        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
        return rows.Select(r => r.Concat(Enumerable.Repeat("", columns - r.Length)).ToArray()).ToList();
    }

    // Excel in pt-BR saves Windows-1252 unless "CSV UTF-8" is chosen.
    private static string ReadText(string path)
    {
        // FileShare.ReadWrite allows reading a CSV that is still open in Excel.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        file.CopyTo(buffer);
        var bytes = buffer.ToArray();
        try
        {
            using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), true);
            return reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private static char DetectDelimiter(string text)
    {
        var counts = Delimiters.ToDictionary(d => d, _ => 0);
        var inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (inQuotes) continue;
            else if (c is '\r' or '\n') break;
            else if (counts.ContainsKey(c)) counts[c]++;
        }
        return counts.MaxBy(kv => kv.Value).Key;
    }
}
