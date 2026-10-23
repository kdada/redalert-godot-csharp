# Official-map object restoration

Scope: only the actual immutable objects preserved in the five selected original maps. No original or third-party game implementation was fetched or copied. Effects owns the original artwork, metadata and new partial consumer; Lead owns main ArtLibrary/Battlefield hooks and native validation; terrain owns the pure simulation placement/collision table.

## Coverage and provenance

| Map | Terrain | Structures | Civilian vehicles | Smudges | Total |
|---|---:|---:|---:|---:|---:|
| mp01t4 | 156 | 29 | 0 | 0 | 185 |
| mp02t2 | 206 | 25 | 5 | 55 | 291 |
| mp08t2 | 249 | 0 | 0 | 0 | 249 |
| mp23t4 | 549 | 6 | 0 | 0 | 555 |
| mp25t6 | 365 | 0 | 0 | 0 | 365 |
| Total | 1525 | 60 | 5 | 55 | 1645 |

The 91 distinct names are 34 terrain types, 31 neutral structures/urban props, five parked civilian vehicles and 21 smudges. All 91 original Rules and Art sections are present. Generated art covers every mandatory base plus eight original standing/active layers: 92 unique SHP conversions and five complete original VXL/HVA body assemblies. Required and optional missing lists are both empty.

102 source members are recorded in `Assets/Reference/Visual/MapObjects/sources.json`, including exact MIX nesting, offsets, lengths and SHA256. Eight TREE01..08 sources already retained in Visual/Scenery are reused read-only. The remaining 94 members required 2,305,897 new bytes, fetched only by exact HTTP206 ranges from pinned revision `acfa5b834899e7fab4c8499ccb4b49c402927b44`. The dedicated fetch state has a 32 MB cap; no complete archive was downloaded and the prior expansion fetch state was not modified.

## Shared static metadata

`Assets/Generated/ClassicExpansion/MapObjects/metadata.json` has schema1 and an `objects` dictionary keyed by original Name. Each definition preserves kind, original Rules.Image/Art.Image, actual Art.Foundation width/height, passability/buildability policy, native palette, theater flags, raw occupation bits, original layers, and complete original Rules/Art scalar dictionaries. Source Rules and Art SHA256 values are embedded. All numeric collision fields were frozen before the shared simulation table was generated.

Structures use their actual Art.Foundation, including CANWY26 5×3, CAPARS13 3×4 and CAMIAM01/02 4×2. Terrain and parked cars use one cell. Smudge Width/Height comes from its original Rules and describes its existing artwork extent, without blocking movement/building. Raw records retain original coordinates; only SourceOrigin is subtracted by the shared parser.

TemperateOccupationBits describes allowed infantry spots inside one cell, rather than a multi-cell rectangle. The public descriptive [terrain flag dictionary](https://wiki.ra2diy.com/w/8.%E5%9C%B0%E5%BD%A2%E5%AF%B9%E8%B1%A1%E8%AF%8D%E5%85%B8) documents 0=left/bottom/right, 1=left/bottom, 2=bottom/right, 3=bottom, 4=left/right, 5=left, 6=right, 7=none. These raw integers are retained without inventing a 3×3 collision footprint. The existing simulation's cell-sized static obstacle policy does not implement original infantry subcells, crushing, scenery destruction, garrisoning or neutral-tech capture.

Ordinary terrain and smudges use original isotem.pal. The three SpawnsTiberium ore-drill types use the documented unit-palette class (unittem.pal for the selected temperate maps), instead of the terrain palette. This class exception was reconciled after the initial asset publication; final catalog metadataSha256 equals the final metadata, and the three final sprite keys carry `_unittem`. Prior wrong-palette cache entries are not referenced by the final catalog.

## Consumer contract

`Scripts/ArtLibrary.MapObjects.cs` provides private partial loader hooks `OriginalMapObjectSpriteFolder(name)` (complete res:// directory ending in /) and `OriginalMapObjectVehicleManifest(name)` (complete res:// manifest path), plus `DisposeOriginalMapObjectMetadata()`. Textures are owned and released by the existing Sprite/Vehicle caches; metadata and record lookups are cleared by the new disposal hook.

Public APIs:

```csharp
bool DrawOriginalMapObject(CanvasItem canvas, ClassicMapObject item,
    Vector2 groundAnchor, float zoom, Color light, int tick);
Rect2 OriginalMapObjectBounds(ClassicMapObject item,
    Vector2 groundAnchor, float zoom);
bool HasOriginalMapObjects { get; }
int OriginalMapObjectTypeCount { get; }
```

Host input is the actual source first-cell center for terrain, vehicles and smudges: ProjectGround(X+.5,Y+.5). Structure input is its real foundation center: ProjectGround(X+Width/2,Y+Height/2). The source record is never mutated. The five parked vehicle facings come from the original 0..255 record field and select one of the original 32 baked directions.

Original tree/post canvas centers and signed X/Y draw offsets are retained. Structure ground registration uses native crop feet and actual foundation geometry. Original smudges are complete images in a 120×120 canvas, not per-cell component frames: source anchor (60,75) registers their first cell, and larger 2×1/1×2/2×2 extents already exist in the original pixels. They must not be tiled, stretched or recentered on their cropped bounding box. Shadows precede color layers, neutral source colors are not remapped, and supplied cell lighting is applied to all parts.

These are static scenery: source base damage state and original standing layers are retained; the API does not add neutral-structure interaction or simulation animation state. Native overlap/ground placement and scene integration remain Lead's checks.

## Validation and reproduction

Final `MapObjects/Catalog/validation.json` passes: all 102 source sizes/hashes, all 618 original SHP frame crop-index arrays, remap masks against original indices, 172 independent native RGBA frame comparisons and 160 actual nonempty civilian-vehicle body facings. All 92 sprite manifests have one image set. The largest atlas is 813,288 pixels, below the runtime 16,777,216 limit. VXL/HVA and palette hashes are bound to their original members; directions=32, columns=8 and common native baker anchor are retained.

The original oil-derrick flag ctoild_f has 16 color frames and no shadow half. Its manifest deliberately uses `shadowLayout: unspecified`. The other 91 SHPs have independently verified 0/1/empty second-half shadow masks; color counts are derived from paired-frame metadata rather than unconditionally halving every file.

Actually inspected original tree, multi-cell scorch, corrected ore drill, Miami lifeguard tower, bridge repair hut, oil derrick and 32-facing taxi images. These are original source/artifact previews, not native game screenshots. Transparent palette-index0 pixels retain their original RGB while alpha remains zero.

```sh
python3 Tools/classic_art/prepare_map_object_metadata.py
python3 Tools/classic_art/prepare_map_object_art.py --plan --fetch --prepare --validate --preview
```

The tools require existing cached conversion DLLs, and fail if they are absent. They never build tools or the game and never launch native gameplay. All child/background jobs completed and were collected.
