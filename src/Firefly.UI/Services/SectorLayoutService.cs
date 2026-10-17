using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Firefly.UI.Services;

public sealed class SectorLayoutService
{
    private readonly RepoPaths _paths;
    private readonly Lazy<SectorLayout> _layout;

    public SectorLayoutService(RepoPaths paths)
    {
        _paths = paths;
        _layout = new Lazy<SectorLayout>(Load);
    }

    public SectorLayout Layout => _layout.Value;

    private SectorLayout Load()
    {
        if (!File.Exists(_paths.SectorLayoutPath))
            throw new FileNotFoundException("SectorLayout.json not found.", _paths.SectorLayoutPath);

        var json = File.ReadAllText(_paths.SectorLayoutPath);
        var file = JsonSerializer.Deserialize<SectorLayoutFile>(json)
            ?? throw new InvalidOperationException("Failed to parse SectorLayout.json.");

        var byId = new Dictionary<string, SectorGeometry>(StringComparer.OrdinalIgnoreCase);
        var list = new List<SectorGeometry>();
        if (file.Sectors != null)
        {
            foreach (var (id, dto) in file.Sectors)
            {
                if (dto.Points == null || dto.Points.Count < 3)
                    continue;
                var cx = dto.Centroid is { Length: >= 2 } ? dto.Centroid[0] : Average(dto.Points, 0);
                var cy = dto.Centroid is { Length: >= 2 } ? dto.Centroid[1] : Average(dto.Points, 1);
                var geo = new SectorGeometry
                {
                    SectorId = id,
                    FaceId = dto.FaceId ?? "",
                    CentroidX = cx,
                    CentroidY = cy,
                    PointsAttribute = FormatPoints(dto.Points)
                };
                byId[id] = geo;
                list.Add(geo);
            }
        }

        return new SectorLayout
        {
            ById = byId,
            All = list
        };
    }

    private static double Average(List<double[]> points, int axis)
    {
        double sum = 0;
        foreach (var p in points)
            sum += p[axis];
        return sum / points.Count;
    }

    private static string FormatPoints(List<double[]> points)
    {
        var sb = new StringBuilder(points.Count * 16);
        var inv = CultureInfo.InvariantCulture;
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(points[i][0].ToString("0.###", inv));
            sb.Append(',');
            sb.Append(points[i][1].ToString("0.###", inv));
        }
        return sb.ToString();
    }
}
