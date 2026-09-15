using System.Globalization;
using ElasticBezierEasing.Core;
using HarmonyLib;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;

namespace ElasticBezierEasing.Patches;

/// <summary>イージング一覧のアイコン文字（例："弾"）を表示するためのコンバーターパッチ。</summary>
[HarmonyPatch(typeof(AnimationTypeToCharConverter), "Convert",
    new[] { typeof(object), typeof(System.Type), typeof(object), typeof(CultureInfo) })]
internal class AnimationTypeToCharConverterPatch
{
    private static bool Prefix(object value, ref object __result)
    {
        try
        {
            if (value is AnimationType typeId && ElasticEasingConsts.IsElasticEasing((int)typeId))
            {
                var p = ElasticConfigStore.Get(ElasticEasingConsts.DecodeConfigIndex((int)typeId));
                __result = ElasticEasingConsts.GetShortLabel(p);
                return false;
            }
        }
        catch { }
        return true;
    }
}

/// <summary>ツールチップに現在のパラメーターを表示するためのコンバーターパッチ。</summary>
[HarmonyPatch(typeof(AnimationTypeToToolTipConverter), "ConvertText",
    new[] { typeof(AnimationType), typeof(bool) })]
internal class ConvertTextPatch
{
    private static bool Prefix(AnimationType type, bool ignoreModes, ref string __result)
    {
        try
        {
            int typeId = (int)type;
            if (ElasticEasingConsts.IsElasticEasing(typeId))
            {
                var p = ElasticConfigStore.Get(ElasticEasingConsts.DecodeConfigIndex(typeId));
                __result = ElasticEasingConsts.GetTooltip(p);
                return false;
            }
        }
        catch { }
        return true;
    }
}
