using System.Numerics;
using HybridSlicer.Domain.Enums;

namespace HybridSlicer.Infrastructure.Resin;

/// <summary>
/// Advanced resin support generation engine.
/// 100% original implementation — no external libraries.
///
/// Support types:
/// - Light: minimal contact, thin shaft, easy removal, small marks
/// - Medium: balanced contact, standard shaft, reliable
/// - Heavy: maximum contact, thick shaft, critical overhangs
/// - Tree: branching structure — multiple tips merge into shared trunks
/// - CrossBraced: columns with diagonal struts for rigidity
///
/// Support anatomy (per support):
/// - Contact tip: sphere/cone at model surface
/// - Neck: thin breakpoint for easy removal
/// - Upper taper: transition from neck to shaft
/// - Shaft: main column body
/// - Lower taper: transition from shaft to base
/// - Base/foot: wider platform on build plate
///
/// Cross-bracing:
/// - Diagonal struts between adjacent supports at regular intervals
/// - Dramatically increases rigidity for tall/thin parts
/// </summary>
public static class AdvancedSupportEngine
{
    // ── Support presets ──────────────────────────────────────────────────────

    public sealed record SupportPreset
    {
        public string Name { get; init; } = "Medium";
        // Contact tip
        public float TipDiameterMm { get; init; } = 0.5f;
        public string TipShape { get; init; } = "sphere";  // point|sphere|cone|flat|pyramid|skate|chisel|mushroom|cross|ring|needle
        public float ContactDepthMm { get; init; } = 0.2f; // penetration into model
        // Neck (breakpoint)
        public float NeckDiameterMm { get; init; } = 0.3f;
        public float NeckLengthMm { get; init; } = 0.5f;
        public string NeckType { get; init; } = "thin";     // thin|waist|perforated|graduated|double
        // Shaft
        public float ShaftDiameterMm { get; init; } = 0.8f;
        public string ShaftType { get; init; } = "cylinder"; // cylinder|cone|hollow|square|xprofile|ibeam|lattice|spiral|ribbed|diamond
        // Tapers
        public float UpperTaperLengthMm { get; init; } = 1.0f;
        public float LowerTaperLengthMm { get; init; } = 1.5f;
        // Base/foot
        public float BaseDiameterMm { get; init; } = 2.0f;
        public float BaseHeightMm { get; init; } = 0.5f;
        public string BaseType { get; init; } = "disc";     // disc|cone|pyramid|raft|miniraft|pin|skirted|webbed|anchor|pad
        // Cross-brace
        public float BraceDiameterMm { get; init; } = 0.4f;
        public float BraceIntervalMm { get; init; } = 5.0f;
        public string BraceType { get; init; } = "diagonal"; // diagonal|horizontal|truss|ladder|shore
        // Structure
        public string StructureType { get; init; } = "column"; // column|tapered|tree|crossbraced|lattice|wall|gusset|cage|organic|truss|scaffold
    }

    // ── ALL SUPPORT PRESETS (from research taxonomy) ─────────────────────────

    // --- Column types (weight variants) ---
    public static readonly SupportPreset LightPreset = new() {
        Name="Light", TipDiameterMm=0.25f, TipShape="point", ContactDepthMm=0.1f,
        NeckDiameterMm=0.12f, NeckLengthMm=0.3f, NeckType="thin",
        ShaftDiameterMm=0.4f, ShaftType="cylinder",
        UpperTaperLengthMm=0.5f, LowerTaperLengthMm=1.0f,
        BaseDiameterMm=1.2f, BaseHeightMm=0.3f, BaseType="disc",
        BraceDiameterMm=0.2f, BraceIntervalMm=8.0f, StructureType="column",
    };
    public static readonly SupportPreset MediumPreset = new() {
        Name="Medium", TipDiameterMm=0.5f, TipShape="cone", ContactDepthMm=0.2f,
        NeckDiameterMm=0.3f, NeckLengthMm=0.5f, NeckType="waist",
        ShaftDiameterMm=0.8f, ShaftType="cylinder",
        UpperTaperLengthMm=1.0f, LowerTaperLengthMm=1.5f,
        BaseDiameterMm=2.0f, BaseHeightMm=0.5f, BaseType="disc",
        BraceDiameterMm=0.4f, BraceIntervalMm=5.0f, StructureType="column",
    };
    public static readonly SupportPreset HeavyPreset = new() {
        Name="Heavy", TipDiameterMm=1.0f, TipShape="flat", ContactDepthMm=0.4f,
        NeckDiameterMm=0.6f, NeckLengthMm=0.8f, NeckType="graduated",
        ShaftDiameterMm=1.5f, ShaftType="cylinder",
        UpperTaperLengthMm=1.5f, LowerTaperLengthMm=2.0f,
        BaseDiameterMm=3.0f, BaseHeightMm=0.8f, BaseType="anchor",
        BraceDiameterMm=0.6f, BraceIntervalMm=4.0f, StructureType="column",
    };

