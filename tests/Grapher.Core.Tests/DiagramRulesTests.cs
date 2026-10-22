using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grapher.Core.Build;
using Grapher.Core.Geometry;
using Grapher.Core.Model;
using Grapher.Core.Numerics;
using Grapher.Core.Parsing;
using Grapher.Core.Scene;
using Grapher.Core.Serialization;
using Xunit;

namespace Grapher.Core.Tests;

public class TextMarkupTests
{
    private static string Describe(string text) => string.Join("|", TextMarkup.Parse(text).Select(r => r.ToString()));

    [Theory]
    [InlineData("V_qS2X(φ)", "V|_{qS2X}|(φ)")]       // индекс продолжается, пока идут буквы и цифры
    [InlineData("a_x(φ)", "a|_{x}|(φ)")]
    [InlineData("м^2", "м|^{2}")]
    [InlineData("с^-1", "с|^{-1}")]                   // у верхнего индекса допускается знак
    [InlineData("M_{с пр}", "M|_{с пр}")]             // несколько слов — в фигурных скобках
    [InlineData("M_c^пр, Н·м", "M|_{c}|^{пр}|, Н·м")]
    [InlineData("σ, МПа", "σ, МПа")]
    [InlineData("a\\_b", "a_b")]                      // обратная косая черта отменяет разметку
    [InlineData("165 \\frac{мм}{м}", "165 |\\frac{мм}|{м}")]   // дробь
    [InlineData("μ_{Vq} = \\frac{π}{6}", "μ|_{Vq}| = |\\frac{π}|{6}")]
    [InlineData("x_", "x_")]                          // знак без индекса остаётся знаком
    [InlineData("_ ^", "_ ^")]
    public void Splits_text_into_runs(string text, string expected)
    {
        Assert.Equal(expected, Describe(text));
    }

    [Fact]
    public void Plain_text_drops_markup_but_keeps_characters()
    {
        Assert.Equal("VqS2X(φ)", TextMarkup.Plain("V_qS2X(φ)"));
        Assert.Equal("M с пр", TextMarkup.Plain("M_{ с пр}"));
        Assert.Equal("a_b", TextMarkup.Plain("a\\_b"));
        Assert.True(TextMarkup.HasIndices("V_q"));
        Assert.False(TextMarkup.HasIndices("σ, МПа"));
        Assert.Equal("a_b", TextMarkup.Plain(TextMarkup.Escape("a_b")));

        Assert.Equal("\\frac{π}{6}", TextMarkup.Fraction("π", "6"));
        Assert.Equal("π/6", TextMarkup.Plain("\\frac{π}{6}"));
        Assert.True(TextMarkup.HasFractions("165 \\frac{мм}{м}"));
        Assert.False(TextMarkup.HasIndices("165 \\frac{мм}{м}"));
        Assert.True(TextMarkup.HasStructure("V_q"));
        Assert.False(TextMarkup.HasStructure("σ, МПа"));
    }

    [Fact]
    public void Index_and_fraction_sizes_follow_kompas_text()
    {
        // Индекс средней высоты — 2/3 высоты текста, дробь нормальной высоты — в размер текста.
        Assert.Equal(2.0 / 3.0, TextMarkup.Scale(TextRunKind.Subscript), 12);
        Assert.Equal(1.0, TextMarkup.Scale(TextRunKind.Numerator), 12);

        // Положение строк измерено в КОМПАС: верхний индекс поднят на 0,78 высоты текста,
        // нижний опущен на 0,38; числитель дроби поднят на 0,89, знаменатель опущен на 0,76.
        Assert.Equal(0.778, TextMarkup.BaselineRise(TextRunKind.Superscript), 3);
        Assert.Equal(-0.379, TextMarkup.BaselineRise(TextRunKind.Subscript), 3);
        Assert.Equal(0.891, TextMarkup.BaselineRise(TextRunKind.Numerator), 3);
        Assert.Equal(-0.758, TextMarkup.BaselineRise(TextRunKind.Denominator), 3);

        // Строка с дробью выше и ниже обычной.
        Assert.True(TextMetrics.Above("\\frac{π}{6}", 5) > TextMetrics.Above("π", 5) + 4);
        Assert.True(TextMetrics.Below("\\frac{π}{6}", 5) > TextMetrics.Below("π", 5) + 3);
        Assert.True(TextMetrics.Below("V_q", 5) > TextMetrics.Below("V", 5));
    }

    [Fact]
    public void Indices_are_narrower_than_normal_text()
    {
        double plain = TextMetrics.EstimateWidth("Vq", 5);
        double indexed = TextMetrics.EstimateWidth("V_q", 5);
        Assert.True(indexed < plain);
        Assert.True(indexed > TextMetrics.EstimateWidth("V", 5));

        // Верхний и нижний индексы у одного основания стоят друг над другом и занимают место один раз.
        Close.Equal(TextMetrics.EstimateWidth("M_c", 5), TextMetrics.EstimateWidth("M_c^п", 5), 0.5);
    }
}

public class AngleAndScaleNumbersTests
{
    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(Math.PI / 6, 1, 6)]
    [InlineData(2 * Math.PI / 3, 2, 3)]
    [InlineData(Math.PI, 1, 1)]
    [InlineData(11 * Math.PI / 6, 11, 6)]
    [InlineData(2 * Math.PI, 2, 1)]
    [InlineData(-Math.PI / 4, -1, 4)]
    public void Angles_are_written_as_fractions_of_pi(double value, int numerator, int denominator)
    {
        Assert.True(NumberFormatter.TryPiFraction(value, out int n, out int d));
        Assert.Equal(numerator, n);
        Assert.Equal(denominator, d);
    }

    [Fact]
    public void Arbitrary_angle_is_not_a_fraction_of_pi()
    {
        Assert.False(NumberFormatter.TryPiFraction(1.0, out _, out _));
        Assert.Equal("π", NumberFormatter.PiNumerator(1));
        Assert.Equal("5π", NumberFormatter.PiNumerator(5));
        Assert.Equal("−π", NumberFormatter.PiNumerator(-1));
        Assert.Equal("0", NumberFormatter.PiNumerator(0));
    }

    [Fact]
    public void Full_turn_in_radians_gets_step_of_pi_over_six()
    {
        // Данные из Mathcad записаны с округлением: 6,283 вместо 2π.
        NiceScale.NiceLimitsPi(0, 6.283, 16, out double min, out double max, out double step);

        Assert.Equal(0, min);
        Close.Equal(2 * Math.PI, max, 1e-12);
        Close.Equal(Math.PI / 6, step, 1e-12);

        AxisTicks ticks = TickGenerator.Generate(GridStepMode.Auto, 0, min, max, 157, "X", NumberStyle.PiFractions);
        Assert.Equal(13, ticks.Values.Count);
        Assert.All(ticks.Values, v => Assert.True(NumberFormatter.TryPiFraction(v, out _, out _)));
    }

    [Theory]
    [InlineData(165.0, "165")]
    [InlineData(25.0, "25")]
    [InlineData(0.012345, "0,0123")]
    [InlineData(1234.5, "1230")]
    [InlineData(2.5, "2,5")]
    public void Scale_factor_is_written_with_three_significant_digits(double value, string expected)
    {
        Assert.Equal(expected, NumberFormatter.Significant(value, 3, true));
    }

    [Fact]
    public void Second_scale_limits_put_its_divisions_on_the_grid_lines()
    {
        // Основная шкала −0,2…0,2 с четырьмя интервалами, ноль на средней линии.
        Assert.True(NiceScale.AlignedLimits(-0.17, 0.17, 4, 2, out double min, out double max, out double step));
        Close.Equal(-0.2, min);
        Close.Equal(0.2, max);
        Close.Equal(0.1, step);

        // Обе шкалы от нуля: ноль остаётся внизу.
        Assert.True(NiceScale.AlignedLimits(3, 47, 5, 0, out min, out max, out step));
        Assert.Equal(0, min);
        Assert.Equal(50, max);
        Assert.Equal(10, step);

        // Данные далеко от нуля: ради совмещения нулей шкалу не растягивают.
        Assert.True(NiceScale.AlignedLimits(1000, 1010, 4, 0, out min, out max, out step));
        Assert.True(min <= 1000 && max >= 1010);
        Assert.True(max - min <= 20);
        Close.Equal((max - min) / 4, step);
    }
}

