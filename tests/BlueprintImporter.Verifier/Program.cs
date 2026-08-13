using ScheduleOneBlueprintImporter.Blueprints;

var failures = new List<string>();

Check(BlueprintSource.TryGetShareId(
    "https://scheduleoneeditor.com/?id=11111111-2222-3333-4444-555555555555",
    out Guid parsed) && parsed == Guid.Parse("11111111-2222-3333-4444-555555555555"),
    "parses a website share URL");
Check(BlueprintSource.TryGetShareId("11111111-2222-3333-4444-555555555555", out parsed) &&
      parsed == Guid.Parse("11111111-2222-3333-4444-555555555555"),
    "parses a copied blueprint UUID for the importblueprint command");
Check(!BlueprintSource.TryGetShareId("https://example.com/?id=11111111-2222-3333-4444-555555555555", out _),
    "rejects foreign hosts");

const string nestedPayload = """
{
  "blueprint_data": "{\"type\":\"bungalow\",\"hiredEmployees\":[{}],\"floors\":[{\"blueprint\":[[\"EC3\",\"EC4\"],[\"OL\",\"OR\"]],\"placedItems\":[{\"itemTypeId\":\"pot\",\"blueprintX\":1,\"blueprintY\":1,\"width\":2,\"height\":2,\"price\":360,\"destinationRoute\":{\"start\":{\"id\":\"source-id\"},\"end\":{\"id\":\"target-id\"}},\"potConfiguration\":{\"pot\":{\"id\":\"air-pot\",\"name\":\"Air Pot\"},\"light\":{\"id\":\"full-spectrum-grow-light\",\"name\":\"Full Spectrum Grow Light\"},\"extra\":{\"id\":\"suspension-rack\",\"name\":\"Suspension Rack\"}}}]}]}"
}
""";
BlueprintDocument parsedPayload = BlueprintJsonParser.ParseApiResponse(nestedPayload);
Check(parsedPayload.SkippedEmployeeCount == 1 &&
      parsedPayload.Floors[0].PlacedItems[0].DestinationRoute?.StartId == "source-id" &&
      parsedPayload.Floors[0].PlacedItems[0].DestinationRoute?.EndId == "target-id" &&
      parsedPayload.Floors[0].PlacedItems[0].PotConfiguration?.Light?.Id == "full-spectrum-grow-light",
    "parses nested route endpoints and composite pot components");

const string legacyPayload = """
{
  "blueprint_data": "{\"type\":\"warehouse\",\"floors\":[{\"blueprint\":[[\"0\",\"0\"],[\"0\",\"0\"]],\"placedItems\":[{\"name\":\"Lab Oven\",\"blueprintX\":1,\"blueprintY\":1,\"width\":2,\"height\":4,\"price\":1000},{\"name\":\"Storage\",\"displayName\":\"Huge Storage Closet\",\"blueprintX\":1,\"blueprintY\":1,\"width\":2,\"height\":4,\"price\":500},{\"name\":\"Air Pot Setup\",\"blueprintX\":1,\"blueprintY\":1,\"width\":2,\"height\":2,\"price\":360,\"potConfiguration\":{\"pot\":{\"name\":\"Air Pot\"},\"light\":{\"name\":\"Full Spectrum Grow Light\"},\"extra\":{\"name\":\"Suspension Rack\"}}}]}]}"
}
""";
BlueprintDocument legacyDocument = BlueprintJsonParser.ParseApiResponse(legacyPayload);
Check(legacyDocument.Floors[0].PlacedItems[0].ItemTypeId == "lab_oven" &&
      legacyDocument.Floors[0].PlacedItems[1].ItemTypeId == "huge_storage_closet" &&
      legacyDocument.Floors[0].PlacedItems[2].ItemTypeId == "pot",
    "resolves old shares that identify placements by name or displayName");
Check(legacyDocument.Floors[0].PlacedItems[2].PotConfiguration?.Pot?.Id == "air-pot" &&
      legacyDocument.Floors[0].PlacedItems[2].PotConfiguration?.Light?.Id == "full-spectrum-grow-light" &&
      legacyDocument.Floors[0].PlacedItems[2].PotConfiguration?.Extra?.Id == "suspension-rack",
    "resolves old composite-pot components that omit ids");

BlueprintDocument valid = Document(Item("packaging_station", 3, 4, 4, 2, 100m));
Check(BlueprintPlanner.TryCreatePlan(valid, out ImportPlan? plan, out string error), error);
Check(plan?.TotalCost == 100m, "uses the website-declared price");
Check(plan?.Placements.Single().Rotation == 0, "preserves an unrotated footprint");
Check(plan?.Placements.Single().OriginX == 2 && plan.Placements.Single().OriginY == 3,
    "removes the website perimeter from an unrotated origin");

BlueprintDocument rotated = Document(Item("mixing_station_mk2", 3, 4, 2, 4, 2_000m));
Check(BlueprintPlanner.TryCreatePlan(rotated, out plan, out error), error);
Check(plan?.Placements.Single().Rotation == 90, "infers a quarter-turn from swapped dimensions");
Check(plan?.Placements.Single().OriginX == 2 && plan.Placements.Single().OriginY == 6,
    "converts the website rectangle origin to the native 90-degree origin");

