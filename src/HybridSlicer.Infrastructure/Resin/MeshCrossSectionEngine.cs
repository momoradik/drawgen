using System.Numerics;

namespace HybridSlicer.Infrastructure.Resin;

/// <summary>
/// Cross-sections a triangle mesh at a given Z height.
/// Returns closed polygon contours as lists of 2D points.
/// Uses edge-plane intersection — O(N) per layer where N = triangle count.
/// </summary>
public static class MeshCrossSectionEngine
{
    /// <summary>
    /// Compute the cross-section of the mesh at the given Z height.
    /// Returns a list of closed polygons (each polygon is a list of (x,y) points).
    /// </summary>
    public static List<List<Vector2>> CrossSection(StlMesh mesh, float z)
    {
        // Step 1: collect all edge-plane intersection segments
        var segments = new List<(Vector2 a, Vector2 b)>();

        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var v0 = mesh.Vertices[t * 3];
            var v1 = mesh.Vertices[t * 3 + 1];
            var v2 = mesh.Vertices[t * 3 + 2];

            // Find intersection points of triangle edges with Z plane
            var pts = new List<Vector2>(3);

            AddEdgeIntersection(pts, v0, v1, z);
            AddEdgeIntersection(pts, v1, v2, z);
            AddEdgeIntersection(pts, v2, v0, z);

            if (pts.Count >= 2)
                segments.Add((pts[0], pts[1]));
        }

        if (segments.Count == 0)
            return new List<List<Vector2>>();

        // Step 2: chain segments into closed polygons
        return ChainSegments(segments);
    }

    private static void AddEdgeIntersection(List<Vector2> pts, Vector3 a, Vector3 b, float z)
    {
        // Check if the edge crosses the Z plane
        if ((a.Z - z) * (b.Z - z) > 0) return;  // both on same side
        if (Math.Abs(a.Z - b.Z) < 1e-8f) return; // edge parallel to plane

        float t = (z - a.Z) / (b.Z - a.Z);
        if (t < 0 || t > 1) return;

        float x = a.X + t * (b.X - a.X);
        float y = a.Y + t * (b.Y - a.Y);
        pts.Add(new Vector2(x, y));
    }

    private static List<List<Vector2>> ChainSegments(List<(Vector2 a, Vector2 b)> segments)
    {
        var result = new List<List<Vector2>>();
        var used = new bool[segments.Count];
        const float EPS = 0.001f;

        for (int start = 0; start < segments.Count; start++)
        {
            if (used[start]) continue;
            used[start] = true;

            var polygon = new List<Vector2> { segments[start].a, segments[start].b };
            var current = segments[start].b;
            bool changed = true;

            while (changed)
            {
                changed = false;
                for (int i = 0; i < segments.Count; i++)
                {
                    if (used[i]) continue;

                    if (Vector2.Distance(current, segments[i].a) < EPS)
                    {
                        used[i] = true;
                        polygon.Add(segments[i].b);
                        current = segments[i].b;
                        changed = true;
                        break;
                    }
                    if (Vector2.Distance(current, segments[i].b) < EPS)
                    {
                        used[i] = true;
                        polygon.Add(segments[i].a);
                        current = segments[i].a;
                        changed = true;
                        break;
                    }
                }
            }

            if (polygon.Count >= 3)
                result.Add(polygon);
        }

        return result;
    }
}
