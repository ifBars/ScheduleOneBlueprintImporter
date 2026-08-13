# Build and runtime testing

The project has two validation layers: a fast managed verifier for parsing and
planning, and an opt-in real-game smoke runner for native placement and charging.

## Local setup

Copy `local.build.props.example` to `local.build.props` and configure paths to
your own Mono and IL2CPP installations plus matching S1API DLLs. The local file
is ignored by Git.

No game assembly, generated wrapper, save, or S1API binary is distributed here.

## Managed verifier

```powershell
dotnet run --project tests\BlueprintImporter.Verifier\BlueprintImporter.Verifier.csproj
```

The verifier uses only its NuGet dependencies; it does not require a game
installation. It covers share URL parsing, nested route objects, current and legacy
placement identities, legacy pot components, footprint rotation, perimeter
conversion, prices, skipped routes/employees, and composite Warehouse layout
partitioning.

## Runtime builds

```powershell
dotnet build ScheduleOneBlueprintImporter.csproj -c Mono
dotnet build ScheduleOneBlueprintImporter.csproj -c Il2cpp
```

Treat these as separate products. A successful Mono build or game run does not
prove IL2CPP compatibility.

## Real-game smoke runner

The runner copies a user-owned save into a unique evidence directory, clears
only the selected property in that copy, temporarily swaps the selected live
installation's `Mods` directory, launches the game, waits for a namespaced
result, captures logs and a screenshot, and restores the install.

It refuses to run without the explicit mutation acknowledgement:

```powershell
.\tests\Run-BlueprintSmoke.ps1 `
  -Runtime Il2cpp `
  -Il2CppGamePath 'C:\Games\Schedule I' `
  -SourceSave 'C:\Path\To\Owned\SaveGame_1' `
  -S1ApiDllPath 'C:\Path\To\S1API.Il2Cpp.MelonLoader.dll' `
  -BlueprintId '00000000-0000-0000-0000-000000000000' `
  -PropertyCode 'dockswarehouse' `
  -PropertyFileName 'Docks Warehouse.json' `
  -AllowLiveInstallMutation
```

Close the game first. Use a save that owns the target property. The runner does
not mutate the source save, but it does temporarily move the selected install's
`Mods` directory, so do not interrupt it casually.

## Required evidence

A runtime pass requires all of the following:

- the selected save reaches the gameplay scene;
- the importer downloads and accepts the share;
- the native buildable-object count increases by the expected amount;
- the online balance decreases by the planned physical-item total;
- a `PASS|Backend=...` result is written;
- the screenshot exists and is visually inspected;
- the game process exits and the original `Mods` directory is restored.

Use disposable public-share fixtures that exercise ordinary furniture, rotated
items, composite pots, routes/employees, legacy schemas, cropped floors,
multi-room floors, and unequal composite grids. Do not publish user saves or
private blueprint IDs as fixtures.
