using System.Collections.Generic;
using System.Linq;
using Grapher.Core.Model;
using Grapher.Core.Numerics;
using Xunit;

namespace Grapher.Core.Tests;

public class NiceScaleTests
{
    [Theory]
    [InlineData(100, 10, 10)]
    [InlineData(10, 10, 1)]
    [InlineData(99, 10, 10)]
    [InlineData(7, 5, 2)]
    [InlineData(0.37, 5, 0.1)]
    [InlineData(1234, 6, 500)]
    [InlineData(0.0042, 4, 0.002)]
    public void Step_is_1_2_or_5_times_power_of_ten(double range, int target, double expected)
    {
        Assert.Equal(expected, NiceScale.NiceStep(range, target), 12);
    }

    [Theory]
    [InlineData(0, 10, 10, 0, 10, 1)]
    [InlineData(0.3, 9.7, 10, 0, 10, 1)]
    [InlineData(-3.2, 7.9, 5, -5, 10, 5)]
    [InlineData(0, 100, 10, 0, 100, 10)]
    [InlineData(1.0101, 100, 10, 0, 100, 10)]
    [InlineData(203, 487, 6, 200, 500, 50)]
    [InlineData(0.012, 0.047, 5, 0.01, 0.05, 0.01)]
    public void Limits_are_rounded_outwards_to_step_multiples(double dataMin, double dataMax, int target,
        double expectedMin, double expectedMax, double expectedStep)
    {
        NiceScale.NiceLimits(dataMin, dataMax, target, out double min, out double max, out double step);

        Assert.Equal(expectedStep, step, 12);
        Assert.Equal(expectedMin, min, 12);
        Assert.Equal(expectedMax, max, 12);
    }

    [Theory]
    [InlineData(0, 360, 10, 30)]       // угол поворота: 0, 30, 60 … 360, а не 0 … 400 с шагом 50
    [InlineData(0, 360, 18, 20)]
    [InlineData(-180, 180, 10, 30)]
    [InlineData(0, 720, 10, 60)]
    [InlineData(0, 24, 10, 2)]
    [InlineData(-0.25, 0.25, 10, 0.05)]
    public void Round_data_bounds_are_kept_as_limits(double dataMin, double dataMax, int target, double expectedStep)
    {
        NiceScale.NiceLimits(dataMin, dataMax, target, out double min, out double max, out double step);

        Assert.Equal(dataMin, min, 12);
        Assert.Equal(dataMax, max, 12);
        Assert.Equal(expectedStep, step, 12);
    }

    [Theory]
    [InlineData(0, 360, 10, 30)]
    [InlineData(0, 50, 10, 5)]
    [InlineData(0, 900, 10, 100)]      // обычные шаги 1-2-5 предпочтительнее, если подходят
    [InlineData(0.3, 9.7, 10, 1)]      // «некруглые» пределы — обычный красивый шаг
    [InlineData(0, 359, 10, 50)]
    public void Automatic_step_fits_the_limits_when_possible(double min, double max, int target, double expected)
    {
        Assert.Equal(expected, NiceScale.AutoStep(min, max, target), 12);
    }

    [Fact]
    public void Constant_data_gets_a_non_empty_range()
    {
        NiceScale.NiceLimits(5, 5, 10, out double min, out double max, out _);

        Assert.True(min < 5 && max > 5);
    }

    [Fact]
    public void Multiples_have_no_binary_noise()
    {
        List<double> values = NiceScale.Multiples(0, 1, 0.1, 100);

        Assert.Equal(11, values.Count);
        Assert.Equal(0.3, values[3]);   // точное равенство: не 0.30000000000000004
        Assert.Equal(0.7, values[7]);
        Assert.Equal(1.0, values[10]);
    }

    [Fact]
    public void Multiples_cover_negative_values_and_zero()
    {
        Assert.Equal(new[] { -1, -0.5, 0, 0.5, 1 }, NiceScale.Multiples(-1, 1, 0.5, 100));
        Assert.Equal(new[] { 10.0, 20, 30 }, NiceScale.Multiples(5, 35, 10, 100));
    }
}

