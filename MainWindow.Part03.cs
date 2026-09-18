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
    private void DrawPointHandles(ArrowItem arrow)
    {
        var diameter = 14.0 / SafeViewScale();
        var strokeThickness = 1.8 / SafeViewScale();

        for (var i = 0; i < arrow.Points.Count; i++)
        {
            var point = arrow.Points[i];
            var isSelected = _selectedPointArrowId == arrow.Id && _selectedPointIndex == i;
            var handle = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = isSelected ? BrushFromHex("#FFFF8C00") : BrushFromHex("#FFFFD400"),
                Stroke = WpfBrushes.Black,
                StrokeThickness = strokeThickness,
                Cursor = WpfCursors.SizeAll,
                Tag = new PointHandleTag { ArrowId = arrow.Id, PointIndex = i }
            };
            handle.MouseLeftButtonDown += PointHandle_MouseLeftButtonDown;
            Canvas.SetLeft(handle, point.X - diameter / 2);
            Canvas.SetTop(handle, point.Y - diameter / 2);
            System.Windows.Controls.Panel.SetZIndex(handle, 1000);
            DrawingCanvas.Children.Add(handle);
        }
    }

    private void PointHandle_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (_isDrawing || sender is not FrameworkElement element || element.Tag is not PointHandleTag tag)
            return;

        var arrow = _arrows.FirstOrDefault(a => a.Id == tag.ArrowId);
        if (arrow is null || tag.PointIndex < 0 || tag.PointIndex >= arrow.Points.Count)
            return;

        SelectOnlyArrow(arrow);
        SetSelectedPoint(arrow.Id, tag.PointIndex);
        _dragPointArrow = arrow;
        _dragPointIndex = tag.PointIndex;
        _isDraggingPoint = true;
        DrawingCanvas.CaptureMouse();
        DrawingCanvas.Focus();
        SetStatus($"{arrow.Name}: 점 {tag.PointIndex + 1} 선택. 드래그 또는 방향키로 이동하세요.");
        e.Handled = true;
    }

    private void EndPointDrag()
    {
        if (_isDraggingPoint)
            DrawingCanvas.ReleaseMouseCapture();

        _isDraggingPoint = false;
        _dragPointArrow = null;
        _dragPointIndex = -1;
    }

    private void SetSelectedPoint(Guid arrowId, int pointIndex)
    {
        _selectedPointArrowId = arrowId;
        _selectedPointIndex = pointIndex;
        RedrawAllArrows();
    }

    private void ClearSelectedPoint()
    {
        _selectedPointArrowId = null;
        _selectedPointIndex = -1;
    }

    private void ArrowShape_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (_isDrawing || sender is not FrameworkElement element || element.Tag is not Guid id)
            return;

        var arrow = _arrows.FirstOrDefault(a => a.Id == id);
        if (arrow is not null)
        {
            ClearSelectedPoint();
            SelectOnlyArrow(arrow);
            DrawingCanvas.Focus();
        }
        e.Handled = true;
    }

    private void ArrowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        EndPointDrag();

        var selected = GetSelectedArrows();
        if (selected.Count != 1 || _selectedPointArrowId != selected[0].Id || _selectedPointIndex < 0 || _selectedPointIndex >= selected[0].Points.Count)
            ClearSelectedPoint();

        UpdateEditorFromSelection();
        RedrawAllArrows();
    }

    private List<ArrowItem> GetSelectedArrows()
    {
        return ArrowList.SelectedItems.Cast<ArrowItem>().ToList();
    }

    private void SelectOnlyArrow(ArrowItem arrow)
    {
        ArrowList.SelectedItems.Clear();
        ArrowList.SelectedItem = arrow;
        ArrowList.ScrollIntoView(arrow);
    }

    private void UpdateEditorFromSelection()
    {
        var selected = GetSelectedArrows();
        _selectedArrow = selected.Count == 1 ? selected[0] : null;
        _updatingEditor = true;

        if (selected.Count == 0)
        {
            NameTextBox.Text = string.Empty;
            ColorHexTextBox.Text = string.Empty;
            ThicknessTextBox.Text = string.Empty;
            ColorPreview.Background = WpfBrushes.Transparent;
            SetEditorEnabled(false);
        }
        else if (selected.Count == 1)
        {
            var arrow = selected[0];
            SetEditorEnabled(true);
            NameTextBox.IsEnabled = true;
            NameTextBox.Text = arrow.Name;
            ColorHexTextBox.Text = ColorForEditor(arrow.ColorHex);
            ThicknessSlider.Value = arrow.Thickness;
            ThicknessTextBox.Text = arrow.Thickness.ToString("0.#");
            ColorPreview.Background = BrushFromHex(arrow.ColorHex);
        }
        else
        {
            SetEditorEnabled(true);
            NameTextBox.IsEnabled = false;
            NameTextBox.Text = $"{selected.Count}개 선택";

            var firstColor = selected[0].ColorHex;
            var sameColor = selected.All(a => string.Equals(a.ColorHex, firstColor, StringComparison.OrdinalIgnoreCase));
            ColorHexTextBox.Text = sameColor ? ColorForEditor(firstColor) : string.Empty;
            ColorPreview.Background = sameColor ? BrushFromHex(firstColor) : WpfBrushes.Transparent;

            var firstThickness = selected[0].Thickness;
            var sameThickness = selected.All(a => Math.Abs(a.Thickness - firstThickness) < 0.0001);
            ThicknessSlider.Value = firstThickness;
            if (sameThickness)
            {
                ThicknessSlider.Value = firstThickness;
                ThicknessTextBox.Text = firstThickness.ToString("0.#");
            }
            else
            {
                ThicknessTextBox.Text = string.Empty;
            }
        }

        DeleteArrowButton.IsEnabled = selected.Count > 0;
        _updatingEditor = false;
    }

    private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingEditor)
            return;

        var selected = GetSelectedArrows();
        if (selected.Count != 1)
            return;

        selected[0].Name = NameTextBox.Text;
        ArrowList.Items.Refresh();
    }

    private void ColorHexTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingEditor)
            return;

        var selected = GetSelectedArrows();
        if (selected.Count == 0)
            return;

        var normalized = NormalizeColor(ColorHexTextBox.Text);
        if (normalized is null)
            return;

        foreach (var arrow in selected)
            arrow.ColorHex = normalized;

        ColorPreview.Background = BrushFromHex(normalized);
        ArrowList.Items.Refresh();
        RedrawAllArrows();
        RememberLastCreatedArrowStyle(selected);
    }

    private void ColorPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string hex })
            return;

        ColorHexTextBox.Text = hex;
    }

    private void ChooseColorButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedArrows();
        if (selected.Count == 0)
            return;

        var current = (WpfColor)WpfColorConverter.ConvertFromString(selected[0].ColorHex);
        using var dialog = new WinForms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B)
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
            return;

        var color = dialog.Color;
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        foreach (var arrow in selected)
            arrow.ColorHex = hex;

        _updatingEditor = true;
        ColorHexTextBox.Text = hex;
        ColorPreview.Background = BrushFromHex(hex);
        _updatingEditor = false;
        ArrowList.Items.Refresh();
        RedrawAllArrows();
        RememberLastCreatedArrowStyle(selected);
    }

}