public class CartesianRulesTests
{
    private static List<T> Of<T>(BuildResult result, PrimitiveRole role) where T : Primitive =>
        result.Scene.Primitives.OfType<T>().Where(p => p.Role == role).ToList();

    private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    [Fact]
    public void Two_scale_sample_follows_the_reference_sheet()
    {
        BuildResult result = GraphBuilder.Build(SampleData.TmmProject());

        Assert.False(result.HasErrors);
        Assert.Equal(2, result.YScales.Count);
        Close.Equal(165, result.YScales[0].MmPerUnit, 1e-9);       // 165 мм на метр
        Close.Equal(25, result.ScaleX, 1e-9);                      // 25 мм на радиан

        // Деления правой шкалы стоят на линиях сетки левой.
        Close.Equal(0.1, result.YScales[1].Step);
        double width = result.WidthMm;
        string[] right = Of<TextPrimitive>(result, PrimitiveRole.TickLabel)
            .Where(t => t.Align == TextAlign.Left && t.Anchor.X > width).OrderBy(t => t.Anchor.Y).Select(t => t.Text).ToArray();
        Assert.Equal(new[] { "-0,2", "-0,1", "0", "0,1", "0,2" }, right);

        // Оси не рисуются — шкалами служат стороны рамки, все они основной линией.
        Assert.Empty(Of<LinePrimitive>(result, PrimitiveRole.Axis));
        List<LinePrimitive> frame = Of<LinePrimitive>(result, PrimitiveRole.Frame);
        Assert.Equal(4, frame.Count);
        Assert.All(frame, line => Assert.Equal(LineStyleId.Main, line.Style));
    }

    [Fact]
    public void Units_stand_between_the_last_two_numbers_of_each_scale()
    {
        BuildResult result = GraphBuilder.Build(SampleData.TmmProject());

        List<TextPrimitive> units = Of<TextPrimitive>(result, PrimitiveRole.Unit);
        Assert.Equal(new[] { "–", "м", "рад" }, units.Select(u => u.Text).OrderBy(t => t).ToArray());

        // «м» — посередине между 0,1 и 0,2 на левой шкале.
        TextPrimitive metre = units.Single(u => u.Text == "м");
        Close.Equal((0.15 + 0.25) * 165, metre.Anchor.Y + metre.HeightMm / 2, 1e-6);
        Assert.True(metre.Anchor.X < 0);

        // Для «рад» места между числами нет: она стоит вместо предпоследнего числа (11π/6).
        TextPrimitive rad = units.Single(u => u.Text == "рад");
        Close.Equal(11 * Math.PI / 6 * 25, rad.Anchor.X, 1e-6);
        Assert.DoesNotContain(Of<TextPrimitive>(result, PrimitiveRole.TickLabel), t => t.Text == "11π");
        Assert.Contains(Of<TextPrimitive>(result, PrimitiveRole.TickLabel), t => t.Text == "2π");
    }

    [Fact]
    public void Angle_labels_in_fractions_of_pi_are_fractions_inside_the_text()
    {
        BuildResult result = GraphBuilder.Build(SampleData.TmmProject());
        List<TextPrimitive> labels = Of<TextPrimitive>(result, PrimitiveRole.TickLabel);

        // π/6 — одна надпись с дробью (штатная дробь текста КОМПАС), по центру деления.
        double x = Math.PI / 6 * 25;
        TextPrimitive sixth = labels.Single(t => Math.Abs(t.Anchor.X - x) < 1e-6 && t.Align == TextAlign.Center);
        Assert.Equal("\\frac{π}{6}", sixth.Text);
        Assert.Equal("π/6", TextMarkup.Plain(sixth.Text));

        // Обычные числа стоят на той же базовой линии: черта дроби проходит на половине их высоты.
        TextPrimitive pi = labels.Single(t => t.Text == "π");
        Assert.Equal(sixth.Anchor.Y, pi.Anchor.Y);
        // Дробь не залезает на рамку: числитель целиком ниже нижней стороны поля.
        Assert.True(sixth.Anchor.Y + TextMetrics.Above(sixth.Text, sixth.HeightMm) <= 1e-9);
        // Отдельных линий-чёрточек у подписей нет.
        Assert.Empty(Of<LinePrimitive>(result, PrimitiveRole.TickLabel));
    }

