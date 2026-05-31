using System.Numerics;
using HybridSlicer.Domain.Entities;
using HybridSlicer.Domain.Enums;

namespace HybridSlicer.Infrastructure.Resin;

/// <summary>
/// Advanced support optimization for resin printers.
///
/// Features:
/// - Recoater-aware support placement (top-down printers with blade/roller)
/// - Tall/thin part reinforcement (extra supports for delicate geometry)
/// - Support reduction (remove redundant supports while maintaining safety)
/// - Suction force mitigation (bottom-up printers)
/// </summary>
public static class SupportOptimizer
{
    public sealed record OptimizationConfig
    {
        public bool RecoaterAware { get; init; }
        public double RecoaterSpeedMmPerS { get; init; }
        public double RecoaterClearanceMm { get; init; } = 2.0;
        public string RecoaterType { get; init; } = "blade"; // blade | roller
        public string RecoaterDirection { get; init; } = "X"; // X | Y

        public bool TallThinReinforcement { get; init; } = true;
        public double ThinThresholdMm { get; init; } = 2.0;    // features thinner than this get reinforced
        public double TallThresholdMm { get; init; } = 20.0;   // features taller than this get extra supports

        public bool ReduceSupports { get; init; }
        public double MinSupportSpacingMm { get; init; } = 2.0;
    }

    public sealed record OptimizationResult
    {
        public List<AutoSupportEngine.GeneratedSupport> Supports { get; init; } = [];
        public int OriginalCount { get; init; }
        public int AddedForReinforcement { get; init; }
        public int RemovedForReduction { get; init; }
        public int RecoaterReinforcements { get; init; }
        public List<string> Warnings { get; init; } = [];
    }

