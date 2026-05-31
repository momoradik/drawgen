using HybridSlicer.Domain.Exceptions;

namespace HybridSlicer.Domain.Entities;

/// <summary>
/// Resin/material definition for MSLA/DLP printing.
/// Contains ONLY material-level settings — no machine or process fields.
/// </summary>
public class ResinMaterial
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = "Standard";   // Standard, Tough, Flexible, Castable, Dental, etc.
    public string? Manufacturer { get; private set; }
    public string? ColorHex { get; private set; }

    // Material-level exposure recommendations
    public double NormalExposureMs { get; private set; } = 2500;
    public double BottomExposureMs { get; private set; } = 30000;
    public int BottomLayerCount { get; private set; } = 5;
    public double LightOffDelayMs { get; private set; }

    // Material-level lift/retract overrides (0 = use printer default)
    public double LiftDistanceMm { get; private set; }
    public double LiftSpeedMmPerMin { get; private set; }
    public double RetractSpeedMmPerMin { get; private set; }

    // Physical properties
    public double DensityGPerCm3 { get; private set; } = 1.1;
    public double ViscosityCps { get; private set; }
    public int WavelengthNm { get; private set; } = 405;
    public double ShrinkagePct { get; private set; }

    public string? Notes { get; private set; }

    // Audit
    public string Version { get; private set; } = "1.0";
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    private ResinMaterial() { }

    public static ResinMaterial Create(string name, string category = "Standard")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("INVALID_NAME", "Name must not be empty.");
        return new ResinMaterial
        {
            Id = Guid.NewGuid(), Name = name.Trim(), Category = category?.Trim() ?? "Standard",
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
        string category, string? manufacturer, string? colorHex,
        double normalExposureMs, double bottomExposureMs, int bottomLayerCount, double lightOffDelayMs,
        double liftDistanceMm, double liftSpeedMmPerMin, double retractSpeedMmPerMin,
        double densityGPerCm3, double viscosityCps, int wavelengthNm, double shrinkagePct,
        string? notes)
    {
        Category = category?.Trim() ?? "Standard";
        Manufacturer = manufacturer?.Trim();
        ColorHex = colorHex?.Trim();
        NormalExposureMs = Math.Max(0, normalExposureMs);
        BottomExposureMs = Math.Max(0, bottomExposureMs);
        BottomLayerCount = Math.Max(0, bottomLayerCount);
        LightOffDelayMs = Math.Max(0, lightOffDelayMs);
        LiftDistanceMm = Math.Max(0, liftDistanceMm);
        LiftSpeedMmPerMin = Math.Max(0, liftSpeedMmPerMin);
        RetractSpeedMmPerMin = Math.Max(0, retractSpeedMmPerMin);
        DensityGPerCm3 = densityGPerCm3 > 0 ? densityGPerCm3 : DensityGPerCm3;
        ViscosityCps = Math.Max(0, viscosityCps);
        WavelengthNm = wavelengthNm > 0 ? wavelengthNm : WavelengthNm;
        ShrinkagePct = Math.Max(0, shrinkagePct);
        Notes = notes;
        Touch();
    }

    public ResinMaterial Duplicate(string newName)
    {
        var c = Create(newName, Category);
        c.Manufacturer = Manufacturer; c.ColorHex = ColorHex;
        c.NormalExposureMs = NormalExposureMs; c.BottomExposureMs = BottomExposureMs;
        c.BottomLayerCount = BottomLayerCount; c.LightOffDelayMs = LightOffDelayMs;
        c.LiftDistanceMm = LiftDistanceMm; c.LiftSpeedMmPerMin = LiftSpeedMmPerMin;
        c.RetractSpeedMmPerMin = RetractSpeedMmPerMin;
        c.DensityGPerCm3 = DensityGPerCm3; c.ViscosityCps = ViscosityCps;
        c.WavelengthNm = WavelengthNm; c.ShrinkagePct = ShrinkagePct;
        c.Notes = Notes;
        return c;
    }

    public void SoftDelete() { IsDeleted = true; Touch(); }
    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