    [Fact]
    public void Symbols_at_mid_scale_get_separate_arrows()
    {
        BuildResult result = GraphBuilder.Build(SampleData.TmmProject());
        List<TextPrimitive> titles = Of<TextPrimitive>(result, PrimitiveRole.AxisTitle);

        Assert.Equal(new[] { "V_q", "φ", "ω_q" }, titles.Select(t => t.Text).OrderBy(t => t, StringComparer.Ordinal).ToArray());
        Assert.All(titles, t => Assert.Equal(0, t.AngleDeg));
        Assert.True(titles.Single(t => t.Text == "V_q").Anchor.X < 0);
        Assert.True(titles.Single(t => t.Text == "ω_q").Anchor.X > result.WidthMm);

        // Три самостоятельные стрелки — линии-выноски КОМПАС: по одной на шкалу.
        Assert.Equal(3, Of<LeaderArrowPrimitive>(result, PrimitiveRole.AxisTitle).Count);
        Assert.Empty(Of<PolylinePrimitive>(result, PrimitiveRole.Arrow));

        // Те же стрелки отрезком и залитым треугольником.
        GraphProject drawn = SampleData.TmmProject();
        drawn.Graph.NativeArrows = false;
        BuildResult own = GraphBuilder.Build(drawn);
        Assert.Equal(3, Of<LinePrimitive>(own, PrimitiveRole.AxisTitle).Count);
        Assert.Equal(3, Of<PolylinePrimitive>(own, PrimitiveRole.Arrow).Count);
    }

    [Fact]
    public void Scale_factors_are_written_for_every_scale()
    {
        BuildResult result = GraphBuilder.Build(SampleData.TmmProject());

        // Каждая надпись — один текст: «μ» с индексом, значение и дробь «мм / единица».
        string[] notes = Of<TextPrimitive>(result, PrimitiveRole.Note).Select(t => t.Text).ToArray();
        Assert.Equal(3, notes.Length);
        Assert.Contains("μ_{Vq} = 165 \\frac{мм}{м}", notes);
        Assert.Contains("μ_{ωq} = 165 \\frac{мм}{–}", notes);
        Assert.Contains("μ_{φ} = 25 \\frac{мм}{рад}", notes);
        Assert.Empty(Of<LinePrimitive>(result, PrimitiveRole.Note));
    }

    [Fact]
    public void Characteristic_points_are_ends_extremes_and_zero_crossings()
    {
        GraphProject project = SampleData.TmmProject();
        // Оставляем одну кривую: V = −0,21·sin φ.
        foreach (CurveSettings curve in project.Curves) curve.Visible = curve.Name == "V_qS2Y";

        BuildResult result = GraphBuilder.Build(project);

        List<CirclePrimitive> dots = Of<CirclePrimitive>(result, PrimitiveRole.Marker);
        Assert.All(dots, d => Assert.True(d.Filled));
        double[] xs = dots.Select(d => d.Center.X / 25 / Math.PI).OrderBy(v => v).ToArray();
        // Начало, минимум при π/2, ноль при π, максимум при 3π/2, конец.
        Assert.Equal(5, xs.Length);
        Close.Equal(0, xs[0], 1e-3);
        Close.Equal(0.5, xs[1], 1e-3);
        Close.Equal(1, xs[2], 1e-3);
        Close.Equal(1.5, xs[3], 1e-3);
        Close.Equal(2, xs[4], 1e-3);
    }

    [Fact]
    public void Curve_labels_stand_on_leader_shelves_and_break_the_grid()
    {
        GraphProject project = SampleData.TmmProject();
        BuildResult result = GraphBuilder.Build(project);

        List<TextPrimitive> labels = Of<TextPrimitive>(result, PrimitiveRole.CurveLabel);
        Assert.Equal(new[] { "V_qBY", "V_qS2X", "V_qS2Y", "ω_q2" }, labels.Select(t => t.Text).OrderBy(t => t, StringComparer.Ordinal).ToArray());
        Assert.Equal(8, Of<LinePrimitive>(result, PrimitiveRole.Leader).Count);     // выноска и полка у каждой подписи

        // Надписи на поле диаграммы; линии сетки под ними прерваны (Р 50-77-88, п. 6.2).
        RectD field = result.Scene.PlotRect;
        List<LinePrimitive> grid = Of<LinePrimitive>(result, PrimitiveRole.Grid);
        foreach (TextPrimitive label in labels)
        {
            RectD box = TextMetrics.EstimateBox(label.Text, label.HeightMm, label.Anchor, label.Align);
            Assert.True(field.Contains(new Pt(box.XMin, box.YMin)) && field.Contains(new Pt(box.XMax, box.YMax)));
            Assert.DoesNotContain(grid, line => Clipper.ClipSegment(box, line.A, line.B, out double t0, out double t1) && t1 - t0 > 1e-9);
        }

        project.Graph.BreakGridUnderLabels = false;
        Assert.True(Of<LinePrimitive>(GraphBuilder.Build(project), PrimitiveRole.Grid).Count < grid.Count);
    }

    [Fact]
    public void Function_break_is_a_dashed_line_between_separately_smoothed_parts()
    {
        var project = new GraphProject();
        // Сила сопротивления: 2 кН до x = 0,2, затем скачком 0,5 кН.
        double[][] rows = { new[] { 0.0, 2 }, new[] { 0.1, 2 }, new[] { 0.2, 2 }, new[] { 0.2, 0.5 }, new[] { 0.3, 0.5 }, new[] { 0.4, 0.5 } };
        foreach (double[] row in rows) project.Data.Rows.Add(new[] { N(row[0]), N(row[1]) });
        project.Curves.Add(new CurveSettings { Name = "F", Mode = CurveMode.Smooth });

        BuildResult result = GraphBuilder.Build(project);

        LinePrimitive jump = Assert.Single(Of<LinePrimitive>(result, PrimitiveRole.Jump));
        Assert.Equal(LineStyleId.Dashed, jump.Style);
        Close.Equal(jump.A.X, jump.B.X);
        Assert.Equal(2, Of<BezierPrimitive>(result, PrimitiveRole.Curve).Count);

        // Каждый участок остаётся горизонтальным: сглаживание не «переливается» через разрыв.
        foreach (BezierPrimitive part in Of<BezierPrimitive>(result, PrimitiveRole.Curve))
        {
            Assert.All(part.Nodes, node => Close.Equal(part.Nodes[0].Point.Y, node.Out.Y, 1e-9));
        }

        // Без этого правила точки соединяются одной кривой, как в Mathcad.
        project.Graph.DashedJumps = false;
        BuildResult joined = GraphBuilder.Build(project);
        Assert.Empty(Of<LinePrimitive>(joined, PrimitiveRole.Jump));
    }

    [Fact]
    public void Periodic_curve_has_no_kink_where_cycles_meet()
    {
        var project = new GraphProject();
        const int count = 13;
        for (int i = 0; i < count; i++)
        {
            double phi = 2 * Math.PI * i / (count - 1);
            project.Data.Rows.Add(new[] { N(phi), N(Math.Sin(phi + 1)) });
        }
        project.Curves.Add(new CurveSettings { Name = "y", Mode = CurveMode.Smooth });

        double Slope(Pt from, Pt to) => (to.Y - from.Y) / (to.X - from.X);

        BezierPrimitive periodic = Assert.Single(Of<BezierPrimitive>(GraphBuilder.Build(project), PrimitiveRole.Curve));
        BezierNode first = periodic.Nodes[0], last = periodic.Nodes[periodic.Nodes.Count - 1];
        Close.Equal(Slope(first.Point, first.Out), Slope(last.In, last.Point), 1e-9);

        project.Graph.SmoothPeriodic = false;
        BezierPrimitive open = Assert.Single(Of<BezierPrimitive>(GraphBuilder.Build(project), PrimitiveRole.Curve));
        first = open.Nodes[0];
        last = open.Nodes[open.Nodes.Count - 1];
        Assert.True(Math.Abs(Slope(first.Point, first.Out) - Slope(last.In, last.Point)) > 1e-3);
    }

