using HybridSlicer.Domain.Entities;
using HybridSlicer.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace HybridSlicer.Api.Controllers;

[ApiController]
[Route("api/resin-materials")]
public sealed class ResinMaterialsController : ControllerBase
{
    private readonly IResinMaterialRepository _repo;
    public ResinMaterialsController(IResinMaterialRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _repo.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var m = await _repo.GetByIdAsync(id, ct);
        return m is null ? NotFound() : Ok(m);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ResinMaterialDto dto, CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(ct);
        if (all.Any(m => string.Equals(m.Name, dto.Name?.Trim(), StringComparison.OrdinalIgnoreCase)))
            return BadRequest($"A resin material named '{dto.Name?.Trim()}' already exists.");

        var mat = ResinMaterial.Create(dto.Name!, dto.Category ?? "Standard");
        Apply(mat, dto);
        await _repo.AddAsync(mat, ct);
        return CreatedAtAction(nameof(GetById), new { id = mat.Id }, mat);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ResinMaterialDto dto, CancellationToken ct)
    {
        var mat = await _repo.GetByIdAsync(id, ct);
        if (mat is null) return NotFound();

        if (dto.Name is not null)
        {
            var trimmed = dto.Name.Trim();
            if (!string.Equals(trimmed, mat.Name, StringComparison.OrdinalIgnoreCase))
            {
                var all = await _repo.GetAllAsync(ct);
                if (all.Any(m => m.Id != id && string.Equals(m.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                    return BadRequest($"A resin material named '{trimmed}' already exists.");
            }
            mat.Rename(trimmed);
        }
        Apply(mat, dto);
        await _repo.UpdateAsync(mat, ct);
        return Ok(mat);
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid id, [FromBody] DupResinMaterialReq req, CancellationToken ct)
    {
        var orig = await _repo.GetByIdAsync(id, ct);
        if (orig is null) return NotFound();
        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = orig.Name + " (Copy)";
        var all = await _repo.GetAllAsync(ct);
        if (all.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        { var i = 2; while (all.Any(m => string.Equals(m.Name, $"{name} ({i})", StringComparison.OrdinalIgnoreCase))) i++; name = $"{name} ({i})"; }
        var copy = orig.Duplicate(name);
        await _repo.AddAsync(copy, ct);
        return CreatedAtAction(nameof(GetById), new { id = copy.Id }, copy);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var mat = await _repo.GetByIdAsync(id, ct);
        if (mat is null) return NotFound();
        mat.SoftDelete();
        await _repo.UpdateAsync(mat, ct);
        return NoContent();
    }

    private static void Apply(ResinMaterial m, ResinMaterialDto d) => m.Update(
        d.Category ?? "Standard", d.Manufacturer, d.ColorHex,
        d.NormalExposureMs ?? m.NormalExposureMs, d.BottomExposureMs ?? m.BottomExposureMs,
        d.BottomLayerCount ?? m.BottomLayerCount, d.LightOffDelayMs ?? m.LightOffDelayMs,
        d.LiftDistanceMm ?? m.LiftDistanceMm, d.LiftSpeedMmPerMin ?? m.LiftSpeedMmPerMin,
        d.RetractSpeedMmPerMin ?? m.RetractSpeedMmPerMin,
        d.DensityGPerCm3 ?? m.DensityGPerCm3, d.ViscosityCps ?? m.ViscosityCps,
        d.WavelengthNm ?? m.WavelengthNm, d.ShrinkagePct ?? m.ShrinkagePct,
        d.Notes);
}

public record ResinMaterialDto(
    string? Name = null, string? Category = null, string? Manufacturer = null, string? ColorHex = null,
    double? NormalExposureMs = null, double? BottomExposureMs = null, int? BottomLayerCount = null,
    double? LightOffDelayMs = null,
    double? LiftDistanceMm = null, double? LiftSpeedMmPerMin = null, double? RetractSpeedMmPerMin = null,
    double? DensityGPerCm3 = null, double? ViscosityCps = null, int? WavelengthNm = null,
    double? ShrinkagePct = null, string? Notes = null);

public record DupResinMaterialReq(string? Name = null);
