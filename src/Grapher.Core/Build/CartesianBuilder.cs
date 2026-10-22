using System;
using System.Collections.Generic;
using System.Globalization;
using Grapher.Core.Geometry;
using Grapher.Core.Model;
using Grapher.Core.Numerics;
using Grapher.Core.Parsing;
using Grapher.Core.Scene;

namespace Grapher.Core.Build;

/// <summary>Диаграмма в прямоугольной системе координат: одна шкала X и одна или несколько шкал Y.</summary>
internal static class CartesianBuilder
{
    private const double Gap = SceneTools.GapMm;
    private const double LegendSampleMm = 12.0;

    /// <summary>Просвет между соседними шкалами, стоящими с одной стороны поля, мм.</summary>
    private const double ScaleSpacingMm = 4.0;

    /// <summary>Наименьшая длина делительных штрихов у шкалы, вынесенной за поле диаграммы, мм.</summary>
    private const double ParallelTickMm = 1.5;

    /// <summary>
    /// Просвет между линией и областью, которую закрывает текст на чертеже, мм
    /// (половина толщины основной линии и небольшой запас).
    /// </summary>
    private const double LineClearanceMm = 0.5;

    /// <summary>Подпись деления: число либо дробь (доля π) в записи <see cref="TextMarkup"/>.</summary>
    private readonly struct TickText
    {
        public readonly string Text;
        public readonly bool IsFraction;

        public TickText(string text, bool fraction = false)
        {
            Text = text;
            IsFraction = fraction;
        }

        public double Width(double height) => TextMetrics.EstimateWidth(Text, height);
    }

    private sealed class Scale
    {
        public AxisSettings Axis;
        public string Name;
        public double Min, Max;

        /// <summary>Масштабный коэффициент, мм на единицу величины.</summary>
        public double K;

        public AxisTicks Ticks;
        public List<TickText> Texts = new List<TickText>();

        /// <summary>Номера делений, у которых стоят числа.</summary>
        public List<int> Labeled = new List<int>();

        /// <summary>Деление, число у которого не наносится: на его месте единица измерения.</summary>
        public int Skipped = -1;

        /// <summary>Единица измерения между числами шкалы: текст и положение вдоль шкалы, мм.</summary>
        public string UnitText;
        public double UnitAt;

        /// <summary>Ширина столбца чисел, мм.</summary>
        public double LabelWidth;

        /// <summary>Сторона, с которой стоят числа: −1 — слева от линии шкалы, +1 — справа.</summary>
        public int Side = -1;

        /// <summary>Абсцисса линии шкалы, мм.</summary>
        public double Position;

        /// <summary>Шкала лежит на стороне рамки (иначе вынесена за поле параллельно оси).</summary>
        public bool OnFrame = true;

        public double TickLength;

        /// <summary>Внешний край всего, что относится к шкале (числа, обозначение), мм.</summary>
        public double OuterEdge;

        /// <summary>Логарифмическая шкала: <see cref="K"/> — миллиметров на декаду.</summary>
        public bool Log;

        /// <summary>Линии сетки логарифмической шкалы без чисел (2…9 единиц декады).</summary>
        public List<double> Minor = new List<double>();

        public double Map(double value) => Log ? Math.Log10(value / Min) * K : (value - Min) * K;

        /// <summary>Значение можно отложить на шкале (на логарифмической — только больше нуля).</summary>
        public bool Accepts(double value) => !Log || value > 0;

        /// <summary>Длина шкалы в единицах величины (на логарифмической — в декадах).</summary>
        public double Span => Log ? LogScale.Decades(Min, Max) : Max - Min;

        public string Unit => (Axis.Unit ?? "").Trim();
        public string Title => (Axis.Title ?? "").Trim();
    }

    private sealed class Layout
    {
        public BuildContext C;
        public GraphSettings G;
        public GraphScene Scene;
        public double Width, Height;
        public Scale X;
        public List<Scale> Y;

        /// <summary>Ордината оси X и абсцисса оси Y в локальных координатах, мм.</summary>
        public double AxisXPos, AxisYPos;

        public double Tick, HLabel, HTitle;

        /// <summary>Числа у шкал наносятся.</summary>
        public bool Labels;

        /// <summary>Строка чисел под осью X: середина (черта дробей), базовая линия обычных чисел, нижний край.</summary>
        public double XRowCenter, XRowBaseline, XRowBottom;

        /// <summary>Ноль общий для шкал X и Y: наносится один раз у точки их пересечения (Р 50-77-88, п. 2.7).</summary>
        public bool SingleZero;

        /// <summary>Границы места, занятого надписями вокруг поля.</summary>
        public double Top, Bottom, Left, Right;

        /// <summary>Правый край обозначения оси X, стоящего у середины шкалы (для надписи масштаба в той же строке).</summary>
        public double XTitleRight = double.NaN;

        public double XTitleRowCenter;

        public double AxisEndX => G.ShowAxes && G.ShowArrows ? Width + Math.Max(0, G.AxisExtensionMm) : Width;
        public double AxisEndY => G.ShowAxes && G.ShowArrows ? Height + Math.Max(0, G.AxisExtensionMm) : Height;

        public double ArrowHead => G.ArrowLengthMm > 0 ? G.ArrowLengthMm : 4;

        /// <summary>Длина самостоятельной стрелки рядом с обозначением величины, мм.</summary>
        public double SeparateArrow => Math.Max(8, 3 * HTitle);
    }

