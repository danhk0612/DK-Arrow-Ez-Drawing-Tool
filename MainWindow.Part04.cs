using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfCursors = System.Windows.Input.Cursors;
using IoPath = System.IO.Path;

namespace ArrowOverlayTool;

public partial class MainWindow : Window
{
    private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingEditor)
            return;

        var selected = GetSelectedArrows();
        if (selected.Count == 0)
            return;

        var value = Math.Round(ThicknessSlider.Value, 1);
        foreach (var arrow in selected)
            arrow.Thickness = value;

        _updatingEditor = true;
        ThicknessTextBox.Text = value.ToString("0.#");
        _updatingEditor = false;
        ArrowList.Items.Refresh();
        RedrawAllArrows();
        RememberLastCreatedArrowStyle(selected);
    }

    private void ThicknessTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingEditor)
            return;

        var selected = GetSelectedArrows();
        if (selected.Count == 0 || !double.TryParse(ThicknessTextBox.Text, out var value))
            return;

        value = Math.Clamp(value, 1, 30);
        foreach (var arrow in selected)
            arrow.Thickness = value;

        _updatingEditor = true;
        ThicknessSlider.Value = value;
        _updatingEditor = false;
        ArrowList.Items.Refresh();
        RedrawAllArrows();
        RememberLastCreatedArrowStyle(selected);
    }

    private void DeleteArrowButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedArrows();
        if (selected.Count == 0)
            return;

        EndPointDrag();
        var firstIndex = selected.Select(a => _arrows.IndexOf(a)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        foreach (var arrow in selected)
            _arrows.Remove(arrow);

        _selectedArrow = null;
        if (_arrows.Count > 0)
            ArrowList.SelectedIndex = Math.Min(firstIndex, _arrows.Count - 1);
        else
            UpdateEditorFromSelection();

        RedrawAllArrows();
        SetStatus(selected.Count == 1 ? "화살표를 삭제했습니다." : $"화살표 {selected.Count}개를 삭제했습니다.");
    }

    private void SaveProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backgroundBitmap is null || _backgroundBytes is null)
        {
            SetStatus("저장할 작업이 없습니다.");
            return;
        }

        var dialog = new WpfSaveFileDialog
        {
            Title = "작업 저장",
            Filter = "Arrow Overlay 작업 파일|*.arrowproj",
            DefaultExt = ".arrowproj",
            AddExtension = true,
            FileName = GetBackgroundBaseName() + ".arrowproj"
        };

        if (dialog.ShowDialog() != true)
            return;

        var project = new ArrowProject
        {
            CanvasWidth = _backgroundBitmap.PixelWidth,
            CanvasHeight = _backgroundBitmap.PixelHeight,
            BackgroundFileName = _backgroundFileName,
            BackgroundBase64 = Convert.ToBase64String(_backgroundBytes),
            Arrows = _arrows.ToList()
        };

        var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dialog.FileName, json);
        SetStatus($"작업 저장 완료: {IoPath.GetFileName(dialog.FileName)}");
    }

    private void LoadProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "작업 불러오기",
            Filter = "Arrow Overlay 작업 파일|*.arrowproj|모든 파일|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        var json = File.ReadAllText(dialog.FileName);
        var project = JsonSerializer.Deserialize<ArrowProject>(json);
        if (project is null || string.IsNullOrWhiteSpace(project.BackgroundBase64))
        {
            SetStatus("올바른 작업 파일이 아닙니다.");
            return;
        }

        var backgroundBytes = Convert.FromBase64String(project.BackgroundBase64);
        LoadBackground(backgroundBytes, project.BackgroundFileName ?? "embedded-background");

        _arrows.Clear();
        foreach (var arrow in project.Arrows)
            _arrows.Add(arrow);

        _arrowCounter = Math.Max(1, _arrows.Count + 1);
        _lastCreatedArrowId = null;
        ClearDraft();
        EndPointDrag();
        _isDrawing = false;
        DrawingCanvas.Cursor = WpfCursors.Arrow;
        ArrowList.Items.Refresh();
        ArrowList.SelectedIndex = _arrows.Count > 0 ? 0 : -1;
        RedrawAllArrows();
        SetStatus($"작업 불러오기 완료: {IoPath.GetFileName(dialog.FileName)}");
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backgroundBitmap is null)
        {
            SetStatus("먼저 배경 이미지를 여세요.");
            return;
        }

        var includeBackground = IncludeBackgroundCheckBox.IsChecked == true;
        var exportSuffix = ExportSuffixTextBox.Text + (includeBackground ? "_bg" : string.Empty);
        var dialog = new WpfSaveFileDialog
        {
            Title = includeBackground ? "배경 포함 PNG 출력" : "투명 PNG 출력",
            Filter = "PNG 이미지|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = GetBackgroundBaseName() + exportSuffix + ".png"
        };

        if (dialog.ShowDialog() != true)
            return;

        var width = _backgroundBitmap.PixelWidth;
        var height = _backgroundBitmap.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (includeBackground)
                dc.DrawImage(_backgroundBitmap, new Rect(0, 0, width, height));

            foreach (var arrow in _arrows)
                DrawArrowForExport(dc, arrow);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
        SetStatus($"PNG 출력 완료: {IoPath.GetFileName(dialog.FileName)} ({width}×{height}, {(includeBackground ? "배경 포함" : "투명 배경")})");
    }

    private static void DrawArrowForExport(DrawingContext dc, ArrowItem arrow)
    {
        if (arrow.Points.Count < 2)
            return;

        var color = (WpfColor)WpfColorConverter.ConvertFromString(arrow.ColorHex);
        var brush = new SolidColorBrush(color);
        var pen = new WpfPen(brush, arrow.Thickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Flat
        };

        var headGeometry = CalculateArrowHeadGeometry(arrow);
        var shaftPoints = arrow.Points.Select(p => new WpfPoint(p.X, p.Y)).ToList();
        if (headGeometry.IsValid)
            shaftPoints[^1] = headGeometry.ShaftEnd;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(shaftPoints[0], false, false);
            ctx.PolyLineTo(shaftPoints.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        if (!headGeometry.IsValid)
            return;

        var head = new StreamGeometry();
        using (var ctx = head.Open())
        {
            ctx.BeginFigure(headGeometry.Tip, true, true);
            ctx.LineTo(headGeometry.BaseLeft, true, true);
            ctx.LineTo(headGeometry.BaseRight, true, true);
        }
        head.Freeze();
        dc.DrawGeometry(brush, null, head);
    }

}
