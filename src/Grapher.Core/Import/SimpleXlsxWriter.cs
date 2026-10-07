using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using Grapher.Core.Parsing;

namespace Grapher.Core.Import;

/// <summary>
/// Запись простой книги .xlsx с одним листом. Числовые ячейки сохраняются числами, остальные — текстом.
/// Используется для примеров и тестов импорта; Excel для этого не нужен.
/// </summary>
public static class SimpleXlsxWriter
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void Write(string path, string sheetName, IEnumerable<string[]> rows)
    {
        using (FileStream stream = File.Create(path))
        {
            Write(stream, sheetName, rows);
        }
    }

    public static void Write(Stream stream, string sheetName, IEnumerable<string[]> rows)
    {
        var strings = new List<string>();
        var stringIndex = new Dictionary<string, int>();
        var sheet = new StringBuilder();
        sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sheet.Append("<worksheet xmlns=\"").Append(MainNs).Append("\"><sheetData>");

        int rowNumber = 0;
        foreach (string[] row in rows)
        {
            rowNumber++;
            sheet.Append("<row r=\"").Append(rowNumber.ToString(CultureInfo.InvariantCulture)).Append("\">");
            for (int c = 0; row != null && c < row.Length; c++)
            {
                string cell = row[c];
                if (string.IsNullOrEmpty(cell)) continue;

                string reference = CellRange.ColumnName(c) + rowNumber.ToString(CultureInfo.InvariantCulture);
                if (NumberParser.TryParse(cell, out double number))
                {
                    sheet.Append("<c r=\"").Append(reference).Append("\"><v>")
                        .Append(number.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                }
                else
                {
                    if (!stringIndex.TryGetValue(cell, out int index))
                    {
                        index = strings.Count;
                        strings.Add(cell);
                        stringIndex[cell] = index;
                    }
                    sheet.Append("<c r=\"").Append(reference).Append("\" t=\"s\"><v>")
                        .Append(index.ToString(CultureInfo.InvariantCulture)).Append("</v></c>");
                }
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");

        var shared = new StringBuilder();
        shared.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        shared.Append("<sst xmlns=\"").Append(MainNs).Append("\" count=\"").Append(strings.Count)
            .Append("\" uniqueCount=\"").Append(strings.Count).Append("\">");
        foreach (string s in strings)
        {
            shared.Append("<si><t xml:space=\"preserve\">").Append(SecurityElement.Escape(s)).Append("</t></si>");
        }
        shared.Append("</sst>");

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            AddEntry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "</Types>");

            AddEntry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"" + PackageRelNs + "\">" +
                "<Relationship Id=\"rId1\" Type=\"" + RelNs + "/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");

            AddEntry(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"" + MainNs + "\" xmlns:r=\"" + RelNs + "\">" +
                "<sheets><sheet name=\"" + SecurityElement.Escape(sheetName ?? "Лист1") + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                "</workbook>");

            AddEntry(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"" + PackageRelNs + "\">" +
                "<Relationship Id=\"rId1\" Type=\"" + RelNs + "/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"" + RelNs + "/sharedStrings\" Target=\"sharedStrings.xml\"/>" +
                "<Relationship Id=\"rId3\" Type=\"" + RelNs + "/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>");

            AddEntry(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"" + MainNs + "\">" +
                "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>" +
                "</styleSheet>");

            AddEntry(zip, "xl/sharedStrings.xml", shared.ToString());
            AddEntry(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
        {
            writer.Write(content);
        }
    }
}
