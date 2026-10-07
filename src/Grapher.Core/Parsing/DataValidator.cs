using System;
using System.Collections.Generic;
using System.Globalization;
using Grapher.Core.Geometry;
using Grapher.Core.Model;

namespace Grapher.Core.Parsing;

public enum IssueSeverity
{
    Warning,
    Error
}

/// <summary>Замечание к данным или настройкам.</summary>
public sealed class DataIssue
{
    public DataIssue(IssueSeverity severity, string message, int row = 0, int column = -1)
    {
        Severity = severity;
        Message = message;
        Row = row;
        Column = column;
    }

    public IssueSeverity Severity { get; }
    public string Message { get; }

    /// <summary>Номер строки таблицы с единицы; 0 — замечание не привязано к строке.</summary>
    public int Row { get; }

    /// <summary>Индекс столбца: 0 — X, 1 и далее — Y; −1 — не привязано к столбцу.</summary>
    public int Column { get; }

    public override string ToString() => Message;
}

/// <summary>Точки одной кривой после проверки.</summary>
public sealed class SeriesPoints
{
    public int CurveIndex { get; set; }
    public List<Pt> Points { get; } = new List<Pt>();

    /// <summary>Номера строк таблицы (с единицы), из которых взяты точки.</summary>
    public List<int> Rows { get; } = new List<int>();
}

public sealed class ValidatedData
{
    public List<SeriesPoints> Series { get; } = new List<SeriesPoints>();
    public List<DataIssue> Issues { get; } = new List<DataIssue>();

    /// <summary>Число непустых строк таблицы.</summary>
    public int RowCount { get; set; }
}

/// <summary>Проверка таблицы данных и перевод её в числовые точки.</summary>
public static class DataValidator
{
    /// <summary>Сколько замечаний одного вида показывать, прежде чем свернуть остальные.</summary>
    private const int MaxIssuesPerKind = 20;

    public static ValidatedData Validate(GraphData data, IList<CurveSettings> curves)
    {
        var result = new ValidatedData();
        int curveCount = curves?.Count ?? 0;
        for (int c = 0; c < curveCount; c++)
        {
            result.Series.Add(new SeriesPoints { CurveIndex = c });
        }

        int errors = 0, warnings = 0, hiddenErrors = 0, hiddenWarnings = 0;
        void Add(IssueSeverity severity, string message, int row, int column)
        {
            if (severity == IssueSeverity.Error)
            {
                if (errors++ < MaxIssuesPerKind) result.Issues.Add(new DataIssue(severity, message, row, column));
                else hiddenErrors++;
            }
            else
            {
                if (warnings++ < MaxIssuesPerKind) result.Issues.Add(new DataIssue(severity, message, row, column));
                else hiddenWarnings++;
            }
        }

        List<string[]> rows = data?.Rows ?? new List<string[]>();
        string xName = string.IsNullOrWhiteSpace(data?.XName) ? "X" : data.XName;

        for (int r = 0; r < rows.Count; r++)
        {
            string[] row = rows[r] ?? new string[0];
            int rowNumber = r + 1;
            if (IsEmptyRow(row)) continue;
            result.RowCount++;

            string xText = row.Length > 0 ? row[0] : null;
            NumberParseStatus xStatus = NumberParser.Parse(xText, out double x);
            if (xStatus != NumberParseStatus.Ok)
            {
                Add(IssueSeverity.Error, DescribeCell(rowNumber, xName, xText, xStatus) + ", строка пропущена", rowNumber, 0);
                continue;
            }

            for (int c = 0; c < curveCount; c++)
            {
                string yText = row.Length > c + 1 ? row[c + 1] : null;
                NumberParseStatus yStatus = NumberParser.Parse(yText, out double y);
                if (yStatus == NumberParseStatus.Ok)
                {
                    result.Series[c].Points.Add(new Pt(x, y));
                    result.Series[c].Rows.Add(rowNumber);
                    continue;
                }

                string name = CurveName(curves, c);
                if (yStatus == NumberParseStatus.Empty)
                {
                    // Пустая ячейка в столбце Y — не ошибка: у этой кривой просто нет точки при данном X.
                    Add(IssueSeverity.Warning, DescribeCell(rowNumber, name, yText, yStatus) + ", точка пропущена", rowNumber, c + 1);
                }
                else
                {
                    Add(IssueSeverity.Error, DescribeCell(rowNumber, name, yText, yStatus) + ", точка пропущена", rowNumber, c + 1);
                }
            }
        }

        if (hiddenErrors > 0)
        {
            result.Issues.Add(new DataIssue(IssueSeverity.Error,
                string.Format(CultureInfo.CurrentCulture, "…и ещё ошибок в данных: {0}", hiddenErrors)));
        }
        if (hiddenWarnings > 0)
        {
            result.Issues.Add(new DataIssue(IssueSeverity.Warning,
                string.Format(CultureInfo.CurrentCulture, "…и ещё предупреждений: {0}", hiddenWarnings)));
        }

        if (result.RowCount == 0)
        {
            result.Issues.Add(new DataIssue(IssueSeverity.Error, "Таблица данных пуста. Введите или вставьте точки графика."));
        }
        else if (curveCount == 0)
        {
            result.Issues.Add(new DataIssue(IssueSeverity.Error, "В таблице нет ни одного столбца Y."));
        }
        else
        {
            for (int c = 0; c < curveCount; c++)
            {
                if (curves[c] != null && curves[c].Visible && result.Series[c].Points.Count < 2)
                {
                    result.Issues.Add(new DataIssue(IssueSeverity.Warning,
                        string.Format(CultureInfo.CurrentCulture,
                            "Кривая «{0}»: меньше двух корректных точек, линия не будет построена.", CurveName(curves, c)),
                        0, c + 1));
                }
            }
        }

        return result;
    }

    private static string CurveName(IList<CurveSettings> curves, int index)
    {
        string name = curves[index]?.Name;
        return string.IsNullOrWhiteSpace(name) ? "Y" + (index + 1).ToString(CultureInfo.InvariantCulture) : name;
    }

    private static string DescribeCell(int row, string column, string text, NumberParseStatus status)
    {
        switch (status)
        {
            case NumberParseStatus.Empty:
                return string.Format(CultureInfo.CurrentCulture, "Строка {0}, столбец «{1}»: пустая ячейка", row, column);
            case NumberParseStatus.NonFinite:
                return string.Format(CultureInfo.CurrentCulture,
                    "Строка {0}, столбец «{1}»: значение «{2}» не является конечным числом (NaN или бесконечность)",
                    row, column, Shorten(text));
            default:
                return string.Format(CultureInfo.CurrentCulture,
                    "Строка {0}, столбец «{1}»: «{2}» не является числом", row, column, Shorten(text));
        }
    }

    private static string Shorten(string text)
    {
        text = (text ?? "").Trim();
        return text.Length <= 24 ? text : text.Substring(0, 24) + "…";
    }

    private static bool IsEmptyRow(string[] row)
    {
        foreach (string cell in row)
        {
            if (!string.IsNullOrWhiteSpace(cell)) return false;
        }
        return true;
    }
}
