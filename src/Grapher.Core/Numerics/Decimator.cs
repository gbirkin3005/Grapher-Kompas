using System.Collections.Generic;
using Grapher.Core.Geometry;

namespace Grapher.Core.Numerics;

/// <summary>
/// Прореживание ломаной алгоритмом Рамера — Дугласа — Пекера.
/// Остаются только точки, без которых линия отклонилась бы от исходной больше допуска,
/// поэтому при допуске в сотые доли миллиметра разница на чертеже не видна.
/// </summary>
public static class Decimator
{
    /// <summary>Возвращает индексы сохраняемых точек по возрастанию. Крайние точки сохраняются всегда.</summary>
    public static List<int> SimplifyIndices(IReadOnlyList<Pt> points, double tolerance)
    {
        int n = points.Count;
        var kept = new List<int>();
        if (n == 0) return kept;
        if (n <= 2 || !(tolerance > 0))
        {
            for (int i = 0; i < n; i++) kept.Add(i);
            return kept;
        }

        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;

        // Явный стек вместо рекурсии: точек могут быть сотни тысяч.
        var stack = new Stack<KeyValuePair<int, int>>();
        stack.Push(new KeyValuePair<int, int>(0, n - 1));
        while (stack.Count > 0)
        {
            KeyValuePair<int, int> range = stack.Pop();
            int first = range.Key, last = range.Value;
            if (last - first < 2) continue;

            double maxDistance = -1;
            int index = -1;
            Pt a = points[first], b = points[last];
            for (int i = first + 1; i < last; i++)
            {
                double distance = Pt.DistanceToSegment(points[i], a, b);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    index = i;
                }
            }

            if (maxDistance > tolerance)
            {
                keep[index] = true;
                stack.Push(new KeyValuePair<int, int>(first, index));
                stack.Push(new KeyValuePair<int, int>(index, last));
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (keep[i]) kept.Add(i);
        }
        return kept;
    }

    public static List<Pt> Simplify(IReadOnlyList<Pt> points, double tolerance)
    {
        List<int> indices = SimplifyIndices(points, tolerance);
        var result = new List<Pt>(indices.Count);
        foreach (int i in indices) result.Add(points[i]);
        return result;
    }
}
