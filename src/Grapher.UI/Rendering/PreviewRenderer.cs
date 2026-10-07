using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Grapher.Core.Geometry;
using Grapher.Core.Model;
using Grapher.Core.Scene;

namespace Grapher.UI.Rendering;

/// <summary>
/// Рендерер предпросмотра (GDI+). Рисует тот же список примитивов, что уходит в КОМПАС,
/// поэтому геометрия на экране и на чертеже совпадает.
/// </summary>
public sealed class PreviewRenderer
{
    private readonly Dictionary<string, FontMetrics> _fonts = new Dictionary<string, FontMetrics>();

    public Color LineColor { get; set; } = Color.Black;
    public Color PaperColor { get; set; } = Color.White;

    /// <summary>Поля вокруг графика, пиксели.</summary>
    public float Margin { get; set; } = 14;

    /// <summary>Показывать точку вставки (левый нижний угол поля графика).</summary>
    public bool ShowInsertionPoint { get; set; } = true;

    /// <summary>Пикселей на миллиметр при последней отрисовке.</summary>
    public float LastScale { get; private set; }

    /// <summary>Рисует сцену, вписывая её в заданную область.</summary>
    public void Render(Graphics g, GraphScene scene, RectangleF area)
    {
        if (scene == null || area.Width < 10 || area.Height < 10) return;

        RectD bounds = scene.GetBounds();
        double width = Math.Max(bounds.Width, 1e-6), height = Math.Max(bounds.Height, 1e-6);
        float scale = (float)Math.Min((area.Width - 2 * Margin) / width, (area.Height - 2 * Margin) / height);
        if (!(scale > 0)) return;
        LastScale = scale;

        // Центрируем график в области; ось Y чертежа направлена вверх, экрана — вниз.
        float offsetX = area.Left + (area.Width - (float)width * scale) / 2 - (float)bounds.XMin * scale;
        float offsetY = area.Top + (area.Height - (float)height * scale) / 2 + (float)bounds.YMax * scale;
        PointF Map(Pt p) => new PointF(offsetX + (float)p.X * scale, offsetY - (float)p.Y * scale);

        GraphicsState state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            using (var brush = new SolidBrush(LineColor))
            {
                foreach (Primitive primitive in scene.Primitives)
                {
                    Draw(g, primitive, Map, scale, brush);
                }
            }

            if (ShowInsertionPoint && scene.PlotCorners != null && scene.PlotCorners.Length > 0)
            {
                DrawInsertionPoint(g, Map(scene.PlotCorners[0]));
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    private void Draw(Graphics g, Primitive primitive, Func<Pt, PointF> map, float scale, Brush brush)
    {
        switch (primitive)
        {
            case LinePrimitive line:
                using (Pen pen = CreatePen(line.Style, scale, line.Color))
                {
                    g.DrawLine(pen, map(line.A), map(line.B));
                }
                break;

            case PolylinePrimitive polyline:
                if (polyline.Points.Count < 2) break;
                var points = new PointF[polyline.Points.Count];
                for (int i = 0; i < points.Length; i++) points[i] = map(polyline.Points[i]);
                using (Pen pen = CreatePen(polyline.Style, scale, polyline.Color))
                {
                    if (polyline.Closed)
                    {
                        if (polyline.Filled) g.FillPolygon(brush, points);
                        g.DrawPolygon(pen, points);
                    }
                    else
                    {
                        g.DrawLines(pen, points);
                    }
                }
                break;

            case BezierPrimitive bezier:
                if (bezier.Nodes.Count < 2) break;
                // Формат GDI+: начальная точка, затем по три точки на сегмент (две управляющие и конечная).
                var curve = new PointF[3 * (bezier.Nodes.Count - 1) + 1];
                curve[0] = map(bezier.Nodes[0].Point);
                for (int i = 1; i < bezier.Nodes.Count; i++)
                {
                    curve[3 * i - 2] = map(bezier.Nodes[i - 1].Out);
                    curve[3 * i - 1] = map(bezier.Nodes[i].In);
                    curve[3 * i] = map(bezier.Nodes[i].Point);
                }
                using (Pen pen = CreatePen(bezier.Style, scale, bezier.Color))
                {
                    g.DrawBeziers(pen, curve);
                }
                break;

            case CirclePrimitive circle:
                PointF center = map(circle.Center);
                float radius = (float)circle.Radius * scale;
                using (Pen pen = CreatePen(circle.Style, scale, circle.Color))
                {
                    g.DrawEllipse(pen, center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
                }
                break;

            case TextPrimitive text:
                DrawText(g, text, map(text.Anchor), scale, brush);
                break;
        }
    }

    /// <summary>Перо со стилем линии КОМПАС: толщина на бумаге и чередование штрихов.</summary>
    private Pen CreatePen(LineStyleId style, float scale, int? rgb)
    {
        LineStyleInfo info = LineStyles.Get(style);
        float width = Math.Max(1f, (float)info.WidthMm * scale);
        // Свой цвет кривой; остальное рисуется, как на печати, чёрным.
        Color color = rgb.HasValue
            ? Color.FromArgb(ColorValue.Red(rgb.Value), ColorValue.Green(rgb.Value), ColorValue.Blue(rgb.Value))
            : LineColor;
        var pen = new Pen(color, width)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Flat,
            EndCap = LineCap.Flat
        };

        if (info.DashPatternMm != null)
        {
            // Шаблон GDI+ задаётся в толщинах пера.
            var pattern = new float[info.DashPatternMm.Length];
            for (int i = 0; i < pattern.Length; i++)
            {
                pattern[i] = Math.Max(0.5f, (float)info.DashPatternMm[i] * scale / width);
            }
            pen.DashPattern = pattern;
        }
        return pen;
    }

    private void DrawText(Graphics g, TextPrimitive text, PointF anchor, float scale, Brush brush)
    {
        if (string.IsNullOrEmpty(text.Text) || !(text.HeightMm > 0)) return;

        FontMetrics metrics = GetMetrics(text.FontName, text.Italic);
        // Высота шрифта по ГОСТ — это высота прописных букв, а GDI+ нужен размер кегельной площадки.
        float emSize = (float)text.HeightMm * scale / metrics.CapHeightRatio;
        if (emSize < 0.5f) return;

        float width;
        using (var font = new Font(metrics.Family, emSize, metrics.Style, GraphicsUnit.Pixel))
        {
            width = g.MeasureString(text.Text, font, PointF.Empty, MeasureFormat).Width;
        }

        float left = text.Align == TextAlign.Left ? 0 : text.Align == TextAlign.Center ? -width / 2 : -width;
        float top = -metrics.BaselineRatio * emSize;

        GraphicsState state = g.Save();
        try
        {
            g.TranslateTransform(anchor.X, anchor.Y);
            g.RotateTransform(-(float)text.AngleDeg);
            using (var path = new GraphicsPath())
            {
                path.AddString(text.Text, metrics.Family, (int)metrics.Style, emSize, new PointF(left, top), StringFormat.GenericTypographic);
                g.FillPath(brush, path);
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    private static readonly StringFormat MeasureFormat = CreateMeasureFormat();

    private static StringFormat CreateMeasureFormat()
    {
        var format = new StringFormat(StringFormat.GenericTypographic);
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        return format;
    }

    private void DrawInsertionPoint(Graphics g, PointF p)
    {
        using (var pen = new Pen(Color.FromArgb(200, 200, 30, 30), 1.5f))
        {
            const float r = 5;
            g.DrawEllipse(pen, p.X - r, p.Y - r, 2 * r, 2 * r);
            g.DrawLine(pen, p.X - r - 3, p.Y, p.X + r + 3, p.Y);
            g.DrawLine(pen, p.X, p.Y - r - 3, p.X, p.Y + r + 3);
        }
    }

    // ------------------------------------------------------------------ метрики шрифта

    private sealed class FontMetrics
    {
        public FontFamily Family;
        public FontStyle Style;
        /// <summary>Высота прописной буквы в долях размера шрифта.</summary>
        public float CapHeightRatio;
        /// <summary>Расстояние от верха строки до базовой линии в долях размера шрифта.</summary>
        public float BaselineRatio;
    }

    private FontMetrics GetMetrics(string fontName, bool italic)
    {
        string key = (fontName ?? "") + (italic ? "|i" : "|r");
        if (_fonts.TryGetValue(key, out FontMetrics cached)) return cached;

        FontFamily family = FindFamily(fontName);
        FontStyle style = italic ? FontStyle.Italic : FontStyle.Regular;
        if (!family.IsStyleAvailable(style))
        {
            style = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Italic;
        }

        var metrics = new FontMetrics { Family = family, Style = style, CapHeightRatio = 0.7f, BaselineRatio = 0.8f };
        try
        {
            // Измеряем контур прописной буквы: его высота и нижний край дают высоту букв и базовую линию.
            using (var path = new GraphicsPath())
            {
                const float em = 1000f;
                path.AddString("H", family, (int)style, em, PointF.Empty, StringFormat.GenericTypographic);
                RectangleF box = path.GetBounds();
                if (box.Height > 1)
                {
                    metrics.CapHeightRatio = box.Height / em;
                    metrics.BaselineRatio = box.Bottom / em;
                }
            }
        }
        catch (ArgumentException)
        {
            // Шрифт не поддерживает стиль — остаются типовые значения.
        }

        _fonts[key] = metrics;
        return metrics;
    }

    /// <summary>Шрифт по имени; если он не установлен — запасной чертёжный или системный шрифт.</summary>
    private static FontFamily FindFamily(string fontName)
    {
        foreach (string name in new[] { fontName, "GOST type A", "GOST Type AU", "Arial Narrow", "Arial" })
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            try
            {
                var family = new FontFamily(name);
                // GDI+ может молча подставить другой шрифт: проверяем имя.
                if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase)) return family;
            }
            catch (ArgumentException)
            {
            }
        }
        return FontFamily.GenericSansSerif;
    }

    /// <summary>Установлен ли в системе шрифт с таким именем.</summary>
    public static bool IsFontInstalled(string fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName)) return false;
        try
        {
            using (var family = new FontFamily(fontName))
            {
                return string.Equals(family.Name, fontName, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Рисует сцену в растровое изображение (для сохранения предпросмотра в файл).</summary>
    public Bitmap RenderToBitmap(GraphScene scene, int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(PaperColor);
            Render(g, scene, new RectangleF(0, 0, width, height));
        }
        return bitmap;
    }
}
