# Original ore, gems, density and remaining quantity

The corrected TEMPERAT resource build now passes Lead's fresh real Native run with all eight captures, depletion, actual harvest, hidden-quantity protection and live tooltip checks. Effects read the complete new report and actually opened its live-tooltip GPU screenshot: ore is gold and gems are multicolor, with the tooltip showing150 credits/6bales. The initial ISOTEM palette error and its retained Native evidence are documented below. Effects did not build or launch Native. Lead's subsequent normal-production naval check also passed, and its actual sea-combat screenshot confirms the former empty tooltip frame is gone. Task39 is fully accepted.

## Confirmed causes

1. Both original-map and legacy-landscape loops interleaved each ground tile with its resource patch. A later native TMP diamond/extra could repaint an already drawn resource. Both loops now finish all ground first, then draw resource overlays, before bridges/actors. The original-map change is confined to the ground/ore hunk; terrain's independent intact/broken bridge gates were reread and preserved through a filesystem stale-version retry.
2. Battlefield.Mining ignored ResourceKind and always called GOLD01..04. A gem field therefore never chose GEM art.
3. Its `(amount-1)/180` density did not match the actual simulation amount: ClassicMapRepository initializes `Map.Ore=(ResourceDensity+1)*ResourceValue*4`. OreValue25 means 100 credits per density step; GemValue50 means 200. A full 1,200-credit ore cell was drawn at density6 instead of11. Drawing now uses `(amount-1)/(value*4)`, clamped0..11, and draws nothing for amount<=0.
4. The old GOLD01..04 are native24×24 legacy images, whereas the selected original maps name TIB## / TIB2_## / GEM01 and require their actual60×60 source images. Read-only exact64-byte MIX header probes confirmed TIB01/TIB07/TIB20 are60×60/24frames and GOLD01 is24×24/24frames. Original Rules/Art do not declare a TIB→GOLD alias. The consumer now uses exact original Image lookup, not a fabricated GOLD family mapping or three reduced/repeated clusters.
5. Lead's real Native visual review found the first resource extraction used ISOTEM, which is the TMP palette. This made TIB ore look like rainbow crystals and GEM look gray/green. The original cache.mix contains the distinct TEMPERAT.PAL at absolute offset247057137,768 bytes, SHA256 `5903b69868b84f494cfbb4e7100398015ef9775b37726019a0d7b5fb6cb33b55`. Under identical original TIB01 indices, index12 changes from ISOTEM RGB(0,124,244) to TEMPERAT RGB(252,244,96). Actual original-resolution comparison produces gold TIB and multicolor GEM. UNITTEM is also incorrect: GEM01 frame11 would gain50 pure-magenta pixels. The palette is now separately retained in the original exact-range ledger and bound by all twelve current manifests.

## Authoritative original resources

The [OriginalCell catalog](../../Assets/Generated/Resources/OriginalCell/catalog.json) is the sole game-consumed resource catalog. Its 18 actual Overlay names cover all 3,393 resource cells in five maps: 2,649 ore and 744 gems. They map to12 real images: GEM01 and TIB01,02,03,04,05,06,07,08,10,11,12. TIB2_## maps to TIB## only when original Rules.Image says so. GEM01 maps directly to GEM01. Unused TIB09/20 and other GEM variants were not added.

All12 sources preserve native60×60 canvases and all24 original frames. Frames12..23 are genuinely empty and are not invented shadows. The explicit twelve density frames prevent an empty-tail selection. SourceRect/atlas bounds are retained; the renderer paints one native image at full zoom, neutral/unremapped, with supplied ground lighting. Existing Sprite/Texture cache and disposal own the resulting GPU images.

Original density art lies in the canvas's lower half, corresponding to native cell diamond y30..60. For the projected **ground cell center**, the registration pivot is `(30,45)`, while header canvas center remains `(30,30)`. For example TIB01 frame11 source crop `(4,31,52,25)` becomes ground-relative `(-26,-14,52,25)`; GEM01 crop `(7,31,42,26)` becomes `(-23,-14,42,26)`. Using `(30,30)` would place the piles15 native pixels forward. This registration follows the authored ground diamond and is recorded as a rendering registration; no independent encoded SHP ground pivot was invented. Lead's Native captures provide final visual confirmation.

### TIB02 density inference

The original TIB02 frame0 has414 opaque pixels. Frames1..11 have `[7,17,25,44,60,83,113,158,218,284,341]`. All other11 actual source images grow monotonically in original frame0..11 order. The consumer therefore uses only for TIB02 the explicit cycle `[1,2,3,4,5,6,7,8,9,10,11,0]`, equivalent to `(simulationDensity+1)%12`. This is an **inferred visual-density consumption mapping supported by original opaque coverage**, not proof of the original game's OverlayData→SHP-frame formula. It prevents resource depletion from visibly enlarging a pile. It is not a generic arbitrary pixel sort. Original source bytes, manifest frame order, raw indices and offsets are unchanged by this density selector, and the audit exposes the actual selected sourceFrame. The source catalog retains raw densityFrames and separately records the measured order.