    [Fact]
    public void Unit_can_follow_the_symbol_or_form_a_fraction_with_it()
    {
        GraphProject project = SampleData.SqrtProject();
        project.XAxis.Title = "l";
        project.XAxis.Unit = "мм";
        project.YAxis.Title = "σ";
        project.YAxis.Unit = "МПа";

        project.Graph.Units = UnitPlacement.AfterTitle;
        BuildResult after = GraphBuilder.Build(project);
        Assert.Equal(new[] { "l, мм", "σ, МПа" }, Of<TextPrimitive>(after, PrimitiveRole.AxisTitle).Select(t => t.Text).OrderBy(t => t).ToArray());
        Assert.Empty(Of<TextPrimitive>(after, PrimitiveRole.Unit));

        project.Graph.Units = UnitPlacement.Fraction;
        BuildResult fraction = GraphBuilder.Build(project);
        List<TextPrimitive> parts = Of<TextPrimitive>(fraction, PrimitiveRole.AxisTitle);
        Assert.Equal(new[] { "\\frac{l}{мм}", "\\frac{σ}{МПа}" }, parts.Select(t => t.Text).OrderBy(t => t, StringComparer.Ordinal).ToArray());
        // Дробь σ/МПа — в конце шкалы, над полем: и знаменатель выше верхней стороны рамки.
        TextPrimitive sigma = parts.Single(t => t.Text == "\\frac{σ}{МПа}");
        Assert.True(sigma.Anchor.Y - TextMetrics.Below(sigma.Text, sigma.HeightMm) > fraction.HeightMm);
        // Дробь l/мм — в конце шкалы X, правее поля.
        Assert.True(parts.Single(t => t.Text == "\\frac{l}{мм}").Anchor.X > fraction.WidthMm);
    }

    [Fact]
    public void Degree_sign_is_written_once_at_the_last_number()
    {
        var project = new GraphProject();
        for (int angle = 0; angle <= 360; angle += 30)
        {
            project.Data.Rows.Add(new[] { N(angle), N(Math.Sin(angle * Math.PI / 180)) });
        }
        project.Curves.Add(new CurveSettings { Name = "y" });
        project.XAxis.Unit = "°";

        BuildResult result = GraphBuilder.Build(project);

        string[] labels = Of<TextPrimitive>(result, PrimitiveRole.TickLabel)
            .Where(t => t.Align == TextAlign.Center).OrderBy(t => t.Anchor.X).Select(t => t.Text).ToArray();
        Assert.Equal("360°", labels[labels.Length - 1]);
        Assert.Single(labels, t => t.Contains("°"));
        Assert.Empty(Of<TextPrimitive>(result, PrimitiveRole.Unit));
    }

    [Fact]
    public void Extra_scales_on_the_left_are_parallel_lines_with_ticks()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Data.Rows = project.Data.Rows.Select(r => new[] { r[0], r[1], N(double.Parse(r[1], CultureInfo.InvariantCulture) * 30) }).ToList();
        project.Curves.Add(new CurveSettings { Name = "HB", Axis = 1 });
        project.ExtraAxes.Add(new AxisSettings { Title = "HB", Side = AxisSide.Left });

        BuildResult result = GraphBuilder.Build(project);

        // Шкала вынесена левее чисел основной шкалы; у неё своя линия основной толщины.
        ScaleResult extra = result.YScales[1];
        Assert.Equal(0, extra.Min);
        Assert.Equal(300, extra.Max);
        LinePrimitive axis = Of<LinePrimitive>(result, PrimitiveRole.Axis)
            .Single(l => l.A.X < -1 && Math.Abs(l.A.X - l.B.X) < 1e-9);
        Assert.Equal(LineStyleId.Main, axis.Style);
        double mainLabelsLeft = Of<TextPrimitive>(result, PrimitiveRole.TickLabel)
            .Where(t => t.Align == TextAlign.Right && t.Anchor.X > axis.A.X)
            .Min(t => t.Anchor.X - TextMetrics.EstimateWidth(t.Text, t.HeightMm));
        Assert.True(axis.A.X < mainLabelsLeft);

        // Кривая второй шкалы построена в её масштабе: 300 единиц занимают всю высоту поля.
        BezierPrimitive curve = Of<BezierPrimitive>(result, PrimitiveRole.Curve)[1];
        Close.Equal(100, curve.Nodes[curve.Nodes.Count - 1].Point.Y, 1e-6);
    }
}

public class PolarAndTernaryTests
{
    private static List<T> Of<T>(BuildResult result, PrimitiveRole role) where T : Primitive =>
        result.Scene.Primitives.OfType<T>().Where(p => p.Role == role).ToList();

    [Fact]
    public void Polar_sample_has_circular_grid_and_angle_scale()
    {
        BuildResult result = GraphBuilder.Build(SampleData.PolarProject());

        Assert.False(result.HasErrors);
        Assert.Equal(DiagramKind.Polar, result.Kind);
        Assert.Equal(new Pt(0, 0), result.Scene.InsertionPoint);           // точка вставки — полюс

        // Внешняя окружность — основной линией, радиус равен половине диаметра.
        CirclePrimitive outer = Assert.Single(Of<CirclePrimitive>(result, PrimitiveRole.Frame));
        Assert.Equal(60, outer.Radius);
        Assert.Equal(LineStyleId.Main, outer.Style);

        // Шесть внутренних окружностей (20…120) и двенадцать лучей через 30°.
        int circles = Of<CirclePrimitive>(result, PrimitiveRole.Grid).Count
                      + Of<ArcPrimitive>(result, PrimitiveRole.Grid).Select(a => Math.Round(a.Radius, 6)).Distinct().Count();
        Assert.Equal(6, circles);
        Assert.True(Of<LinePrimitive>(result, PrimitiveRole.Grid).Count >= 12);

        // Нулевой луч направлен вниз, углы растут против часовой стрелки: 90° — справа.
        List<TextPrimitive> labels = Of<TextPrimitive>(result, PrimitiveRole.TickLabel);
        TextPrimitive zero = labels.Single(t => t.Text == "0°" && t.Anchor.Length > 60);
        Assert.True(zero.Anchor.Y < -60 && Math.Abs(zero.Anchor.X) < 1e-6);
        TextPrimitive ninety = labels.Single(t => t.Text == "90°");
        Assert.True(ninety.Anchor.X > 60);

        // Знак градуса — у каждого числа; единица радиуса — вместо предпоследнего числа.
        Assert.Equal(12, labels.Count(t => t.Text.EndsWith("°")));
        Assert.Equal("cd", Assert.Single(Of<TextPrimitive>(result, PrimitiveRole.Unit)).Text);

        // По Р 50-77-88 (п. 5.2) знак градуса можно поставить один раз — у последнего числа.
        GraphProject once = SampleData.PolarProject();
        once.Polar.DegreeSignOnAll = false;
        List<TextPrimitive> onceLabels = Of<TextPrimitive>(GraphBuilder.Build(once), PrimitiveRole.TickLabel);
        Assert.Single(onceLabels, t => t.Text.EndsWith("°"));
        Assert.Contains(onceLabels, t => t.Text == "330°");
        Assert.DoesNotContain(labels, t => t.Text == "120" && t.Anchor.Length < 60);
        Assert.Contains(labels, t => t.Text == "140");
    }

