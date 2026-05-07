using System.Globalization;
using System.Text;
using HybridSlicer.Application.Interfaces;
using HybridSlicer.Domain.Entities;
using HybridSlicer.Infrastructure.Slicing.MultiExtruder;
using Microsoft.Extensions.Logging;

namespace HybridSlicer.Infrastructure.Slicing;

/// <summary>
/// Final pipeline stage: translates G-code from the internal bed-centre reference frame
/// to the machine's real coordinate frame using the machine profile's origin and bed position.
///
/// Internal frame: (0,0) = bed centre (CuraEngine machine_center_is_zero=true)
/// Machine frame:  (0,0) = machine origin (originXMm, originYMm in travel frame)
///
/// Translation:
///   bed centre in travel frame = (bedPositionX + bedWidth/2, bedPositionY + bedDepth/2)
///   bed centre in machine frame = bed_centre_travel - origin
///   G-code offset = bed_centre_machine = (bedPosX + bedW/2 - originX, bedPosY + bedD/2 - originY)
///
/// If origin is at bed centre, offset is (0,0) — no translation needed.
/// If origin is at machine home (0,0) and bed is centred, offset is (bedWidth/2, bedDepth/2).
///
/// This runs AFTER multi-extruder post-processing and custom G-code injection,
/// so all coordinates (including tool-change offsets) get translated uniformly.
/// </summary>
public sealed class MachineCoordinateTranslator : IMachineCoordinateTranslator
{
    private readonly ILogger<MachineCoordinateTranslator> _logger;

    public MachineCoordinateTranslator(ILogger<MachineCoordinateTranslator> logger)
        => _logger = logger;

    public Task TranslateAsync(string gcodePath, MachineProfile machine, CancellationToken ct = default)
        => TranslateAsync(gcodePath, machine, null, ct);

    public async Task TranslateAsync(string gcodePath, MachineProfile machine, int? bedIndex, CancellationToken ct = default)
    {
        // Use per-bed position if bedIndex is specified and beds exist
        double bedPosX = machine.BedPositionXMm, bedPosY = machine.BedPositionYMm;
        double bedW = machine.BedWidthMm, bedD = machine.BedDepthMm;
        if (bedIndex.HasValue)
        {
            var beds = machine.Beds;
            var bi = bedIndex.Value;
            if (bi >= 0 && bi < beds.Count)
            {
                bedPosX = beds[bi].PositionXMm;
                bedPosY = beds[bi].PositionYMm;
                bedW = beds[bi].WidthMm;
                bedD = beds[bi].DepthMm;
                _logger.LogInformation("Using Bed {Bed} position ({X},{Y}) size {W}x{D}",
                    bi + 1, bedPosX, bedPosY, bedW, bedD);
            }
        }

        var dx = bedPosX + bedW / 2.0 - machine.OriginXMm;
        var dy = bedPosY + bedD / 2.0 - machine.OriginYMm;

        // No translation needed if origin is effectively at bed centre
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            _logger.LogDebug("Machine origin is at bed centre — no coordinate translation needed");
            return;
        }

        _logger.LogInformation(
            "Translating G-code to machine coordinates: dX={DX:F3} dY={DY:F3} " +
            "(origin=({OX},{OY}), bed=({BX},{BY}), bed size={BW}x{BD})",
            dx, dy, machine.OriginXMm, machine.OriginYMm,
            machine.BedPositionXMm, machine.BedPositionYMm,
            machine.BedWidthMm, machine.BedDepthMm);

        var ic = CultureInfo.InvariantCulture;
        var input = await File.ReadAllTextAsync(gcodePath, ct);
        var lines = input.Split('\n');
        var sb = new StringBuilder(input.Length + 200);

        sb.AppendLine($"; === Machine coordinate translation ===");
        sb.AppendLine($"; Slicer frame: (0,0) = bed centre");
        sb.AppendLine($"; Machine frame: (0,0) = origin ({machine.OriginXMm}, {machine.OriginYMm})");
        sb.AppendLine($"; Translation: X{dx:+0.000;-0.000} Y{dy:+0.000;-0.000}");
        sb.AppendLine($"; ======================================");
        sb.AppendLine();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            sb.AppendLine(CoordinateOffsetApplicator.OffsetLine(line, dx, dy));
        }

        await File.WriteAllTextAsync(gcodePath, sb.ToString(), ct);

        _logger.LogInformation("Machine coordinate translation complete: {Path}", gcodePath);
    }

    /// <summary>
    /// Remaps axis letters in a G-code file. E.g. if axes = "UVW", then
    /// X→U, Y→V, Z→W in all G0/G1/G28/G92 lines. Only replaces the axis
    /// letter prefix — values are unchanged.
    /// </summary>
    public async Task RemapAxesAsync(string gcodePath, string axes, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(axes) || axes == "XYZ") return;

        var axisX = axes.Length > 0 ? axes[0] : 'X';
        var axisY = axes.Length > 1 ? axes[1] : 'Y';
        var axisZ = axes.Length > 2 ? axes[2] : 'Z';

        if (axisX == 'X' && axisY == 'Y' && axisZ == 'Z') return;

        _logger.LogInformation("Remapping axes: X→{AX} Y→{AY} Z→{AZ} in {Path}",
            axisX, axisY, axisZ, gcodePath);

        var input = await File.ReadAllTextAsync(gcodePath, ct);
        var lines = input.Split('\n');
        var sb = new StringBuilder(input.Length);

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();

            // Only remap G-code motion/position commands
            if (trimmed.Length > 1 && trimmed[0] == 'G' &&
                (trimmed.StartsWith("G0") || trimmed.StartsWith("G1") ||
                 trimmed.StartsWith("G28") || trimmed.StartsWith("G92")))
            {
                var remapped = line
                    .Replace("X", axisX.ToString())
                    .Replace("x", char.ToLower(axisX).ToString())
                    .Replace("Y", axisY.ToString())
                    .Replace("y", char.ToLower(axisY).ToString())
                    .Replace("Z", axisZ.ToString())
                    .Replace("z", char.ToLower(axisZ).ToString());
                sb.AppendLine(remapped);
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        await File.WriteAllTextAsync(gcodePath, sb.ToString(), ct);
        _logger.LogInformation("Axis remapping complete: {Path}", gcodePath);
    }
}