    public static void Build(BuildContext c)
    {
        GraphProject project = c.Project;
        GraphSettings g = c.G;
        BuildResult result = c.Result;

        double width = GraphSizeSolver.Clamp(g.WidthMm);
        double height = GraphSizeSolver.Clamp(g.HeightMm);

        var yAxes = new List<AxisSettings> { project.YAxis };
        yAxes.AddRange(project.ExtraAxes);
        int scaleCount = yAxes.Count;

        // 1. Размах данных: общий по X и свой для каждой шкалы Y.
        double xLow = double.MaxValue, xHigh = double.MinValue;
        var yLow = new double[scaleCount];
        var yHigh = new double[scaleCount];
        var hasData = new bool[scaleCount];
        bool anyData = false;
        for (int i = 0; i < scaleCount; i++)
        {
            yLow[i] = double.MaxValue;
            yHigh[i] = double.MinValue;
        }

        bool xLog = project.XAxis.Logarithmic;
        foreach (SeriesPoints series in c.Data.Series)
        {
            CurveSettings curve = project.Curves[series.CurveIndex];
            if (curve == null || !curve.Visible) continue;
            int axis = AxisIndex(curve, scaleCount);
            bool yLog = yAxes[axis].Logarithmic;
            foreach (Pt p in series.Points)
            {
                // На логарифмической шкале нельзя отложить ноль и отрицательные значения.
                if (xLog && !(p.X > 0) || yLog && !(p.Y > 0)) continue;
                anyData = true;
                hasData[axis] = true;
                if (p.X < xLow) xLow = p.X;
                if (p.X > xHigh) xHigh = p.X;
                if (p.Y < yLow[axis]) yLow[axis] = p.Y;
                if (p.Y > yHigh[axis]) yHigh[axis] = p.Y;
            }
        }

        // 2. Пределы и сетка основных шкал.
        var x = new Scale { Axis = project.XAxis, Name = "X", Log = xLog };
        ResolveLimits(c, x, anyData, xLow, xHigh, TickGenerator.AutoDivisions(width));
        var main = new Scale { Axis = project.YAxis, Name = "Y", Log = project.YAxis.Logarithmic };
        ResolveLimits(c, main, hasData[0], yLow[0], yHigh[0], TickGenerator.AutoDivisions(height));

        if (g.EqualScale && (x.Log || main.Log))
        {
            c.Warn("Одинаковый масштаб по осям при логарифмической шкале не применяется.");
        }
        else if (g.EqualScale)
        {
            double equalHeight = GraphSizeSolver.HeightForEqualScale(width, x.Max - x.Min, main.Max - main.Min);
            height = GraphSizeSolver.Clamp(equalHeight);
            if (Math.Abs(height - equalHeight) > 1e-9)
            {
                c.Warn("При одинаковом масштабе по осям высота графика выходит за допустимые пределы — масштаб по Y отличается.");
            }
        }

        x.K = width / x.Span;
        main.K = height / main.Span;
        x.Ticks = x.Log ? LogTicks(x) : TickGenerator.Generate(g.GridMode, x.Axis.GridStep, x.Min, x.Max, width, "X", x.Axis.Numbers);
        main.Ticks = main.Log ? LogTicks(main) : TickGenerator.Generate(g.GridMode, main.Axis.GridStep, main.Min, main.Max, height, "Y", main.Axis.Numbers);
        if (x.Ticks.Warning != null) c.Warn(x.Ticks.Warning);
        if (main.Ticks.Warning != null) c.Warn(main.Ticks.Warning);

        // 3. Дополнительные шкалы Y.
        var scales = new List<Scale> { main };
        for (int i = 1; i < scaleCount; i++)
        {
            var extra = new Scale
            {
                Axis = yAxes[i],
                Name = "Y" + (i + 1).ToString(CultureInfo.InvariantCulture),
                Side = yAxes[i].Side == AxisSide.Right ? 1 : -1,
                Log = yAxes[i].Logarithmic
            };
            ResolveExtra(c, extra, main, hasData[i], yLow[i], yHigh[i], height);
            scales.Add(extra);
        }

        result.XMin = x.Min;
        result.XMax = x.Max;
        result.YMin = main.Min;
        result.YMax = main.Max;
        result.WidthMm = width;
        result.HeightMm = height;
        result.ScaleX = x.K;
        result.ScaleY = main.K;
        result.TicksX = x.Ticks;
        result.TicksY = main.Ticks;
        foreach (Scale scale in scales)
        {
            result.YScales.Add(new ScaleResult { Name = scale.Name, Min = scale.Min, Max = scale.Max, Step = scale.Ticks.Step, MmPerUnit = scale.K });
        }

        c.Scene.PlotRect = new RectD(0, 0, width, height);

        var l = new Layout
        {
            C = c,
            G = g,
            Scene = c.Scene,
            Width = width,
            Height = height,
            X = x,
            Y = scales,
            Tick = Math.Max(0, g.TickLengthMm),
            HLabel = g.LabelHeightMm,
            HTitle = g.TitleHeightMm,
            Labels = g.ShowTickLabels && g.LabelHeightMm > 0,
            Top = height,
            Bottom = 0,
            Left = 0,
            Right = width
        };

        // Положение осей: в углу рамки либо в нуле, если ноль внутри пределов.
        if (g.Crossing == AxesCrossing.AtZero)
        {
            if (x.Min < 0 && x.Max > 0) l.AxisYPos = x.Map(0);
            if (main.Min < 0 && main.Max > 0) l.AxisXPos = main.Map(0);
        }
        if (g.ShowAxes && g.ShowArrows)
        {
            l.Top = Math.Max(l.Top, l.AxisEndY);
            l.Right = Math.Max(l.Right, l.AxisEndX);
        }

        // 4. Подписи делений.
        PrepareLabels(l, x, true);
        foreach (Scale scale in scales)
        {
            PrepareLabels(l, scale, false);
        }
        l.SingleZero = l.Labels && HasZeroAtStart(x) && HasZeroAtStart(main);

        bool rightOnFrame = false;
        foreach (Scale scale in scales)
        {
            if (scale != main && scale.Side > 0)
            {
                rightOnFrame = true;
                break;
            }
        }

        BuildGrid(l, rightOnFrame);
        BuildFrame(l, rightOnFrame);
        BuildAxes(l);
        DrawXScale(l);
        DrawYScales(l);
        DrawXTitle(l);
        DrawScaleFactors(l);

        List<BuiltCurve> curves = DrawCurves(l);

        RectD field = c.Scene.PlotRect;
        c.KeepOut.AddRange(LeaderLabels.Place(c, curves, box => field.Contains(new Pt(box.XMin, box.YMin)) && field.Contains(new Pt(box.XMax, box.YMax))));

        BuildLegend(l);
    }

    private static int AxisIndex(CurveSettings curve, int scaleCount) =>
        curve.Axis > 0 && curve.Axis < scaleCount ? curve.Axis : 0;

    // ------------------------------------------------------------------ пределы

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static void ResolveLimits(BuildContext c, Scale scale, bool hasData, double dataMin, double dataMax, int targetDivisions)
    {
        AxisSettings axis = scale.Axis;
        if (scale.Log)
        {
            ResolveLogLimits(c, scale, hasData, dataMin, dataMax);
            return;
        }
        if (!axis.AutoLimits)
        {
            if (IsFinite(axis.Min) && IsFinite(axis.Max) && axis.Max > axis.Min)
            {
                // Пределы угла, введённые числом (6,283), приводятся к точным долям π.
                bool snap = axis.Numbers == NumberStyle.PiFractions;
                scale.Min = snap ? NiceScale.SnapToPi(axis.Min) : axis.Min;
                scale.Max = snap ? NiceScale.SnapToPi(axis.Max) : axis.Max;
                return;
            }

            c.Error(string.Format(CultureInfo.CurrentCulture,
                "Пределы оси {0} заданы неверно: максимум должен быть больше минимума. Временно использованы пределы по данным.",
                scale.Name));
        }

        bool pi = axis.Numbers == NumberStyle.PiFractions;
        if (!hasData)
        {
            scale.Min = 0;
            scale.Max = pi ? 2 * Math.PI : 10;
            return;
        }

        if (pi) NiceScale.NiceLimitsPi(dataMin, dataMax, targetDivisions, out scale.Min, out scale.Max, out _);
        else NiceScale.NiceLimits(dataMin, dataMax, targetDivisions, out scale.Min, out scale.Max, out _);
    }

    /// <summary>Пределы логарифмической шкалы: заданные (больше нуля) либо по данным, округлённые до декад.</summary>
    private static void ResolveLogLimits(BuildContext c, Scale scale, bool hasData, double dataMin, double dataMax)
    {
        AxisSettings axis = scale.Axis;
        if (!axis.AutoLimits)
        {
            if (IsFinite(axis.Min) && IsFinite(axis.Max) && axis.Min > 0 && axis.Max > axis.Min)
            {
                scale.Min = axis.Min;
                scale.Max = axis.Max;
                return;
            }
            c.Error(string.Format(CultureInfo.CurrentCulture,
                "Пределы логарифмической шкалы {0} заданы неверно: оба должны быть больше нуля, максимум — больше минимума. " +
                "Временно использованы пределы по данным.", scale.Name));
        }

        if (hasData) LogScale.Limits(dataMin, dataMax, out scale.Min, out scale.Max);
        else
        {
            scale.Min = 1;
            scale.Max = 10;
        }
    }

