using FluentAssertions;
using HybridSlicer.Application.Interfaces;
using HybridSlicer.Application.Interfaces.Repositories;
using HybridSlicer.Application.UseCases.GenerateToolpaths;
using HybridSlicer.Domain.Entities;
using HybridSlicer.Domain.Enums;
using HybridSlicer.Infrastructure.Toolpath;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace HybridSlicer.Application.Tests;

/// <summary>
/// Verifies the "Machine inner walls" checkbox behaviour end-to-end through
/// GenerateToolpathsHandler:
///   • Unchecked (false): inner wall paths must NOT be sent to the planner.
///   • Checked   (true):  BOTH outer AND inner wall paths must be sent.
///
/// Strategy: mock the I/O dependencies (repositories, parser, safety, coord
/// translator), inject a SpyPlanner that wraps the real ContourToolpathPlanner
/// and records every WallPathsRequest it receives, then assert on the captured
/// requests' IsOuterWall flag and WallPaths reference equality.
/// </summary>
public class MachineInnerWallsFlagTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(),
        "hs-machine-inner-walls-" + Guid.NewGuid().ToString("N"));

    public MachineInnerWallsFlagTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ── A planner that records every call but still produces real toolpaths ────
    private sealed class SpyPlanner : IToolpathPlanner
    {
        private readonly IToolpathPlanner _inner;
        public List<WallPathsRequest> Calls { get; } = new();
        public SpyPlanner(IToolpathPlanner inner) => _inner = inner;
        public Task<ToolpathResult> PlanFromWallPathsAsync(
            WallPathsRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            return _inner.PlanFromWallPathsAsync(request, ct);
        }
        public Task<ToolpathResult> PlanContourAsync(
            ToolpathRequest request, CancellationToken ct = default)
            => _inner.PlanContourAsync(request, ct);
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────
    // CCW 20×20 mm square (outer wall) and CW 10×10 mm square (inner pocket).
    private static IReadOnlyList<(double X, double Y)> OuterWallCcw() => new[]
    {
        (-10.0, -10.0), (10.0, -10.0), (10.0, 10.0), (-10.0, 10.0), (-10.0, -10.0),
    };
    private static IReadOnlyList<(double X, double Y)> InnerWallCw() => new[]
    {
        (-5.0, -5.0), (-5.0, 5.0), (5.0, 5.0), (5.0, -5.0), (-5.0, -5.0),
    };

    private record TestArrangement(
        GenerateToolpathsHandler Handler,
        SpyPlanner Spy,
        Guid JobId,
        Guid CncToolId,
        IReadOnlyList<(double X, double Y)> CapturedOuter,
        IReadOnlyList<(double X, double Y)> CapturedInner,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> CapturedOuterPaths,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> CapturedInnerPaths);

    // Builds an arrangement where every layer has the SAME outer + inner paths.
    private TestArrangement Build(int totalLayers = 5, int machineEveryN = 5)
        => BuildWith(
            outerPaths: new[] { OuterWallCcw() },
            innerPaths: new[] { InnerWallCw() },
            totalLayers: totalLayers);

    private TestArrangement BuildWith(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> outerPaths,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> innerPaths,
        int totalLayers = 5)
    {
        // Place a placeholder print.gcode under a job directory inside the temp dir.
        // The parser is mocked so contents don't matter — but the handler reads the
        // file and uses its directory to write toolpath.gcode.
        var jobDir = Path.Combine(_tempDir, "job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobDir);
        var stlPath  = Path.Combine(jobDir, "model.stl");
        var gcodePath = Path.Combine(jobDir, "print.gcode");
        File.WriteAllText(stlPath, "");
        File.WriteAllText(gcodePath, "; placeholder — parser is mocked\n");

        // Real-ish entities
        var machine = MachineProfile.Create(
            "TestMachine", MachineType.Hybrid,
            bedWidth: 300, bedDepth: 300, bedHeight: 300);
        var printProfile = PrintProfile.Create("TestProfile");
        var tool = CncTool.Create(
            "TestEndmill", ToolType.FlatEndMill,
            diameterMm: 4.0, fluteLengthMm: 10.0, shankDiameterMm: 6.0,
            recommendedRpm: 12000, recommendedFeedMmPerMin: 500, toolLengthMm: 30);
        var job = PrintJob.Create(
            "test-job", stlPath,
            machineProfileId: machine.Id,
            printProfileId:   printProfile.Id,
            materialId:       Guid.NewGuid());
        job.MarkSlicing();
        job.MarkSlicingComplete(gcodePath, totalLayers);

        // Mocked layer data: every layer has the supplied outer + inner paths.
        // The handler's "manual schedule" only fires for layers whose
        // partLayerCount is a multiple of MachineEveryNLayers (counting only
        // layers that actually have wall geometry). Populating every layer
        // makes the schedule deterministic.
        var layers = new Dictionary<int, ParsedCuraLayer>();
        for (var li = 0; li < totalLayers; li++)
        {
            layers[li] = new ParsedCuraLayer(li, (li + 1) * printProfile.LayerHeightMm,
                OuterWallPaths: outerPaths,
                InnerWallPaths: innerPaths,
                SupportPaths:   Array.Empty<IReadOnlyList<(double X, double Y)>>());
        }
        var parsed = new ParsedCuraGCode(layers);

        // Mocks
        var jobs    = Substitute.For<IPrintJobRepository>();
        jobs.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var machines = Substitute.For<IMachineProfileRepository>();
        machines.GetByIdAsync(machine.Id, Arg.Any<CancellationToken>()).Returns(machine);

        var profiles = Substitute.For<IPrintProfileRepository>();
        profiles.GetByIdAsync(printProfile.Id, Arg.Any<CancellationToken>()).Returns(printProfile);

        var tools = Substitute.For<ICncToolRepository>();
        tools.GetByIdAsync(tool.Id, Arg.Any<CancellationToken>()).Returns(tool);

        var parser = Substitute.For<ICuraGCodeParser>();
        parser.Parse(Arg.Any<string>()).Returns(parsed);

        var safety = Substitute.For<ISafetyValidator>();
        safety.ValidateToolpathAsync(Arg.Any<SafetyValidationRequest>(), Arg.Any<CancellationToken>())
              .Returns(new SafetyValidationResult(SafetyStatus.Clear, Array.Empty<string>()));

        var translator = Substitute.For<IMachineCoordinateTranslator>();
        translator.RemapAxesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                  .Returns(Task.CompletedTask);

        var realPlanner = new ContourToolpathPlanner(NullLogger<ContourToolpathPlanner>.Instance);
        var spy = new SpyPlanner(realPlanner);

        var handler = new GenerateToolpathsHandler(
            jobs, profiles, machines, tools, spy, safety, parser, translator,
            NullLogger<GenerateToolpathsHandler>.Instance);

        // Back-compat single-path accessors (point to the first of each bucket).
        var outerRef = outerPaths.Count > 0 ? outerPaths[0] : Array.Empty<(double X, double Y)>();
        var innerRef = innerPaths.Count > 0 ? innerPaths[0] : Array.Empty<(double X, double Y)>();
        return new TestArrangement(handler, spy, job.Id, tool.Id, outerRef, innerRef, outerPaths, innerPaths);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unchecked_NoInnerWallPlannerCalls()
    {
        var a = Build();
        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId,
            CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: false);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Should().NotBeEmpty("the handler must call the planner for the outer wall");
        a.Spy.Calls.Should().OnlyContain(c => c.IsOuterWall,
            "with MachineInnerWalls=false, every planner call must be for an outer-wall request");
        a.Spy.Calls.SelectMany(c => c.WallPaths)
                    .Should().NotContain(p => ReferenceEquals(p, a.CapturedInner),
            "the inner-wall path must NEVER reach the planner when the flag is off");
    }

    [Fact]
    public async Task Checked_BothOuterAndInnerWallPlannerCalls()
    {
        var a = Build();
        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId,
            CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: true);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Should().HaveCount(2,
            "exactly two planner calls per machined layer: one outer, one inner");
        a.Spy.Calls.Should().ContainSingle(c => c.IsOuterWall,
            "exactly one outer-wall call");
        a.Spy.Calls.Should().ContainSingle(c => !c.IsOuterWall,
            "exactly one inner-wall call");

        var outerCall = a.Spy.Calls.Single(c => c.IsOuterWall);
        var innerCall = a.Spy.Calls.Single(c => !c.IsOuterWall);
        outerCall.WallPaths.Should().ContainSingle()
                 .Which.Should().BeSameAs(a.CapturedOuter);
        innerCall.WallPaths.Should().ContainSingle()
                 .Which.Should().BeSameAs(a.CapturedInner);
    }

    [Fact]
    public async Task Unchecked_NoInnerWallsPresent_StillCallsOuterOnly()
    {
        // Even when inner walls would exist, the unchecked flag suppresses them.
        // This guards against accidental call-through.
        var a = Build();
        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId,
            CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: false);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Should().HaveCount(1,
            "with MachineInnerWalls=false there should be exactly one planner call (outer)");
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true,  2)]
    public async Task PlannerCallCount_MatchesFlag(bool machineInnerWalls, int expectedCallCount)
    {
        var a = Build();
        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId,
            CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: machineInnerWalls);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Count.Should().Be(expectedCallCount,
            $"MachineInnerWalls={machineInnerWalls} → expected {expectedCallCount} planner call(s)");
    }

    // ── Tests for Cura emitting holes inside WALL-OUTER ───────────────────────
    // Real Cura output bundles BOTH the part exterior AND every hole perimeter
    // under WALL-OUTER. The user's reported bug: with "Machine inner walls" off,
    // a part with a circular hole still got its hole machined as a second
    // contour. The handler must split OuterWallPaths by winding and gate the
    // CW (hole) paths on the MachineInnerWalls flag.

    // CW circle around (0,0), radius r. CW = inner/hole orientation.
    private static IReadOnlyList<(double X, double Y)> CircleCw(double r, int segs = 64)
    {
        var pts = new List<(double X, double Y)>(segs + 1);
        for (var i = 0; i <= segs; i++)
        {
            var t = -i / (double)segs * 2 * Math.PI;  // negative → CW
            pts.Add((r * Math.Cos(t), r * Math.Sin(t)));
        }
        return pts;
    }
    // CCW square at given centre/size. CCW = part-exterior orientation.
    private static IReadOnlyList<(double X, double Y)> SquareCcw(
        double cx, double cy, double size)
    {
        var h = size / 2.0;
        return new[] {
            (cx - h, cy - h), (cx + h, cy - h),
            (cx + h, cy + h), (cx - h, cy + h),
            (cx - h, cy - h),
        };
    }

    [Fact]
    public async Task Unchecked_PartWithHole_OnlyExteriorMachined()
    {
        // Cura-style: exterior square + hole circle, BOTH in OuterWallPaths.
        var exterior = SquareCcw(0, 0, 40);   // CCW (positive signed area)
        var hole     = CircleCw(8);            // CW  (negative signed area)
        var a = BuildWith(
            outerPaths: new[] { exterior, hole },
            innerPaths: Array.Empty<IReadOnlyList<(double X, double Y)>>());

        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId, CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: false);

        await a.Handler.Handle(cmd, default);

        // Exactly one planner call — the outer-wall pass — with ONLY the exterior.
        a.Spy.Calls.Should().HaveCount(1,
            "with MachineInnerWalls=false the hole inside WALL-OUTER must NOT trigger a separate inner pass");
        var call = a.Spy.Calls.Single();
        call.IsOuterWall.Should().BeTrue();
        call.WallPaths.Should().ContainSingle("only the exterior CCW ring should be machined");
        call.WallPaths.Single().Should().BeSameAs(exterior);
    }

    [Fact]
    public async Task Checked_PartWithHole_BothExteriorAndHoleMachined()
    {
        var exterior = SquareCcw(0, 0, 40);
        var hole     = CircleCw(8);
        var a = BuildWith(
            outerPaths: new[] { exterior, hole },
            innerPaths: Array.Empty<IReadOnlyList<(double X, double Y)>>());

        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId, CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: true);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Should().HaveCount(2,
            "outer pass for the exterior + inner pass for the hole");
        a.Spy.Calls.Should().ContainSingle(c => c.IsOuterWall);
        a.Spy.Calls.Should().ContainSingle(c => !c.IsOuterWall);

        var outerCall = a.Spy.Calls.Single(c => c.IsOuterWall);
        var innerCall = a.Spy.Calls.Single(c => !c.IsOuterWall);
        outerCall.WallPaths.Single().Should().BeSameAs(exterior);
        innerCall.WallPaths.Single().Should().BeSameAs(hole);
    }

    // Disjoint hole at an arbitrary position inside a part (CW = Cura's
    // typical hole orientation).
    private static IReadOnlyList<(double X, double Y)> HoleAt(double cx, double cy, double r, int segs = 32)
    {
        var pts = new List<(double X, double Y)>(segs + 1);
        for (var i = 0; i <= segs; i++)
        {
            var t = -i / (double)segs * 2 * Math.PI;
            pts.Add((cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
        }
        return pts;
    }

    [Fact]
    public async Task Unchecked_MultipleHoles_AllSuppressed()
    {
        // A part with three disjoint holes — all CW, all inside WALL-OUTER.
        var exterior = SquareCcw(0, 0, 60);
        var hole1    = HoleAt(-20,   0, 5);
        var hole2    = HoleAt( 20,   0, 3);
        var hole3    = HoleAt(  0,  20, 2);
        var a = BuildWith(
            outerPaths: new[] { exterior, hole1, hole2, hole3 },
            innerPaths: Array.Empty<IReadOnlyList<(double X, double Y)>>());

        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId, CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: false);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Should().HaveCount(1);
        a.Spy.Calls.Single().WallPaths.Should().ContainSingle()
            .Which.Should().BeSameAs(exterior);
    }

    [Fact]
    public async Task Checked_MultipleHolesAndWallInner_AllPassedToInnerRequest()
    {
        var exterior  = SquareCcw(0, 0, 60);
        var hole1     = HoleAt(-20, 0, 5);
        var hole2     = HoleAt( 20, 0, 3);
        var wallInner = InnerWallCw();  // Cura WALL-INNER concentric perimeter
        var a = BuildWith(
            outerPaths: new[] { exterior, hole1, hole2 },
            innerPaths: new[] { wallInner });

        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId, CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: true);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Should().HaveCount(2);
        var innerCall = a.Spy.Calls.Single(c => !c.IsOuterWall);
        innerCall.WallPaths.Should().HaveCount(3,
            "inner pass should bundle the two CW holes from WALL-OUTER plus the WALL-INNER path");
        innerCall.WallPaths.Should().Contain(hole1);
        innerCall.WallPaths.Should().Contain(hole2);
        innerCall.WallPaths.Should().Contain(wallInner);
    }

    [Fact]
    public async Task Unchecked_NoExterior_OnlyHole_ProducesEmptyTopLevelButNoInnerPass()
    {
        // Pathological: WALL-OUTER contains a SINGLE isolated ring. Since it
        // is not nested inside anything, it's depth-0 → treated as exterior,
        // so it IS machined (matches user intent: any unenclosed perimeter is
        // a real boundary, not a hole).
        var loop = CircleCw(8);
        var a = BuildWith(
            outerPaths: new[] { loop },
            innerPaths: Array.Empty<IReadOnlyList<(double X, double Y)>>());

        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId, CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: false);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Should().HaveCount(1, "an isolated unenclosed CW ring is a real boundary, not a hole");
        a.Spy.Calls.Single().IsOuterWall.Should().BeTrue();
    }

    // ── Containment-based classification — winding agnostic ───────────────────

    // CW circle around (cx,cy), radius r.
    private static IReadOnlyList<(double X, double Y)> CircleCwAt(double cx, double cy, double r, int segs = 64)
    {
        var pts = new List<(double X, double Y)>(segs + 1);
        for (var i = 0; i <= segs; i++)
        {
            var t = -i / (double)segs * 2 * Math.PI;
            pts.Add((cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
        }
        return pts;
    }
    // CCW circle around (cx,cy), radius r.
    private static IReadOnlyList<(double X, double Y)> CircleCcwAt(double cx, double cy, double r, int segs = 64)
    {
        var pts = new List<(double X, double Y)>(segs + 1);
        for (var i = 0; i <= segs; i++)
        {
            var t = i / (double)segs * 2 * Math.PI;
            pts.Add((cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
        }
        return pts;
    }

    [Fact]
    public async Task Unchecked_BothCwConcentric_LargerKeptAsExterior()
    {
        // The user's actual case: Cura emitted both the part exterior AND the
        // hole inside as CW (concentric circles at (603, 168), radii ~14 and
        // ~9). The handler must still treat the outer one as the exterior and
        // suppress the hole when the flag is off.
        var outer = CircleCwAt(603, 168, 14, segs: 96);
        var hole  = CircleCwAt(603, 168,  9, segs: 96);
        var a = BuildWith(
            outerPaths: new[] { outer, hole },
            innerPaths: Array.Empty<IReadOnlyList<(double X, double Y)>>());

        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId, CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: false);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Should().HaveCount(1, "the user's concentric-CW case: only the larger ring is the exterior");
        a.Spy.Calls.Single().WallPaths.Should().ContainSingle()
            .Which.Should().BeSameAs(outer);
    }

    [Fact]
    public async Task Unchecked_BothCcwConcentric_LargerKeptAsExterior()
    {
        // Mirror of the previous test: same geometry, opposite winding.
        var outer = CircleCcwAt(0, 0, 20, segs: 96);
        var hole  = CircleCcwAt(0, 0, 10, segs: 96);
        var a = BuildWith(
            outerPaths: new[] { outer, hole },
            innerPaths: Array.Empty<IReadOnlyList<(double X, double Y)>>());

        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId, CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: false);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Should().HaveCount(1);
        a.Spy.Calls.Single().WallPaths.Should().ContainSingle()
            .Which.Should().BeSameAs(outer);
    }

    [Fact]
    public async Task Unchecked_TwoSeparateParts_BothMachined()
    {
        // Two disjoint parts on the bed — neither contained by the other.
        // Both depth=0 → both treated as exterior, both machined.
        var part1 = CircleCcwAt(  0,   0, 15, segs: 32);
        var part2 = CircleCwAt (100, 100, 12, segs: 32);
        var a = BuildWith(
            outerPaths: new[] { part1, part2 },
            innerPaths: Array.Empty<IReadOnlyList<(double X, double Y)>>());

        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId, CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: false);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Should().HaveCount(1);
        a.Spy.Calls.Single().WallPaths.Should().HaveCount(2);
        a.Spy.Calls.Single().WallPaths.Should().Contain(part1);
        a.Spy.Calls.Single().WallPaths.Should().Contain(part2);
    }

    [Fact]
    public async Task Unchecked_IslandInsideHole_IslandMachined()
    {
        // 3-level nesting: big exterior, hole inside it, island inside the hole.
        // Even-odd rule: depth-0 outer ✓, depth-1 hole ✗, depth-2 island ✓.
        var outer  = CircleCcwAt(0, 0, 50, segs: 64);
        var hole   = CircleCwAt (0, 0, 30, segs: 64);
        var island = CircleCcwAt(0, 0, 10, segs: 64);
        var a = BuildWith(
            outerPaths: new[] { outer, hole, island },
            innerPaths: Array.Empty<IReadOnlyList<(double X, double Y)>>());

        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId, CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: false);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Should().HaveCount(1);
        var paths = a.Spy.Calls.Single().WallPaths;
        paths.Should().HaveCount(2, "even-depth: the big outer (0) AND the island (2)");
        paths.Should().Contain(outer);
        paths.Should().Contain(island);
        paths.Should().NotContain(hole);
    }

    [Fact]
    public async Task Checked_BothCwConcentric_HoleSentAsInner()
    {
        // Same as Unchecked_BothCwConcentric but with the flag on — the hole
        // should now reach the planner as an inner pass.
        var outer = CircleCwAt(603, 168, 14, segs: 96);
        var hole  = CircleCwAt(603, 168,  9, segs: 96);
        var a = BuildWith(
            outerPaths: new[] { outer, hole },
            innerPaths: Array.Empty<IReadOnlyList<(double X, double Y)>>());

        var cmd = new GenerateToolpathsCommand(
            JobId: a.JobId, CncToolId: a.CncToolId,
            MachineEveryNLayers: 5,
            MachineInnerWalls: true);

        await a.Handler.Handle(cmd, default);

        a.Spy.Calls.Should().HaveCount(2);
        a.Spy.Calls.Single(c => c.IsOuterWall).WallPaths.Single().Should().BeSameAs(outer);
        a.Spy.Calls.Single(c => !c.IsOuterWall).WallPaths.Single().Should().BeSameAs(hole);
    }
}
