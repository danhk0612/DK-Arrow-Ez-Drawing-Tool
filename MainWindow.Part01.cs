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

    private readonly ObservableCollection<ArrowItem> _arrows = [];

    private readonly List<WpfPoint> _draftPoints = [];

    private readonly List<Ellipse> _draftPointMarkers = [];

    private readonly MatrixTransform _viewTransform = new();

    private Polyline? _draftPolyline;

    private Line? _draftGuide;

    private BitmapSource? _backgroundBitmap;

    private byte[]? _backgroundBytes;

    private string? _backgroundFileName;

    private ArrowItem? _selectedArrow;

    private AppSettings _settings = new();
    private Guid? _lastCreatedArrowId;
    private bool _updatingSettingsUi;
    private readonly string _settingsPath = IoPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DK Arrow Ez Drawing Tool",
        "settings.json");

    private bool _isDrawing;

    private bool _updatingEditor;

    private int _arrowCounter = 1;

    private double _viewScale = 1.0;

    private double _fitScale = 1.0;

    private double _panX;

    private double _panY;

    private bool _viewModified;

    private bool _isPanning;

    private WpfPoint _panStart;

    private double _panStartX;

    private double _panStartY;

    private ArrowItem? _dragPointArrow;

    private int _dragPointIndex = -1;

    private bool _isDraggingPoint;

    private Guid? _selectedPointArrowId;

    private int _selectedPointIndex = -1;

    private sealed class PointHandleTag
    {
        public required Guid ArrowId { get; init; }
        public required int PointIndex { get; init; }
    }

    private readonly record struct ArrowHeadGeometry(
        WpfPoint Tip,
        WpfPoint BaseLeft,
        WpfPoint BaseRight,
        WpfPoint ShaftEnd,
        bool IsValid);

    public MainWindow()
    {
        InitializeComponent();
        ArrowList.ItemsSource = _arrows;
        CanvasHost.RenderTransform = _viewTransform;
        LoadAppSettings();
        _updatingSettingsUi = true;
        ExportSuffixTextBox.Text = _settings.ExportSuffix;
        _updatingSettingsUi = false;
        SetEditorEnabled(false);
        SetStatus("배경 이미지를 열어 시작하세요.");
    }

    private void OpenBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "배경 이미지 열기",
            Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif|모든 파일|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        OpenBackgroundFile(dialog.FileName);
    }

    private void Window_PreviewDragOver(object sender, WpfDragEventArgs e)
    {
        if (TryGetDroppedImagePath(e.Data, out _))
            e.Effects = System.Windows.DragDropEffects.Copy;
        else
            e.Effects = System.Windows.DragDropEffects.None;

        e.Handled = true;
    }

    private void Window_PreviewDrop(object sender, WpfDragEventArgs e)
    {
        if (!TryGetDroppedImagePath(e.Data, out var path) || path is null)
            return;

        OpenBackgroundFile(path);
        e.Handled = true;
    }

    private static bool TryGetDroppedImagePath(System.Windows.IDataObject data, out string? path)
    {
        path = null;
        if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            return false;

        if (data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files)
            return false;

        path = files.FirstOrDefault(IsSupportedImageFile);
        return path is not null;
    }

    private static bool IsSupportedImageFile(string path)
    {
        var extension = IoPath.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private void OpenBackgroundFile(string path)
    {
        LoadBackground(File.ReadAllBytes(path), IoPath.GetFileName(path));
        _arrows.Clear();
        _selectedArrow = null;
        _arrowCounter = 1;
        ArrowList.Items.Refresh();
        ClearDraft();
        EndPointDrag();
        _isDrawing = false;
        DrawingCanvas.Cursor = WpfCursors.Arrow;
        RedrawAllArrows();
        UpdateEditorFromSelection();
        SetStatus($"배경: {_backgroundFileName}  ({_backgroundBitmap!.PixelWidth}×{_backgroundBitmap.PixelHeight})");
    }

    private void NewArrowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backgroundBitmap is null)
        {
            SetStatus("먼저 배경 이미지를 여세요.");
            return;
        }

        EndPointDrag();
        ClearDraft();
        _isDrawing = true;
        ClearSelectedPoint();
        DrawingCanvas.Cursor = WpfCursors.Cross;
        DrawingCanvas.Focus();
        RedrawAllArrows();
        SetStatus("화살표 작성 중: 왼쪽 클릭=점 추가, 오른쪽 클릭/Enter=완료, Esc=취소");
    }

    private void DrawingCanvas_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isDrawing)
            return;

        var point = ClampToCanvas(e.GetPosition(DrawingCanvas));
        if (_draftPoints.Count > 0 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            point = SnapOrthogonal(_draftPoints[^1], point);

        _draftPoints.Add(point);
        RedrawDraft();
        DrawingCanvas.Focus();
        e.Handled = true;
    }

    private void DrawingCanvas_MouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isDraggingPoint)
            return;

        EndPointDrag();
        e.Handled = true;
    }

    private void DrawingCanvas_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_isDraggingPoint && _dragPointArrow is not null && _dragPointIndex >= 0 && _dragPointIndex < _dragPointArrow.Points.Count)
        {
            var point = ClampToCanvas(e.GetPosition(DrawingCanvas));
            _dragPointArrow.Points[_dragPointIndex].X = point.X;
            _dragPointArrow.Points[_dragPointIndex].Y = point.Y;
            RedrawAllArrows();
            SetStatus($"{_dragPointArrow.Name}: 점 {_dragPointIndex + 1} 위치 수정 중");
            e.Handled = true;
            return;
        }

        if (!_isDrawing || _draftPoints.Count == 0)
            return;

        var cursor = ClampToCanvas(e.GetPosition(DrawingCanvas));
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            cursor = SnapOrthogonal(_draftPoints[^1], cursor);

        if (_draftGuide is null)
        {
            _draftGuide = new Line
            {
                Stroke = BrushFromHex("#FFFF00CC"),
                StrokeThickness = DraftStrokeThickness(),
                StrokeDashArray = new DoubleCollection { 5, 3 },
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
            DrawingCanvas.Children.Add(_draftGuide);
        }

        var last = _draftPoints[^1];
        _draftGuide.X1 = last.X;
        _draftGuide.Y1 = last.Y;
        _draftGuide.X2 = cursor.X;
        _draftGuide.Y2 = cursor.Y;
    }

    private void DrawingCanvas_MouseRightButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isDrawing)
            return;

        FinishDraft();
        e.Handled = true;
    }

    private void DrawingCanvas_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (!_isDrawing)
            return;

        if (e.Key == Key.Enter)
        {
            FinishDraft();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClearDraft();
            _isDrawing = false;
            DrawingCanvas.Cursor = WpfCursors.Arrow;
            RedrawAllArrows();
            SetStatus("화살표 작성을 취소했습니다.");
            e.Handled = true;
        }
    }

}
