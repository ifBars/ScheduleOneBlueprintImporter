using System.Reflection;
using MelonLoader;
using S1API.Money;
using S1API.Property;
using UnityEngine;
using UnityEngine.SceneManagement;
#if IL2CPPMELON
using NativeDateTime = Il2CppSystem.DateTime;
using NativeCoordinate = Il2CppScheduleOne.Tiles.Coordinate;
using NativeDateTimeData = Il2CppScheduleOne.Persistence.Datas.DateTimeData;
using NativeLoadManager = Il2CppScheduleOne.Persistence.LoadManager;
using NativeMetaData = Il2CppScheduleOne.Persistence.Datas.MetaData;
using NativePlayerMovement = Il2CppScheduleOne.PlayerScripts.PlayerMovement;
using NativeProperty = Il2CppScheduleOne.Property.Property;
using NativeSaveInfo = Il2CppScheduleOne.Persistence.SaveInfo;
using NativeDevUtilities = Il2CppScheduleOne.DevUtilities;
#else
using NativeDateTime = System.DateTime;
using NativeCoordinate = ScheduleOne.Tiles.Coordinate;
using NativeDateTimeData = ScheduleOne.Persistence.Datas.DateTimeData;
using NativeLoadManager = ScheduleOne.Persistence.LoadManager;
using NativeMetaData = ScheduleOne.Persistence.Datas.MetaData;
using NativePlayerMovement = ScheduleOne.PlayerScripts.PlayerMovement;
using NativeProperty = ScheduleOne.Property.Property;
using NativeSaveInfo = ScheduleOne.Persistence.SaveInfo;
using NativeDevUtilities = ScheduleOne.DevUtilities;
#endif

namespace ScheduleOneBlueprintImporter.Runtime;

internal sealed class BlueprintSmokeProbe
{
    private enum Phase
    {
        AwaitMenu,
        AwaitGameplay,
        AwaitImport,
        Capture,
    }

    private static BlueprintSmokeProbe? _current;

    private readonly BlueprintImportController _controller;
    private readonly MelonLogger.Instance _logger;
    private readonly string _sourceSave;
    private readonly string _shareId;
    private readonly string _outputDirectory;
    private readonly string _propertyCode;
    private readonly string _resultPath;
    private Phase _phase;
    private float _phaseStarted;
    private float _elapsed;
    private int _beforeCount;
    private float _beforeBalance;
    private ImportOutcome? _outcome;
    private bool _loadRequested;
    private bool _finished;
    private bool _screenshotRequested;

    private BlueprintSmokeProbe(
        BlueprintImportController controller,
        MelonLogger.Instance logger,
        string sourceSave,
        string shareId,
        string outputDirectory,
        string propertyCode)
    {
        _controller = controller;
        _logger = logger;
        _sourceSave = sourceSave;
        _shareId = shareId;
        _outputDirectory = outputDirectory;
        _propertyCode = propertyCode;
        Directory.CreateDirectory(outputDirectory);
        _resultPath = Path.Combine(outputDirectory, "result.txt");
        if (File.Exists(_resultPath))
            File.Delete(_resultPath);
        _controller.Completed += OnCompleted;
        Enter(Phase.AwaitMenu);
    }

    internal static void TryCreate(BlueprintImportController controller, MelonLogger.Instance logger)
    {
        string[] args = Environment.GetCommandLineArgs();
        if (!args.Any(arg => string.Equals(arg, "--blueprint-import-smoke", StringComparison.OrdinalIgnoreCase)))
            return;

        string sourceSave = GetArgument(args, "--blueprint-smoke-save");
        string shareId = GetArgument(args, "--blueprint-smoke-id");
        string output = GetArgument(args, "--blueprint-smoke-dir");
        string propertyCode = GetArgument(args, "--blueprint-smoke-property");
        if (string.IsNullOrWhiteSpace(sourceSave) || string.IsNullOrWhiteSpace(shareId) ||
            string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(propertyCode))
            throw new ArgumentException("Blueprint smoke requires save, id, output, and property arguments.");

        _current = new BlueprintSmokeProbe(controller, logger, sourceSave, shareId, output, propertyCode);
        logger.Msg($"[BlueprintSmoke] Enabled with disposable save '{sourceSave}'.");
    }

