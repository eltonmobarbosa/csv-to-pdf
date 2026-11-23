using CsvToPdf;
using Microsoft.VisualBasic.FileIO;

var force = args.Contains("--force");
var paths = args.Where(a => a != "--force").ToArray();

if (paths.Length is 0 or > 2)
{
    Console.Error.WriteLine("Uso: CsvToPdf <entrada.csv> [saida.pdf] [--force]");
    return 2;
}

var input = paths[0];
var output = paths.Length == 2 ? paths[1] : Path.ChangeExtension(input, ".pdf");

if (!File.Exists(input))
{
    Console.Error.WriteLine($"Arquivo não encontrado: {input}");
    return 1;
}
if (File.Exists(output) && !force)
{
    Console.Error.WriteLine($"{output} já existe. Use --force para sobrescrever.");
    return 1;
}

try
{
    var rows = CsvLoader.Load(input);
    PdfExporter.Export(rows, output, Path.GetFileNameWithoutExtension(input));
    Console.WriteLine($"PDF gerado: {output} ({rows.Count} linhas)");
    return 0;
}
catch (MalformedLineException e)
{
    Console.Error.WriteLine($"CSV inválido na linha {e.LineNumber}: {e.Message}");
}
catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Erro: {e.Message}");
}
return 1;
