#if IL2CPPMELON
using Il2CppNewtonsoft.Json.Linq;
#else
using Newtonsoft.Json.Linq;
#endif

namespace ScheduleOneBlueprintImporter.Blueprints;

public static class BlueprintJsonParser
{
    public static BlueprintDocument ParseApiResponse(string json)
    {
        JObject envelope = JObject.Parse(json);
        string blueprintData = envelope["blueprint_data"]?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(blueprintData))
            throw new InvalidDataException("The website response did not contain blueprint_data.");

        JObject root = JObject.Parse(blueprintData);
        var document = new BlueprintDocument
        {
            Type = Text(root["type"]),
        };

        JArray? employees = AsArray(root["hiredEmployees"]);
        if (employees != null)
        {
            for (int index = 0; index < employees.Count; index++)
                document.HiredEmployees.Add(new object());
        }

        JArray? floors = AsArray(root["floors"]);
        if (floors == null)
            return document;

        for (int floorIndex = 0; floorIndex < floors.Count; floorIndex++)
        {
            JObject? floorObject = AsObject(floors[floorIndex]);
            if (floorObject == null)
                continue;
            var floor = new BlueprintFloor();
            JArray? rows = AsArray(floorObject["blueprint"]);
            if (rows != null)
            {
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var row = new List<string>();
                    JArray? cells = AsArray(rows[rowIndex]);
                    if (cells != null)
                    {
                        for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                            row.Add(Text(cells[cellIndex]));
                    }
                    floor.Blueprint.Add(row);
                }
            }

            JArray? items = AsArray(floorObject["placedItems"]);
            if (items != null)
            {
                for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
                {
                    JObject? item = AsObject(items[itemIndex]);
                    if (item != null)
                        floor.PlacedItems.Add(ParseItem(item));
                }
            }
            document.Floors.Add(floor);
        }

        return document;
    }

    private static BlueprintItem ParseItem(JObject item)
    {
        BlueprintPotConfiguration? potConfiguration = ParsePotConfiguration(item["potConfiguration"]);
        return new BlueprintItem
        {
            ItemTypeId = ResolveItemTypeId(item, potConfiguration),
            BlueprintX = Integer(item["blueprintX"]),
            BlueprintY = Integer(item["blueprintY"]),
            Width = Integer(item["width"]),
            Height = Integer(item["height"]),
            Price = Decimal(item["price"]),
            DestinationRoute = ParseRoute(item["destinationRoute"]),
            PotConfiguration = potConfiguration,
        };
    }

    private static string ResolveItemTypeId(JObject item, BlueprintPotConfiguration? potConfiguration)
    {
        string itemTypeId = Text(item["itemTypeId"]);
        if (!string.IsNullOrWhiteSpace(itemTypeId))
            return itemTypeId;
        if (potConfiguration != null)
            return "pot";
        if (WebsiteItemCatalog.TryResolveLegacyName(Text(item["displayName"]), out itemTypeId))
            return itemTypeId;
        return WebsiteItemCatalog.TryResolveLegacyName(Text(item["name"]), out itemTypeId)
            ? itemTypeId
            : string.Empty;
    }

    private static BlueprintRoute? ParseRoute(JToken? token)
    {
        JObject? route = AsObject(token);
        if (route == null)
            return null;
        return new BlueprintRoute
        {
            StartId = EndpointId(route["start"]),
            EndId = EndpointId(route["end"]),
        };
    }

    private static BlueprintPotConfiguration? ParsePotConfiguration(JToken? token)
    {
        JObject? config = AsObject(token);
        if (config == null)
            return null;
        return new BlueprintPotConfiguration
        {
            Pot = ParseComponent(config["pot"]),
            Light = ParseComponent(config["light"]),
            Extra = ParseComponent(config["extra"]),
        };
    }

    private static BlueprintComponent? ParseComponent(JToken? token)
    {
        JObject? component = AsObject(token);
        if (component == null)
            return null;
        string name = Text(component["name"]);
        string id = Text(component["id"]);
        return new BlueprintComponent
        {
            Id = string.IsNullOrWhiteSpace(id) ? ResolveLegacyComponentId(name) : id,
            Name = name,
        };
    }

    private static string ResolveLegacyComponentId(string name) => Normalize(name) switch
    {
        "airpot" => "air-pot",
        "plasticpot" => "plastic-pot",
        "moisturepreservingpot" => "moisture-preserving-pot",
        "halogengrowlight" => "halogen-grow-light",
        "ledgrowlight" => "led-grow-light",
        "fullspectrumgrowlight" => "full-spectrum-grow-light",
        "suspensionrack" => "suspension-rack",
        _ => string.Empty,
    };

    private static string? EndpointId(JToken? token)
    {
        JObject? endpoint = AsObject(token);
        if (endpoint != null)
            return Text(endpoint["id"]);
        string value = Text(token);
        return string.IsNullOrWhiteSpace(value) || value == "null" ? null : value;
    }

    private static string Text(JToken? token) => token?.ToString() ?? string.Empty;

    private static int Integer(JToken? token) =>
        int.TryParse(Text(token), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int value) ? value : 0;

    private static decimal Decimal(JToken? token) =>
        decimal.TryParse(Text(token), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out decimal value) ? value : 0m;

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static JArray? AsArray(JToken? token) =>
        token != null && token.Type == JTokenType.Array ? JArray.Parse(token.ToString()) : null;

    private static JObject? AsObject(JToken? token) =>
        token != null && token.Type == JTokenType.Object ? JObject.Parse(token.ToString()) : null;
}
