namespace HybridSlicer.Infrastructure.Resin;

/// <summary>
/// Structured per-layer metadata for one sliced layer.
/// This is the bridge between the slicer engine and preview/export consumers.
/// Image data is stored separately on disk as PNG; this record holds the metadata.
/// </summary>
public sealed record LayerRecord
{
    public int Index { get; init; }
    public float ZHeightMm { get; init; }           // mid-layer Z position
    public float LayerThicknessMm { get; init; }
    public LayerType Type { get; init; }
    public double ExposureMs { get; init; }
    public double LiftDistanceMm { get; init; }
    public double LiftSpeedMmPerMin { get; init; }
    public double LightOffDelayMs { get; init; }
    public string ImageFileName { get; init; } = "";
    public int ContourCount { get; init; }           // polygon contours in this layer
    public long ImageSizeBytes { get; init; }
    public bool IsEmpty { get; init; }               // true if no geometry at this Z
    public int IslandCount { get; init; }             // unsupported islands in this layer
}

public enum LayerType
{
    Bottom,
    Transition,
    Normal,
}

/// <summary>
/// Complete sliced job data: summary + per-layer records.
/// This is the structured output of the slicer, consumed by preview and export.
/// </summary>
public sealed class SliceJobData
{
    public string JobId { get; init; } = "";
    public string OutputDir { get; init; } = "";
    public int LayerCount { get; init; }
    public int BottomLayerCount { get; init; }
    public int TransitionLayerCount { get; init; }
    public double LayerHeightMm { get; init; }
    public int ResolutionX { get; init; }
    public int ResolutionY { get; init; }
    public double BuildWidthMm { get; init; }
    public double BuildDepthMm { get; init; }
    public double TotalHeightMm { get; init; }
    public double NormalExposureMs { get; init; }
    public double BottomExposureMs { get; init; }
    public double EstimatedPrintTimeMin { get; init; }
    public long ElapsedMs { get; init; }
    public string PrinterName { get; init; } = "";
    public string ProfileName { get; init; } = "";
    public string Orientation { get; init; } = "";
    public string AntiAliasing { get; init; } = "";
    public bool MirrorX { get; init; }
    public bool MirrorY { get; init; }
    public bool HollowEnabled { get; init; }
    public double HollowWallThicknessMm { get; init; }
    public bool SupportEnabled { get; init; }
    public string SupportType { get; init; } = "";
    public string SupportPlacement { get; init; } = "";
    public DateTime SlicedAt { get; init; }

    /// <summary>Per-layer metadata. Index matches layer number.</summary>
    public List<LayerRecord> Layers { get; init; } = [];
}
