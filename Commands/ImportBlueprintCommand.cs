using MelonLoader;
using S1API.Console;

namespace ScheduleOneBlueprintImporter.Commands;

public sealed class ImportBlueprintCommand : BaseConsoleCommand
{
    public override string CommandWord => Constants.CommandWord;

    public override string CommandDescription =>
        "Builds the physical items from a shared blueprint in an owned property and charges their listed cost.";

    public override string ExampleUsage =>
        "importblueprint 00000000-0000-0000-0000-000000000000";

    public override void ExecuteCommand(List<string> args)
    {
        if (args.Count != 1)
        {
            MelonLogger.Warning($"[BlueprintImporter] Usage: {ExampleUsage}");
            return;
        }

        if (Core.Instance == null)
        {
            MelonLogger.Warning("[BlueprintImporter] The mod is not initialized.");
            return;
        }

        Core.Instance.Import(args[0]);
    }
}
