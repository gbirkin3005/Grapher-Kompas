using System;
using System.Collections.Generic;
using System.Text;

namespace Grapher.Core.Parsing;

/// <summary>
/// Разбор текстовых таблиц: буфер обмена (Mathcad и Excel отдают текст с табуляцией) и CSV.
/// </summary>
public static class TableTextParser
{
    /// <summary>Разделитель «любое количество пробелов».</summary>
    public const char Whitespace = ' ';

    /// <summary>
    /// Разбивает текст на строки и ячейки. Полностью пустые строки пропускаются.
    /// </summary>
    /// <param name="delimiter">Разделитель столбцов; '\0' — определить автоматически.</param>
    /// <param name="preferCommaDelimiter">
    /// Считать запятую разделителем столбцов, если другого разделителя нет.
    /// Для буфера обмена это неверно (запятая там десятичная), для файлов .csv — обычно верно.
    /// </param>
    public static List<string[]> Parse(string text, char delimiter = '\0', bool preferCommaDelimiter = false)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(text)) return rows;

        List<string> lines = SplitLines(text);
        if (delimiter == '\0')
        {
            delimiter = DetectDelimiter(lines, preferCommaDelimiter);
        }

        foreach (string line in lines)
        {
            string[] cells = SplitLine(line, delimiter);
            bool any = false;
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = cells[i].Trim();
                if (cells[i].Length > 0) any = true;
            }
            if (any) rows.Add(cells);
        }
        return rows;
    }

    public static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Trim().Length > 0) lines.Add(line);
        }
        return lines;
    }

    /// <summary>Определяет разделитель столбцов по первым строкам.</summary>
    public static char DetectDelimiter(IReadOnlyList<string> lines, bool preferCommaDelimiter = false)
    {
        int sample = Math.Min(lines.Count, 30);
        bool hasSemicolon = false, hasComma = false, hasDot = false, hasInnerSpace = false;
        for (int i = 0; i < sample; i++)
        {
            string line = lines[i];
            if (line.IndexOf('\t') >= 0) return '\t';
            if (line.IndexOf(';') >= 0) hasSemicolon = true;
            if (line.IndexOf(',') >= 0) hasComma = true;
            if (line.IndexOf('.') >= 0) hasDot = true;
            if (line.Trim().IndexOf(' ') >= 0) hasInnerSpace = true;
        }

        if (hasSemicolon) return ';';

        if (hasComma && (preferCommaDelimiter || hasDot) && CommaSplitsEvenly(lines, sample))
        {
            return ',';
        }

        if (hasInnerSpace) return Whitespace;
        return '\t';
    }

    /// <summary>Запятая — разделитель столбцов, если даёт одинаковое число столбцов (не менее двух) в каждой строке.</summary>
    private static bool CommaSplitsEvenly(IReadOnlyList<string> lines, int sample)
    {
        int expected = -1;
        for (int i = 0; i < sample; i++)
        {
            int n = SplitLine(lines[i], ',').Length;
            if (n < 2) return false;
            if (expected < 0) expected = n;
            else if (n != expected) return false;
        }
        return expected >= 2;
    }

    /// <summary>Разбивает строку на ячейки. Для разделителей, отличных от пробела, учитываются кавычки CSV.</summary>
    public static string[] SplitLine(string line, char delimiter)
    {
        if (delimiter == Whitespace)
        {
            return line.Split(new[] { ' ', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        if (line.IndexOf('"') < 0)
        {
            return line.Split(delimiter);
        }

        var cells = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                cells.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        cells.Add(sb.ToString());
        return cells.ToArray();
    }

    /// <summary>
    /// Строка похожа на заголовок: в ней есть непустые ячейки и ни одна из них не является числом.
    /// </summary>
    public static bool IsHeaderRow(string[] row)
    {
        if (row == null) return false;
        bool any = false;
        foreach (string cell in row)
        {
            NumberParseStatus status = NumberParser.Parse(cell, out _);
            if (status == NumberParseStatus.Empty) continue;
            if (status != NumberParseStatus.NotANumber) return false;
            any = true;
        }
        return any;
    }
}
