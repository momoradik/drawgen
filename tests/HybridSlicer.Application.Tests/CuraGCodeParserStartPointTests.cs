using FluentAssertions;
using HybridSlicer.Infrastructure.Toolpath;
using Xunit;

namespace HybridSlicer.Application.Tests;

/// <summary>
/// Validates that CuraGCodeParser includes the starting position of each
/// wall segment (the nozzle position BEFORE the first G1 extrusion move).
/// Without this, wall paths miss their first vertex, appear open instead
/// of closed, and CNC contour toolpaths fail to trace around the part.
/// </summary>
public class CuraGCodeParserStartPointTests
{
    private readonly CuraGCodeParser _parser = new();

    /// <summary>
    /// A simple square wall: nozzle travels to (10,10) via G0, then extrudes
    /// around a square back to (10,10). The parser must include (10,10) as the
    /// first point so the path is closed.
    /// </summary>
    [Fact]
    public void ClosedSquareWall_IncludesStartPoint()
    {
        var gcode = string.Join("\n",
            ";LAYER:0",
            "G0 X10 Y10",           // rapid to start
            ";TYPE:WALL-OUTER",
            "G1 X20 Y10 E1",        // right
            "G1 X20 Y20 E2",        // up
            "G1 X10 Y20 E3",        // left
            "G1 X10 Y10 E4",        // back to start
            ";TYPE:FILL");

        var parsed = _parser.Parse(gcode);
        parsed.Layers.Should().ContainKey(0);

        var walls = parsed.Layers[0].OuterWallPaths;
        walls.Should().HaveCount(1);

        var path = walls[0];
        // Must have 5 points: (10,10) start + 4 G1 destinations
        path.Should().HaveCount(5);
        path[0].Should().Be((10.0, 10.0), "first point should be the start position before G1");
        path[^1].Should().Be((10.0, 10.0), "last point should close the loop");
    }

    /// <summary>
    /// When a wall segment starts after a G0 rapid, the nozzle's G0 destination
    /// must be the first point of the following wall segment.
    /// </summary>
    [Fact]
    public void WallAfterRapid_StartsFromRapidDestination()
    {
        var gcode = string.Join("\n",
            ";LAYER:0",
            "G0 X50 Y60",
            ";TYPE:WALL-OUTER",
            "G1 X70 Y60 E1",
            "G1 X70 Y80 E2",
            ";TYPE:FILL");

        var parsed = _parser.Parse(gcode);
        var path = parsed.Layers[0].OuterWallPaths[0];

        // 3 points: (50,60) start + (70,60) + (70,80)
        path.Should().HaveCount(3);
        path[0].Should().Be((50.0, 60.0), "start point from G0 rapid");
        path[1].Should().Be((70.0, 60.0));
        path[2].Should().Be((70.0, 80.0));
    }

    /// <summary>
    /// Multiple wall segments separated by G0 rapids should each include
    /// their own starting position.
    /// </summary>
    [Fact]
    public void MultipleSegments_EachIncludesStartPoint()
    {
        var gcode = string.Join("\n",
            ";LAYER:0",
            "G0 X10 Y10",
            ";TYPE:WALL-OUTER",
            "G1 X20 Y10 E1",
            "G1 X20 Y20 E2",
            "G0 X50 Y50",              // rapid break
            "G1 X60 Y50 E3",           // new segment
            "G1 X60 Y60 E4",
            ";TYPE:FILL");

        var parsed = _parser.Parse(gcode);
        var walls = parsed.Layers[0].OuterWallPaths;

        walls.Should().HaveCount(2);

        // Segment 1: starts from (10,10)
        walls[0].Should().HaveCount(3);
        walls[0][0].Should().Be((10.0, 10.0));

        // Segment 2: starts from (50,50) — the G0 destination
        walls[1].Should().HaveCount(3);
        walls[1][0].Should().Be((50.0, 50.0));
    }

