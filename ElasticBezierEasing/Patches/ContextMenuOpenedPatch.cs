using System;
using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ElasticBezierEasing.Core;
using ElasticBezierEasing.UI;
using HarmonyLib;
using YukkuriMovieMaker.Controls;

namespace ElasticBezierEasing.Patches;

/// <summary>
/// AnimationSlider の右クリックメニュー(ContextMenu_Opened)をパッチし、
/// 「エラスティックベジェ」の項目（クイックプリセット＋専用編集ウィンドウを開く項目）を追加する。
/// </summary>
[HarmonyPatch]
internal class ContextMenuOpenedPatch
{
    private const string ParentMenuTag = "ElasticBezierEasingPlugin_Parent";

    private static MethodBase? TargetMethod()
        => typeof(AnimationSlider).GetMethod("ContextMenu_Opened", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? ClickMethod =
        typeof(AnimationSlider).GetMethod("ContextMenuItem_Click", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo? AnimationProp =
        typeof(AnimationSlider).GetProperty("Animation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static void Prefix(object __instance)
    {
        try
        {
            var cm = GetContextMenu(__instance);
            if (cm == null) return;

            MenuItem? found = null;
            foreach (object item in (IEnumerable)cm.Items)
                if (item is MenuItem mi && mi.Tag is string t && t == ParentMenuTag) { found = mi; break; }

            if (found != null) cm.Items.Remove(found);
        }
        catch { }
    }

    private static void Postfix(object __instance)
    {
        try
        {
            var cm = GetContextMenu(__instance);
            if (cm == null || ClickMethod == null) return;
            if (__instance is not AnimationSlider slider) return;

            int currentId = TryGetCurrentTypeId(slider);
            bool isCurrentlyElastic = ElasticEasingConsts.IsElasticEasing(currentId);

            var parent = new MenuItem
            {
                Header = "エラスティックベジェ(_E)",
                Tag = ParentMenuTag,
                IsChecked = isCurrentlyElastic,
            };

            // 専用編集ウィンドウを開く項目
            var editItem = new MenuItem
            {
                Header = isCurrentlyElastic ? "編集(_D)" : "ベジェを追加&編集(_D)",
            };
            editItem.Click += (s, e) => OpenEditor(slider);
            parent.Items.Add(editItem);

            parent.Items.Add(new Separator());

            // クイックプリセット（編集ウィンドウを開かず即適用）
            AddPresetItem(parent, slider, "Ease Out（標準収束）", ElasticBezierParameters.Presets.Normal);
            AddPresetItem(parent, slider, "Ease In（助走発進）",   ElasticBezierParameters.Presets.InNormal);
            AddPresetItem(parent, slider, "Ease In-Out（両方）",  ElasticBezierParameters.Presets.InOutNormal);
            parent.Items.Add(new Separator());
            AddPresetItem(parent, slider, "Soft（弱め）",   ElasticBezierParameters.Presets.Soft);
            AddPresetItem(parent, slider, "Strong（強め）", ElasticBezierParameters.Presets.Strong);

            cm.Items.Add(parent);
        }
        catch { }
    }

    private static void AddPresetItem(MenuItem parent, AnimationSlider slider, string header, ElasticBezierParameters preset)
    {
        var item = new MenuItem { Header = header };
        item.Click += (s, e) => ApplyParameters(slider, preset, e);
        parent.Items.Add(item);
    }

    internal static void ApplyParameters(AnimationSlider slider, ElasticBezierParameters parameters, RoutedEventArgs? e = null)
    {
        try
        {
            if (ClickMethod == null) return;
            int configIndex = ElasticConfigStore.GetOrCreateIndex(parameters);
            int newId = ElasticEasingConsts.EncodeId(configIndex);
            var fakeItem = new MenuItem { Tag = newId.ToString() };
            ClickMethod.Invoke(slider, new object[] { fakeItem, e ?? new RoutedEventArgs() });
        }
        catch { }
    }

    internal static void OpenEditor(AnimationSlider slider)
    {
        try
        {
            int currentId = TryGetCurrentTypeId(slider);
            var current = ElasticEasingConsts.IsElasticEasing(currentId)
                ? ElasticConfigStore.Get(ElasticEasingConsts.DecodeConfigIndex(currentId))
                : ElasticBezierParameters.Presets.Normal.Clone();

            var window = new ElasticBezierEditorWindow(current);

            if (Application.Current?.MainWindow != null)
                window.Owner = Application.Current.MainWindow;

            if (window.ShowDialog() == true)
            {
                ApplyParameters(slider, window.ResultParameters);
            }
        }
        catch { }
    }

    private static ContextMenu? GetContextMenu(object instance)
        => typeof(AnimationSlider)
            .GetField("contextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(instance) as ContextMenu;

    private static int TryGetCurrentTypeId(object instance)
    {
        try
        {
            var anim = AnimationProp?.GetValue(instance);
            if (anim == null) return -1;
            var atProp = anim.GetType().GetProperty("AnimationType");
            return atProp != null ? (int)(atProp.GetValue(anim) ?? -1) : -1;
        }
        catch { return -1; }
    }
}
