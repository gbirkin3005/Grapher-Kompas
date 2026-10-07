using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ExcelDataReader;
using Grapher.Core.Parsing;

namespace Grapher.Core.Import;

/// <summary>Лист книги: имя и ячейки в виде текста.</summary>
public sealed class SheetData
{
    public string Name { get; set; }
    public List<string[]> Rows { get; } = new List<string[]>();

    /// <summary>Наибольшее число столбцов в строках листа.</summary>
    public int ColumnCount
    {
        get
        {
            int max = 0;
            foreach (string[] row in Rows)
            {
                if (row.Length > max) max = row.Length;
            }
            return max;
        }
    }

    /// <summary>Ячейки заданного диапазона; null — весь лист. Пустые строки в начале и в конце отбрасываются.</summary>
    public List<string[]> GetRange(CellRange? range)
    {
        var result = new List<string[]>();
        int firstRow = 0, lastRow = Rows.Count - 1, firstColumn = 0, lastColumn = ColumnCount - 1;
        if (range.HasValue)
        {
            firstRow = range.Value.FirstRow;
            lastRow = Math.Min(lastRow, range.Value.LastRow);
            firstColumn = range.Value.FirstColumn;
            lastColumn = Math.Min(lastColumn, range.Value.LastColumn);
        }
        if (lastColumn < firstColumn) return result;

        for (int r = firstRow; r <= lastRow; r++)
        {
            string[] source = Rows[r];
            var row = new string[lastColumn - firstColumn + 1];
            bool any = false;
            for (int c = firstColumn; c <= lastColumn; c++)
            {
                string value = c < source.Length ? source[c] ?? "" : "";
                row[c - firstColumn] = value;
                if (value.Trim().Length > 0) any = true;
            }
            if (any) result.Add(row);
        }

        if (!range.HasValue)
        {
            TrimEmptyColumns(result);
        }
        return result;
    }

    /// <summary>Убирает полностью пустые столбцы слева (данные на листе могут начинаться не с A).</summary>
    private static void TrimEmptyColumns(List<string[]> rows)
    {
        if (rows.Count == 0) return;
        int width = rows[0].Length;
        int skip = 0;
        while (skip < width)
        {
            bool empty = true;
            foreach (string[] row in rows)
            {
                if (row[skip].Trim().Length > 0)
                {
                    empty = false;
                    break;
                }
            }
            if (!empty) break;
            skip++;
        }
        if (skip == 0 || skip == width) return;

        for (int i = 0; i < rows.Count; i++)
        {
            var trimmed = new string[width - skip];
            Array.Copy(rows[i], skip, trimmed, 0, trimmed.Length);
            rows[i] = trimmed;
        }
    }
}

/// <summary>
/// Чтение таблиц из файлов Excel (.xlsx, .xls) и CSV. Установленный Excel не нужен:
/// книги читает библиотека ExcelDataReader (лицензия MIT).
/// </summary>
public static class SpreadsheetImporter
{
    public const string FileFilter =
        "Таблицы (*.xlsx;*.xls;*.csv;*.txt)|*.xlsx;*.xlsm;*.xls;*.csv;*.txt;*.dat;*.prn|" +
        "Книги Excel (*.xlsx;*.xls)|*.xlsx;*.xlsm;*.xls|" +
        "Текст с разделителями (*.csv;*.txt)|*.csv;*.txt;*.dat;*.prn|" +
        "Все файлы (*.*)|*.*";

    public static bool IsExcelFile(string path)
    {
        string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
        return ext == ".xlsx" || ext == ".xlsm" || ext == ".xls" || ext == ".xlsb";
    }

    /// <summary>Читает все листы книги Excel.</summary>
    public static List<SheetData> ReadWorkbook(string path)
    {
        var sheets = new List<SheetData>();
        try
        {
            // FileShare.ReadWrite: файл можно читать, даже если он открыт в Excel.
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (IExcelDataReader reader = ExcelReaderFactory.CreateReader(stream))
            {
                do
                {
                    var sheet = new SheetData { Name = reader.Name };
                    while (reader.Read())
                    {
                        var row = new string[reader.FieldCount];
                        for (int i = 0; i < row.Length; i++)
                        {
                            row[i] = CellToString(reader.GetValue(i));
                        }
                        sheet.Rows.Add(row);
                    }
                    sheets.Add(sheet);
                }
                while (reader.NextResult());
            }
        }
        catch (IOException ex)
        {
            throw new IOException("Не удалось прочитать файл «" + Path.GetFileName(path) + "»: " + ex.Message, ex);
        }
        catch (Exception ex) when (ex is ExcelDataReader.Exceptions.ExcelReaderException || ex is InvalidDataException || ex is NotSupportedException)
        {
            throw new InvalidDataException(
                "Файл «" + Path.GetFileName(path) + "» не является книгой Excel или повреждён: " + ex.Message, ex);
        }
        return sheets;
    }

    /// <summary>Значение ячейки Excel текстом. Числа пишутся с точкой и без потери точности.</summary>
    private static string CellToString(object value)
    {
        switch (value)
        {
            case null:
                return "";
            case double d:
                return d.ToString("R", CultureInfo.InvariantCulture);
            case float f:
                return ((double)f).ToString("R", CultureInfo.InvariantCulture);
            case decimal m:
                return m.ToString(CultureInfo.InvariantCulture);
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            default:
                return value.ToString();
        }
    }

    /// <summary>Читает текстовый файл с разделителями как один лист.</summary>
    /// <param name="delimiter">Разделитель столбцов; '\0' — определить автоматически.</param>
    public static SheetData ReadDelimitedFile(string path, char delimiter = '\0')
    {
        string text;
        try
        {
            text = ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new IOException("Не удалось прочитать файл «" + Path.GetFileName(path) + "»: " + ex.Message, ex);
        }

        var sheet = new SheetData { Name = Path.GetFileName(path) };
        bool preferComma = string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase);
        sheet.Rows.AddRange(TableTextParser.Parse(text, delimiter, preferComma));
        return sheet;
    }

    /// <summary>
    /// Читает текст в UTF-8 (с BOM или без) либо в UTF-16; если это не Юникод — в кодировке Windows-1251,
    /// в которой русский Excel сохраняет CSV.
    /// </summary>
    public static string ReadAllText(string path)
    {
        byte[] bytes;
        using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            bytes = memory.ToArray();
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }
}
