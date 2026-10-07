using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Grapher.Core.Import;
using Grapher.Core.Model;
using Grapher.Core.Parsing;

namespace Grapher.UI.Controls;

/// <summary>
/// Таблица исходных данных: столбец X и по столбцу Y на каждую кривую.
/// Работает в виртуальном режиме прямо со строками проекта, поэтому десятки тысяч точек не тормозят окно.
/// </summary>
public sealed class DataTableView : DataGridView
{
    /// <summary>Сколько строк показывать в пустой таблице.</summary>
    private const int MinVisibleRows = 30;

    private static readonly Color ErrorColor = Color.FromArgb(255, 214, 214);
    private static readonly Color GapColor = Color.FromArgb(255, 248, 205);

    private GraphProject _project;
    private bool _rebuilding;

    public DataTableView()
    {
        VirtualMode = true;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        AllowUserToOrderColumns = false;
        MultiSelect = true;
        SelectionMode = DataGridViewSelectionMode.RowHeaderSelect;
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        BackgroundColor = SystemColors.Window;
        BorderStyle = BorderStyle.FixedSingle;
        DoubleBuffered = true;
        ContextMenuStrip = BuildContextMenu();
    }

    /// <summary>Изменились значения ячеек.</summary>
    public event EventHandler DataChanged;

    /// <summary>Изменился набор кривых или их названия (после вставки или импорта).</summary>
    public event EventHandler StructureChanged;

    /// <summary>Курсор перешёл в столбец кривой; аргумент — номер кривой с нуля.</summary>
    public event Action<int> CurveColumnSelected;

    private List<string[]> Store => _project.Data.Rows;
    private int ModelColumns => 1 + _project.Curves.Count;

    public void Bind(GraphProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        RebuildColumns();
    }

    /// <summary>Пересоздаёт столбцы по списку кривых проекта.</summary>
    public void RebuildColumns()
    {
        if (_project == null) return;
        _rebuilding = true;
        try
        {
            if (IsCurrentCellInEditMode) CancelEdit();
            RowCount = 0;
            Columns.Clear();
            Columns.Add(CreateColumn(string.IsNullOrWhiteSpace(_project.Data.XName) ? "X" : _project.Data.XName));
            for (int i = 0; i < _project.Curves.Count; i++)
            {
                Columns.Add(CreateColumn(CurveTitle(i)));
            }
        }
        finally
        {
            _rebuilding = false;
        }
        RefreshRows();
    }

    private static DataGridViewColumn CreateColumn(string header) => new DataGridViewTextBoxColumn
    {
        HeaderText = header,
        SortMode = DataGridViewColumnSortMode.NotSortable,
        MinimumWidth = 60,
        FillWeight = 100
    };

    private string CurveTitle(int index)
    {
        string name = _project.Curves[index]?.Name;
        return string.IsNullOrWhiteSpace(name) ? "Y" + (index + 1).ToString(CultureInfo.InvariantCulture) : name;
    }

    /// <summary>Обновляет заголовки столбцов после переименования кривой.</summary>
    public void RefreshHeaders()
    {
        if (_project == null || Columns.Count != ModelColumns)
        {
            RebuildColumns();
            return;
        }
        Columns[0].HeaderText = string.IsNullOrWhiteSpace(_project.Data.XName) ? "X" : _project.Data.XName;
        for (int i = 0; i < _project.Curves.Count; i++)
        {
            Columns[i + 1].HeaderText = CurveTitle(i);
        }
    }

    /// <summary>Приводит число строк таблицы в соответствие с данными (плюс пустые строки для ввода).</summary>
    public void RefreshRows()
    {
        if (_project == null || Columns.Count == 0) return;
        int wanted = Math.Max(Store.Count + 1, MinVisibleRows);
        if (RowCount != wanted) RowCount = wanted;
        Invalidate();
    }

