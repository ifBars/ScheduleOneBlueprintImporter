# Architecture

The importer deliberately separates untrusted website data, runtime-neutral
planning, and game mutation. This makes most of the difficult logic testable
without launching Unity and keeps Mono/IL2CPP differences at the edge.

## Import flow

```text
console command
    -> validate UUID or editor URL
    -> GET the Quick Share envelope in the background
    -> enqueue JSON parsing on Unity's main thread
    -> normalize current and legacy schemas
    -> create a runtime-neutral placement plan
    -> resolve the owned native property and grids
    -> validate every tile and item definition
    -> create native objects
    -> charge one online transaction
```

The HTTP request is the only background operation. Parsing also runs on the
Unity main thread because IL2CPP's generated Newtonsoft wrappers can require
runtime state that is unsafe to initialize from a worker thread.

## Component boundaries

| Component | Responsibility |
| --- | --- |
| `ImportBlueprintCommand` | S1API console registration and argument forwarding. |
| `BlueprintSource` | Accept only a raw UUID or an HTTPS editor share URL. |
| `BlueprintImportController` | Async download, main-thread handoff, warnings, and orchestration. |
| `BlueprintJsonParser` | Manual `JObject`/`JArray` parsing and legacy-schema normalization. |
| `WebsiteItemCatalog` | Explicit website-to-native item IDs and canonical footprints. |
| `BlueprintPlanner` | Bounds, price, footprint, orientation, and supported-item validation. |
| `GridRegionLayoutPlanner` | Runtime-neutral partitioning of composite website floors. |
| `GameBlueprintImporter` | Owned-property lookup, native grid validation, object creation, and charging. |

## Placement planning

The website surrounds floor templates with a one-cell border. A website origin
of `(x, y)` therefore starts at native grid coordinate `(x - 1, y - 1)`.

An item whose website dimensions match its catalog dimensions is planned at
zero degrees. Swapped dimensions indicate a 90-degree turn. The site does not
retain enough information to distinguish 0 from 180 or 90 from 270, so the
runtime tries the eight dihedral floor orientations and selects one whose tiles
exist, are buildable, and are unoccupied.

Floor-to-grid mapping has four strategies:

1. Exact: one website interior matches one native grid.
2. Cropped: a larger editor canvas contains one native grid, as with the Barn.
3. Segmented: equal-sized room grids occupy non-empty blocks in one canvas.
4. Composite: unequal grids partition a floor. Candidate regions are scored by
   buildable cells and by the normalized pairwise distances between website
   regions and native grid centers. This resolves layouts such as two Warehouse
   catwalks plus its office without depending on grid names.

## Mutation and charging

All definitions, pot components, regions, orientations, and target tiles are
validated before the first object is created. Ordinary items use the native
grid-item creation path. A configured pot expands into its base pot and optional
suspension rack; a grow light is then created on the rack's procedural tiles.

The online balance is checked before placement and charged once after the
expected native object count is created. Employee signing fees are excluded
because employee creation is outside this POC.

There is no transactional rollback if an unexpected native creation exception
occurs after mutation begins. Production code should add an undo journal or a
staged native transaction before expanding the supported surface.

## Runtime compatibility

Compilation aliases keep native types behind one implementation. Business
models and planning code use ordinary managed types. IL2CPP-specific rules are
limited to generated namespaces, native collections, and manual JSON token
handling; generic `JsonConvert.DeserializeObject<T>` is intentionally avoided.
