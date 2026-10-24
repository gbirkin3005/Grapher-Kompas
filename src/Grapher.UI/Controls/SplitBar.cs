using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Grapher.UI.Controls;

/// <summary>
/// Полоса-разделитель с «ручкой» посередине, как между двумя окнами «Блокнота»: горизонтальная — между верхней
/// и нижней частями окна, вертикальная — между левой и правой. Её перетаскивают мышью. Размеры соседних частей
/// полоса сама не меняет: она сообщает, на сколько сдвинут указатель, а владелец задаёт размеры с учётом своих пределов.
/// </summary>
public sealed class SplitBar : Control
{
    private Orientation _orientation = Orientation.Horizontal;
    private bool _hot;
    private bool _dragging;
    private int _start;

    public SplitBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Cursor = Cursors.HSplit;
        // Толщина полосы; вторую сторону задаёт закрепление (Dock).
        Size = new Size(9, 9);
        AccessibleRole = AccessibleRole.Separator;
    }

    /// <summary>Horizontal — полоса между верхней и нижней частями (тянут вверх-вниз), Vertical — между левой и правой.</summary>
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            _orientation = value;
            Cursor = value == Orientation.Vertical ? Cursors.VSplit : Cursors.HSplit;
            Invalidate();
        }
    }

    /// <summary>Начало перетаскивания.</summary>
    public event EventHandler DragStarted;

    /// <summary>Перетаскивание: смещение указателя от начала, пикселей (вниз или вправо — больше нуля).</summary>
    public event EventHandler<int> DragMoved;

    /// <summary>Конец перетаскивания.</summary>
    public event EventHandler DragFinished;

    /// <summary>Положение указателя вдоль направления перетаскивания, в экранных координатах: сама полоса при этом сдвигается.</summary>
    private int Along(MouseEventArgs e)
    {
        Point point = PointToScreen(e.Location);
        return _orientation == Orientation.Vertical ? point.X : point.Y;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        // Второй щелчок двойного — не перетаскивание.
        if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
        _start = Along(e);
        _dragging = true;
        Invalidate();
        DragStarted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) DragMoved?.Invoke(this, Along(e) - _start);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        EndDrag();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hot = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = false;
        Invalidate();
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        Invalidate();
        DragFinished?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        bool active = _hot || _dragging;
        Graphics g = e.Graphics;
        g.Clear(active ? Blend(BackColor, SystemColors.ControlDark, 0.3) : BackColor);

        // Ручка: короткая линия со скруглёнными концами; её размер следует за толщиной полосы, то есть за масштабом экрана.
        bool vertical = _orientation == Orientation.Vertical;
        int thickness = vertical ? Width : Height;
        float half = thickness * 2f;
        float x = Width / 2f, y = Height / 2f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(active ? SystemColors.ControlDarkDark : SystemColors.ControlDark, Math.Max(2f, thickness / 4f)))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            if (vertical) g.DrawLine(pen, x, y - half, x, y + half);
            else g.DrawLine(pen, x - half, y, x + half, y);
        }
    }

    private static Color Blend(Color a, Color b, double share) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * share),
        (int)Math.Round(a.G + (b.G - a.G) * share),
        (int)Math.Round(a.B + (b.B - a.B) * share));
}