    // --- Tip shape variants ---
    public static readonly SupportPreset PointTipPreset = new() {
        Name="Point Tip", TipDiameterMm=0.15f, TipShape="point", ContactDepthMm=0.05f,
        NeckDiameterMm=0.1f, NeckLengthMm=0.2f, ShaftDiameterMm=0.35f,
        BaseDiameterMm=1.0f, BaseHeightMm=0.3f, StructureType="column",
    };
    public static readonly SupportPreset PyramidTipPreset = new() {
        Name="Pyramid Tip", TipDiameterMm=0.5f, TipShape="pyramid", ContactDepthMm=0.3f,
        NeckDiameterMm=0.3f, NeckLengthMm=0.5f, ShaftDiameterMm=0.8f,
        BaseDiameterMm=2.0f, BaseHeightMm=0.5f, StructureType="column",
    };
    public static readonly SupportPreset SkateTipPreset = new() {
        Name="Skate Tip", TipDiameterMm=0.4f, TipShape="skate", ContactDepthMm=0.2f,
        NeckDiameterMm=0.25f, NeckLengthMm=0.4f, ShaftDiameterMm=0.7f,
        BaseDiameterMm=1.8f, BaseHeightMm=0.5f, StructureType="column",
    };
    public static readonly SupportPreset ChiselTipPreset = new() {
        Name="Chisel Tip", TipDiameterMm=0.3f, TipShape="chisel", ContactDepthMm=0.15f,
        NeckDiameterMm=0.2f, NeckLengthMm=0.3f, ShaftDiameterMm=0.6f,
        BaseDiameterMm=1.5f, BaseHeightMm=0.4f, StructureType="column",
    };
    public static readonly SupportPreset MushroomTipPreset = new() {
        Name="Mushroom Tip", TipDiameterMm=0.6f, TipShape="mushroom", ContactDepthMm=0.15f,
        NeckDiameterMm=0.2f, NeckLengthMm=0.6f, NeckType="double",
        ShaftDiameterMm=0.8f, BaseDiameterMm=2.0f, BaseHeightMm=0.5f, StructureType="column",
    };
    public static readonly SupportPreset CrossTipPreset = new() {
        Name="Cross Tip", TipDiameterMm=0.6f, TipShape="cross", ContactDepthMm=0.3f,
        NeckDiameterMm=0.35f, NeckLengthMm=0.5f, ShaftDiameterMm=0.9f,
        BaseDiameterMm=2.2f, BaseHeightMm=0.5f, StructureType="column",
    };
    public static readonly SupportPreset RingTipPreset = new() {
        Name="Ring Tip", TipDiameterMm=0.5f, TipShape="ring", ContactDepthMm=0.1f,
        NeckDiameterMm=0.25f, NeckLengthMm=0.4f, ShaftDiameterMm=0.7f,
        BaseDiameterMm=1.8f, BaseHeightMm=0.5f, StructureType="column",
    };
    public static readonly SupportPreset NeedleTipPreset = new() {
        Name="Needle Tip", TipDiameterMm=0.1f, TipShape="needle", ContactDepthMm=0.5f,
        NeckDiameterMm=0.08f, NeckLengthMm=0.2f, ShaftDiameterMm=0.3f,
        BaseDiameterMm=1.0f, BaseHeightMm=0.3f, StructureType="column",
    };

