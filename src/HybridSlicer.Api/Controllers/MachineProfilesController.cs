using HybridSlicer.Application.Interfaces.Repositories;
using HybridSlicer.Domain.Entities;
using HybridSlicer.Domain.Enums;
using HybridSlicer.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace HybridSlicer.Api.Controllers;

[ApiController]
[Route("api/machine-profiles")]
public sealed class MachineProfilesController : ControllerBase
{
    private readonly IMachineProfileRepository _repo;

    public MachineProfilesController(IMachineProfileRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _repo.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var profile = await _repo.GetByIdAsync(id, ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMachineProfileRequest req, CancellationToken ct)
    {
        // Prevent duplicate names
        var all = await _repo.GetAllAsync(ct);
        if (all.Any(m => string.Equals(m.Name, req.Name?.Trim(), StringComparison.OrdinalIgnoreCase)))
            return BadRequest($"A machine profile named '{req.Name?.Trim()}' already exists.");

        var profile = MachineProfile.Create(
            req.Name, req.Type,
            req.BedWidthMm, req.BedDepthMm, req.BedHeightMm,
            req.ExtruderCount);

        if (req.TravelXMm.HasValue && req.TravelYMm.HasValue && req.TravelZMm.HasValue)
            profile.SetTravel(req.TravelXMm.Value, req.TravelYMm.Value, req.TravelZMm.Value);

        if (req.OriginMode is not null && Enum.TryParse<OriginMode>(req.OriginMode, true, out var om))
            profile.SetOriginMode(om);

        if (req.BedPositionXMm.HasValue || req.BedPositionYMm.HasValue)
            profile.SetBedPosition(req.BedPositionXMm ?? 0, req.BedPositionYMm ?? 0);

        if (req.OriginXMm.HasValue || req.OriginYMm.HasValue)
            profile.SetOrigin(req.OriginXMm ?? 0, req.OriginYMm ?? 0);

        if (req.Beds is { Count: > 0 })
            profile.SetBeds(req.Beds.Select((b, i) =>
                new BedDefinition(i, b.WidthMm, b.DepthMm, b.HeightMm, b.PositionXMm, b.PositionYMm)).ToList());

        if (req.IpAddress is not null)
            profile.SetNetworkEndpoint(req.IpAddress, req.Port);

        if (req.NozzleXOffsets is not null)
            profile.SetNozzleXOffsets(req.NozzleXOffsets);
        if (req.NozzleYOffsets is not null)
            profile.SetNozzleYOffsets(req.NozzleYOffsets);

        profile.SetBedEdgeOffsets(req.LeftBedEdgeOffsetMm, req.RightBedEdgeOffsetMm,
            req.FrontBedEdgeOffsetMm, req.BackBedEdgeOffsetMm);

        if (req.ExtruderAssignments is not null)
            profile.SetExtruderAssignments(
                req.ExtruderAssignments.Select(a => new ExtruderAssignment(a.ExtruderIndex, a.Duty)).ToList());

        if (req.CncOffset is not null)
            profile.UpdateCncOffset(new Domain.ValueObjects.MachineOffset(
                req.CncOffset.X, req.CncOffset.Y, req.CncOffset.Z, req.CncOffset.RotationDeg));

        if (req.SafeClearanceHeightMm.HasValue)
            profile.SetSafeClearanceHeight(req.SafeClearanceHeightMm.Value);

        if (req.ExtruderAxes is not null) profile.SetExtruderAxes(req.ExtruderAxes);
        if (req.CncAxes is not null) profile.SetCncAxes(req.CncAxes);
        if (req.MotionAssignmentEnabled.HasValue || req.MotionAssignmentJson is not null)
            profile.SetMotionAssignment(req.MotionAssignmentEnabled ?? false, req.MotionAssignmentJson ?? "{}");

        // Resin fields (MSLA / DLP)
        if (req.Type is MachineType.MSLA or MachineType.DLP)
            ApplyResinFields(profile, req);

        await _repo.AddAsync(profile, ct);
        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, profile);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMachineProfileRequest req, CancellationToken ct)
    {
        var profile = await _repo.GetByIdAsync(id, ct);
        if (profile is null) return NotFound();

        if (req.Name is not null)
        {
            var trimmed = req.Name.Trim();
            if (!string.Equals(trimmed, profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                var all = await _repo.GetAllAsync(ct);
                if (all.Any(m => m.Id != id && string.Equals(m.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                    return BadRequest($"A machine profile named '{trimmed}' already exists.");
            }
            profile.Rename(req.Name);
        }

        if (req.TravelXMm.HasValue || req.TravelYMm.HasValue || req.TravelZMm.HasValue)
            profile.SetTravel(
                req.TravelXMm ?? profile.TravelXMm,
                req.TravelYMm ?? profile.TravelYMm,
                req.TravelZMm ?? profile.TravelZMm);

        if (req.OriginMode is not null && Enum.TryParse<OriginMode>(req.OriginMode, true, out var om))
            profile.SetOriginMode(om);

        if (req.BedPositionXMm.HasValue || req.BedPositionYMm.HasValue)
            profile.SetBedPosition(
                req.BedPositionXMm ?? profile.BedPositionXMm,
                req.BedPositionYMm ?? profile.BedPositionYMm);

        if (req.OriginXMm.HasValue || req.OriginYMm.HasValue)
            profile.SetOrigin(
                req.OriginXMm ?? profile.OriginXMm,
                req.OriginYMm ?? profile.OriginYMm);

        if (req.BedWidthMm.HasValue || req.BedDepthMm.HasValue || req.BedHeightMm.HasValue)
            profile.UpdateBedDimensions(
                req.BedWidthMm ?? profile.BedWidthMm,
                req.BedDepthMm ?? profile.BedDepthMm,
                req.BedHeightMm ?? profile.BedHeightMm);

        if (req.Beds is { Count: > 0 })
            profile.SetBeds(req.Beds.Select((b, i) =>
                new BedDefinition(i, b.WidthMm, b.DepthMm, b.HeightMm, b.PositionXMm, b.PositionYMm)).ToList());

        if (req.ExtruderCount.HasValue)
            profile.SetExtruderCount(req.ExtruderCount.Value);

        if (req.NozzleXOffsets is not null)
            profile.SetNozzleXOffsets(req.NozzleXOffsets);
        if (req.NozzleYOffsets is not null)
            profile.SetNozzleYOffsets(req.NozzleYOffsets);

        if (req.LeftBedEdgeOffsetMm.HasValue || req.RightBedEdgeOffsetMm.HasValue
            || req.FrontBedEdgeOffsetMm.HasValue || req.BackBedEdgeOffsetMm.HasValue)
            profile.SetBedEdgeOffsets(
                req.LeftBedEdgeOffsetMm ?? profile.LeftBedEdgeOffsetMm,
                req.RightBedEdgeOffsetMm ?? profile.RightBedEdgeOffsetMm,
                req.FrontBedEdgeOffsetMm ?? profile.FrontBedEdgeOffsetMm,
                req.BackBedEdgeOffsetMm ?? profile.BackBedEdgeOffsetMm);

        if (req.ExtruderAssignments is not null)
            profile.SetExtruderAssignments(
                req.ExtruderAssignments.Select(a => new ExtruderAssignment(a.ExtruderIndex, a.Duty)).ToList());

        if (req.IpAddress is not null)
        {
            if (string.IsNullOrWhiteSpace(req.IpAddress))
                profile.ClearNetworkEndpoint();
            else
                profile.SetNetworkEndpoint(req.IpAddress, req.Port ?? profile.Port);
        }

        if (req.CncOffset is not null)
            profile.UpdateCncOffset(new MachineOffset(
                req.CncOffset.X, req.CncOffset.Y, req.CncOffset.Z, req.CncOffset.RotationDeg));

        if (req.SafeClearanceHeightMm.HasValue)
            profile.SetSafeClearanceHeight(req.SafeClearanceHeightMm.Value);

        if (req.ExtruderAxes is not null) profile.SetExtruderAxes(req.ExtruderAxes);
        if (req.CncAxes is not null) profile.SetCncAxes(req.CncAxes);
        if (req.MotionAssignmentEnabled.HasValue || req.MotionAssignmentJson is not null)
            profile.SetMotionAssignment(req.MotionAssignmentEnabled ?? profile.MotionAssignmentEnabled, req.MotionAssignmentJson ?? profile.MotionAssignmentJson);

        // Resin fields
        if (req.ResinSettings is not null)
            ApplyResinFields(profile, req.ResinSettings);

        await _repo.UpdateAsync(profile, ct);
        return Ok(profile);
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid id, [FromBody] DuplicateRequest req, CancellationToken ct)
    {
        var original = await _repo.GetByIdAsync(id, ct);
        if (original is null) return NotFound();

        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = original.Name + " (Copy)";

        var all = await _repo.GetAllAsync(ct);
        if (all.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            var i = 2;
            while (all.Any(m => string.Equals(m.Name, $"{name} ({i})", StringComparison.OrdinalIgnoreCase))) i++;
            name = $"{name} ({i})";
        }

        var copy = original.Duplicate(name);
        await _repo.AddAsync(copy, ct);
        return CreatedAtAction(nameof(GetById), new { id = copy.Id }, copy);
    }

    private static void ApplyResinFields(MachineProfile profile, ResinSettingsDto r)
    {
        profile.SetResinSettings(
            Enum.TryParse<Domain.Enums.PrinterOrientation>(r.Orientation, true, out var o) ? o : profile.Orientation,
            r.ResolutionX ?? profile.ResolutionX,
            r.ResolutionY ?? profile.ResolutionY,
            r.PixelPitchUm ?? profile.PixelPitchUm,
            r.MirrorX ?? profile.MirrorX,
            r.MirrorY ?? profile.MirrorY,
            r.BuildOffsetXMm ?? profile.BuildOffsetXMm,
            r.BuildOffsetYMm ?? profile.BuildOffsetYMm,
            r.DefaultLayerHeightMm ?? profile.DefaultLayerHeightMm,
            r.DefaultBottomLayerCount ?? profile.DefaultBottomLayerCount,
            r.DefaultNormalExposureMs ?? profile.DefaultNormalExposureMs,
            r.DefaultBottomExposureMs ?? profile.DefaultBottomExposureMs,
            r.LightOffDelayMs ?? profile.LightOffDelayMs,
            r.LiftDistanceMm ?? profile.LiftDistanceMm,
            r.LiftSpeedMmPerMin ?? profile.LiftSpeedMmPerMin,
            r.RetractDistanceMm ?? profile.RetractDistanceMm,
            r.RetractSpeedMmPerMin ?? profile.RetractSpeedMmPerMin,
            r.BottomLiftDistanceMm ?? profile.BottomLiftDistanceMm,
            r.BottomLiftSpeedMmPerMin ?? profile.BottomLiftSpeedMmPerMin,
            r.RestTimeAfterLiftMs ?? profile.RestTimeAfterLiftMs,
            r.RestTimeAfterRetractMs ?? profile.RestTimeAfterRetractMs,
            Enum.TryParse<Domain.Enums.AntiAliasingLevel>(r.AntiAliasing, true, out var aa) ? aa : profile.AntiAliasing,
            r.ExportFormat ?? profile.ExportFormat);
    }

    private static void ApplyResinFields(MachineProfile profile, CreateMachineProfileRequest req)
    {
        var r = new ResinSettingsDto(
            Orientation: req.Orientation, ResolutionX: req.ResolutionX, ResolutionY: req.ResolutionY,
            PixelPitchUm: req.PixelPitchUm, MirrorX: req.MirrorX, MirrorY: req.MirrorY,
            BuildOffsetXMm: req.BuildOffsetXMm, BuildOffsetYMm: req.BuildOffsetYMm,
            DefaultLayerHeightMm: req.DefaultLayerHeightMm, DefaultBottomLayerCount: req.DefaultBottomLayerCount,
            DefaultNormalExposureMs: req.DefaultNormalExposureMs, DefaultBottomExposureMs: req.DefaultBottomExposureMs,
            LightOffDelayMs: req.LightOffDelayMs,
            LiftDistanceMm: req.LiftDistanceMm, LiftSpeedMmPerMin: req.LiftSpeedMmPerMin,
            RetractDistanceMm: req.RetractDistanceMm, RetractSpeedMmPerMin: req.RetractSpeedMmPerMin,
            BottomLiftDistanceMm: req.BottomLiftDistanceMm, BottomLiftSpeedMmPerMin: req.BottomLiftSpeedMmPerMin,
            RestTimeAfterLiftMs: req.RestTimeAfterLiftMs, RestTimeAfterRetractMs: req.RestTimeAfterRetractMs,
            AntiAliasing: req.AntiAliasing, ExportFormat: req.ExportFormat);
        ApplyResinFields(profile, r);
    }

    [HttpPut("{id:guid}/offsets")]
    public async Task<IActionResult> UpdateOffsets(Guid id, [FromBody] UpdateOffsetsRequest req, CancellationToken ct)
    {
        var profile = await _repo.GetByIdAsync(id, ct);
        if (profile is null) return NotFound();

        profile.UpdateCncOffset(new MachineOffset(req.X, req.Y, req.Z, req.RotationDeg));

        foreach (var to in req.ToolOffsets)
            profile.UpsertToolOffset(new ToolOffset(to.ToolIndex, to.LengthOffsetMm, to.RadiusOffsetMm,
                to.OffsetX, to.OffsetY, to.OffsetZ, to.Description));

        await _repo.UpdateAsync(profile, ct);
        return Ok(profile);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var profile = await _repo.GetByIdAsync(id, ct);
        if (profile is null) return NotFound();
        profile.SoftDelete();
        await _repo.UpdateAsync(profile, ct);
        return NoContent();
    }
}

public record CreateMachineProfileRequest(
    string Name,
    MachineType Type,
    double BedWidthMm,
    double BedDepthMm,
    double BedHeightMm,
    int ExtruderCount = 1,
    double? TravelXMm = null,
    double? TravelYMm = null,
    double? TravelZMm = null,
    string? OriginMode = null,
    double? BedPositionXMm = null,
    double? BedPositionYMm = null,
    double? OriginXMm = null,
    double? OriginYMm = null,
    IReadOnlyList<BedDto>? Beds = null,
    IReadOnlyList<double>? NozzleXOffsets = null,
    IReadOnlyList<double>? NozzleYOffsets = null,
    double LeftBedEdgeOffsetMm = 0,
    double RightBedEdgeOffsetMm = 0,
    double FrontBedEdgeOffsetMm = 0,
    double BackBedEdgeOffsetMm = 0,
    IReadOnlyList<ExtruderAssignmentDto>? ExtruderAssignments = null,
    string? IpAddress = null,
    int Port = 8080,
    OffsetDto? CncOffset = null,
    double? SafeClearanceHeightMm = null,
    string? ExtruderAxes = null,
    string? CncAxes = null,
    bool? MotionAssignmentEnabled = null,
    string? MotionAssignmentJson = null,
    // Resin fields
    string? Orientation = null,
    int? ResolutionX = null, int? ResolutionY = null,
    double? PixelPitchUm = null,
    bool? MirrorX = null, bool? MirrorY = null,
    double? BuildOffsetXMm = null, double? BuildOffsetYMm = null,
    double? DefaultLayerHeightMm = null, int? DefaultBottomLayerCount = null,
    double? DefaultNormalExposureMs = null, double? DefaultBottomExposureMs = null,
    double? LightOffDelayMs = null,
    double? LiftDistanceMm = null, double? LiftSpeedMmPerMin = null,
    double? RetractDistanceMm = null, double? RetractSpeedMmPerMin = null,
    double? BottomLiftDistanceMm = null, double? BottomLiftSpeedMmPerMin = null,
    double? RestTimeAfterLiftMs = null, double? RestTimeAfterRetractMs = null,
    string? AntiAliasing = null, string? ExportFormat = null);

public record UpdateMachineProfileRequest(
    string? Name = null,
    double? BedWidthMm = null,
    double? BedDepthMm = null,
    double? BedHeightMm = null,
    int? ExtruderCount = null,
    double? TravelXMm = null,
    double? TravelYMm = null,
    double? TravelZMm = null,
    string? OriginMode = null,
    double? BedPositionXMm = null,
    double? BedPositionYMm = null,
    double? OriginXMm = null,
    double? OriginYMm = null,
    IReadOnlyList<BedDto>? Beds = null,
    IReadOnlyList<double>? NozzleXOffsets = null,
    IReadOnlyList<double>? NozzleYOffsets = null,
    double? LeftBedEdgeOffsetMm = null,
    double? RightBedEdgeOffsetMm = null,
    double? FrontBedEdgeOffsetMm = null,
    double? BackBedEdgeOffsetMm = null,
    IReadOnlyList<ExtruderAssignmentDto>? ExtruderAssignments = null,
    string? IpAddress = null,
    int? Port = null,
    OffsetDto? CncOffset = null,
    double? SafeClearanceHeightMm = null,
    string? ExtruderAxes = null,
    string? CncAxes = null,
    bool? MotionAssignmentEnabled = null,
    string? MotionAssignmentJson = null,
    ResinSettingsDto? ResinSettings = null);

public record ResinSettingsDto(
    string? Orientation = null,
    int? ResolutionX = null, int? ResolutionY = null,
    double? PixelPitchUm = null,
    bool? MirrorX = null, bool? MirrorY = null,
    double? BuildOffsetXMm = null, double? BuildOffsetYMm = null,
    double? DefaultLayerHeightMm = null, int? DefaultBottomLayerCount = null,
    double? DefaultNormalExposureMs = null, double? DefaultBottomExposureMs = null,
    double? LightOffDelayMs = null,
    double? LiftDistanceMm = null, double? LiftSpeedMmPerMin = null,
    double? RetractDistanceMm = null, double? RetractSpeedMmPerMin = null,
    double? BottomLiftDistanceMm = null, double? BottomLiftSpeedMmPerMin = null,
    double? RestTimeAfterLiftMs = null, double? RestTimeAfterRetractMs = null,
    string? AntiAliasing = null, string? ExportFormat = null);

public record DuplicateRequest(string? Name = null);

public record UpdateOffsetsRequest(
    double X, double Y, double Z, double RotationDeg,
    IReadOnlyList<ToolOffsetDto> ToolOffsets);

public record OffsetDto(double X, double Y, double Z, double RotationDeg = 0);
public record ToolOffsetDto(int ToolIndex, double LengthOffsetMm, double RadiusOffsetMm,
    double OffsetX = 0, double OffsetY = 0, double OffsetZ = 0, string? Description = null);
public record ExtruderAssignmentDto(int ExtruderIndex, string Duty);
public record BedDto(double WidthMm, double DepthMm, double HeightMm, double PositionXMm, double PositionYMm);
