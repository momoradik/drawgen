using System.Numerics;
using SkiaSharp;

namespace HybridSlicer.Infrastructure.Resin;

/// <summary>
/// Drain hole management for hollowed resin parts.
/// Drain holes allow uncured resin to escape from hollow interiors.
///
/// Benefits:
/// - Prevents trapped resin (cracking, leaking, bad curing)
/// - Improves wash/post-cure (fluid and UV reach interior)
/// - Reduces suction forces in bottom-up printing
/// - Reduces part weight
///
/// Implementation: Subtracts circular holes from each affected layer during rasterization.
/// </summary>
public static class DrainHoleEngine
{
    public sealed record DrainHole
    {
        public required float X { get; init; }       // position on model surface (mm)
        public required float Y { get; init; }
        public required float Z { get; init; }       // Z position on surface
        public required float DiameterMm { get; init; }
        public required float DepthMm { get; init; } // how deep into the model
        public required float NormalX { get; init; }  // direction into the model
        public required float NormalY { get; init; }
        public required float NormalZ { get; init; }
    }

    /// <summary>
    /// Subtract drain holes from a layer image.
    /// For each hole, if this layer's Z is within the hole's Z range,
    /// draw a black circle at the hole position.
    /// </summary>
    public static void SubtractDrainHoles(
        LayerRasterizer.RenderContext ctx,
        IReadOnlyList<DrainHole> holes,
        float z)
    {
        if (holes.Count == 0) return;

        using var erasePaint = new SKPaint
        {
            Color = SKColors.Black, Style = SKPaintStyle.Fill,
            IsAntialias = ctx.Paint.IsAntialias, BlendMode = SKBlendMode.Src,
        };

        foreach (var hole in holes)
        {
            // Simple vertical hole model: hole extends from Z to Z-DepthMm
            float holeTop = hole.Z;
            float holeBottom = hole.Z - hole.DepthMm;
            if (z < holeBottom || z > holeTop) continue;

            float radius = hole.DiameterMm / 2;
            float px = hole.X * ctx.ScaleX;
            float py = hole.Y * ctx.ScaleY;
            if (ctx.MirrorX) px = ctx.ResX - px;
            if (ctx.MirrorY) py = ctx.ResY - py;
            float pr = radius * Math.Min(ctx.ScaleX, ctx.ScaleY);

            ctx.Canvas.DrawCircle(px, py, pr, erasePaint);
        }
    }

    /// <summary>
    /// Auto-suggest drain hole positions for a hollowed model.
    /// Places holes at the lowest points of the model to maximize drainage.
    /// </summary>
    public static List<DrainHole> SuggestDrainHoles(
        StlMesh mesh, double holeDiameterMm = 2.5, double holeDepthMm = 5.0, int maxHoles = 3)
    {
        var holes = new List<DrainHole>();

        // Find lowest Z faces and place holes there
        var candidates = new List<(float x, float y, float z, float nx, float ny, float nz)>();
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var v0 = mesh.Vertices[t * 3];
            var v1 = mesh.Vertices[t * 3 + 1];
            var v2 = mesh.Vertices[t * 3 + 2];
            var center = (v0 + v1 + v2) / 3f;
            var normal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
            if (float.IsNaN(normal.X)) continue;

            // Bottom-facing faces near the bottom of the model
            if (normal.Z < -0.3f && center.Z < mesh.Min.Z + (mesh.Max.Z - mesh.Min.Z) * 0.3f)
                candidates.Add((center.X, center.Y, center.Z, normal.X, normal.Y, normal.Z));
        }

        // Sort by Z (lowest first) and pick spaced candidates
        candidates.Sort((a, b) => a.z.CompareTo(b.z));
        float spacing = (mesh.Max.X - mesh.Min.X) * 0.3f;

        foreach (var c in candidates)
        {
            if (holes.Count >= maxHoles) break;
            bool tooClose = holes.Any(h =>
                Vector2.Distance(new Vector2(h.X, h.Y), new Vector2(c.x, c.y)) < spacing);
            if (tooClose) continue;

            holes.Add(new DrainHole
            {
                X = c.x, Y = c.y, Z = c.z,
                DiameterMm = (float)holeDiameterMm, DepthMm = (float)holeDepthMm,
                NormalX = c.nx, NormalY = c.ny, NormalZ = c.nz,
            });
        }

        return holes;
    }
}