    internal static void UpdateCurrent() => _current?.Update();

    private void Update()
    {
        if (_finished)
            return;
        _elapsed += Time.unscaledDeltaTime;
        if (_elapsed > 180f)
        {
            Finish(false, $"Timeout|Phase={_phase}");
            return;
        }

        try
        {
            switch (_phase)
            {
                case Phase.AwaitMenu:
                    UpdateAwaitMenu();
                    break;
                case Phase.AwaitGameplay:
                    UpdateAwaitGameplay();
                    break;
                case Phase.AwaitImport:
                    UpdateAwaitImport();
                    break;
                case Phase.Capture:
                    UpdateCapture();
                    break;
            }
        }
        catch (Exception ex)
        {
            Finish(false, $"Exception|Phase={_phase}|Type={ex.GetType().Name}|Message={ex.Message}");
        }
    }

    private void UpdateAwaitMenu()
    {
        NativeLoadManager? loadManager = NativeLoadManager.Instance;
        if (loadManager == null || loadManager.IsLoading || _loadRequested)
            return;

        EnableDebugMode(loadManager);
        _loadRequested = true;
        loadManager.StartGame(CreateSaveInfo(), false, false);
        Enter(Phase.AwaitGameplay);
    }

    private void UpdateAwaitGameplay()
    {
        NativeLoadManager? loadManager = NativeLoadManager.Instance;
        if (SceneManager.GetActiveScene().name != "Main" || loadManager == null ||
            loadManager.IsLoading || !loadManager.IsGameLoaded || !loadManager.IsInGameScene)
        {
            return;
        }

        NativeProperty property = FindOwnedTargetProperty()
            ?? throw new InvalidOperationException($"Disposable save did not load owned property '{_propertyCode}'.");
        _beforeCount = PropertyManager.GetOwnedProperties()
            .First(candidate => string.Equals(candidate.PropertyCode, _propertyCode, StringComparison.OrdinalIgnoreCase))
            .BuildableItemCount;
        _beforeBalance = Money.GetOnlineBalance();
        _controller.BeginImport(_shareId);
        Enter(Phase.AwaitImport);
    }

    private void UpdateAwaitImport()
    {
        if (_outcome == null)
            return;
        if (!_outcome.Success || _outcome.Plan == null)
        {
            Finish(false, $"ImportRejected|{_outcome.Error}");
            return;
        }

        NativeProperty property = FindOwnedTargetProperty()
            ?? throw new InvalidOperationException($"Owned property '{_propertyCode}' disappeared after import.");
        int countDelta = PropertyManager.GetOwnedProperties()
            .First(candidate => string.Equals(candidate.PropertyCode, _propertyCode, StringComparison.OrdinalIgnoreCase))
            .BuildableItemCount - _beforeCount;
        float balanceDelta = Money.GetOnlineBalance() - _beforeBalance;
        if (countDelta != _outcome.Plan.ExpectedNativeObjectCount)
            throw new InvalidOperationException($"Buildable count delta was {countDelta}, expected {_outcome.Plan.ExpectedNativeObjectCount}.");
        if (Math.Abs(balanceDelta) < 0.01f && Time.unscaledTime - _phaseStarted < 5f)
            return;
        if (Math.Abs(balanceDelta + (float)_outcome.Plan.TotalCost) > 0.01f)
            throw new InvalidOperationException($"Balance delta was {balanceDelta}, expected {-_outcome.Plan.TotalCost}.");

        if (NativeDevUtilities.PlayerSingleton<NativePlayerMovement>.InstanceExists)
        {
            NativePlayerMovement movement = NativeDevUtilities.PlayerSingleton<NativePlayerMovement>.Instance;
            var grid = property.Grids[0];
            var centerTile = grid.GetTile(new NativeCoordinate(grid.Width / 2, grid.Height / 2));
            Vector3 target = centerTile != null ? centerTile.transform.position : grid.Origin;
            Vector3 viewPosition = target - grid.transform.forward * 4f + Vector3.up * 0.1f;
            movement.Teleport(viewPosition, false);
            movement.SetPlayerRotation(
                Quaternion.LookRotation((target + Vector3.up - viewPosition).normalized, Vector3.up));
        }

        _logger.Msg($"[BlueprintSmoke] STATE|Imported|Items={countDelta}|BalanceDelta={balanceDelta:0.00}");
        Enter(Phase.Capture);
    }

