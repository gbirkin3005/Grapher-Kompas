using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grapher.Core.Build;
using Grapher.Core.Geometry;
using Grapher.Core.Model;
using Grapher.Core.Parsing;
using Grapher.Core.Scene;
using Xunit;

namespace Grapher.Core.Tests;

public class GraphBuilderTests
{
    private static List<T> Of<T>(BuildResult result, PrimitiveRole role) where T : Primitive =>
        result.Scene.Primitives.OfType<T>().Where(p => p.Role == role).ToList();

    private static GraphProject LineProject(Func<double, double> f, double xMin, double xMax, int count)
    {
        var project = new GraphProject();
        for (int i = 0; i < count; i++)
        {
            double x = xMin + (xMax - xMin) * i / (count - 1);
            project.Data.Rows.Add(new[]
            {
                x.ToString("R", CultureInfo.InvariantCulture),
                f(x).ToString("R", CultureInfo.InvariantCulture)
            });
        }
        project.Curves.Add(new CurveSettings { Name = "Y", Mode = CurveMode.Polyline });
        return project;
    }

    // ---------------------------------------------------------------- контрольный пример

    [Fact]
    public void Sample_has_expected_limits_and_scale()
    {
        BuildResult result = GraphBuilder.Build(SampleData.SqrtProject());

        Assert.False(result.HasErrors);
        Assert.Empty(result.Issues);
        Assert.Equal(0, result.XMin);
        Assert.Equal(100, result.XMax);
        Assert.Equal(0, result.YMin);
        Assert.Equal(10, result.YMax);
        Assert.Equal(100, result.WidthMm);
        Assert.Equal(100, result.HeightMm);
        Close.Equal(1, result.ScaleX);     // 1 мм на единицу X
        Close.Equal(10, result.ScaleY);    // 10 мм на единицу Y
        Assert.Equal(100, result.SourcePointCount);
    }

    [Fact]
    public void Sample_has_ten_by_ten_grid_without_duplicated_lines()
    {
        BuildResult result = GraphBuilder.Build(SampleData.SqrtProject());

        // 11 линий в каждом направлении: две крайние — это рамка и ось, поэтому линий сетки по 9.
        List<LinePrimitive> grid = Of<LinePrimitive>(result, PrimitiveRole.Grid);
        Assert.Equal(18, grid.Count);
        Assert.All(grid, line => Assert.Equal(LineStyleId.Thin, line.Style));
        Assert.Equal(new[] { 10.0, 20, 30, 40, 50, 60, 70, 80, 90 },
            grid.Where(l => l.A.X == l.B.X).Select(l => l.A.X).OrderBy(x => x));
        Assert.Equal(new[] { 10.0, 20, 30, 40, 50, 60, 70, 80, 90 },
            grid.Where(l => l.A.Y == l.B.Y).Select(l => l.A.Y).OrderBy(y => y));

        // Верхняя и правая стороны рамки; нижнюю и левую рисуют оси.
        Assert.Equal(2, Of<LinePrimitive>(result, PrimitiveRole.Frame).Count);
    }

    [Fact]
    public void Sample_has_axes_with_arrows()
    {
        BuildResult result = GraphBuilder.Build(SampleData.SqrtProject());

        List<LinePrimitive> axes = Of<LinePrimitive>(result, PrimitiveRole.Axis);
        Assert.Equal(2, axes.Count);
        Assert.All(axes, axis => Assert.Equal(LineStyleId.Main, axis.Style));
        Assert.All(axes, axis => Assert.Equal(new Pt(0, 0), axis.A));

        List<PolylinePrimitive> arrows = Of<PolylinePrimitive>(result, PrimitiveRole.Arrow);
        Assert.Equal(2, arrows.Count);
        Assert.All(arrows, arrow => Assert.True(arrow.Closed && arrow.Filled));
        Assert.Contains(arrows, a => a.Points[0] == new Pt(110, 0));   // остриё оси X: 100 мм + выступ 10 мм
        Assert.Contains(arrows, a => a.Points[0] == new Pt(0, 110));
    }

