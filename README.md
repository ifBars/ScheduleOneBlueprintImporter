# Schedule One Blueprint Importer

A dual-runtime proof-of-concept mod that imports a public
[Schedule One Blueprint Editor](https://scheduleoneeditor.com/) Quick Share into
an owned property, builds its physical items with the game's native placement
systems, and charges the online balance.

```text
importblueprint <blueprint-uuid>
```

A full `https://scheduleoneeditor.com/?id=<uuid>` URL is accepted too.

## Current scope

- Mono and IL2CPP MelonLoader builds through S1API.
- Native grid furniture, rotated footprints, composite pots, multi-room floors,
  cropped floors, and Warehouse catwalk/office layouts.
- Full placement validation before building and one item-cost transaction after
  successful creation.
- Employee hiring and destination routes are intentionally skipped and not
  charged.
- Single-player proof of concept; multiplayer authority is not implemented.

## Build

1. Install MelonLoader and the matching [S1API](https://github.com/ifBars/S1API).
2. Copy `local.build.props.example` to `local.build.props` and set your local paths.
3. Build and copy the matching DLL into the game's `Mods` directory:

```powershell
dotnet build ScheduleOneBlueprintImporter.csproj -c Mono
dotnet build ScheduleOneBlueprintImporter.csproj -c Il2cpp
```

## Developer documentation

- [System architecture](docs/architecture.md)
- [Blueprint JSON and site reverse engineering](docs/reverse-engineering.md)
- [Recreating the importer](docs/reimplementation-guide.md)
- [Build and runtime testing](docs/testing.md)

This repository contains no game assemblies, decompiled game code, generated
IL2CPP wrappers, saves, website bundles, or exported Unity assets. It is an
independent fan project and is not affiliated with TVGS or the editor site.
Source code and documentation are available under the [MIT License](LICENSE).
