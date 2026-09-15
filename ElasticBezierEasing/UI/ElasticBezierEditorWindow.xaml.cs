using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ElasticBezierEasing.Core;

namespace ElasticBezierEasing.UI;

/// <summary>
/// 指数減衰型エラスティックベジェ編集ウィンドウ。
///
/// 対応モード:
/// - easeOutElastic
/// - easeInElastic
/// - easeInOutElastic
///
/// 特徴:
/// - 上部水平バー（○──────○）で強さ（Amplitude）と持続減衰を直感操作
/// - 谷ハンドル（●──────○）で周期/振動回数（Oscillation）と減衰深さを直感操作
/// - モード即時切替（Ease Out / In / In-Out）
/// - 初期カーブとの比較用ゴースト線表示
/// - カーブコードによるシリアル化・クリップボード共有
/// - リアルタイムのアニメーションプレビュートラック
/// </summary>
public partial class ElasticBezierEditorWindow : Window
{
    private readonly EditorViewModel _vm;
    private readonly ElasticBezierParameters _initialParameters;

    public ElasticBezierParameters ResultParameters { get; private set; }

    private UIElement? _captured;
    private Point _dragStartPos;

    private const double SampleCount = 280;
    private const double AxisMargin = 36.0;

    // ビュー操作（ズーム・パン）
    private double _zoom = 1.0;
    private double _panX = 0.0;
    private double _panY = 0.0;
    private bool _isPanning = false;
    private Point _panStartMouse;
    private double _panStartPanX;
    private double _panStartPanY;

    private readonly DispatcherTimer _previewTimer;
    private double _previewProgress = 0.0;
    private bool _isPreviewRunning = false;

