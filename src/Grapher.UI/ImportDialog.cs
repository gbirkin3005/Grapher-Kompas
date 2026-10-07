using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Grapher.Core.Import;
using Grapher.Core.Parsing;

namespace Grapher.UI;

/// <summary>
/// Импорт таблицы из Excel или CSV: выбор листа, диапазона, строки заголовков и столбца X.
/// </summary>
public sealed class ImportDialog : Form
{
    private const int PreviewRows = 200;

    private readonly string _path;
    private readonly bool _isExcel;
    private readonly List<SheetData> _sheets = new List<SheetData>();

    private readonly ComboBox _sheetBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _delimiterBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly TextBox _rangeBox = new TextBox { Width = 220 };
    private readonly CheckBox _headerBox = new CheckBox { Text = "Первая строка — заголовки", AutoSize = true };
    private readonly ComboBox _xColumnBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Label _statusLabel = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly DataGridView _preview = new DataGridView();
    private readonly Button _okButton = Ui.Button("Импортировать");

    private bool _updating;
    private bool _headerTouched;

    private ImportDialog(string path)
    {
        _path = path;
        _isExcel = SpreadsheetImporter.IsExcelFile(path);
        BuildLayout();
    }

    /// <summary>Таблица, выбранная пользователем.</summary>
    public ImportedTable Result { get; private set; }