    /// <summary>Линии сетки логарифмической шкалы: с числами — декады (и 2, 5 на короткой шкале), без чисел — остальные.</summary>
    private static AxisTicks LogTicks(Scale scale)
    {
        var ticks = new AxisTicks { FromMin = true };
        LogScale.Lines(scale.Min, scale.Max, ticks.Values, scale.Minor);
        return ticks;
    }

    /// <summary>
    /// Пределы и деления дополнительной шкалы. По возможности деления совмещаются с линиями сетки
    /// основной шкалы: тогда шкале не нужна собственная сетка.
    /// </summary>
    private static void ResolveExtra(BuildContext c, Scale scale, Scale main, bool hasData, double dataMin, double dataMax, double height)
    {
        AxisSettings axis = scale.Axis;
        if (scale.Log)
        {
            ResolveLogLimits(c, scale, hasData, dataMin, dataMax);
            scale.Ticks = LogTicks(scale);
            scale.K = height / scale.Span;
            return;
        }
        // На логарифмической основной шкале линии сетки неравномерны: дополнительная шкала делится сама.
        int intervals = main.Log ? 0 : UniformIntervals(main);

        bool manual = !axis.AutoLimits && IsFinite(axis.Min) && IsFinite(axis.Max) && axis.Max > axis.Min;
        if (!axis.AutoLimits && !manual)
        {
            c.Error(string.Format(CultureInfo.CurrentCulture,
                "Пределы шкалы {0} заданы неверно: максимум должен быть больше минимума. Временно использованы пределы по данным.",
                scale.Name));
        }

        if (manual)
        {
            scale.Min = axis.Min;
            scale.Max = axis.Max;
            scale.Ticks = (main.Log ? null : TicksAtMainLines(scale, main))
                          ?? TickGenerator.Generate(GridStepMode.Auto, 0, scale.Min, scale.Max, height, scale.Name, axis.Numbers);
        }
        else
        {
            if (!hasData)
            {
                dataMin = 0;
                dataMax = 10;
            }

            if (axis.AlignToGrid && intervals > 0 &&
                NiceScale.AlignedLimits(dataMin, dataMax, intervals, ZeroIndex(main), out double min, out double max, out double step))
            {
                scale.Min = min;
                scale.Max = max;
                scale.Ticks = AlignedTicks(min, step, intervals);
            }
            else
            {
                NiceScale.NiceLimits(dataMin, dataMax, TickGenerator.AutoDivisions(height), out scale.Min, out scale.Max, out _);
                scale.Ticks = TickGenerator.Generate(GridStepMode.Auto, 0, scale.Min, scale.Max, height, scale.Name, axis.Numbers);
            }
        }

        scale.K = height / (scale.Max - scale.Min);
    }

    /// <summary>
    /// Деления шкалы с заданными пределами на тех же уровнях, что линии сетки основной шкалы, —
    /// если значения на этих уровнях получаются «круглыми». Иначе null.
    /// </summary>
    private static AxisTicks TicksAtMainLines(Scale scale, Scale main)
    {
        List<double> lines = main.Ticks.Values;
        if (lines.Count < 2) return null;

        double range = scale.Max - scale.Min, mainRange = main.Max - main.Min;
        double step = (lines[1] - lines[0]) / mainRange * range;
        if (!IsRoundStep(step)) return null;

        double first = scale.Min + (lines[0] - main.Min) / mainRange * range;
        bool multiple = IsWhole(first / step);
        bool round = multiple;
        foreach (int part in new[] { 2, 4, 5, 10 })
        {
            round |= IsWhole(first / (step / part));
        }
        if (!round) return null;

        var ticks = new AxisTicks { Step = step, FromMin = !multiple };
        for (int i = 0; i < lines.Count; i++)
        {
            ticks.Values.Add(NiceScale.Snap(first + i * step, step));
        }
        ticks.Decimals = TickGenerator.DecimalsFor(ticks.Values, step);
        return ticks;
    }

    private static bool IsWhole(double quotient) => Math.Abs(quotient - Math.Round(quotient)) < 1e-6;

    /// <summary>Число интервалов сетки, если её линии идут от края до края поля; иначе 0.</summary>
    private static int UniformIntervals(Scale main)
    {
        List<double> values = main.Ticks.Values;
        if (values.Count < 2) return 0;
        double tolerance = (main.Max - main.Min) * 1e-9;
        if (Math.Abs(values[0] - main.Min) > tolerance || Math.Abs(values[values.Count - 1] - main.Max) > tolerance) return 0;
        return values.Count - 1;
    }

    private static int ZeroIndex(Scale main)
    {
        double tolerance = (main.Max - main.Min) * 1e-9;
        for (int i = 0; i < main.Ticks.Values.Count; i++)
        {
            if (Math.Abs(main.Ticks.Values[i]) <= tolerance) return i;
        }
        return -1;
    }

    private static AxisTicks AlignedTicks(double min, double step, int intervals)
    {
        var ticks = new AxisTicks { Step = step, FromMin = true };
        for (int i = 0; i <= intervals; i++)
        {
            ticks.Values.Add(NiceScale.Snap(min + i * step, step));
        }
        ticks.Decimals = TickGenerator.DecimalsFor(ticks.Values, step);
        return ticks;
    }

    /// <summary>Шаг записывается не более чем тремя значащими цифрами.</summary>
    private static bool IsRoundStep(double step)
    {
        if (!(step > 0) || double.IsInfinity(step)) return false;
        double mantissa = step / NiceScale.Pow10((int)Math.Floor(Math.Log10(step))) * 100;
        return Math.Abs(mantissa - Math.Round(mantissa)) < 1e-6;
    }

    // ------------------------------------------------------------------ подписи делений

    private static bool IsAngleMark(string unit) => unit == "°" || unit == "′" || unit == "″" || unit == "'" || unit == "\"";