    [Fact]
    public void Polar_curve_point_is_at_its_angle_and_radius()
    {
        GraphProject project = SampleData.PolarProject();
        project.Data.Rows = new List<string[]> { new[] { "0", "70" }, new[] { "90", "140" }, new[] { "180", "35" } };
        project.Curves[0].Mode = CurveMode.Polyline;

        BuildResult result = GraphBuilder.Build(project);

        PolylinePrimitive curve = Assert.Single(Of<PolylinePrimitive>(result, PrimitiveRole.Curve));
        Close.Equal(new Pt(0, -30), curve.Points[0], 1e-9);      // 0° — вниз, 70 из 140 — половина радиуса
        Close.Equal(new Pt(60, 0), curve.Points[1], 1e-9);       // 90° — вправо, на внешней окружности
        Close.Equal(new Pt(0, 15), curve.Points[2], 1e-9);       // 180° — вверх
    }

    [Fact]
    public void Polar_curve_beyond_the_scale_is_cut_at_the_outer_circle()
    {
        GraphProject project = SampleData.PolarProject();
        project.YAxis.Max = 100;        // кривая доходит до 120 cd

        BuildResult result = GraphBuilder.Build(project);

        List<PolylinePrimitive> pieces = Of<PolylinePrimitive>(result, PrimitiveRole.Curve);
        Assert.NotEmpty(pieces);
        Assert.Empty(Of<BezierPrimitive>(result, PrimitiveRole.Curve));
        Assert.All(pieces, piece => Assert.All(piece.Points, p => Assert.True(p.Length <= 60 + 1e-6)));
        // Концы кусков лежат точно на окружности.
        Assert.Contains(pieces, piece => Math.Abs(piece.Points[0].Length - 60) < 1e-6 || Math.Abs(piece.Points[piece.Points.Count - 1].Length - 60) < 1e-6);
    }

    [Fact]
    public void Ternary_sample_has_three_scales_along_the_sides()
    {
        BuildResult result = GraphBuilder.Build(SampleData.TernaryProject());

        Assert.Equal(DiagramKind.Ternary, result.Kind);
        double side = 120, height = side * Math.Sqrt(3) / 2;
        Close.Equal(height, result.HeightMm, 1e-9);

        // Три стороны основной линией, по девять линий сетки в каждом из трёх направлений.
        List<LinePrimitive> frame = Of<LinePrimitive>(result, PrimitiveRole.Frame);
        Assert.Equal(3, frame.Count);
        Assert.All(frame, line => Assert.Equal(LineStyleId.Main, line.Style));
        Assert.Equal(27, Of<LinePrimitive>(result, PrimitiveRole.Grid).Count);

        // У каждой шкалы числа 0, 20, 40, 60, 100 и «%» вместо или между последними числами.
        List<TextPrimitive> labels = Of<TextPrimitive>(result, PrimitiveRole.TickLabel);
        Assert.Equal(3, labels.Count(t => t.Text == "100"));
        Assert.Equal(3, labels.Count(t => t.Text == "0"));
        Assert.Equal(3, Of<TextPrimitive>(result, PrimitiveRole.Unit).Count(t => t.Text == "%"));

        // Обозначения составляющих и три стрелки направления возрастания.
        Assert.Equal(new[] { "Pb", "Sb", "Sn" }, Of<TextPrimitive>(result, PrimitiveRole.AxisTitle).Select(t => t.Text).OrderBy(t => t).ToArray());
        Assert.Equal(3, Of<LeaderArrowPrimitive>(result, PrimitiveRole.AxisTitle).Count);
    }

    [Fact]
    public void Ternary_point_position_follows_its_composition()
    {
        GraphProject project = SampleData.TernaryProject();
        project.Data.Rows = new List<string[]> { new[] { "100", "0" }, new[] { "0", "100" }, new[] { "0", "0" }, new[] { "20", "30" } };
        project.Curves.RemoveAt(1);
        project.Curves[0].Mode = CurveMode.Polyline;

        BuildResult result = GraphBuilder.Build(project);

        double side = 120, height = side * Math.Sqrt(3) / 2;
        PolylinePrimitive curve = Assert.Single(Of<PolylinePrimitive>(result, PrimitiveRole.Curve));
        Close.Equal(new Pt(side, 0), curve.Points[0], 1e-9);                     // 100 % A — правая нижняя вершина
        Close.Equal(new Pt(side / 2, height), curve.Points[1], 1e-9);            // 100 % B — верхняя вершина
        Close.Equal(new Pt(0, 0), curve.Points[2], 1e-9);                        // 100 % C — левая нижняя вершина
        Close.Equal(new Pt((20 + 15) / 100.0 * side, 0.3 * height), curve.Points[3], 1e-9);
    }

    [Fact]
    public void Ternary_points_outside_the_triangle_are_reported_and_skipped()
    {
        GraphProject project = SampleData.TernaryProject();
        project.Data.Rows = new List<string[]> { new[] { "10", "10" }, new[] { "70", "50" }, new[] { "30", "20" }, new[] { "-5", "20" } };
        project.Curves.RemoveAt(1);
        project.Curves[0].Mode = CurveMode.Polyline;

        BuildResult result = GraphBuilder.Build(project);

        DataIssue issue = Assert.Single(result.Issues, i => i.Message.Contains("вне треугольника"));
        Assert.Equal(2, issue.Row);
        Assert.Equal(2, Assert.Single(Of<PolylinePrimitive>(result, PrimitiveRole.Curve)).Points.Count);
    }
}

