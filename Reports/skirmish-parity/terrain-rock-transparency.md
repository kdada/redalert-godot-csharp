# Native TMP transparency and original overlay rocks

Task32 is complete. Native blue polygons were caused by palette index0 in the TMP diamond producer. The original rock overlays that already blocked movement now also draw their actual artwork. Existing map physics, original map records and the separate 91-type MapObjects catalog are unchanged.

## Captured defect and confirmed cause

The retained [forest capture before correction](visual-20261021-185005/mp23t4-forest-0.png) contains opaque blue polygons near (1300,1250) and (1180,1300). The [normal gameplay capture before correction](gameplay-20261021-185126/native-gameplay.png) shows the same polygons near the upper-right ore area. Neither evidence image was deleted or modified.

Decoded forest PNG samples at (1300,1250), (1290,1255), (1180,1300) and (1200,1298) are all exactly RGB(0,0,124). That is the original isotem.pal index0, stored as VGA RGB(0,0,31) and expanded by channel<<2. The lower-right bounded region contained 2,420 such blue pixels. Original GOLD01..04 atlas pixels with that RGB all have alpha0, as do the restored map-object SHP color frames. SHP shadow masks draw black. The TMP extra-layer branch also already skipped index0. The diamond branch was the sole path that unconditionally wrote an opaque palette entry for index0.

[The TMP producer](../../Tools/classic_art/prepare_original_map_terrain.py) originally constructed every palette color with alpha255. Slopes and ramp-fix TMP diamonds include genuine empty support indices: the selected map uses native frames with 150, 300, 480, 578 and 676 zero entries among their 900 diamond indices. The producer therefore painted those empty areas blue. This was a TMP alpha error, rather than ore graphics, tree shadows or a MapObjects consumer error.

The correction preserves palette-index0 RGB exactly and changes its alpha from 255 to 0. All nonzero indices retain alpha255. There is no replacement ground color, synthetic support, geometry change or object-placement adjustment.

## Data validation

Repacked all 331 native TMP templates, 1,179 frames, 1,438 layers including 259 extra layers into the same 2048×1535 atlas. All five selected maps remain completely covered with missing0. The [terrain validation](../../Assets/Generated/ClassicExpansion/Terrain/validation.json) passes original indices, raw depth and interpreted native RGBA comparisons.

The parent independently retained the old atlas in memory before repacking and compared every old/new RGBA pixel afterward:

- Exactly 24,191 pixels changed, each from RGBA(0,0,124,255) to (0,0,124,0).
- Every other RGBA byte is identical.
- Original indices.bin and depth.bin SHA256 values are identical.
- Every frame's raw offsets, atlas regions and cell-center offsets are identical.
- Atlas dimensions and the existing Level×15 projection contract are unchanged.

The repack/verification background job completed exit0 and was collected. Effects did not build or launch the game.

## Actual remaining overlay coverage

Resource overlays are excluded by decoded ResourceKind, because original TIB*/TIB2* names refer to gold/gem images. After also excluding bridge overlays, the maps contain 448 remaining cells and 11 names. These consist only of the following ten original rock images plus RUBBLE_OVERLAY, whose original Rules.Image is SROCK01, DrawFlat=true and IsRubble=yes.

| Original name | Cells |
|---|---:|
| TROCK01 | 53 |
| TROCK02 | 33 |
| TROCK03 | 36 |
| TROCK04 | 36 |
| TROCK05 | 27 |
| SROCK01 | 25 |
| SROCK02 | 50 |
| SROCK03 | 26 |
| SROCK04 | 49 |
| SROCK05 | 97 |
| RUBBLE_OVERLAY → SROCK01 | 16 |
| Total | 448 |

Per-map counts are mp01t4=59, mp02t2=31 including 16 rubble, mp08t2=38, mp23t4=160 and mp25t6=160. No additional overlay type or simulation entity was invented.

## Rock source, consumer and validation

