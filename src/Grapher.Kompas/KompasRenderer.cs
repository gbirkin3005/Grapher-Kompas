using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Grapher.Core.Geometry;
using Grapher.Core.Scene;
using Kompas6Constants;
using KompasAPI7;

namespace Grapher.Kompas;

/// <summary>Итог построения графика в документе КОМПАС.</summary>
public sealed class RenderResult
{
    /// <summary>Сколько объектов КОМПАС создано.</summary>
    public int ObjectCount { get; set; }

    /// <summary>Объекты объединены в макроэлемент.</summary>
    public bool Grouped { get; set; }

    /// <summary>Масштаб вида, в который построен график (1 для вида 1:1, 0,5 для 1:2).</summary>
    public double ViewScale { get; set; } = 1;

    public string DocumentName { get; set; } = "";

    public List<string> Warnings { get; } = new List<string>();
}

/// <summary>
/// Рендерер КОМПАС: переносит абстрактные примитивы графика в текущий вид активного 2D-документа
/// нативными объектами (отрезки, ломаные, кривые Безье, окружности, тексты) и объединяет их в макроэлемент.
/// </summary>
public static class KompasRenderer
{
    /// <summary>Цвет заливки стрелок: чёрный.</summary>
    private const int FillColor = 0;

    /// <param name="scene">Сцена в локальных координатах (мм на листе, начало — левый нижний угол поля графика).</param>
    /// <param name="x">Абсцисса точки вставки в системе координат текущего вида.</param>
    /// <param name="y">Ордината точки вставки в системе координат текущего вида.</param>
    /// <param name="angleDeg">Угол наклона оси абсцисс графика к оси абсцисс вида, градусы.</param>
    /// <param name="name">Имя макроэлемента в дереве чертежа.</param>
    public static RenderResult Render(KompasSession session, GraphScene scene, double x, double y, double angleDeg, string name)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (scene == null) throw new ArgumentNullException(nameof(scene));