public class ProjectFormatTests
{
    [Fact]
    public void New_settings_survive_saving_and_loading()
    {
        GraphProject project = SampleData.TmmProject();
        project.Polar.ZeroAngleDeg = 90;
        project.Ternary.TitleC = "Pb";

        GraphProject copy = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project));

        Assert.Equal(2, copy.FormatVersion);
        Assert.Equal(1, copy.Curves[3].Axis);
        Assert.Equal(CurveLabelMode.Leader, copy.Curves[0].Label);
        Assert.Equal(MarkerPlacement.Characteristic, copy.Curves[0].MarkerPlacement);
        Assert.Equal(MarkerShape.Dot, copy.Curves[0].Marker);
        AxisSettings extra = Assert.Single(copy.ExtraAxes);
        Assert.Equal("ω_q", extra.Title);
        Assert.Equal(AxisSide.Right, extra.Side);
        Assert.Equal(NumberStyle.PiFractions, copy.XAxis.Numbers);
        Assert.Equal("рад", copy.XAxis.Unit);
        Assert.True(copy.Graph.ShowScaleFactors);
        Assert.True(copy.Graph.SeparateArrows);
        Assert.Equal(90, copy.Polar.ZeroAngleDeg);
        Assert.Equal("Pb", copy.Ternary.TitleC);

        // Построение из копии даёт ту же сцену.
        Assert.Equal(GraphBuilder.Build(project).Scene.Primitives.Count, GraphBuilder.Build(copy).Scene.Primitives.Count);
    }

    [Fact]
    public void Project_of_the_previous_version_opens_with_default_new_settings()
    {
        const string json = @"{
  ""FormatVersion"": 1,
  ""Data"": { ""XName"": ""x"", ""Rows"": [[0, 0], [1, 1], [2, 4]] },
  ""Curves"": [ { ""Name"": ""y"", ""LineStyle"": ""Main"", ""Mode"": ""Smooth"", ""Marker"": ""Circle"" } ],
  ""XAxis"": { ""Title"": ""x, мм"" },
  ""YAxis"": { ""Title"": ""σ, МПа"" },
  ""Graph"": { ""WidthMm"": 80, ""HeightMm"": 60 }
}";
        GraphProject project = ProjectSerializer.FromJson(json);

        Assert.Equal(DiagramKind.Cartesian, project.Graph.Kind);
        Assert.Empty(project.ExtraAxes);
        Assert.Equal("", project.XAxis.Unit);
        Assert.Equal(0, project.Curves[0].Axis);
        Assert.Equal(CurveLabelMode.None, project.Curves[0].Label);
        Assert.NotNull(project.Polar);
        Assert.NotNull(project.Ternary);

        BuildResult result = GraphBuilder.Build(project);
        Assert.False(result.HasErrors);
        Assert.Contains(result.Scene.Primitives.OfType<TextPrimitive>(), t => t.Text == "σ, МПа");
    }

    [Fact]
    public void Curve_cannot_refer_to_a_missing_scale()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Curves[0].Axis = 3;

        BuildResult result = GraphBuilder.Build(project);

        Assert.False(result.HasErrors);
        Assert.Equal(0, project.Curves[0].Axis);
        Assert.Single(result.YScales);
    }
}

public class EnteredAnglesAndSamplesTests
{
    private static List<T> Of<T>(BuildResult result, PrimitiveRole role) where T : Primitive =>
        result.Scene.Primitives.OfType<T>().Where(p => p.Role == role).ToList();

    [Theory]
    [InlineData(0.5236, Math.PI / 6)]      // угол, введённый числом с округлением
    [InlineData(6.283, 2 * Math.PI)]
    [InlineData(6.28, 2 * Math.PI)]
    [InlineData(-1.5708, -Math.PI / 2)]
    [InlineData(3.1416, Math.PI)]
    public void Rounded_angle_is_replaced_by_exact_fraction_of_pi(double entered, double expected)
    {
        Close.Equal(expected, NiceScale.SnapToPi(entered), 1e-12);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(1.05)]
    [InlineData(0)]
    public void Other_values_are_left_as_entered(double entered)
    {
        Assert.Equal(entered, NiceScale.SnapToPi(entered));
    }

    [Fact]
    public void Step_and_limits_typed_as_decimals_still_give_pi_labels()
    {
        // Так пределы и шаг попадают в проект из окна: числом с шестью знаками.
        GraphProject project = SampleData.TmmProject();
        project.XAxis.AutoLimits = false;
        project.XAxis.Min = 0;
        project.XAxis.Max = 6.283185;
        project.XAxis.GridStep = 0.523599;

        BuildResult result = GraphBuilder.Build(project);

        Assert.False(result.HasErrors);
        Close.Equal(2 * Math.PI, result.XMax, 1e-12);
        List<string> labels = Of<TextPrimitive>(result, PrimitiveRole.TickLabel).Select(t => t.Text).ToList();
        Assert.Contains(TextMarkup.Fraction("π", "6"), labels);
        Assert.Contains("2π", labels);
    }

    [Theory]
    [InlineData("sample")]
    [InlineData("sample:tmm")]
    [InlineData("sample:polar")]
    [InlineData("sample:ternary")]
    public void Built_in_samples_build_without_remarks(string name)
    {
        BuildResult result = GraphBuilder.Build(SampleData.ByName(name));

        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Short_stub_of_a_grid_line_between_two_labels_is_dropped()
    {
        // Числа радиальной шкалы стоят вплотную вдоль луча: между ними от лучей сетки остались бы обрывки в 1–2 мм.
        BuildResult result = GraphBuilder.Build(SampleData.PolarProject());

        List<LinePrimitive> grid = Of<LinePrimitive>(result, PrimitiveRole.Grid);
        Assert.NotEmpty(grid);
        Assert.All(grid, line => Assert.True(Pt.Distance(line.A, line.B) >= 3 - 1e-9,
            "обрывок линии сетки длиной " + Pt.Distance(line.A, line.B).ToString("0.###", CultureInfo.InvariantCulture) + " мм"));
    }
}

public class IndicesInsideFractionsTests
{
    private static string Describe(string text) => string.Join("|", TextMarkup.Parse(text).Select(r => r.ToString()));