    public ElasticBezierEditorWindow(ElasticBezierParameters initial)
    {
        InitializeComponent();

        _initialParameters = initial.Clone();
        ResultParameters = initial.Clone();
        _vm = new EditorViewModel(initial.Clone());
        DataContext = _vm;

        // プレビューアニメーションタイマー (60fps)
        _previewTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _previewTimer.Tick += PreviewTimer_Tick;

        _vm.PropertyChanged += (s, e) =>
        {
            RedrawGraph();
            if (e.PropertyName is nameof(EditorViewModel.Amplitude) or nameof(EditorViewModel.Oscillation) or nameof(EditorViewModel.Damping) or nameof(EditorViewModel.Mode))
            {
                TriggerPreview();
            }
        };

        Loaded += (s, e) =>
        {
            RedrawGraph();
            TriggerPreview();
        };

        Unloaded += (s, e) =>
        {
            _previewTimer.Stop();
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ResultParameters = _vm.ToParameters();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void GraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawGraph();

    // ─────────────────────────────────────────────────────────
    // 座標系とグラフ変換
    // ─────────────────────────────────────────────────────────

    private (double yMin, double yMax) GetValueRange(ElasticBezierParameters p)
    {
        double yMin = 0.0, yMax = 1.0;
        for (int i = 0; i <= SampleCount; i++)
        {
            double t = i / SampleCount;
            double v = ElasticEasingConsts.CalculateEasingRate(t, p);
            if (v < yMin) yMin = v;
            if (v > yMax) yMax = v;
        }

        // 初期カーブもレンジに含めて比較しやすくする
        for (int i = 0; i <= SampleCount; i += 10)
        {
            double t = i / SampleCount;
            double v = ElasticEasingConsts.CalculateEasingRate(t, _initialParameters);
            if (v < yMin) yMin = v;
            if (v > yMax) yMax = v;
        }

        double span = Math.Max(1.0, yMax - yMin);
        double pad = span * 0.18;
        return (yMin - pad, yMax + pad);
    }

    private Point ToCanvas(double t, double v, double w, double h, double yMin, double yMax)
    {
        double x = AxisMargin + Math.Clamp(t, 0.0, 1.0) * (w - AxisMargin * 2);
        double y = (h - AxisMargin) - (v - yMin) / (yMax - yMin) * (h - AxisMargin * 2);

        // ズームとパンを中心基準で適用
        double cx = w / 2.0;
        double cy = h / 2.0;
        double zx = cx + (x - cx) * _zoom + _panX;
        double zy = cy + (y - cy) * _zoom + _panY;
        return new Point(zx, zy);
    }

    private (double t, double v) FromCanvas(Point pos, double w, double h, double yMin, double yMax)
    {
        double cx = w / 2.0;
        double cy = h / 2.0;
        double origX = cx + (pos.X - _panX - cx) / _zoom;
        double origY = cy + (pos.Y - _panY - cy) / _zoom;

        double t = (origX - AxisMargin) / (w - AxisMargin * 2);
        double v = yMin + ((h - AxisMargin) - origY) / (h - AxisMargin * 2) * (yMax - yMin);
        return (t, v);
    }

    // ─────────────────────────────────────────────────────────
    // 描画ロジック
    // ─────────────────────────────────────────────────────────

    private void RedrawGraph()
    {
        double w = GraphCanvas.ActualWidth;
        double h = GraphCanvas.ActualHeight;
        if (w <= 10 || h <= 10) return;

        var p = _vm.ToParameters();
        var (yMin, yMax) = GetValueRange(p);

        // 1. 背景グリッド線の描画
        DrawBackgroundGrid(w, h);

        // 2. 基準線（0.0 と 1.0）
        ZeroLinePath.Data = CreateHorizontalLineGeo(0.0, w, h, yMin, yMax);
        OneLinePath.Data = CreateHorizontalLineGeo(1.0, w, h, yMin, yMax);

        // 3. 比較用ゴーストカーブ（変更前）
        GhostCurvePath.Data = CreateCurveGeometry(_initialParameters, w, h, yMin, yMax);

        // 4. メインカーブ
        MainCurvePath.Data = CreateCurveGeometry(p, w, h, yMin, yMax);

        // 5. 始点 (0,0) と終点 (1,1) アンカー
        var startPt = ToCanvas(0.0, 0.0, w, h, yMin, yMax);
        Canvas.SetLeft(StartAnchor, startPt.X - StartAnchor.Width / 2);
        Canvas.SetTop(StartAnchor, startPt.Y - StartAnchor.Height / 2);

        var endPt = ToCanvas(1.0, 1.0, w, h, yMin, yMax);
        Canvas.SetLeft(EndAnchor, endPt.X - EndAnchor.Width / 2);
        Canvas.SetTop(EndAnchor, endPt.Y - EndAnchor.Height / 2);

        // 6. 各モードに応じたハンドル位置計算
        double period = p.Oscillation <= 0.01 ? 0.3 : (1.0 / p.Oscillation);

        // カーブ全体の最大値を検出し、トップバーが曲線の山（ピーク）と絶対に重ならず頭上に美しく浮くように配置する
        double curveMaxV = 1.0;
        for (int i = 0; i <= SampleCount; i++)
        {
            double cv = ElasticEasingConsts.CalculateEasingRate(i / SampleCount, p);
            if (cv > curveMaxV) curveMaxV = cv;
        }
        double peakCanvasY = ToCanvas(0, curveMaxV, w, h, yMin, yMax).Y;
        // ピーク頂点より26px上（画面上部）に配置。キャンバス上端16pxを下限とする
        double barY = Math.Max(16.0, peakCanvasY - 26.0);

        if (p.Mode == ElasticMode.EaseOut)
        {
            double leftT = 0.06;
            double rightT = Math.Clamp(1.0 - p.Damping * 0.45, 0.42, 0.94);
            var barLeftPt = new Point(ToCanvas(leftT, 0, w, h, yMin, yMax).X, barY);
            var barRightPt = new Point(ToCanvas(rightT, 0, w, h, yMin, yMax).X, barY);

            TopBarLine.X1 = barLeftPt.X; TopBarLine.Y1 = barLeftPt.Y;
            TopBarLine.X2 = barRightPt.X; TopBarLine.Y2 = barRightPt.Y;
            Canvas.SetLeft(TopBarLeftKnob, barLeftPt.X - TopBarLeftKnob.Width / 2);
            Canvas.SetTop(TopBarLeftKnob, barLeftPt.Y - TopBarLeftKnob.Height / 2);
            Canvas.SetLeft(TopBarRightKnob, barRightPt.X - TopBarRightKnob.Width / 2);
            Canvas.SetTop(TopBarRightKnob, barRightPt.Y - TopBarRightKnob.Height / 2);

            // 谷ハンドル
            double tValley = Math.Clamp(period * 1.0, 0.08, 0.95);
            double vValley = ElasticEasingConsts.CalculateEasingRate(tValley, p);
            var valleyAnchorPt = ToCanvas(tValley, vValley, w, h, yMin, yMax);
            Canvas.SetLeft(ValleyAnchor, valleyAnchorPt.X - ValleyAnchor.Width / 2);
            Canvas.SetTop(ValleyAnchor, valleyAnchorPt.Y - ValleyAnchor.Height / 2);

            double stemLength = 46.0;
            var valleyKnobPt = new Point(valleyAnchorPt.X, valleyAnchorPt.Y + stemLength);
            ValleyStemLine.X1 = valleyAnchorPt.X; ValleyStemLine.Y1 = valleyAnchorPt.Y;
            ValleyStemLine.X2 = valleyKnobPt.X;   ValleyStemLine.Y2 = valleyKnobPt.Y;
            Canvas.SetLeft(ValleyKnob, valleyKnobPt.X - ValleyKnob.Width / 2);
            Canvas.SetTop(ValleyKnob, valleyKnobPt.Y - ValleyKnob.Height / 2);
        }
        else if (p.Mode == ElasticMode.EaseIn)
        {
            // 谷ハンドル
            double tValley = Math.Clamp(1.0 - period * 1.0, 0.05, 0.92);
            double vValley = ElasticEasingConsts.CalculateEasingRate(tValley, p);
            var valleyAnchorPt = ToCanvas(tValley, vValley, w, h, yMin, yMax);
            Canvas.SetLeft(ValleyAnchor, valleyAnchorPt.X - ValleyAnchor.Width / 2);
            Canvas.SetTop(ValleyAnchor, valleyAnchorPt.Y - ValleyAnchor.Height / 2);

            double stemLength = 46.0;
            var valleyKnobPt = new Point(valleyAnchorPt.X, valleyAnchorPt.Y + stemLength);
            ValleyStemLine.X1 = valleyAnchorPt.X; ValleyStemLine.Y1 = valleyAnchorPt.Y;
            ValleyStemLine.X2 = valleyKnobPt.X;   ValleyStemLine.Y2 = valleyKnobPt.Y;
            Canvas.SetLeft(ValleyKnob, valleyKnobPt.X - ValleyKnob.Width / 2);
            Canvas.SetTop(ValleyKnob, valleyKnobPt.Y - ValleyKnob.Height / 2);

            double leftT = Math.Clamp(p.Damping * 0.45, 0.06, 0.58);
            double rightT = 0.94;
            var barLeftPt = new Point(ToCanvas(leftT, 0, w, h, yMin, yMax).X, barY);
            var barRightPt = new Point(ToCanvas(rightT, 0, w, h, yMin, yMax).X, barY);

            TopBarLine.X1 = barLeftPt.X; TopBarLine.Y1 = barLeftPt.Y;
            TopBarLine.X2 = barRightPt.X; TopBarLine.Y2 = barRightPt.Y;
            Canvas.SetLeft(TopBarLeftKnob, barLeftPt.X - TopBarLeftKnob.Width / 2);
            Canvas.SetTop(TopBarLeftKnob, barLeftPt.Y - TopBarLeftKnob.Height / 2);
            Canvas.SetLeft(TopBarRightKnob, barRightPt.X - TopBarRightKnob.Width / 2);
            Canvas.SetTop(TopBarRightKnob, barRightPt.Y - TopBarRightKnob.Height / 2);
        }
        else // EaseInOut
        {
            double periodIO = period * 1.5;
            double tValley = Math.Clamp(0.5 - periodIO * 0.25, 0.08, 0.45);
            double vValley = ElasticEasingConsts.CalculateEasingRate(tValley, p);
            var valleyAnchorPt = ToCanvas(tValley, vValley, w, h, yMin, yMax);
            Canvas.SetLeft(ValleyAnchor, valleyAnchorPt.X - ValleyAnchor.Width / 2);
            Canvas.SetTop(ValleyAnchor, valleyAnchorPt.Y - ValleyAnchor.Height / 2);

            double stemLength = 46.0;
            var valleyKnobPt = new Point(valleyAnchorPt.X, valleyAnchorPt.Y + stemLength);
            ValleyStemLine.X1 = valleyAnchorPt.X; ValleyStemLine.Y1 = valleyAnchorPt.Y;
            ValleyStemLine.X2 = valleyKnobPt.X;   ValleyStemLine.Y2 = valleyKnobPt.Y;
            Canvas.SetLeft(ValleyKnob, valleyKnobPt.X - ValleyKnob.Width / 2);
            Canvas.SetTop(ValleyKnob, valleyKnobPt.Y - ValleyKnob.Height / 2);

            var barLeftPt = new Point(ToCanvas(0.1, 0, w, h, yMin, yMax).X, barY);
            var barRightPt = new Point(ToCanvas(0.9, 0, w, h, yMin, yMax).X, barY);

            TopBarLine.X1 = barLeftPt.X; TopBarLine.Y1 = barLeftPt.Y;
            TopBarLine.X2 = barRightPt.X; TopBarLine.Y2 = barRightPt.Y;
            Canvas.SetLeft(TopBarLeftKnob, barLeftPt.X - TopBarLeftKnob.Width / 2);
            Canvas.SetTop(TopBarLeftKnob, barLeftPt.Y - TopBarLeftKnob.Height / 2);
            Canvas.SetLeft(TopBarRightKnob, barRightPt.X - TopBarRightKnob.Width / 2);
            Canvas.SetTop(TopBarRightKnob, barRightPt.Y - TopBarRightKnob.Height / 2);
        }
    }

    private void DrawBackgroundGrid(double w, double h)
    {
        GridCanvas.Children.Clear();
        double baseGridStep = 42.0;
        double gridStep = baseGridStep * _zoom;
        while (gridStep < 24.0) gridStep *= 2.0;
        while (gridStep > 84.0) gridStep /= 2.0;

        double cx = w / 2.0;
        double cy = h / 2.0;
        double offsetX = (cx + _panX) % gridStep;
        if (offsetX < 0) offsetX += gridStep;
        double offsetY = (cy + _panY) % gridStep;
        if (offsetY < 0) offsetY += gridStep;

        for (double x = offsetX; x < w; x += gridStep)
        {
            var line = new Line
            {
                X1 = x, Y1 = 0,
                X2 = x, Y2 = h,
                Stroke = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
                StrokeThickness = 1.0
            };
            GridCanvas.Children.Add(line);
        }

        for (double y = offsetY; y < h; y += gridStep)
        {
            var line = new Line
            {
                X1 = 0, Y1 = y,
                X2 = w, Y2 = y,
                Stroke = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
                StrokeThickness = 1.0
            };
            GridCanvas.Children.Add(line);
        }
    }

    private StreamGeometry CreateCurveGeometry(ElasticBezierParameters p, double w, double h, double yMin, double yMax)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i <= SampleCount; i++)
            {
                double t = i / SampleCount;
                double v = ElasticEasingConsts.CalculateEasingRate(t, p);
                var pt = ToCanvas(t, v, w, h, yMin, yMax);
                if (i == 0) ctx.BeginFigure(pt, false, false);
                else ctx.LineTo(pt, true, false);
            }
        }
        geo.Freeze();
        return geo;
    }

    private Geometry CreateHorizontalLineGeo(double v, double w, double h, double yMin, double yMax)
    {
        if (v < yMin || v > yMax) return Geometry.Empty;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(ToCanvas(0, v, w, h, yMin, yMax), false, false);
            ctx.LineTo(ToCanvas(1, v, w, h, yMin, yMax), true, false);
        }
        geo.Freeze();
        return geo;
    }

    // ─────────────────────────────────────────────────────────
    // マウスドラッグ操作
    // ─────────────────────────────────────────────────────────

    private void GraphCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as UIElement;
        if (src == TopBarLeftKnob || src == TopBarRightKnob || src == TopBarLine ||
            src == ValleyKnob || src == ValleyAnchor)
        {
            _captured = src;
            src.CaptureMouse();
            _dragStartPos = e.GetPosition(GraphCanvas);
            e.Handled = true;
        }
    }

    private void GraphCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_captured != null)
        {
            _captured.ReleaseMouseCapture();
            _captured = null;
            e.Handled = true;
        }
    }

    private void GraphCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _panStartMouse = e.GetPosition(GraphCanvas);
            _panStartPanX = _panX;
            _panStartPanY = _panY;
            GraphCanvas.CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    private void GraphCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _isPanning)
        {
            _isPanning = false;
            GraphCanvas.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            e.Handled = true;
        }
    }

    private void GraphCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            double zoomFactor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
            double newZoom = Math.Clamp(_zoom * zoomFactor, 0.2, 10.0);
            if (Math.Abs(newZoom - _zoom) > 0.0001)
            {
                Point mousePos = e.GetPosition(GraphCanvas);
                double cx = GraphCanvas.ActualWidth / 2.0;
                double cy = GraphCanvas.ActualHeight / 2.0;

                double origX = cx + (mousePos.X - _panX - cx) / _zoom;
                double origY = cy + (mousePos.Y - _panY - cy) / _zoom;

                _zoom = newZoom;

                _panX = mousePos.X - cx - (origX - cx) * _zoom;
                _panY = mousePos.Y - cy - (origY - cy) * _zoom;

                RedrawGraph();
            }
            e.Handled = true;
        }
    }

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        _zoom = 1.0;
        _panX = 0.0;
        _panY = 0.0;
        RedrawGraph();
    }

    private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            Point cur = e.GetPosition(GraphCanvas);
            _panX = _panStartPanX + (cur.X - _panStartMouse.X);
            _panY = _panStartPanY + (cur.Y - _panStartMouse.Y);
            RedrawGraph();
            return;
        }

        if (_captured == null) return;

        double w = GraphCanvas.ActualWidth;
        double h = GraphCanvas.ActualHeight;
        if (w <= 10 || h <= 10) return;

        var p = _vm.ToParameters();
        var (yMin, yMax) = GetValueRange(p);
        var currentPos = e.GetPosition(GraphCanvas);
        var (t, v) = FromCanvas(currentPos, w, h, yMin, yMax);

        // 1. 上部バー操作
        if (ReferenceEquals(_captured, TopBarLine) || ReferenceEquals(_captured, TopBarLeftKnob))
        {
            // 上下ドラッグで Amplitude（強さ）を調整
            double deltaY = _dragStartPos.Y - currentPos.Y;
            if (Math.Abs(deltaY) > 1)
            {
                double dAmp = deltaY * 0.008;
                _vm.Amplitude = Math.Clamp(Math.Round(_vm.Amplitude + dAmp, 2), 0.1, 2.5);
                _dragStartPos = currentPos;
            }
        }
        else if (ReferenceEquals(_captured, TopBarRightKnob))
        {
            double deltaY = _dragStartPos.Y - currentPos.Y;
            if (Math.Abs(deltaY) > 1)
            {
                double dAmp = deltaY * 0.008;
                _vm.Amplitude = Math.Clamp(Math.Round(_vm.Amplitude + dAmp, 2), 0.1, 2.5);
            }

            // 左右で Damping を調整
            double newDamp = (1.0 - Math.Clamp(t, 0.4, 0.98)) / 0.45;
            _vm.Damping = Math.Clamp(Math.Round(newDamp, 2), 0.0, 1.0);
            _dragStartPos = currentPos;
        }
        // 2. 谷ハンドル操作
        else if (ReferenceEquals(_captured, ValleyAnchor) || ReferenceEquals(_captured, ValleyKnob))
        {
            // 左右ドラッグで Oscillation を調整
            double clampedT = Math.Clamp(t, 0.08, 0.92);
            double newPeriod = clampedT / 1.25;
            if (newPeriod > 0.05)
            {
                double newOsc = 1.0 / newPeriod;
                _vm.Oscillation = Math.Clamp(Math.Round(newOsc, 2), 0.5, 6.0);
            }

            // 谷ノブの上下ドラッグで Damping を微調整
            if (ReferenceEquals(_captured, ValleyKnob))
            {
                double deltaY = currentPos.Y - _dragStartPos.Y;
                if (Math.Abs(deltaY) > 2)
                {
                    double dDamp = deltaY * 0.006;
                    _vm.Damping = Math.Clamp(Math.Round(_vm.Damping + dDamp, 2), 0.0, 1.0);
                    _dragStartPos = currentPos;
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    // ツールバーアクション
    // ─────────────────────────────────────────────────────────

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_vm.CurveCode);
            MessageBox.Show($"カーブコード「{_vm.CurveCode}」をクリップボードにコピーしました。",
                "エラスティックベジェ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { }
    }

    private void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText().Trim();
                if (_vm.TryApplyCode(text))
                {
                    TriggerPreview();
                }
                else
                {
                    MessageBox.Show("有効なカーブコードではありません。\n例: 3442686 または 1.0, 3.33, 0.5",
                        "読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch { }
    }

    private void CurveCodeTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _vm.TryApplyCode(CurveCodeTextBox.Text);
            TriggerPreview();
            Keyboard.ClearFocus();
        }
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        PresetPopup.IsOpen = !PresetPopup.IsOpen;
    }

    private void ClosePopup_Click(object sender, RoutedEventArgs e)
    {
        PresetPopup.IsOpen = false;
        TriggerPreview();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.ApplyPreset(ElasticBezierParameters.Presets.Normal);
        TriggerPreview();
    }

    // ─────────────────────────────────────────────────────────
    // リアルタイムプレビューアニメーション
    // ─────────────────────────────────────────────────────────

    private void PlayPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        TriggerPreview();
    }

    private void TriggerPreview()
    {
        _previewProgress = 0.0;
        _isPreviewRunning = true;
        _previewTimer.Start();
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isPreviewRunning) return;

        _previewProgress += 0.016; // 約1秒のアニメーション
        if (_previewProgress >= 1.0)
        {
            _previewProgress = 1.0;
            _isPreviewRunning = false;
            _previewTimer.Stop();
        }

        double trackW = PreviewTrackCanvas.ActualWidth - PreviewBall.Width - 4;
        if (trackW <= 0) trackW = 200;

        var p = _vm.ToParameters();
        double rate = ElasticEasingConsts.CalculateEasingRate(_previewProgress, p);

        double ballX = 2 + Math.Clamp(rate * trackW, -15, trackW + 30);
        Canvas.SetLeft(PreviewBall, ballX);
    }
}

