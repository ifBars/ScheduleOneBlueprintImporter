namespace ScheduleOneBlueprintImporter.Blueprints;

public static class WebsiteItemCatalog
{
    private static readonly IReadOnlyDictionary<string, SupportedItem> Items =
        new[]
        {
            new SupportedItem("packaging_station", "packagingstation", 4, 2),
            new SupportedItem("packaging_station_mk2", "packagingstationmk2", 4, 2),
            new SupportedItem("mixing_station", "mixingstation", 4, 2),
            new SupportedItem("mixing_station_mk2", "mixingstationmk2", 4, 2),
            new SupportedItem("tv", "tv", 4, 2),
            new SupportedItem("chemistry_station", "chemistrystation", 4, 2),
            new SupportedItem("brick_press", "brickpress", 2, 2),
            new SupportedItem("trash_can", "trashcan", 2, 2),
            new SupportedItem("large_storage_rack", "largestoragerack", 4, 1),
            new SupportedItem("medium_storage_rack", "mediumstoragerack", 3, 1),
            new SupportedItem("small_storage_rack", "smallstoragerack", 2, 1),
            new SupportedItem("safe", "safe", 2, 2),
            new SupportedItem("bed", "bed", 3, 5),
            new SupportedItem("locker", "locker", 3, 1),
            new SupportedItem("drying_rack", "dryingrack", 3, 2),
            new SupportedItem("floor_lamp", "floorlamp", 1, 1),
            new SupportedItem("cauldron", "cauldron", 4, 4),
            new SupportedItem("pot_sprinker", "potsprinkler", 2, 1),
            new SupportedItem("big_sprinker", "largesprinkler", 2, 2),
            new SupportedItem("soil_pourer", "soilpourer", 2, 1),
            new SupportedItem("coffee_table", "coffeetable", 4, 2),
            new SupportedItem("wooden_square_table", "woodensquaretable", 2, 2),
            new SupportedItem("metal_square_table", "metalsquaretable", 2, 2),
            new SupportedItem("plastic_table", "plastictable", 4, 2),
            new SupportedItem("garbage_throne", "garbagethrone", 4, 3),
            new SupportedItem("toilet_pre_owned", "toilet", 2, 2),
            new SupportedItem("golden_toilet", "goldentoilet", 2, 2),
            new SupportedItem("display_cabinet", "displaycabinet", 3, 2),
            new SupportedItem("lab_oven", "laboven", 4, 2),
            new SupportedItem("jukebox", "jukebox", 2, 2),
            new SupportedItem("grandfather_clock", "grandfatherclock", 2, 1),
            new SupportedItem("ac_unit", "acunit", 2, 2),
            new SupportedItem("spore_spawn_station", "mushroomspawnstation", 4, 2),
            new SupportedItem("mushroom_bed", "mushroombed", 2, 2),
            new SupportedItem("small_storage_closet", "smallstoragecloset", 2, 1),
            new SupportedItem("medium_storage_closet", "mediumstoragecloset", 3, 1),
            new SupportedItem("large_storage_closet", "largestoragecloset", 4, 1),
            new SupportedItem("huge_storage_closet", "hugestoragecloset", 4, 2),
        }.ToDictionary(item => item.WebsiteId, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> LegacyNames =
        Items.Values
            .SelectMany(item => new[]
            {
                new KeyValuePair<string, string>(Normalize(item.WebsiteId), item.WebsiteId),
                new KeyValuePair<string, string>(Normalize(item.GameId), item.WebsiteId),
            })
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string websiteId, out SupportedItem item) =>
        Items.TryGetValue(websiteId, out item!);

    public static bool TryResolveLegacyName(string value, out string websiteId) =>
        LegacyNames.TryGetValue(Normalize(value), out websiteId!);

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