    [Fact]
    public void Fraction_parts_keep_their_indices()
    {
        // Обозначение с индексом над единицей измерения: V_q / (м/с).
        string fraction = TextMarkup.Fraction("V_q", "м/с");
        Assert.Equal("\\frac{V_{q}}{м/с}", fraction);

        List<TextRun> runs = TextMarkup.Parse(fraction);
        Assert.Equal(2, runs.Count);
        Assert.Equal(TextRunKind.Numerator, runs[0].Kind);
        Assert.Equal("V_{q}", runs[0].Text);
        Assert.Equal("V|_{q}", string.Join("|", TextMarkup.ParsePart(runs[0].Text).Select(r => r.ToString())));
        Assert.Equal("м/с", runs[1].Text);

        Assert.Equal("Vq/м/с", TextMarkup.Plain(fraction));
    }

    [Theory]
    [InlineData("a = 5 \\frac{м}{с^2}", "a = 5 |\\frac{м}|{с^2}")]
    [InlineData("\\frac{M_{с пр}}{Н·м} и x", "\\frac{M_{с пр}}|{Н·м}| и x")]     // скобки индекса не закрывают числитель
    [InlineData("\\frac{a}{b", "frac{a}{b")]                                    // незакрытая дробь — не дробь
    public void Braces_of_indices_do_not_end_the_fraction_part(string text, string expected)
    {
        Assert.Equal(expected, Describe(text));
    }

    [Fact]
    public void Fraction_inside_a_fraction_is_written_with_a_slash()
    {
        string fraction = TextMarkup.Fraction("\\frac{a}{b}", "c_1");
        Assert.Equal("\\frac{a/b}{c_{1}}", fraction);
        Assert.Equal("a/b/c1", TextMarkup.Plain(fraction));
    }

    [Fact]
    public void Index_inside_numerator_moves_it_away_from_the_bar()
    {
        double plain = TextMarkup.PartRise(TextRunKind.Numerator, "V");
        Assert.Equal(TextMarkup.BaselineRise(TextRunKind.Numerator), plain, 12);
        Assert.Equal(plain + TextMarkup.NumeratorLift, TextMarkup.PartRise(TextRunKind.Numerator, "V_q"), 12);
        Assert.Equal(plain, TextMarkup.PartRise(TextRunKind.Numerator, "x^2"), 12);
        Assert.Equal(TextMarkup.BaselineRise(TextRunKind.Denominator) - TextMarkup.DenominatorDrop,
            TextMarkup.PartRise(TextRunKind.Denominator, "с^2"), 12);

        // Строка с индексом в числителе выше, со степенью в знаменателе — ниже обычной дроби.
        Assert.True(TextMetrics.Above("\\frac{V_q}{м}", 5) > TextMetrics.Above("\\frac{V}{м}", 5) + 1);
        Assert.True(TextMetrics.Below("\\frac{м}{с^2}", 5) > TextMetrics.Below("\\frac{м}{с}", 5) + 1);
        Assert.True(TextMetrics.ClearedAbove("\\frac{V_q}{м}", 5) > TextMetrics.ClearedAbove("\\frac{V}{м}", 5) + 1);
    }

    [Fact]
    public void Width_of_a_fraction_counts_indices_at_their_size()
    {
        double plain = TextMetrics.EstimateWidth("\\frac{Vq}{м}", 5);
        double indexed = TextMetrics.EstimateWidth("\\frac{V_q}{м}", 5);
        Assert.True(indexed < plain);
        Assert.True(indexed > TextMetrics.EstimateWidth("\\frac{V}{м}", 5));
    }

    [Fact]
    public void Unit_written_as_a_fraction_keeps_the_index_of_the_symbol()
    {
        GraphProject project = SampleData.TmmProject();
        project.Graph.Units = UnitPlacement.Fraction;

        BuildResult result = GraphBuilder.Build(project);

        Assert.False(result.HasErrors);
        List<string> texts = result.Scene.Primitives.OfType<TextPrimitive>().Select(t => t.Text).ToList();
        Assert.Contains("\\frac{V_{q}}{м}", texts);
        Assert.Contains("\\frac{φ}{рад}", texts);
    }
}

public class LogarithmicScaleTests
{
    private static List<T> Of<T>(BuildResult result, PrimitiveRole role) where T : Primitive =>
        result.Scene.Primitives.OfType<T>().Where(p => p.Role == role).ToList();

    [Fact]
    public void Limits_are_whole_decades()
    {
        LogScale.Limits(0.37, 4200, out double min, out double max);
        Close.Equal(0.1, min, 1e-15);
        Close.Equal(10000, max, 1e-9);

        LogScale.Limits(10, 100, out min, out max);
        Close.Equal(10, min, 1e-12);
        Close.Equal(100, max, 1e-12);
    }

    [Fact]
    public void Short_scale_labels_two_and_five_long_scale_only_decades()
    {
        var major = new List<double>();
        var minor = new List<double>();
        LogScale.Lines(1, 100, major, minor);
        Assert.Equal(new[] { 1.0, 2, 5, 10, 20, 50, 100 }, major.ToArray());
        Assert.Equal(12, minor.Count);                  // 3, 4, 6…9 в двух декадах

        LogScale.Lines(0.1, 10000, major, minor);
        Assert.Equal(6, major.Count);
        Assert.Equal(5 * 8, minor.Count);
    }

    [Theory]
    [InlineData(0.01, "0,01")]
    [InlineData(1, "1")]
    [InlineData(1000, "1000")]
    [InlineData(1e6, "10^{6}")]
    [InlineData(1e-5, "10^{−5}")]
    public void Labels_are_decimals_or_powers_of_ten(double value, string expected)
    {
        Assert.Equal(expected, LogScale.Label(value, true));
    }

    private static GraphProject Squares()
    {
        var project = new GraphProject();
        project.Data.XName = "x";
        for (int x = 1; x <= 100; x++)
        {
            project.Data.Rows.Add(new[] { x.ToString(CultureInfo.InvariantCulture), (x * x).ToString(CultureInfo.InvariantCulture) });
        }
        project.Curves.Add(new CurveSettings { Name = "y", Mode = CurveMode.Polyline });
        project.XAxis.Logarithmic = true;
        project.YAxis.Logarithmic = true;
        project.Graph.WidthMm = 100;
        project.Graph.HeightMm = 120;
        return project;
    }

    [Fact]
    public void Points_are_placed_by_logarithms()
    {
        BuildResult result = GraphBuilder.Build(Squares());

        Assert.False(result.HasErrors);
        Close.Equal(1, result.XMin, 1e-12);
        Close.Equal(100, result.XMax, 1e-12);
        Close.Equal(10000, result.YMax, 1e-9);
        Close.Equal(50, result.ScaleX, 1e-9);           // 100 мм на две декады
        Close.Equal(30, result.ScaleY, 1e-9);           // 120 мм на четыре декады

        // y = x² в логарифмических координатах — прямая: точка (10; 100) — в середине поля.
        PolylinePrimitive curve = Assert.Single(Of<PolylinePrimitive>(result, PrimitiveRole.Curve));
        Close.Equal(new Pt(50, 60), curve.Points[9], 1e-9);

        // Числа у декад, промежуточные линии сетки — без чисел.
        List<string> labels = Of<TextPrimitive>(result, PrimitiveRole.TickLabel).Select(t => t.Text).ToList();
        Assert.Contains("1000", labels);
        Assert.Contains("10000", labels);
        Assert.True(Of<LinePrimitive>(result, PrimitiveRole.Grid).Count > 20);
    }

