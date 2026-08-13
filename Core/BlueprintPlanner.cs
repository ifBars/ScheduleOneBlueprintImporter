namespace ScheduleOneBlueprintImporter.Blueprints;

public static class BlueprintPlanner
{
    public static bool TryCreatePlan(BlueprintDocument document, out ImportPlan? plan, out string error)
    {
        plan = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(document.Type))
            return Fail("Blueprint has no property type.", out error);
        if (document.Floors.Count == 0)
            return Fail("Blueprint has no floors.", out error);

        var placements = new List<PlannedPlacement>();
        decimal total = 0m;

        for (int floorIndex = 0; floorIndex < document.Floors.Count; floorIndex++)
        {
            BlueprintFloor floor = document.Floors[floorIndex];
            if (floor.Width <= 0 || floor.Height <= 0 || floor.Blueprint.Any(row => row.Count != floor.Width))
                return Fail($"Floor {floorIndex + 1} is not a rectangular grid.", out error);

            foreach (BlueprintItem source in floor.PlacedItems)
            {
                if (!TryResolveItem(source, out SupportedItem item, out error))
                    return false;
                if (!WebsiteItemCatalog.TryGet(source.ItemTypeId, out _) && source.PotConfiguration == null)
                    return Fail($"Website item '{source.ItemTypeId}' is not in the supported grid-item catalog.", out error);
                if (source.Price < 0m || source.Price > 1_000_000m)
                    return Fail($"'{source.ItemTypeId}' has an invalid price ({source.Price}).", out error);

                int rotation;
                int gridX = source.X - 1;
                int gridY = source.Y - 1;
                int originX = gridX;
                int originY = gridY;
                if (source.Width == item.Width && source.Height == item.Height)
                {
                    rotation = 0;
                }
                else if (source.Width == item.Height && source.Height == item.Width)
                {
                    rotation = 90;
                    originY += item.Width - 1;
                }
                else
                {
                    return Fail(
                        $"'{source.ItemTypeId}' has footprint {source.Width}x{source.Height}; expected " +
                        $"{item.Width}x{item.Height} or {item.Height}x{item.Width}.", out error);
                }

                if (source.X < 1 || source.Y < 1 ||
                    source.X + source.Width > floor.Width - 1 ||
                    source.Y + source.Height > floor.Height - 1)
                {
                    return Fail($"'{source.ItemTypeId}' extends outside floor {floorIndex + 1}.", out error);
                }

                placements.Add(new PlannedPlacement(
                    source, item, floorIndex, gridX, gridY, originX, originY, rotation));
                total += source.Price;
            }
        }

        if (placements.Count == 0)
            return Fail("Blueprint contains no supported placed items.", out error);

        plan = new ImportPlan(document.Type, placements, total);
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    private static bool TryResolveItem(BlueprintItem source, out SupportedItem item, out string error)
    {
        error = string.Empty;
        if (!string.Equals(source.ItemTypeId, "pot", StringComparison.OrdinalIgnoreCase))
        {
            if (WebsiteItemCatalog.TryGet(source.ItemTypeId, out item!))
                return true;
            item = null!;
            return Fail($"Website item '{source.ItemTypeId}' is not in the supported grid-item catalog.", out error);
        }

        string? websitePotId = source.PotConfiguration?.Pot?.Id;
        string gameId = websitePotId switch
        {
            "air-pot" => "airpot",
            "plastic-pot" => "plasticpot",
            "moisture-preserving-pot" => "moisturepreservingpot",
            _ => string.Empty,
        };
        if (string.IsNullOrEmpty(gameId))
        {
            item = null!;
            return Fail($"Pot setup has unsupported pot id '{websitePotId ?? "missing"}'.", out error);
        }

        item = new SupportedItem("pot", gameId, 2, 2);
        return true;
    }
}
