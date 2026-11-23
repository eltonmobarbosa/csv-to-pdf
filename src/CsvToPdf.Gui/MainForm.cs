using System.ComponentModel;
using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;

namespace CsvToPdf.Gui;

internal sealed class MainForm : Form
{
    private const string DropText = "Arraste um arquivo CSV aqui\nou clique para escolher";

    private readonly Button _drop = new()
    {
        Dock = DockStyle.Fill,
        FlatStyle = FlatStyle.Flat,
        Text = DropText,
        AllowDrop = true,
        AccessibleName = "Escolher arquivo CSV",
        Margin = new Padding(0, 0, 0, 12),
    };
    private readonly ComboBox _paper = NewCombo("Tamanho do papel", "A4", "A3");
    private readonly ComboBox _orientation = NewCombo("Orientação da página", "Paisagem", "Retrato");
    private readonly CheckBox _openAfter = new()
    {
        Text = "Abrir ao concluir",
        Checked = true,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(12, 3, 0, 3),
    };
    private readonly Button _convert = new()
    {
        Text = "Converter para PDF",
        Dock = DockStyle.Top,
        Height = 40,
        Enabled = false,
        Margin = new Padding(0, 12, 0, 8),
    };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Height = 6, Margin = new Padding(0, 0, 0, 6) };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Top,
        AutoEllipsis = true,
        Height = 24,
        Margin = Padding.Empty,
        Text = "Nenhum arquivo selecionado.",
    };

    private string? _inputPath;
    private List<string[]>? _rows;
    private CancellationTokenSource? _cancellation;
    private bool _loading;
    private bool _closeWhenDone;

    public MainForm(string? initialFile)
    {
        Text = "CSV para PDF";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 400);
        MinimumSize = new Size(460, 380);
        StartPosition = FormStartPosition.CenterScreen;

        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Margin = Padding.Empty };
        options.Controls.AddRange([NewLabel("Papel"), _paper, NewLabel("Orientação"), _orientation, _openAfter]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_drop, 0, 0);
        layout.Controls.Add(options, 0, 1);
        layout.Controls.Add(_convert, 0, 2);
        layout.Controls.Add(_progress, 0, 3);
        layout.Controls.Add(_status, 0, 4);
        Controls.Add(layout);

        _drop.FlatAppearance.BorderColor = SystemColors.ControlDark;
        _drop.Click += (_, _) => ChooseFile();
        _drop.DragEnter += OnDragEnter;
        _drop.DragLeave += (_, _) => _drop.BackColor = SystemColors.Control;
        _drop.DragDrop += OnDragDrop;
        _convert.Click += async (_, _) => await ConvertOrCancelAsync();
        FormClosing += OnFormClosing;

        if (initialFile is not null)
            Shown += async (_, _) => await LoadFileAsync(initialFile);
    }

    private bool IsBusy => _loading || _cancellation is not null;

    private static ComboBox NewCombo(string accessibleName, params object[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 92, AccessibleName = accessibleName };
        combo.Items.AddRange(items);
        combo.SelectedIndex = 0;
        return combo;
    }

    private static Label NewLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 3, 4, 3),
    };

    private void ChooseFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Escolher arquivo CSV",
            Filter = "Arquivos CSV (*.csv)|*.csv",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _ = LoadFileAsync(dialog.FileName);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var valid = !IsBusy && DroppedFile(e) is { } file && CsvLoader.IsCsvPath(file);
        e.Effect = valid ? DragDropEffects.Copy : DragDropEffects.None;
        if (valid) _drop.BackColor = SystemColors.ControlLight;
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        _drop.BackColor = SystemColors.Control;
        if (DroppedFile(e) is { } file) await LoadFileAsync(file);
    }

    private static string? DroppedFile(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files ? files[0] : null;

    private async Task LoadFileAsync(string path)
    {
        if (IsBusy) return;
        _loading = true;
        _rows = null;
        _progress.Value = 0;
        UpdateControls();
        ShowStatus("Lendo arquivo...");
        try
        {
            var rows = await Task.Run(() => CsvLoader.Load(path));
            if (rows.Count == 0) throw new InvalidDataException("O CSV está vazio.");
            _rows = rows;
            _inputPath = path;
            _drop.Text = Path.GetFileName(path) + "\n\nArraste outro arquivo ou clique para trocar";
            ShowStatus($"{rows.Count - 1:N0} registros, {rows[0].Length} colunas.");
        }
        catch (Exception e) when (IsExpected(e))
        {
            _inputPath = null;
            _drop.Text = DropText;
            ShowStatus(e switch
            {
                MalformedLineException m => $"CSV inválido na linha {m.LineNumber}.",
                FileNotFoundException or DirectoryNotFoundException => "Arquivo não encontrado.",
                IOException or UnauthorizedAccessException => "Não foi possível ler o arquivo.",
                OutOfMemoryException => "Arquivo grande demais para a memória disponível.",
                _ => e.Message,
            }, error: true);
        }
        finally
        {
            _loading = false;
            UpdateControls();
        }
    }

    private async Task ConvertOrCancelAsync()
    {
        if (_cancellation is not null)
        {
            _cancellation.Cancel();
            ShowStatus("Cancelando...");
            return;
        }
        if (_rows is null || _inputPath is null) return;

        using var dialog = new SaveFileDialog
        {
            Title = "Salvar PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            DefaultExt = "pdf",
            FileName = Path.GetFileNameWithoutExtension(_inputPath) + ".pdf",
            InitialDirectory = Path.GetDirectoryName(_inputPath),
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            await ExportAsync(dialog.FileName);
    }

    private async Task ExportAsync(string output)
    {
        var rows = _rows!;
        var title = Path.GetFileNameWithoutExtension(_inputPath!);
        var options = new ExportOptions(
            _paper.SelectedIndex == 0 ? PaperSize.A4 : PaperSize.A3,
            _orientation.SelectedIndex == 0);
        var progress = new Progress<int>(p => _progress.Value = p);

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        UpdateControls();
        ShowStatus("Gerando PDF...");
        try
        {
            var result = await Task.Run(() => PdfExporter.Export(rows, output, title, options, progress, cancellation.Token));
            var blocks = result.ColumnGroups.Count > 1 ? $", colunas em {result.ColumnGroups.Count} blocos" : "";
            var pages = result.Pages == 1 ? "1 página" : $"{result.Pages:N0} páginas";
            ShowStatus($"PDF pronto: {pages}{blocks}.");
            if (_openAfter.Checked && !_closeWhenDone) OpenPdf(output);
        }
        catch (OperationCanceledException)
        {
            _progress.Value = 0;
            ShowStatus("Conversão cancelada.");
        }
        catch (Exception e) when (IsExpected(e))
        {
            _progress.Value = 0;
            ShowStatus(e switch
            {
                IOException or UnauthorizedAccessException => "Não foi possível salvar. Feche o PDF se ele estiver aberto.",
                OutOfMemoryException => "Arquivo grande demais para a memória disponível.",
                _ => e.Message,
            }, error: true);
        }
        finally
        {
            _cancellation = null;
            UpdateControls();
            if (_closeWhenDone) Close();
        }
    }

    private void OpenPdf(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            ShowStatus("PDF salvo, mas nenhum leitor de PDF foi encontrado.", error: true);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_cancellation is null) return;
        e.Cancel = true;
        _closeWhenDone = true;
        _cancellation.Cancel();
    }

    private void UpdateControls()
    {
        var converting = _cancellation is not null;
        _drop.Enabled = _paper.Enabled = _orientation.Enabled = !IsBusy;
        _convert.Text = converting ? "Cancelar" : "Converter para PDF";
        _convert.Enabled = converting || (!_loading && _rows is not null);
    }

    private void ShowStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.Firebrick : SystemColors.ControlText;
    }

    private static bool IsExpected(Exception e) =>
        e is MalformedLineException or InvalidDataException or IOException or UnauthorizedAccessException or OutOfMemoryException;
}