## Remaining quantity and privacy

[Battlefield.Mining](../../Scripts/Battlefield.Mining.cs) reads the actual amount only after the existing Controller.ViewExplored/ViewVisible APIs permit it. Those APIs delegate to World; no visibility-bit definitions or FogOfWar behavior are duplicated. Unexplored/unplayable cells return no description and no quantity field in their audit. Amounts from a genuinely nonvisible previously observed cell use only viewer-specific memory, marked as a last observation; hidden extraction does not update that memory. Changing the world, player or spectator state resets the memory.

Native Godot hover text identifies ore versus gems, the current remaining credits, approximate physical bales and value per bale. It explicitly shows depletion at zero. The custom native TooltipLabel refreshes each drawn frame, so a stationary hover updates as the real harvester extracts, and a rehidden cell loses its quantity text. The renderer also removes the resource image immediately at amount0. Neither this presentation nor the checker changes economy, resource amounts in normal gameplay, source maps or visibility rules.

## Completed data verification

[OriginalCell validation](../../Assets/Generated/Resources/OriginalCell/validation.json) is PASS:

- 13 source SHA/size checks (12 original SHPs plus original TEMPERAT.PAL), 288 original frame index arrays, 288 independent native TEMPERAT RGBA comparisons and 288 remap masks.
- 96,078 palette-index0 pixels remain transparent. All original24 frames per source remain intact.
- Every atlas is one image set, all PNG CRC/dimensions pass, largest atlas41,600 pixels.
- No required/optional missing source. Newly retained50,304 bytes (49,536 SHP +768 palette), exact HTTP206 only, separate1 MB cap and source ledger; existing reference states remain untouched.
- Full pre/post palette correction proof is PASS: all original SHP SHA values, all288 frame indices/SourceRect/atlas regions, indices PNGs, remap-mask PNGs, every alpha byte, canvas, anchor and density arrays are identical. Only resource RGB/palette binding changes. Stable tib/gem catalog keys point at new *_temperat manifests; older ISOTEM conversions remain unused audit material. The final repack/validate/preview job completed exit0 and was collected.
- All12 effective density sequences have positive alpha coverage and monotonically increasing visible coverage after the single explicit TIB02 inference. Independent parent contract validation checked all3,393 cells,18 aliases, native60×60 sizes, anchor30,45, image paths/bounds and value-derived density round trips.
- Parent actually opened original-resolution TIB01/GEM01 full-density previews, and the source child opened sparse/partial/full examples. Palette-index0 alpha was separately sampled in the generated atlas; the preserved blue RGB support pixels have alpha0.

[Source-frame audit](../../Assets/Generated/Resources/OriginalCell/source-frame-audit.json) and [source provenance](../../Assets/Generated/Resources/OriginalCell/Sources/sources.json) retain exact crops, pixel coverage and pin/range hashes. The reproducible [producer](../../Tools/ResourceArt/prepare_original_cell_resources.py) uses only cached conversion DLLs:

```sh
python3 Tools/ResourceArt/prepare_original_cell_resources.py --plan --fetch --prepare --validate --preview
```

The independently produced top-level Resources catalog contains the initially investigated legacy GOLD/GEM eight-image family. It is retained as rejected-source audit material; the game consumer uses only `Resources/OriginalCell/catalog.json`. It downloaded17,360 additional bytes in its separate source state and did not alter original source states. PaletteAudit previews are likewise diagnostic only. No legacy GOLD image or top-level default family influences the actual map resource consumer.

## Public Native checker and source handoff

[Main.OreVisibilityChecks](../../Scripts/Main.OreVisibilityChecks.cs) exposes:

```csharp
public void RunOreVisibilityChecks(); // async void implementation
public Godot.Collections.Dictionary GetOreVisibilityAudit();
```

The checker prepares a fresh official mp01t4 inspection world and calls public World.RevealClassicMap rather than writing visibility bits. It checks and captures full, partial and empty original ore and gems; checks the actual selected sprite/source frame, native alpha coverage, rendered amount/density and zero fallback; compares GPU framebuffer pixels in each resource region against depleted ground to prove that nonempty deposits actually appear. A separate naturally initialized World tests a genuinely unexplored resource, hidden amount mutation and reveal refresh through World.Visible/Explored APIs.

It then spawns an operational refinery/power plant and an actual ChronoMiner at an original ore coordinate, advances the real simulation20 ticks, and requires `resourceBefore-resourceAfter==miner.Cargo>0` plus matching rendered amount. It moves the actual existing native pointer to that cell, waits for the real tooltip, advances another5 extraction ticks without moving, and requires the visible native label to equal the current resource description. The temporarily shortened tooltip delay is restored. Eight native PNG captures and report.json are written under `Reports/skirmish-parity/ore-visibility-<UTC timestamp>/`. This is a short focused checker; no historical broad regression suite is called.

