using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
using Gsa.Missions.Contracts;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Gameplay;

/// <summary>Survey grid on the south-facing vertical surfaces selected by the contract.</summary>
public sealed class CityPhotoCoverage
{
    public const string ObservationClass = "survey_surface_cell";
    public const double CellSizeMetres = 2;
    private const double SurfaceOffsetMetres = .02;

    private sealed record Cell(string Id, string TargetId, double North, Bounds2DEnu PlaneBounds, Vector3d Offset);
    private readonly Dictionary<string, Cell> _cells;
    public SurveySurfaceReference Reference { get; }

    public CityPhotoCoverage(string surfaceId, IEnumerable<WorldBoxObject> targets)
    {
        var faces = targets.OrderBy(target => target.PositionEnu.X).ToArray();
        var exclusions = new List<Bounds2DEnu>();
        var cells = new List<Cell>();
        // Pack faces on an E/elevation grid, with no useful area between separate objects.
        // Padding keeps 2 m cells aligned even when a bench face is shorter than a cell.
        var first = faces[0];
        var bottom = first.PositionEnu.Z - first.SizeEnu.Z / 2;
        var height = Math.Ceiling(faces.Max(face => face.SizeEnu.Z) / CellSizeMetres) * CellSizeMetres;
        var start = first.PositionEnu.X - first.SizeEnu.X / 2;
        var atlasEast = start;
        foreach (var face in faces)
        {
            var paddedWidth = Math.Ceiling(face.SizeEnu.X / CellSizeMetres) * CellSizeMetres;
            if (paddedWidth > face.SizeEnu.X)
                exclusions.Add(new(atlasEast + face.SizeEnu.X, bottom, atlasEast + paddedWidth, bottom + height));
            if (height > face.SizeEnu.Z)
                exclusions.Add(new(atlasEast, bottom + face.SizeEnu.Z, atlasEast + face.SizeEnu.X, bottom + height));

            var offset = new Vector3d(face.PositionEnu.X - face.SizeEnu.X / 2 - atlasEast, 0,
                face.PositionEnu.Z - face.SizeEnu.Z / 2 - bottom);
            var north = face.PositionEnu.Y - face.SizeEnu.Y / 2 - SurfaceOffsetMetres;
            for (var column = 0; column * CellSizeMetres < face.SizeEnu.X; column++)
            {
                var u = column * CellSizeMetres;
                var right = Math.Min(u + CellSizeMetres, face.SizeEnu.X);
                for (var row = 0; row * CellSizeMetres < face.SizeEnu.Z; row++)
                {
                    var v = row * CellSizeMetres;
                    var top = Math.Min(v + CellSizeMetres, face.SizeEnu.Z);
                    cells.Add(new($"coverage/{face.Id}/{column}/{row}", face.Id, north,
                        new(atlasEast + u, bottom + v, atlasEast + right, bottom + top), offset));
                }
            }
            atlasEast += paddedWidth;
        }

        _cells = cells.ToDictionary(cell => cell.Id, StringComparer.Ordinal);
        Reference = new(surfaceId, new(start, bottom, atlasEast, bottom + height), first.PositionEnu.Z, exclusions);
    }

    public IReadOnlyList<ObservedFeature2D> Observe(
        RigidTransform cameraToMap, CameraSensorProfile profile, Func<Vector3d, Vector3d, string, bool> isOccluded)
    {
        var projector = new CameraProjector(profile.Intrinsics);
        var worldToCamera = cameraToMap.Inverse();
        var origin = cameraToMap.Translation;
        var visible = new List<ObservedFeature2D>();

        foreach (var cell in _cells.Values)
        {
            if (origin.Y >= cell.North) continue;
            var b = cell.PlaneBounds;
            var offset = cell.Offset;
            var center = new Vector3d((b.MinEast + b.MaxEast) / 2, cell.North, (b.MinNorth + b.MaxNorth) / 2) + offset;
            var cameraCenter = worldToCamera.Transform(center);
            if (cameraCenter.Z <= 0 || cameraCenter.Z > profile.RangeMetres ||
                projector.ProjectCameraPoint(cameraCenter).Pixel is not { } pixel)
                continue;

            Vector3d[] corners =
            [
                new Vector3d(b.MinEast, cell.North, b.MinNorth) + offset,
                new Vector3d(b.MaxEast, cell.North, b.MinNorth) + offset,
                new Vector3d(b.MinEast, cell.North, b.MaxNorth) + offset,
                new Vector3d(b.MaxEast, cell.North, b.MaxNorth) + offset,
            ];
            var pixels = corners.Select(corner => projector.ProjectCameraPoint(worldToCamera.Transform(corner))).ToArray();
            if (pixels.Any(result => !result.IsVisible)) continue;
            var minU = pixels.Min(result => result.Pixel!.Value.U);
            var maxU = pixels.Max(result => result.Pixel!.Value.U);
            var minV = pixels.Min(result => result.Pixel!.Value.V);
            var maxV = pixels.Max(result => result.Pixel!.Value.V);
            var size = Math.Min(maxU - minU, maxV - minV);
            if (size < profile.MinProjectedFeaturePixels || isOccluded(origin, center, cell.TargetId) ||
                corners.Any(corner => isOccluded(origin, center + (corner - center) * .9, cell.TargetId)))
                continue;

            visible.Add(new(cell.Id, ObservationClass, pixel.U, pixel.V, minU, minV, maxU, maxV,
                size, 5, 5, 0, false));
        }
        return visible;
    }

    public IEnumerable<SampleFootprintObservation> ReadFootprints(IEnumerable<CameraObservationSample> samples)
    {
        foreach (var sample in samples)
            foreach (var feature in sample.ObservedFeatures)
                if (feature.ObjectClass == ObservationClass && _cells.TryGetValue(feature.TargetId, out var cell))
                    yield return new(sample.SampleId, Reference.SurfaceId, cell.PlaneBounds);
    }

    public IReadOnlyList<string> MissingTargets(IEnumerable<SampleFootprintObservation> footprints)
    {
        var covered = footprints.Select(footprint => footprint.CoveredFootprint).ToHashSet();
        return _cells.Values.Where(cell => !covered.Contains(cell.PlaneBounds))
            .Select(cell => cell.TargetId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public static CityPhotoCoverage? ForContract(ContractDefinition definition, IReadOnlyDictionary<string, WorldBoxObject> manifest)
    {
        if (definition.Requirements.OfType<ThresholdRequirementSpec>()
            .FirstOrDefault(rule => rule.Metric == "area_coverage")?.SurfaceId is not { } surfaceId)
            return null;
        return new(surfaceId, definition.RequiredFeatureIds.Select(id => manifest[id]));
    }
}
