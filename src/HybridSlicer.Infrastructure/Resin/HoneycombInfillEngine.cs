using System.Numerics;
using SkiaSharp;

namespace HybridSlicer.Infrastructure.Resin;

/// <summary>
/// Generates honeycomb and lattice internal structures for resin parts.
/// Applied at rasterization time — fills hollow interiors with structured patterns.
///
/// Types:
/// - Honeycomb: hexagonal grid pattern — best strength-to-weight ratio
/// - Grid: rectangular grid — simple, uniform
/// - Triangular: triangular lattice — good multi-directional strength
/// - Gyroid: continuous curved surface — isotropic strength, smooth transitions
/// </summary>
public static class HoneycombInfillEngine
{
    public sealed record InfillConfig
    {
        public string Pattern { get; init; } = "honeycomb"; // honeycomb | grid | triangular | gyroid
        public double DensityPct { get; init; } = 20;       // 0-100%
        public double WallThicknessMm { get; init; } = 0.4; // wall/strut thickness
        public double CellSizeMm { get; init; } = 3.0;      // cell size for honeycomb
    }

    /// <summary>
    /// Draw infill pattern inside the hollow region of a layer.
    /// Call AFTER RasterizeHollow — this fills the black interior with the pattern.
    /// </summary>
    public static void DrawInfill(
        LayerRasterizer.RenderContext ctx,
        List<List<Vector2>> polygons,
        float wallThicknessMm,
        InfillConfig config,
        float zHeight)
    {
        if (polygons.Count == 0 || config.DensityPct <= 0) return;

        // Get the inset (inner) polygons — same as hollowing uses
        var innerPolygons = new List<List<Vector2>>();
        foreach (var poly in polygons)
        {
            var inset = InsetPolygon(poly, wallThicknessMm);
            if (inset.Count >= 3) innerPolygons.Add(inset);
        }
        if (innerPolygons.Count == 0) return;

        // Compute bounds of inner area
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var poly in innerPolygons)
            foreach (var p in poly)
            {
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
            }

        float cellMm = (float)config.CellSizeMm;
        float strokePx = (float)config.WallThicknessMm * Math.Min(ctx.ScaleX, ctx.ScaleY);

