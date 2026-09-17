# ElasticBezierEasing
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](#)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](#)
[![Downloads](https://img.shields.io/github/downloads/leftcontroller0518/ElasticBezierEasing/total)](https://github.com/leftcontroller0518/ElasticBezierEasing/releases/latest)
<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/fddef2ad-66c0-48b7-9d0b-b0cb2e676edf" />


> [!NOTE]
> このプラグインはharmonyというライブラリを使用しています。
> ライセンスは[こちらをご参照ください](https://github.com/leftcontroller0518/ElasticBezierEasing/blob/main/Harmony_LICENSE)。

## 機能
- YMM4のイージング選択に「エラスティックベジェ」を追加
- クリック→「編集...」で専用のイージング編集ウィンドウ（WPF）を開く
- グラフ上のハンドルをドラッグしてカーブを直感的に編集可能
- $$f(t) = t + A・sin(2π・N・t)・(1-t)^P$$という式でイージングを計算するため、
  0〜1を超えるオーバーシュート／アンダーシュートに自然に対応
- 編集結果をリアルタイムでグラフ表示
- Amplitude（強さ）、Oscillation（振動回数）、Damping（減衰）を調整可能
- Soft / Normal / Strong の組み込みプリセット
- 名前を付けて任意のプリセットを保存・読込可能（`Presets` フォルダにJSONで保存）

## イージング関数について
$$
\Huge
\begin{array}{l}
f(t) = t + A \cdot \sin(2\pi \cdot f \cdot t) \cdot (1 - t)^d
\end{array}
$$

- $$f(0) = 0$$, $$f(1) = 1$$を厳密に満たすため、通常のキーフレーム間イージングとしてそのまま使えます。
- `Amplitude`：オーバーシュート／アンダーシュートの強さ。負値にすると最初のスイング方向を反転できます。
- `Oscillation`：0〜1区間に含まれる振動回数。0.5なら「1回だけ行き過ぎて戻る」動きになります。
- `Damping`（0〜1のUI値）：内部的には `1.0 + Damping * 7.0` の減衰指数に変換され、大きいほど早く収束します。

## プロジェクト構成
```
ElasticBezierEasing/
├─ Directory.Build.props      … YMM4DirPath（開発者の方は要編集）
├─ ElasticBezierEasing.slnx
├─ LICENSE / Harmony_LICENSE
└─ ElasticBezierEasing/
   ├─ ElasticBezierEasing.csproj
   ├─ Core/
   │  ├─ AddEasingPlugin.cs        … IPlugin実装、Harmony初期化
   │  ├─ ElasticBezierParameters.cs… パラメーターモデル・プリセット
   │  ├─ ElasticEasingConsts.cs    … ID変換・イージング計算式
   │  ├─ ElasticEasingConfig.cs    … BaseId設定(config.json)
   │  └─ ElasticConfigStore.cs     … configIndex⇔パラメーターの永続化
   ├─ Patches/
   │  ├─ AnimationGetEasingRatePatch.cs … イージング計算を差し込む
   │  ├─ AnimationTypeExPatch.cs        … IsBeizer等の判定を上書き
   │  ├─ ContextMenuOpenedPatch.cs      … 右クリックメニュー追加・編集ウィンドウ起動
   │  ├─ ConverterPatches.cs            … ラベル・ツールチップ表示
   │  └─ ToExoStringPatch.cs            … exo書き出し時のフォールバック
   └─ UI/
      ├─ ElasticBezierEditorWindow.xaml(.cs) … グラフエディタ本体
      ├─ RelayCommand.cs
      └─ UserPresetStore.cs               … 名前付きプリセットの保存/読込
```

## ID競合について
本プラグインは `AnimationType` に独自ID（デフォルト: 1,300,000〜2,299,999）を割り当てています。

ほとんどないとは思われますが、
他のプラグインとIDが競合する場合は、プラグインフォルダ内の `config.json` を開き `BaseId` を書き換えてから
YMM4を再起動してください。

## ライセンス
MIT License（`LICENSE`参照）。Lib.Harmony (MIT License) を使用しています（`Harmony_LICENSE`参照）。

### 追記
Issues、プルリクエストなど大歓迎です。
