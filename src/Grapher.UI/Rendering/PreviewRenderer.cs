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

            if (ShowInsertionPoint)
            {
                DrawInsertionPoint(g, Map(scene.InsertionPoint));
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
                    if (circle.Filled)
                    {
                        using (var fill = new SolidBrush(pen.Color))
                        {
                            g.FillEllipse(fill, center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
                        }
                    }
                    g.DrawEllipse(pen, center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
                }
                break;

            case ArcPrimitive arc:
                PointF arcCenter = map(arc.Center);
                float arcRadius = (float)arc.Radius * scale;
                if (arcRadius < 0.5f || !(arc.SweepDeg > 0)) break;
                using (Pen pen = CreatePen(arc.Style, scale, arc.Color))
                {
                    // На чертеже углы отсчитываются против часовой стрелки при оси Y вверх,
                    // в GDI+ — по часовой при оси Y вниз: знак угла меняется.
                    g.DrawArc(pen, arcCenter.X - arcRadius, arcCenter.Y - arcRadius, 2 * arcRadius, 2 * arcRadius,
                        -(float)arc.StartDeg, -(float)arc.SweepDeg);
                }
                break;

            case TextPrimitive text:
                DrawText(g, text, map(text.Anchor), scale, brush);
                break;

            case PointPrimitive point:
                DrawPoint(g, point, map(point.Position), scale);
                break;

            case LeaderArrowPrimitive arrow:
                DrawLeaderArrow(g, map(arrow.Tail), map(arrow.Tip), scale, brush);
                break;
        }
    }

    /// <summary>Значок точки КОМПАС: размеры измерены на чертеже КОМПАС-3D v25.</summary>
    private void DrawPoint(Graphics g, PointPrimitive point, PointF c, float scale)
    {
        float r = (float)point.SizeMm * scale / 2;
        using (Pen pen = CreatePen(point.Style == 9 ? LineStyleId.Main : LineStyleId.Thin, scale, point.Color))
        {
            switch (point.Style)
            {
                case 1:
                    using (var fill = new SolidBrush(pen.Color)) g.FillEllipse(fill, c.X - r, c.Y - r, 2 * r, 2 * r);
                    break;
                case 3:
                    g.DrawLine(pen, c.X - r, c.Y - r, c.X + r, c.Y + r);
                    g.DrawLine(pen, c.X - r, c.Y + r, c.X + r, c.Y - r);
                    break;
                case 4:
                    g.DrawRectangle(pen, c.X - r, c.Y - r, 2 * r, 2 * r);
                    break;
                case 5:
                    g.DrawPolygon(pen, new[] { new PointF(c.X, c.Y - r), new PointF(c.X - r, c.Y + r), new PointF(c.X + r, c.Y + r) });
                    break;
                case 6:
                    g.DrawEllipse(pen, c.X - r, c.Y - r, 2 * r, 2 * r);
                    break;
                case 7:
                    g.DrawLine(pen, c.X - r, c.Y, c.X + r, c.Y);
                    g.DrawLine(pen, c.X - r * 0.7f, c.Y - r * 0.7f, c.X + r * 0.7f, c.Y + r * 0.7f);
                    g.DrawLine(pen, c.X - r * 0.7f, c.Y + r * 0.7f, c.X + r * 0.7f, c.Y - r * 0.7f);
                    break;
                case 8:
                    g.DrawRectangle(pen, c.X - r, c.Y - r, 2 * r, 2 * r);
                    g.DrawLine(pen, c.X - r, c.Y - r, c.X + r, c.Y + r);
                    g.DrawLine(pen, c.X - r, c.Y + r, c.X + r, c.Y - r);
                    break;
                default:
                    // 2 — крестик, 9 — утолщённый крестик.
                    g.DrawLine(pen, c.X - r, c.Y, c.X + r, c.Y);
                    g.DrawLine(pen, c.X, c.Y - r, c.X, c.Y + r);
                    break;
            }
        }
    }

    /// <summary>Линия-выноска КОМПАС со стрелкой: тонкая линия и узкая залитая стрелка 5 × 1,4 мм.</summary>
    private void DrawLeaderArrow(Graphics g, PointF tail, PointF tip, float scale, Brush brush)
    {
        float dx = tip.X - tail.X, dy = tip.Y - tail.Y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (!(length > 0)) return;
        dx /= length;
        dy /= length;

        float head = Math.Min(length, (float)LeaderArrowPrimitive.HeadLengthMm * scale);
        float half = (float)LeaderArrowPrimitive.HeadWidthMm * scale / 2 * head / ((float)LeaderArrowPrimitive.HeadLengthMm * scale);
        var back = new PointF(tip.X - dx * head, tip.Y - dy * head);
        using (Pen pen = CreatePen(LineStyleId.Thin, scale, null))
        {
            g.DrawLine(pen, tail, back);
        }
        g.FillPolygon(brush, new[] { tip, new PointF(back.X - dy * half, back.Y + dx * half), new PointF(back.X + dy * half, back.Y - dx * half) });
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

        // Строка разбирается на обычный текст, индексы и дроби. Размеры и положение фрагментов —
        // такие же, как у штатных индексов и дробей текста КОМПАС (см. TextMarkup).
        List<TextRun> runs = TextMarkup.Parse(text.Text);
        var pieces = new List<TextPiece>(runs.Count);
        var bars = new List<RectangleF>();
        float width = 0;
        float capHeight = (float)text.HeightMm * scale;

        TextPiece Piece(TextRun run, float x)
        {
            float em = emSize * (float)TextMarkup.Scale(run.Kind);
            return new TextPiece { Text = run.Text, X = x, Em = em, Rise = (float)TextMarkup.BaselineRise(run.Kind) * capHeight };
        }

        // Числитель или знаменатель: обычный текст и индексы; координаты X — от начала этой части дроби.
        List<TextPiece> LayoutPart(TextRun part, out float partWidth)
        {
            var result = new List<TextPiece>();
            float em = emSize * (float)TextMarkup.FractionScale;
            float cap = capHeight * (float)TextMarkup.FractionScale;
            float rise = (float)TextMarkup.PartRise(part.Kind, part.Text) * capHeight;
            float x = 0;

            List<TextRun> inner = TextMarkup.ParsePart(part.Text);
            for (int k = 0; k < inner.Count; k++)
            {
                TextRun run = inner[k];
                if (run.Kind == TextRunKind.Normal)
                {
                    result.Add(new TextPiece { Text = run.Text, X = x, Em = em, Rise = rise });
                    x += MeasureWidth(g, run.Text, metrics, em);
                    continue;
                }

                float innerEm = em * (float)TextMarkup.IndexScale;
                float innerWidth = MeasureWidth(g, run.Text, metrics, innerEm);
                result.Add(new TextPiece { Text = run.Text, X = x, Em = innerEm, Rise = rise + (float)TextMarkup.BaselineRise(run.Kind) * cap });
                if (k + 1 < inner.Count && inner[k + 1].Kind != run.Kind && inner[k + 1].Kind != TextRunKind.Normal)
                {
                    TextRun second = inner[++k];
                    result.Add(new TextPiece { Text = second.Text, X = x, Em = innerEm, Rise = rise + (float)TextMarkup.BaselineRise(second.Kind) * cap });
                    innerWidth = Math.Max(innerWidth, MeasureWidth(g, second.Text, metrics, innerEm));
                }
                x += innerWidth;
            }

            partWidth = x;
            return result;
        }

        void AddShifted(List<TextPiece> part, float shift)
        {
            foreach (TextPiece piece in part)
            {
                TextPiece moved = piece;
                moved.X += shift;
                pieces.Add(moved);
            }
        }

        for (int i = 0; i < runs.Count; i++)
        {
            TextRun run = runs[i];
            if (run.Kind == TextRunKind.Normal)
            {
                pieces.Add(Piece(run, width));
                width += MeasureWidth(g, run.Text, metrics, emSize);
                continue;
            }

            if (run.Kind == TextRunKind.Numerator || run.Kind == TextRunKind.Denominator)
            {
                // Дробь: числитель и знаменатель по центру черты.
                TextRun? lower = run.Kind == TextRunKind.Numerator && i + 1 < runs.Count && runs[i + 1].Kind == TextRunKind.Denominator
                    ? runs[++i]
                    : (TextRun?)null;
                List<TextPiece> upperPart = LayoutPart(run, out float upperWidth);
                float lowerWidth = 0;
                List<TextPiece> lowerPart = lower.HasValue ? LayoutPart(lower.Value, out lowerWidth) : null;
                float fractionWidth = Math.Max(upperWidth, lowerWidth);
                float gap = (float)TextMetrics.FractionSideGap * capHeight;
                float x0 = width + gap;

                AddShifted(upperPart, x0 + (fractionWidth - upperWidth) / 2);
                if (lowerPart != null) AddShifted(lowerPart, x0 + (fractionWidth - lowerWidth) / 2);
                bars.Add(new RectangleF(x0, -(float)TextMarkup.FractionBarRise * capHeight, fractionWidth, 0));
                width += fractionWidth + 2 * gap;
                continue;
            }

            // Индекс; верхний и нижний у одного основания стоят друг над другом.
            float indexEm = emSize * (float)TextMarkup.IndexScale;
            float indexWidth = MeasureWidth(g, run.Text, metrics, indexEm);
            pieces.Add(Piece(run, width));
            if (i + 1 < runs.Count && runs[i + 1].Kind != run.Kind &&
                (runs[i + 1].Kind == TextRunKind.Subscript || runs[i + 1].Kind == TextRunKind.Superscript))
            {
                TextRun second = runs[++i];
                pieces.Add(Piece(second, width));
                indexWidth = Math.Max(indexWidth, MeasureWidth(g, second.Text, metrics, indexEm));
            }
            width += indexWidth;
        }

        float left = text.Align == TextAlign.Left ? 0 : text.Align == TextAlign.Center ? -width / 2 : -width;

        GraphicsState state = g.Save();
        try
        {
            g.TranslateTransform(anchor.X, anchor.Y);
            g.RotateTransform(-(float)text.AngleDeg);
            using (var path = new GraphicsPath())
            {
                foreach (TextPiece piece in pieces)
                {
                    if (piece.Em < 0.5f || piece.Text.Length == 0) continue;
                    var origin = new PointF(left + piece.X, -piece.Rise - metrics.BaselineRatio * piece.Em);
                    path.AddString(piece.Text, metrics.Family, (int)metrics.Style, piece.Em, origin, StringFormat.GenericTypographic);
                }
                g.FillPath(brush, path);
            }

            if (bars.Count > 0)
            {
                // Черта дроби — толщиной со штрих букв.
                using (var pen = new Pen(brush, Math.Max(1f, capHeight * 0.07f)))
                {
                    foreach (RectangleF bar in bars)
                    {
                        g.DrawLine(pen, left + bar.X, bar.Y, left + bar.X + bar.Width, bar.Y);
                    }
                }
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    private struct TextPiece
    {
        public string Text;
        public float X;
        public float Em;
        /// <summary>Подъём базовой линии фрагмента над строкой, пиксели.</summary>
        public float Rise;
    }

    private static float MeasureWidth(Graphics g, string text, FontMetrics metrics, float emSize)
    {
        if (emSize < 0.5f || string.IsNullOrEmpty(text)) return 0;
        using (var font = new Font(metrics.Family, emSize, metrics.Style, GraphicsUnit.Pixel))
        {
            return g.MeasureString(text, font, PointF.Empty, MeasureFormat).Width;
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