    [Fact]
    public void Sample_has_tick_labels_and_axis_titles()
    {
        BuildResult result = GraphBuilder.Build(SampleData.SqrtProject());

        List<TextPrimitive> labels = Of<TextPrimitive>(result, PrimitiveRole.TickLabel);
        string[] xLabels = labels.Where(t => t.Align == TextAlign.Center).OrderBy(t => t.Anchor.X).Select(t => t.Text).ToArray();
        string[] yLabels = labels.Where(t => t.Align == TextAlign.Right).OrderBy(t => t.Anchor.Y).Select(t => t.Text).ToArray();

        Assert.Equal(new[] { "0", "10", "20", "30", "40", "50", "60", "70", "80", "90", "100" }, xLabels);
        Assert.Equal(new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" }, yLabels);
        Assert.All(labels, t => Assert.Equal(3.5, t.HeightMm));
        Assert.All(labels, t => Assert.Equal("GOST Type AU", t.FontName));

        // Числа стоят вне поля графика: под нижней стороной и слева от левой.
        Assert.All(labels.Where(t => t.Align == TextAlign.Center), t => Assert.True(t.Anchor.Y + t.HeightMm < 0));
        Assert.All(labels.Where(t => t.Align == TextAlign.Right), t => Assert.True(t.Anchor.X < 0));

        Assert.Equal(22, Of<LinePrimitive>(result, PrimitiveRole.Tick).Count);

        List<TextPrimitive> titles = Of<TextPrimitive>(result, PrimitiveRole.AxisTitle);
        Assert.Equal(new[] { "x", "y" }, titles.Select(t => t.Text).OrderBy(t => t));
        Assert.All(titles, t => Assert.Equal(5, t.HeightMm));
    }

    [Fact]
    public void Sample_curve_is_one_smooth_spline_through_all_points_inside_the_frame()
    {
        BuildResult result = GraphBuilder.Build(SampleData.SqrtProject());

        BezierPrimitive curve = Assert.Single(Of<BezierPrimitive>(result, PrimitiveRole.Curve));
        Assert.Equal(LineStyleId.Main, curve.Style);
        Assert.Equal(100, curve.Nodes.Count);
        Close.Equal(new Pt(0, 0), curve.Nodes[0].Point);
        Close.Equal(new Pt(100, 100), curve.Nodes[99].Point);

        // x = 25 → y = 5 → на листе (25 мм; 50 мм). Ближайший узел таблицы: x = 2500/99.
        Pt node = curve.Nodes[25].Point;
        Close.Equal(2500.0 / 99, node.X, 1e-5);
        Close.Equal(Math.Sqrt(2500.0 / 99) * 10, node.Y, 1e-4);

        RectD frame = result.Scene.PlotRect;
        foreach (CubicBezier segment in curve.ToSegments())
        {
            for (int i = 0; i <= 10; i++)
            {
                Assert.True(frame.Contains(segment.Eval(i / 10.0), 1e-9));
            }
        }
        Assert.Empty(Of<PolylinePrimitive>(result, PrimitiveRole.Curve));
    }

    [Fact]
    public void Scene_bounds_cover_frame_arrows_and_labels()
    {
        BuildResult result = GraphBuilder.Build(SampleData.SqrtProject());

        RectD bounds = result.Scene.GetBounds();

        Assert.True(bounds.XMin < -3 && bounds.YMin < -4);
        Assert.True(bounds.XMax >= 110 && bounds.YMax >= 110);
    }

    // ---------------------------------------------------------------- режимы кривой

    [Fact]
    public void Polyline_mode_gives_a_polyline_through_the_points()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Curves[0].Mode = CurveMode.Polyline;

        BuildResult result = GraphBuilder.Build(project);

        PolylinePrimitive line = Assert.Single(Of<PolylinePrimitive>(result, PrimitiveRole.Curve));
        Assert.Equal(100, line.Points.Count);
        Assert.False(line.Closed);
        Assert.Empty(Of<BezierPrimitive>(result, PrimitiveRole.Curve));
    }

