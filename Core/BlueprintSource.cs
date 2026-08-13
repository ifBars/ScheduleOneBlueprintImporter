namespace ScheduleOneBlueprintImporter.Blueprints;

public static class BlueprintSource
{
    public static bool TryGetShareId(string input, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string value = input.Trim();
        if (Guid.TryParse(value, out id))
            return true;

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Host, "scheduleoneeditor.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? candidate = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2 && string.Equals(parts[0], "id", StringComparison.OrdinalIgnoreCase))
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .FirstOrDefault();

        return Guid.TryParse(candidate, out id);
    }
}
