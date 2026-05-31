using HybridSlicer.Domain.Entities;
using HybridSlicer.Domain.Enums;
using HybridSlicer.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace HybridSlicer.Api.Controllers;

[ApiController]
[Route("api/resin-print-profiles")]
public sealed class ResinPrintProfilesController : ControllerBase
{
    private readonly IResinPrintProfileRepository _repo;
    public ResinPrintProfilesController(IResinPrintProfileRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _repo.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var p = await _repo.GetByIdAsync(id, ct);
        return p is null ? NotFound() : Ok(p);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ResinPrintProfileDto dto, CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(ct);
        if (all.Any(p => string.Equals(p.Name, dto.Name?.Trim(), StringComparison.OrdinalIgnoreCase)))
            return BadRequest($"A resin print profile named '{dto.Name?.Trim()}' already exists.");

        var profile = ResinPrintProfile.Create(dto.Name!);
        ApplyDto(profile, dto);
        await _repo.AddAsync(profile, ct);
        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, profile);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ResinPrintProfileDto dto, CancellationToken ct)
    {
        var profile = await _repo.GetByIdAsync(id, ct);
        if (profile is null) return NotFound();

        if (dto.Name is not null)
        {
            var trimmed = dto.Name.Trim();
            if (!string.Equals(trimmed, profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                var all = await _repo.GetAllAsync(ct);
                if (all.Any(p => p.Id != id && string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                    return BadRequest($"A resin print profile named '{trimmed}' already exists.");
            }
            profile.Rename(trimmed);
        }

        ApplyDto(profile, dto);
        await _repo.UpdateAsync(profile, ct);
        return Ok(profile);
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid id, [FromBody] DuplicateResinProfileRequest req, CancellationToken ct)
    {
        var original = await _repo.GetByIdAsync(id, ct);
        if (original is null) return NotFound();

        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = original.Name + " (Copy)";

        var all = await _repo.GetAllAsync(ct);
        if (all.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            var i = 2;
            while (all.Any(p => string.Equals(p.Name, $"{name} ({i})", StringComparison.OrdinalIgnoreCase))) i++;
            name = $"{name} ({i})";
        }

        var copy = original.Duplicate(name);
        await _repo.AddAsync(copy, ct);
        return CreatedAtAction(nameof(GetById), new { id = copy.Id }, copy);
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

    private static void ApplyDto(ResinPrintProfile p, ResinPrintProfileDto d)
    {
        p.Update(
            d.LayerHeightMm ?? 0.05,
            Enum.TryParse<AntiAliasingLevel>(d.AntiAliasing, true, out var aa) ? aa : AntiAliasingLevel.None,
            d.SupportEnabled ?? false, d.SupportType ?? "normal", d.SupportPlacement ?? "buildplate",
            d.SupportDensity ?? 0.5, d.SupportPattern ?? "default", d.SupportOverhangAngleDeg ?? 45,
            d.SupportXYDistanceMm ?? 0.3, d.SupportZDistanceMm ?? 0.15,
            d.SupportInterfaceEnabled ?? true, d.SupportInterfaceDensity ?? 0.8,
            d.SupportRoofEnabled ?? true, d.SupportFloorEnabled ?? false,
            d.HollowingEnabled ?? false, d.HollowWallThicknessMm ?? 1.5,
            d.DrainHoleDiameterMm ?? 2.5, d.DrainHoleDepthMm ?? 5.0);
    }
}

public record ResinPrintProfileDto(
    string? Name = null,
    double? LayerHeightMm = null, string? AntiAliasing = null,
    bool? SupportEnabled = null, string? SupportType = null, string? SupportPlacement = null,
    double? SupportDensity = null, string? SupportPattern = null, double? SupportOverhangAngleDeg = null,
    double? SupportXYDistanceMm = null, double? SupportZDistanceMm = null,
    bool? SupportInterfaceEnabled = null, double? SupportInterfaceDensity = null,
    bool? SupportRoofEnabled = null, bool? SupportFloorEnabled = null,
    bool? HollowingEnabled = null, double? HollowWallThicknessMm = null,
    double? DrainHoleDiameterMm = null, double? DrainHoleDepthMm = null);

public record DuplicateResinProfileRequest(string? Name = null);