    [Fact]
    public void Zero_and_negative_values_are_reported_and_skipped()
    {
        GraphProject project = Squares();
        project.Data.Rows.Insert(0, new[] { "0", "0" });
        project.Data.Rows.Add(new[] { "200", "-5" });

        BuildResult result = GraphBuilder.Build(project);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Message.Contains("логарифмической") && i.Message.Contains("2"));
        Assert.Equal(100, Assert.Single(Of<PolylinePrimitive>(result, PrimitiveRole.Curve)).Points.Count);
    }

    [Fact]
    public void Manual_limits_must_be_positive()
    {
        GraphProject project = Squares();
        project.YAxis.AutoLimits = false;
        project.YAxis.Min = 0;
        project.YAxis.Max = 100;

        BuildResult result = GraphBuilder.Build(project);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Message.Contains("больше нуля"));
    }
}

public class PolarOptionsTests
{
    private static List<TextPrimitive> Labels(GraphProject project) =>
        GraphBuilder.Build(project).Scene.Primitives.OfType<TextPrimitive>().Where(t => t.Role == PrimitiveRole.TickLabel).ToList();

    [Fact]
    public void Zero_ray_can_point_at_any_angle()
    {
        GraphProject project = SampleData.PolarProject();
        project.Polar.ZeroAngleDeg = 45;

        TextPrimitive zero = Labels(project).Single(t => t.Text == "0°");
        double direction = Math.Atan2(zero.Anchor.Y + 1.75, zero.Anchor.X) * 180 / Math.PI;
        Assert.InRange(direction, 40, 50);
    }

    [Fact]
    public void Angles_can_run_from_minus_180_to_180()
    {
        GraphProject project = SampleData.PolarProject();
        project.Polar.SignedAngles = true;

        List<string> labels = Labels(project).Select(t => t.Text).ToList();
        Assert.Contains("180°", labels);
        Assert.Contains("-30°", labels);
        Assert.DoesNotContain("330°", labels);
    }

    [Fact]
    public void Radius_numbers_can_stand_on_either_side_of_the_ray()
    {
        // Нулевой луч вниз, числа вдоль него: справа от луча (смотря от полюса) — левее его на чертеже, и наоборот.
        GraphProject project = SampleData.PolarProject();
        double right = Labels(project).Single(t => t.Text == "20").Anchor.X;
        project.Polar.LabelSide = RadialLabelSide.Left;
        double left = Labels(project).Single(t => t.Text == "20").Anchor.X;

        Assert.True(right < 0, "справа от луча, идущего вниз, — левее его");
        Assert.True(left > 0);
    }

    [Fact]
    public void Old_project_file_keeps_its_zero_direction()
    {
        GraphProject project = ProjectSerializer.FromJson("{ \"FormatVersion\": 2, \"Graph\": { \"Kind\": \"Polar\" }, \"Polar\": { \"Zero\": \"Down\" } }");

        Assert.Equal(270, project.Polar.ZeroAngleDeg);
        Assert.DoesNotContain("\"Zero\"", ProjectSerializer.ToJson(project));
    }
}

public class ArrowsUnitsAndPointsTests
{
    private static List<T> Of<T>(BuildResult result, PrimitiveRole role) where T : Primitive =>
        result.Scene.Primitives.OfType<T>().Where(p => p.Role == role).ToList();

    [Fact]
    public void Unit_stands_behind_the_tip_of_the_arrow()
    {
        GraphProject project = SampleData.TmmProject();
        project.Graph.Units = UnitPlacement.AtArrow;

        BuildResult result = GraphBuilder.Build(project);

        List<TextPrimitive> units = Of<TextPrimitive>(result, PrimitiveRole.Unit);
        List<LeaderArrowPrimitive> arrows = Of<LeaderArrowPrimitive>(result, PrimitiveRole.AxisTitle);
        Assert.Equal(3, arrows.Count);
        Assert.Equal(3, units.Count);

        // «φ ⟶ рад»: единица правее острия горизонтальной стрелки, на той же высоте.
        LeaderArrowPrimitive horizontal = arrows.Single(a => Math.Abs(a.Tip.Y - a.Tail.Y) < 1e-9);
        TextPrimitive rad = units.Single(t => t.Text == "рад");
        Assert.True(rad.Anchor.X > horizontal.Tip.X && rad.Anchor.Y < horizontal.Tip.Y && rad.Anchor.Y > horizontal.Tip.Y - 5);

        // У шкал Y — над остриём вертикальной стрелки.
        TextPrimitive metre = units.Single(t => t.Text == "м");
        Assert.Contains(arrows, a => Math.Abs(a.Tip.X - metre.Anchor.X) < 1e-9 && metre.Anchor.Y > a.Tip.Y);
        TextPrimitive dash = units.Single(t => t.Text == "–");
        Assert.Contains(arrows, a => Math.Abs(a.Tip.X - dash.Anchor.X) < 1e-9 && dash.Anchor.Y > a.Tip.Y);
    }

    [Fact]
    public void Without_separate_arrows_the_unit_follows_the_title()
    {
        GraphProject project = SampleData.TmmProject();
        project.Graph.Units = UnitPlacement.AtArrow;
        project.Graph.SeparateArrows = false;

        BuildResult result = GraphBuilder.Build(project);

        Assert.Contains(Of<TextPrimitive>(result, PrimitiveRole.AxisTitle), t => t.Text == "φ, рад");
    }

    [Fact]
    public void Kompas_point_markers_use_point_styles()
    {
        GraphProject project = SampleData.SqrtProject();
        project.Curves[0].Marker = MarkerShape.KompasPointCircle;
        project.Curves[0].MarkerEvery = 10;

        List<PointPrimitive> points = Of<PointPrimitive>(GraphBuilder.Build(project), PrimitiveRole.Marker);

        Assert.Equal(10, points.Count);
        Assert.All(points, p => Assert.Equal(6, p.Style));
        Assert.Equal(1, MarkerShapes.KompasPointStyle(MarkerShape.KompasPoint));
        Assert.Equal(9, MarkerShapes.KompasPointStyle(MarkerShape.KompasPointBoldCross));
        Assert.Equal(0, MarkerShapes.KompasPointStyle(MarkerShape.Dot));
    }
}
