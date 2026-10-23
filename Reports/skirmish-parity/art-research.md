# Original vanilla RA2 artwork research

## Cursor delivery

The original RA2 `mouse.shp` was extracted from `ra2.mix → conquer.mix`, and the original `mousepal.pal` from `ra2.mix → cache.mix`. The pinned asset source is revision `acfa5b834899e7fab4c8499ccb4b49c402927b44` of the existing authorized binary-data repository. Only bounded HTTP 206 ranges were fetched, totaling 374,270 bytes including archive indexes. No game implementation was fetched or copied.

| Original file | Bytes | SHA-256 | Exact source range |
|---|---:|---|---|
| mouse.shp | 359,800 | `e5a356737787d681dd1a2b1255c7c7d1e9bd8334e5674705f2ccc39cd12634df` | [ra2.mix.part00](https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/acfa5b834899e7fab4c8499ccb4b49c402927b44/game-res/ra2.mix.part00), bytes 13404896–13764695 |
| mousepal.pal | 768 | `4dcbe78b534a58fc972bce4b25aa58a2a56c9a5bb0ffbfcf5d6194be5dbde60c` | [ra2.mix.part03](https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/acfa5b834899e7fab4c8499ccb4b49c402927b44/game-res/ra2.mix.part03), bytes 36985249–36986016 |

The source is 450 frames on a 55×43 native canvas. There are 449 raw format-1 frames and one raw format-0 frame (144, an adjacent SellUnit animation). Source palette indices, original crop coordinates, and transparency are preserved. The generated atlas is 944×1363. Every region is a full native canvas, including the original frame X/Y placement; draw the region at `pointerPosition - hotspot * scale`, without adding sourceRect again. Use nearest sampling.

| Action | Original frame indices | Numeric hotspot on 55×43 canvas | Independent original-image inspection |
|---|---|---|---|
| arrow | 0 | 0,0 | Original white/cyan RA2 arrow, tip at top left |
| sell | 129–138 | 27,21 | Native rotating dollar symbol |
| sell_invalid | 149 | 27,21 | Native red prohibition sign over dollar symbol |
| repair | 150–169 | 27,21 | Native wrench with green arrows |
| repair_invalid | 190 | 27,21 | Native red prohibition sign over wrench |

All five action previews were actually read with `read_image`. They confirm the selected ranges identify the requested actions in this original RA2 asset. The adjacent SellUnit 139–148 and EngineerRepair 170–189 modes were not substituted.