    [Fact]
    public void Several_curves_keep_their_own_style_and_mode()
    {
        GraphProject project = SampleData.SqrtProject();
        foreach (string[] row in project.Data.Rows.ToList())
        {
            int index = project.Data.Rows.IndexOf(row);
            double x = double.Parse(row[0], CultureInfo.InvariantCulture);
            project.Data.Rows[index] = new[] { row[0], row[1], (x / 20).ToString("R", CultureInfo.InvariantCulture) };
        }
        project.Curves.Add(new CurveSettings { Name = "прямая", LineStyle = LineStyleId.Dashed, Mode = CurveMode.Polyline });
        project.Graph.ShowLegend = true;

        BuildResult result = GraphBuilder.Build(project);

        Assert.Equal(LineStyleId.Main, Assert.Single(Of<BezierPrimitive>(result, PrimitiveRole.Curve)).Style);
        Assert.Equal(LineStyleId.Dashed, Assert.Single(Of<PolylinePrimitive>(result, PrimitiveRole.Curve)).Style);
        Assert.Equal(new[] { "√x", "прямая" }, Of<TextPrimitive>(result, PrimitiveRole.Legend).Select(t => t.Text));
        Assert.Equal(new[] { LineStyleId.Main, LineStyleId.Dashed }, Of<LinePrimitive>(result, PrimitiveRole.Legend).Select(l => l.Style));
    }

    [Fact]
    public void Hidden_curve_is_not_drawn_and_does_not_affect_limits()
    {
        GraphProject project = LineProject(x => x, 0, 10, 11);
        for (int i = 0; i < project.Data.Rows.Count; i++)
        {
            project.Data.Rows[i] = new[] { project.Data.Rows[i][0], project.Data.Rows[i][1], "1000" };
        }
        project.Curves.Add(new CurveSettings { Name = "скрытая", Visible = false });

        BuildResult result = GraphBuilder.Build(project);

        Assert.Equal(10, result.YMax);
        Assert.Single(Of<PolylinePrimitive>(result, PrimitiveRole.Curve));
    }

    // ---------------------------------------------------------------- пределы и обрезка

    [Fact]
    public void Manual_limits_cut_the_smooth_curve_exactly_on_the_frame()
    {
        GraphProject project = SampleData.SqrtProject();
        project.XAxis.AutoLimits = false;
        project.XAxis.Min = 0;
        project.XAxis.Max = 50;

        BuildResult result = GraphBuilder.Build(project);

        Close.Equal(2, result.ScaleX);
        BezierPrimitive curve = Assert.Single(Of<BezierPrimitive>(result, PrimitiveRole.Curve));
        Pt last = curve.Nodes[curve.Nodes.Count - 1].Point;
        Assert.Equal(100, last.X);                                 // ровно правая сторона рамки
        Close.Equal(Math.Sqrt(50) * 10, last.Y, 0.01);             // значение функции на границе, а не в последней точке таблицы
    }

    [Fact]
    public void Curve_leaving_through_the_top_is_cut_on_the_top_side()
    {
        GraphProject project = SampleData.SqrtProject();
        project.YAxis.AutoLimits = false;
        project.YAxis.Min = 0;
        project.YAxis.Max = 5;

        BuildResult result = GraphBuilder.Build(project);

        BezierPrimitive curve = Assert.Single(Of<BezierPrimitive>(result, PrimitiveRole.Curve));
        Pt last = curve.Nodes[curve.Nodes.Count - 1].Point;
        Assert.Equal(100, last.Y);
        Close.Equal(25, last.X, 0.01);       // √x = 5 при x = 25
    }

    [Fact]
    public void Polyline_is_cut_on_the_frame_and_may_split_into_pieces()
    {
        // Синусоида выходит за пределы по Y сверху и снизу.
        GraphProject project = LineProject(x => Math.Sin(x), 0, 4 * Math.PI, 400);
        project.YAxis.AutoLimits = false;
        project.YAxis.Min = -0.5;
        project.YAxis.Max = 0.5;

        BuildResult result = GraphBuilder.Build(project);

        List<PolylinePrimitive> pieces = Of<PolylinePrimitive>(result, PrimitiveRole.Curve);
        Assert.Equal(5, pieces.Count);
        RectD frame = result.Scene.PlotRect;
        Assert.All(pieces.SelectMany(p => p.Points), p => Assert.True(frame.Contains(p, 1e-9)));

        // Внутренние концы кусков лежат точно на верхней или нижней стороне рамки.
        var innerEnds = pieces.Skip(1).Select(p => p.Points[0]).Concat(pieces.Take(4).Select(p => p.Points.Last()));
        Assert.All(innerEnds, p => Assert.True(p.Y == 0 || p.Y == frame.YMax, $"Конец куска {p} не на границе"));
    }

