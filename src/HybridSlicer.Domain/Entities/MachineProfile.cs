using HybridSlicer.Domain.Enums;
using HybridSlicer.Domain.Exceptions;
using HybridSlicer.Domain.ValueObjects;

namespace HybridSlicer.Domain.Entities;

public class MachineProfile
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public MachineType Type { get; private set; }

    // Machine travel limits (mm) — total axis travel; defaults to bed size if unset
    public double TravelXMm { get; private set; }
    public double TravelYMm { get; private set; }
    public double TravelZMm { get; private set; }

    // Origin mode: where machine (0,0) sits relative to the bed
    public OriginMode OriginMode { get; private set; } = OriginMode.BedCenter;

    // Build volume (mm) — printable area on the bed
    public double BedWidthMm { get; private set; }
    public double BedDepthMm { get; private set; }
    public double BedHeightMm { get; private set; }

    // Bed position inside machine travel (mm) — front-left corner of bed in machine coords.
    public double BedPositionXMm { get; private set; }
    public double BedPositionYMm { get; private set; }

    // Machine origin (0,0) position inside the travel frame (mm).
    // Default = (0, 0) = machine zero at front-left of travel.
    public double OriginXMm { get; private set; }
    public double OriginYMm { get; private set; }

    // Multi-bed support: number of beds and per-bed definitions.
    // Bed 1 is always backed by the legacy single-bed fields for backward compat.
    // Additional beds are stored in BedsJson.
    public int BedCount { get; private set; } = 1;
    public string BedsJson { get; private set; } = "[]";
    public IReadOnlyList<ValueObjects.BedDefinition> Beds =>
        System.Text.Json.JsonSerializer.Deserialize<List<ValueObjects.BedDefinition>>(BedsJson ?? "[]",
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

    // FDM properties
    public int ExtruderCount { get; private set; }

    // Nozzle X/Y-axis offsets between adjacent nozzles (mm).
    // Length = ExtruderCount - 1. E.g. for 3 extruders: [0.0, 0.0] and [30.0, 30.0]
    // Stored as JSON for EF Core compatibility.
    public string NozzleXOffsetsJson { get; private set; } = "[]";
    public IReadOnlyList<double> NozzleXOffsets =>
        System.Text.Json.JsonSerializer.Deserialize<List<double>>(NozzleXOffsetsJson ?? "[]") ?? [];

    public string NozzleYOffsetsJson { get; private set; } = "[]";
    public IReadOnlyList<double> NozzleYOffsets =>
        System.Text.Json.JsonSerializer.Deserialize<List<double>>(NozzleYOffsetsJson ?? "[]") ?? [];

    // Distance from the furthest nozzle to bed edges (mm)
    // Left/Right = along the Y axis, Front/Back = along the X axis
    public double LeftBedEdgeOffsetMm { get; private set; }
    public double RightBedEdgeOffsetMm { get; private set; }
    public double FrontBedEdgeOffsetMm { get; private set; }
    public double BackBedEdgeOffsetMm { get; private set; }

    // Extruder-to-duty assignments
    private readonly List<ExtruderAssignment> _extruderAssignments = [];
    public IReadOnlyList<ExtruderAssignment> ExtruderAssignments => _extruderAssignments.AsReadOnly();

    // Network communication
    public string? IpAddress { get; private set; }
    public int Port { get; private set; }

    // CNC coordinate offset relative to the printer origin
    public MachineOffset CncOffset { get; private set; } = MachineOffset.Zero;

    // Per-tool length and radius offsets (tool index → offset)
    private readonly List<ToolOffset> _toolOffsets = [];
    public IReadOnlyList<ToolOffset> ToolOffsets => _toolOffsets.AsReadOnly();

    // Rapid travel clearance height above part for CNC moves
    public double SafeClearanceHeightMm { get; private set; } = 5.0;

    // Axis naming: allows remapping G-code axis letters per component.
    // Default is "XYZ" for everything. E.g. CNC could be "UVW".
    // Stored as 3-char strings. Only the letter changes in G-code output.
    public string ExtruderAxes { get; private set; } = "XYZ";
    public string CncAxes { get; private set; } = "XYZ";

    // Motion assignment: which physical component moves on each axis.
    // Used in hybrid preview simulation only. Does not affect G-code.
    // Enabled flag — when false, preview uses default (everything moves on nozzle/tool).
    public bool MotionAssignmentEnabled { get; private set; }
    // JSON: {"extruder":"YZ","bed1":"X","cnc":"YZ","bed1Cnc":"X"} etc.
    // Each value is a string of axis letters that component moves on.
    public string MotionAssignmentJson { get; private set; } = "{}";

    public void SetExtruderAxes(string axes)
    {
        ExtruderAxes = (axes?.Length >= 3 ? axes[..3] : axes?.PadRight(3, 'Z') ?? "XYZ").ToUpperInvariant();
        Touch();
    }

    public void SetCncAxes(string axes)
    {
        CncAxes = (axes?.Length >= 3 ? axes[..3] : axes?.PadRight(3, 'Z') ?? "XYZ").ToUpperInvariant();
        Touch();
    }

    public void SetMotionAssignment(bool enabled, string json)
    {
        MotionAssignmentEnabled = enabled;
        MotionAssignmentJson = json ?? "{}";
        Touch();
    }

    // ── Resin printer fields (MSLA / DLP) ─────────────────────────────────
    public PrinterOrientation Orientation { get; private set; } = PrinterOrientation.BottomUp;

    // Native image resolution (pixels)
    public int ResolutionX { get; private set; }
    public int ResolutionY { get; private set; }

    // Pixel pitch in microns (MSLA: from LCD spec; DLP: calculated from projection width / resolutionX)
    // If 0, auto-calculated from build width / resolutionX
    public double PixelPitchUm { get; private set; }

    // Mirror / flip for printer output
    public bool MirrorX { get; private set; }
    public bool MirrorY { get; private set; }

    // Build-area offsets (mm)
    public double BuildOffsetXMm { get; private set; }
    public double BuildOffsetYMm { get; private set; }

    // Default exposure settings
    public double DefaultLayerHeightMm { get; private set; } = 0.05;
    public int DefaultBottomLayerCount { get; private set; } = 5;
    public double DefaultNormalExposureMs { get; private set; } = 2500;
    public double DefaultBottomExposureMs { get; private set; } = 30000;
    public double LightOffDelayMs { get; private set; }

    // Lift / retract / peel settings
    public double LiftDistanceMm { get; private set; } = 5.0;
    public double LiftSpeedMmPerMin { get; private set; } = 60;
    public double RetractDistanceMm { get; private set; } = 5.0;
    public double RetractSpeedMmPerMin { get; private set; } = 150;
    public double BottomLiftDistanceMm { get; private set; } = 8.0;
    public double BottomLiftSpeedMmPerMin { get; private set; } = 45;
    public double RestTimeAfterLiftMs { get; private set; }
    public double RestTimeAfterRetractMs { get; private set; }

    // Anti-aliasing / grayscale
    public AntiAliasingLevel AntiAliasing { get; private set; } = AntiAliasingLevel.None;

    // Target export format family (e.g. "ctb", "pwmx", "sl1s", "image-sequence")
    public string ExportFormat { get; private set; } = "";

    // ── Recoater configuration (Top-Down printers) ──────────────────────
    public bool HasRecoater { get; private set; }
    public double RecoaterSpeedMmPerS { get; private set; }        // blade/roller speed
    public double RecoaterClearanceMm { get; private set; } = 2.0; // clearance above part
    public string RecoaterType { get; private set; } = "blade";    // blade | roller
    public string RecoaterDirection { get; private set; } = "X";   // X | Y — sweep direction

    public bool IsResinPrinter => Type is MachineType.MSLA or MachineType.DLP;

    /// <summary>Calculated pixel pitch in mm. Falls back to build width / resolutionX.</summary>
    public double EffectivePixelPitchMm =>
        PixelPitchUm > 0 ? PixelPitchUm / 1000.0
        : ResolutionX > 0 ? BedWidthMm / ResolutionX
        : 0.05;

    public void SetResinSettings(
        PrinterOrientation orientation,
        int resolutionX, int resolutionY,
        double pixelPitchUm,
        bool mirrorX, bool mirrorY,
        double buildOffsetXMm, double buildOffsetYMm,
        double defaultLayerHeightMm, int defaultBottomLayerCount,
        double defaultNormalExposureMs, double defaultBottomExposureMs,
        double lightOffDelayMs,
        double liftDistanceMm, double liftSpeedMmPerMin,
        double retractDistanceMm, double retractSpeedMmPerMin,
        double bottomLiftDistanceMm, double bottomLiftSpeedMmPerMin,
        double restTimeAfterLiftMs, double restTimeAfterRetractMs,
        AntiAliasingLevel antiAliasing,
        string exportFormat)
    {
        Orientation = orientation;
        ResolutionX = resolutionX > 0 ? resolutionX : ResolutionX;
        ResolutionY = resolutionY > 0 ? resolutionY : ResolutionY;
        PixelPitchUm = pixelPitchUm;
        MirrorX = mirrorX;
        MirrorY = mirrorY;
        BuildOffsetXMm = buildOffsetXMm;
        BuildOffsetYMm = buildOffsetYMm;
        DefaultLayerHeightMm = defaultLayerHeightMm > 0 ? defaultLayerHeightMm : DefaultLayerHeightMm;
        DefaultBottomLayerCount = defaultBottomLayerCount > 0 ? defaultBottomLayerCount : DefaultBottomLayerCount;
        DefaultNormalExposureMs = defaultNormalExposureMs > 0 ? defaultNormalExposureMs : DefaultNormalExposureMs;
        DefaultBottomExposureMs = defaultBottomExposureMs > 0 ? defaultBottomExposureMs : DefaultBottomExposureMs;
        LightOffDelayMs = lightOffDelayMs;
        LiftDistanceMm = liftDistanceMm > 0 ? liftDistanceMm : LiftDistanceMm;
        LiftSpeedMmPerMin = liftSpeedMmPerMin > 0 ? liftSpeedMmPerMin : LiftSpeedMmPerMin;
        RetractDistanceMm = retractDistanceMm > 0 ? retractDistanceMm : RetractDistanceMm;
        RetractSpeedMmPerMin = retractSpeedMmPerMin > 0 ? retractSpeedMmPerMin : RetractSpeedMmPerMin;
        BottomLiftDistanceMm = bottomLiftDistanceMm > 0 ? bottomLiftDistanceMm : BottomLiftDistanceMm;
        BottomLiftSpeedMmPerMin = bottomLiftSpeedMmPerMin > 0 ? bottomLiftSpeedMmPerMin : BottomLiftSpeedMmPerMin;
        RestTimeAfterLiftMs = restTimeAfterLiftMs;
        RestTimeAfterRetractMs = restTimeAfterRetractMs;
        AntiAliasing = antiAliasing;
        ExportFormat = exportFormat?.Trim() ?? "";
        Touch();
    }

    // Versioning / audit
    public string Version { get; private set; } = "1.0";
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    // EF Core constructor
    private MachineProfile() { }

    public static MachineProfile Create(
        string name,
        MachineType type,
        double bedWidth,
        double bedDepth,
        double bedHeight,
        int extruderCount = 1)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("INVALID_NAME", "Machine profile name must not be empty.");
        if (bedWidth <= 0 || bedDepth <= 0 || bedHeight <= 0)
            throw new DomainException("INVALID_DIMENSIONS", "Bed dimensions must be positive.");

        return new MachineProfile
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Type = type,
            TravelXMm = bedWidth,
            TravelYMm = bedDepth,
            TravelZMm = bedHeight,
            BedPositionXMm = 0, // centred when travel = bed
            BedPositionYMm = 0,
            BedWidthMm = bedWidth,
            BedDepthMm = bedDepth,
            BedHeightMm = bedHeight,
            ExtruderCount = extruderCount,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void SetNetworkEndpoint(string ipAddress, int port)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new DomainException("INVALID_IP", "IP address must not be empty.");
        if (port is < 1 or > 65535)
            throw new DomainException("INVALID_PORT", $"Port {port} is out of range.");

        IpAddress = ipAddress.Trim();
        Port = port;
        Touch();
    }

    public void ClearNetworkEndpoint()
    {
        IpAddress = null;
        Port = 0;
        Touch();
    }

    public void UpdateBedDimensions(double width, double depth, double height)
    {
        if (width <= 0 || depth <= 0 || height <= 0)
            throw new DomainException("INVALID_DIMENSIONS", "Bed dimensions must be positive.");
        // Keep travel in sync if travel == old bed (i.e. user never set travel independently)
        if (TravelXMm == BedWidthMm) TravelXMm = width;
        if (TravelYMm == BedDepthMm) TravelYMm = depth;
        if (TravelZMm == BedHeightMm) TravelZMm = height;
        BedWidthMm = width;
        BedDepthMm = depth;
        BedHeightMm = height;
        Touch();
    }

    public void SetExtruderCount(int count)
    {
        if (count < 1)
            throw new DomainException("INVALID_EXTRUDER_COUNT", "Extruder count must be at least 1.");
        ExtruderCount = count;
        // Trim offsets/assignments if extruder count decreased
        var yOffsets = NozzleYOffsets.Take(count - 1).ToList();
        NozzleYOffsetsJson = System.Text.Json.JsonSerializer.Serialize(yOffsets);
        var xOffsets = NozzleXOffsets.Take(count - 1).ToList();
        NozzleXOffsetsJson = System.Text.Json.JsonSerializer.Serialize(xOffsets);
        _extruderAssignments.RemoveAll(a => a.ExtruderIndex >= count);
        Touch();
    }

    public void SetNozzleXOffsets(IReadOnlyList<double> offsets)
    {
        var list = offsets?.Take(ExtruderCount - 1).ToList() ?? [];
        NozzleXOffsetsJson = System.Text.Json.JsonSerializer.Serialize(list);
        Touch();
    }

    public void SetNozzleYOffsets(IReadOnlyList<double> offsets)
    {
        var list = offsets?.Take(ExtruderCount - 1).ToList() ?? [];
        NozzleYOffsetsJson = System.Text.Json.JsonSerializer.Serialize(list);
        Touch();
    }

    public void SetBedEdgeOffsets(double left, double right, double front = 0, double back = 0)
    {
        LeftBedEdgeOffsetMm = left;
        RightBedEdgeOffsetMm = right;
        FrontBedEdgeOffsetMm = front;
        BackBedEdgeOffsetMm = back;
        Touch();
    }

    public void SetExtruderAssignments(IReadOnlyList<ExtruderAssignment> assignments)
    {
        _extruderAssignments.Clear();
        if (assignments is not null)
        {
            foreach (var a in assignments.Where(a => a.ExtruderIndex < ExtruderCount))
                _extruderAssignments.Add(a);
        }
        Touch();
    }

    public void UpdateCncOffset(MachineOffset offset)
    {
        CncOffset = offset ?? throw new ArgumentNullException(nameof(offset));
        Touch();
    }

    public void SetSafeClearanceHeight(double heightMm)
    {
        if (heightMm <= 0)
            throw new DomainException("INVALID_CLEARANCE", "Safe clearance height must be positive.");
        SafeClearanceHeightMm = heightMm;
        Touch();
    }

    public void UpsertToolOffset(ToolOffset toolOffset)
    {
        ArgumentNullException.ThrowIfNull(toolOffset);
        var existing = _toolOffsets.FindIndex(t => t.ToolIndex == toolOffset.ToolIndex);
        if (existing >= 0)
            _toolOffsets[existing] = toolOffset;
        else
            _toolOffsets.Add(toolOffset);
        Touch();
    }

    public void SetTravel(double x, double y, double z)
    {
        if (x <= 0 || y <= 0 || z <= 0)
            throw new DomainException("INVALID_TRAVEL", "Travel dimensions must be positive.");
        TravelXMm = x; TravelYMm = y; TravelZMm = z;
        Touch();
    }

    public void SetOriginMode(OriginMode mode)
    {
        OriginMode = mode;
        Touch();
    }

    public void SetBedPosition(double x, double y)
    {
        BedPositionXMm = x;
        BedPositionYMm = y;
        Touch();
    }

    public void SetOrigin(double x, double y)
    {
        OriginXMm = x;
        OriginYMm = y;
        Touch();
    }

    public void SetBedCount(int count)
    {
        if (count < 1)
            throw new DomainException("INVALID_BED_COUNT", "Bed count must be at least 1.");
        BedCount = count;
        // Rebuild beds list: keep existing beds, add new ones with defaults, trim excess
        var beds = Beds.ToList();
        // Always sync bed 1 from the legacy fields
        var bed1 = new ValueObjects.BedDefinition(0, BedWidthMm, BedDepthMm, BedHeightMm, BedPositionXMm, BedPositionYMm);
        if (beds.Count == 0) beds.Add(bed1); else beds[0] = bed1;
        // Add new beds with default size/position
        while (beds.Count < count)
        {
            var i = beds.Count;
            beds.Add(new ValueObjects.BedDefinition(i, BedWidthMm, BedDepthMm, BedHeightMm,
                BedPositionXMm + (BedWidthMm + 20) * i, BedPositionYMm));
        }
        // Trim excess
        while (beds.Count > count) beds.RemoveAt(beds.Count - 1);
        BedsJson = System.Text.Json.JsonSerializer.Serialize(beds, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Touch();
    }

    public void SetBeds(IReadOnlyList<ValueObjects.BedDefinition> beds)
    {
        if (beds is null || beds.Count == 0)
            throw new DomainException("INVALID_BEDS", "At least one bed is required.");
        BedCount = beds.Count;
        // Sync legacy single-bed fields from bed 1
        var bed1 = beds[0];
        BedWidthMm = bed1.WidthMm;
        BedDepthMm = bed1.DepthMm;
        BedHeightMm = bed1.HeightMm;
        BedPositionXMm = bed1.PositionXMm;
        BedPositionYMm = bed1.PositionYMm;
        BedsJson = System.Text.Json.JsonSerializer.Serialize(beds.Select((b, i) =>
            new ValueObjects.BedDefinition(i, b.WidthMm, b.DepthMm, b.HeightMm, b.PositionXMm, b.PositionYMm)).ToList(),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Touch();
    }

    /// <summary>
    /// Computes the unified machine mapping from all stored fields.
    /// This is the single source of truth for coordinate interpretation.
    /// </summary>
    public MachineMapping GetMapping()
    {
        // E0 position relative to bed front-left corner
        var e0X = FrontBedEdgeOffsetMm;
        var e0Y = LeftBedEdgeOffsetMm;

        // Compute absolute extruder positions relative to bed front-left corner
        var positions = new List<(double X, double Y)> { (e0X, e0Y) };
        var xList = NozzleXOffsets;
        var yList = NozzleYOffsets;
        double cumX = e0X, cumY = e0Y;
        for (var i = 0; i < ExtruderCount - 1; i++)
        {
            cumX += i < xList.Count ? xList[i] : 0;
            cumY += i < yList.Count ? yList[i] : 0;
            positions.Add((cumX, cumY));
        }

        // Bed origin in machine coords depends on origin mode
        double bedOriginX, bedOriginY, bedCenterX, bedCenterY;
        if (OriginMode == OriginMode.BedCenter)
        {
            bedOriginX = -BedWidthMm / 2;
            bedOriginY = -BedDepthMm / 2;
            bedCenterX = 0;
            bedCenterY = 0;
        }
        else // BedFrontLeft
        {
            bedOriginX = 0;
            bedOriginY = 0;
            bedCenterX = BedWidthMm / 2;
            bedCenterY = BedDepthMm / 2;
        }

        // Print origin = bed center when machine_center_is_zero=true (our slicer config)
        var printOriginX = bedCenterX;
        var printOriginY = bedCenterY;

        return new MachineMapping
        {
            TravelXMm = TravelXMm > 0 ? TravelXMm : BedWidthMm,
            TravelYMm = TravelYMm > 0 ? TravelYMm : BedDepthMm,
            TravelZMm = TravelZMm > 0 ? TravelZMm : BedHeightMm,
            Origin = OriginMode,
            BedWidthMm = BedWidthMm,
            BedDepthMm = BedDepthMm,
            BedHeightMm = BedHeightMm,
            BedOriginX = bedOriginX,
            BedOriginY = bedOriginY,
            BedCenterX = bedCenterX,
            BedCenterY = bedCenterY,
            PrintOriginX = printOriginX,
            PrintOriginY = printOriginY,
            ExtruderCount = ExtruderCount,
            ExtruderPositions = positions,
            DutyAssignments = ExtruderAssignments
                .Select(a => (a.ExtruderIndex, a.Duty))
                .ToList(),
            LeftBedEdgeOffsetMm = LeftBedEdgeOffsetMm,
            RightBedEdgeOffsetMm = RightBedEdgeOffsetMm,
            FrontBedEdgeOffsetMm = FrontBedEdgeOffsetMm,
            BackBedEdgeOffsetMm = BackBedEdgeOffsetMm,
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("INVALID_NAME", "Machine profile name must not be empty.");
        Name = name.Trim();
        Touch();
    }

    public MachineProfile Duplicate(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new DomainException("INVALID_NAME", "Name must not be empty.");

        var copy = Create(newName, Type, BedWidthMm, BedDepthMm, BedHeightMm, ExtruderCount);
        copy.TravelXMm = TravelXMm; copy.TravelYMm = TravelYMm; copy.TravelZMm = TravelZMm;
        copy.OriginMode = OriginMode;
        copy.BedPositionXMm = BedPositionXMm; copy.BedPositionYMm = BedPositionYMm;
        copy.OriginXMm = OriginXMm; copy.OriginYMm = OriginYMm;
        copy.IpAddress = IpAddress; copy.Port = Port;
        // Resin fields
        copy.Orientation = Orientation;
        copy.ResolutionX = ResolutionX; copy.ResolutionY = ResolutionY;
        copy.PixelPitchUm = PixelPitchUm;
        copy.MirrorX = MirrorX; copy.MirrorY = MirrorY;
        copy.BuildOffsetXMm = BuildOffsetXMm; copy.BuildOffsetYMm = BuildOffsetYMm;
        copy.DefaultLayerHeightMm = DefaultLayerHeightMm;
        copy.DefaultBottomLayerCount = DefaultBottomLayerCount;
        copy.DefaultNormalExposureMs = DefaultNormalExposureMs;
        copy.DefaultBottomExposureMs = DefaultBottomExposureMs;
        copy.LightOffDelayMs = LightOffDelayMs;
        copy.LiftDistanceMm = LiftDistanceMm; copy.LiftSpeedMmPerMin = LiftSpeedMmPerMin;
        copy.RetractDistanceMm = RetractDistanceMm; copy.RetractSpeedMmPerMin = RetractSpeedMmPerMin;
        copy.BottomLiftDistanceMm = BottomLiftDistanceMm; copy.BottomLiftSpeedMmPerMin = BottomLiftSpeedMmPerMin;
        copy.RestTimeAfterLiftMs = RestTimeAfterLiftMs; copy.RestTimeAfterRetractMs = RestTimeAfterRetractMs;
        copy.AntiAliasing = AntiAliasing;
        copy.ExportFormat = ExportFormat;
        return copy;
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}

public sealed record ExtruderAssignment(
    int ExtruderIndex,
    string Duty);
