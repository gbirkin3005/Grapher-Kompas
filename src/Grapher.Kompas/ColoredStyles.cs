using System.Collections.Generic;
using System.Runtime.InteropServices;
using Grapher.Core.Model;
using KompasAPI7;

namespace Grapher.Kompas;

/// <summary>
/// Стили линий со своим цветом. У объектов чертежа КОМПАС нет собственного цвета — он задаётся
/// стилем линии, поэтому для кривой нестандартного цвета в документе создаётся стиль
/// «Grapher: Основная #RRGGBB» с толщиной и штрихами системного стиля и нужным цветом.
/// </summary>
internal sealed class ColoredStyles
{
    private const string NamePrefix = "Grapher: ";

    private readonly IStyles _documentStyles;
    private readonly IStyles _systemStyles;
    private readonly Dictionary<long, int> _cache = new Dictionary<long, int>();

    public ColoredStyles(KompasSession session, IKompasDocument document)
    {
        _documentStyles = (document as IStylesManager)?.CurvesStyles;
        _systemStyles = (session.Api7 as IStylesManager)?.CurvesStyles;
    }

    /// <summary>Не удалось создать хотя бы один стиль: линии построены системным стилем.</summary>
    public bool HadFailures { get; private set; }

    /// <summary>Номер стиля линии для API: системный, если цвет не задан, иначе стиль документа с этим цветом.</summary>
    public int Resolve(LineStyleId style, int? rgb)
    {
        if (rgb == null) return (int)style;

        long key = ((long)style << 32) | (uint)rgb.Value;
        if (_cache.TryGetValue(key, out int cached)) return cached;

        int id = FindOrCreate(style, rgb.Value);
        if (id == 0)
        {
            HadFailures = true;
            id = (int)style;
        }
        _cache[key] = id;
        return id;
    }

    public static string StyleName(LineStyleId style, int rgb) =>
        NamePrefix + LineStyles.Get(style).Name + " " + ColorValue.Format(rgb);

    private int FindOrCreate(LineStyleId style, int rgb)
    {
        if (_documentStyles == null || _systemStyles == null) return 0;

        try
        {
            string name = StyleName(style, rgb);
            int bgr = ColorValue.ToBgr(rgb);

            // Стиль мог остаться от предыдущего построения в этом документе.
            for (int i = 0; i < _documentStyles.Count; i++)
            {
                if (_documentStyles[i] is ICurveStyle existing && existing.Name == name && existing.Color == bgr)
                {
                    return existing.ApiStyleId;
                }
            }

            if (!(_systemStyles.get_StyleByApiId((int)style) is ICurveStyle source)) return 0;

            // Стиль создаётся с нуля, параметры переносятся вручную. IStyles.Copy для системного стиля
            // использовать нельзя: «копия» остаётся связанной с оригиналом, и смена её цвета
            // перекрашивает сам системный стиль во всём КОМПАС (проверено на v25).
            if (!(_documentStyles.Add() is ICurveStyle created)) return 0;

            created.Name = name;
            created.CurveStyleType = source.CurveStyleType;
            created.CurvePenType = Kompas6Constants.ksCurvePenTypeEnum.ksCPTIndependent;
            created.PaperWidth = source.PaperWidth;
            created.ScreenWidth = source.ScreenWidth;
            created.Color = bgr;

            // У нового прерывистого стиля уже есть штрих по умолчанию — убираем его.
            created.ClearPatterns();
            for (int p = 0; p < source.PatternsCount; p++)
            {
                created.AddPattern(source.get_PatternVisibleSegmentLenght(p), source.get_PatternInvisibleSegmentLenght(p));
            }

            return created.Update() ? created.ApiStyleId : 0;
        }
        catch (COMException)
        {
            return 0;
        }
    }

    /// <summary>Цвет (0xBBGGRR) стиля документа с заданным номером; null, если такого стиля нет.</summary>
    public int? GetDocumentStyleColor(int apiStyleId)
    {
        if (_documentStyles == null) return null;
        for (int i = 0; i < _documentStyles.Count; i++)
        {
            if (_documentStyles[i] is ICurveStyle style && style.ApiStyleId == apiStyleId) return style.Color;
        }
        return null;
    }
}
