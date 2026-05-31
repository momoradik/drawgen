using HybridSlicer.Infrastructure.Resin;
using Microsoft.AspNetCore.Mvc;

namespace HybridSlicer.Api.Controllers;

[ApiController]
[Route("api/mesh")]
public sealed class MeshValidationController : ControllerBase
{
    [HttpPost("validate")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> Validate([FromForm] IFormFile stlFile, CancellationToken ct)
    {
        if (stlFile is null || stlFile.Length == 0)
            return BadRequest("STL file is required.");

        byte[] data;
        using (var ms = new MemoryStream())
        {
            await stlFile.CopyToAsync(ms, ct);
            data = ms.ToArray();
        }

        try
        {
            var mesh = StlMesh.FromBinary(data);
            var result = MeshValidator.Validate(mesh);

            var size = mesh.Max - mesh.Min;
            return Ok(new
            {
                result.IsValid,
                result.TriangleCount,
                result.DegenerateTriangles,
                result.NanInfVertices,
                result.FlippedNormals,
                result.NonManifoldEdges,
                result.OpenEdges,
                result.BoundsValid,
                result.VolumeMm3,
                sizeX = size.X, sizeY = size.Y, sizeZ = size.Z,
                result.Warnings,
                result.Errors,
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                IsValid = false,
                TriangleCount = 0,
                Warnings = Array.Empty<string>(),
                Errors = new[] { $"Failed to parse STL: {ex.Message}" },
            });
        }
    }
}