    /// <summary>В таблице нет ни одного значения.</summary>
    public bool IsDataEmpty()
    {
        foreach (string[] row in Store)
        {
            if (row == null) continue;
            foreach (string cell in row)
            {
                if (!string.IsNullOrWhiteSpace(cell)) return false;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------ виртуальный режим

    protected override void OnCellValueNeeded(DataGridViewCellValueEventArgs e)
    {
        base.OnCellValueNeeded(e);
        e.Value = GetCell(e.RowIndex, e.ColumnIndex);
    }

    protected override void OnCellValuePushed(DataGridViewCellValueEventArgs e)
    {
        base.OnCellValuePushed(e);
        string text = (e.Value?.ToString() ?? "").Trim();
        if (text.Length == 0 && e.RowIndex >= Store.Count) return;

        SetCell(e.RowIndex, e.ColumnIndex, text);
        // Число строк меняем после завершения редактирования ячейки.
        BeginInvoke((Action)RefreshRows);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private string GetCell(int row, int column)
    {
        if (_project == null || row < 0 || row >= Store.Count) return "";
        string[] cells = Store[row];
        return cells != null && column < cells.Length ? cells[column] ?? "" : "";
    }

    private void SetCell(int row, int column, string value)
    {
        while (Store.Count <= row)
        {
            Store.Add(new string[ModelColumns]);
        }

        string[] cells = Store[row];
        int width = Math.Max(ModelColumns, column + 1);
        if (cells == null || cells.Length < width)
        {
            var resized = new string[width];
            if (cells != null) Array.Copy(cells, resized, cells.Length);
            Store[row] = cells = resized;
        }
        cells[column] = value;
    }

    // ------------------------------------------------------------------ оформление

    protected override void OnCellFormatting(DataGridViewCellFormattingEventArgs e)
    {
        base.OnCellFormatting(e);
        if (_project == null || e.RowIndex < 0 || e.RowIndex >= Store.Count) return;

        NumberParseStatus status = NumberParser.Parse(GetCell(e.RowIndex, e.ColumnIndex), out _);
        if (status == NumberParseStatus.NotANumber || status == NumberParseStatus.NonFinite)
        {
            e.CellStyle.BackColor = ErrorColor;
        }
        else if (status == NumberParseStatus.Empty && RowHasData(e.RowIndex))
        {
            // Пропуск внутри заполненной строки: точка этой кривой будет пропущена.
            e.CellStyle.BackColor = GapColor;
        }
    }

    private bool RowHasData(int row)
    {
        string[] cells = Store[row];
        if (cells == null) return false;
        foreach (string cell in cells)
        {
            if (!string.IsNullOrWhiteSpace(cell)) return true;
        }
        return false;
    }

    protected override void OnRowPostPaint(DataGridViewRowPostPaintEventArgs e)
    {
        base.OnRowPostPaint(e);
        // Номер строки в заголовке — на него ссылаются сообщения об ошибках в данных.
        var bounds = new Rectangle(e.RowBounds.Left, e.RowBounds.Top, RowHeadersWidth - 4, e.RowBounds.Height);
        TextRenderer.DrawText(e.Graphics, (e.RowIndex + 1).ToString(CultureInfo.CurrentCulture), RowHeadersDefaultCellStyle.Font ?? Font,
            bounds, RowHeadersDefaultCellStyle.ForeColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    protected override void OnCurrentCellChanged(EventArgs e)
    {
        base.OnCurrentCellChanged(e);
        if (_rebuilding || CurrentCell == null) return;
        if (CurrentCell.ColumnIndex >= 1) CurveColumnSelected?.Invoke(CurrentCell.ColumnIndex - 1);
    }

    /// <summary>Переходит к ячейке, на которую указывает сообщение об ошибке.</summary>
    public void GoToCell(int rowNumber, int column)
    {
        int row = rowNumber - 1;
        if (row < 0 || row >= RowCount || Columns.Count == 0) return;
        if (column < 0 || column >= Columns.Count) column = 0;
        ClearSelection();
        CurrentCell = this[column, row];
        Focus();
    }

    // ------------------------------------------------------------------ клавиатура и буфер обмена

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_project != null)
        {
            if (keyData == (Keys.Control | Keys.V) || keyData == (Keys.Shift | Keys.Insert))
            {
                string text = GetClipboardText();
                // Внутри редактируемой ячейки одиночное значение вставляется как обычный текст.
                bool block = text != null && (text.IndexOf('\t') >= 0 || text.TrimEnd('\r', '\n').IndexOf('\n') >= 0);
                if (!IsCurrentCellInEditMode || block)
                {
                    if (IsCurrentCellInEditMode) CancelEdit();
                    PasteText(text, false);
                    return true;
                }
            }
            else if (!IsCurrentCellInEditMode)
            {
                if (keyData == Keys.Delete)
                {
                    DeleteSelection();
                    return true;
                }
                if (keyData == (Keys.Control | Keys.X))
                {
                    CopySelection();
                    DeleteSelection();
                    return true;
                }
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static string GetClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Буфер обмена занят другой программой.
            return null;
        }
    }

    public void PasteFromClipboard(bool replaceAll)
    {
        string text = GetClipboardText();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(FindForm(), "В буфере обмена нет текста. Скопируйте таблицу в Mathcad или Excel и повторите вставку.",
                "Вставка из буфера обмена", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        PasteText(text, replaceAll);
    }

    /// <summary>
    /// Вставляет текстовую таблицу. При <paramref name="replaceAll"/> (или в пустую таблицу)
    /// данные заменяются целиком, иначе блок вставляется начиная с текущей ячейки.
    /// </summary>
    public void PasteText(string text, bool replaceAll)
    {
        if (_project == null || string.IsNullOrWhiteSpace(text)) return;

        List<string[]> cells = TableTextParser.Parse(text);
        if (cells.Count == 0) return;

        int row0 = CurrentCell?.RowIndex ?? 0, column0 = CurrentCell?.ColumnIndex ?? 0;
        if (replaceAll || (IsDataEmpty() && row0 == 0 && column0 == 0))
        {
            LoadTable(ImportedTable.FromCells(cells));
            return;
        }

        if (cells.Count > 1 && TableTextParser.IsHeaderRow(cells[0]))
        {
            cells.RemoveAt(0);
        }

        int width = 0;
        foreach (string[] line in cells)
        {
            width = Math.Max(width, line.Length);
        }

        bool structureChanged = false;
        while (_project.Curves.Count < column0 + width - 1)
        {
            _project.Curves.Add(CurveDefaults.Create(_project.Curves.Count, _project.Curves));
            structureChanged = true;
        }

        for (int i = 0; i < cells.Count; i++)
        {
            for (int j = 0; j < cells[i].Length; j++)
            {
                SetCell(row0 + i, column0 + j, cells[i][j]);
            }
        }

        if (structureChanged)
        {
            RebuildColumns();
            StructureChanged?.Invoke(this, EventArgs.Empty);
        }
        RefreshRows();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Заменяет все данные таблицей из импорта или буфера обмена.</summary>
    public void LoadTable(ImportedTable table)
    {
        if (_project == null || table == null) return;

        table.ApplyTo(_project);
        if (_project.Curves.Count == 0)
        {
            // Один столбец чисел: оставляем столбец Y для ручного ввода.
            _project.Curves.Add(CurveDefaults.Create(0, null));
        }

        RebuildColumns();
        if (RowCount > 0 && Columns.Count > 0) CurrentCell = this[0, 0];
        StructureChanged?.Invoke(this, EventArgs.Empty);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CopySelection()
    {
        try
        {
            DataObject data = GetClipboardContent();
            if (data != null) Clipboard.SetDataObject(data);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
    }

    /// <summary>Удаляет выделенные строки целиком либо очищает выделенные ячейки.</summary>
    public void DeleteSelection()
    {
        if (_project == null) return;

        if (SelectedRows.Count > 0)
        {
            var indices = new List<int>();
            foreach (DataGridViewRow row in SelectedRows)
            {
                if (row.Index < Store.Count) indices.Add(row.Index);
            }
            indices.Sort();
            for (int i = indices.Count - 1; i >= 0; i--)
            {
                Store.RemoveAt(indices[i]);
            }
            ClearSelection();
        }
        else
        {
            foreach (DataGridViewCell cell in SelectedCells)
            {
                if (cell.RowIndex < Store.Count && Store[cell.RowIndex] != null && cell.ColumnIndex < Store[cell.RowIndex].Length)
                {
                    Store[cell.RowIndex][cell.ColumnIndex] = "";
                }
            }
        }

        TrimTrailingEmptyRows();
        RefreshRows();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearAll()
    {
        if (_project == null) return;
        if (IsCurrentCellInEditMode) CancelEdit();
        Store.Clear();
        RefreshRows();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TrimTrailingEmptyRows()
    {
        while (Store.Count > 0 && !RowHasData(Store.Count - 1))
        {
            Store.RemoveAt(Store.Count - 1);
        }
    }

    /// <summary>Добавляет столбец новой кривой.</summary>
    public int AddCurve()
    {
        if (IsCurrentCellInEditMode) EndEdit();
        _project.Curves.Add(CurveDefaults.Create(_project.Curves.Count, _project.Curves));
        RebuildColumns();
        return _project.Curves.Count - 1;
    }

    /// <summary>Удаляет кривую вместе с её столбцом данных.</summary>
    public void RemoveCurve(int index)
    {
        if (index < 0 || index >= _project.Curves.Count) return;
        if (IsCurrentCellInEditMode) CancelEdit();

        _project.Curves.RemoveAt(index);
        int column = index + 1;
        for (int r = 0; r < Store.Count; r++)
        {
            string[] cells = Store[r];
            if (cells == null || cells.Length <= column) continue;
            var shortened = new string[cells.Length - 1];
            Array.Copy(cells, 0, shortened, 0, column);
            Array.Copy(cells, column + 1, shortened, column, cells.Length - column - 1);
            Store[r] = shortened;
        }

        TrimTrailingEmptyRows();
        RebuildColumns();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Вставить", null, (s, e) => PasteFromClipboard(false)) { ShortcutKeyDisplayString = "Ctrl+V" });
        menu.Items.Add(new ToolStripMenuItem("Вставить с заменой всей таблицы", null, (s, e) => PasteFromClipboard(true)));
        menu.Items.Add(new ToolStripMenuItem("Копировать", null, (s, e) => CopySelection()) { ShortcutKeyDisplayString = "Ctrl+C" });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Удалить строки / очистить ячейки", null, (s, e) => DeleteSelection()) { ShortcutKeyDisplayString = "Del" });
        menu.Items.Add(new ToolStripMenuItem("Очистить таблицу", null, (s, e) => ClearAll()));
        return menu;
    }
}
