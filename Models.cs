using System.Text.Json.Serialization;

namespace ArrowOverlayTool;

public sealed class ArrowProject
{
    public int Version { get; set; } = 1;
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
    public string? BackgroundFileName { get; set; }
    public string? BackgroundBase64 { get; set; }
    public List<ArrowItem> Arrows { get; set; } = [];
}

public sealed class ArrowItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "화살표";
    public string ColorHex { get; set; } = "#FF0000";
    public double Thickness { get; set; } = 6;
    public List<PointData> Points { get; set; } = [];

    [JsonIgnore]
    public string DisplayText => $"{Name}  {ColorHex}  {Thickness:0.#}px";
}

public sealed class PointData
{
    public double X { get; set; }
    public double Y { get; set; }
}
