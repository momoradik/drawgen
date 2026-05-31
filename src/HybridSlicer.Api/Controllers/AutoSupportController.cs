using HybridSlicer.Application.Interfaces.Repositories;
using HybridSlicer.Domain.Enums;
using HybridSlicer.Infrastructure.Resin;
using Microsoft.AspNetCore.Mvc;

namespace HybridSlicer.Api.Controllers;

[ApiController]
[Route("api/auto-support")]
public sealed class AutoSupportController : ControllerBase
{
    private readonly IMachineProfileRepository _printerRepo;

    public AutoSupportController(IMachineProfileRepository printerRepo) => _printerRepo = printerRepo;

    /// <summary>
    /// Generate auto-supports, raft, and skirt for an STL mesh.
    /// Returns support positions + raft/skirt geometry data for viewport rendering.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> Generate(
        [FromForm] IFormFile stlFile,
        [FromForm] string? printerId = null,
        [FromForm] string orientation = "BottomUp",
        [FromForm] double overhangAngleDeg = 45,
        [FromForm] double density = 0.5,
        [FromForm] double tipDiameterMm = 0.4,
        [FromForm] double columnDiameterMm = 0.8,
        [FromForm] string supportType = "normal",
        [FromForm] string placement = "buildplate",
        [FromForm] bool raftEnabled = false,
        [FromForm] string raftType = "grid",
        [FromForm] double raftThicknessMm = 1.5,
        [FromForm] double raftMarginMm = 3.0,
        [FromForm] bool skirtEnabled = false,
        [FromForm] int skirtLayers = 3,
        [FromForm] double skirtDistanceMm = 2.0,
        CancellationToken ct = default)
    {
        if (stlFile is null || stlFile.Length == 0)
            return BadRequest("STL file is required.");

        byte[] stlData;
        using (var ms = new MemoryStream())
        {
            await stlFile.CopyToAsync(ms, ct);
            stlData = ms.ToArray();
        }

        // Resolve orientation from printer if provided
        var orient = PrinterOrientation.BottomUp;
        if (!string.IsNullOrEmpty(printerId) && Guid.TryParse(printerId, out var pid))
        {
            var printer = await _printerRepo.GetByIdAsync(pid, ct);
            if (printer is not null) orient = printer.Orientation;
        }
        else if (Enum.TryParse<PrinterOrientation>(orientation, true, out var o))
            orient = o;

        var (mesh, _) = MeshValidator.ValidateAndRepair(stlData);

        var config = new AutoSupportEngine.SupportConfig
        {
            Orientation = orient,
            OverhangAngleDeg = overhangAngleDeg,
            DensityFactor = density,
            TipDiameterMm = tipDiameterMm,
            ColumnDiameterMm = columnDiameterMm,
            SupportType = supportType,
            Placement = placement,
            RaftEnabled = raftEnabled,
            RaftType = raftType,
            RaftThicknessMm = raftThicknessMm,
            RaftMarginMm = raftMarginMm,
            SkirtEnabled = skirtEnabled,
            SkirtLayers = skirtLayers,
            SkirtDistanceMm = skirtDistanceMm,
        };

        var result = AutoSupportEngine.Generate(mesh, config);

        return Ok(new
        {
            supportCount = result.Supports.Count,
            overhangFaceCount = result.OverhangFaceCount,
            result.ElapsedMs,
            orientation = orient.ToString(),
            supports = result.Supports.Select(s => new {
                s.X, s.Y, contactZ = s.ContactZ, baseZ = s.BaseZ,
                s.TipDiameter, s.ColumnDiameter, s.BaseDiameter,
                normalX = s.NormalX, normalY = s.NormalY, normalZ = s.NormalZ,
            }),
            raft = result.Raft is not null ? new {
                result.Raft.Type, result.Raft.MinX, result.Raft.MinY,
                result.Raft.MaxX, result.Raft.MaxY, result.Raft.ThicknessMm, result.Raft.MarginMm,
            } : null,
            skirt = result.Skirt is not null ? new {
                result.Skirt.MinX, result.Skirt.MinY, result.Skirt.MaxX, result.Skirt.MaxY,
                result.Skirt.Layers, result.Skirt.DistanceMm, result.Skirt.WidthMm,
            } : null,
        });
    }
}