    /// <summary>
    /// Simulates the real-world multi-bed scenario where Cura emits very short
    /// wall segments (1-2 G1 moves) separated by bed-switch rapids. Even these
    /// short segments must include the starting point.
    /// </summary>
    [Fact]
    public void MultiBedShortSegments_IncludeStartPoints()
    {
        var gcode = string.Join("\n",
            ";LAYER:0",
            "G0 X-100 Y-160",
            ";TYPE:WALL-OUTER",
            "G1 F1500 E0",                          // retraction — breaks seg
            "G1 F1200 X-100.254 Y-159.771 E0.0023", // single extrusion move
            "G0 F600 Z0.2",                          // rapid breaks segment
            "G0 F3600 X-100.304 Y-160.022",          // travel
            "G0 X109.040 Y-163.148",                 // travel to next bed
            ";TYPE:WALL-OUTER",
            "G1 F1500 E0",                           // retraction
            "G1 F1200 X109.046 Y-162.871 E0.0023",  // single extrusion move
            "G0 X50 Y50",
            ";TYPE:FILL");

        var parsed = _parser.Parse(gcode);
        var walls = parsed.Layers[0].OuterWallPaths;

        // Each short segment should have 2 points (start + 1 G1 destination)
        foreach (var wall in walls)
        {
            wall.Count.Should().BeGreaterThanOrEqualTo(2,
                "even single-G1 segments must include the start position");
        }
    }

    /// <summary>
    /// A closed circular wall should have its start point so that
    /// BuildGeometry creates a Polygon (not a LineString).
    /// </summary>
    [Fact]
    public void CircularWall_ClosedPolygon_IncludesStartVertex()
    {
        // Approximate circle: 8 vertices + close
        var gcode = string.Join("\n",
            ";LAYER:0",
            "G0 X30 Y0",
            ";TYPE:WALL-OUTER",
            "G1 X21.213 Y21.213 E1",
            "G1 X0 Y30 E2",
            "G1 X-21.213 Y21.213 E3",
            "G1 X-30 Y0 E4",
            "G1 X-21.213 Y-21.213 E5",
            "G1 X0 Y-30 E6",
            "G1 X21.213 Y-21.213 E7",
            "G1 X30 Y0 E8",    // closes back to start
            ";TYPE:FILL");

        var parsed = _parser.Parse(gcode);
        var path = parsed.Layers[0].OuterWallPaths[0];

        // Must have 9 points: (30,0) start + 8 G1 destinations
        path.Should().HaveCount(9);
        path[0].Should().Be((30.0, 0.0));
        path[^1].Should().Be((30.0, 0.0));

        // First == Last → BuildGeometry will create a closed Polygon
        var (fx, fy) = path[0];
        var (lx, ly) = path[^1];
        var dist = Math.Sqrt((lx - fx) * (lx - fx) + (ly - fy) * (ly - fy));
        dist.Should().BeLessThan(0.5, "path must be closed so BuildGeometry creates a Polygon");
    }

    /// <summary>
    /// Runs 20 iterations of a randomized wall pattern and verifies
    /// every segment includes the start point.
    /// </summary>
    [Fact]
    public void TwentyRandomizedWalls_AllIncludeStartPoint()
    {
        var rng = new Random(42);

        for (int iter = 0; iter < 20; iter++)
        {
            var lines = new List<string> { ";LAYER:0" };
            var nSegments = rng.Next(1, 6);
            var expectedSegments = new List<(double startX, double startY, int nMoves)>();

            for (int s = 0; s < nSegments; s++)
            {
                var sx = Math.Round(rng.NextDouble() * 200 - 100, 3);
                var sy = Math.Round(rng.NextDouble() * 200 - 100, 3);
                lines.Add($"G0 X{sx} Y{sy}");
                lines.Add(";TYPE:WALL-OUTER");

                var nMoves = rng.Next(2, 20);
                for (int m = 0; m < nMoves; m++)
                {
                    var mx = Math.Round(sx + rng.NextDouble() * 50 - 25, 3);
                    var my = Math.Round(sy + rng.NextDouble() * 50 - 25, 3);
                    lines.Add($"G1 X{mx} Y{my} E{m + 1}");
                }

                expectedSegments.Add((sx, sy, nMoves));
            }
            lines.Add(";TYPE:FILL");

            var gcode = string.Join("\n", lines);
            var parsed = _parser.Parse(gcode);
            var walls = parsed.Layers[0].OuterWallPaths;

            walls.Should().HaveCount(nSegments,
                $"iteration {iter}: expected {nSegments} segments");

            for (int s = 0; s < nSegments; s++)
            {
                var (sx, sy, nMoves) = expectedSegments[s];
                var wall = walls[s];

                wall.Count.Should().Be(nMoves + 1,
                    $"iteration {iter} segment {s}: should have start point + {nMoves} G1 moves");

                wall[0].X.Should().BeApproximately(sx, 0.001,
                    $"iteration {iter} segment {s}: first X should be G0 rapid X");
                wall[0].Y.Should().BeApproximately(sy, 0.001,
                    $"iteration {iter} segment {s}: first Y should be G0 rapid Y");
            }
        }
    }
}