    // --- Shaft type variants ---
    public static readonly SupportPreset TaperedColumnPreset = new() {
        Name="Tapered Column", TipDiameterMm=0.5f, TipShape="cone",
        NeckDiameterMm=0.3f, NeckLengthMm=0.5f, ShaftDiameterMm=1.2f, ShaftType="cone",
        BaseDiameterMm=2.5f, BaseHeightMm=0.6f, StructureType="tapered",
    };
    public static readonly SupportPreset HollowTubePreset = new() {
        Name="Hollow Tube", TipDiameterMm=0.5f, TipShape="cone",
        NeckDiameterMm=0.3f, NeckLengthMm=0.5f, ShaftDiameterMm=1.0f, ShaftType="hollow",
        BaseDiameterMm=2.0f, BaseHeightMm=0.5f, StructureType="column",
    };
    public static readonly SupportPreset SquareColumnPreset = new() {
        Name="Square Column", TipDiameterMm=0.5f, TipShape="pyramid",
        NeckDiameterMm=0.3f, NeckLengthMm=0.5f, ShaftDiameterMm=0.8f, ShaftType="square",
        BaseDiameterMm=2.0f, BaseHeightMm=0.5f, BaseType="pyramid", StructureType="column",
    };
    public static readonly SupportPreset XProfilePreset = new() {
        Name="X-Profile", TipDiameterMm=0.4f, TipShape="cross",
        NeckDiameterMm=0.25f, NeckLengthMm=0.4f, ShaftDiameterMm=0.8f, ShaftType="xprofile",
        BaseDiameterMm=1.8f, BaseHeightMm=0.5f, StructureType="column",
    };
    public static readonly SupportPreset IBeamPreset = new() {
        Name="I-Beam", TipDiameterMm=0.5f, TipShape="flat",
        NeckDiameterMm=0.3f, NeckLengthMm=0.5f, ShaftDiameterMm=1.0f, ShaftType="ibeam",
        BaseDiameterMm=2.5f, BaseHeightMm=0.6f, StructureType="column",
    };
    public static readonly SupportPreset LatticeColumnPreset = new() {
        Name="Lattice Column", TipDiameterMm=0.4f, TipShape="cone",
        NeckDiameterMm=0.25f, NeckLengthMm=0.4f, ShaftDiameterMm=1.2f, ShaftType="lattice",
        BaseDiameterMm=2.0f, BaseHeightMm=0.5f, StructureType="lattice",
    };
    public static readonly SupportPreset SpiralPreset = new() {
        Name="Spiral", TipDiameterMm=0.4f, TipShape="cone",
        NeckDiameterMm=0.25f, NeckLengthMm=0.4f, ShaftDiameterMm=0.6f, ShaftType="spiral",
        BaseDiameterMm=1.5f, BaseHeightMm=0.5f, StructureType="column",
    };
    public static readonly SupportPreset RibbedPreset = new() {
        Name="Ribbed", TipDiameterMm=0.5f, TipShape="cone",
        NeckDiameterMm=0.3f, NeckLengthMm=0.5f, ShaftDiameterMm=0.8f, ShaftType="ribbed",
        BaseDiameterMm=2.0f, BaseHeightMm=0.5f, StructureType="column",
    };
    public static readonly SupportPreset DiamondPreset = new() {
        Name="Diamond/Open", TipDiameterMm=0.4f, TipShape="cone",
        NeckDiameterMm=0.25f, NeckLengthMm=0.4f, ShaftDiameterMm=1.0f, ShaftType="diamond",
        BaseDiameterMm=2.0f, BaseHeightMm=0.5f, StructureType="column",
    };

    // --- Base type variants ---
    public static readonly SupportPreset ConeBasePreset = new() {
        Name="Cone Base", TipDiameterMm=0.5f, TipShape="cone",
        ShaftDiameterMm=0.8f, BaseDiameterMm=2.5f, BaseHeightMm=0.8f, BaseType="cone", StructureType="column",
    };
    public static readonly SupportPreset PyramidBasePreset = new() {
        Name="Pyramid Base", TipDiameterMm=0.5f, TipShape="cone",
        ShaftDiameterMm=0.8f, BaseDiameterMm=2.5f, BaseHeightMm=0.8f, BaseType="pyramid", StructureType="column",
    };
    public static readonly SupportPreset RaftBasePreset = new() {
        Name="Raft Base", TipDiameterMm=0.5f, TipShape="cone",
        ShaftDiameterMm=0.8f, BaseDiameterMm=4.0f, BaseHeightMm=1.0f, BaseType="raft", StructureType="column",
    };
    public static readonly SupportPreset MiniRaftBasePreset = new() {
        Name="Mini Raft Base", TipDiameterMm=0.5f, TipShape="cone",
        ShaftDiameterMm=0.8f, BaseDiameterMm=3.0f, BaseHeightMm=0.6f, BaseType="miniraft", StructureType="column",
    };
    public static readonly SupportPreset PinBasePreset = new() {
        Name="Pin Base", TipDiameterMm=0.3f, TipShape="sphere",
        ShaftDiameterMm=0.5f, BaseDiameterMm=0.6f, BaseHeightMm=0.2f, BaseType="pin", StructureType="column",
    };
    public static readonly SupportPreset SkirtedBasePreset = new() {
        Name="Skirted Base", TipDiameterMm=0.5f, TipShape="cone",
        ShaftDiameterMm=0.8f, BaseDiameterMm=2.5f, BaseHeightMm=0.6f, BaseType="skirted", StructureType="column",
    };
    public static readonly SupportPreset WebbedBasePreset = new() {
        Name="Webbed Base", TipDiameterMm=0.5f, TipShape="cone",
        ShaftDiameterMm=0.8f, BaseDiameterMm=2.5f, BaseHeightMm=0.5f, BaseType="webbed", StructureType="column",
    };
    public static readonly SupportPreset AnchorBasePreset = new() {
        Name="Anchor Base", TipDiameterMm=0.5f, TipShape="cone",
        ShaftDiameterMm=0.8f, BaseDiameterMm=3.5f, BaseHeightMm=1.0f, BaseType="anchor", StructureType="column",
    };

