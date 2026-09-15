using ElasticBezierEasing.Core;
using HarmonyLib;
using YukkuriMovieMaker.Commons;

namespace ElasticBezierEasing.Patches;

/// <summary>
/// AnimationTypeEx の各種判定メソッドをパッチする。
///
/// - IsBeizer: false固定。
///   YMM4標準のインラインベジェ編集パネルではなく、
///   本プラグイン独自のポップアップウィンドウ（ElasticBezierEditorWindow）で編集するため、標準パネルは出さない。
///
/// - IsInterpolationEasing: false固定。
///   重要: trueにするとYMM4は「アイテム全体のtotalFrame」でrateを計算し、複数キーフレーム間をスプライン補間しようとするため、
///   キーフレーム移動終了後に遅れて反動がやってくる致命的なバグの原因となる。
///   falseにすることで、各キーフレーム間(0〜1)の区間補間として正しく評価される。
///
/// - IsKeyFrameSupported: true固定。
///   キーフレーム間のイージング計算として各区間の進行度で評価させるため必須。
/// </summary>
internal static class AnimationTypeExPatch
{
    [HarmonyPatch(typeof(AnimationTypeEx), nameof(AnimationTypeEx.IsBeizer))]
    internal class IsBeizerPatch
    {
        private static bool Prefix(AnimationType animationType, ref bool __result)
        {
            if (!ElasticEasingConsts.IsElasticEasing((int)animationType)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(AnimationTypeEx), nameof(AnimationTypeEx.IsInterpolationEasing))]
    internal class IsInterpolationEasingPatch
    {
        private static bool Prefix(AnimationType animationType, ref bool __result)
        {
            if (!ElasticEasingConsts.IsElasticEasing((int)animationType)) return true;
            __result = false; // 2点間イージングとして正しく評価させるため false
            return false;
        }
    }

    [HarmonyPatch(typeof(AnimationTypeEx), nameof(AnimationTypeEx.IsKeyFrameSupported))]
    internal class IsKeyFrameSupportedPatch
    {
        private static bool Prefix(AnimationType animationType, ref bool __result)
        {
            if (!ElasticEasingConsts.IsElasticEasing((int)animationType)) return true;
            __result = true;
            return false;
        }
    }
}
