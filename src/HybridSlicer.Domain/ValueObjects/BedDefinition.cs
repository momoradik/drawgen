namespace HybridSlicer.Domain.ValueObjects;

/// <summary>
/// Definition of a single bed/build area in a multi-bed machine.
/// All positions are in the machine travel frame (mm).
/// </summary>
public sealed record BedDefinition(
    int Index,
    double WidthMm,
    double DepthMm,
    double HeightMm,
    double PositionXMm,
    double PositionYMm);
