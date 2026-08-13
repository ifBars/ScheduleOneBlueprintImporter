namespace ScheduleOneBlueprintImporter.Blueprints;

public sealed class BlueprintEnvelope
{
    public string blueprint_data { get; set; } = string.Empty;
}

public sealed class BlueprintDocument
{
    public string Type { get; set; } = string.Empty;

    public List<BlueprintFloor> Floors { get; set; } = new();

    public List<object> HiredEmployees { get; set; } = new();

    public int SkippedEmployeeCount => HiredEmployees.Count;
}

public sealed class BlueprintFloor
{
    public List<List<string>> Blueprint { get; set; } = new();

    public List<BlueprintItem> PlacedItems { get; set; } = new();

    public int Width => Blueprint.Count == 0 ? 0 : Blueprint.Max(row => row.Count);

    public int Height => Blueprint.Count;
}

public sealed class BlueprintItem
{
    public string ItemTypeId { get; set; } = string.Empty;

    public int BlueprintX { get; set; }

    public int BlueprintY { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public decimal Price { get; set; }

    public BlueprintRoute? DestinationRoute { get; set; }

    public BlueprintPotConfiguration? PotConfiguration { get; set; }

    public int X => BlueprintX;

    public int Y => BlueprintY;
}

public sealed class BlueprintRoute
{
    public string? StartId { get; set; }

    public string? EndId { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(StartId) || !string.IsNullOrWhiteSpace(EndId);
}

public sealed class BlueprintPotConfiguration
{
    public BlueprintComponent? Pot { get; set; }

    public BlueprintComponent? Light { get; set; }

    public BlueprintComponent? Extra { get; set; }
}

public sealed class BlueprintComponent
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed record SupportedItem(string WebsiteId, string GameId, int Width, int Height);

public sealed record PlannedPlacement(
    BlueprintItem Source,
    SupportedItem Item,
    int FloorIndex,
    int GridX,
    int GridY,
    int OriginX,
    int OriginY,
    int Rotation);

public sealed record ImportPlan(
    string PropertyType,
    IReadOnlyList<PlannedPlacement> Placements,
    decimal TotalCost)
{
    public int ExpectedNativeObjectCount => Placements.Sum(placement =>
        1 +
        (placement.Source.PotConfiguration?.Extra != null ? 1 : 0) +
        (placement.Source.PotConfiguration?.Light != null ? 1 : 0));
}
