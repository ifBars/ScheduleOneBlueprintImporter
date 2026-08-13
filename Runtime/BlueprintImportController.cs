using System.Collections.Concurrent;
using System.Net.Http;
using MelonLoader;
using ScheduleOneBlueprintImporter.Blueprints;

namespace ScheduleOneBlueprintImporter.Runtime;

internal sealed class BlueprintImportController : IDisposable
{
    private readonly MelonLogger.Instance _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private bool _busy;

    internal event Action<ImportOutcome>? Completed;

    internal BlueprintImportController(MelonLogger.Instance logger)
    {
        _logger = logger;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ScheduleOneBlueprintImporter/0.1");
    }

    internal void BeginImport(string source)
    {
        if (_busy)
        {
            _logger.Warning("An import is already in progress.");
            Completed?.Invoke(new ImportOutcome(false, "An import is already in progress.", null));
            return;
        }

        if (!BlueprintSource.TryGetShareId(source, out Guid id))
        {
            _logger.Error("Expected a scheduleoneeditor.com share URL or blueprint UUID.");
            Completed?.Invoke(new ImportOutcome(false, "Invalid blueprint source.", null));
            return;
        }

        _busy = true;
        _logger.Msg($"Fetching shared blueprint {id}...");
        _ = FetchAsync(id);
    }

    internal void Update()
    {
        while (_mainThreadActions.TryDequeue(out Action? action))
            action();
    }

    private async Task FetchAsync(Guid id)
    {
        try
        {
            string json = await _http.GetStringAsync(Constants.Endpoint + id).ConfigureAwait(false);
            _mainThreadActions.Enqueue(() => ParseAndImportOnMainThread(id, json));
        }
        catch (Exception ex)
        {
            _mainThreadActions.Enqueue(() =>
            {
                _busy = false;
                _logger.Error($"Blueprint download failed: {ex.Message}");
                Completed?.Invoke(new ImportOutcome(false, ex.Message, null));
            });
        }
    }

    private void ParseAndImportOnMainThread(Guid id, string json)
    {
        try
        {
            BlueprintDocument document = BlueprintJsonParser.ParseApiResponse(json);
            ImportOnMainThread(id, document);
        }
        catch (Exception ex)
        {
            _busy = false;
            _logger.Error($"Blueprint parse failed: {ex.Message}");
            Completed?.Invoke(new ImportOutcome(false, ex.Message, null));
        }
    }

    private void ImportOnMainThread(Guid id, BlueprintDocument document)
    {
        try
        {
            if (!BlueprintPlanner.TryCreatePlan(document, out ImportPlan? plan, out string error))
            {
                _logger.Error($"Blueprint rejected: {error}");
                Completed?.Invoke(new ImportOutcome(false, error, plan));
                return;
            }

            int routedItems = document.Floors.Sum(floor =>
                floor.PlacedItems.Count(item => item.DestinationRoute?.IsConfigured == true));
            if (routedItems > 0)
                _logger.Warning($"Skipping {routedItems} website destination route(s); physical items will still be imported.");
            if (document.SkippedEmployeeCount > 0)
                _logger.Warning($"Skipping {document.SkippedEmployeeCount} hired employee assignment(s); physical items will still be imported.");

            var importer = new GameBlueprintImporter(_logger);
            if (!importer.TryImport(plan!, id, document.Floors, out error))
            {
                _logger.Error($"Blueprint import failed: {error}");
                Completed?.Invoke(new ImportOutcome(false, error, plan));
                return;
            }

            _logger.Msg(
                $"Imported {plan!.Placements.Count} item(s) into {plan.PropertyType}; " +
                $"charged ${plan.TotalCost:0.00} online.");
            Completed?.Invoke(new ImportOutcome(true, string.Empty, plan));
        }
        catch (Exception ex)
        {
            _logger.Error($"Blueprint import failed unexpectedly: {ex}");
            Completed?.Invoke(new ImportOutcome(false, ex.Message, null));
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        while (_mainThreadActions.TryDequeue(out _))
        {
        }
    }
}

internal sealed record ImportOutcome(bool Success, string Error, ImportPlan? Plan);