    /// <summary>
    /// Optimize an existing support set based on mesh analysis and printer config.
    /// </summary>
    public static OptimizationResult Optimize(
        List<AutoSupportEngine.GeneratedSupport> supports,
        StlMesh mesh,
        MachineProfile printer,
        OptimizationConfig config)
    {
        var result = new List<AutoSupportEngine.GeneratedSupport>(supports);
        int originalCount = result.Count;
        int added = 0, removed = 0, recoaterAdded = 0;
        var warnings = new List<string>();

        // 1. Tall/thin part reinforcement
        if (config.TallThinReinforcement)
        {
            var reinforcements = AnalyzeTallThinFeatures(mesh, config, printer.Orientation);
            foreach (var r in reinforcements)
            {
                // Check not too close to existing support
                bool tooClose = result.Any(s =>
                    Vector2.Distance(new Vector2(s.X, s.Y), new Vector2(r.X, r.Y)) < (float)config.MinSupportSpacingMm);
                if (!tooClose) { result.Add(r); added++; }
            }
            if (reinforcements.Count > 0)
                warnings.Add($"Added {added} reinforcement supports for tall/thin features");
        }

        // 2. Recoater-aware support placement (top-down printers)
        if (config.RecoaterAware && printer.Orientation == PrinterOrientation.TopDown && printer.HasRecoater)
        {
            var recoaterSupports = AnalyzeRecoaterForces(mesh, config, printer);
            foreach (var r in recoaterSupports)
            {
                bool tooClose = result.Any(s =>
                    Vector2.Distance(new Vector2(s.X, s.Y), new Vector2(r.X, r.Y)) < (float)config.MinSupportSpacingMm);
                if (!tooClose) { result.Add(r); recoaterAdded++; }
            }
            if (recoaterAdded > 0)
                warnings.Add($"Added {recoaterAdded} supports for recoater force resistance");
        }

        // 3. Support reduction (remove redundant)
        if (config.ReduceSupports)
        {
            var reduced = ReduceRedundantSupports(result, (float)config.MinSupportSpacingMm);
            removed = result.Count - reduced.Count;
            result = reduced;
            if (removed > 0)
                warnings.Add($"Removed {removed} redundant supports (were too close together)");
        }

        return new OptimizationResult
        {
            Supports = result,
            OriginalCount = originalCount,
            AddedForReinforcement = added,
            RemovedForReduction = removed,
            RecoaterReinforcements = recoaterAdded,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Find tall/thin features that need extra support.
    /// Thin features: narrow cross-sections that may flex.
    /// Tall features: high aspect ratio that may wobble.
    /// </summary>
    private static List<AutoSupportEngine.GeneratedSupport> AnalyzeTallThinFeatures(
        StlMesh mesh, OptimizationConfig config, PrinterOrientation orientation)
    {
        var extras = new List<AutoSupportEngine.GeneratedSupport>();
        float height = mesh.Max.Z - mesh.Min.Z;

        // Sample cross-sections at different heights
        int samples = Math.Min(20, (int)(height / 2));
        for (int s = 1; s < samples; s++)
        {
            float z = mesh.Min.Z + (float)s / samples * height;
            var polygons = MeshCrossSectionEngine.CrossSection(mesh, z);

            foreach (var poly in polygons)
            {
                if (poly.Count < 3) continue;
                // Compute bounding box of the contour
                float pMinX = poly.Min(p => p.X), pMaxX = poly.Max(p => p.X);
                float pMinY = poly.Min(p => p.Y), pMaxY = poly.Max(p => p.Y);
                float width = pMaxX - pMinX;
                float depth = pMaxY - pMinY;
                float minDim = Math.Min(width, depth);

                // If narrow and tall, add reinforcement supports
                if (minDim < config.ThinThresholdMm && z > config.TallThresholdMm)
                {
                    float cx = (pMinX + pMaxX) / 2;
                    float cy = (pMinY + pMaxY) / 2;
                    extras.Add(new AutoSupportEngine.GeneratedSupport
                    {
                        X = cx, Y = cy, ContactZ = z, BaseZ = 0,
                        TipDiameter = 0.5f, ColumnDiameter = 1.0f, BaseDiameter = 1.5f,
                        NormalX = 0, NormalY = 0, NormalZ = -1,
                    });
                }
            }
        }

        return extras;
    }

    /// <summary>
    /// Analyze recoater forces for top-down printers.
    /// The recoater blade/roller sweeps across the build plate, creating lateral forces
    /// on protruding features. Add supports on the trailing edge of features.
    /// </summary>
    private static List<AutoSupportEngine.GeneratedSupport> AnalyzeRecoaterForces(
        StlMesh mesh, OptimizationConfig config, MachineProfile printer)
    {
        var extras = new List<AutoSupportEngine.GeneratedSupport>();
        bool sweepX = config.RecoaterDirection == "X";
        float height = mesh.Max.Z - mesh.Min.Z;

        // For each layer, find the leading edge in the sweep direction and add support
        int samples = Math.Min(10, (int)(height / 5));
        for (int s = 1; s < samples; s++)
        {
            float z = mesh.Min.Z + (float)s / samples * height;
            var polygons = MeshCrossSectionEngine.CrossSection(mesh, z);

            foreach (var poly in polygons)
            {
                if (poly.Count < 3) continue;
                // Find the trailing edge point in sweep direction
                Vector2 trailingPt;
                if (sweepX)
                    trailingPt = poly.OrderByDescending(p => p.X).First();
                else
                    trailingPt = poly.OrderByDescending(p => p.Y).First();

                // If this is an overhang with no support, add one
                float force = (float)(config.RecoaterSpeedMmPerS * 0.1); // rough force estimate
                if (force > 1.0) // significant force
                {
                    extras.Add(new AutoSupportEngine.GeneratedSupport
                    {
                        X = trailingPt.X, Y = trailingPt.Y, ContactZ = z, BaseZ = 0,
                        TipDiameter = 0.4f, ColumnDiameter = 0.9f, BaseDiameter = 1.5f,
                        NormalX = sweepX ? -1 : 0, NormalY = sweepX ? 0 : -1, NormalZ = 0,
                    });
                }
            }
        }

        return extras;
    }

    /// <summary>
    /// Remove supports that are too close to each other (redundant).
    /// Keep the support with the higher contact Z (more important).
    /// </summary>
    private static List<AutoSupportEngine.GeneratedSupport> ReduceRedundantSupports(
        List<AutoSupportEngine.GeneratedSupport> supports, float minSpacing)
    {
        var kept = new List<AutoSupportEngine.GeneratedSupport>();
        var sorted = supports.OrderByDescending(s => s.ContactZ).ToList();

        foreach (var s in sorted)
        {
            bool tooClose = kept.Any(k =>
                Vector2.Distance(new Vector2(k.X, k.Y), new Vector2(s.X, s.Y)) < minSpacing);
            if (!tooClose) kept.Add(s);
        }

        return kept;
    }
}
