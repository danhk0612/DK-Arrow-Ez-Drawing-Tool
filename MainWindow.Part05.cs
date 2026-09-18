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
    private void LoadBackground(byte[] bytes, string fileName)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        _backgroundBitmap = bitmap;
        _backgroundBytes = bytes;
        _backgroundFileName = fileName;
        BackgroundImage.Source = bitmap;
        CanvasHost.Width = bitmap.PixelWidth;
        CanvasHost.Height = bitmap.PixelHeight;
        BackgroundImage.Width = bitmap.PixelWidth;
        BackgroundImage.Height = bitmap.PixelHeight;
        DrawingCanvas.Width = bitmap.PixelWidth;
        DrawingCanvas.Height = bitmap.PixelHeight;

        _viewModified = false;
        ScheduleFitViewToViewport();
    }

    private void ScheduleFitViewToViewport()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (_backgroundBitmap is null || _viewModified)
                return;

            ViewportBorder.UpdateLayout();
            FitViewToViewport();
        }));
    }

    private void ViewportBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_backgroundBitmap is not null && !_viewModified)
            FitViewToViewport();
    }

    private void FitViewToViewport()
    {
        if (_backgroundBitmap is null || ViewportBorder.ActualWidth <= 0 || ViewportBorder.ActualHeight <= 0)
            return;

        var scaleX = ViewportBorder.ActualWidth / _backgroundBitmap.PixelWidth;
        var scaleY = ViewportBorder.ActualHeight / _backgroundBitmap.PixelHeight;
        _fitScale = Math.Max(0.0001, Math.Min(scaleX, scaleY));
        _viewScale = _fitScale;
        _panX = (ViewportBorder.ActualWidth - _backgroundBitmap.PixelWidth * _viewScale) / 2.0;
        _panY = (ViewportBorder.ActualHeight - _backgroundBitmap.PixelHeight * _viewScale) / 2.0;
        ApplyViewTransform();
        RedrawAllArrows();
    }

    private void ViewportBorder_PreviewMouseWheel(object sender, WpfMouseWheelEventArgs e)
    {
        if (_backgroundBitmap is null)
            return;

        var mouse = e.GetPosition(ViewportBorder);
        var oldScale = SafeViewScale();
        var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        var minScale = Math.Max(0.0001, _fitScale * 0.1);
        var maxScale = Math.Max(minScale, _fitScale * 20.0);
        var newScale = Math.Clamp(oldScale * factor, minScale, maxScale);

        if (Math.Abs(newScale - oldScale) < 0.0000001)
            return;

        var imageX = (mouse.X - _panX) / oldScale;
        var imageY = (mouse.Y - _panY) / oldScale;
        _viewScale = newScale;
        _panX = mouse.X - imageX * newScale;
        _panY = mouse.Y - imageY * newScale;
        _viewModified = true;
        ApplyViewTransform();
        RedrawAllArrows();
        e.Handled = true;
    }

    private void ViewportBorder_PreviewMouseDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (_backgroundBitmap is null)
            return;

        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && !_isDrawing && !_isDraggingPoint)
        {
            _viewModified = false;
            FitViewToViewport();
            SetStatus("화면 맞춤으로 초기화했습니다.");
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Middle)
            return;

        EndPointDrag();
        _isPanning = true;
        _panStart = e.GetPosition(ViewportBorder);
        _panStartX = _panX;
        _panStartY = _panY;
        ViewportBorder.CaptureMouse();
        ViewportBorder.Cursor = WpfCursors.SizeAll;
        e.Handled = true;
    }

    private void ViewportBorder_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isPanning)
            return;

        var current = e.GetPosition(ViewportBorder);
        _panX = _panStartX + current.X - _panStart.X;
        _panY = _panStartY + current.Y - _panStart.Y;
        _viewModified = true;
        ApplyViewTransform();
        e.Handled = true;
    }

    private void ViewportBorder_PreviewMouseUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isPanning || e.ChangedButton != MouseButton.Middle)
            return;

        _isPanning = false;
        ViewportBorder.ReleaseMouseCapture();
        ViewportBorder.Cursor = WpfCursors.Arrow;
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (HelpOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                HelpOverlay.Visibility = Visibility.Collapsed;
                DrawingCanvas.Focus();
                e.Handled = true;
            }
            return;
        }

        if (_isDrawing)
            return;

        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            return;

        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox or Slider)
            return;

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10.0 : 1.0;
        var delta = e.Key switch
        {
            Key.Left => new WpfPoint(-step, 0),
            Key.Right => new WpfPoint(step, 0),
            Key.Up => new WpfPoint(0, -step),
            Key.Down => new WpfPoint(0, step),
            _ => default
        };

        if (TryMoveSelectedPoint(delta))
        {
            e.Handled = true;
            return;
        }

        if (TryMoveSelectedArrows(delta))
            e.Handled = true;
    }

    private bool TryMoveSelectedPoint(WpfPoint delta)
    {
        if (_selectedPointArrowId is null || _selectedPointIndex < 0)
            return false;

        var arrow = _arrows.FirstOrDefault(a => a.Id == _selectedPointArrowId.Value);
        if (arrow is null || _selectedPointIndex >= arrow.Points.Count)
            return false;

        var point = arrow.Points[_selectedPointIndex];
        point.X = Math.Clamp(point.X + delta.X, 0, DrawingCanvas.Width);
        point.Y = Math.Clamp(point.Y + delta.Y, 0, DrawingCanvas.Height);
        RedrawAllArrows();
        SetStatus($"{arrow.Name}: 점 {_selectedPointIndex + 1}을(를) {(int)delta.X},{(int)delta.Y} 만큼 이동했습니다.");
        return true;
    }

}