        return session.Guard(() => RenderCore(session, scene, x, y, angleDeg, name));
    }

    private static RenderResult RenderCore(KompasSession session, GraphScene scene, double x, double y, double angleDeg, string name)
    {
        var result = new RenderResult();

        IKompasDocument2D document = session.GetActiveDocument2D();
        result.DocumentName = KompasSession.DisplayName(document);

        IView view = document.ViewsAndLayersManager.Views.ActiveView;
        if (view == null)
        {
            throw new KompasException(KompasErrorKind.ApiError, "В документе КОМПАС нет текущего вида.");
        }

        // Размеры сцены заданы в миллиметрах на бумаге. В виде с масштабом, отличным от 1:1,
        // координаты нужно разделить на масштаб, иначе на листе график окажется другого размера.
        double viewScale = view.Scale;
        if (!(viewScale > 0)) viewScale = 1;
        result.ViewScale = viewScale;
        GraphScene placed = scene.Transform(new Transform2D(x, y, angleDeg, 1.0 / viewScale));

        var document1 = (IKompasDocument2D1)document;
        bool undoOpened = TrySetUndoContainer(document1, true);
        var styles = new ColoredStyles(session, document);

        IMacroObject macro = null;
        try
        {
            var viewContainer = (IDrawingContainer)view;
            IDrawingContainer target = viewContainer;

            // Макроэлемент сам является контейнером объектов: всё, что создано через него,
            // сразу принадлежит макроэлементу и выделяется, перемещается и удаляется как единое целое.
            macro = viewContainer.MacroObjects.Add(false);
            if (macro is IDrawingContainer macroContainer)
            {
                target = macroContainer;
                result.Grouped = true;
            }
            else
            {
                macro = null;
                result.Warnings.Add("Не удалось создать макроэлемент: объекты графика построены без группировки.");
            }

            int failed = 0;
            foreach (Primitive primitive in placed.Primitives)
            {
                int created = Draw(target, primitive, styles);
                if (created > 0) result.ObjectCount += created;
                else if (created < 0) failed++;
            }

            if (macro != null)
            {
                if (!string.IsNullOrWhiteSpace(name)) macro.Name = name;
                if (!macro.Update())
                {
                    result.Grouped = false;
                    result.Warnings.Add("КОМПАС не подтвердил создание макроэлемента. Проверьте, что график выделяется как один объект.");
                }
            }

            if (failed > 0)
            {
                result.Warnings.Add("Не удалось создать объектов: " + failed + ".");
            }
            if (styles.HadFailures)
            {
                result.Warnings.Add("Не удалось создать в документе стиль линии со своим цветом: кривая построена стандартным стилем КОМПАС.");
            }
        }
        catch
        {
            // Незавершённый макроэлемент убираем, чтобы на чертеже не осталось половины графика.
            if (macro != null)
            {
                try
                {
                    macro.Delete();
                }
                catch (COMException)
                {
                }
            }
            throw;
        }
        finally
        {
            if (undoOpened) TrySetUndoContainer(document1, false);
        }

        return result;
    }

    /// <summary>Объединяет все операции построения в один шаг отмены (Ctrl+Z убирает график целиком).</summary>
    private static bool TrySetUndoContainer(IKompasDocument2D1 document, bool value)
    {
        try
        {
            document.UndoContainer = value;
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    /// <returns>Число созданных объектов; 0 — примитив пуст; −1 — КОМПАС отказался создать объект.</returns>
    private static int Draw(IDrawingContainer target, Primitive primitive, ColoredStyles styles)
    {
        switch (primitive)
        {
            case LinePrimitive line:
                return DrawLine(target, line, styles.Resolve(line.Style, line.Color));
            case PolylinePrimitive polyline:
                return DrawPolyline(target, polyline, styles.Resolve(polyline.Style, polyline.Color));
            case BezierPrimitive bezier:
                return DrawBezier(target, bezier, styles.Resolve(bezier.Style, bezier.Color));
            case CirclePrimitive circle:
                return DrawCircle(target, circle, styles.Resolve(circle.Style, circle.Color));
            case TextPrimitive text:
                return DrawText(target, text);
            default:
                return 0;
        }
    }

    private static int DrawLine(IDrawingContainer target, LinePrimitive line, int style)
    {
        ILineSegment segment = target.LineSegments.Add();
        segment.X1 = line.A.X;
        segment.Y1 = line.A.Y;
        segment.X2 = line.B.X;
        segment.Y2 = line.B.Y;
        segment.Style = style;
        return segment.Update() ? 1 : -1;
    }

    private static int DrawPolyline(IDrawingContainer target, PolylinePrimitive polyline, int style)
    {
        if (polyline.Points.Count < 2) return 0;

        var coordinates = new double[polyline.Points.Count * 2];
        for (int i = 0; i < polyline.Points.Count; i++)
        {
            coordinates[2 * i] = polyline.Points[i].X;
            coordinates[2 * i + 1] = polyline.Points[i].Y;
        }

        IPolyLine2D line = target.PolyLines2D.Add();
        line.Points = coordinates;
        line.Closed = polyline.Closed;
        line.Style = style;
        if (!line.Update()) return -1;

        if (!(polyline.Closed && polyline.Filled)) return 1;

        // Сплошная заливка замкнутого контура (стрелки осей).
        var colouring = target.Colourings.Add();
        if (colouring is IBoundariesObject boundaries && boundaries.AddBoundaries(line, false))
        {
            colouring.ColouringType = ksColouringTypeEnum.ksColouringSolid;
            colouring.Color1 = FillColor;
            if (colouring.Update()) return 2;
        }
        return 1;
    }

    private static int DrawBezier(IDrawingContainer target, BezierPrimitive bezier, int style)
    {
        if (bezier.Nodes.Count < 2) return 0;

        // Каждый узел: опорная точка, левая и правая управляющие точки.
        var coordinates = new double[bezier.Nodes.Count * 6];
        for (int i = 0; i < bezier.Nodes.Count; i++)
        {
            BezierNode node = bezier.Nodes[i];
            int k = 6 * i;
            coordinates[k] = node.Point.X;
            coordinates[k + 1] = node.Point.Y;
            coordinates[k + 2] = node.In.X;
            coordinates[k + 3] = node.In.Y;
            coordinates[k + 4] = node.Out.X;
            coordinates[k + 5] = node.Out.Y;
        }

        IBezier curve = target.Beziers.Add();
        curve.set_Points(true, coordinates);
        curve.Style = style;
        return curve.Update() ? 1 : -1;
    }

    private static int DrawCircle(IDrawingContainer target, CirclePrimitive circle, int style)
    {
        if (!(circle.Radius > 0)) return 0;

        ICircle c = target.Circles.Add();
        c.Xc = circle.Center.X;
        c.Yc = circle.Center.Y;
        c.Radius = circle.Radius;
        c.Style = style;
        return c.Update() ? 1 : -1;
    }

    private static int DrawText(IDrawingContainer target, TextPrimitive primitive)
    {
        if (string.IsNullOrEmpty(primitive.Text) || !(primitive.HeightMm > 0)) return 0;

        // Точка привязки текста КОМПАС — на базовой линии; Allocation задаёт, какой конец строки в ней находится.
        IDrawingText drawingText = target.DrawingTexts.Add();
        drawingText.X = primitive.Anchor.X;
        drawingText.Y = primitive.Anchor.Y;
        drawingText.Angle = primitive.AngleDeg;
        drawingText.Allocation = ToAllocation(primitive.Align);

        var text = (IText)drawingText;
        text.Str = primitive.Text;

        // Шрифт и высота задаются для каждого фрагмента строки.
        for (int i = 0; i < text.Count; i++)
        {
            ITextLine line = text.get_TextLine(i);
            for (int j = 0; j < line.Count; j++)
            {
                ITextItem item = line.get_TextItem(j);
                var font = (ITextFont)item;
                if (!string.IsNullOrWhiteSpace(primitive.FontName)) font.FontName = primitive.FontName;
                font.Height = primitive.HeightMm;
                font.Italic = primitive.Italic;
                item.Update();
            }
        }

        return drawingText.Update() ? 1 : -1;
    }

    private static ksAllocationEnum ToAllocation(TextAlign align)
    {
        switch (align)
        {
            case TextAlign.Center:
                return ksAllocationEnum.ksAlCentre;
            case TextAlign.Right:
                return ksAllocationEnum.ksAlRight;
            default:
                return ksAllocationEnum.ksAlLeft;
        }
    }
}