    // --- Structure type variants ---
    public static readonly SupportPreset WallBladePreset = new() {
        Name="Wall/Blade", TipDiameterMm=0.3f, TipShape="chisel",
        NeckDiameterMm=0.2f, NeckLengthMm=0.3f, ShaftDiameterMm=0.3f, ShaftType="cylinder",
        BaseDiameterMm=1.5f, BaseHeightMm=0.5f, StructureType="wall",
    };
    public static readonly SupportPreset SmallPillarPreset = new() {
        Name="Small Pillar", TipDiameterMm=0.2f, TipShape="point", ContactDepthMm=0.05f,
        NeckDiameterMm=0.1f, NeckLengthMm=0.15f, ShaftDiameterMm=0.25f,
        BaseDiameterMm=0.8f, BaseHeightMm=0.2f, BaseType="pin", StructureType="column",
    };
    public static readonly SupportPreset ScaffoldPreset = new() {
        Name="Scaffold", TipDiameterMm=0.4f, TipShape="cone",
        ShaftDiameterMm=0.5f, ShaftType="lattice",
        BaseDiameterMm=1.5f, BaseHeightMm=0.5f, BaseType="disc",
        BraceDiameterMm=0.3f, BraceIntervalMm=3.0f, BraceType="truss", StructureType="scaffold",
    };
    public static readonly SupportPreset TrussPreset = new() {
        Name="Truss", TipDiameterMm=0.5f, TipShape="cone",
        ShaftDiameterMm=0.6f, ShaftType="cylinder",
        BaseDiameterMm=2.0f, BaseHeightMm=0.5f,
        BraceDiameterMm=0.4f, BraceIntervalMm=3.0f, BraceType="truss", StructureType="truss",
    };
    public static readonly SupportPreset GussetPreset = new() {
        Name="Gusset", TipDiameterMm=0.4f, TipShape="flat",
        ShaftDiameterMm=0.6f, ShaftType="cylinder",
        BaseDiameterMm=1.5f, BaseHeightMm=0.4f, StructureType="gusset",
    };
    public static readonly SupportPreset CagePreset = new() {
        Name="Cage", TipDiameterMm=0.3f, TipShape="cone",
        ShaftDiameterMm=0.4f, ShaftType="lattice",
        BaseDiameterMm=1.5f, BaseHeightMm=0.5f,
        BraceDiameterMm=0.3f, BraceIntervalMm=2.0f, BraceType="truss", StructureType="cage",
    };
    public static readonly SupportPreset OrganicPreset = new() {
        Name="Organic", TipDiameterMm=0.3f, TipShape="sphere",
        NeckDiameterMm=0.2f, NeckLengthMm=0.4f, NeckType="graduated",
        ShaftDiameterMm=0.6f, ShaftType="spiral",
        BaseDiameterMm=1.8f, BaseHeightMm=0.5f, BaseType="webbed", StructureType="organic",
    };

    // --- Neck type variants ---
    public static readonly SupportPreset WaistNeckPreset = new() {
        Name="Waist Neck", TipDiameterMm=0.5f, TipShape="cone",
        NeckDiameterMm=0.2f, NeckLengthMm=0.6f, NeckType="waist",
        ShaftDiameterMm=0.8f, BaseDiameterMm=2.0f, StructureType="column",
    };
    public static readonly SupportPreset PerforatedNeckPreset = new() {
        Name="Perforated Neck", TipDiameterMm=0.5f, TipShape="cone",
        NeckDiameterMm=0.25f, NeckLengthMm=0.5f, NeckType="perforated",
        ShaftDiameterMm=0.8f, BaseDiameterMm=2.0f, StructureType="column",
    };
    public static readonly SupportPreset DoubleNeckPreset = new() {
        Name="Double Neck", TipDiameterMm=0.5f, TipShape="cone",
        NeckDiameterMm=0.2f, NeckLengthMm=0.8f, NeckType="double",
        ShaftDiameterMm=0.8f, BaseDiameterMm=2.0f, StructureType="column",
    };

    // ── ALL PRESETS DICTIONARY ───────────────────────────────────────────────

