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
    private void FinishDraft()
    {
        if (_draftPoints.Count < 2)
        {
            ClearDraft();
            _isDrawing = false;
            DrawingCanvas.Cursor = WpfCursors.Arrow;
            RedrawAllArrows();
            SetStatus("화살표는 최소 2개의 점이 필요합니다.");
            return;
        }

        var arrow = new ArrowItem
        {
            Name = $"화살표 {_arrowCounter++}",
            ColorHex = _settings.LastArrowColorHex,
            Thickness = _settings.LastArrowThickness,
            Points = _draftPoints.Select(p => new PointData { X = p.X, Y = p.Y }).ToList()
        };

        _arrows.Add(arrow);
        _lastCreatedArrowId = arrow.Id;
        ClearDraft();
        _isDrawing = false;
        DrawingCanvas.Cursor = WpfCursors.Arrow;
        ArrowList.SelectedItem = arrow;
        RedrawAllArrows();
        SetStatus($"{arrow.Name}을(를) 추가했습니다.");
    }

    private void RedrawDraft()
    {
        if (_draftPolyline is null)
        {
            _draftPolyline = new Polyline
            {
                Stroke = BrushFromHex("#FFFF00CC"),
                StrokeThickness = DraftStrokeThickness(),
                StrokeDashArray = new DoubleCollection { 5, 3 },
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
            DrawingCanvas.Children.Add(_draftPolyline);
        }

        _draftPolyline.StrokeThickness = DraftStrokeThickness();
        _draftPolyline.Points = new PointCollection(_draftPoints);

        while (_draftPointMarkers.Count < _draftPoints.Count)
        {
            var marker = new Ellipse
            {
                Fill = BrushFromHex("#FFFFFF00"),
                Stroke = WpfBrushes.Black,
                IsHitTestVisible = false
            };
            _draftPointMarkers.Add(marker);
            DrawingCanvas.Children.Add(marker);
        }

        var diameter = DraftPointDiameter();
        for (var i = 0; i < _draftPoints.Count; i++)
        {
            var marker = _draftPointMarkers[i];
            marker.Width = diameter;
            marker.Height = diameter;
            marker.StrokeThickness = Math.Max(1.0 / SafeViewScale(), 1.5 / SafeViewScale());
            Canvas.SetLeft(marker, _draftPoints[i].X - diameter / 2);
            Canvas.SetTop(marker, _draftPoints[i].Y - diameter / 2);
        }
    }

    private void ClearDraft()
    {
        _draftPoints.Clear();

        if (_draftPolyline is not null)
            DrawingCanvas.Children.Remove(_draftPolyline);
        if (_draftGuide is not null)
            DrawingCanvas.Children.Remove(_draftGuide);
        foreach (var marker in _draftPointMarkers)
            DrawingCanvas.Children.Remove(marker);

        _draftPolyline = null;
        _draftGuide = null;
        _draftPointMarkers.Clear();
    }

    private void RedrawAllArrows()
    {
        var draftPolyline = _draftPolyline;
        var draftGuide = _draftGuide;
        var draftMarkers = _draftPointMarkers.ToList();

        DrawingCanvas.Children.Clear();

        foreach (var arrow in _arrows)
            DrawArrowOnCanvas(arrow);

        var selectedArrows = GetSelectedArrows();
        if (!_isDrawing && selectedArrows.Count == 1)
            DrawPointHandles(selectedArrows[0]);

        if (draftPolyline is not null)
        {
            draftPolyline.StrokeThickness = DraftStrokeThickness();
            DrawingCanvas.Children.Add(draftPolyline);
        }

        foreach (var marker in draftMarkers)
        {
            var index = _draftPointMarkers.IndexOf(marker);
            if (index < 0 || index >= _draftPoints.Count)
                continue;

            var diameter = DraftPointDiameter();
            marker.Width = diameter;
            marker.Height = diameter;
            marker.StrokeThickness = 1.5 / SafeViewScale();
            Canvas.SetLeft(marker, _draftPoints[index].X - diameter / 2);
            Canvas.SetTop(marker, _draftPoints[index].Y - diameter / 2);
            DrawingCanvas.Children.Add(marker);
        }

        if (draftGuide is not null)
        {
            draftGuide.StrokeThickness = DraftStrokeThickness();
            DrawingCanvas.Children.Add(draftGuide);
        }
    }

    private void DrawArrowOnCanvas(ArrowItem arrow)
    {
        if (arrow.Points.Count < 2)
            return;

        var brush = BrushFromHex(arrow.ColorHex);
        var headGeometry = CalculateArrowHeadGeometry(arrow);
        var shaftPoints = arrow.Points.Select(p => new WpfPoint(p.X, p.Y)).ToList();
        if (headGeometry.IsValid)
            shaftPoints[^1] = headGeometry.ShaftEnd;

        var polyline = new Polyline
        {
            Points = new PointCollection(shaftPoints),
            Stroke = brush,
            StrokeThickness = arrow.Thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Flat,
            Cursor = WpfCursors.Hand,
            Tag = arrow.Id
        };
        polyline.MouseLeftButtonDown += ArrowShape_MouseLeftButtonDown;
        DrawingCanvas.Children.Add(polyline);

        if (!headGeometry.IsValid)
            return;

        var head = new Polygon
        {
            Fill = brush,
            Points = new PointCollection
            {
                headGeometry.Tip,
                headGeometry.BaseLeft,
                headGeometry.BaseRight
            },
            Cursor = WpfCursors.Hand,
            Tag = arrow.Id
        };
        head.MouseLeftButtonDown += ArrowShape_MouseLeftButtonDown;
        DrawingCanvas.Children.Add(head);
    }

    private static ArrowHeadGeometry CalculateArrowHeadGeometry(ArrowItem arrow)
    {
        if (arrow.Points.Count < 2)
            return default;

        var end = arrow.Points[^1];
        var prev = arrow.Points[^2];
        var dx = end.X - prev.X;
        var dy = end.Y - prev.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001)
            return default;

        var ux = dx / length;
        var uy = dy / length;
        var desiredSize = Math.Max(12, arrow.Thickness * 3.4);
        var size = Math.Min(desiredSize, length * 0.8);
        var width = size * 0.78;
        var baseX = end.X - ux * size;
        var baseY = end.Y - uy * size;
        var px = -uy;
        var py = ux;

        return new ArrowHeadGeometry(
            new WpfPoint(end.X, end.Y),
            new WpfPoint(baseX + px * width / 2, baseY + py * width / 2),
            new WpfPoint(baseX - px * width / 2, baseY - py * width / 2),
            new WpfPoint(baseX, baseY),
            true);
    }

}