    private static void PrepareLabels(Layout l, Scale scale, bool horizontal)
    {
        GraphSettings g = l.G;
        double h = l.HLabel;
        int decimals = scale.Axis.Decimals >= 0 ? scale.Axis.Decimals : scale.Ticks.Decimals;
        bool pi = scale.Axis.Numbers == NumberStyle.PiFractions;

        foreach (double value in scale.Ticks.Values)
        {
            if (scale.Log)
            {
                scale.Texts.Add(new TickText(LogScale.Label(value, g.DecimalComma)));
            }
            else if (pi && NumberFormatter.TryPiFraction(value, out int numerator, out int denominator))
            {
                string top = NumberFormatter.PiNumerator(numerator);
                scale.Texts.Add(denominator == 1
                    ? new TickText(top)
                    : new TickText(TextMarkup.Fraction(top, denominator.ToString(CultureInfo.InvariantCulture)), true));
            }
            else
            {
                scale.Texts.Add(new TickText(NumberFormatter.Format(value, decimals, g.DecimalComma)));
            }
        }

        // Прореживание: не чаще, чем задал пользователь, и не чаще, чем позволяет место.
        double needed;
        if (horizontal)
        {
            double widest = 0;
            foreach (TickText text in scale.Texts) widest = Math.Max(widest, text.Width(h));
            // Соседние числа не должны закрывать друг друга (текст КОМПАС закрывает область шире самих букв).
            needed = widest + TextMetrics.ClearPad * h + 2 * TextMetrics.ClearGapMm;
        }
        else
        {
            double tallest = 0;
            foreach (TickText text in scale.Texts)
            {
                tallest = Math.Max(tallest, TextMetrics.Above(text.Text, h) + TextMetrics.ClearedBelow(text.Text, h));
            }
            needed = Math.Max(1.4 * h, tallest - 0.1);
        }
        if (scale.Log)
        {
            LogLabeled(scale, needed, l.Labels);
        }
        else
        {
            int stride = LabelStride(scale.Ticks, scale.K, scale.Axis.LabelEvery, needed, l.Labels);
            for (int i = 0; i < scale.Ticks.Values.Count; i++)
            {
                if (IsLabeled(scale.Ticks, i, stride)) scale.Labeled.Add(i);
            }
        }

        // Единица измерения между последним и предпоследним числами (Р 50-77-88, п. 5.1).
        string unit = scale.Unit;
        if (l.Labels && unit.Length > 0 && g.Units == UnitPlacement.BetweenNumbers && scale.Labeled.Count > 0)
        {
            int last = scale.Labeled[scale.Labeled.Count - 1];
            if (IsAngleMark(unit))
            {
                // Единицы измерения углов — один раз, у последнего числа шкалы (п. 5.2).
                if (!scale.Texts[last].IsFraction) scale.Texts[last] = new TickText(scale.Texts[last].Text + unit);
            }
            else if (scale.Labeled.Count >= 2)
            {
                int previous = scale.Labeled[scale.Labeled.Count - 2];
                double atLast = scale.Map(scale.Ticks.Values[last]), atPrevious = scale.Map(scale.Ticks.Values[previous]);
                scale.UnitText = unit;

                bool fits;
                if (horizontal)
                {
                    double from = atPrevious + scale.Texts[previous].Width(h) / 2, to = atLast - scale.Texts[last].Width(h) / 2;
                    fits = to - from >= TextMetrics.EstimateWidth(unit, h) + 2 * TextMetrics.ClearPad * h + 3 * TextMetrics.ClearGapMm;
                    scale.UnitAt = (from + to) / 2;
                }
                else
                {
                    fits = Math.Abs(atLast - atPrevious) >= 2.6 * h + 2 * TextMetrics.ClearGapMm;
                    scale.UnitAt = (atLast + atPrevious) / 2;
                }

                if (!fits)
                {
                    // Места нет: единица ставится вместо предпоследнего числа.
                    scale.Skipped = previous;
                    scale.UnitAt = atPrevious;
                }
            }
        }

        double width = scale.UnitText != null ? TextMetrics.EstimateWidth(scale.UnitText, h) : 0;
        foreach (int index in scale.Labeled)
        {
            if (index != scale.Skipped) width = Math.Max(width, scale.Texts[index].Width(h));
        }
        scale.LabelWidth = l.Labels ? width : 0;
    }

    /// <summary>
    /// Какие линии логарифмической шкалы подписать: сначала декады (каждую N-ю, если тесно),
    /// затем 2 и 5 единиц декады — там, где для них есть место.
    /// </summary>
    private static void LogLabeled(Scale scale, double neededMm, bool autoThin)
    {
        List<double> values = scale.Ticks.Values;
        var decades = new List<int>();
        var others = new List<int>();
        for (int i = 0; i < values.Count; i++)
        {
            (LogScale.DecadeOf(values[i]).HasValue ? decades : others).Add(i);
        }

        int stride = Math.Max(1, scale.Axis.LabelEvery);
        if (autoThin && scale.K > 0)
        {
            while (stride < 100 && scale.K * stride < neededMm) stride++;
        }
        var chosen = new List<int>();
        for (int j = 0; j < decades.Count; j++)
        {
            int power = LogScale.DecadeOf(values[decades[j]]).Value;
            if (((power % stride) + stride) % stride == 0) chosen.Add(decades[j]);
        }
        if (chosen.Count == 0 && decades.Count > 0) chosen.Add(decades[0]);

        if (stride == 1)
        {
            foreach (int index in others)
            {
                double at = scale.Map(values[index]);
                bool fits = true;
                foreach (int other in chosen) fits &= Math.Abs(scale.Map(values[other]) - at) >= neededMm;
                if (fits) chosen.Add(index);
            }
        }

        chosen.Sort();
        scale.Labeled.AddRange(chosen);
    }

    private static bool HasZeroAtStart(Scale scale)
    {
        if (scale.Labeled.Count == 0) return false;
        int first = scale.Labeled[0];
        double tolerance = (scale.Max - scale.Min) * 1e-9;
        return first != scale.Skipped && Math.Abs(scale.Ticks.Values[first]) <= tolerance && Math.Abs(scale.Min) <= tolerance;
    }

    /// <summary>
    /// Через сколько линий сетки ставить подпись: не чаще, чем задал пользователь,
    /// и не чаще, чем позволяет место (чтобы числа не налезали друг на друга).
    /// </summary>
    private static int LabelStride(AxisTicks ticks, double mmPerUnit, int userEvery, double neededMm, bool autoThin)
    {
        int stride = Math.Max(1, userEvery);
        if (!autoThin || ticks.Values.Count < 2) return stride;

        double spacing = Math.Abs(ticks.Values[1] - ticks.Values[0]) * mmPerUnit;
        if (!(spacing > 0)) return stride;

        int needed = (int)Math.Ceiling(neededMm / spacing - 1e-9);
        if (needed <= stride) return stride;

        // Прореживаем «круглым» шагом (2, 5, 10, 20…), чтобы подписанные значения остались ровными.
        int nice = 1;
        foreach (int factor in new[] { 1, 2, 5 })
        {
            for (int power = 1; power <= 100000; power *= 10)
            {
                int candidate = factor * power;
                if (candidate >= needed && (nice < needed || candidate < nice)) nice = candidate;
            }
        }
        return Math.Max(stride, nice);
    }

    private static bool IsLabeled(AxisTicks ticks, int index, int stride)
    {
        if (stride <= 1) return true;
        if (ticks.FromMin || !(ticks.Step > 0)) return index % stride == 0;

        // Значения кратны шагу: подписываем «круглые» номера, чтобы ноль всегда попадал в подписи.
        long number = (long)Math.Round(ticks.Values[index] / ticks.Step);
        return number % stride == 0;
    }

    // ------------------------------------------------------------------ сетка, рамка, оси

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;