    public static readonly Dictionary<string, SupportPreset> AllPresets = new()
    {
        // Weight variants
        ["light"] = LightPreset, ["medium"] = MediumPreset, ["heavy"] = HeavyPreset,
        // Tip shapes
        ["point-tip"] = PointTipPreset, ["pyramid-tip"] = PyramidTipPreset, ["skate-tip"] = SkateTipPreset,
        ["chisel-tip"] = ChiselTipPreset, ["mushroom-tip"] = MushroomTipPreset, ["cross-tip"] = CrossTipPreset,
        ["ring-tip"] = RingTipPreset, ["needle-tip"] = NeedleTipPreset,
        // Shaft types
        ["tapered"] = TaperedColumnPreset, ["hollow-tube"] = HollowTubePreset,
        ["square"] = SquareColumnPreset, ["x-profile"] = XProfilePreset, ["i-beam"] = IBeamPreset,
        ["lattice-column"] = LatticeColumnPreset, ["spiral"] = SpiralPreset,
        ["ribbed"] = RibbedPreset, ["diamond-open"] = DiamondPreset,
        // Base types
        ["cone-base"] = ConeBasePreset, ["pyramid-base"] = PyramidBasePreset,
        ["raft-base"] = RaftBasePreset, ["miniraft-base"] = MiniRaftBasePreset,
        ["pin-base"] = PinBasePreset, ["skirted-base"] = SkirtedBasePreset,
        ["webbed-base"] = WebbedBasePreset, ["anchor-base"] = AnchorBasePreset,
        // Structure types
        ["tree"] = MediumPreset, ["crossbraced"] = MediumPreset, // use medium but structure differs
        ["wall-blade"] = WallBladePreset, ["small-pillar"] = SmallPillarPreset,
        ["scaffold"] = ScaffoldPreset, ["truss"] = TrussPreset,
        ["gusset"] = GussetPreset, ["cage"] = CagePreset, ["organic"] = OrganicPreset,
        // Neck types
        ["waist-neck"] = WaistNeckPreset, ["perforated-neck"] = PerforatedNeckPreset,
        ["double-neck"] = DoubleNeckPreset,
    };

    public static SupportPreset GetPreset(string type) =>
        AllPresets.TryGetValue(type, out var p) ? p : MediumPreset;

    // ── Support structure data model ────────────────────────────────────────

    /// <summary>One complete support structure with all anatomical components.</summary>
    public sealed record AdvancedSupport
    {
        public required string Id { get; init; }
        public required string Type { get; init; }    // light | medium | heavy | tree | crossbraced
        // Contact point on model
        public required float ContactX { get; init; }
        public required float ContactY { get; init; }
        public required float ContactZ { get; init; }
        public required float NormalX { get; init; }
        public required float NormalY { get; init; }
        public required float NormalZ { get; init; }
        // Anatomy dimensions
        public required SupportPreset Preset { get; init; }
        // Base point (on build plate or on another surface)
        public required float BaseX { get; init; }
        public required float BaseY { get; init; }
        public required float BaseZ { get; init; }
        // Tree: branch merge point (only for tree supports)
        public float? MergeZ { get; init; }
        public float? MergeX { get; init; }
        public float? MergeY { get; init; }
        public string? ParentTrunkId { get; init; } // which trunk this branch connects to
        // Segments for preview (detailed geometry points)
        public List<SupportSegment> Segments { get; init; } = [];
    }

    /// <summary>One geometric segment of a support for rendering.</summary>
    public sealed record SupportSegment
    {
        public required string Part { get; init; }   // tip | neck | upperTaper | shaft | lowerTaper | base | branch | brace
        public required float X1 { get; init; }
        public required float Y1 { get; init; }
        public required float Z1 { get; init; }
        public required float R1 { get; init; }      // radius at start
        public required float X2 { get; init; }
        public required float Y2 { get; init; }
        public required float Z2 { get; init; }
        public required float R2 { get; init; }      // radius at end
    }

    /// <summary>Cross-brace between two supports.</summary>
    public sealed record CrossBrace
    {
        public required string SupportA { get; init; }
        public required string SupportB { get; init; }
        public required float X1 { get; init; }
        public required float Y1 { get; init; }
        public required float Z1 { get; init; }
        public required float X2 { get; init; }
        public required float Y2 { get; init; }
        public required float Z2 { get; init; }
        public required float Diameter { get; init; }
    }

    public sealed record AdvancedSupportResult
    {
        public List<AdvancedSupport> Supports { get; init; } = [];
        public List<CrossBrace> CrossBraces { get; init; } = [];
        public int OverhangFaceCount { get; init; }
        public long ElapsedMs { get; init; }
    }

