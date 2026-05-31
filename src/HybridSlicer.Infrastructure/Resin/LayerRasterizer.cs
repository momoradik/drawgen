using System.Numerics;
using SkiaSharp;

namespace HybridSlicer.Infrastructure.Resin;

/// <summary>
/// Rasterizes polygon contours into layer images at printer-native resolution.
/// Uses SkiaSharp for GPU-quality anti-aliased polygon fill.
/// </summary>
public static class LayerRasterizer
{
    /// <summary>
    /// Render polygon contours to a grayscale PNG at the specified resolution.
    /// </summary>
    /// <param name="polygons">Closed polygon contours from cross-section (in mm coords)</param>
    /// <param name="resX">Image width in pixels</param>
    /// <param name="resY">Image height in pixels</param>
    /// <param name="buildWidthMm">Build plate width in mm</param>
    /// <param name="buildDepthMm">Build plate depth in mm</param>
    /// <param name="antiAlias">Enable anti-aliased edges</param>
    /// <param name="mirrorX">Mirror horizontally</param>
    /// <param name="mirrorY">Mirror vertically</param>
    /// <param name="meshMinX">Mesh bounding box min X (for centering)</param>
    /// <param name="meshMinY">Mesh bounding box min Y (for centering)</param>
    /// <returns>PNG-encoded byte array</returns>
    /// <summary>
    /// Reusable rendering context. Create once, reuse across all layers.
    /// </summary>
    public sealed class RenderContext : IDisposable
    {
        public SKSurface Surface { get; }
        public SKCanvas Canvas { get; }
        public SKPaint Paint { get; }
        public int ResX { get; }
        public int ResY { get; }
        public float ScaleX { get; }
        public float ScaleY { get; }
        public bool MirrorX { get; }
        public bool MirrorY { get; }

        public RenderContext(int resX, int resY, float buildWidthMm, float buildDepthMm,
            bool antiAlias, bool mirrorX, bool mirrorY)
        {
            ResX = resX; ResY = resY;
            ScaleX = resX / buildWidthMm;
            ScaleY = resY / buildDepthMm;
            MirrorX = mirrorX; MirrorY = mirrorY;
            Surface = SKSurface.Create(new SKImageInfo(resX, resY, SKColorType.Gray8));
            Canvas = Surface.Canvas;
            Paint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = antiAlias };
        }

