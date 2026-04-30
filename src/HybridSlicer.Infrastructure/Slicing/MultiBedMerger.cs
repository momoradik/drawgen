using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace HybridSlicer.Infrastructure.Slicing;

/// <summary>
/// Merges separately-sliced per-bed G-code files into one final print G-code,
/// interleaving layers across beds according to a configurable step size.
///
/// Example with step=5 and 2 beds:
///   Bed1 layers 0–4, Bed2 layers 0–4, Bed1 layers 5–9, Bed2 layers 5–9, ...
///
/// Each bed's G-code is parsed into layer blocks via ;LAYER:N markers.
/// The merge preserves the header from bed 1 and the footer from the last bed.
/// Between bed switches, a comment marker is inserted for traceability.
/// </summary>
public sealed class MultiBedMerger
{
    private readonly ILogger<MultiBedMerger> _logger;

    public MultiBedMerger(ILogger<MultiBedMerger> logger) => _logger = logger;

    /// <summary>
    /// Merges per-bed G-code files into one output file.
    /// </summary>
    /// <param name="bedGCodePaths">Ordered list of per-bed G-code file paths (index = bed number).</param>
    /// <param name="outputPath">Path for the merged output file.</param>
    /// <param name="layerStep">How many layers to print on each bed before switching.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task MergeAsync(
        IReadOnlyList<string> bedGCodePaths,
        string outputPath,
        int layerStep,
        CancellationToken ct = default)
    {
        if (bedGCodePaths.Count == 0) return;
        if (bedGCodePaths.Count == 1)
        {
            // Single bed: just copy the file
            File.Copy(bedGCodePaths[0], outputPath, overwrite: true);
            return;
        }

        _logger.LogInformation("Merging {Count} beds with layer step {Step}", bedGCodePaths.Count, layerStep);

        // Parse each bed's G-code into header, layers, footer
        var beds = new List<ParsedBedGCode>();
        for (var i = 0; i < bedGCodePaths.Count; i++)
        {
            var text = await File.ReadAllTextAsync(bedGCodePaths[i], ct);
            beds.Add(ParseBedGCode(text, i));
            _logger.LogDebug("Bed {Bed}: {Layers} layers parsed", i + 1, beds[i].Layers.Count);
        }

        // Find the max layer count across all beds
        var maxLayers = beds.Max(b => b.Layers.Count);

        var sb = new StringBuilder();

        // ── Header from bed 1 ──────────────────────────────────────────────
        sb.AppendLine("; === Multi-Bed Merged G-code ===");
        sb.AppendLine($"; Beds: {beds.Count}");
        sb.AppendLine($"; Layer step: {layerStep}");
        sb.AppendLine($"; Max layers: {maxLayers}");
        sb.AppendLine("; ================================");
        sb.AppendLine();
        sb.Append(beds[0].Header);

        // ── Interleaved layers ─────────────────────────────────────────────
        var layerIndex = 0;
        while (layerIndex < maxLayers)
        {
            var endLayer = Math.Min(layerIndex + layerStep, maxLayers);

            for (var bi = 0; bi < beds.Count; bi++)
            {
                var bed = beds[bi];
                var hasLayers = false;

                for (var l = layerIndex; l < endLayer && l < bed.Layers.Count; l++)
                {
                    if (!hasLayers)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"; --- Bed {bi + 1}, layers {layerIndex}–{endLayer - 1} ---");
                        hasLayers = true;
                    }
                    sb.Append(bed.Layers[l]);
                }
            }

            layerIndex = endLayer;
        }

        // ── Footer from last bed ───────────────────────────────────────────
        sb.AppendLine();
        sb.Append(beds[^1].Footer);

        // Ensure output directory exists
        var outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outDir)) Directory.CreateDirectory(outDir);

        await File.WriteAllTextAsync(outputPath, sb.ToString(), ct);

        _logger.LogInformation("Multi-bed merge complete: {Path} ({Beds} beds, {Layers} max layers, step {Step})",
            outputPath, beds.Count, maxLayers, layerStep);
    }

    // ── Parsing ─────────────────────────────────────────────────────────────

    private sealed class ParsedBedGCode
    {
        public required string Header { get; init; }
        public required List<string> Layers { get; init; } // each entry = full text of one layer
        public required string Footer { get; init; }
        public required int BedIndex { get; init; }
    }

    private static readonly Regex LayerRx = new(@"^;LAYER:(\d+)", RegexOptions.Multiline);

    private static ParsedBedGCode ParseBedGCode(string gcode, int bedIndex)
    {
        var header = new StringBuilder();
        var layers = new List<string>();
        var footer = new StringBuilder();
        var current = new StringBuilder();
        var inHeader = true;
        var inFooter = false;

        foreach (var rawLine in gcode.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            // Detect footer (M84 or ;End of Gcode)
            if (!inHeader && !inFooter)
            {
                var t = line.TrimStart();
                if (t.StartsWith("M84", StringComparison.OrdinalIgnoreCase)
                    || t.StartsWith(";End of Gcode", StringComparison.OrdinalIgnoreCase))
                {
                    // Flush current layer
                    if (current.Length > 0)
                        layers.Add(current.ToString());
                    current.Clear();
                    inFooter = true;
                    footer.AppendLine(line);
                    continue;
                }
            }

            if (inFooter) { footer.AppendLine(line); continue; }

            // Detect first layer
            if (inHeader && line.StartsWith(";LAYER:", StringComparison.Ordinal))
            {
                inHeader = false;
                header.Append(current.ToString());
                current.Clear();
                current.AppendLine(line);
                continue;
            }

            if (inHeader) { current.AppendLine(line); continue; }

            // Detect next layer
            if (line.StartsWith(";LAYER:", StringComparison.Ordinal))
            {
                if (current.Length > 0)
                    layers.Add(current.ToString());
                current.Clear();
                current.AppendLine(line);
                continue;
            }

            current.AppendLine(line);
        }

        // Flush
        if (!inFooter && current.Length > 0)
            layers.Add(current.ToString());

        return new ParsedBedGCode
        {
            Header = header.ToString(),
            Layers = layers,
            Footer = footer.ToString(),
            BedIndex = bedIndex,
        };
    }
}
