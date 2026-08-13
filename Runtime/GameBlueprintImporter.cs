using MelonLoader;
using S1API.Building;
using S1API.Items;
using ScheduleOneBlueprintImporter.Blueprints;
using UnityEngine;
#if IL2CPPMELON
using NativeCoordinate = Il2CppScheduleOne.Tiles.Coordinate;
using NativeBuildManager = Il2CppScheduleOne.Building.BuildManager;
using NativeCoordinateProceduralTilePair = Il2CppScheduleOne.Tiles.CoordinateProceduralTilePair;
using NativeFloorRack = Il2CppScheduleOne.ObjectScripts.FloorRack;
using NativeGrid = Il2CppScheduleOne.Tiles.Grid;
using NativePairList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Tiles.CoordinateProceduralTilePair>;
using NativeProperty = Il2CppScheduleOne.Property.Property;
using NativeRegistry = Il2CppScheduleOne.Registry;
#else
using NativeCoordinate = ScheduleOne.Tiles.Coordinate;
using NativeBuildManager = ScheduleOne.Building.BuildManager;
using NativeCoordinateProceduralTilePair = ScheduleOne.Tiles.CoordinateProceduralTilePair;
using NativeFloorRack = ScheduleOne.ObjectScripts.FloorRack;
using NativeGrid = ScheduleOne.Tiles.Grid;
using NativePairList = System.Collections.Generic.List<ScheduleOne.Tiles.CoordinateProceduralTilePair>;
using NativeProperty = ScheduleOne.Property.Property;
using NativeRegistry = ScheduleOne.Registry;
#endif

namespace ScheduleOneBlueprintImporter.Runtime;