        using var paint = new SKPaint
        {
            Color = SKColors.White, Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, strokePx), IsAntialias = ctx.Paint.IsAntialias,
        };

        // Create clip path from inner polygons (only draw inside the hollow)
        using var clipPath = new SKPath();
        foreach (var poly in innerPolygons)
        {
            bool first = true;
            foreach (var pt in poly)
            {
                float px = pt.X * ctx.ScaleX, py = pt.Y * ctx.ScaleY;
                if (ctx.MirrorX) px = ctx.ResX - px;
                if (ctx.MirrorY) py = ctx.ResY - py;
                if (first) { clipPath.MoveTo(px, py); first = false; }
                else clipPath.LineTo(px, py);
            }
            clipPath.Close();
        }

        ctx.Canvas.Save();
        ctx.Canvas.ClipPath(clipPath);

        switch (config.Pattern)
        {
            case "honeycomb": DrawHoneycomb(ctx, paint, minX, minY, maxX, maxY, cellMm, zHeight); break;
            case "grid": DrawGrid(ctx, paint, minX, minY, maxX, maxY, cellMm); break;
            case "triangular": DrawTriangular(ctx, paint, minX, minY, maxX, maxY, cellMm); break;
            case "gyroid": DrawGyroid(ctx, paint, minX, minY, maxX, maxY, cellMm, zHeight); break;
            default: DrawHoneycomb(ctx, paint, minX, minY, maxX, maxY, cellMm, zHeight); break;
        }

        ctx.Canvas.Restore();
    }

    private static void DrawHoneycomb(LayerRasterizer.RenderContext ctx, SKPaint paint,
        float minX, float minY, float maxX, float maxY, float cellMm, float z)
    {
        float h = cellMm * MathF.Sqrt(3) / 2;
        int row = 0;
        for (float y = minY - cellMm; y <= maxY + cellMm; y += h, row++)
        {
            float xOff = (row % 2 == 0) ? 0 : cellMm * 0.75f;
            for (float x = minX - cellMm + xOff; x <= maxX + cellMm; x += cellMm * 1.5f)
            {
                DrawHexagon(ctx, paint, x, y, cellMm * 0.5f);
            }
        }
    }

    private static void DrawHexagon(LayerRasterizer.RenderContext ctx, SKPaint paint, float cx, float cy, float r)
    {
        using var path = new SKPath();
        for (int i = 0; i < 6; i++)
        {
            float angle = MathF.PI / 3 * i + MathF.PI / 6;
            float px = (cx + r * MathF.Cos(angle)) * ctx.ScaleX;
            float py = (cy + r * MathF.Sin(angle)) * ctx.ScaleY;
            if (ctx.MirrorX) px = ctx.ResX - px;
            if (ctx.MirrorY) py = ctx.ResY - py;
            if (i == 0) path.MoveTo(px, py); else path.LineTo(px, py);
        }
        path.Close();
        ctx.Canvas.DrawPath(path, paint);
    }

    private static void DrawGrid(LayerRasterizer.RenderContext ctx, SKPaint paint,
        float minX, float minY, float maxX, float maxY, float cellMm)
    {
        for (float x = minX; x <= maxX; x += cellMm)
        {
            float px = x * ctx.ScaleX;
            if (ctx.MirrorX) px = ctx.ResX - px;
            float py1 = minY * ctx.ScaleY, py2 = maxY * ctx.ScaleY;
            if (ctx.MirrorY) { py1 = ctx.ResY - py1; py2 = ctx.ResY - py2; }
            ctx.Canvas.DrawLine(px, py1, px, py2, paint);
        }
        for (float y = minY; y <= maxY; y += cellMm)
        {
            float py = y * ctx.ScaleY;
            if (ctx.MirrorY) py = ctx.ResY - py;
            float px1 = minX * ctx.ScaleX, px2 = maxX * ctx.ScaleX;
            if (ctx.MirrorX) { px1 = ctx.ResX - px1; px2 = ctx.ResX - px2; }
            ctx.Canvas.DrawLine(px1, py, px2, py, paint);
        }
    }

    private static void DrawTriangular(LayerRasterizer.RenderContext ctx, SKPaint paint,
        float minX, float minY, float maxX, float maxY, float cellMm)
    {
        // Horizontal + two diagonals
        DrawGrid(ctx, paint, minX, minY, maxX, maxY, cellMm);
        float h = cellMm * MathF.Sqrt(3) / 2;
        for (float y = minY; y <= maxY; y += h)
        {
            float px1 = minX * ctx.ScaleX, px2 = maxX * ctx.ScaleX;
            float py1 = y * ctx.ScaleY, py2 = (y + h) * ctx.ScaleY;
            if (ctx.MirrorX) { px1 = ctx.ResX - px1; px2 = ctx.ResX - px2; }
            if (ctx.MirrorY) { py1 = ctx.ResY - py1; py2 = ctx.ResY - py2; }
            ctx.Canvas.DrawLine(px1, py1, px2, py2, paint);
            ctx.Canvas.DrawLine(px2, py1, px1, py2, paint);
        }
    }

    private static void DrawGyroid(LayerRasterizer.RenderContext ctx, SKPaint paint,
        float minX, float minY, float maxX, float maxY, float cellMm, float z)
    {
        // Gyroid approximation: sinusoidal curves that shift per Z layer
        float period = cellMm * 2;
        float phase = (z / cellMm) * MathF.PI;
        int steps = 100;
        for (float baseY = minY; baseY <= maxY; baseY += cellMm)
        {
            using var path = new SKPath();
            bool first = true;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                float x = minX + t * (maxX - minX);
                float y = baseY + MathF.Sin(x / period * MathF.PI * 2 + phase) * cellMm * 0.4f;
                float px = x * ctx.ScaleX, py = y * ctx.ScaleY;
                if (ctx.MirrorX) px = ctx.ResX - px;
                if (ctx.MirrorY) py = ctx.ResY - py;
                if (first) { path.MoveTo(px, py); first = false; } else path.LineTo(px, py);
            }
            ctx.Canvas.DrawPath(path, paint);
        }
    }

    // Reuse polygon inset from LayerRasterizer
    private static List<Vector2> InsetPolygon(List<Vector2> polygon, float offsetMm)
    {
        int n = polygon.Count;
        if (n < 3) return new List<Vector2>();
        var cx = polygon.Average(p => p.X);
        var cy = polygon.Average(p => p.Y);
        var result = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            var p0 = polygon[i]; var p1 = polygon[(i + 1) % n];
            var dx = p1.X - p0.X; var dy = p1.Y - p0.Y;
            var len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6f) continue;
            var nx = dy / len; var ny = -dx / len;
            var midX = (p0.X + p1.X) / 2; var midY = (p0.Y + p1.Y) / 2;
            if (nx * (cx - midX) + ny * (cy - midY) < 0) { nx = -nx; ny = -ny; }
            result.Add(new Vector2(
                polygon[i].X + nx * offsetMm,
                polygon[i].Y + ny * offsetMm));
        }
        return result;
    }
}
