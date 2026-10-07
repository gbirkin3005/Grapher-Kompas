using System;
using System.Collections.Generic;
using System.Globalization;
using Grapher.Core.Geometry;
using Grapher.Core.Scene;
using KompasAPI7;

namespace Grapher.Kompas;

/// <summary>Итог сверки чертежа со сценой.</summary>
public sealed class VerificationReport
{
    public int Lines { get; set; }
    public int Polylines { get; set; }
    public int Beziers { get; set; }
    public int Circles { get; set; }
    public int Texts { get; set; }
    public int Fills { get; set; }

    /// <summary>Сколько объектов проверено на свой цвет линии.</summary>
    public int ColouredObjects { get; set; }

    /// <summary>Наибольшее расхождение координат между чертежом и сценой, мм.</summary>
    public double MaxDeviation { get; set; }

    public List<string> Mismatches { get; } = new List<string>();

    public bool Ok => Mismatches.Count == 0;

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "lines={0}; polylines={1}; beziers={2}; circles={3}; texts={4}; fills={5}; coloured={8}; maxDeviation={6:0.############} mm; mismatches={7}",
        Lines, Polylines, Beziers, Circles, Texts, Fills, MaxDeviation, Mismatches.Count, ColouredObjects);
}

/// <summary>
/// Сверка построенного графика со сценой: читает объекты последнего макроэлемента из КОМПАС
/// и сравнивает их координаты с примитивами. Так проверяется, что чертёж и предпросмотр,
/// построенные из одной сцены, совпадают по геометрии.
/// </summary>
public static class KompasVerifier
{
    public static VerificationReport CompareLastMacro(KompasSession session, GraphScene scene, double x, double y, double angleDeg,
        double tolerance = 1e-6)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        return session.Guard(() => Compare(session, scene, x, y, angleDeg, tolerance));
    }

    private static VerificationReport Compare(KompasSession session, GraphScene scene, double x, double y, double angleDeg, double tolerance)
    {
        var report = new VerificationReport();

        IKompasDocument2D document = session.GetActiveDocument2D();
        IView view = document.ViewsAndLayersManager.Views.ActiveView;
        double viewScale = view.Scale > 0 ? view.Scale : 1;
        GraphScene placed = scene.Transform(new Transform2D(x, y, angleDeg, 1.0 / viewScale));

        IMacroObjects macros = ((IDrawingContainer)view).MacroObjects;
        if (macros.Count == 0)
        {
            report.Mismatches.Add("В текущем виде нет макроэлементов.");
            return report;
        }

        var macro = (IMacroObject)macros.get_MacroObject(macros.Count - 1);
        var container = (IDrawingContainer)macro;

        var lines = new List<LinePrimitive>();
        var polylines = new List<PolylinePrimitive>();
        var beziers = new List<BezierPrimitive>();
        var circles = new List<CirclePrimitive>();
        var texts = new List<TextPrimitive>();
        int fills = 0;
        foreach (Primitive primitive in placed.Primitives)
        {
            switch (primitive)
            {
                case LinePrimitive p: lines.Add(p); break;
                case PolylinePrimitive p:
                    polylines.Add(p);
                    if (p.Closed && p.Filled) fills++;
                    break;
                case BezierPrimitive p: beziers.Add(p); break;
                case CirclePrimitive p: circles.Add(p); break;
                case TextPrimitive p when !string.IsNullOrEmpty(p.Text): texts.Add(p); break;
            }
        }

        void Check(string what, Pt expected, double actualX, double actualY)
        {
            double deviation = Pt.Distance(expected, new Pt(actualX, actualY));
            if (deviation > report.MaxDeviation) report.MaxDeviation = deviation;
            if (deviation > tolerance && report.Mismatches.Count < 20)
            {
                report.Mismatches.Add(string.Format(CultureInfo.InvariantCulture, "{0}: ожидалось {1}, в КОМПАС ({2:0.######}; {3:0.######})",
                    what, expected, actualX, actualY));
            }
        }

        // Стиль объекта: системный номер либо стиль документа нужного цвета.
        var styles = new ColoredStyles(session, document);
        void CheckStyle(string what, int actualStyle, Grapher.Core.Model.LineStyleId expectedStyle, int? expectedRgb)
        {
            if (expectedRgb == null)
            {
                if (actualStyle != (int)expectedStyle) report.Mismatches.Add(what + ": стиль " + actualStyle + " вместо " + (int)expectedStyle);
                return;
            }

            report.ColouredObjects++;
            int? actualBgr = styles.GetDocumentStyleColor(actualStyle);
            int expectedBgr = Grapher.Core.Model.ColorValue.ToBgr(expectedRgb.Value);
            if (actualBgr != expectedBgr)
            {
                report.Mismatches.Add(what + ": цвет стиля " + (actualBgr == null ? "не задан (стиль " + actualStyle + ")" : "0x" + actualBgr.Value.ToString("X6")) +
                                      " вместо 0x" + expectedBgr.ToString("X6"));
            }
        }

        bool CountsMatch(string what, int expected, int actual)
        {
            if (expected == actual) return true;
            report.Mismatches.Add(string.Format(CultureInfo.InvariantCulture, "{0}: в сцене {1}, в КОМПАС {2}", what, expected, actual));
            return false;
        }

        // Отрезки
        ILineSegments kLines = container.LineSegments;
        report.Lines = kLines.Count;
        if (CountsMatch("Отрезки", lines.Count, kLines.Count))
        {
            for (int i = 0; i < lines.Count; i++)
            {
                ILineSegment k = kLines.get_LineSegment(i);
                Check("Отрезок " + i + ", начало", lines[i].A, k.X1, k.Y1);
                Check("Отрезок " + i + ", конец", lines[i].B, k.X2, k.Y2);
                CheckStyle("Отрезок " + i, k.Style, lines[i].Style, lines[i].Color);
            }
        }

        // Ломаные
        IPolyLines2D kPolylines = container.PolyLines2D;
        report.Polylines = kPolylines.Count;
        if (CountsMatch("Ломаные", polylines.Count, kPolylines.Count))
        {
            for (int i = 0; i < polylines.Count; i++)
            {
                IPolyLine2D k = kPolylines.get_PolyLine2D(i);
                CheckStyle("Ломаная " + i, k.Style, polylines[i].Style, polylines[i].Color);
                if (!CountsMatch("Ломаная " + i + ", вершины", polylines[i].Points.Count, k.PointsCount)) continue;
                for (int j = 0; j < polylines[i].Points.Count; j++)
                {
                    k.GetPoint(j, out double px, out double py);
                    Check("Ломаная " + i + ", вершина " + j, polylines[i].Points[j], px, py);
                }
            }
        }

        // Кривые Безье
        IBeziers kBeziers = container.Beziers;
        report.Beziers = kBeziers.Count;
        if (CountsMatch("Кривые Безье", beziers.Count, kBeziers.Count))
        {
            for (int i = 0; i < beziers.Count; i++)
            {
                IBezier k = kBeziers.get_Bezier(i);
                if (!CountsMatch("Кривая " + i + ", узлы", beziers[i].Nodes.Count, k.PointsCount)) continue;
                for (int j = 0; j < beziers[i].Nodes.Count; j++)
                {
                    k.GetPoint(j, out double xb, out double yb, out double xl, out double yl, out double xr, out double yr);
                    BezierNode node = beziers[i].Nodes[j];
                    Check("Кривая " + i + ", узел " + j, node.Point, xb, yb);
                    Check("Кривая " + i + ", узел " + j + ", левая управляющая", node.In, xl, yl);
                    Check("Кривая " + i + ", узел " + j + ", правая управляющая", node.Out, xr, yr);
                }
                CheckStyle("Кривая " + i, k.Style, beziers[i].Style, beziers[i].Color);
            }
        }

        // Окружности
        ICircles kCircles = container.Circles;
        report.Circles = kCircles.Count;
        if (CountsMatch("Окружности", circles.Count, kCircles.Count))
        {
            for (int i = 0; i < circles.Count; i++)
            {
                ICircle k = kCircles.get_Circle(i);
                Check("Окружность " + i + ", центр", circles[i].Center, k.Xc, k.Yc);
                CheckStyle("Окружность " + i, k.Style, circles[i].Style, circles[i].Color);
                if (Math.Abs(k.Radius - circles[i].Radius) > tolerance) report.Mismatches.Add("Окружность " + i + ": радиус " + k.Radius);
            }
        }

        // Тексты
        IDrawingTexts kTexts = container.DrawingTexts;
        report.Texts = kTexts.Count;
        if (CountsMatch("Тексты", texts.Count, kTexts.Count))
        {
            for (int i = 0; i < texts.Count; i++)
            {
                IDrawingText k = kTexts.get_DrawingText(i);
                Check("Текст «" + texts[i].Text + "»", texts[i].Anchor, k.X, k.Y);
                string actual = ((IText)k).Str;
                if (actual != texts[i].Text) report.Mismatches.Add("Текст " + i + ": «" + actual + "» вместо «" + texts[i].Text + "»");
                double angle = (k.Angle - texts[i].AngleDeg) % 360;
                if (Math.Abs(angle) > 1e-6 && Math.Abs(Math.Abs(angle) - 360) > 1e-6) report.Mismatches.Add("Текст «" + texts[i].Text + "»: угол " + k.Angle);
            }
        }

        report.Fills = container.Colourings.Count;
        CountsMatch("Заливки", fills, report.Fills);
        return report;
    }
}