    private static void BuildGrid(Layout l, bool rightScaleOnFrame)
    {
        GraphSettings g = l.G;
        if (!g.ShowGrid) return;

        var xLines = new List<double>(l.X.Ticks.Values);
        xLines.AddRange(l.X.Minor);
        foreach (double value in xLines)
        {
            double x = l.X.Map(value);
            // Линии, совпадающие с рамкой или осью, не дублируем: в КОМПАС это были бы наложенные объекты.
            if (g.ShowFrame && (Near(x, 0) || Near(x, l.Width))) continue;
            if (!g.ShowFrame && rightScaleOnFrame && Near(x, l.Width)) continue;
            if (g.ShowAxes && Near(x, l.AxisYPos)) continue;
            l.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Grid, A = new Pt(x, 0), B = new Pt(x, l.Height), Style = g.GridStyle });
        }

        Scale main = l.Y[0];
        var yLines = new List<double>(main.Ticks.Values);
        yLines.AddRange(main.Minor);
        foreach (double value in yLines)
        {
            double y = main.Map(value);
            if (g.ShowFrame && (Near(y, 0) || Near(y, l.Height))) continue;
            if (g.ShowAxes && Near(y, l.AxisXPos)) continue;
            l.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Grid, A = new Pt(0, y), B = new Pt(l.Width, y), Style = g.GridStyle });
        }
    }

    /// <summary>
    /// Рамка поля. Стороны, которые служат шкалами, чертятся основной линией (Р 50-77-88, п. 3.2):
    /// если оси не рисуются — вся рамка, иначе только сторона с дополнительной шкалой.
    /// </summary>
    private static void BuildFrame(Layout l, bool rightScaleOnFrame)
    {
        GraphSettings g = l.G;
        double w = l.Width, h = l.Height;
        void Edge(Pt a, Pt b, LineStyleId style) =>
            l.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Frame, A = a, B = b, Style = style });

        if (!g.ShowFrame)
        {
            if (rightScaleOnFrame) Edge(new Pt(w, 0), new Pt(w, h), LineStyleId.Main);
            return;
        }

        LineStyleId style = g.ShowAxes ? LineStyleId.Thin : LineStyleId.Main;

        // Стороны, по которым проходят оси, рисует ось (основной линией).
        if (!(g.ShowAxes && Near(l.AxisXPos, 0))) Edge(new Pt(0, 0), new Pt(w, 0), style);
        if (!(g.ShowAxes && Near(l.AxisYPos, 0))) Edge(new Pt(0, 0), new Pt(0, h), style);
        Edge(new Pt(0, h), new Pt(w, h), style);
        Edge(new Pt(w, 0), new Pt(w, h), rightScaleOnFrame ? LineStyleId.Main : style);
    }

    private static void BuildAxes(Layout l)
    {
        GraphSettings g = l.G;
        if (!g.ShowAxes) return;

        double arrow = g.ShowArrows ? Math.Max(0, g.ArrowLengthMm) : 0;
        SceneTools.Arrow(l.Scene, new Pt(0, l.AxisXPos), new Pt(l.AxisEndX, l.AxisXPos), arrow, LineStyleId.Main, PrimitiveRole.Axis, g.NativeArrows);
        SceneTools.Arrow(l.Scene, new Pt(l.AxisYPos, 0), new Pt(l.AxisYPos, l.AxisEndY), arrow, LineStyleId.Main, PrimitiveRole.Axis, g.NativeArrows);
    }

    // ------------------------------------------------------------------ шкала X

    private static void DrawXScale(Layout l)
    {
        Scale s = l.X;
        GraphSettings g = l.G;
        double h = l.HLabel;

        bool fractions = false;
        foreach (int index in s.Labeled) fractions |= s.Texts[index].IsFraction;

        // Строка чисел. У дроби черта проходит на половине высоты букв, поэтому обычные числа
        // и дроби стоят на одной базовой линии: «0», «π/6», «π» выровнены по черте.
        double rowTop = -(l.Tick + Gap);
        l.XRowBaseline = rowTop - (fractions ? SceneTools.FractionAbove(h) : h);

        // На чертеже текст закрывает то, что под ним: строка опущена так, чтобы не задеть линию шкалы.
        double clearedAbove = 0, clearedBelow = TextMetrics.ClearedBelow("0", h);
        foreach (int index in s.Labeled)
        {
            if (index == s.Skipped) continue;
            clearedAbove = Math.Max(clearedAbove, TextMetrics.ClearedAbove(s.Texts[index].Text, h));
            clearedBelow = Math.Max(clearedBelow, TextMetrics.ClearedBelow(s.Texts[index].Text, h));
        }
        if (l.Labels && clearedAbove > 0)
        {
            l.XRowBaseline = Math.Min(l.XRowBaseline, -l.Tick - clearedAbove - LineClearanceMm);
        }
        l.XRowBottom = l.XRowBaseline - clearedBelow;
        l.XRowCenter = l.XRowBaseline + h / 2;

        foreach (int index in s.Labeled)
        {
            double x = s.Map(s.Ticks.Values[index]);
            if (l.Tick > 0)
            {
                l.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Tick, A = new Pt(x, 0), B = new Pt(x, -l.Tick), Style = LineStyleId.Thin });
            }
            if (!l.Labels || index == s.Skipped) continue;
            if (l.SingleZero && index == s.Labeled[0]) continue;

            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.TickLabel, s.Texts[index].Text, new Pt(x, l.XRowBaseline), h, TextAlign.Center));
        }

        if (l.Labels)
        {
            if (s.UnitText != null)
            {
                l.Scene.Add(SceneTools.Text(g, PrimitiveRole.Unit, s.UnitText, new Pt(s.UnitAt, l.XRowBaseline), h, TextAlign.Center));
            }
            if (l.SingleZero)
            {
                // Общий ноль — слева внизу от точки пересечения шкал.
                l.Scene.Add(SceneTools.Text(g, PrimitiveRole.TickLabel, "0", new Pt(-(l.Tick + Gap), l.XRowBaseline), h, TextAlign.Right));
            }
            l.Bottom = Math.Min(l.Bottom, l.XRowBottom);
        }
        else
        {
            l.XRowBottom = -l.Tick;
            l.Bottom = Math.Min(l.Bottom, -l.Tick);
        }
    }

    // ------------------------------------------------------------------ шкалы Y

    private static void DrawYScales(Layout l)
    {
        double leftCursor = 0, rightCursor = l.Width;
        bool rightFrameTaken = false;

        for (int i = 0; i < l.Y.Count; i++)
        {
            Scale s = l.Y[i];
            bool isMain = i == 0;

            if (isMain)
            {
                s.Position = 0;
            }
            else if (s.Side > 0)
            {
                s.OnFrame = !rightFrameTaken;
                s.Position = rightFrameTaken ? rightCursor + ScaleSpacingMm : l.Width;
                rightFrameTaken = true;
            }
            else
            {
                s.OnFrame = false;
                s.Position = leftCursor - ScaleSpacingMm;
            }
            s.TickLength = s.OnFrame ? l.Tick : Math.Max(l.Tick, ParallelTickMm);

            DrawYScale(l, s, isMain);
            DrawYTitle(l, s, isMain);

            if (s.Side > 0) rightCursor = Math.Max(rightCursor, s.OuterEdge);
            else leftCursor = Math.Min(leftCursor, s.OuterEdge);
        }

        l.Left = Math.Min(l.Left, leftCursor);
        l.Right = Math.Max(l.Right, rightCursor);
    }

    private static void DrawYScale(Layout l, Scale s, bool isMain)
    {
        GraphSettings g = l.G;
        double h = l.HLabel;
        int side = s.Side;

        if (!s.OnFrame)
        {
            // Шкала, параллельная оси: своя линия, деления только штрихами (Р 50-77-88, п. 2.5).
            l.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Axis, A = new Pt(s.Position, 0), B = new Pt(s.Position, l.Height), Style = LineStyleId.Main });
        }

        double labelEdge = s.Position + side * (s.TickLength + Gap);
        TextAlign align = side < 0 ? TextAlign.Right : TextAlign.Left;

        for (int index = 0; index < s.Ticks.Values.Count; index++)
        {
            bool labeled = s.Labeled.Contains(index);
            if (!labeled && s.OnFrame) continue;

            double y = s.Map(s.Ticks.Values[index]);
            if (s.TickLength > 0)
            {
                l.Scene.Add(new LinePrimitive
                {
                    Role = PrimitiveRole.Tick,
                    A = new Pt(s.Position, y),
                    B = new Pt(s.Position + side * s.TickLength, y),
                    Style = LineStyleId.Thin
                });
            }

            if (!labeled || !l.Labels || index == s.Skipped) continue;
            if (isMain && l.SingleZero && index == s.Labeled[0]) continue;

            // Середина букв (и черта дроби) — на уровне деления.
            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.TickLabel, s.Texts[index].Text, new Pt(labelEdge, y - h / 2), h, align));
        }

        if (l.Labels && s.UnitText != null)
        {
            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.Unit, s.UnitText, new Pt(labelEdge, s.UnitAt - h / 2), h, align));
        }

        s.OuterEdge = l.Labels ? labelEdge + side * s.LabelWidth : s.Position + side * s.TickLength;
    }

    /// <summary>
    /// Единица измерения ставится за остриём самостоятельной стрелки. Такие стрелки рисуются у обозначений,
    /// стоящих у середины шкал; без них единица пишется после обозначения через запятую.
    /// </summary>
    private static bool UnitAtArrow(GraphSettings g) =>
        g.Units == UnitPlacement.AtArrow && g.TitlePlacement == AxisTitlePlacement.Centered && g.SeparateArrows;

    /// <summary>Обозначение величины вместе с единицей, если она наносится после запятой.</summary>
    private static string TitleWithUnit(Scale s, GraphSettings g)
    {
        string title = s.Title, unit = s.Unit;
        bool afterTitle = g.Units == UnitPlacement.AfterTitle || g.Units == UnitPlacement.AtArrow && !UnitAtArrow(g);
        if (!afterTitle || unit.Length == 0) return title;
        return title.Length == 0 ? unit : title + ", " + unit;
    }

    /// <summary>Единица измерения над остриём вертикальной стрелки.</summary>
    private static double UnitAboveArrow(Layout l, Scale s, double x, double tipY, double h)
    {
        string unit = s.Unit;
        if (!UnitAtArrow(l.G) || unit.Length == 0) return 0;
        double baseline = tipY + Gap + TextMetrics.ClearedBelow(unit, h);
        l.Scene.Add(SceneTools.Text(l.G, PrimitiveRole.Unit, unit, new Pt(x, baseline), h, TextAlign.Center));
        return TextMetrics.EstimateWidth(unit, h);
    }

    /// <summary>Высота, которую единица над стрелкой добавляет к столбцу «обозначение — стрелка».</summary>
    private static double UnitRoom(GraphSettings g, Scale s, double h) =>
        UnitAtArrow(g) && s.Unit.Length > 0 ? Gap + TextMetrics.ClearedBelow(s.Unit, h) + TextMetrics.ClearedAbove(s.Unit, h) : 0;

    /// <summary>
    /// Наименование (слова) или символ. Символы располагают горизонтально,
    /// наименования — параллельно оси (Р 50-77-88, п. 4.3).
    /// </summary>
    private static bool IsName(string title)
    {
        string plain = TextMarkup.Plain(title);
        int comma = plain.IndexOf(',');
        if (comma >= 0) plain = plain.Substring(0, comma);
        plain = plain.Trim();
        return plain.IndexOf(' ') >= 0 || plain.Length > 10;
    }

    private static void DrawYTitle(Layout l, Scale s, bool isMain)
    {
        GraphSettings g = l.G;
        double h = l.HTitle;
        if (!(h > 0)) return;
        int side = s.Side;

        // Дробь «обозначение / единица» в конце шкалы, над столбцом чисел (п. 5.1).
        if (g.Units == UnitPlacement.Fraction && s.Title.Length > 0 && s.Unit.Length > 0)
        {
            string fraction = TextMarkup.Fraction(s.Title, s.Unit);
            double width = TextMetrics.EstimateWidth(fraction, h);
            double center = s.Position + side * (s.TickLength + Gap + s.LabelWidth / 2);
            double left = center - width / 2;
            // Дробь не должна лечь на продолжение оси со стрелкой.
            if (isMain && g.ShowAxes && g.ShowArrows) left = Math.Min(left, l.AxisYPos - 2 * Gap - width);
            double baseline = l.Height + LineClearanceMm + TextMetrics.ClearedBelow(fraction, h);
            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.AxisTitle, fraction, new Pt(left + width / 2, baseline), h, TextAlign.Center));
            l.Top = Math.Max(l.Top, baseline + TextMetrics.ClearedAbove(fraction, h));
            if (side < 0) s.OuterEdge = Math.Min(s.OuterEdge, left);
            else s.OuterEdge = Math.Max(s.OuterEdge, left + width);
            return;
        }

        string title = TitleWithUnit(s, g);
        if (title.Length == 0) return;

        if (g.TitlePlacement == AxisTitlePlacement.AtEnd)
        {
            double x = isMain ? l.AxisYPos : s.Position;
            // Над концом оси; текст не должен закрыть остриё стрелки.
            double y = (isMain ? l.AxisEndY : l.Height) + Math.Max(1.5 * Gap, TextMetrics.ClearedBelow(title, h) + 0.2);
            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.AxisTitle, title, new Pt(x, y), h, TextAlign.Center));
            l.Top = Math.Max(l.Top, y + TextMetrics.ClearedAbove(title, h));
            return;
        }

        // У середины шкалы, с её внешней стороны (п. 4.2).
        double outer = s.OuterEdge + side * 2 * Gap;
        double middle = l.Height / 2;
        bool arrow = g.SeparateArrows;
        double arrowLength = l.SeparateArrow, arrowGap = 2 * Gap;
        double width0 = TextMetrics.EstimateWidth(title, h);

        double unitRoom = arrow ? UnitRoom(g, s, h) : 0;

        if (IsName(title))
        {
            // Текст повёрнут на 90°: читается снизу вверх, буквы «растут» влево от базовой линии.
            double baselineX = side < 0 ? outer : outer + h;
            double centerY = arrow ? middle - (arrowGap + arrowLength + unitRoom) / 2 : middle;
            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.AxisTitle, title, new Pt(baselineX, centerY), h, TextAlign.Center, 90));
            double extra = 0;
            if (arrow)
            {
                double x = baselineX - h / 2, from = centerY + width0 / 2 + arrowGap;
                SceneTools.Arrow(l.Scene, new Pt(x, from), new Pt(x, from + arrowLength), Math.Min(l.ArrowHead, arrowLength / 2),
                    LineStyleId.Thin, PrimitiveRole.AxisTitle, g.NativeArrows);
                extra = UnitAboveArrow(l, s, x, from + arrowLength, h);
            }
            s.OuterEdge = outer + side * Math.Max(1.3 * h, extra / 2 + h / 2);
            return;
        }

        TextAlign align = side < 0 ? TextAlign.Right : TextAlign.Left;
        double unitWidth = 0;
        if (arrow)
        {
            // Самостоятельная стрелка над обозначением, параллельно оси (п. 1.3).
            double bottom = middle - (h + arrowGap + arrowLength + unitRoom) / 2;
            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.AxisTitle, title, new Pt(outer, bottom), h, align));
            double x = outer + side * width0 / 2, from = bottom + h + arrowGap;
            SceneTools.Arrow(l.Scene, new Pt(x, from), new Pt(x, from + arrowLength), Math.Min(l.ArrowHead, arrowLength / 2),
                LineStyleId.Thin, PrimitiveRole.AxisTitle, g.NativeArrows);
            unitWidth = UnitAboveArrow(l, s, x, from + arrowLength, h);
        }
        else
        {
            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.AxisTitle, title, new Pt(outer, middle - h / 2), h, align));
        }
        s.OuterEdge = outer + side * Math.Max(width0, width0 / 2 + unitWidth / 2);
    }

    private static void DrawXTitle(Layout l)
    {
        GraphSettings g = l.G;
        Scale s = l.X;
        double h = l.HTitle;
        if (!(h > 0)) return;

        if (g.Units == UnitPlacement.Fraction && s.Title.Length > 0 && s.Unit.Length > 0)
        {
            // Дробь в конце шкалы после последнего числа.
            double lastWidth = 0;
            if (l.Labels && s.Labeled.Count > 0) lastWidth = s.Texts[s.Labeled[s.Labeled.Count - 1]].Width(l.HLabel);
            double left = l.Width + lastWidth / 2 + 2 * Gap;
            string fraction = TextMarkup.Fraction(s.Title, s.Unit);
            // Черта дроби — на уровне середины чисел шкалы.
            double fractionBaseline = l.XRowCenter - SceneTools.FractionBar(h);
            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.AxisTitle, fraction, new Pt(left, fractionBaseline), h, TextAlign.Left));
            l.Right = Math.Max(l.Right, left + TextMetrics.EstimateWidth(fraction, h));
            l.Bottom = Math.Min(l.Bottom, fractionBaseline - TextMetrics.ClearedBelow(fraction, h));
            return;
        }

        string title = TitleWithUnit(s, g);
        if (title.Length == 0) return;

        if (g.TitlePlacement == AxisTitlePlacement.AtEnd)
        {
            // Продолжение оси: текст сразу за стрелкой, по высоте — по центру линии оси.
            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.AxisTitle, title,
                new Pt(l.AxisEndX + 1.5 * Gap, l.AxisXPos - h / 2), h, TextAlign.Left));
            l.Right = Math.Max(l.Right, l.AxisEndX + 1.5 * Gap + TextMetrics.EstimateWidth(title, h));
            return;
        }

        // Под строкой чисел; области, которые закрывают тексты, не должны пересекаться.
        double baseline = l.Bottom - Math.Max(1.5 * Gap + h, TextMetrics.ClearedAbove(title, h) + 0.2);
        double width0 = TextMetrics.EstimateWidth(title, h);
        if (g.SeparateArrows)
        {
            double arrowLength = l.SeparateArrow, arrowGap = 2 * Gap;
            // Единица измерения — за остриём стрелки: «φ ⟶ рад».
            string unit = UnitAtArrow(g) ? s.Unit : "";
            double unitWidth = unit.Length > 0 ? 1.5 * Gap + TextMetrics.EstimateWidth(unit, h) : 0;
            double start = l.Width / 2 - (width0 + arrowGap + arrowLength + unitWidth) / 2;
            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.AxisTitle, title, new Pt(start + width0, baseline), h, TextAlign.Right));
            double from = start + width0 + arrowGap, y = baseline + h / 2;
            SceneTools.Arrow(l.Scene, new Pt(from, y), new Pt(from + arrowLength, y), Math.Min(l.ArrowHead, arrowLength / 2),
                LineStyleId.Thin, PrimitiveRole.AxisTitle, g.NativeArrows);
            if (unit.Length > 0)
            {
                l.Scene.Add(SceneTools.Text(g, PrimitiveRole.Unit, unit, new Pt(from + arrowLength + 1.5 * Gap, baseline), h, TextAlign.Left));
            }
            l.XTitleRight = from + arrowLength + unitWidth;
        }
        else
        {
            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.AxisTitle, title, new Pt(l.Width / 2, baseline), h, TextAlign.Center));
            l.XTitleRight = l.Width / 2 + width0 / 2;
        }
        l.XTitleRowCenter = baseline + h / 2;
        l.Bottom = baseline - TextMetrics.ClearedBelow(title, h);
    }

    // ------------------------------------------------------------------ масштабы шкал

    /// <summary>Индекс у буквы μ: символ величины без собственных индексов и единицы («V_q, м» → «Vq»).</summary>
    private static string FactorIndex(Scale s)
    {
        string plain = TextMarkup.Plain(s.Title);
        int comma = plain.IndexOf(',');
        if (comma >= 0) plain = plain.Substring(0, comma);
        plain = plain.Replace(" ", "").Replace("{", "").Replace("}", "").Trim();
        return plain.Length == 0 || plain.Length > 8 ? s.Name.ToLowerInvariant() : plain;
    }

    /// <summary>
    /// Надписи масштабов: «μ = 165 мм/м» — сколько миллиметров чертежа приходится на единицу величины.
    /// Шкалы Y подписываются над полем, шкала X — под полем справа.
    /// </summary>
    private static void DrawScaleFactors(Layout l)
    {
        GraphSettings g = l.G;
        double h = l.HTitle;
        if (!g.ShowScaleFactors || !(h > 0)) return;

        // Одна надпись: «μ» с индексом, значение и дробь «мм / единица».
        string Caption(Scale s) => "μ_{" + FactorIndex(s) + "} = " + NumberFormatter.Significant(s.K, 3, g.DecimalComma) + " " +
                                   TextMarkup.Fraction("мм", s.Unit.Length > 0 ? s.Unit : "–");

        string widest = Caption(l.Y[0]);
        double baseline = l.Top + LineClearanceMm + TextMetrics.ClearedBelow(widest, h);
        double left = 0, right = l.Width;
        foreach (Scale s in l.Y)
        {
            // У логарифмической шкалы нет постоянного числа миллиметров на единицу величины.
            if (s.Log) continue;
            string caption = Caption(s);
            double width = TextMetrics.EstimateWidth(caption, h);
            if (s.Side < 0)
            {
                l.Scene.Add(SceneTools.Text(g, PrimitiveRole.Note, caption, new Pt(left, baseline), h, TextAlign.Left));
                left += width + 6 * Gap;
            }
            else
            {
                l.Scene.Add(SceneTools.Text(g, PrimitiveRole.Note, caption, new Pt(right, baseline), h, TextAlign.Right));
                right -= width + 6 * Gap;
            }
        }
        l.Top = baseline + TextMetrics.ClearedAbove(widest, h);

        if (!l.X.Log)
        {
            string caption = Caption(l.X);
            double width = TextMetrics.EstimateWidth(caption, h) + TextMetrics.ClearPad * h + TextMetrics.ClearGapMm;
            double above = TextMetrics.ClearedAbove(caption, h);

            // Если обозначение оси X стоит у середины шкалы и места хватает — в одну строку с ним;
            // числитель дроби при этом не должен закрыть числа шкалы.
            double y;
            if (!double.IsNaN(l.XTitleRight) && l.Width - width > l.XTitleRight + 2 * Gap)
            {
                y = Math.Min(l.XTitleRowCenter - h / 2, l.XRowBottom - 0.2 - above);
            }
            else
            {
                y = l.Bottom - 0.2 - above;
            }
            l.Bottom = Math.Min(l.Bottom, y - TextMetrics.ClearedBelow(caption, h));
            l.Scene.Add(SceneTools.Text(g, PrimitiveRole.Note, caption, new Pt(l.Width, y), h, TextAlign.Right));
        }
    }

    // ------------------------------------------------------------------ кривые и маркеры

    private static List<BuiltCurve> DrawCurves(Layout l)
    {
        BuildContext c = l.C;
        GraphSettings g = l.G;
        RectD rect = l.Scene.PlotRect;
        var built = new List<BuiltCurve>();

        foreach (SeriesPoints series in c.Data.Series)
        {
            CurveSettings curve = c.Project.Curves[series.CurveIndex];
            if (curve == null || !curve.Visible || series.Points.Count == 0) continue;

            Scale scale = l.Y[AxisIndex(curve, l.Y.Count)];
            int? color = ColorValue.Parse(curve.Color);
            var info = new BuiltCurve { CurveIndex = series.CurveIndex, Settings = curve, Color = color };
            built.Add(info);

            bool characteristic = curve.MarkerPlacement == MarkerPlacement.Characteristic;
            double zeroLevel = scale.Min <= 0 && scale.Max >= 0 ? scale.Map(0) : double.NaN;
            var dataPoints = new List<Pt>(series.Points.Count);
            var markPoints = new List<Pt>();

            List<List<Pt>> runs = CurveTools.SplitAtJumps(series.Points, g.DashedJumps, l.X.Max - l.X.Min);
            if (l.X.Log || scale.Log)
            {
                runs = AcceptedRuns(runs, l.X, scale, out int rejected);
                if (rejected > 0)
                {
                    c.Warn(string.Format(CultureInfo.CurrentCulture,
                        "Кривая «{0}»: точек с нулевыми или отрицательными значениями на логарифмической шкале — {1}, они не построены.",
                        curve.Name, rejected));
                }
            }
            bool hasPrevious = false;
            Pt previousEnd = default;

            foreach (List<Pt> run in runs)
            {
                var mapped = new List<Pt>(run.Count);
                foreach (Pt p in run)
                {
                    mapped.Add(new Pt(l.X.Map(p.X), scale.Map(p.Y)));
                }
                dataPoints.AddRange(mapped);

                if (hasPrevious)
                {
                    // Разрыв функции: вертикальная штриховая линия между участками.
                    Pt a = previousEnd, b = mapped[0];
                    if (Clipper.ClipSegment(rect, ref a, ref b) && Pt.Distance(a, b) > 1e-9)
                    {
                        l.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Jump, Color = color, A = a, B = b, Style = LineStyleId.Dashed });
                    }
                }
                hasPrevious = true;
                previousEnd = mapped[mapped.Count - 1];

                List<Pt> line = CurveTools.Decimate(c, mapped);
                if (line.Count < 2)
                {
                    if (characteristic) markPoints.AddRange(line);
                    continue;
                }

                if (curve.Mode == CurveMode.Smooth)
                {
                    // Сначала сглаживаем по всем точкам, потом обрезаем: так форма у границы не искажается.
                    List<Pt> nodes = Interpolation.RemoveConsecutiveDuplicates(line);
                    bool periodic = g.SmoothPeriodic && runs.Count == 1 && Interpolation.LooksPeriodic(nodes);
                    List<CubicBezier> segments = Interpolation.Smooth(nodes, periodic);
                    foreach (List<CubicBezier> chain in Clipper.ClipBezierChain(rect, segments))
                    {
                        BezierPrimitive spline = BezierPrimitive.FromSegments(chain, curve.LineStyle, PrimitiveRole.Curve);
                        spline.Color = color;
                        l.Scene.Add(spline);
                        c.Result.CurveVertexCount += chain.Count + 1;
                        info.Pieces.Add(CurveTools.Sample(chain));
                    }
                    if (characteristic) markPoints.AddRange(CurveTools.CharacteristicPoints(nodes, segments, zeroLevel));
                }
                else
                {
                    foreach (List<Pt> piece in Clipper.ClipPolyline(rect, line))
                    {
                        l.Scene.Add(new PolylinePrimitive { Role = PrimitiveRole.Curve, Color = color, Points = piece, Style = curve.LineStyle });
                        c.Result.CurveVertexCount += piece.Count;
                        info.Pieces.Add(piece);
                    }
                    if (characteristic) markPoints.AddRange(CurveTools.CharacteristicPoints(line, null, zeroLevel));
                }
            }

            if (curve.Marker == MarkerShape.None || !(curve.MarkerSizeMm > 0)) continue;

            if (characteristic)
            {
                var placed = new List<Pt>();
                foreach (Pt p in markPoints)
                {
                    if (!rect.Contains(p, 1e-9)) continue;
                    bool duplicate = false;
                    foreach (Pt other in placed) duplicate |= Pt.Distance(other, p) < 0.3;
                    if (duplicate) continue;
                    placed.Add(p);
                    SceneTools.Marker(l.Scene, curve.Marker, p, curve.MarkerSizeMm, PrimitiveRole.Marker, color);
                }
            }
            else
            {
                int every = Math.Max(1, curve.MarkerEvery);
                for (int i = 0; i < dataPoints.Count; i += every)
                {
                    if (rect.Contains(dataPoints[i], 1e-9))
                    {
                        SceneTools.Marker(l.Scene, curve.Marker, dataPoints[i], curve.MarkerSizeMm, PrimitiveRole.Marker, color);
                    }
                }
            }
        }
        return built;
    }

    /// <summary>
    /// Участки кривой без точек, которые нельзя отложить на логарифмической шкале. Участки, разделённые
    /// такими точками, становятся отдельными (как при разрыве функции).
    /// </summary>
    private static List<List<Pt>> AcceptedRuns(List<List<Pt>> runs, Scale x, Scale y, out int rejected)
    {
        rejected = 0;
        var result = new List<List<Pt>>();
        foreach (List<Pt> run in runs)
        {
            var current = new List<Pt>();
            foreach (Pt p in run)
            {
                if (x.Accepts(p.X) && y.Accepts(p.Y))
                {
                    current.Add(p);
                    continue;
                }
                rejected++;
                if (current.Count > 0) result.Add(current);
                current = new List<Pt>();
            }
            if (current.Count > 0) result.Add(current);
        }
        return result;
    }

    // ------------------------------------------------------------------ легенда

    private static void BuildLegend(Layout l)
    {
        GraphSettings g = l.G;
        if (!g.ShowLegend || !(g.LabelHeightMm > 0)) return;

        double h = g.LabelHeightMm;
        double pitch = Math.Max(h * 1.8, 5);
        // Справа от поля; если там стоят шкалы — за ними.
        double x0 = (l.Y.Count > 1 ? l.Right : l.Width) + 6 * Gap;
        DrawLegend(l.C, x0, l.Height, h, pitch);
    }

    /// <summary>Легенда: образец линии и название каждой видимой кривой, сверху вниз от точки (x0, top).</summary>
    public static void DrawLegend(BuildContext c, double x0, double top, double h, double pitch)
    {
        GraphSettings g = c.G;
        int row = 0;
        foreach (CurveSettings curve in c.Project.Curves)
        {
            if (curve == null || !curve.Visible) continue;

            double y = top - pitch * (row + 0.5);
            row++;
            int? color = ColorValue.Parse(curve.Color);

            c.Scene.Add(new LinePrimitive
            {
                Role = PrimitiveRole.Legend,
                Color = color,
                A = new Pt(x0, y),
                B = new Pt(x0 + LegendSampleMm, y),
                Style = curve.LineStyle
            });
            if (curve.Marker != MarkerShape.None && curve.MarkerSizeMm > 0)
            {
                SceneTools.Marker(c.Scene, curve.Marker, new Pt(x0 + LegendSampleMm / 2, y), curve.MarkerSizeMm, PrimitiveRole.Legend, color);
            }
            string name = string.IsNullOrWhiteSpace(curve.Name) ? "Y" : curve.Name.Trim();
            c.Scene.Add(SceneTools.Text(g, PrimitiveRole.Legend, name, new Pt(x0 + LegendSampleMm + 2 * Gap, y - h / 2), h, TextAlign.Left));
        }
    }
}
