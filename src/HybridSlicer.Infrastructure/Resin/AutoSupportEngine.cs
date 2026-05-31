using System.Numerics;
using HybridSlicer.Domain.Enums;

namespace HybridSlicer.Infrastructure.Resin;

/// <summary>
/// Auto-support generation for resin printers.
///
/// Top-Down vs Bottom-Up differences:
/// - Bottom-Up: model is inverted, build plate on top. Gravity pulls DOWN.
///   Supports go FROM build plate (top) DOWN to overhanging surfaces.
///   Overhangs face downward (toward FEP). Tips must be fine (visible surface).
///   Suction cups (inverted concave surfaces) are critical concerns.
///
/// - Top-Down: model is upright, build plate on bottom. Gravity pulls DOWN.
///   Supports go FROM build plate (bottom) UP to overhanging surfaces.
///   Similar to FDM supports. Less peel concern. Simpler strategy.
/// </summary>
public static class AutoSupportEngine
{
    public sealed record SupportConfig
    {
        public PrinterOrientation Orientation { get; init; } = PrinterOrientation.BottomUp;
        public double OverhangAngleDeg { get; init; } = 45;
        public double DensityFactor { get; init; } = 0.5; // 0=sparse, 1=dense
        public double TipDiameterMm { get; init; } = 0.4;
        public double ColumnDiameterMm { get; init; } = 0.8;
        public double BaseDiameterMm { get; init; } = 1.5;
        public string SupportType { get; init; } = "normal"; // normal | tree
        public string Placement { get; init; } = "buildplate"; // buildplate | everywhere
        // Raft settings
        public bool RaftEnabled { get; init; }
        public string RaftType { get; init; } = "grid"; // solid | grid | pad
        public double RaftThicknessMm { get; init; } = 1.5;
        public double RaftMarginMm { get; init; } = 3.0;
        // Skirt settings
        public bool SkirtEnabled { get; init; }
        public int SkirtLayers { get; init; } = 3;
        public double SkirtDistanceMm { get; init; } = 2.0;
        public double SkirtWidthMm { get; init; } = 0.5;
    }

    public sealed record GeneratedSupport
    {
        public required float X { get; init; }
        public required float Y { get; init; }
        public required float ContactZ { get; init; }  // Z on model surface
        public required float BaseZ { get; init; }      // Z on build plate (0 for buildplate supports)
        public required float TipDiameter { get; init; }
        public required float ColumnDiameter { get; init; }
        public required float BaseDiameter { get; init; }
        public required float NormalX { get; init; }
        public required float NormalY { get; init; }
        public required float NormalZ { get; init; }
    }

    public sealed record GeneratedRaft
    {
        public required string Type { get; init; }        // solid | grid | pad
        public required float MinX { get; init; }
        public required float MinY { get; init; }
        public required float MaxX { get; init; }
        public required float MaxY { get; init; }
        public required float ThicknessMm { get; init; }
        public required float MarginMm { get; init; }
    }

    public sealed record GeneratedSkirt
    {
        public required float MinX { get; init; }
        public required float MinY { get; init; }
        public required float MaxX { get; init; }
        public required float MaxY { get; init; }
        public required int Layers { get; init; }
        public required float DistanceMm { get; init; }
        public required float WidthMm { get; init; }
    }

    public sealed record AutoSupportResult
    {
        public List<GeneratedSupport> Supports { get; init; } = [];
        public GeneratedRaft? Raft { get; init; }
        public GeneratedSkirt? Skirt { get; init; }
        public int OverhangFaceCount { get; init; }
        public long ElapsedMs { get; init; }
    }

    /// <summary>
    /// Analyze mesh and generate supports, raft, and skirt.
    /// </summary>
    public static AutoSupportResult Generate(StlMesh mesh, SupportConfig config)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Gravity direction depends on printer orientation
        // Bottom-Up: model is inverted, so overhangs face -Z (downward toward FEP)
        // Top-Down: model is upright, overhangs face -Z (downward, unsupported by gravity)
        // In both cases, we detect faces whose normal points "downward" relative to gravity
        var gravityDir = new Vector3(0, 0, -1); // always -Z for both orientations

