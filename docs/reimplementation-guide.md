# Recreating the import system

This is the smallest practical path for another mod developer to build a
similar importer. The same architecture works for another planner site or a
local blueprint file.

## 1. Define the trust boundary

Accept a UUID, not an arbitrary URL. Construct the fixed Quick Share endpoint
yourself, use HTTPS, set a short timeout, cap response and collection sizes, and
validate all prices, dimensions, coordinates, and item IDs. Never let website
data select a CLR type, prefab path, method, or arbitrary native registry ID.

## 2. Build runtime-neutral models

Model only the fields needed for planning:

- property type;
- floor cell matrix;
- placed item ID, origin, footprint, and price;
- route endpoint IDs if you intend to support logistics;
- explicit pot component IDs;
- employee count or assignments if you intend to support hiring.

Keep these models free of Unity, MelonLoader, and IL2CPP types. They can then be
unit-tested in an ordinary .NET process.

## 3. Parse manually and normalize versions

Parse the envelope and inner document separately with `JObject`/`JArray`.
Manual token access avoids IL2CPP generic-deserializer initialization failures
and makes legacy fallbacks explicit. Perform parsing on Unity's main thread;
keep only the network request on a worker.

Normalize current and legacy fields into one canonical model. Use allowlisted
maps for legacy item/component names. Unknown physical items should stop the
import before any mutation and produce a useful error containing the source
identity and floor index.

## 4. Maintain an explicit item catalog

For each supported website item, record:

```text
website ID -> native item ID + canonical width + canonical height
```

Resolve native definitions only after game registries are ready. Verify that
each result is a grid-buildable definition. Treat configured assemblies such as
a pot/rack/light as recipes rather than pretending they are one native item.

## 5. Create an immutable plan

For every placement:

1. subtract the website perimeter;
2. compare the source footprint with the catalog footprint;
3. infer 0 or 90 degrees from normal/swapped dimensions;
4. validate floor bounds and price;
5. accumulate the physical-item total;
6. record the source object for later recipe expansion.

Do not look up tiles or create native objects yet.

## 6. Map website floors to native grids

Enumerate only the grids belonging to the owned target property. Try mapping in
increasing order of complexity:

1. exact dimension match;
2. a cropped region that contains every floor placement;
3. equal-size segmentation guided by non-`-1` cells;
4. a composite partition of unequal grids.

For a composite partition, enumerate candidate rectangles for each grid in
normal and swapped dimensions. A valid solution must cover every placement
exactly once without overlapping rectangles. Break ambiguous solutions by
comparing normalized pairwise distances between candidate region centers and
native grid centers.

## 7. Resolve orientation and validate tiles

For each mapped region, try all combinations of axis swap and X/Y reflection.
Transform every rectangle, then require each covered tile to:

- exist;
- be buildable;
- have no buildable occupant;
- be used by no other planned placement.

Complete this pass for every item before creating anything.

## 8. Create native objects

Use the game's native build manager so ownership, networking components, and
save serialization match ordinary built furniture. For composite recipes:

- create the grid-mounted support/base objects;
- retrieve their native procedural tiles;
- create attached procedural items with the expected tile pairs.

Generate fresh persistent GUIDs for every native object. Count returned objects
and compare the result with the plan's expected count.

## 9. Charge after success

Check the balance before mutation. After every native object is created, add one
negative online transaction for the planned physical-item total. Do not charge
for skipped employees or other unsupported systems.

For production use, add a rollback journal covering every created object. The
POC validates everything up front but cannot atomically reverse a late native
exception.

## 10. Decide how to handle non-physical systems

Routes and employees are separate game systems with lifecycle, persistence,
and potentially network authority. Either implement them end-to-end or skip
them explicitly with warnings. Silently charging for data you did not recreate
is incorrect.

## Dual-runtime checklist

- Compile Mono and IL2CPP independently.
- Keep runtime aliases near native imports.
- Do not use LINQ assumptions on IL2CPP-native collections.
- Avoid generic JSON deserialization at the IL2CPP boundary.
- Do not access registries during early Melon initialization.
- Run the same public-share fixtures in both actual game runtimes.
- Keep assemblies, generated wrappers, decompiled output, saves, and exported
  Unity assets out of the repository.