    // ── Generation config ───────────────────────────────────────────────────

    public sealed record AdvancedSupportConfig
    {
        public PrinterOrientation Orientation { get; init; } = PrinterOrientation.BottomUp;
        public string SupportType { get; init; } = "medium"; // light | medium | heavy | tree | crossbraced
        public string Placement { get; init; } = "buildplate";
        public double OverhangAngleDeg { get; init; } = 45;
        public double DensityFactor { get; init; } = 0.5;
        public bool CrossBracingEnabled { get; init; } = true;
        public double CrossBraceMaxDistMm { get; init; } = 8.0;
    }

    // ── Generation ──────────────────────────────────────────────────────────

    public static AdvancedSupportResult Generate(StlMesh mesh, AdvancedSupportConfig config)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var preset = GetPreset(config.SupportType);

        // Bottom-Up: thinner tips for surface quality
        if (config.Orientation == PrinterOrientation.BottomUp)
            preset = preset with { TipDiameterMm = preset.TipDiameterMm * 0.8f, NeckDiameterMm = preset.NeckDiameterMm * 0.8f };

        // Center mesh: place on bed (Z=0) and center XY
        float meshW = mesh.Max.X - mesh.Min.X;
        float meshD = mesh.Max.Y - mesh.Min.Y;
        float offX = -(mesh.Min.X + meshW / 2);
        float offY = -(mesh.Min.Y + meshD / 2);
        float offZ = -mesh.Min.Z;
        mesh = mesh.Transform(new Vector3(offX, offY, offZ), 1.0f);

        // Detect overhangs
        var overhangCos = MathF.Cos(MathF.PI / 180f * (float)config.OverhangAngleDeg);
        var gravityDir = new Vector3(0, 0, -1);
        var overhangPoints = new List<(Vector3 center, Vector3 normal, float area)>();

        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var v0 = mesh.Vertices[t * 3];
            var v1 = mesh.Vertices[t * 3 + 1];
            var v2 = mesh.Vertices[t * 3 + 2];
            var cross = Vector3.Cross(v1 - v0, v2 - v0);
            var area = cross.Length() * 0.5f;
            var normal = Vector3.Normalize(cross);
            if (float.IsNaN(normal.X) || area < 1e-8f) continue;