        float overhangCos = MathF.Cos(MathF.PI / 180f * (float)config.OverhangAngleDeg);

        // Analyze each triangle face for overhangs
        var overhangPoints = new List<(Vector3 center, Vector3 normal)>();
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var v0 = mesh.Vertices[t * 3];
            var v1 = mesh.Vertices[t * 3 + 1];
            var v2 = mesh.Vertices[t * 3 + 2];

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));

            if (float.IsNaN(normal.X)) continue;

            // Face is an overhang if its normal points downward beyond the threshold
            float dotGravity = Vector3.Dot(normal, gravityDir);
            if (dotGravity > overhangCos)
            {
                var center = (v0 + v1 + v2) / 3f;
                // Skip faces that are already on the build plate
                if (center.Z < 0.1f) continue;
                overhangPoints.Add((center, normal));
            }
        }

        // Density-based sampling: place supports at a subset of overhang points
        float spacing = (float)(5.0 / (config.DensityFactor + 0.1)); // mm between supports
        var supports = new List<GeneratedSupport>();
        var placed = new List<Vector3>();

        foreach (var (center, normal) in overhangPoints)
        {
            // Check minimum spacing
            bool tooClose = false;
            foreach (var p in placed)
            {
                if (Vector2.Distance(new Vector2(center.X, center.Y), new Vector2(p.X, p.Y)) < spacing)
                { tooClose = true; break; }
            }
            if (tooClose) continue;

            float baseZ = 0; // supports go to build plate
            if (config.Placement == "everywhere" && center.Z > 2.0f)
            {
                // Could support from intermediate surfaces — for now, all go to bed
                baseZ = 0;
            }

            supports.Add(new GeneratedSupport
            {
                X = center.X,
                Y = center.Y,
                ContactZ = center.Z,
                BaseZ = baseZ,
                TipDiameter = (float)config.TipDiameterMm,
                ColumnDiameter = (float)config.ColumnDiameterMm,
                BaseDiameter = (float)config.BaseDiameterMm,
                NormalX = normal.X,
                NormalY = normal.Y,
                NormalZ = normal.Z,
            });
            placed.Add(center);
        }

        // Bottom-Up specific: make tips thinner (surface quality critical on visible side)
        if (config.Orientation == PrinterOrientation.BottomUp)
        {
            for (int i = 0; i < supports.Count; i++)
            {
                supports[i] = supports[i] with { TipDiameter = supports[i].TipDiameter * 0.8f };
            }
        }

        // Generate raft
        GeneratedRaft? raft = null;
        if (config.RaftEnabled)
        {
            raft = new GeneratedRaft
            {
                Type = config.RaftType,
                MinX = mesh.Min.X - (float)config.RaftMarginMm,
                MinY = mesh.Min.Y - (float)config.RaftMarginMm,
                MaxX = mesh.Max.X + (float)config.RaftMarginMm,
                MaxY = mesh.Max.Y + (float)config.RaftMarginMm,
                ThicknessMm = (float)config.RaftThicknessMm,
                MarginMm = (float)config.RaftMarginMm,
            };
        }

        // Generate skirt
        GeneratedSkirt? skirt = null;
        if (config.SkirtEnabled)
        {
            skirt = new GeneratedSkirt
            {
                MinX = mesh.Min.X - (float)config.SkirtDistanceMm,
                MinY = mesh.Min.Y - (float)config.SkirtDistanceMm,
                MaxX = mesh.Max.X + (float)config.SkirtDistanceMm,
                MaxY = mesh.Max.Y + (float)config.SkirtDistanceMm,
                Layers = config.SkirtLayers,
                DistanceMm = (float)config.SkirtDistanceMm,
                WidthMm = (float)config.SkirtWidthMm,
            };
        }

        sw.Stop();
        return new AutoSupportResult
        {
            Supports = supports,
            Raft = raft,
            Skirt = skirt,
            OverhangFaceCount = overhangPoints.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
        };
    }
}
