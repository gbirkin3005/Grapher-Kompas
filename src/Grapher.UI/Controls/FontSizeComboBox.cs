using System;
using System.Windows.Forms;
using Grapher.Core.Numerics;
using Grapher.Core.Parsing;

namespace Grapher.UI.Controls;

/// <summary>
/// Поле высоты шрифта со списком стандартных размеров по ГОСТ 2.304 (1,8…40 мм).
/// Свой размер ввести можно: список подсказывает стандарт, но не ограничивает.
/// Интерфейс — как у <see cref="NumberBox"/>: Value, SetValue, ValueChanged.
/// </summary>
public sealed class FontSizeComboBox : ComboBox
{
    private static readonly double[] StandardSizes = { 1.8, 2.5, 3.5, 5, 7, 10, 14, 20, 28, 40 };

    private double _value;
    private bool _setting;

    public event EventHandler ValueChanged;

    public FontSizeComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDown;
        foreach (double size in StandardSizes) Items.Add(Format(size));
        TextChanged += (s, e) => OnEdited();
    }

    public double Value => _value;

    public void SetValue(double value)
    {
        _value = value;
        _setting = true;
        // Текст пункта списка: при стандартном размере список подсветит его сам.
        Text = Format(value);
        _setting = false;
    }

    private void OnEdited()
    {
        if (_setting) return;
        // Отрицательная высота бессмысленна; 0 означает «как у чисел шкал» у подписей точек.
        if (NumberParser.Parse(Text, out double value) == NumberParseStatus.Ok && value >= 0 && value != _value)
        {
            _value = value;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string Format(double value) => NumberFormatter.ForInput(value, 2);
}