    /// <summary>Показывает диалог импорта. Возвращает null, если пользователь отказался или файл не прочитан.</summary>
    public static ImportedTable Show(IWin32Window owner, string path)
    {
        using (var dialog = new ImportDialog(path))
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                dialog.LoadFile();
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(owner, ex.Message, "Импорт данных", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }

            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
        }
    }

    /// <summary>Снимок диалога для заданного файла (проверка раскладки без ручного запуска).</summary>
    public static Bitmap CaptureImage(string path, string range = null)
    {
        using (var dialog = new ImportDialog(path))
        {
            dialog.LoadFile();
            if (!string.IsNullOrEmpty(range)) dialog._rangeBox.Text = range;
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(40, 40);
            dialog.Show();
            Application.DoEvents();
            var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(0, 0, dialog.Width, dialog.Height));
            dialog.Hide();
            return bitmap;
        }
    }

    private void BuildLayout()
    {
        Text = "Импорт данных — " + Path.GetFileName(_path);
        Icon = AppIcon.Get();
        Font = SystemFonts.MessageBoxFont;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(640, 520);
        MinimumSize = new Size(520, 400);

        var options = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Padding = new Padding(8, 8, 8, 4)
        };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        if (_isExcel)
        {
            options.Controls.Add(Ui.Label("Лист:"), 0, row);
            options.Controls.Add(_sheetBox, 1, row++);
            options.Controls.Add(Ui.Label("Диапазон:"), 0, row);
            options.Controls.Add(_rangeBox, 1, row);
            options.Controls.Add(Ui.Hint("например A1:C101; пусто — весь лист"), 2, row++);
        }
        else
        {
            options.Controls.Add(Ui.Label("Разделитель столбцов:"), 0, row);
            options.Controls.Add(_delimiterBox, 1, row++);
            _delimiterBox.Items.AddRange(new object[] { "Определить автоматически", "Точка с запятой  ;", "Запятая  ,", "Табуляция", "Пробелы" });
            _delimiterBox.SelectedIndex = 0;
            options.Controls.Add(Ui.Label("Диапазон:"), 0, row);
            options.Controls.Add(_rangeBox, 1, row);
            options.Controls.Add(Ui.Hint("например A2:B500; пусто — весь файл"), 2, row++);
        }

        options.Controls.Add(Ui.Label("Столбец X:"), 0, row);
        options.Controls.Add(_xColumnBox, 1, row);
        options.Controls.Add(Ui.Hint("остальные столбцы станут кривыми Y"), 2, row++);
        _headerBox.Margin = new Padding(3, 6, 3, 3);
        options.Controls.Add(_headerBox, 1, row);
        options.SetColumnSpan(_headerBox, 2);

        _preview.Dock = DockStyle.Fill;
        _preview.ReadOnly = true;
        _preview.AllowUserToAddRows = false;
        _preview.AllowUserToDeleteRows = false;
        _preview.AllowUserToResizeRows = false;
        _preview.RowHeadersVisible = false;
        _preview.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _preview.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _preview.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _preview.BackgroundColor = SystemColors.Window;
        _preview.BorderStyle = BorderStyle.FixedSingle;

        var previewPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
        previewPanel.Controls.Add(_preview);

        Button cancel = Ui.Button("Отмена");
        cancel.DialogResult = DialogResult.Cancel;
        _okButton.DialogResult = DialogResult.OK;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8, 4, 8, 8)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_okButton);

        var statusPanel = new Panel { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(10, 2, 8, 0) };
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.AutoSize = false;
        statusPanel.Controls.Add(_statusLabel);

        Controls.Add(previewPanel);
        Controls.Add(statusPanel);
        Controls.Add(buttons);
        Controls.Add(options);
        AcceptButton = _okButton;
        CancelButton = cancel;

        _sheetBox.SelectedIndexChanged += (s, e) => OnSourceChanged();
        _delimiterBox.SelectedIndexChanged += (s, e) => OnDelimiterChanged();
        _rangeBox.TextChanged += (s, e) => UpdatePreview(false);
        _xColumnBox.SelectedIndexChanged += (s, e) => UpdatePreview(true);
        _headerBox.CheckedChanged += (s, e) =>
        {
            if (!_updating) _headerTouched = true;
            UpdatePreview(true);
        };

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private void LoadFile()
    {
        _sheets.Clear();
        if (_isExcel)
        {
            _sheets.AddRange(SpreadsheetImporter.ReadWorkbook(_path));
            if (_sheets.Count == 0)
            {
                throw new InvalidDataException("В книге «" + Path.GetFileName(_path) + "» нет листов.");
            }

            _updating = true;
            _sheetBox.Items.Clear();
            int firstWithData = 0;
            for (int i = 0; i < _sheets.Count; i++)
            {
                _sheetBox.Items.Add(_sheets[i].Name);
                if (_sheets[firstWithData].Rows.Count == 0 && _sheets[i].Rows.Count > 0) firstWithData = i;
            }
            _sheetBox.SelectedIndex = firstWithData;
            _updating = false;
        }
        else
        {
            _sheets.Add(SpreadsheetImporter.ReadDelimitedFile(_path, SelectedDelimiter()));
        }
        OnSourceChanged();
    }

    private char SelectedDelimiter()
    {
        switch (_delimiterBox.SelectedIndex)
        {
            case 1: return ';';
            case 2: return ',';
            case 3: return '\t';
            case 4: return TableTextParser.Whitespace;
            default: return '\0';
        }
    }

    private void OnDelimiterChanged()
    {
        if (_updating || _isExcel) return;
        try
        {
            _sheets.Clear();
            _sheets.Add(SpreadsheetImporter.ReadDelimitedFile(_path, SelectedDelimiter()));
        }
        catch (IOException ex)
        {
            _statusLabel.Text = ex.Message;
            return;
        }
        OnSourceChanged();
    }

    private SheetData CurrentSheet =>
        _isExcel ? (_sheetBox.SelectedIndex >= 0 ? _sheets[_sheetBox.SelectedIndex] : null) : (_sheets.Count > 0 ? _sheets[0] : null);

    /// <summary>Сменился лист или разделитель: признак заголовка и столбец X определяются заново.</summary>
    private void OnSourceChanged()
    {
        if (_updating) return;
        _headerTouched = false;
        UpdatePreview(false);
    }

    private void UpdatePreview(bool keepXColumn)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            Result = null;
            _okButton.Enabled = false;
            _preview.Columns.Clear();

            SheetData sheet = CurrentSheet;
            if (sheet == null) return;

            CellRange? range = null;
            string rangeText = _rangeBox.Text.Trim();
            if (rangeText.Length > 0)
            {
                if (!CellRange.TryParse(rangeText, out CellRange parsed))
                {
                    _rangeBox.BackColor = Color.FromArgb(255, 220, 220);
                    _statusLabel.Text = "Диапазон записан неверно. Пример: A1:C101.";
                    return;
                }
                range = parsed;
            }
            _rangeBox.BackColor = SystemColors.Window;

            List<string[]> cells = sheet.GetRange(range);
            if (cells.Count == 0)
            {
                _statusLabel.Text = "В выбранной области нет данных.";
                return;
            }

            if (!_headerTouched)
            {
                _headerBox.Checked = TableTextParser.IsHeaderRow(cells[0]);
            }
            bool header = _headerBox.Checked;

            int columns = 0;
            foreach (string[] line in cells)
            {
                columns = Math.Max(columns, line.Length);
            }

            int xColumn = keepXColumn ? Math.Max(0, _xColumnBox.SelectedIndex) : 0;
            if (xColumn >= columns) xColumn = 0;
            _xColumnBox.Items.Clear();
            int firstColumn = range?.FirstColumn ?? 0;
            for (int c = 0; c < columns; c++)
            {
                string title = CellRange.ColumnName(firstColumn + c);
                if (header && c < cells[0].Length && cells[0][c].Trim().Length > 0) title += " — " + cells[0][c].Trim();
                _xColumnBox.Items.Add(title);
            }
            _xColumnBox.SelectedIndex = xColumn;

            ImportedTable table = ImportedTable.FromCells(cells, header, xColumn);
            Result = table;
            FillPreview(table);

            int bad = CountInvalid(table);
            _statusLabel.Text = string.Format(CultureInfo.CurrentCulture, "Строк данных: {0}, кривых: {1}{2}",
                table.Rows.Count, table.YNames.Count,
                bad > 0 ? string.Format(CultureInfo.CurrentCulture, ". Нечисловых ячеек: {0} (выделены цветом)", bad) : "");
            _okButton.Enabled = table.Rows.Count > 0;
        }
        finally
        {
            _updating = false;
        }
    }

    private void FillPreview(ImportedTable table)
    {
        _preview.Columns.Add("x", "X: " + table.XName);
        foreach (string name in table.YNames)
        {
            _preview.Columns.Add("y" + _preview.Columns.Count, "Y: " + name);
        }
        foreach (DataGridViewColumn column in _preview.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        int shown = Math.Min(table.Rows.Count, PreviewRows);
        for (int r = 0; r < shown; r++)
        {
            int index = _preview.Rows.Add(table.Rows[r]);
            for (int c = 0; c < table.Rows[r].Length && c < _preview.Columns.Count; c++)
            {
                NumberParseStatus status = NumberParser.Parse(table.Rows[r][c], out _);
                if (status == NumberParseStatus.NotANumber || status == NumberParseStatus.NonFinite)
                {
                    _preview.Rows[index].Cells[c].Style.BackColor = Color.FromArgb(255, 214, 214);
                }
            }
        }
    }

    private static int CountInvalid(ImportedTable table)
    {
        int bad = 0;
        foreach (string[] row in table.Rows)
        {
            foreach (string cell in row)
            {
                NumberParseStatus status = NumberParser.Parse(cell, out _);
                if (status == NumberParseStatus.NotANumber || status == NumberParseStatus.NonFinite) bad++;
            }
        }
        return bad;
    }
}
