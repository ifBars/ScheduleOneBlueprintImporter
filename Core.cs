using MelonLoader;
using ScheduleOneBlueprintImporter.Runtime;

[assembly: MelonInfo(
    typeof(ScheduleOneBlueprintImporter.Core),
    ScheduleOneBlueprintImporter.Constants.ModName,
    ScheduleOneBlueprintImporter.Constants.ModVersion,
    ScheduleOneBlueprintImporter.Constants.ModAuthor)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace ScheduleOneBlueprintImporter;

public sealed class Core : MelonMod
{
    private BlueprintImportController? _controller;

    internal static Core? Instance { get; private set; }

    public override void OnInitializeMelon()
    {
        Instance = this;
        _controller = new BlueprintImportController(LoggerInstance);
        BlueprintSmokeProbe.TryCreate(_controller, LoggerInstance);
        LoggerInstance.Msg(
            $"{Constants.ModName} {Constants.ModVersion} initialized. " +
            $"After loading a save, run '{Constants.CommandWord} <share-url-or-id>'.");
    }

    public override void OnUpdate()
    {
        _controller?.Update();
        BlueprintSmokeProbe.UpdateCurrent();
    }

    internal void Import(string source) => _controller?.BeginImport(source);

    public override void OnApplicationQuit()
    {
        _controller?.Dispose();
        _controller = null;
        Instance = null;
    }
}
