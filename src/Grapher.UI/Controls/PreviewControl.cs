using System.Drawing;
using System.Windows.Forms;
using Grapher.Core.Scene;
using Grapher.UI.Rendering;

namespace Grapher.UI.Controls;

/// <summary>Область предпросмотра графика.</summary>
public sealed class PreviewControl : Control
{
    private readonly PreviewRenderer _renderer = new PreviewRenderer();
    private GraphScene _scene;
    private string _message;

    public PreviewControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
    }

    public PreviewRenderer Renderer => _renderer;

    /// <summary>Сцена для показа (уже повёрнутая на угол размещения).</summary>
    public GraphScene Scene
    {
        get => _scene;
        set
        {
            _scene = value;
            Invalidate();
        }
    }

    /// <summary>Текст поверх пустой области, например «Нет данных».</summary>
    public string Message
    {
        get => _message;
        set
        {
            _message = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);

        var area = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
        if (_scene != null)
        {
            _renderer.Render(g, _scene, area);
        }

        if (!string.IsNullOrEmpty(_message))
        {
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near })
            using (var brush = new SolidBrush(Color.FromArgb(150, 60, 60, 60)))
            {
                area.Inflate(-8, -8);
                g.DrawString(_message, Font, brush, area, format);
            }
        }

        using (var pen = new Pen(SystemColors.ControlDark))
        {
            g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }
}
