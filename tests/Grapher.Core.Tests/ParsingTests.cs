using System.Collections.Generic;
using System.Linq;
using Grapher.Core.Model;
using Grapher.Core.Parsing;
using Xunit;

namespace Grapher.Core.Tests;

public class NumberParserTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1.5", 1.5)]
    [InlineData("1,5", 1.5)]
    [InlineData("  2,50  ", 2.5)]
    [InlineData("-3,25", -3.25)]
    [InlineData("−3,25", -3.25)]            // типографский минус
    [InlineData("+7", 7)]
    [InlineData(".5", 0.5)]
    [InlineData(",5", 0.5)]
    [InlineData("1e3", 1000)]
    [InlineData("1,5E-3", 0.0015)]
    [InlineData("1.5E+02", 150)]
    [InlineData("1 234,56", 1234.56)]            // пробел между разрядами
    [InlineData("1 234,5", 1234.5)]         // неразрывный пробел (так копирует Excel)
    [InlineData("1.234,56", 1234.56)]            // европейская запись
    [InlineData("1,234.56", 1234.56)]            // американская запись
    [InlineData("1,234,567", 1234567)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("1.5·10^3", 1500)]          // запись Mathcad: 1.5·10^3
    [InlineData("2*10^-2", 0.02)]
    [InlineData("10^3", 1000)]
    public void Parses_numbers_with_point_and_comma(string text, double expected)
    {
        Assert.Equal(NumberParseStatus.Ok, NumberParser.Parse(text, out double value));
        Assert.Equal(expected, value, 12);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12abc")]
    [InlineData("1,2,3")]
    [InlineData("1..2")]
    [InlineData("--1")]
    [InlineData("-")]
    [InlineData("#DIV/0!")]
    [InlineData("1e")]
    [InlineData("e5")]
    public void Rejects_text_that_is_not_a_number(string text)
    {
        Assert.Equal(NumberParseStatus.NotANumber, NumberParser.Parse(text, out double value));
        Assert.True(double.IsNaN(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Reports_empty_cells(string text)
    {
        Assert.Equal(NumberParseStatus.Empty, NumberParser.Parse(text, out _));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("nan")]
    [InlineData("inf")]
    [InlineData("-Infinity")]
    [InlineData("∞")]
    public void Reports_nan_and_infinity(string text)
    {
        Assert.Equal(NumberParseStatus.NonFinite, NumberParser.Parse(text, out _));
    }
}

public class TableTextParserTests
{
    [Fact]
    public void Splits_tab_separated_clipboard_text_with_decimal_commas()
    {
        List<string[]> rows = TableTextParser.Parse("0\t0\r\n1,5\t2,25\r\n3\t9\r\n");

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "1,5", "2,25" }, rows[1]);
    }

    [Fact]
    public void Decimal_comma_is_not_a_column_separator_for_clipboard_text()
    {
        List<string[]> rows = TableTextParser.Parse("1,5\n2,5\n3,5");

        Assert.All(rows, row => Assert.Single(row));
        Assert.Equal("2,5", rows[1][0]);
    }

    [Fact]
    public void Detects_semicolon_separator_of_russian_csv()
    {
        List<string[]> rows = TableTextParser.Parse("x;y\n1,5;2,5\n2;4");

        Assert.Equal(new[] { "x", "y" }, rows[0]);
        Assert.Equal(new[] { "1,5", "2,5" }, rows[1]);
    }

    [Fact]
    public void Detects_comma_separator_when_decimals_use_points()
    {
        List<string[]> rows = TableTextParser.Parse("x,y\n1.5,2.5\n2,4");

        Assert.Equal(new[] { "1.5", "2.5" }, rows[1]);
    }

    [Fact]
    public void Comma_separated_integers_need_the_csv_hint()
    {
        Assert.Single(TableTextParser.Parse("1,2\n3,4")[0]);
        Assert.Equal(2, TableTextParser.Parse("1,2\n3,4", preferCommaDelimiter: true)[0].Length);
    }

    [Fact]
    public void Splits_columns_aligned_with_spaces()
    {
        List<string[]> rows = TableTextParser.Parse("  1.0    2.0\n  3.0   4.0\n");

        Assert.Equal(new[] { "1.0", "2.0" }, rows[0]);
        Assert.Equal(new[] { "3.0", "4.0" }, rows[1]);
    }

    [Fact]
    public void Skips_blank_lines_and_keeps_empty_cells()
    {
        List<string[]> rows = TableTextParser.Parse("1\t2\n\n3\t\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "3", "" }, rows[1]);
    }

    [Fact]
    public void Understands_quoted_csv_cells()
    {
        string[] cells = TableTextParser.SplitLine("\"Сила; Н\";\"он сказал \"\"да\"\"\";5", ';');

        Assert.Equal(new[] { "Сила; Н", "он сказал \"да\"", "5" }, cells);
    }

    [Fact]
    public void Recognises_header_row()
    {
        Assert.True(TableTextParser.IsHeaderRow(new[] { "x, мм", "F, Н" }));
        Assert.False(TableTextParser.IsHeaderRow(new[] { "0", "1,5" }));
        Assert.False(TableTextParser.IsHeaderRow(new[] { "", "" }));
        Assert.False(TableTextParser.IsHeaderRow(new[] { "x", "1" }));
    }
}

public class DataValidatorTests
{
    private static GraphData Data(params string[][] rows) => new GraphData { Rows = rows.ToList() };

    private static List<CurveSettings> Curves(int count) =>
        Enumerable.Range(1, count).Select(i => new CurveSettings { Name = "Y" + i }).ToList();

    [Fact]
    public void Converts_valid_rows_to_points()
    {
        ValidatedData result = DataValidator.Validate(
            Data(new[] { "0", "0" }, new[] { "1,5", "2,25" }, new[] { "3", "9" }), Curves(1));

        Assert.Empty(result.Issues);
        Assert.Equal(3, result.Series[0].Points.Count);
        Assert.Equal(1.5, result.Series[0].Points[1].X);
        Assert.Equal(2.25, result.Series[0].Points[1].Y);
    }

    [Fact]
    public void Empty_table_is_an_error()
    {
        ValidatedData result = DataValidator.Validate(Data(new[] { "", "" }), Curves(1));

        DataIssue issue = Assert.Single(result.Issues);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("пуста", issue.Message);
    }

    [Fact]
    public void Non_numeric_cell_is_reported_with_row_number()
    {
        ValidatedData result = DataValidator.Validate(
            Data(new[] { "0", "0" }, new[] { "1", "abc" }, new[] { "2", "4" }), Curves(1));

        DataIssue issue = Assert.Single(result.Issues);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal(2, issue.Row);
        Assert.Equal(1, issue.Column);
        Assert.Contains("Строка 2", issue.Message);
        Assert.Contains("abc", issue.Message);
        Assert.Equal(2, result.Series[0].Points.Count);
    }

    [Fact]
    public void Nan_and_infinity_are_errors()
    {
        ValidatedData result = DataValidator.Validate(
            Data(new[] { "0", "NaN" }, new[] { "inf", "1" }, new[] { "2", "4" }, new[] { "3", "9" }), Curves(1));

        Assert.Equal(2, result.Issues.Count(i => i.Severity == IssueSeverity.Error));
        Assert.Contains(result.Issues, i => i.Row == 1 && i.Message.Contains("NaN"));
        Assert.Contains(result.Issues, i => i.Row == 2 && i.Column == 0);
        Assert.Equal(2, result.Series[0].Points.Count);
    }

    [Fact]
    public void Empty_y_cell_is_a_warning_and_only_that_curve_loses_the_point()
    {
        ValidatedData result = DataValidator.Validate(
            Data(new[] { "0", "0", "5" }, new[] { "1", "", "6" }, new[] { "2", "4", "7" }), Curves(2));

        DataIssue issue = Assert.Single(result.Issues);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal(2, issue.Row);
        Assert.Contains("пустая ячейка", issue.Message);
        Assert.Equal(2, result.Series[0].Points.Count);
        Assert.Equal(3, result.Series[1].Points.Count);
    }

    [Fact]
    public void Missing_x_skips_the_whole_row()
    {
        ValidatedData result = DataValidator.Validate(
            Data(new[] { "0", "0" }, new[] { "", "5" }, new[] { "2", "4" }), Curves(1));

        Assert.Contains(result.Issues, i => i.Severity == IssueSeverity.Error && i.Row == 2 && i.Column == 0);
        Assert.Equal(2, result.Series[0].Points.Count);
    }

    [Fact]
    public void Blank_rows_are_ignored()
    {
        ValidatedData result = DataValidator.Validate(
            Data(new[] { "0", "0" }, new[] { "", "" }, new[] { "2", "4" }, new string[0]), Curves(1));

        Assert.Empty(result.Issues);
        Assert.Equal(2, result.RowCount);
    }

    [Fact]
    public void Long_lists_of_issues_are_collapsed()
    {
        string[][] rows = Enumerable.Range(0, 200).Select(i => new[] { i.ToString(), "?" }).ToArray();

        ValidatedData result = DataValidator.Validate(Data(rows), Curves(1));

        Assert.True(result.Issues.Count < 30);
        Assert.Contains(result.Issues, i => i.Message.Contains("ещё ошибок"));
    }
}