Frontend inspection APIs also expose resource cell and native tooltip audits, while ArtLibrary exposes source artwork audit and actual sourceFrame. Main.GetOreVisibilityAudit records sourceSpriteCount, gem/resource/native sprite/fallback counts, captures, real harvest, tooltip, phase, completed/running/passed and any error.

## Actual first Native evidence and visual correction

Effects read the complete [first actual Native report](ore-visibility-20261022-162902/report.json) and actually opened ore-full, gems-full and native-live-resource-tooltip PNGs. All8 real GPU captures pass with fallback0 and hiddenQuantityLeak=false. Ore full/partial versus depleted ground changes2,407/123 GPU pixels; gems changes607/94. Actual20-tick harvesting changes400→200 credits and loads200 credits/8bales. Another5 ticks updates the real stationary native hover label to150 credits/6bales. These are actual running-game results, not asset preview evidence.

That same visual review correctly rejected the color appearance despite the passing quantity/visibility checks: the selected ore target is original TIB01 at runtime(75,107), and the selected gem target is original GEM01 at(106,99); it is not merely the same mixed field. The screenshots used the older ISOTEM conversion. Corrected TEMPERAT gold-ore and multicolor-gem previews were subsequently actually opened, source/geometry invariance was proven, and the resource conversion was refrozen. The subsequent corrected-palette Native run below now confirms the final gold-ore/multicolor-gem appearance; the earlier screenshots and report remain intact as evidence.

Lead also found an empty tooltip frame during naval play. Battlefield.Mining now hides the actual native PopupPanel only when the resource description becomes empty and tooltip.GetWindow() is specifically PopupPanel. It never hides a generic/main game Window. Legitimate nonempty depletion text remains visible. This is the only new C# hunk after the initial Native pass.

## Corrected TEMPERAT Native verification

Lead reports the Alpha13 centralized build completed with zero warnings/errors. The [new actual Native report](ore-visibility-20261022-170133/report.json) is completed=true, passed=true, running=false and contains all8 captures. Effects read all255 report lines and actually opened the [live native quantity screenshot](ore-visibility-20261022-170133/native-live-resource-tooltip.png). The left field is now visibly gold ore; the upper-right field has distinct red, green and blue gems; the real native label displays150 remaining credits,6bales,value25. Lead additionally reviewed the normal deployment PNG and confirmed its ore field is pure gold.

| Native state | Ore credits / density / native source frame | Gem credits / density / native source frame |
|---|---|---|
| Full | 1200 /11 /TIB01f11 | 2400 /11 /GEM01f11 |
| Partial | 300 /2 /TIB01f2 | 600 /2 /GEM01f2 |
| Depleted | 0 /-1 /not drawn | 0 /-1 /not drawn |

All captured states report fallback0; sourceSpriteCount12 and hiddenQuantityLeak=false. Actual20-tick harvesting again changes400→200 credits and loads200 credits/8bales. A further5 ticks changes the stationary hover to150 credits/6bales. The final rendered frame reports114 native resource cells including31 gem cells. These counts describe the final viewport, not total map resources. The report also retains framebuffer region differences, while the actual screenshots and exact frontend/Core amount audits provide direct visual/type/depletion evidence.

## Final naval tooltip acceptance

Lead's [final normal-production naval report](../full-playthrough-20261022-172539-naval/report.json) records completed=true, passed=true, running=false, rulesRevision=command-alpha-13, finalTick7085, normalProduction=true, directEntitySpawns0, rejectedCommands0 and3 actual damage events. Its recorded hits include damage in both directions, at ticks7020,7061 and7070. Effects read those report fields and actually opened [05-sea-combat](../full-playthrough-20261022-172539-naval/05-sea-combat.png): both ships and the pointer are visible, with no empty black/yellow native tooltip frame beside the ship. This confirms the specifically guarded PopupPanel.Hide repair in a normal sea-combat scene. This production/naval check has ordinaryPracticeAi=false; it is separate from Lead's currently running final natural AI match and makes no claim about that match's eventual outcome.

Task39 is fully accepted: corrected gold ore/multicolor gems, density/depletion, native remaining quantity, actual harvest400→200→150, hiddenQuantityLeak=false and the empty native popup regression all pass their focused Native/visual checks. Source remains frozen: ArtLibrary.Scenery, Battlefield.Mining, the ground/ore-only hunk of Battlefield.OriginalMaps, the equivalent legacy DrawLandscapeGround hunk, Main.OreVisibilityChecks; plus the independent original-source producer and OriginalCell assets. The palette repair changed only the original resource producer/assets and the one tooltip hunk, not OriginalMaps, Battlefield.cs, Core/World economy or visibility. Terrain/Root retain their subsequent ground-underlay/bridge changes. Both source children and all old/new background jobs have completed and been collected. No further source edit, build or Native run is made here.