            float dot = Vector3.Dot(normal, gravityDir);
            if (dot > overhangCos)
            {
                var center = (v0 + v1 + v2) / 3f;
                if (center.Z < 0.2f) continue; // skip faces already on bed
                overhangPoints.Add((center, normal, area));
            }
        }

        // Sort by Z (highest first for priority) then by area (largest first)
        overhangPoints.Sort((a, b) =>
        {
            int zc = b.center.Z.CompareTo(a.center.Z);
            return zc != 0 ? zc : b.area.CompareTo(a.area);
        });

        // Place supports with density-based spacing
        float spacing = (float)(4.0 / (config.DensityFactor + 0.1));
        var supports = new List<AdvancedSupport>();
        var placed = new List<Vector3>();
        int idCounter = 0;

        foreach (var (center, normal, area) in overhangPoints)
        {
            bool tooClose = placed.Any(p => Vector2.Distance(
                new Vector2(center.X, center.Y), new Vector2(p.X, p.Y)) < spacing);
            if (tooClose) continue;

            var support = BuildSupport($"sup-{++idCounter}", config.SupportType, preset, center, normal);
            supports.Add(support);
            placed.Add(center);
        }

        // Tree merging (if tree type)
        if (config.SupportType == "tree" && supports.Count > 1)
            supports = MergeIntoTrees(supports, preset);

        // Cross-bracing
        var braces = new List<CrossBrace>();
        if (config.CrossBracingEnabled || config.SupportType == "crossbraced")
            braces = GenerateCrossBraces(supports, preset, (float)config.CrossBraceMaxDistMm);

        sw.Stop();
        return new AdvancedSupportResult
        {
            Supports = supports,
            CrossBraces = braces,
            OverhangFaceCount = overhangPoints.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
        };
    }

    // ── Build one support with full anatomy ──────────────────────────────────

    private static AdvancedSupport BuildSupport(string id, string type, SupportPreset preset,
        Vector3 contact, Vector3 normal)
    {
        var segments = new List<SupportSegment>();
        float z = contact.Z;
        float x = contact.X, y = contact.Y;

        // 1. Tip (sphere/cone at contact)
        float tipBot = z - preset.TipDiameterMm / 2;
        segments.Add(new SupportSegment
        {
            Part = "tip", X1 = x, Y1 = y, Z1 = z, R1 = 0,
            X2 = x, Y2 = y, Z2 = tipBot, R2 = preset.TipDiameterMm / 2,
        });

        // 2. Neck (thin breakpoint)
        float neckBot = tipBot - preset.NeckLengthMm;
        segments.Add(new SupportSegment
        {
            Part = "neck", X1 = x, Y1 = y, Z1 = tipBot, R1 = preset.NeckDiameterMm / 2,
            X2 = x, Y2 = y, Z2 = neckBot, R2 = preset.NeckDiameterMm / 2,
        });

        // 3. Upper taper (neck → shaft)
        float upperBot = neckBot - preset.UpperTaperLengthMm;
        segments.Add(new SupportSegment
        {
            Part = "upperTaper", X1 = x, Y1 = y, Z1 = neckBot, R1 = preset.NeckDiameterMm / 2,
            X2 = x, Y2 = y, Z2 = upperBot, R2 = preset.ShaftDiameterMm / 2,
        });

        // 4. Shaft (main body — from upper taper to lower taper zone)
        float lowerTaperStart = preset.BaseHeightMm + preset.LowerTaperLengthMm;
        float shaftBot = Math.Max(lowerTaperStart, 0.5f);
        if (upperBot > shaftBot + 0.1f)
        {
            segments.Add(new SupportSegment
            {
                Part = "shaft", X1 = x, Y1 = y, Z1 = upperBot, R1 = preset.ShaftDiameterMm / 2,
                X2 = x, Y2 = y, Z2 = shaftBot, R2 = preset.ShaftDiameterMm / 2,
            });
        }

        // 5. Lower taper (shaft → base)
        float lowerBot = preset.BaseHeightMm;
        segments.Add(new SupportSegment
        {
            Part = "lowerTaper", X1 = x, Y1 = y, Z1 = shaftBot, R1 = preset.ShaftDiameterMm / 2,
            X2 = x, Y2 = y, Z2 = lowerBot, R2 = preset.BaseDiameterMm / 2,
        });

        // 6. Base/foot
        segments.Add(new SupportSegment
        {
            Part = "base", X1 = x, Y1 = y, Z1 = lowerBot, R1 = preset.BaseDiameterMm / 2,
            X2 = x, Y2 = y, Z2 = 0, R2 = preset.BaseDiameterMm / 2,
        });

        return new AdvancedSupport
        {
            Id = id, Type = type, Preset = preset,
            ContactX = x, ContactY = y, ContactZ = z,
            NormalX = normal.X, NormalY = normal.Y, NormalZ = normal.Z,
            BaseX = x, BaseY = y, BaseZ = 0,
            Segments = segments,
        };
    }

    // ── Tree merging ────────────────────────────────────────────────────────

    private static List<AdvancedSupport> MergeIntoTrees(List<AdvancedSupport> supports, SupportPreset preset)
    {
        float mergeRadius = 6.0f; // mm — supports within this XY distance can share a trunk
        var result = new List<AdvancedSupport>();
        var used = new bool[supports.Count];

        for (int i = 0; i < supports.Count; i++)
        {
            if (used[i]) continue;

            // Find nearby supports that can merge with this one
            var group = new List<int> { i };
            for (int j = i + 1; j < supports.Count; j++)
            {
                if (used[j]) continue;
                float dist = Vector2.Distance(
                    new Vector2(supports[i].ContactX, supports[i].ContactY),
                    new Vector2(supports[j].ContactX, supports[j].ContactY));
                if (dist < mergeRadius)
                    group.Add(j);
            }

            if (group.Count == 1)
            {
                // No merge partner — keep as standalone
                result.Add(supports[i]);
                used[i] = true;
                continue;
            }

            // Merge point: centroid at the lower Z of the group
            float minZ = group.Min(g => supports[g].ContactZ);
            float mergeZ = minZ * 0.4f; // merge at 40% of the lowest contact
            float cx = group.Average(g => supports[g].ContactX);
            float cy = group.Average(g => supports[g].ContactY);

            // Create trunk from merge point to base
            string trunkId = $"trunk-{i}";

            foreach (int gi in group)
            {
                used[gi] = true;
                var s = supports[gi];
                var segments = new List<SupportSegment>();

                // Tip + neck at contact point (same as normal)
                float z = s.ContactZ;
                segments.Add(new SupportSegment
                {
                    Part = "tip", X1 = s.ContactX, Y1 = s.ContactY, Z1 = z, R1 = 0,
                    X2 = s.ContactX, Y2 = s.ContactY, Z2 = z - preset.TipDiameterMm / 2, R2 = preset.TipDiameterMm / 2,
                });

                // Branch from contact point down to merge point
                segments.Add(new SupportSegment
                {
                    Part = "branch",
                    X1 = s.ContactX, Y1 = s.ContactY, Z1 = z - preset.TipDiameterMm / 2, R1 = preset.NeckDiameterMm / 2,
                    X2 = cx, Y2 = cy, Z2 = mergeZ, R2 = preset.ShaftDiameterMm / 2,
                });

                result.Add(s with
                {
                    Type = "tree", Segments = segments,
                    MergeZ = mergeZ, MergeX = cx, MergeY = cy, ParentTrunkId = trunkId,
                });
            }

            // Add trunk support (merge point to base)
            var trunkSegments = new List<SupportSegment>
            {
                new() { Part = "shaft", X1 = cx, Y1 = cy, Z1 = mergeZ, R1 = preset.ShaftDiameterMm * 0.7f,
                         X2 = cx, Y2 = cy, Z2 = preset.BaseHeightMm + preset.LowerTaperLengthMm, R2 = preset.ShaftDiameterMm * 0.7f },
                new() { Part = "lowerTaper", X1 = cx, Y1 = cy, Z1 = preset.BaseHeightMm + preset.LowerTaperLengthMm, R1 = preset.ShaftDiameterMm * 0.7f,
                         X2 = cx, Y2 = cy, Z2 = preset.BaseHeightMm, R2 = preset.BaseDiameterMm / 2 },
                new() { Part = "base", X1 = cx, Y1 = cy, Z1 = preset.BaseHeightMm, R1 = preset.BaseDiameterMm / 2,
                         X2 = cx, Y2 = cy, Z2 = 0, R2 = preset.BaseDiameterMm / 2 },
            };

            result.Add(new AdvancedSupport
            {
                Id = trunkId, Type = "tree-trunk", Preset = preset,
                ContactX = cx, ContactY = cy, ContactZ = mergeZ,
                NormalX = 0, NormalY = 0, NormalZ = -1,
                BaseX = cx, BaseY = cy, BaseZ = 0,
                Segments = trunkSegments,
            });
        }

        return result;
    }

    // ── Cross-bracing ───────────────────────────────────────────────────────

    private static List<CrossBrace> GenerateCrossBraces(
        List<AdvancedSupport> supports, SupportPreset preset, float maxDist)
    {
        var braces = new List<CrossBrace>();
        float braceInterval = preset.BraceIntervalMm;
        const int MAX_BRACES_PER_SUPPORT = 3; // limit to nearest 3 neighbors

        for (int i = 0; i < supports.Count; i++)
        {
            var si = supports[i];
            // Find nearest neighbors only
            var neighbors = new List<(int idx, float dist)>();
            for (int j = 0; j < supports.Count; j++)
            {
                if (j == i) continue;
                var sj = supports[j];
                float dist = Vector2.Distance(
                    new Vector2(si.ContactX, si.ContactY),
                    new Vector2(sj.ContactX, sj.ContactY));
                if (dist <= maxDist) neighbors.Add((j, dist));
            }
            neighbors.Sort((a, b) => a.dist.CompareTo(b.dist));

            // Only brace to nearest N neighbors, and only if i < j to avoid duplicates
            foreach (var (j, dist) in neighbors.Take(MAX_BRACES_PER_SUPPORT))
            {
                if (j <= i) continue; // avoid duplicate pairs
                var sj = supports[j];
                float minZ = Math.Max(si.BaseZ, sj.BaseZ) + 1;
                float maxZ = Math.Min(si.ContactZ, sj.ContactZ) - 1;
                if (maxZ <= minZ) continue;
                bool alternate = false;

                // Limit braces per pair based on height
                int maxBracesPerPair = Math.Min(5, (int)((maxZ - minZ) / braceInterval));
                int braceCount = 0;

                for (float z = minZ + braceInterval; z < maxZ && braceCount < maxBracesPerPair; z += braceInterval)
                {
                    float z2 = z + braceInterval * 0.3f;
                    if (z2 > maxZ) z2 = maxZ;

                    braces.Add(new CrossBrace
                    {
                        SupportA = si.Id, SupportB = sj.Id,
                        X1 = alternate ? si.ContactX : sj.ContactX,
                        Y1 = alternate ? si.ContactY : sj.ContactY,
                        Z1 = z,
                        X2 = alternate ? sj.ContactX : si.ContactX,
                        Y2 = alternate ? sj.ContactY : si.ContactY,
                        Z2 = z2,
                        Diameter = preset.BraceDiameterMm,
                    });
                    alternate = !alternate;
                    braceCount++;
                }
            }
        }

        return braces;
    }
}
