using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grapher.Core.Model;

namespace Grapher.UI.Controls;

/// <summary>Список стилей линий КОМПАС с образцом линии, как в окне FT Draw.</summary>
public sealed class LineStyleComboBox : ComboBox
{
    public LineStyleComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        foreach (LineStyleInfo info in LineStyles.All)
        {
            Items.Add(info);
        }
        SelectedIndex = 0;
    }

    private Color? _sampleColor;

    /// <summary>Цвет образца линии; null — цвет текста списка.</summary>
    public Color? SampleColor
    {
        get => _sampleColor;
        set
        {
            _sampleColor = value;
            Invalidate();
        }
    }

    public LineStyleId SelectedStyle
    {
        get => SelectedItem is LineStyleInfo info ? info.Id : LineStyleId.Main;
        set
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (((LineStyleInfo)Items[i]).Id == value)
                {
                    SelectedIndex = i;
                    return;
                }
            }
        }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= Items.Count) return;

        var info = (LineStyleInfo)Items[e.Index];
        Color color = (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : ForeColor;
        Rectangle bounds = e.Bounds;
        int sampleWidth = Math.Min(bounds.Width / 3, bounds.Height * 3);
        float y = bounds.Top + bounds.Height / 2f;

        // Образец: толщина и штрихи условно увеличены, чтобы стили различались в строке списка.
        float width = info.WidthMm >= 0.9 ? 3f : info.WidthMm >= 0.4 ? 2f : 1f;
        SmoothingMode saved = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using (var pen = new Pen(_sampleColor ?? color, width))
        {
            if (info.DashPatternMm != null)
            {
                var pattern = new float[info.DashPatternMm.Length];
                for (int i = 0; i < pattern.Length; i++)
                {
                    pattern[i] = Math.Max(1f, (float)info.DashPatternMm[i] * 1.2f / width);
                }
                pen.DashPattern = pattern;
            }
            e.Graphics.DrawLine(pen, bounds.Left + 4, y, bounds.Left + 4 + sampleWidth, y);
        }
        e.Graphics.SmoothingMode = saved;

        var textBounds = new Rectangle(bounds.Left + sampleWidth + 10, bounds.Top, bounds.Width - sampleWidth - 10, bounds.Height);
        TextRenderer.DrawText(e.Graphics, info.Name, e.Font, textBounds, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}