    private void UpdateCapture()
    {
        if (Time.unscaledTime - _phaseStarted < 2f)
            return;
        string screenshot = Path.Combine(_outputDirectory, "imported-blueprint.png");
        if (!_screenshotRequested)
        {
            _screenshotRequested = true;
            ScreenCapture.CaptureScreenshot(screenshot);
            return;
        }
        if (!File.Exists(screenshot) || new FileInfo(screenshot).Length == 0)
            return;
        Finish(true, $"WebsiteItems={_outcome!.Plan!.Placements.Count}|NativeObjects={_outcome.Plan.ExpectedNativeObjectCount}|Cost={_outcome.Plan.TotalCost:0.00}|Screenshot={screenshot}");
    }

    private void OnCompleted(ImportOutcome outcome) => _outcome = outcome;

    private NativeSaveInfo CreateSaveInfo()
    {
        var metadata = new NativeMetaData(
            (NativeDateTimeData?)null,
            (NativeDateTimeData?)null,
            Application.version,
            Application.version,
            false);
        return new NativeSaveInfo(
            _sourceSave,
            -1,
            "Blueprint Import Smoke",
            GetNow(),
            GetNow(),
            0f,
            Application.version,
            metadata);
    }

    private static void EnableDebugMode(NativeLoadManager loadManager)
    {
        PropertyInfo? property = typeof(NativeLoadManager).GetProperty(
            nameof(NativeLoadManager.DebugMode),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo? setter = property?.GetSetMethod(true);
        if (setter == null)
            throw new MissingMethodException(typeof(NativeLoadManager).FullName, "set_DebugMode");
        setter.Invoke(loadManager, new object[] { true });
    }

    private static NativeDateTime GetNow()
    {
#if IL2CPPMELON
        return new NativeDateTime(DateTime.Now.Ticks);
#else
        return DateTime.Now;
#endif
    }

    private static string GetArgument(IReadOnlyList<string> args, string name)
    {
        for (int index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }
        return string.Empty;
    }

    private NativeProperty? FindOwnedTargetProperty()
    {
        foreach (NativeProperty property in NativeProperty.OwnedProperties)
        {
            if (string.Equals(property.PropertyCode, _propertyCode, StringComparison.OrdinalIgnoreCase))
                return property;
        }
        return null;
    }

    private void Enter(Phase phase)
    {
        _phase = phase;
        _phaseStarted = Time.unscaledTime;
        _logger.Msg($"[BlueprintSmoke] STATE|Phase={phase}");
    }

    private void Finish(bool passed, string details)
    {
        if (_finished)
            return;
        _finished = true;
        _controller.Completed -= OnCompleted;
        string backend =
#if IL2CPPMELON
            "Il2Cpp";
#else
            "Mono";
#endif
        string result = $"{(passed ? "PASS" : "FAIL")}|Backend={backend}|{details}";
        File.WriteAllText(_resultPath, result);
        if (passed)
            _logger.Msg($"[BlueprintSmoke] {result}");
        else
            _logger.Error($"[BlueprintSmoke] {result}");
        Application.Quit();
    }
}