The [descriptive default cursor table](http://ares-developers.github.io/Ares-docs/_downloads/MouseCursors.txt), linked by [Ares cursor documentation](https://ares-developers.github.io/Ares-docs/new/mousecursors.html) and [ModEnc](https://modenc.renegadeprojects.com/MouseCursors), supplies frame/count/interval and categorical hotspots. These documents target Yuri's Revenge; their shared frame prefix was checked against the extracted RA2 images. The SHP and PAL themselves contain no action names, click-hotspot table, or animation clock. Center/Middle are represented by the integer canvas center (27,21).

Animated repair and sell modes use documented interval 4. The catalog's nominal 60 Hz conversion to 4/60 seconds is explicitly marked as an inferred render-clock choice, not an original timing value encoded in SHP. Static states remain static. No claim of measured vanilla animation cadence is made.

`python3 Tools/classic_art/prepare_cursors.py --prepare --validate` passed: all 450 full-canvas RGBA frames and all original palette-index buffers match their atlas entries. This is an asset check, not native UI integration validation. No build or game launch was performed by this asset task.

Runtime delivery: `Assets/Generated/Cursors/catalog.json` and `atlas.png`. The catalog exposes `frames[index].region` and `cursors[arrow|repair|repair_invalid|sell|sell_invalid]` with frame lists, hotspots and animation fields. Original provenance is retained in `Assets/Reference/Cursors/sources.json`.

## Official-map exact TMP terrain

The original 331 TMP files referenced by the five official maps are packed into `Assets/Generated/ClassicExpansion/Terrain/catalog.json` and `atlas.png` (2048×1535). There are 1,179 original subtiles and 1,438 native layers, including 259 extra cliff/overhang layers. Original palette indices and raw depth payloads are retained in packed channels. No source tile is substituted with generated shore masks, a generic clear tile, a warped quad, or a synthetic cliff wall.

All 1,179 raw TMP header positions equal their source subtile grid positions. The per-cell registration is therefore `rawLayerOffset - sourceSubTileGridOffset - (30,15)`, and every diamond starts at `(-30,-15)` relative to its projected cell center. The center is already projected by original `cell.Level * 15`; applying `templateHeight` or template `maxHeight` again would double-raise parts of the terrain. `SurfaceLevel` is reserved for units and derived bridge decks, and must not re-raise the base TMP water or bridge-head tile.

`ArtLibrary.OriginalMapTerrain.cs` exposes `DrawOriginalMapTile`, native bounds, extra-layer availability and `DrawOriginalMapTileForeground`. Its source frame lookup uses the original TileIndex/SubTile pair, preserves sparse source indices and only normalizes the explicit clear sentinels -1/65535 to tile 0. The foreground helper redraws original extra pixels by default; the host may include original top pixels only for a genuinely higher foreground cell, excluding an actor's own floor. The existing texture cache owns GPU texture reuse and disposal.

Pixel/index/raw-depth validation passed for every packed layer. All five map datasets are fully covered: South Pacific 19,855 cells, Isle of War 19,900, Little Big Lake 6,320, Hammer and Sickle 19,879, DEFCON 6 24,054; total 90,008 cells with zero missing TileIndex/SubTile pairs. Source bytes remain identified by per-template SHA-256 and the frozen map tile mapping.

Actually inspected source-assembly previews: South Pacific coast (continuous native sand/wet/water), Isle of War cliffs (native road/rock face/high ground/water edge), Hammer and Sickle cliffs (native high/low plateaus) and Little Big Lake coast. These are explicit offline terrain-only assemblies, not native game screenshots. Lead owns the Battlefield integration and native unit-footpoint/foreground verification. No build or game launch was performed here.

## Complete skirmish artwork

Completed source expansion in `Assets/Generated/ClassicExpansion/catalog.json` (`schema:1`, `complete:true`): 51 unit variants representing 47 logical types plus 4 original auxiliary aircraft/missiles, 38 building variants, 174 sprite conversions, 44 complete voxel assemblies/alternate states, and 115 original 60×48 RGBA cameos. Reused 68 exact source/palette-matching sprite conversions and 10 complete voxel assemblies; 106 sprite conversions and 34 voxel assemblies are new. Country-aware RulesId/ArtId/Image/Cameo metadata comes from the independently verified original roster, without a new switch over unit names.

All 405 original source files pass SHA/size verification. The structural check covers 17,844 SHP frames, image/index/mask artifacts and 2,016 actual nonempty voxel part facings with original HVA/common-anchor identity. Every model retains 32 directions, 8 columns, native source geometry and the existing independent baker's scale/elevation. All 174 SHP sources were independently pixel checked (187 representative native frame comparisons), and all 115 cameos match original SHP/PAL pixels exactly; zero mismatches or pending items. New alpha-correct RGBA cameos repair 13 legacy indexed PNGs lacking transparency metadata. Actually inspected native carrier, Black Eagle and MCV composites, naval-yard art and an MCV cameo; these are asset previews, not game screenshots.

All mandatory assets are present. Nineteen optional references represent 17 unique absent candidates: thirteen conventional barrel filenames, two unused elite icons and two original animation IDs (GAREFNL4/GAGAP_A, one referenced twice). Missing items remain explicit in the catalog; no pixel substitute is invented. The original NGMISL base is a 152-byte SHP with six empty codec4 headers. A disclosed unused empty-codec normalization allows the existing converter to retain the standard manifest schema; original bytes/hash/compression are preserved. The visible silo comes entirely from original animation layers, and the consumer handles an empty base without drawing a placeholder.

`Scripts/ArtLibrary.Expansion.cs` now consumes the metadata for infantry, sprite vehicles, voxel vehicles, buildings and the four real `ClassicProjectile` auxiliary types. Source action ranges drive Ready/Guard/Walk/Idle/FireUp/Deployed/Prone selection. Non-sequence SHPs derive the facing count from source frame spans: DRON and DLPH have eight, SQD four. Original passenger IFVMode and turret mapping select FV variants; charge-turret state selects SREF variants; V3/DEST/DRED retain their original empty-launcher models. Building layers retain original normal/damaged pairing, raw loop boundaries, source X/Y offsets, powered pauses, original shadows and construction frames. Presentation animation cadence is a nominal conversion of retained source Rate values; exact engine clock units have not been claimed. Ground anchors use existing calibrated offsets where available and native crop feet/foundation geometry elsewhere; Lead owns native placement verification. No presentation clocks or apparent-owner arguments mutate simulation state.

`Tools/classic_art/prepare_consumer_metadata.py` additionally retains original Art scalars and selected Rules scalars omitted from the compact catalog, including IFVMode, ToOverlay/DamageLevels, frame spans and turret offsets. The new consumer shares existing Sprite/Vehicle cache ownership and exposes the three DrawExpanded hooks, auxiliary-projectile draw, exact resource-folder/manifest hooks and DisposeExpansionMetadata. Lead exclusively owns edits to the main ArtLibrary and Battlefield entry points.

Source transfer was 19,297,565 bytes within the 64 MB budget, using exact HTTP206 ranges from the pinned binary-data revision and original SHP/VXL/HVA/PAL/index data only. Source ledger and independent pixel evidence reside under `Assets/Reference/ClassicExpansion/`. No original or third-party game implementation, full archive, build or game launch was used for this asset work.

## Original bridge overlays and dynamic crates

The missing high bridge art was located in the real `ra2.mix → temperat.mix` archive (distinct from `tem.mix`/`isotemp.mix`). `bridge.tem` and `bridgb.tem` retain all 72 original frames; original CRATE/WCRATE add four frames. The resulting schema2 overlay catalog and 2048×790 atlas live in `Assets/Generated/ClassicExpansion/Terrain/Bridges/`, with 36 foreground layers copied strictly from source exterior pixels. Every source frame RGBA/index buffer and every foreground subset passed validation. Pure index1 second-half shadow masks are drawn as black coverage rather than cyan source palette pictures.

The append-only methods in `ArtLibrary.OriginalMapTerrain.cs` provide original bridge overlay/bounds/foreground and original crate draw. Bridge selection uses only the real source OverlayName/OverlayData; no span is reconstructed from bounds. Genuine low bridge empty frames return handled with zero bounds. Source South Pacific has 60 such empty cells, Isle of War nine colored high cells, and Hammer and Sickle 27 high plus thirteen colored low cells and 33 empty low cells. Source bridge-head/TMP and SHP registration was actually inspected in an intact Hammer and Sickle assembly and a broken Isle of War assembly; the latter preserves its original missing middle cells. These are offline source assemblies, not native game screenshots. Second-axis and wooden high bridge registration is derived from source geometry because the five selected maps do not contain those variants. High foreground conservatively redraws exterior near-side structure and does not guess above-deck cap materials.

Crate draw accepts only land/water state and a projected center, never the hidden reward kind. CRATE and WCRATE each contain one original color frame and one shadow; no animation is invented from the supplied tick. Original hashes/ranges are recorded in `Assets/Reference/ClassicExpansion/MapOverlays/sources.json`. Lead owns the actual World.ClassicCrates/Fog iteration and Native proof.