public class TickGeneratorTests
{
    [Fact]
    public void Step_in_millimetres_gives_ten_by_ten_grid_for_the_sample()
    {
        // Контрольный пример: поле 100×100 мм, шаг сетки 10 мм, X от 0 до 100, Y от 0 до 10.
        AxisTicks x = TickGenerator.Generate(GridStepMode.Millimeters, 10, 0, 100, 100, "X");
        AxisTicks y = TickGenerator.Generate(GridStepMode.Millimeters, 10, 0, 10, 100, "Y");

        Assert.Equal(Enumerable.Range(0, 11).Select(i => i * 10.0), x.Values);
        Assert.Equal(Enumerable.Range(0, 11).Select(i => (double)i), y.Values);
        Assert.Equal(10, x.Step, 12);
        Assert.Equal(1, y.Step, 12);
        Assert.Equal(0, x.Decimals);
        Assert.Equal(0, y.Decimals);
        Assert.Null(x.Warning);
    }

    [Fact]
    public void Step_in_axis_units_uses_multiples_of_the_step()
    {
        AxisTicks ticks = TickGenerator.Generate(GridStepMode.Units, 0.25, -0.3, 1.1, 100, "X");

        Assert.Equal(new[] { -0.25, 0, 0.25, 0.5, 0.75, 1 }, ticks.Values);
        Assert.Equal(2, ticks.Decimals);
        Assert.False(ticks.FromMin);
    }

    [Fact]
    public void Number_of_divisions_splits_the_range_evenly()
    {
        AxisTicks ticks = TickGenerator.Generate(GridStepMode.Divisions, 4, 2, 3, 80, "Y");

        Assert.Equal(new[] { 2, 2.25, 2.5, 2.75, 3 }, ticks.Values);
        Assert.Equal(2, ticks.Decimals);
        Assert.True(ticks.FromMin);
    }

    [Fact]
    public void Automatic_step_targets_about_ten_millimetres()
    {
        AxisTicks ticks = TickGenerator.Generate(GridStepMode.Auto, 0, 0, 100, 100, "X");

        Assert.Equal(10, ticks.Step, 12);
        Assert.Equal(11, ticks.Values.Count);
    }

    [Fact]
    public void Automatic_step_for_full_turn_is_thirty_degrees()
    {
        AxisTicks ticks = TickGenerator.Generate(GridStepMode.Auto, 0, 0, 360, 100, "X");

        Assert.Equal(30, ticks.Step, 12);
        Assert.Equal(13, ticks.Values.Count);
        Assert.Equal(360, ticks.Values[12]);
    }

    [Theory]
    [InlineData(GridStepMode.Units, 0)]
    [InlineData(GridStepMode.Units, -5)]
    [InlineData(GridStepMode.Millimeters, 0)]
    [InlineData(GridStepMode.Divisions, 0)]
    [InlineData(GridStepMode.Units, 0.0001)]      // сотни тысяч линий
    [InlineData(GridStepMode.Millimeters, 0.01)]
    public void Invalid_step_falls_back_to_automatic_with_a_warning(GridStepMode mode, double step)
    {
        AxisTicks ticks = TickGenerator.Generate(mode, step, 0, 100, 100, "X");

        Assert.NotNull(ticks.Warning);
        Assert.Equal(10, ticks.Step, 12);
        Assert.Equal(11, ticks.Values.Count);
    }

    [Fact]
    public void Uneven_step_keeps_three_significant_digits()
    {
        AxisTicks ticks = TickGenerator.Generate(GridStepMode.Divisions, 3, 0, 100, 100, "X");

        Assert.Equal(4, ticks.Values.Count);
        Assert.Equal(1, ticks.Decimals);    // 33,3 — 66,7 — 100,0
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(0.25, 2)]
    [InlineData(2, 0)]
    [InlineData(0.001, 3)]
    public void Decimals_follow_the_step(double step, int expected)
    {
        Assert.Equal(expected, TickGenerator.DecimalsFor(NiceScale.Multiples(0, step * 4, step, 100), step));
    }
}