internal sealed class GameBlueprintImporter
{
    private static readonly IReadOnlyDictionary<string, string[]> PropertyAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["motel"] = new[] { "motel", "motelroom" },
            ["sweatshop"] = new[] { "sweatshop" },
            ["bungalow"] = new[] { "bungalow" },
            ["barn"] = new[] { "barn" },
            ["warehouse"] = new[] { "warehouse", "dockswarehouse" },
            ["stash_and_dash"] = new[] { "stashanddash" },
            ["manor"] = new[] { "manor" },
            ["sewer_office"] = new[] { "seweroffice" },
        };

    private readonly MelonLogger.Instance _logger;

    internal GameBlueprintImporter(MelonLogger.Instance logger) => _logger = logger;

    internal bool TryImport(
        ImportPlan plan,
        Guid shareId,
        IReadOnlyList<BlueprintFloor> floors,
        out string error)
    {
        error = string.Empty;
        if (S1API.Money.Money.GetOnlineBalance() < (float)plan.TotalCost)
            return Fail($"Online balance is below the required ${plan.TotalCost:0.00}.", out error);

        if (!TryFindOwnedProperty(plan.PropertyType, out NativeProperty? property))
            return Fail($"No owned property matches website type '{plan.PropertyType}'.", out error);

        if (!TryMapGridRegions(property!, floors, plan.Placements, out IReadOnlyList<GridRegion>? regions, out error))
            return false;

        var runtimePlacements = new List<RuntimePlacement>();
        foreach (IGrouping<int, GridRegion> floorRegions in regions!.GroupBy(region => region.FloorIndex))
        {
            if (!TryOrientRegions(floorRegions.ToList(), out List<RuntimePlacement>? oriented))
            {
                return Fail(
                    $"Floor {floorRegions.Key + 1} regions could not be aligned with the native grids " +
                    "without using blocked tiles.",
                    out error);
            }
            runtimePlacements.AddRange(oriented!);
        }

        foreach (PlannedPlacement placement in plan.Placements)
        {
            ItemDefinition? definition = ResolveDefinition(placement.Item);
            if (definition is not S1API.Items.Buildable.BuildableItemDefinition)
                return Fail(
                    $"Game item '{placement.Item.GameId}' is missing or is not an ordinary grid-buildable item.",
                    out error);
            if (!ValidatePotComponents(placement.Source.PotConfiguration, out error))
                return false;
        }

        int created = 0;
        foreach (RuntimePlacement runtime in runtimePlacements)
        {
            ItemDefinition definition = ResolveDefinition(runtime.Placement.Item)!;
            GameObject? rackObject = null;
            if (runtime.Placement.Source.PotConfiguration?.Extra != null)
            {
                ItemDefinition rackDefinition = ItemManager.GetDefinition("suspensionrack")!;
                rackObject = BuildManager.CreateGridItem(
                    rackDefinition.CreateInstance(),
                    runtime.Grid!,
                    new Vector2(runtime.OriginX, runtime.OriginY),
                    runtime.Rotation,
                    Guid.NewGuid().ToString());
                created++;
            }
            BuildManager.CreateGridItem(
                definition.CreateInstance(),
                runtime.Grid!,
                new Vector2(runtime.OriginX, runtime.OriginY),
                runtime.Rotation,
                Guid.NewGuid().ToString());
            created++;

            BlueprintComponent? light = runtime.Placement.Source.PotConfiguration?.Light;
            if (light != null)
            {
                string lightError = "A grow light requires a created suspension rack.";
                if (rackObject == null || !TryCreateGrowLight(rackObject, light.Id, out lightError))
                    return Fail(lightError, out error);
                created++;
            }
        }

        if (created != plan.ExpectedNativeObjectCount)
            return Fail("The game did not create every planned item; no charge was applied.", out error);

        S1API.Money.Money.CreateOnlineTransaction(
            "Blueprint import",
            -(float)plan.TotalCost,
            1f,
            $"scheduleoneeditor.com share {shareId}");
        _logger.Msg($"Placed blueprint on native grid(s) for property '{property!.PropertyName}'.");
        return true;
    }

    private static bool TryFindOwnedProperty(string websiteType, out NativeProperty? property)
    {
        property = null;
        if (!PropertyAliases.TryGetValue(websiteType, out string[]? aliases))
            aliases = new[] { Normalize(websiteType) };

        foreach (NativeProperty candidate in NativeProperty.OwnedProperties)
        {
            string code = Normalize(candidate.PropertyCode);
            string name = Normalize(candidate.PropertyName);
            if (aliases.Any(alias => code == alias || name == alias))
            {
                property = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryMapGridRegions(
        NativeProperty property,
        IReadOnlyList<BlueprintFloor> floors,
        IReadOnlyList<PlannedPlacement> placements,
        out IReadOnlyList<GridRegion>? regions,
        out string error)
    {
        error = string.Empty;
        var remaining = new List<NativeGrid>();
        foreach (NativeGrid grid in property.Grids)
            remaining.Add(grid);
        remaining.Sort((left, right) =>
        {
            int heightComparison = left.Origin.y.CompareTo(right.Origin.y);
            return heightComparison != 0
                ? heightComparison
                : string.Compare(left.name, right.name, StringComparison.Ordinal);
        });
        var mapped = new List<GridRegion>();

        for (int floorIndex = 0; floorIndex < floors.Count; floorIndex++)
        {
            BlueprintFloor floor = floors[floorIndex];
            List<PlannedPlacement> floorPlacements = placements
                .Where(placement => placement.FloorIndex == floorIndex)
                .ToList();
            List<NativeGrid> candidates = remaining
                .Where(grid =>
                    (grid.Width == floor.Width - 2 && grid.Height == floor.Height - 2) ||
                    (grid.Width == floor.Height - 2 && grid.Height == floor.Width - 2))
                .ToList();
            if (candidates.Count != 1)
            {
                if (TryMapCroppedFloor(floorIndex, floor, floorPlacements, remaining, out GridRegion? cropped))
                {
                    mapped.Add(cropped!);
                    remaining.Remove(cropped!.Grid);
                    continue;
                }
                if (TryMapSegmentedFloor(floorIndex, floor, floorPlacements, remaining, out List<GridRegion>? segments))
                {
                    mapped.AddRange(segments!);
                    foreach (GridRegion segment in segments!)
                        remaining.Remove(segment.Grid);
                    continue;
                }
                if (TryMapCompositeFloor(floorIndex, floor, floorPlacements, remaining, out List<GridRegion>? composite))
                {
                    mapped.AddRange(composite!);
                    foreach (GridRegion segment in composite!)
                        remaining.Remove(segment.Grid);
                    continue;
                }

                regions = null;
                string available = string.Join(", ", remaining.Select(grid => $"{grid.name}:{grid.Width}x{grid.Height}"));
                error =
                    $"Floor {floorIndex + 1} ({floor.Width}x{floor.Height}) could not be mapped to native grids. " +
                    $"Available: [{available}].";
                return false;
            }

            mapped.Add(new GridRegion(
                floorIndex, 0, 0, floor.Width - 2, floor.Height - 2, candidates[0], floorPlacements));
            remaining.Remove(candidates[0]);
        }

        regions = mapped;
        return true;
    }

    private static bool TryMapCroppedFloor(
        int floorIndex,
        BlueprintFloor floor,
        IReadOnlyList<PlannedPlacement> placements,
        IReadOnlyList<NativeGrid> available,
        out GridRegion? region)
    {
        region = null;
        int innerWidth = floor.Width - 2;
        int innerHeight = floor.Height - 2;
        var matches = new List<(
            NativeGrid Grid, int OffsetX, int OffsetY, int Width, int Height, int Slack, int BuildableCells)>();

        foreach (NativeGrid grid in available)
        {
            var dimensions = new List<(int Width, int Height)> { (grid.Width, grid.Height) };
            if (grid.Width != grid.Height)
                dimensions.Add((grid.Height, grid.Width));

            foreach (var dimension in dimensions)
            {
                if (dimension.Width > innerWidth || dimension.Height > innerHeight ||
                    (dimension.Width != innerWidth && dimension.Height != innerHeight))
                {
                    continue;
                }

                for (int offsetY = 0; offsetY <= innerHeight - dimension.Height; offsetY++)
                {
                    for (int offsetX = 0; offsetX <= innerWidth - dimension.Width; offsetX++)
                    {
                        bool containsEveryPlacement = placements.All(placement =>
                            placement.GridX >= offsetX && placement.GridY >= offsetY &&
                            placement.GridX + placement.Source.Width <= offsetX + dimension.Width &&
                            placement.GridY + placement.Source.Height <= offsetY + dimension.Height);
                        if (!containsEveryPlacement)
                            continue;

                        int buildableCells = 0;
                        for (int y = offsetY + 1; y < offsetY + dimension.Height + 1; y++)
                        {
                            for (int x = offsetX + 1; x < offsetX + dimension.Width + 1; x++)
                            {
                                if (!string.Equals(floor.Blueprint[y][x], "-1", StringComparison.Ordinal))
                                    buildableCells++;
                            }
                        }
                        int slack = innerWidth - dimension.Width + innerHeight - dimension.Height;
                        matches.Add((
                            grid, offsetX, offsetY, dimension.Width, dimension.Height, slack, buildableCells));
                    }
                }
            }
        }

        var best = matches
            .OrderBy(match => match.Slack)
            .ThenByDescending(match => match.BuildableCells)
            .ThenBy(match => match.Grid.name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (best.Grid == null)
            return false;

        List<PlannedPlacement> localPlacements = placements
            .Select(placement => placement with
            {
                GridX = placement.GridX - best.OffsetX,
                GridY = placement.GridY - best.OffsetY,
                OriginX = placement.OriginX - best.OffsetX,
                OriginY = placement.OriginY - best.OffsetY,
            })
            .ToList();
        region = new GridRegion(
            floorIndex, best.OffsetX, best.OffsetY, best.Width, best.Height, best.Grid, localPlacements);
        return true;
    }

    private static bool TryMapSegmentedFloor(
        int floorIndex,
        BlueprintFloor floor,
        IReadOnlyList<PlannedPlacement> placements,
        IReadOnlyList<NativeGrid> available,
        out List<GridRegion>? regions)
    {
        regions = null;
        if (available.Count < 2)
            return false;
        int gridWidth = available[0].Width;
        int gridHeight = available[0].Height;
        if (available.Any(grid => grid.Width != gridWidth || grid.Height != gridHeight))
            return false;
        int innerWidth = floor.Width - 2;
        int innerHeight = floor.Height - 2;
        if (innerWidth % gridWidth != 0 || innerHeight % gridHeight != 0)
            return false;

        int columns = innerWidth / gridWidth;
        int rows = innerHeight / gridHeight;
        var segments = new List<(int Column, int Row)>();
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int buildableCells = 0;
                for (int y = 1 + row * gridHeight; y < 1 + (row + 1) * gridHeight; y++)
                {
                    for (int x = 1 + column * gridWidth; x < 1 + (column + 1) * gridWidth; x++)
                    {
                        if (!string.Equals(floor.Blueprint[y][x], "-1", StringComparison.Ordinal))
                            buildableCells++;
                    }
                }
                if (buildableCells > gridWidth * gridHeight / 2)
                    segments.Add((column, row));
            }
        }
        if (segments.Count != available.Count)
            return false;

        int[] assignment = FindBestGridAssignment(segments, available);
        var result = new List<GridRegion>();
        for (int index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            int offsetX = segment.Column * gridWidth;
            int offsetY = segment.Row * gridHeight;
            List<PlannedPlacement> localPlacements = placements
                .Where(placement =>
                    placement.GridX >= offsetX && placement.GridX < offsetX + gridWidth &&
                    placement.GridY >= offsetY && placement.GridY < offsetY + gridHeight)
                .Select(placement => placement with
                {
                    GridX = placement.GridX - offsetX,
                    GridY = placement.GridY - offsetY,
                    OriginX = placement.OriginX - offsetX,
                    OriginY = placement.OriginY - offsetY,
                })
                .ToList();
            if (localPlacements.Any(placement =>
                    placement.GridX + placement.Source.Width > gridWidth ||
                    placement.GridY + placement.Source.Height > gridHeight))
            {
                return false;
            }
            result.Add(new GridRegion(
                floorIndex, segment.Column, segment.Row, gridWidth, gridHeight,
                available[assignment[index]], localPlacements));
        }
        if (result.Sum(region => region.Placements.Count) != placements.Count)
            return false;

        regions = result;
        return true;
    }

    private static bool TryMapCompositeFloor(
        int floorIndex,
        BlueprintFloor floor,
        IReadOnlyList<PlannedPlacement> placements,
        IReadOnlyList<NativeGrid> available,
        out List<GridRegion>? regions)
    {
        var shapes = available
            .Select((grid, index) =>
            {
                var first = grid.GetTile(new NativeCoordinate(0, 0));
                var last = grid.GetTile(new NativeCoordinate(grid.Width - 1, grid.Height - 1));
                Vector3 center = first != null && last != null
                    ? (first.transform.position + last.transform.position) / 2f
                    : grid.Origin;
                return new GridShape(index, grid.Width, grid.Height, center.x, center.z);
            })
            .ToList();
        if (!GridRegionLayoutPlanner.TryCreateCompositeLayout(
                floor, placements, shapes, out IReadOnlyList<GridRegionLayout>? layouts))
        {
            regions = null;
            return false;
        }

        regions = layouts!
            .Select(layout => new GridRegion(
                floorIndex,
                layout.OffsetX,
                layout.OffsetY,
                layout.Width,
                layout.Height,
                available[layout.GridIndex],
                layout.Placements))
            .ToList();
        return true;
    }

    private static int[] FindBestGridAssignment(
        IReadOnlyList<(int Column, int Row)> segments,
        IReadOnlyList<NativeGrid> grids)
    {
        int count = segments.Count;
        int[] current = Enumerable.Range(0, count).ToArray();
        int[] best = current.ToArray();
        double bestScore = double.MaxValue;
        double physicalStep = double.MaxValue;
        for (int left = 0; left < count; left++)
        {
            for (int right = left + 1; right < count; right++)
            {
                double distance = Vector3.Distance(grids[left].Origin, grids[right].Origin);
                if (distance > 0.01 && distance < physicalStep)
                    physicalStep = distance;
            }
        }
        if (physicalStep == double.MaxValue)
            physicalStep = 1d;

        void Search(int position)
        {
            if (position == count)
            {
                double score = 0d;
                for (int left = 0; left < count; left++)
                {
                    for (int right = left + 1; right < count; right++)
                    {
                        double dx = segments[left].Column - segments[right].Column;
                        double dy = segments[left].Row - segments[right].Row;
                        double expected = Math.Sqrt(dx * dx + dy * dy);
                        double actual = Vector3.Distance(
                            grids[current[left]].Origin,
                            grids[current[right]].Origin) / physicalStep;
                        double delta = expected - actual;
                        score += delta * delta;
                    }
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = current.ToArray();
                }
                return;
            }

            for (int swap = position; swap < count; swap++)
            {
                (current[position], current[swap]) = (current[swap], current[position]);
                Search(position + 1);
                (current[position], current[swap]) = (current[swap], current[position]);
            }
        }

        Search(0);
        return best;
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static ItemDefinition? ResolveDefinition(SupportedItem item)
    {
        ItemDefinition? exact = ItemManager.GetDefinition(item.GameId);
        if (exact != null)
            return exact;

        string normalizedWebsiteId = Normalize(item.WebsiteId);
        List<ItemDefinition> candidates = ItemManager.GetAllItemDefinitions()
            .Where(definition => Normalize(definition.ID) == normalizedWebsiteId)
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static bool ValidatePotComponents(BlueprintPotConfiguration? config, out string error)
    {
        error = string.Empty;
        if (config == null)
            return true;
        if (config.Extra != null && !string.Equals(config.Extra.Id, "suspension-rack", StringComparison.OrdinalIgnoreCase))
            return Fail($"Pot setup has unsupported extra '{config.Extra.Id}'.", out error);
        if (config.Extra != null && ItemManager.GetDefinition("suspensionrack") is not S1API.Items.Buildable.BuildableItemDefinition)
            return Fail("Game item 'suspensionrack' is missing or not grid-buildable.", out error);
        if (config.Light != null)
        {
            if (config.Extra == null)
                return Fail("A grow light requires the pot setup's suspension rack.", out error);
            string lightId = ResolveGrowLightId(config.Light.Id);
            if (string.IsNullOrEmpty(lightId) || NativeRegistry.GetItem(lightId) == null)
                return Fail($"Pot setup has unsupported grow light '{config.Light.Id}'.", out error);
        }
        return true;
    }

    private static bool TryCreateGrowLight(GameObject rackObject, string websiteLightId, out string error)
    {
        error = string.Empty;
        NativeFloorRack? rack = rackObject.GetComponent<NativeFloorRack>();
        if (rack == null)
            return Fail("Created suspension rack has no FloorRack component.", out error);
        string gameLightId = ResolveGrowLightId(websiteLightId);
        var definition = NativeRegistry.GetItem(gameLightId);
        if (definition == null)
            return Fail($"Game item '{gameLightId}' is missing.", out error);

        var matches = new NativePairList();
        for (int index = 0; index < rack.procTiles.Count; index++)
        {
            matches.Add(new NativeCoordinateProceduralTilePair
            {
                coord = new NativeCoordinate(index % 2, index / 2),
                tileParent = rack.NetworkObject,
                tileIndex = index,
            });
        }
        if (matches.Count != 4)
            return Fail($"Suspension rack exposed {matches.Count} procedural tiles; expected 4.", out error);

        var created = NativeBuildManager.Instance.CreateProceduralGridItem(
            definition.GetDefaultInstance(1),
            0,
            matches,
            Guid.NewGuid().ToString());
        return created != null || Fail("Native grow-light creation returned null.", out error);
    }

    private static string ResolveGrowLightId(string websiteId) => websiteId switch
    {
        "halogen-grow-light" => "halogengrowlight",
        "led-grow-light" => "ledgrowlight",
        "full-spectrum-grow-light" => "fullspectrumgrowlight",
        _ => string.Empty,
    };

    private static bool TryOrientFloor(
        NativeGrid grid,
        IReadOnlyList<PlannedPlacement> placements,
        out List<RuntimePlacement>? result)
    {
        var orientations = new[]
        {
            (Swap: false, FlipX: false, FlipY: true),
            (Swap: false, FlipX: false, FlipY: false),
            (Swap: false, FlipX: true, FlipY: true),
            (Swap: false, FlipX: true, FlipY: false),
            (Swap: true, FlipX: false, FlipY: true),
            (Swap: true, FlipX: false, FlipY: false),
            (Swap: true, FlipX: true, FlipY: true),
            (Swap: true, FlipX: true, FlipY: false),
        };

        foreach (var orientation in orientations)
        {
            var occupied = new HashSet<(int X, int Y)>();
            var candidate = new List<RuntimePlacement>();
            bool valid = true;
            foreach (PlannedPlacement placement in placements)
            {
                int baseX = orientation.Swap ? placement.GridY : placement.GridX;
                int baseY = orientation.Swap ? placement.GridX : placement.GridY;
                int rectangleWidth = orientation.Swap ? placement.Source.Height : placement.Source.Width;
                int rectangleHeight = orientation.Swap ? placement.Source.Width : placement.Source.Height;
                int x = orientation.FlipX
                    ? grid.Width - baseX - rectangleWidth
                    : baseX;
                int y = orientation.FlipY
                    ? grid.Height - baseY - rectangleHeight
                    : baseY;
                for (int tileX = x; tileX < x + rectangleWidth && valid; tileX++)
                {
                    for (int tileY = y; tileY < y + rectangleHeight; tileY++)
                    {
                        var tile = grid.GetTile(new NativeCoordinate(tileX, tileY));
                        if (!occupied.Add((tileX, tileY)) || tile == null ||
                            tile.BuildableOccupants.Count != 0 || !tile.CanBeBuiltOn())
                        {
                            valid = false;
                            break;
                        }
                    }
                }

                if (!valid)
                    break;

                int rotation = rectangleWidth == placement.Item.Width ? 0 : 90;
                int originY = rotation == 90 ? y + placement.Item.Width - 1 : y;
                candidate.Add(new RuntimePlacement(placement, x, originY, rotation));
            }

            if (valid)
            {
                result = candidate;
                return true;
            }
        }

        result = null;
        return false;
    }

    private static bool TryOrientRegions(
        IReadOnlyList<GridRegion> regions,
        out List<RuntimePlacement>? result)
    {
        int count = regions.Count;
        int[] assignment = Enumerable.Range(0, count).ToArray();
        List<RuntimePlacement>? match = null;

        bool Search(int position)
        {
            if (position == count)
            {
                var candidate = new List<RuntimePlacement>();
                for (int regionIndex = 0; regionIndex < count; regionIndex++)
                {
                    NativeGrid grid = regions[assignment[regionIndex]].Grid;
                    GridRegion region = regions[regionIndex];
                    if (!((grid.Width == region.Width && grid.Height == region.Height) ||
                          (grid.Width == region.Height && grid.Height == region.Width)))
                    {
                        return false;
                    }
                    if (!TryOrientFloor(grid, region.Placements, out List<RuntimePlacement>? oriented))
                        return false;
                    candidate.AddRange(oriented!.Select(item => item with { Grid = grid }));
                }
                match = candidate;
                return true;
            }

            for (int swap = position; swap < count; swap++)
            {
                (assignment[position], assignment[swap]) = (assignment[swap], assignment[position]);
                if (Search(position + 1))
                    return true;
                (assignment[position], assignment[swap]) = (assignment[swap], assignment[position]);
            }
            return false;
        }

        bool found = Search(0);
        result = match;
        return found;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}

internal sealed record RuntimePlacement(
    PlannedPlacement Placement,
    int OriginX,
    int OriginY,
    int Rotation,
    NativeGrid? Grid = null);

internal sealed record GridRegion(
    int FloorIndex,
    int Column,
    int Row,
    int Width,
    int Height,
    NativeGrid Grid,
    IReadOnlyList<PlannedPlacement> Placements);
