using System;
using System.Collections.Generic;
using System.Globalization;
using Grapher.Core.Model;
using Grapher.Core.Parsing;

namespace Grapher.Core.Import;

/// <summary>Таблица, подготовленная к загрузке в проект: имена столбцов и строки «X, Y1, Y2…».</summary>
public sealed class ImportedTable
{
    public string XName { get; set; } = "X";
    public List<string> YNames { get; } = new List<string>();
    public List<string[]> Rows { get; } = new List<string[]>();

    public int ColumnCount => 1 + YNames.Count;

    /// <summary>
    /// Собирает таблицу из прямоугольного блока ячеек.
    /// </summary>
    /// <param name="cells">Строки исходных ячеек (могут быть разной длины).</param>
    /// <param name="firstRowIsHeader">Первая строка содержит названия столбцов; null — определить автоматически.</param>
    /// <param name="xColumn">Индекс столбца X в блоке; остальные столбцы становятся кривыми.</param>
    public static ImportedTable FromCells(IReadOnlyList<string[]> cells, bool? firstRowIsHeader = null, int xColumn = 0)
    {
        var table = new ImportedTable();
        if (cells == null || cells.Count == 0) return table;

        int columns = 0;
        foreach (string[] row in cells)
        {
            if (row != null && row.Length > columns) columns = row.Length;
        }
        if (columns == 0) return table;
        if (xColumn < 0 || xColumn >= columns) xColumn = 0;

        bool header = firstRowIsHeader ?? TableTextParser.IsHeaderRow(cells[0]);
        string[] names = header ? cells[0] : null;

        table.XName = HeaderName(names, xColumn, "X");
        int yNumber = 0;
        for (int c = 0; c < columns; c++)
        {
            if (c == xColumn) continue;
            yNumber++;
            string fallback = columns == 2 ? "Y" : "Y" + yNumber.ToString(CultureInfo.InvariantCulture);
            table.YNames.Add(HeaderName(names, c, fallback));
        }

        for (int r = header ? 1 : 0; r < cells.Count; r++)
        {
            string[] source = cells[r] ?? new string[0];
            var row = new string[columns];
            int target = 1;
            for (int c = 0; c < columns; c++)
            {
                string value = c < source.Length ? (source[c] ?? "").Trim() : "";
                if (c == xColumn) row[0] = value;
                else row[target++] = value;
            }
            table.Rows.Add(row);
        }

        return table;
    }

    private static string HeaderName(string[] names, int column, string fallback)
    {
        if (names == null || column >= names.Length) return fallback;
        string name = (names[column] ?? "").Trim();
        return name.Length == 0 ? fallback : name;
    }

    /// <summary>
    /// Заменяет данные проекта этой таблицей. Настройки кривых с теми же номерами сохраняются,
    /// для новых столбцов берётся стиль по умолчанию.
    /// </summary>
    public void ApplyTo(GraphProject project)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));

        project.Data.XName = XName;
        project.Data.Rows = new List<string[]>(Rows);

        var curves = new List<CurveSettings>();
        for (int i = 0; i < YNames.Count; i++)
        {
            CurveSettings curve = i < project.Curves.Count && project.Curves[i] != null
                ? project.Curves[i]
                : CurveDefaults.Create(i, project.Curves);
            curve.Name = YNames[i];
            curves.Add(curve);
        }
        project.Curves = curves;
    }
}

/// <summary>Стили новых кривых: по очереди разные типы линий, чтобы кривые различались на чертеже.</summary>
public static class CurveDefaults
{
    private static readonly LineStyleId[] Cycle =
    {
        LineStyleId.Main, LineStyleId.DashedMain, LineStyleId.AxialMain, LineStyleId.Thin, LineStyleId.Dashed, LineStyleId.Axial
    };

    public static CurveSettings Create(int index, IList<CurveSettings> existing)
    {
        var curve = new CurveSettings
        {
            Name = "Y" + (index + 1).ToString(CultureInfo.InvariantCulture),
            LineStyle = Cycle[index % Cycle.Length]
        };

        // Режим и маркеры наследуем от первой кривой: обычно все кривые графика оформляют одинаково.
        if (existing != null && existing.Count > 0 && existing[0] != null)
        {
            curve.Mode = existing[0].Mode;
            curve.Marker = existing[0].Marker;
            curve.MarkerSizeMm = existing[0].MarkerSizeMm;
        }
        return curve;
    }
}