public class ColorValueTests
{
    [Theory]
    [InlineData("#FF0000", 0xFF0000)]
    [InlineData("00a0ff", 0x00A0FF)]
    [InlineData("  #102030 ", 0x102030)]
    public void Parses_hex_colours(string text, int expected)
    {
        Assert.True(ColorValue.TryParse(text, out int rgb));
        Assert.Equal(expected, rgb);
        Assert.Equal(expected, ColorValue.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("#GG0000")]
    [InlineData("red")]
    public void Rejects_anything_else(string text)
    {
        Assert.False(ColorValue.TryParse(text, out _));
        Assert.Null(ColorValue.Parse(text));
    }

    [Fact]
    public void Formats_and_converts_to_windows_byte_order()
    {
        Assert.Equal("#0A0B0C", ColorValue.Format(0x0A0B0C));
        Assert.Equal(0x0A0B0C, ColorValue.FromRgb(10, 11, 12));
        // КОМПАС и Windows хранят цвет как 0xBBGGRR.
        Assert.Equal(0x0000FF, ColorValue.ToBgr(0xFF0000));
        Assert.Equal(0xFF8000, ColorValue.ToBgr(0x0080FF));
    }
}

public class NumberFormatterTests
{
    [Theory]
    [InlineData(1.5, 1, true, "1,5")]
    [InlineData(1.5, 1, false, "1.5")]
    [InlineData(10, 0, true, "10")]
    [InlineData(2.5, 0, true, "3")]
    [InlineData(0.125, 2, false, "0.13")]
    [InlineData(-0.0001, 2, false, "0.00")]       // без «-0,00»
    [InlineData(-12.5, 2, true, "-12,50")]
    [InlineData(1000000, 0, true, "1000000")]
    public void Formats_tick_labels(double value, int decimals, bool comma, string expected)
    {
        Assert.Equal(expected, NumberFormatter.Format(value, decimals, comma));
    }
}

public class GraphSizeSolverTests
{
    [Fact]
    public void Coefficient_is_millimetres_per_axis_unit()
    {
        Assert.Equal(1, GraphSizeSolver.Coefficient(100, 0, 100), 12);
        Assert.Equal(10, GraphSizeSolver.Coefficient(100, 0, 10), 12);
        Assert.Equal(2.5, GraphSizeSolver.Coefficient(50, -10, 10), 12);
        Assert.Equal(0, GraphSizeSolver.Coefficient(100, 5, 5));
    }

    [Fact]
    public void Size_and_coefficient_are_inverse_of_each_other()
    {
        double k = GraphSizeSolver.Coefficient(137.5, 20, 75);

        Assert.Equal(137.5, GraphSizeSolver.Size(k, 20, 75), 9);
    }

    [Fact]
    public void Changing_size_keeps_the_other_axis_when_scales_are_independent()
    {
        double w = 100, h = 80;

        GraphSizeSolver.Solve(SizeDriver.Width, 200, 100, 10, false, ref w, ref h);

        Assert.Equal(200, w);
        Assert.Equal(80, h);
    }

    [Fact]
    public void Changing_coefficient_updates_the_size()
    {
        double w = 100, h = 80;

        GraphSizeSolver.Solve(SizeDriver.ScaleX, 2, 100, 10, false, ref w, ref h);
        GraphSizeSolver.Solve(SizeDriver.ScaleY, 5, 100, 10, false, ref w, ref h);

        Assert.Equal(200, w, 12);
        Assert.Equal(50, h, 12);
    }

    [Theory]
    [InlineData(SizeDriver.Width, 100, 100, 10)]
    [InlineData(SizeDriver.Height, 50, 500, 50)]
    [InlineData(SizeDriver.ScaleX, 2, 200, 20)]
    [InlineData(SizeDriver.ScaleY, 3, 300, 30)]
    public void Equal_scale_adjusts_the_other_axis(SizeDriver driver, double value, double expectedW, double expectedH)
    {
        double w = 1, h = 1;

        GraphSizeSolver.Solve(driver, value, 100, 10, true, ref w, ref h);

        Assert.Equal(expectedW, w, 9);
        Assert.Equal(expectedH, h, 9);
        Assert.Equal(GraphSizeSolver.Coefficient(w, 0, 100), GraphSizeSolver.Coefficient(h, 0, 10), 9);
    }

    [Fact]
    public void Invalid_input_is_ignored()
    {
        double w = 100, h = 80;

        GraphSizeSolver.Solve(SizeDriver.Width, -5, 100, 10, true, ref w, ref h);
        GraphSizeSolver.Solve(SizeDriver.ScaleX, double.NaN, 100, 10, true, ref w, ref h);

        Assert.Equal(100, w);
        Assert.Equal(80, h);
    }
}