/// <summary>エディタ用ViewModel。モード管理・パラメータバインディング・シリアル化を担当。</summary>
internal sealed class EditorViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private ElasticMode _mode = ElasticMode.EaseOut;
    public ElasticMode Mode
    {
        get => _mode;
        set
        {
            if (_mode != value)
            {
                _mode = value;
                OnChanged();
                OnChanged(nameof(IsEaseOut));
                OnChanged(nameof(IsEaseIn));
                OnChanged(nameof(IsEaseInOut));
                UpdateCurveCode();
            }
        }
    }

    public bool IsEaseOut
    {
        get => Mode == ElasticMode.EaseOut;
        set { if (value) Mode = ElasticMode.EaseOut; }
    }

    public bool IsEaseIn
    {
        get => Mode == ElasticMode.EaseIn;
        set { if (value) Mode = ElasticMode.EaseIn; }
    }

    public bool IsEaseInOut
    {
        get => Mode == ElasticMode.EaseInOut;
        set { if (value) Mode = ElasticMode.EaseInOut; }
    }

    private double _amplitude = 1.0;
    public double Amplitude
    {
        get => _amplitude;
        set
        {
            double clamped = Math.Clamp(Math.Round(value, 2), 0.1, 2.5);
            if (Math.Abs(_amplitude - clamped) < 1e-5) return;
            _amplitude = clamped;
            OnChanged();
            OnChanged(nameof(AmplitudeText));
            UpdateCurveCode();
        }
    }

    private double _oscillation = 3.33;
    public double Oscillation
    {
        get => _oscillation;
        set
        {
            double clamped = Math.Clamp(Math.Round(value, 2), 0.5, 6.0);
            if (Math.Abs(_oscillation - clamped) < 1e-5) return;
            _oscillation = clamped;
            OnChanged();
            OnChanged(nameof(OscillationText));
            UpdateCurveCode();
        }
    }

    private double _damping = 0.50;
    public double Damping
    {
        get => _damping;
        set
        {
            double clamped = Math.Clamp(Math.Round(value, 2), 0.0, 1.0);
            if (Math.Abs(_damping - clamped) < 1e-5) return;
            _damping = clamped;
            OnChanged();
            OnChanged(nameof(DampingText));
            UpdateCurveCode();
        }
    }

    public string AmplitudeText => Amplitude.ToString("F2", CultureInfo.InvariantCulture);
    public string OscillationText => Oscillation.ToString("F2", CultureInfo.InvariantCulture);
    public string DampingText => Damping.ToString("F2", CultureInfo.InvariantCulture);

    private string _curveCode = "3442686";
    public string CurveCode
    {
        get => _curveCode;
        set
        {
            if (_curveCode != value)
            {
                _curveCode = value;
                OnChanged();
            }
        }
    }

    public ObservableCollection<UserPreset> UserPresets { get; } = new();

    private UserPreset? _selectedUserPreset;
    public UserPreset? SelectedUserPreset
    {
        get => _selectedUserPreset;
        set
        {
            _selectedUserPreset = value;
            OnChanged();
            if (value != null)
            {
                Amplitude = value.Amplitude;
                Oscillation = value.Oscillation;
                Damping = value.Damping;
                Mode = value.Mode;
                NewPresetName = value.Name;
            }
        }
    }

    private string _newPresetName = "新規プリセット";
    public string NewPresetName
    {
        get => _newPresetName;
        set { _newPresetName = value; OnChanged(); }
    }

    public RelayCommand ApplySoftCommand { get; }
    public RelayCommand ApplyNormalCommand { get; }
    public RelayCommand ApplyStrongCommand { get; }
    public RelayCommand SavePresetCommand { get; }
    public RelayCommand DeletePresetCommand { get; }

    public EditorViewModel(ElasticBezierParameters initial)
    {
        _amplitude = initial.Amplitude;
        _oscillation = initial.Oscillation;
        _damping = initial.Damping;
        _mode = initial.Mode;
        UpdateCurveCode();

        ApplySoftCommand = new RelayCommand(_ => ApplyPreset(ElasticBezierParameters.Presets.Soft));
        ApplyNormalCommand = new RelayCommand(_ => ApplyPreset(ElasticBezierParameters.Presets.Normal));
        ApplyStrongCommand = new RelayCommand(_ => ApplyPreset(ElasticBezierParameters.Presets.Strong));

        SavePresetCommand = new RelayCommand(_ => SavePreset(), _ => !string.IsNullOrWhiteSpace(NewPresetName));
        DeletePresetCommand = new RelayCommand(_ => DeletePreset(), _ => SelectedUserPreset != null);

        foreach (var preset in UserPresetStore.LoadAll())
            UserPresets.Add(preset);
    }

    public void ApplyPreset(ElasticBezierParameters p)
    {
        Amplitude = p.Amplitude;
        Oscillation = p.Oscillation;
        Damping = p.Damping;
        Mode = p.Mode;
    }

    /// <summary>
    /// パラメーターから7桁〜8桁のコード（例: 3442686）を生成。
    /// </summary>
    private void UpdateCurveCode()
    {
        int ampCode = Math.Clamp((int)Math.Round(_amplitude * 34.0), 0, 99);
        int oscCode = Math.Clamp((int)Math.Round(_oscillation * 12.6), 1, 99);
        int dampCode = Math.Clamp((int)Math.Round(_damping * 999.0), 0, 999);

        // EaseOut は 7桁コード (例: 3442686)、In/InOut はプレフィックス付き
        string baseCode = $"{ampCode:D2}{oscCode:D2}{dampCode:D3}";
        _curveCode = Mode switch
        {
            ElasticMode.EaseIn => $"I{baseCode}",
            ElasticMode.EaseInOut => $"IO{baseCode}",
            _ => baseCode
        };
        OnChanged(nameof(CurveCode));
    }

    /// <summary>
    /// 文字列コードからパラメーターとモードを復元。
    /// </summary>
    public bool TryApplyCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        code = code.Trim();

        // 1. カンマ区切り形式のチェック (例: "1.0, 3.33, 0.5")
        var parts = code.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double o) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
        {
            Amplitude = a;
            Oscillation = o;
            Damping = d;
            if (parts.Length >= 4)
            {
                if (parts[3].Contains("In", StringComparison.OrdinalIgnoreCase) && parts[3].Contains("Out", StringComparison.OrdinalIgnoreCase))
                    Mode = ElasticMode.EaseInOut;
                else if (parts[3].Contains("In", StringComparison.OrdinalIgnoreCase))
                    Mode = ElasticMode.EaseIn;
                else
                    Mode = ElasticMode.EaseOut;
            }
            return true;
        }

        // 2. プレフィックス付き/なしコードのチェック
        ElasticMode targetMode = ElasticMode.EaseOut;
        string numPart = code;
        if (code.StartsWith("IO", StringComparison.OrdinalIgnoreCase))
        {
            targetMode = ElasticMode.EaseInOut;
            numPart = code.Substring(2);
        }
        else if (code.StartsWith("I", StringComparison.OrdinalIgnoreCase))
        {
            targetMode = ElasticMode.EaseIn;
            numPart = code.Substring(1);
        }

        if (numPart.Length == 7 && int.TryParse(numPart, out _))
        {
            if (int.TryParse(numPart.Substring(0, 2), out int aCode) &&
                int.TryParse(numPart.Substring(2, 2), out int oCode) &&
                int.TryParse(numPart.Substring(4, 3), out int dCode))
            {
                Amplitude = aCode / 34.0;
                Oscillation = oCode / 12.6;
                Damping = dCode / 999.0;
                Mode = targetMode;
                return true;
            }
        }

        return false;
    }

    private void SavePreset()
    {
        var preset = UserPreset.FromParameters(NewPresetName, ToParameters());
        UserPresetStore.Save(preset);

        var existing = UserPresets.FirstOrDefault(x => x.Name == preset.Name);
        if (existing != null)
        {
            int idx = UserPresets.IndexOf(existing);
            UserPresets[idx] = preset;
        }
        else
        {
            UserPresets.Add(preset);
        }
        SelectedUserPreset = preset;
    }

    private void DeletePreset()
    {
        if (SelectedUserPreset == null) return;
        UserPresetStore.Delete(SelectedUserPreset);
        UserPresets.Remove(SelectedUserPreset);
        SelectedUserPreset = null;
    }

    public ElasticBezierParameters ToParameters() => new(Amplitude, Oscillation, Damping, Mode);
}


