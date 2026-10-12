using System.Text.Json.Serialization;

namespace Firefly.UI.Services;

public sealed class SectorLayoutFile
{
    [JsonPropertyName("meta")]
    public SectorLayoutMeta? Meta { get; set; }

    [JsonPropertyName("sectors")]
    public Dictionary<string, SectorGeometryDto>? Sectors { get; set; }
}

public sealed class SectorLayoutMeta
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("sectorCount")]
    public int SectorCount { get; set; }
}

public sealed class SectorGeometryDto
{
    [JsonPropertyName("faceId")]
    public string? FaceId { get; set; }

    [JsonPropertyName("centroid")]
    public double[]? Centroid { get; set; }

    [JsonPropertyName("points")]
    public List<double[]>? Points { get; set; }
}

/// <summary>Runtime geometry for one sector polygon.</summary>
public sealed class SectorGeometry
{
    public string SectorId { get; init; } = "";
    public string FaceId { get; init; } = "";
    public double CentroidX { get; init; }
    public double CentroidY { get; init; }
    public string PointsAttribute { get; init; } = "";
}

public sealed class SectorLayout
{
    public const double BoardWidth = 5008;
    public const double BoardHeight = 2008;
    public const double ImageOffsetX = 4;
    public const double ImageOffsetY = 4;
    public const double ImageWidth = 5000;
    public const double ImageHeight = 2000;

    public IReadOnlyDictionary<string, SectorGeometry> ById { get; init; }
        = new Dictionary<string, SectorGeometry>();

    public IReadOnlyList<SectorGeometry> All { get; init; } = Array.Empty<SectorGeometry>();
}
