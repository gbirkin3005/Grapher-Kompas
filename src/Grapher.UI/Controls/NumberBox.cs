using System;
using System.Drawing;
using System.Windows.Forms;
using Grapher.Core.Numerics;
using Grapher.Core.Parsing;

namespace Grapher.UI.Controls;

/// <summary>
/// Поле ввода числа. Принимает и точку, и запятую; неверный ввод подсвечивается,
/// а последнее корректное значение сохраняется.
/// </summary>
public sealed class NumberBox : TextBox
{
    private static readonly Color ErrorColor = Color.FromArgb(255, 220, 220);

    private double _value;
    private bool _settingText;

    public NumberBox()
    {
        TextAlign = HorizontalAlignment.Left;
        Width = 70;
        Text = "0";
    }

    public double Minimum { get; set; } = double.NegativeInfinity;
    public double Maximum { get; set; } = double.PositiveInfinity;

    /// <summary>Нижняя граница не включается (значение должно быть строго больше).</summary>
    public bool ExclusiveMinimum { get; set; }

    /// <summary>Сколько знаков после запятой показывать.</summary>
    public int DisplayDecimals { get; set; } = 6;

    /// <summary>Значение изменено пользователем на новое корректное число.</summary>
    public event EventHandler ValueChanged;

    /// <summary>Последнее корректное значение.</summary>
    public double Value => _value;

    /// <summary>В поле сейчас введено корректное число.</summary>
    public bool IsValid { get; private set; } = true;

    /// <summary>Задаёт значение из программы; событие <see cref="ValueChanged"/> не вызывается.</summary>
    public void SetValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;

        // Пока пользователь печатает в этом поле, текст не трогаем, если число то же самое.
        if (Focused && IsValid && Math.Abs(_value - value) <= 1e-12 * Math.Max(1, Math.Abs(value)))
        {
            _value = value;
            return;
        }

        _value = value;
        ShowValue();
    }

    private void ShowValue()
    {
        _settingText = true;
        try
        {
            Text = NumberFormatter.ForInput(_value, DisplayDecimals);
            IsValid = true;
            BackColor = ReadOnly ? SystemColors.Control : SystemColors.Window;
        }
        finally
        {
            _settingText = false;
        }
    }

    private bool InRange(double value)
    {
        if (value > Maximum) return false;
        return ExclusiveMinimum ? value > Minimum : value >= Minimum;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (_settingText || ReadOnly) return;

        bool ok = NumberParser.Parse(Text, out double parsed) == NumberParseStatus.Ok && InRange(parsed);
        IsValid = ok;
        BackColor = ok ? SystemColors.Window : ErrorColor;
        if (!ok || parsed == _value) return;

        _value = parsed;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        // Неверный ввод откатывается к последнему корректному значению.
        if (!ReadOnly) ShowValue();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !ReadOnly)
        {
            ShowValue();
            SelectAll();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        BeginInvoke((Action)SelectAll);
    }
}
