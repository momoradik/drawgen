using HybridSlicer.Domain.Enums;
using HybridSlicer.Domain.Exceptions;

namespace HybridSlicer.Domain.Entities;

/// <summary>
/// Reusable job/process preset for resin (MSLA/DLP) printing.
/// Contains ONLY process-level settings — no machine or material identity fields.
/// Machine settings come from PrinterProfile. Material identity comes from MaterialProfile.
/// </summary>
public class ResinPrintProfile
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    // ── Layer / Quality ─────────────────────────────────────────────────────
    public double LayerHeightMm { get; private set; } = 0.05;
    public AntiAliasingLevel AntiAliasing { get; private set; } = AntiAliasingLevel.None;

    // ── Support ─────────────────────────────────────────────────────────────
    public bool SupportEnabled { get; private set; }
    public string SupportType { get; private set; } = "normal";         // normal | tree
    public string SupportPlacement { get; private set; } = "buildplate"; // buildplate | everywhere
    public double SupportDensity { get; private set; } = 0.5;           // 0..1
    public string SupportPattern { get; private set; } = "default";
    public double SupportOverhangAngleDeg { get; private set; } = 45;
    public double SupportXYDistanceMm { get; private set; } = 0.3;
    public double SupportZDistanceMm { get; private set; } = 0.15;
    public bool SupportInterfaceEnabled { get; private set; } = true;
    public double SupportInterfaceDensity { get; private set; } = 0.8;
    public bool SupportRoofEnabled { get; private set; } = true;
    public bool SupportFloorEnabled { get; private set; }

    // ── Hollowing ───────────────────────────────────────────────────────────
    public bool HollowingEnabled { get; private set; }
    public double HollowWallThicknessMm { get; private set; } = 1.5;

    // ── Drain Holes ─────────────────────────────────────────────────────────
    public double DrainHoleDiameterMm { get; private set; } = 2.5;
    public double DrainHoleDepthMm { get; private set; } = 5.0;

    // ── Audit ───────────────────────────────────────────────────────────────
    public string Version { get; private set; } = "1.0";
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    private ResinPrintProfile() { }

    public static ResinPrintProfile Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("INVALID_NAME", "Name must not be empty.");
        return new ResinPrintProfile
        {
            Id = Guid.NewGuid(), Name = name.Trim(),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("INVALID_NAME", "Name must not be empty.");
        Name = name.Trim(); Touch();
    }

    public void Update(
        double layerHeightMm, AntiAliasingLevel antiAliasing,
        bool supportEnabled, string supportType, string supportPlacement,
        double supportDensity, string supportPattern, double supportOverhangAngleDeg,
        double supportXYDistanceMm, double supportZDistanceMm,
        bool supportInterfaceEnabled, double supportInterfaceDensity,
        bool supportRoofEnabled, bool supportFloorEnabled,
        bool hollowingEnabled, double hollowWallThicknessMm,
        double drainHoleDiameterMm, double drainHoleDepthMm)
    {
        if (layerHeightMm is <= 0 or > 1)
            throw new DomainException("INVALID_LAYER_HEIGHT", "Layer height must be 0.001–1 mm.");
        LayerHeightMm = layerHeightMm;
        AntiAliasing = antiAliasing;
        SupportEnabled = supportEnabled;
        SupportType = supportType ?? "normal";
        SupportPlacement = supportPlacement ?? "buildplate";
        SupportDensity = Math.Clamp(supportDensity, 0, 1);
        SupportPattern = supportPattern ?? "default";
        SupportOverhangAngleDeg = Math.Clamp(supportOverhangAngleDeg, 0, 90);
        SupportXYDistanceMm = Math.Max(0, supportXYDistanceMm);
        SupportZDistanceMm = Math.Max(0, supportZDistanceMm);
        SupportInterfaceEnabled = supportInterfaceEnabled;
        SupportInterfaceDensity = Math.Clamp(supportInterfaceDensity, 0, 1);
        SupportRoofEnabled = supportRoofEnabled;
        SupportFloorEnabled = supportFloorEnabled;
        HollowingEnabled = hollowingEnabled;
        HollowWallThicknessMm = Math.Max(0.1, hollowWallThicknessMm);
        DrainHoleDiameterMm = Math.Max(0.1, drainHoleDiameterMm);
        DrainHoleDepthMm = Math.Max(0.1, drainHoleDepthMm);
        Touch();
    }

    public ResinPrintProfile Duplicate(string newName)
    {
        var copy = Create(newName);
        copy.LayerHeightMm = LayerHeightMm; copy.AntiAliasing = AntiAliasing;
        copy.SupportEnabled = SupportEnabled; copy.SupportType = SupportType;
        copy.SupportPlacement = SupportPlacement; copy.SupportDensity = SupportDensity;
        copy.SupportPattern = SupportPattern; copy.SupportOverhangAngleDeg = SupportOverhangAngleDeg;
        copy.SupportXYDistanceMm = SupportXYDistanceMm; copy.SupportZDistanceMm = SupportZDistanceMm;
        copy.SupportInterfaceEnabled = SupportInterfaceEnabled; copy.SupportInterfaceDensity = SupportInterfaceDensity;
        copy.SupportRoofEnabled = SupportRoofEnabled; copy.SupportFloorEnabled = SupportFloorEnabled;
        copy.HollowingEnabled = HollowingEnabled; copy.HollowWallThicknessMm = HollowWallThicknessMm;
        copy.DrainHoleDiameterMm = DrainHoleDiameterMm; copy.DrainHoleDepthMm = DrainHoleDepthMm;
        return copy;
    }

    public void SoftDelete() { IsDeleted = true; Touch(); }
    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