BlueprintDocument composite = Document(Item("pot", 1, 1, 2, 2, 360m,
    new BlueprintPotConfiguration
    {
        Pot = new BlueprintComponent { Id = "air-pot", Name = "Air Pot" },
        Light = new BlueprintComponent { Id = "full-spectrum-grow-light", Name = "Full Spectrum Grow Light" },
        Extra = new BlueprintComponent { Id = "suspension-rack", Name = "Suspension Rack" },
    }));
Check(BlueprintPlanner.TryCreatePlan(composite, out plan, out error) &&
      plan?.Placements.Single().Item.GameId == "airpot",
    "normalizes a composite pot setup to its native base pot");

BlueprintDocument employee = Document(Item("locker", 1, 1, 3, 1, 150m));
employee.HiredEmployees.Add(new object());
Check(BlueprintPlanner.TryCreatePlan(employee, out _, out error),
    "allows physical placement while employee assignments are skipped");

BlueprintDocument emptyRoute = Document(Item("drying_rack", 1, 1, 3, 2, 250m));
emptyRoute.Floors[0].PlacedItems[0].DestinationRoute = new BlueprintRoute();
Check(BlueprintPlanner.TryCreatePlan(emptyRoute, out _, out error),
    "accepts the website's empty destination-route placeholder");

BlueprintDocument configuredRoute = Document(Item("drying_rack", 1, 1, 3, 2, 250m));
configuredRoute.Floors[0].PlacedItems[0].DestinationRoute = new BlueprintRoute { EndId = "destination-id" };
Check(BlueprintPlanner.TryCreatePlan(configuredRoute, out _, out error),
    "allows physical placement while configured routes are skipped");

BlueprintDocument warehouse = WarehouseDocument();
Check(BlueprintPlanner.TryCreatePlan(warehouse, out ImportPlan? warehousePlan, out error), error);
var warehouseGrids = new List<GridShape>
{
    new(0, 8, 26, -5d, 0d),
    new(1, 8, 26, 5d, 0d),
    new(2, 11, 8, 0d, -4d),
};
bool mappedWarehouse = GridRegionLayoutPlanner.TryCreateCompositeLayout(
    warehouse.Floors[0], warehousePlan!.Placements, warehouseGrids,
    out IReadOnlyList<GridRegionLayout>? warehouseRegions);
string warehouseLayout = warehouseRegions == null
    ? "none"
    : string.Join("; ", warehouseRegions.Select(region =>
        $"{region.OffsetX},{region.OffsetY}:{region.Width}x{region.Height}={region.Placements.Count}"));
Check(mappedWarehouse &&
      warehouseRegions!.Select(region => (region.OffsetX, region.OffsetY, region.Width, region.Height))
          .SequenceEqual(new[] { (0, 0, 26, 8), (17, 8, 8, 11), (0, 19, 26, 8) }) &&
      warehouseRegions!.Select(region => region.Placements.Count).SequenceEqual(new[] { 2, 3, 2 }),
    $"partitions the Warehouse catwalk and office canvas across three native grids (actual: {warehouseLayout})");

if (failures.Count != 0)
{
    Console.Error.WriteLine($"FAILED ({failures.Count}):");
    foreach (string failure in failures)
        Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("BlueprintImporter verifier passed (19 assertions).");
return 0;

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

static BlueprintDocument Document(BlueprintItem item)
{
    var rows = Enumerable.Range(0, 12)
        .Select(_ => Enumerable.Repeat("EC3", 12).ToList())
        .ToList();
    return new BlueprintDocument
    {
        Type = "sweatshop",
        Floors = new List<BlueprintFloor>
        {
            new() { Blueprint = rows, PlacedItems = new List<BlueprintItem> { item } },
        },
    };
}

static BlueprintItem Item(
    string id,
    int x,
    int y,
    int width,
    int height,
    decimal price,
    BlueprintPotConfiguration? potConfiguration = null) =>
    new()
    {
        ItemTypeId = id,
        BlueprintX = x,
        BlueprintY = y,
        Width = width,
        Height = height,
        Price = price,
        PotConfiguration = potConfiguration,
    };

static BlueprintDocument WarehouseDocument()
{
    var rows = Enumerable.Range(0, 29)
        .Select(_ => Enumerable.Repeat("0", 28).ToList())
        .ToList();
    return new BlueprintDocument
    {
        Type = "warehouse",
        Floors = new List<BlueprintFloor>
        {
            new()
            {
                Blueprint = rows,
                PlacedItems = new List<BlueprintItem>
                {
                    Item("huge_storage_closet", 25, 1, 2, 4, 500m),
                    Item("huge_storage_closet", 25, 5, 2, 4, 500m),
                    Item("locker", 18, 9, 1, 3, 150m),
                    Item("bed", 21, 13, 5, 3, 150m),
                    Item("locker", 25, 17, 1, 3, 150m),
                    Item("huge_storage_closet", 25, 20, 2, 4, 500m),
                    Item("huge_storage_closet", 25, 24, 2, 4, 500m),
                },
            },
        },
    };
}