        public void Dispose()
        {
            Paint.Dispose();
            Surface.Dispose();
        }
    }

    /// <summary>
    /// Render polygons to a grayscale PNG using a reusable context.
    /// Build-plate coordinates: (0,0) = build plate origin.
    /// </summary>
    public static byte[] Rasterize(RenderContext ctx, List<List<Vector2>> polygons)
    {
        ctx.Canvas.Clear(SKColors.Black);

        if (polygons.Count == 0)
            return EncodePng(ctx.Surface);

        foreach (var polygon in polygons)
        {
            if (polygon.Count < 3) continue;

            using var path = new SKPath();
            bool first = true;

            foreach (var pt in polygon)
            {
                // Build-plate mm → pixel (origin at 0,0)
                float px = pt.X * ctx.ScaleX;
                float py = pt.Y * ctx.ScaleY;

                if (ctx.MirrorX) px = ctx.ResX - px;
                if (ctx.MirrorY) py = ctx.ResY - py;

                if (first) { path.MoveTo(px, py); first = false; }
                else path.LineTo(px, py);
            }
            path.Close();
            ctx.Canvas.DrawPath(path, ctx.Paint);
        }

        return EncodePng(ctx.Surface);
    }

    /// <summary>
    /// Render hollowed layer: outer fill in white, inner offset fill erased (black).
    /// Result is a ring of width = wallThicknessMm.
    /// </summary>
    public static byte[] RasterizeHollow(RenderContext ctx, List<List<Vector2>> polygons, float wallThicknessMm)
    {
        ctx.Canvas.Clear(SKColors.Black);

        if (polygons.Count == 0)
            return EncodePng(ctx.Surface);

        // Step 1: fill outer contour (white)
        using var outerPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = ctx.Paint.IsAntialias };
        foreach (var polygon in polygons)
        {
            if (polygon.Count < 3) continue;
            using var path = new SKPath();
            bool first = true;
            foreach (var pt in polygon)
            {
                float px = pt.X * ctx.ScaleX;
                float py = pt.Y * ctx.ScaleY;
                if (ctx.MirrorX) px = ctx.ResX - px;
                if (ctx.MirrorY) py = ctx.ResY - py;
                if (first) { path.MoveTo(px, py); first = false; }
                else path.LineTo(px, py);
            }
            path.Close();
            ctx.Canvas.DrawPath(path, outerPaint);
        }

        // Step 2: erase inner (inset) contour — shrink each polygon by wallThickness
        using var innerPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = ctx.Paint.IsAntialias };
        foreach (var polygon in polygons)
        {
            if (polygon.Count < 3) continue;
            var inset = InsetPolygon(polygon, wallThicknessMm);
            if (inset.Count < 3) continue; // polygon too small to hollow

            using var path = new SKPath();
            bool first = true;
            foreach (var pt in inset)
            {
                float px = pt.X * ctx.ScaleX;
                float py = pt.Y * ctx.ScaleY;
                if (ctx.MirrorX) px = ctx.ResX - px;
                if (ctx.MirrorY) py = ctx.ResY - py;
                if (first) { path.MoveTo(px, py); first = false; }
                else path.LineTo(px, py);
            }
            path.Close();
            ctx.Canvas.DrawPath(path, innerPaint);
        }

        return EncodePng(ctx.Surface);
    }

    /// <summary>
    /// Inset (shrink) a polygon by offsetMm along each edge normal.
    /// Simple approach: move each edge inward by offset, intersect adjacent edges.
    /// </summary>
    private static List<Vector2> InsetPolygon(List<Vector2> polygon, float offsetMm)
    {
        int n = polygon.Count;
        if (n < 3) return new List<Vector2>();

        // Compute inward-offset edges
        var offsetEdges = new List<(Vector2 a, Vector2 b)>();
        for (int i = 0; i < n; i++)
        {
            var p0 = polygon[i];
            var p1 = polygon[(i + 1) % n];
            var dx = p1.X - p0.X;
            var dy = p1.Y - p0.Y;
            var len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6f) { offsetEdges.Add((p0, p1)); continue; }

            // Inward normal (for clockwise winding: rotate edge direction 90° CW)
            var nx = dy / len;
            var ny = -dx / len;

            // Try both directions — pick the one that moves toward polygon interior
            // Simple heuristic: use the centroid to determine inside direction
            var cx = polygon.Average(p => p.X);
            var cy = polygon.Average(p => p.Y);
            var midX = (p0.X + p1.X) / 2;
            var midY = (p0.Y + p1.Y) / 2;
            var toCenterX = cx - midX;
            var toCenterY = cy - midY;
            if (nx * toCenterX + ny * toCenterY < 0) { nx = -nx; ny = -ny; }

            offsetEdges.Add((
                new Vector2(p0.X + nx * offsetMm, p0.Y + ny * offsetMm),
                new Vector2(p1.X + nx * offsetMm, p1.Y + ny * offsetMm)
            ));
        }

        // Intersect consecutive offset edges
        var result = new List<Vector2>();
        for (int i = 0; i < offsetEdges.Count; i++)
        {
            var e1 = offsetEdges[i];
            var e2 = offsetEdges[(i + 1) % offsetEdges.Count];
            var intersection = LineIntersect(e1.a, e1.b, e2.a, e2.b);
            if (intersection.HasValue)
                result.Add(intersection.Value);
        }

        return result;
    }

    private static Vector2? LineIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        var d1x = a2.X - a1.X; var d1y = a2.Y - a1.Y;
        var d2x = b2.X - b1.X; var d2y = b2.Y - b1.Y;
        var cross = d1x * d2y - d1y * d2x;
        if (MathF.Abs(cross) < 1e-10f) return null; // parallel
        var t = ((b1.X - a1.X) * d2y - (b1.Y - a1.Y) * d2x) / cross;
        return new Vector2(a1.X + t * d1x, a1.Y + t * d1y);
    }

    /// <summary>
    /// Add support column circles, raft, and skirt outlines to an already-rendered layer.
    /// Called after the main rasterize pass.
    /// </summary>
    public static void DrawSupportsRaftSkirt(
        RenderContext ctx,
        IReadOnlyList<AutoSupportEngine.GeneratedSupport>? supports,
        AutoSupportEngine.GeneratedRaft? raft,
        AutoSupportEngine.GeneratedSkirt? skirt,
        float z, float layerHeight)
    {
        using var paint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = ctx.Paint.IsAntialias };

        // Draw support column cross-sections (circles at this Z)
        if (supports is not null)
        {
            foreach (var s in supports)
            {
                if (z < s.BaseZ || z > s.ContactZ) continue;

                // Taper: base at bottom, column in middle, tip at top
                float t = (z - s.BaseZ) / Math.Max(0.01f, s.ContactZ - s.BaseZ);
                float radius;
                if (t < 0.1f) radius = s.BaseDiameter / 2;        // base
                else if (t > 0.9f) radius = s.TipDiameter / 2;    // tip
                else radius = s.ColumnDiameter / 2;                // column

                float px = s.X * ctx.ScaleX;
                float py = s.Y * ctx.ScaleY;
                if (ctx.MirrorX) px = ctx.ResX - px;
                if (ctx.MirrorY) py = ctx.ResY - py;
                float pr = radius * Math.Min(ctx.ScaleX, ctx.ScaleY);

                ctx.Canvas.DrawCircle(px, py, pr, paint);
            }
        }

        // Draw raft (filled rectangle on bottom layers)
        if (raft is not null && z <= raft.ThicknessMm)
        {
            float x1 = raft.MinX * ctx.ScaleX, y1 = raft.MinY * ctx.ScaleY;
            float x2 = raft.MaxX * ctx.ScaleX, y2 = raft.MaxY * ctx.ScaleY;
            if (ctx.MirrorX) { x1 = ctx.ResX - x1; x2 = ctx.ResX - x2; }
            if (ctx.MirrorY) { y1 = ctx.ResY - y1; y2 = ctx.ResY - y2; }
            float left = Math.Min(x1, x2), right = Math.Max(x1, x2);
            float top = Math.Min(y1, y2), bottom = Math.Max(y1, y2);

            if (raft.Type == "solid")
            {
                ctx.Canvas.DrawRect(left, top, right - left, bottom - top, paint);
            }
            else // grid or pad
            {
                // Grid: draw lines with spacing
                float spacing = raft.Type == "grid" ? 2.0f * ctx.ScaleX : 5.0f * ctx.ScaleX;
                using var linePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = ctx.Paint.IsAntialias };
                for (float gx = left; gx <= right; gx += spacing)
                    ctx.Canvas.DrawLine(gx, top, gx, bottom, linePaint);
                for (float gy = top; gy <= bottom; gy += spacing)
                    ctx.Canvas.DrawLine(left, gy, right, gy, linePaint);
                // Border
                ctx.Canvas.DrawRect(left, top, right - left, bottom - top, linePaint);
            }
        }

        // Draw skirt (outline on bottom layers)
        if (skirt is not null && z <= skirt.Layers * layerHeight)
        {
            float x1 = skirt.MinX * ctx.ScaleX, y1 = skirt.MinY * ctx.ScaleY;
            float x2 = skirt.MaxX * ctx.ScaleX, y2 = skirt.MaxY * ctx.ScaleY;
            if (ctx.MirrorX) { x1 = ctx.ResX - x1; x2 = ctx.ResX - x2; }
            if (ctx.MirrorY) { y1 = ctx.ResY - y1; y2 = ctx.ResY - y2; }
            float left = Math.Min(x1, x2), right = Math.Max(x1, x2);
            float top = Math.Min(y1, y2), bottom = Math.Max(y1, y2);
            float sw = skirt.WidthMm * ctx.ScaleX;
            using var skirtPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = sw, IsAntialias = ctx.Paint.IsAntialias };
            ctx.Canvas.DrawRect(left, top, right - left, bottom - top, skirtPaint);
        }
    }

    private static byte[] EncodePng(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