    [Fact]
    public void Limits_may_start_away_from_zero()
    {
        GraphProject project = LineProject(x => 200 + x, 300, 400, 11);

        BuildResult result = GraphBuilder.Build(project);

        Assert.Equal(300, result.XMin);
        Assert.Equal(400, result.XMax);
        Assert.Equal(500, result.YMin);
        Assert.Equal(600, result.YMax);
        string firstX = Of<TextPrimitive>(result, PrimitiveRole.TickLabel)
            .Where(t => t.Align == TextAlign.Center).OrderBy(t => t.Anchor.X).First().Text;
        Assert.Equal("300", firstX);
    }

    [Fact]
    public void Invalid_manual_limits_are_reported()
    {
        GraphProject project = SampleData.SqrtProject();
        project.XAxis.AutoLimits = false;
        project.XAxis.Min = 10;
        project.XAxis.Max = 10;

        BuildResult result = GraphBuilder.Build(project);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Message.Contains("Пределы оси X"));
        Assert.NotNull(result.Scene);
        Assert.Equal(100, result.XMax);
    }

    [Fact]
    public void Empty_table_still_gives_axes_for_the_preview()
    {
        BuildResult result = GraphBuilder.Build(new GraphProject());

        Assert.True(result.HasErrors);
        Assert.NotNull(result.Scene);
        Assert.NotEmpty(Of<LinePrimitive>(result, PrimitiveRole.Axis));
        Assert.DoesNotContain(result.Scene.Primitives, p => p.Role == PrimitiveRole.Curve);
    }

    [Fact]
    public void Angle_data_from_zero_to_360_keeps_its_limits()
    {
        // Типичные данные по углу поворота кривошипа: 361 точка, φ от 0 до 360.
        GraphProject project = LineProject(x => 0.25 * Math.Sin(x * Math.PI / 180), 0, 360, 361);

        BuildResult result = GraphBuilder.Build(project);

        Assert.Equal(0, result.XMin);
        Assert.Equal(360, result.XMax);
        Assert.Equal(30, result.TicksX.Step, 12);
        string[] labels = Of<TextPrimitive>(result, PrimitiveRole.TickLabel)
            .Where(t => t.Align == TextAlign.Center).OrderBy(t => t.Anchor.X).Select(t => t.Text).ToArray();
        Assert.Equal("0", labels.First());
        Assert.Equal("360", labels.Last());
    }

    // ---------------------------------------------------------------- шрифт

    [Fact]
    public void Greek_letters_switch_old_font_to_its_full_version()
    {
        // В «GOST type A» нет греческих букв: на чертеже «φ» превратилась бы в квадратик.
        GraphProject project = SampleData.SqrtProject();
        project.Graph.FontName = "GOST type A";
        project.XAxis.Title = "φ, °";
        project.YAxis.Title = "Сила F, Н";
        project.Curves[0].Name = "a_x(φ)";
        project.Graph.ShowLegend = true;

        BuildResult result = GraphBuilder.Build(project);

        List<TextPrimitive> titles = Of<TextPrimitive>(result, PrimitiveRole.AxisTitle);
        Assert.Equal("GOST Type AU", titles.Single(t => t.Text == "φ, °").FontName);
        Assert.Equal("GOST type A", titles.Single(t => t.Text == "Сила F, Н").FontName);
        Assert.Equal("GOST Type AU", Assert.Single(Of<TextPrimitive>(result, PrimitiveRole.Legend)).FontName);
        Assert.All(Of<TextPrimitive>(result, PrimitiveRole.TickLabel), t => Assert.Equal("GOST type A", t.FontName));
    }

    [Theory]
    [InlineData("GOST type A", "x, мм", "GOST type A")]
    [InlineData("GOST type A", "σ, МПа", "GOST Type AU")]
    [InlineData("GOST type A", "Δl", "GOST Type AU")]
    [InlineData("GOST type B", "ω, рад/с", "GOST Type BU")]
    [InlineData("GOST type A", "t, °C ±5 №1", "GOST type A")]
    [InlineData("GOST Type AU", "φ", "GOST Type AU")]
    [InlineData("Arial", "φ", "Arial")]                      // посторонние шрифты не подменяются
    [InlineData("", "x", "GOST Type AU")]
    [InlineData(null, "x", "GOST Type AU")]
    public void Font_is_chosen_for_each_text(string font, string text, string expected)
    {
        Assert.Equal(expected, DrawingFonts.ForText(font, text));
    }

    // ---------------------------------------------------------------- цвет кривой

    [Fact]
    public void Curve_colour_goes_to_curve_markers_and_legend_only()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Curves[0].Color = "#D02030";
        project.Curves[0].Marker = MarkerShape.Circle;
        project.Curves[0].MarkerEvery = 10;
        project.Graph.ShowLegend = true;

        BuildResult result = GraphBuilder.Build(project);

        const int expected = 0xD02030;
        Assert.Equal(expected, Assert.Single(Of<BezierPrimitive>(result, PrimitiveRole.Curve)).Color);
        Assert.All(Of<CirclePrimitive>(result, PrimitiveRole.Marker), m => Assert.Equal(expected, m.Color));
        Assert.Equal(expected, Assert.Single(Of<LinePrimitive>(result, PrimitiveRole.Legend)).Color);

        // Сетка, рамка, оси и стрелки остаются в цветах стилей КОМПАС.
        Assert.All(result.Scene.Primitives.Where(p => p.Role == PrimitiveRole.Grid || p.Role == PrimitiveRole.Frame ||
                                                       p.Role == PrimitiveRole.Axis || p.Role == PrimitiveRole.Arrow ||
                                                       p.Role == PrimitiveRole.Tick),
            p => Assert.Null(p.Color));

        // Цвет сохраняется при размещении на листе.
        GraphScene placed = result.Scene.Transform(new Transform2D(10, 20, 30));
        Assert.Equal(expected, placed.Primitives.OfType<BezierPrimitive>().Single().Color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("красный")]
    [InlineData("#12345")]
    public void Missing_or_invalid_colour_means_style_colour(string color)
    {
        GraphProject project = SampleData.SqrtProject();
        project.Curves[0].Color = color;

        BuildResult result = GraphBuilder.Build(project);

        Assert.Null(Assert.Single(Of<BezierPrimitive>(result, PrimitiveRole.Curve)).Color);
    }

    [Fact]
    public void Polyline_curve_is_coloured_too()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Curves[0].Mode = CurveMode.Polyline;
        project.Curves[0].Color = "#0080FF";

        BuildResult result = GraphBuilder.Build(project);

        Assert.Equal(0x0080FF, Assert.Single(Of<PolylinePrimitive>(result, PrimitiveRole.Curve)).Color);
    }

    // ---------------------------------------------------------------- масштаб, оси, оформление

    [Fact]
    public void Equal_scale_derives_height_from_width()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Graph.EqualScale = true;

        BuildResult result = GraphBuilder.Build(project);

        Assert.Equal(100, result.WidthMm);
        Close.Equal(10, result.HeightMm);
        Close.Equal(result.ScaleX, result.ScaleY);
    }

    [Fact]
    public void Axes_cross_at_zero_when_zero_is_inside_the_limits()
    {
        GraphProject project = LineProject(x => x, -50, 50, 11);
        project.Graph.Crossing = AxesCrossing.AtZero;

        BuildResult result = GraphBuilder.Build(project);

        List<LinePrimitive> axes = Of<LinePrimitive>(result, PrimitiveRole.Axis);
        Assert.Contains(axes, a => a.A == new Pt(0, 50) && a.B.Y == 50);     // ось X на уровне y = 0
        Assert.Contains(axes, a => a.A == new Pt(50, 0) && a.B.X == 50);     // ось Y на уровне x = 0
        Assert.Equal(4, Of<LinePrimitive>(result, PrimitiveRole.Frame).Count);
        Assert.DoesNotContain(Of<LinePrimitive>(result, PrimitiveRole.Grid), l => l.A.X == 50 && l.B.X == 50);
    }

    [Fact]
    public void Axes_stay_in_the_corner_when_zero_is_outside_the_limits()
    {
        GraphProject project = LineProject(x => x, 10, 60, 6);
        project.Graph.Crossing = AxesCrossing.AtZero;

        BuildResult result = GraphBuilder.Build(project);

        Assert.All(Of<LinePrimitive>(result, PrimitiveRole.Axis), a => Assert.Equal(new Pt(0, 0), a.A));
    }

    [Fact]
    public void Grid_frame_axes_and_labels_can_be_switched_off()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Graph.ShowGrid = false;
        project.Graph.ShowFrame = false;
        project.Graph.ShowAxes = false;
        project.Graph.ShowTickLabels = false;
        project.Graph.TickLengthMm = 0;
        project.XAxis.Title = "";
        project.YAxis.Title = "";

        BuildResult result = GraphBuilder.Build(project);

        Assert.Single(result.Scene.Primitives);
        Assert.IsType<BezierPrimitive>(result.Scene.Primitives[0]);
    }

    [Fact]
    public void Tick_labels_use_configured_decimals_and_separator()
    {
        GraphProject project = LineProject(x => x, 0, 1, 11);
        project.Graph.GridMode = GridStepMode.Units;
        project.XAxis.GridStep = 0.25;
        project.YAxis.GridStep = 0.5;
        project.YAxis.Decimals = 2;

        string[] Labels(BuildResult r, TextAlign align) => Of<TextPrimitive>(r, PrimitiveRole.TickLabel)
            .Where(t => t.Align == align).OrderBy(t => t.Anchor.X + t.Anchor.Y).Select(t => t.Text).ToArray();

        BuildResult comma = GraphBuilder.Build(project);
        Assert.Equal(new[] { "0,00", "0,25", "0,50", "0,75", "1,00" }, Labels(comma, TextAlign.Center));
        Assert.Equal(new[] { "0,00", "0,50", "1,00" }, Labels(comma, TextAlign.Right));

        project.Graph.DecimalComma = false;
        BuildResult point = GraphBuilder.Build(project);
        Assert.Equal(new[] { "0.00", "0.25", "0.50", "0.75", "1.00" }, Labels(point, TextAlign.Center));
    }

    [Fact]
    public void Dense_grid_labels_are_thinned_so_numbers_do_not_overlap()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Graph.GridMode = GridStepMode.Millimeters;
        project.XAxis.GridStep = 2;     // линия сетки каждые 2 мм — числа через 2 мм не поместятся
        project.YAxis.GridStep = 2;

        BuildResult result = GraphBuilder.Build(project);

        List<TextPrimitive> xLabels = Of<TextPrimitive>(result, PrimitiveRole.TickLabel)
            .Where(t => t.Align == TextAlign.Center).OrderBy(t => t.Anchor.X).ToList();
        Assert.True(xLabels.Count < 20);
        for (int i = 0; i + 1 < xLabels.Count; i++)
        {
            double gap = xLabels[i + 1].Anchor.X - xLabels[i].Anchor.X;
            double needed = (TextMetrics.EstimateWidth(xLabels[i].Text, 3.5) + TextMetrics.EstimateWidth(xLabels[i + 1].Text, 3.5)) / 2;
            Assert.True(gap >= needed, $"Подписи «{xLabels[i].Text}» и «{xLabels[i + 1].Text}» перекрываются");
        }
        Assert.Equal(49 + 49, Of<LinePrimitive>(result, PrimitiveRole.Grid).Count);
    }

    [Fact]
    public void Centered_titles_are_placed_outside_tick_labels()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Graph.TitlePlacement = AxisTitlePlacement.Centered;
        project.XAxis.Title = "x, мм";
        project.YAxis.Title = "σ, МПа";

        BuildResult result = GraphBuilder.Build(project);

        List<TextPrimitive> titles = Of<TextPrimitive>(result, PrimitiveRole.AxisTitle);
        TextPrimitive x = titles.Single(t => t.Text == "x, мм");
        TextPrimitive y = titles.Single(t => t.Text == "σ, МПа");
        Assert.Equal(50, x.Anchor.X);
        Assert.Equal(0, x.AngleDeg);
        Assert.True(x.Anchor.Y + x.HeightMm < -(1.5 + 1 + 3.5));          // ниже числовых подписей
        Assert.Equal(90, y.AngleDeg);
        Assert.Equal(50, y.Anchor.Y);
        double labelsLeft = Of<TextPrimitive>(result, PrimitiveRole.TickLabel)
            .Where(t => t.Align == TextAlign.Right)
            .Min(t => t.Anchor.X - TextMetrics.EstimateWidth(t.Text, t.HeightMm));
        Assert.True(y.Anchor.X < labelsLeft);                             // левее числовых подписей
    }

    [Fact]
    public void Markers_are_drawn_only_for_points_inside_the_frame()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Curves[0].Marker = MarkerShape.Circle;
        project.Curves[0].MarkerSizeMm = 2;
        project.XAxis.AutoLimits = false;
        project.XAxis.Min = 0;
        project.XAxis.Max = 50;

        BuildResult result = GraphBuilder.Build(project);

        List<CirclePrimitive> markers = Of<CirclePrimitive>(result, PrimitiveRole.Marker);
        Assert.Equal(50, markers.Count);      // точки с x ≤ 50: i = 0…49
        Assert.All(markers, m => Assert.Equal(1, m.Radius));
    }

    // ---------------------------------------------------------------- прореживание

    [Fact]
    public void Thousands_of_points_are_decimated_without_visible_change()
    {
        GraphProject project = LineProject(x => Math.Sin(x), 0, 4 * Math.PI, 20000);

        BuildResult result = GraphBuilder.Build(project);

        Assert.True(result.Decimated);
        PolylinePrimitive line = Assert.Single(Of<PolylinePrimitive>(result, PrimitiveRole.Curve));
        Assert.True(line.Points.Count < 1500, $"Вершин: {line.Points.Count}");

        // Каждая исходная точка лежит не дальше допуска от прореженной линии.
        double tolerance = project.Graph.DecimateToleranceMm;
        double kx = result.ScaleX, ky = result.ScaleY;
        int segment = 0;
        for (int i = 0; i < 20000; i += 7)
        {
            double x = 4 * Math.PI * i / 19999;
            var p = new Pt((x - result.XMin) * kx, (Math.Sin(x) - result.YMin) * ky);
            while (segment + 2 < line.Points.Count && line.Points[segment + 1].X < p.X) segment++;
            Assert.True(Pt.DistanceToSegment(p, line.Points[segment], line.Points[segment + 1]) <= tolerance + 1e-9);
        }
    }

    [Fact]
    public void Decimation_can_be_switched_off()
    {
        GraphProject project = LineProject(x => Math.Sin(x), 0, 4 * Math.PI, 3000);
        project.Graph.Decimate = false;

        BuildResult result = GraphBuilder.Build(project);

        Assert.False(result.Decimated);
        Assert.Equal(3000, Assert.Single(Of<PolylinePrimitive>(result, PrimitiveRole.Curve)).Points.Count);
    }

    [Fact]
    public void Small_data_sets_are_never_decimated()
    {
        BuildResult result = GraphBuilder.Build(LineProject(x => 2 * x, 0, 10, 101));

        Assert.False(result.Decimated);
        Assert.Equal(101, Assert.Single(Of<PolylinePrimitive>(result, PrimitiveRole.Curve)).Points.Count);
    }

    // ---------------------------------------------------------------- размещение

    [Fact]
    public void Placement_rotates_and_moves_the_whole_scene()
    {
        BuildResult result = GraphBuilder.Build(SampleData.SqrtProject());

        GraphScene placed = result.Scene.Transform(new Transform2D(10, 20, 90));

        // Правый нижний угол поля (100; 0) после поворота на 90° и переноса оказывается в (10; 120).
        Close.Equal(new Pt(10, 20), placed.PlotCorners[0]);
        Close.Equal(new Pt(10, 120), placed.PlotCorners[1]);
        Close.Equal(new Pt(-90, 120), placed.PlotCorners[2]);

        TextPrimitive label = placed.Primitives.OfType<TextPrimitive>().First(t => t.Role == PrimitiveRole.TickLabel);
        Assert.Equal(90, label.AngleDeg);
        Assert.Equal(3.5, label.HeightMm);

        BezierPrimitive curve = placed.Primitives.OfType<BezierPrimitive>().Single();
        Close.Equal(new Pt(10, 20), curve.Nodes[0].Point);
        Close.Equal(new Pt(-90, 120), curve.Nodes[99].Point);
        Assert.Equal(result.Scene.Primitives.Count, placed.Primitives.Count);
    }

    [Fact]
    public void View_scale_changes_geometry_but_not_text_height()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Curves[0].Marker = MarkerShape.Circle;
        BuildResult result = GraphBuilder.Build(project);

        // Вид в масштабе 1:2: чтобы на бумаге получилось 100 мм, в координатах вида нужно 200 мм.
        GraphScene placed = result.Scene.Transform(new Transform2D(0, 0, 0, 2));

        Close.Equal(new Pt(200, 200), placed.PlotCorners[2]);
        Assert.All(placed.Primitives.OfType<TextPrimitive>().Where(t => t.Role == PrimitiveRole.TickLabel),
            t => Assert.Equal(3.5, t.HeightMm));
        Assert.All(placed.Primitives.OfType<CirclePrimitive>(), c => Assert.Equal(2, c.Radius));
    }
}
