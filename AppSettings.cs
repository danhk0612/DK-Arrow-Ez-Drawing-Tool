namespace ArrowOverlayTool;

public sealed class AppSettings
{
    public static readonly string[] DefaultColorPresets =
    [
        "#FF0000",
        "#FF7A00",
        "#FFD400",
        "#00A651",
        "#0078D4",
        "#00B7C3",
        "#8E44AD",
        "#000000"
    ];

    public string LastArrowColorHex { get; set; } = "#FF0000";
    public double LastArrowThickness { get; set; } = 6;
    public string ExportSuffix { get; set; } = "_arrows";
    public List<string> ColorPresets { get; set; } = [.. DefaultColorPresets];
}
