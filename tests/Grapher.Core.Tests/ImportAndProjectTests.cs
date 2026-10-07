using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Grapher.Core.Build;
using Grapher.Core.Geometry;
using Grapher.Core.Import;
using Grapher.Core.Model;
using Grapher.Core.Parsing;
using Grapher.Core.Scene;
using Grapher.Core.Serialization;
using Xunit;

namespace Grapher.Core.Tests;

/// <summary>Временная папка для файлов теста; удаляется после теста.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GrapherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
        }
    }
}

public class CellRangeTests
{
    [Theory]
    [InlineData("B2:D10", 1, 1, 9, 3)]
    [InlineData("a1", 0, 0, 0, 0)]
    [InlineData("$A$1:$B$101", 0, 0, 100, 1)]
    [InlineData("D10:B2", 1, 1, 9, 3)]
    [InlineData("AA5:AB6", 4, 26, 5, 27)]
    public void Parses_excel_ranges(string text, int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        Assert.True(CellRange.TryParse(text, out CellRange range));
        Assert.Equal(firstRow, range.FirstRow);
        Assert.Equal(firstColumn, range.FirstColumn);
        Assert.Equal(lastRow, range.LastRow);
        Assert.Equal(lastColumn, range.LastColumn);
    }

    [Fact]
    public void Whole_columns_can_be_selected()
    {
        Assert.True(CellRange.TryParse("A:C", out CellRange range));
        Assert.Equal(0, range.FirstRow);
        Assert.Equal(0, range.FirstColumn);
        Assert.Equal(2, range.LastColumn);
        Assert.True(range.LastRow > 1000000);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1A")]
    [InlineData("A0")]
    [InlineData("A1:B2:C3")]
    [InlineData("A")]
    [InlineData("A1;B2")]
    public void Rejects_invalid_ranges(string text)
    {
        Assert.False(CellRange.TryParse(text, out _));
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void Column_names_follow_excel(int index, string expected)
    {
        Assert.Equal(expected, CellRange.ColumnName(index));
    }
}

public class ImportedTableTests
{
    [Fact]
    public void Header_row_is_detected_and_used_for_names()
    {
        var cells = new List<string[]> { new[] { "t, с", "v, м/с", "a" }, new[] { "0", "1,5", "2" }, new[] { "1", "2,5", "3" } };

        ImportedTable table = ImportedTable.FromCells(cells);

        Assert.Equal("t, с", table.XName);
        Assert.Equal(new[] { "v, м/с", "a" }, table.YNames);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(new[] { "0", "1,5", "2" }, table.Rows[0]);
    }

    [Fact]
    public void Without_header_columns_get_default_names()
    {
        var cells = new List<string[]> { new[] { "0", "1" }, new[] { "1", "2" } };

        ImportedTable table = ImportedTable.FromCells(cells);

        Assert.Equal("X", table.XName);
        Assert.Equal(new[] { "Y" }, table.YNames);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void Any_column_can_be_x()
    {
        var cells = new List<string[]> { new[] { "y1", "x", "y2" }, new[] { "10", "1", "100" } };

        ImportedTable table = ImportedTable.FromCells(cells, true, xColumn: 1);

        Assert.Equal("x", table.XName);
        Assert.Equal(new[] { "y1", "y2" }, table.YNames);
        Assert.Equal(new[] { "1", "10", "100" }, table.Rows[0]);
    }

    [Fact]
    public void Ragged_rows_are_padded()
    {
        var cells = new List<string[]> { new[] { "0", "1", "2" }, new[] { "1" } };

        ImportedTable table = ImportedTable.FromCells(cells, false);

        Assert.Equal(new[] { "1", "", "" }, table.Rows[1]);
    }

    [Fact]
    public void Applying_keeps_settings_of_existing_curves()
    {
        var project = new GraphProject();
        project.Curves.Add(new CurveSettings { Name = "старая", LineStyle = LineStyleId.Dashed, Mode = CurveMode.Polyline });
        var cells = new List<string[]> { new[] { "x", "a", "b" }, new[] { "0", "1", "2" } };

        ImportedTable.FromCells(cells).ApplyTo(project);

        Assert.Equal(2, project.Curves.Count);
        Assert.Equal("a", project.Curves[0].Name);
        Assert.Equal(LineStyleId.Dashed, project.Curves[0].LineStyle);
        Assert.Equal("b", project.Curves[1].Name);
        Assert.Equal(CurveMode.Polyline, project.Curves[1].Mode);     // режим наследуется от первой кривой
        Assert.NotEqual(LineStyleId.Dashed, project.Curves[1].LineStyle);
        Assert.Equal("x", project.Data.XName);
    }
}

/// <summary>
/// Критерий готовности: один и тот же график получается из встроенного примера,
/// из текста буфера обмена и из файлов .xlsx и .csv.
/// </summary>
public class SampleImportTests
{
    private static List<Pt> CurvePoints(GraphProject project)
    {
        BuildResult result = GraphBuilder.Build(project);
        Assert.False(result.HasErrors, string.Join("; ", result.Issues.Select(i => i.Message)));
        BezierPrimitive curve = Assert.Single(result.Scene.Primitives.OfType<BezierPrimitive>());
        Assert.Equal(22, result.Scene.Primitives.OfType<TextPrimitive>().Count(t => t.Role == PrimitiveRole.TickLabel));
        Assert.Equal(18, result.Scene.Primitives.Count(p => p.Role == PrimitiveRole.Grid));
        return curve.Nodes.SelectMany(n => new[] { n.In, n.Point, n.Out }).ToList();
    }

    private static GraphProject SampleWith(ImportedTable table)
    {
        GraphProject project = SampleData.SqrtProject();
        project.Data = new GraphData();
        table.ApplyTo(project);
        return project;
    }

    private static void AssertSameCurve(List<Pt> expected, List<Pt> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Close.Equal(expected[i], actual[i], 1e-6);
        }
    }

    /// <summary>Так данные приходят из Mathcad и русского Excel: табуляция и десятичная запятая.</summary>
    private static string ClipboardText(bool decimalComma, bool header)
    {
        var sb = new StringBuilder();
        if (header) sb.Append("x\ty\r\n");
        foreach (string[] row in SampleData.SqrtRows())
        {
            string x = decimalComma ? row[0].Replace('.', ',') : row[0];
            string y = decimalComma ? row[1].Replace('.', ',') : row[1];
            sb.Append(x).Append('\t').Append(y).Append("\r\n");
        }
        return sb.ToString();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Clipboard_text_gives_the_same_graph(bool decimalComma, bool header)
    {
        List<Pt> expected = CurvePoints(SampleData.SqrtProject());

        List<string[]> cells = TableTextParser.Parse(ClipboardText(decimalComma, header));
        ImportedTable table = ImportedTable.FromCells(cells);

        Assert.Equal(100, table.Rows.Count);
        AssertSameCurve(expected, CurvePoints(SampleWith(table)));
    }

    [Fact]
    public void Xlsx_file_gives_the_same_graph()
    {
        List<Pt> expected = CurvePoints(SampleData.SqrtProject());
        using var folder = new TempFolder();
        string path = folder.File("sqrt.xlsx");
        var rows = new List<string[]> { new[] { "x", "y" } };
        rows.AddRange(SampleData.SqrtRows());
        SimpleXlsxWriter.Write(path, "Данные", rows);

        List<SheetData> sheets = SpreadsheetImporter.ReadWorkbook(path);

        SheetData sheet = Assert.Single(sheets);
        Assert.Equal("Данные", sheet.Name);
        Assert.Equal(101, sheet.Rows.Count);
        ImportedTable table = ImportedTable.FromCells(sheet.GetRange(null), firstRowIsHeader: true);
        Assert.Equal("x", table.XName);
        Assert.Equal(new[] { "y" }, table.YNames);
        AssertSameCurve(expected, CurvePoints(SampleWith(table)));
    }

    [Fact]
    public void Xlsx_range_selects_part_of_the_sheet()
    {
        using var folder = new TempFolder();
        string path = folder.File("range.xlsx");
        SimpleXlsxWriter.Write(path, "Лист1", new List<string[]>
        {
            new[] { "Заголовок отчёта", "", "", "" },
            new[] { "", "", "", "" },
            new[] { "№", "x", "y", "примечание" },
            new[] { "1", "0", "0", "" },
            new[] { "2", "1,5", "2,25", "середина" },
            new[] { "3", "3", "9", "" }
        });

        SheetData sheet = SpreadsheetImporter.ReadWorkbook(path)[0];
        Assert.True(CellRange.TryParse("B3:C6", out CellRange range));
        ImportedTable table = ImportedTable.FromCells(sheet.GetRange(range), firstRowIsHeader: true);

        Assert.Equal("x", table.XName);
        Assert.Equal(new[] { "y" }, table.YNames);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(NumberParseStatus.Ok, NumberParser.Parse(table.Rows[1][0], out double x));
        Assert.Equal(1.5, x);
        Assert.Equal(NumberParseStatus.Ok, NumberParser.Parse(table.Rows[1][1], out double y));
        Assert.Equal(2.25, y);
    }

    [Fact]
    public void Semicolon_csv_in_windows_1251_gives_the_same_graph()
    {
        List<Pt> expected = CurvePoints(SampleData.SqrtProject());
        using var folder = new TempFolder();
        string path = folder.File("sqrt.csv");
        var sb = new StringBuilder("Перемещение, мм;Сила, Н\r\n");
        foreach (string[] row in SampleData.SqrtRows())
        {
            sb.Append(row[0].Replace('.', ',')).Append(';').Append(row[1].Replace('.', ',')).Append("\r\n");
        }
        File.WriteAllBytes(path, Encoding.GetEncoding(1251).GetBytes(sb.ToString()));

        SheetData sheet = SpreadsheetImporter.ReadDelimitedFile(path);
        ImportedTable table = ImportedTable.FromCells(sheet.GetRange(null));

        Assert.Equal("Перемещение, мм", table.XName);
        Assert.Equal(new[] { "Сила, Н" }, table.YNames);
        AssertSameCurve(expected, CurvePoints(SampleWith(table)));
    }

    [Fact]
    public void Comma_csv_in_utf8_is_read_too()
    {
        using var folder = new TempFolder();
        string path = folder.File("data.csv");
        File.WriteAllText(path, "x,y\n0,0\n1.5,2.25\n3,9\n", new UTF8Encoding(true));

        SheetData sheet = SpreadsheetImporter.ReadDelimitedFile(path);
        ImportedTable table = ImportedTable.FromCells(sheet.GetRange(null));

        Assert.Equal(new[] { "1.5", "2.25" }, table.Rows[1]);
    }

    [Fact]
    public void File_that_is_not_a_workbook_gives_a_clear_error()
    {
        using var folder = new TempFolder();
        string path = folder.File("broken.xlsx");
        File.WriteAllText(path, "это не книга Excel");

        var ex = Assert.Throws<InvalidDataException>(() => SpreadsheetImporter.ReadWorkbook(path));

        Assert.Contains("broken.xlsx", ex.Message);
    }
}

public class ProjectSerializerTests
{
    [Fact]
    public void Project_survives_save_and_load()
    {
        GraphProject project = SampleData.SqrtProject();
        project.YAxis.Title = "σ, МПа";
        project.Curves[0].LineStyle = LineStyleId.DashedMain;
        project.Curves[0].Color = "#D02030";
        project.Curves[0].Marker = MarkerShape.Diamond;
        project.Graph.EqualScale = true;
        project.Graph.Crossing = AxesCrossing.AtZero;
        project.Graph.LabelHeightMm = 2.5;
        project.Placement.AngleDeg = 30;
        project.Placement.X = 12.5;
        project.XAxis.AutoLimits = false;
        project.XAxis.Max = 80;

        using var folder = new TempFolder();
        string path = folder.File("graph" + ProjectSerializer.FileExtension);
        ProjectSerializer.Save(project, path);
        GraphProject loaded = ProjectSerializer.Load(path);

        Assert.Equal("σ, МПа", loaded.YAxis.Title);
        Assert.Equal(LineStyleId.DashedMain, loaded.Curves[0].LineStyle);
        Assert.Equal("#D02030", loaded.Curves[0].Color);
        Assert.Equal(MarkerShape.Diamond, loaded.Curves[0].Marker);
        Assert.Equal("√x", loaded.Curves[0].Name);
        Assert.True(loaded.Graph.EqualScale);
        Assert.Equal(AxesCrossing.AtZero, loaded.Graph.Crossing);
        Assert.Equal(GridStepMode.Millimeters, loaded.Graph.GridMode);
        Assert.Equal(2.5, loaded.Graph.LabelHeightMm);
        Assert.Equal(30, loaded.Placement.AngleDeg);
        Assert.Equal(12.5, loaded.Placement.X);
        Assert.False(loaded.XAxis.AutoLimits);
        Assert.Equal(80, loaded.XAxis.Max);
        Assert.Equal(100, loaded.Data.Rows.Count);

        // Данные совпадают численно, а значит и график тот же.
        for (int i = 0; i < 100; i++)
        {
            for (int c = 0; c < 2; c++)
            {
                Assert.True(NumberParser.TryParse(project.Data.Rows[i][c], out double before));
                Assert.True(NumberParser.TryParse(loaded.Data.Rows[i][c], out double after));
                Assert.Equal(before, after);
            }
        }
    }

    [Fact]
    public void Json_stores_numbers_as_numbers_and_enums_as_names()
    {
        var project = new GraphProject();
        project.Data.Rows.Add(new[] { "1,5", "2" });
        project.Data.Rows.Add(new[] { "abc", "" });
        project.Curves.Add(new CurveSettings());

        string json = ProjectSerializer.ToJson(project);

        Assert.Contains("[1.5,2.0]", json);
        Assert.Contains("[\"abc\",null]", json);
        Assert.Contains("\"GridMode\": \"Auto\"", json);
        Assert.Contains("\"LineStyle\": \"Main\"", json);
    }

    [Fact]
    public void Text_and_empty_cells_are_preserved()
    {
        var project = new GraphProject();
        project.Data.Rows.Add(new[] { "abc", "", "7" });

        GraphProject loaded = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project));

        Assert.Equal(new[] { "abc", "", "7" }, loaded.Data.Rows[0]);
    }

    [Fact]
    public void Missing_sections_get_defaults()
    {
        GraphProject loaded = ProjectSerializer.FromJson("{ \"Curves\": [ { \"Name\": null } ], \"XAxis\": null }");

        Assert.NotNull(loaded.Data);
        Assert.NotNull(loaded.XAxis);
        Assert.NotNull(loaded.Graph);
        Assert.Equal("Y1", loaded.Curves[0].Name);
        Assert.Equal("GOST Type AU", loaded.Graph.FontName);
        Assert.Equal(100, loaded.Graph.WidthMm);
    }

    [Theory]
    [InlineData("не json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("")]
    public void Broken_file_gives_a_clear_error(string json)
    {
        Assert.Throws<InvalidDataException>(() => ProjectSerializer.FromJson(json));
    }

    [Fact]
    public void Project_saved_before_colours_existed_still_loads()
    {
        GraphProject loaded = ProjectSerializer.FromJson("{ \"Curves\": [ { \"Name\": \"F\", \"LineStyle\": \"Dashed\" } ] }");

        Assert.Null(loaded.Curves[0].Color);
        Assert.Equal(LineStyleId.Dashed, loaded.Curves[0].LineStyle);
    }

    [Fact]
    public void Settings_only_copy_has_no_data()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Graph.WidthMm = 150;

        GraphProject settings = ProjectSerializer.CloneSettingsOnly(project);

        Assert.Empty(settings.Data.Rows);
        Assert.Empty(settings.Curves);
        Assert.Equal(150, settings.Graph.WidthMm);
        Assert.Equal(100, project.Data.Rows.Count);
    }
}
