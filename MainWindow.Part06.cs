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
    private bool TryMoveSelectedArrows(WpfPoint delta)
    {
        var selected = GetSelectedArrows();
        if (selected.Count == 0)
            return false;

        var allowedDx = delta.X;
        var allowedDy = delta.Y;

        foreach (var arrow in selected)
        {
            foreach (var point in arrow.Points)
            {
                if (allowedDx < 0)
                    allowedDx = Math.Max(allowedDx, -point.X);
                else if (allowedDx > 0)
                    allowedDx = Math.Min(allowedDx, DrawingCanvas.Width - point.X);

                if (allowedDy < 0)
                    allowedDy = Math.Max(allowedDy, -point.Y);
                else if (allowedDy > 0)
                    allowedDy = Math.Min(allowedDy, DrawingCanvas.Height - point.Y);
            }
        }

        if (Math.Abs(allowedDx) < 0.000001 && Math.Abs(allowedDy) < 0.000001)
            return true;

        foreach (var arrow in selected)
        {
            foreach (var point in arrow.Points)
            {
                point.X += allowedDx;
                point.Y += allowedDy;
            }
        }

        RedrawAllArrows();
        var amountText = $"{(int)allowedDx},{(int)allowedDy}";
        SetStatus(selected.Count == 1
            ? $"{selected[0].Name}을(를) {amountText} 만큼 이동했습니다."
            : $"화살표 {selected.Count}개를 {amountText} 만큼 이동했습니다.");
        return true;
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        HelpOverlay.Visibility = Visibility.Visible;
        HelpCloseButton.Focus();
    }

    private void HelpCloseButton_Click(object sender, RoutedEventArgs e)
    {
        HelpOverlay.Visibility = Visibility.Collapsed;
        DrawingCanvas.Focus();
    }

    private void HelpOverlay_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, HelpOverlay))
        {
            HelpOverlay.Visibility = Visibility.Collapsed;
            DrawingCanvas.Focus();
            e.Handled = true;
        }
    }

    private void ApplyViewTransform()
    {
        _viewTransform.Matrix = new Matrix(_viewScale, 0, 0, _viewScale, _panX, _panY);
    }

    private static WpfPoint SnapOrthogonal(WpfPoint origin, WpfPoint target)
    {
        var dx = target.X - origin.X;
        var dy = target.Y - origin.Y;

        return Math.Abs(dx) >= Math.Abs(dy)
            ? new WpfPoint(target.X, origin.Y)
            : new WpfPoint(origin.X, target.Y);
    }

    private WpfPoint ClampToCanvas(WpfPoint point)
    {
        return new WpfPoint(
            Math.Clamp(point.X, 0, DrawingCanvas.Width),
            Math.Clamp(point.Y, 0, DrawingCanvas.Height));
    }

    private double SafeViewScale() => Math.Max(_viewScale, 0.0001);

    private double DraftStrokeThickness() => 3.0 / SafeViewScale();

    private double DraftPointDiameter() => 12.0 / SafeViewScale();

    private void UpdateColorPreview()
    {
        var selected = GetSelectedArrows();
        if (selected.Count == 0)
        {
            ColorPreview.Background = WpfBrushes.Transparent;
            return;
        }

        var firstColor = selected[0].ColorHex;
        ColorPreview.Background = selected.All(a => string.Equals(a.ColorHex, firstColor, StringComparison.OrdinalIgnoreCase))
            ? BrushFromHex(firstColor)
            : WpfBrushes.Transparent;
    }

    private static SolidColorBrush BrushFromHex(string value)
    {
        var color = (WpfColor)WpfColorConverter.ConvertFromString(value);
        return new SolidColorBrush(color);
    }

    private static string? NormalizeColor(string value)
    {
        value = value.Trim();
        if (!value.StartsWith('#'))
            value = "#" + value;

        if (value.Length is not (7 or 9))
            return null;

        try
        {
            _ = (WpfColor)WpfColorConverter.ConvertFromString(value);
            return value.ToUpperInvariant();
        }
        catch
        {
            return null;
        }
    }

    private static string ColorForEditor(string value)
    {
        var normalized = NormalizeColor(value);
        if (normalized is null)
            return value;

        return normalized.Length == 9 && normalized.StartsWith("#FF", StringComparison.OrdinalIgnoreCase)
            ? "#" + normalized[3..]
            : normalized;
    }

    private void SetEditorEnabled(bool enabled)
    {
        NameTextBox.IsEnabled = enabled;
        ColorHexTextBox.IsEnabled = enabled;
        ChooseColorButton.IsEnabled = enabled;
        ColorPresetPanel.IsEnabled = true;
        ThicknessSlider.IsEnabled = enabled;
        ThicknessTextBox.IsEnabled = enabled;
        DeleteArrowButton.IsEnabled = enabled;
    }

    private void ExportSuffixTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSettingsUi)
            return;

        _settings.ExportSuffix = ExportSuffixTextBox.Text;
        SaveAppSettings();
    }

    private void RememberLastCreatedArrowStyle(IReadOnlyList<ArrowItem> selected)
    {
        if (selected.Count != 1 || _lastCreatedArrowId is null || selected[0].Id != _lastCreatedArrowId.Value)
            return;

        _settings.LastArrowColorHex = selected[0].ColorHex;
        _settings.LastArrowThickness = selected[0].Thickness;
        SaveAppSettings();
    }

    private void LoadAppSettings()
    {
        if (!File.Exists(_settingsPath))
            return;

        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath));
            if (loaded is null)
                return;

            var color = NormalizeColor(loaded.LastArrowColorHex);
            loaded.LastArrowColorHex = color ?? "#FF0000";
            loaded.LastArrowThickness = Math.Clamp(loaded.LastArrowThickness, 1, 30);
            loaded.ExportSuffix ??= "_arrows";

            loaded.ColorPresets ??= [];
            var normalizedPresets = new List<string>(8);
            for (var i = 0; i < 8; i++)
            {
                var preset = i < loaded.ColorPresets.Count
                    ? NormalizeColor(loaded.ColorPresets[i])
                    : null;
                normalizedPresets.Add(preset ?? AppSettings.DefaultColorPresets[i]);
            }
            loaded.ColorPresets = normalizedPresets;

            _settings = loaded;
        }
        catch
        {
            _settings = new AppSettings();
        }
    }

    private void RefreshColorPresetButtons()
    {
        var buttons = ColorPresetPanel.Children.OfType<System.Windows.Controls.Button>().ToList();
        for (var i = 0; i < buttons.Count && i < _settings.ColorPresets.Count; i++)
        {
            var hex = _settings.ColorPresets[i];
            if (buttons[i].Content is Border swatch)
                swatch.Background = BrushFromHex(hex);

            buttons[i].ToolTip = $"{hex}\n좌클릭: 적용\n우클릭: 템플릿 색상 수정";
        }
    }

    private void SaveAppSettings()
    {
        var directory = IoPath.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private string GetBackgroundBaseName()
    {
        var baseName = IoPath.GetFileNameWithoutExtension(_backgroundFileName);
        return string.IsNullOrWhiteSpace(baseName) ? "arrow-work" : baseName;
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

}
