using System;
using System.Drawing;
using System.Windows.Forms;

namespace Grapher.UI.Controls;

/// <summary>
/// Кнопка выбора цвета с образцом. Значение null означает «цвет не задан»
/// (линия рисуется цветом стиля КОМПАС) — образец тогда перечёркнут.
/// </summary>
public sealed class ColorSwatchButton : Button
{
    private Color? _value;

    public ColorSwatchButton()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TextImageRelation = TextImageRelation.ImageBeforeText;
        ImageAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(4, 1, 6, 1);
        Margin = new Padding(3, 2, 3, 2);
        Anchor = AnchorStyles.Left;
    }

    /// <summary>Выбранный цвет; null — цвет не задан.</summary>
    public Color? Value
    {
        get => _value;
        set
        {
            _value = value;
            UpdateSwatch();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateSwatch();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateSwatch();
    }

    /// <summary>Перерисовывает образец; его размер следует за шрифтом, то есть и за масштабом экрана.</summary>
    private void UpdateSwatch()
    {
        int size = Math.Max(10, Font.Height - 2);
        var bitmap = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            var box = new Rectangle(0, 0, size - 1, size - 1);
            if (_value.HasValue)
            {
                using (var brush = new SolidBrush(_value.Value))
                {
                    g.FillRectangle(brush, box);
                }
            }
            else
            {
                g.FillRectangle(Brushes.White, box);
                g.DrawLine(Pens.Gray, 0, size - 1, size - 1, 0);
            }
            g.DrawRectangle(Pens.DimGray, box);
        }

        Image old = Image;
        Image = bitmap;
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Image?.Dispose();
        base.Dispose(disposing);
    }
}
