using System.Numerics;

namespace HybridSlicer.Infrastructure.Resin;

/// <summary>
/// Detects unsupported islands — contours in a layer that have no overlap
/// with contours in the previous layer (floating geometry).
/// </summary>
public static class IslandDetector
{
    /// <summary>
    /// Compare two consecutive layers and find contours in the current layer
    /// that don't overlap with any contour in the previous layer.
    /// Returns the number of island contours.
    /// </summary>
    public static int DetectIslands(
        List<List<Vector2>> currentLayer,
        List<List<Vector2>> previousLayer)
    {
        if (previousLayer.Count == 0)
            return 0; // first layer — everything is on the build plate

        if (currentLayer.Count == 0)
            return 0;

        int islands = 0;
        foreach (var contour in currentLayer)
        {
            if (contour.Count < 3) continue;

            // Check if the centroid of this contour falls inside any previous-layer contour
            var centroid = ComputeCentroid(contour);
            bool hasSupport = false;

            foreach (var prevContour in previousLayer)
            {
                if (PointInPolygon(centroid, prevContour))
                {
                    hasSupport = true;
                    break;
                }
            }

            if (!hasSupport) islands++;
        }

        return islands;
    }

    private static Vector2 ComputeCentroid(List<Vector2> polygon)
    {
        float cx = 0, cy = 0;
        foreach (var p in polygon) { cx += p.X; cy += p.Y; }
        return new Vector2(cx / polygon.Count, cy / polygon.Count);
    }

    private static bool PointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        int n = polygon.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                inside = !inside;
        }
        return inside;
    }
}
