# Blueprint JSON and site reverse engineering

This document records the observed public Quick Share contract and the method
used to derive it. It is a behavioral description, not a copy of the site's
source code or assets.

## Method

1. Save the public editor page for offline reference.
2. Inspect its HTML and referenced application chunks to locate Quick Share and
   public-load behavior.
3. Create or open public shares in the editor and compare visible floor sizes,
   item counts, prices, rotations, routes, and pot configurations with the API
   response.
4. Compare several property types and older shares to identify schema drift.
5. Compare website floor geometry with locally owned native property grids.
6. Confirm every inferred transform by importing into disposable save copies on
   both Mono and IL2CPP.

The saved page did not contain the complete application chunk, so live public
shares were the authoritative contract. No authentication, private records, or
non-public endpoints were used.

## Quick Share endpoint

Public blueprints are retrieved with:

```http
GET https://scheduleoneeditor.com/api/blueprints/get?id=<uuid>
```

The response is an envelope. Its `blueprint_data` field is itself a JSON string:

```json
{
  "blueprint_data": "{\"type\":\"bungalow\",\"floors\":[...],\"hiredEmployees\":[...]}"
}
```

Treat both layers as untrusted input. Require a UUID, use a fixed endpoint, set
request and size limits, and reject unsupported values before touching game
state.

## Document shape

The decoded document has this observed shape:

```json
{
  "type": "warehouse",
  "floors": [
    {
      "blueprint": [["-1", "OT", "0"]],
      "placedItems": [
        {
          "id": "editor-instance-id",
          "itemTypeId": "drying_rack",
          "name": "Drying Rack",
          "blueprintX": 12,
          "blueprintY": 7,
          "width": 3,
          "height": 2,
          "price": 250,
          "destinationRoute": {
            "start": { "id": "source-instance-id" },
            "end": { "id": "destination-instance-id" }
          }
        }
      ]
    }
  ],
  "hiredEmployees": []
}
```

Cell strings describe the editor's floor mask and visual boundary types. `-1`
is non-floor space. The importer needs only the rectangular dimensions and
whether a cell is present when it partitions floors; it does not depend on the
meaning of each visual boundary code.

## Coordinates and rotation

- Coordinates and footprints are integer cells; one cell corresponds to the
  native 0.5 m build grid.
- Floor templates include a one-cell perimeter.
- Native coordinates are therefore website coordinates minus `(1, 1)`.
- Native floor dimensions are normally website dimensions minus two.
- Rotation is represented by swapping `width` and `height`, not by a rotation
  field.
- Whole-floor reflections and quarter turns must be considered because the
  website coordinate frame does not uniquely identify the native grid frame.

## Schema variants

Current shares use `itemTypeId`. Older public shares may omit it entirely and
identify a placement only through `displayName` or `name`. A compatible parser
should prefer fields in this order:

1. `itemTypeId`
2. a recognized `displayName`
3. a recognized `name`

Custom labels belong in `name`, so `displayName` is the safer legacy fallback
when present. Keep the fallback allowlisted; do not derive arbitrary game item
IDs from user-controlled labels.

Composite pots use `potConfiguration` with `pot`, `light`, and `extra`
components. Current components can include an `id`; older shares can expose
only names such as `Air Pot`, `Full Spectrum Grow Light`, and `Suspension Rack`.
Those names also require an explicit compatibility map.

`destinationRoute.start` and `.end` are nullable nested placement objects in
real shares, not string IDs. Extract their `id` fields. Serializing a route as
`string` is the source of the common `Unexpected character ... Path
'...destinationRoute.start'` failure.

## Property geometry findings

Website floors are presentation canvases rather than a one-to-one list of
native grids:

- Simple properties usually have one exact interior grid.
- Multi-room properties combine equal native grids within one sparse matrix.
- The Barn uses a larger first-floor canvas around distinct lower/mezzanine
  native grids.
- The Warehouse second floor combines two long catwalk grids and a smaller
  office grid in one irregular mask.

Do not maintain a table of hard-coded offsets per property unless there is no
alternative. Matching dimensions, valid cells, placed-item containment, and
native grid topology is more resilient to editor and game updates.

## Drift policy

The editor does not advertise a versioned public schema. Preserve unknown
fields, reject unknown physical items clearly, log the failing floor/item, and
keep parser fixtures for every schema variant encountered. Re-run the public
share inspection whenever the site or game changes.