The independent [rock catalog](../../Assets/Generated/ClassicExpansion/MapObjects/Rocks/catalog.json) has schema1, complete=true, ten actual sprite resources and eleven original-name mappings. All required and optional missing lists are empty. Existing five TROCK TEM files were reused read-only. Only five SROCK files were fetched, using exact HTTP206 ranges at the same pinned original-data revision, totaling 3,888 bytes against an independent 1 MB cap. Exact source URLs, ranges, MIX offsets, sizes and hashes are retained in the [rock source ledger](../../Assets/Reference/Visual/MapObjects/Rocks/sources.json). The parent 32 MB MapObjects state and its 91-type metadata/catalog were untouched.

[The independent rock validator](../../Assets/Generated/ClassicExpansion/MapObjects/Rocks/validation.json) passes ten source SHA/size checks, 20 native index arrays and frame pixel SHA values, ten original color RGBA comparisons, ten independent original shadow-coverage comparisons and 20 remap masks. All 4,809 index0 pixels are transparent. Each original SHP has one color frame plus its real shadow. The largest atlas is 1,768 pixels. TROCK and SROCK representative images were actually opened and inspected.

[The original terrain partial](../../Scripts/ArtLibrary.OriginalMapTerrain.cs) exposes these frozen APIs:

```csharp
bool DrawOriginalMapRockOverlay(CanvasItem canvas, ClassicMapCell cell,
    Vector2 groundCellCenter, float zoom, Color light);
Rect2 OriginalMapRockOverlayBounds(ClassicMapCell cell,
    Vector2 groundCellCenter, float zoom);
bool HasOriginalMapRocks { get; }
int OriginalMapRockTypeCount { get; } // ten real source images
```

The private OriginalMapRockSpriteFolder(name) hook gives the main Sprite loader its existing cache/texture ownership. Disposal clears rock metadata through the existing original-terrain disposal hook. OverlayData selects only native color states; native shadow precedes color, neutral palette colors remain unremapped, and supplied lighting is applied. TROCK canvases are 96×96 with anchor(48,48); SROCK canvases are 120×120 with anchor(60,60). SourceRect offsets are preserved without cropped-image recentering. Ground input already includes original cell Level once. Lead integrates ordinary rock depth at (x+y+1)×Scale with actors and draws the DrawFlat rubble alias in the ground pass. Core collision remains intact.

Reproduction uses only existing cached conversion DLLs, never a tool/game build:

```sh
python3 Tools/classic_art/prepare_original_map_terrain.py --prepare --validate
python3 Tools/classic_art/prepare_map_overlay_rocks.py --plan --fetch --prepare --validate --preview
```

## Final real Native evidence

Lead reports a successful centralized full build with zero warnings/errors in 31.92s and a fresh Native process token11. Effects then read the complete [final seven-capture report](visual-20261021-191245/report.json) and actually opened the [final forest GPU screenshot](visual-20261021-191245/mp23t4-forest-0.png). The former lower-right blue polygons are absent, original boulders now appear among the trees, and the original tree/shadow/shore/cliff rendering remains present. These images come from the running native game, not generated asset previews.

All seven captures report MapObjectTypes=91, RockOverlayTypes=10, mapObjectDrawFailures=0, rockOverlayDrawFailures=0 and opaqueTerrainBluePixels=0. The report is completed=true, passed=true and running=false.

| Native scene | Map objects actually drawn | Rock overlays actually drawn | Original bridge pieces | Opaque terrain blue pixels |
|---|---:|---:|---:|---:|
| mp01t4 city | 16 | 4 | 0 | 0 |
| mp02t2 city | 46 | 16, including rubble | 0 | 0 |
| mp23t4 forest | 69 | 9 | 0 | 0 |
| mp23t4 high bridge | 11 | 0 | 14 | 0 |
| mp01t4 naval | 0 | 0 | 31 | 0 |
| mp08t2 buildings | 42 | 3 | 0 | 0 |
| mp08t2 special roster | 40 | 3 | 0 | 0 |

All code and generated artwork remain frozen. The asset child and all its background jobs completed and were collected; Effects has no pending job and did not independently build or launch Native. Lead continues its final normal-gameplay verification separately.
