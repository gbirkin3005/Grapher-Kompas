using System;
using System.Globalization;

namespace Grapher.Core.Import;

/// <summary>Диапазон ячеек листа в записи Excel («A1:C100»). Индексы с нуля, границы включительно.</summary>
public readonly struct CellRange
{
    public readonly int FirstRow;
    public readonly int FirstColumn;
    public readonly int LastRow;
    public readonly int LastColumn;

    public CellRange(int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        FirstRow = Math.Min(firstRow, lastRow);
        LastRow = Math.Max(firstRow, lastRow);
        FirstColumn = Math.Min(firstColumn, lastColumn);
        LastColumn = Math.Max(firstColumn, lastColumn);
    }

    /// <summary>
    /// Разбирает диапазон: «A1:C100», «B2», столбцы целиком «A:C». Строчные буквы и знаки «$» допускаются.
    /// </summary>
    public static bool TryParse(string text, out CellRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string s = text.Trim().Replace("$", "").ToUpperInvariant();
        string[] parts = s.Split(':');
        if (parts.Length < 1 || parts.Length > 2) return false;

        if (!TryParseCell(parts[0], out int r1, out int c1)) return false;
        int r2 = r1, c2 = c1;
        if (parts.Length == 2 && !TryParseCell(parts[1], out r2, out c2)) return false;

        // «A:C» — столбцы целиком: строки не заданы.
        if (r1 < 0 || r2 < 0)
        {
            if (parts.Length == 1) return false;
            r1 = 0;
            r2 = int.MaxValue - 1;
        }

        range = new CellRange(r1, c1, r2, c2);
        return true;
    }

    /// <summary>Разбирает адрес ячейки. Если номера строки нет («A»), row = −1.</summary>
    private static bool TryParseCell(string s, out int row, out int column)
    {
        row = -1;
        column = -1;
        int i = 0;
        int col = 0;
        while (i < s.Length && s[i] >= 'A' && s[i] <= 'Z')
        {
            col = col * 26 + (s[i] - 'A' + 1);
            if (col > 16384) return false;
            i++;
        }
        if (i == 0) return false;
        column = col - 1;

        if (i == s.Length) return true;
        if (!int.TryParse(s.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out int rowNumber) || rowNumber < 1)
        {
            return false;
        }
        row = rowNumber - 1;
        return true;
    }

    /// <summary>Буквенное имя столбца: 0 → «A», 26 → «AA».</summary>
    public static string ColumnName(int column)
    {
        string name = "";
        for (int n = column + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + (n - 1) % 26) + name;
        }
        return name;
    }

    public override string ToString() =>
        ColumnName(FirstColumn) + (FirstRow + 1).ToString(CultureInfo.InvariantCulture) + ":" +
        ColumnName(LastColumn) + (LastRow + 1).ToString(CultureInfo.InvariantCulture);
}
