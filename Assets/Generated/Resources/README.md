# Original resource artwork — Task39

Final resource extraction and pixel validation are complete. Only this Resources directory and Tools/ResourceArt were written. No C#, simulation, Battlefield, main application, original reference files, prior extraction state, tool build, game build or native launch was changed/performed.

## Frozen resource contract

[catalog.json](catalog.json) has schema1, complete=true and eight sprites keyed gold01..04/gem01..04. Each entry preserves its project-relative standard manifest path, original sourceImage/sourceName/sourceSha256, ore/gem kind, densityFrameCount=12, densityFrames=0..11, totalFrameCount=24, nativeCanvas, nativeAnchor, actual tail classification and per-density original nontransparent pixel count. Every sprite uses one image set and retains its full original source rectangles.

| Family | Original source canvas | Header canvas-center anchor | Density frames | Tail frames |
|---|---|---|---|---|
| GOLD01..04 | 24×24 | (12,12) | 0..11 | 12..23 all empty |
| GEM01..03 | 60×60 | (30,30) | 0..11 | 12..23 all empty |
| GEM04 | 60×60 | (30,30) | 0..11 | 12..19 empty; 20..23 each one original index1 mask pixel |

GEM04 alone uses the standard second-half shadow convention, preserving its real four tiny mask pixels as black alpha128 and retaining pairedFrame. The other seven manifests use shadowLayout=unspecified because their tails have no visible shadow. Nothing is halved without checking the actual original index data. The catalog explicitly identifies 12 density frames even where the standard manifest preserves 24 original frames.

nativeAnchor means the **header canvas center**, whose definition is explicit here. It is not a separately encoded game ground pivot. Actual GEM source content lies mostly near native y45, below the 60×60 header center. A ground-cell-center renderer may need a source ground pivot near (30,45); parent Native registration decides that relationship without changing pixels or crop offsets. GOLD content lies around (12,12). No artwork is resized, recentered on its crop, duplicated into repeated clusters or baked into a synthetic ground patch.

## Exact source and aliases

All eight original members resolve in the pinned RA2 temperate MIX data at revision acfa5b834899e7fab4c8499ccb4b49c402927b44. The existing four retained GOLD TEM files are reused read-only. Only GEM01..04 were fetched: 17,360 bytes by exact HTTP206 ranges, against a separate 1,000,000-byte cap. [Sources/sources.json](Sources/sources.json) preserves source file lengths, SHA256, absolute MIX member offsets, URLs and HTTP ranges. Original isotem.pal is used unchanged, with six-bit channels expanded by channel<<2 and index0 alpha0.

The exact original INI strings are kept separate from the selected resource artwork family. Original Rules[TIB01..20] have no Image override; Rules[TIB2_##].Image=TIB##; Art[TIB##] only has Theater=yes. They do not literally say Image=GOLD. Original [Tiberiums] and numeric resource-group Image values are also retained in catalog.sourceMetadata. Ordinary Rules.Image must not be misreported as a GOLD alias.

catalog.overlayImages covers all 18 actual resource names in the five selected maps. It preserves original rulesImage/artImage, full original sections, current decoded resourceKind/kind, instance counts and the four native sourceImages for the family. sourceImage is the default family representative or a direct literal match when available. Selecting an ore family by decoded kind is explicitly distinguished from an explicit original INI image alias; the catalog does not invent a historical TIB07→GOLD03 variation-selection algorithm. The parent owns variant selection and rendering.

The decoded maps contain 2,649 ore-kind cells under TIB*/TIB2* names and 744 gem-kind cells under GEM01. Per-map counts and exact source metadata hashes are embedded in the catalog. The pinned MIX also contains other original GEM/TIB variants, but this bounded task converts only the authorized four GOLD and four GEM images.

## Validation and actual image inspection

[validation.json](validation.json) passes:

- Eight original source sizes and SHA256 values.
- All 192 original frame index arrays, original crop positions and per-frame index/RGBA hashes.
- 96 independent original density-frame RGBA comparisons plus 84 independent empty-tail RGBA comparisons.
- Twelve independent GEM04 original shadow-coverage comparisons.
- All 192 remap masks and 46,512 original index0 pixels with alpha0.
- One image set per sprite and a maximum atlas of 38,880 pixels, below the runtime 16,777,216 limit.

[native-pixel-comparisons.json](native-pixel-comparisons.json) and [frame-audit.json](frame-audit.json) preserve frame-specific evidence. Density0 is genuinely nonempty: original GOLD01..04 have 50/36/44/38 opaque pixels, and GEM01..04 have 10/13/19/16. Density11 has 274/271/329/348 GOLD pixels and 366/389/373/359 GEM pixels. Each native sequence increases its coverage with density.

Actually opened the six full-canvas GOLD01/GEM01 previews at density0/6/11 and the remaining six complete GOLD/GEM atlases (all density frames visible), including GEM04's correct shadow tail. Twenty-four full-native-resolution preview files are retained under Previews; none were resized. Palette-index0 original RGB may be visible as a blue placeholder in some preview tooling, but every such index0 pixel has verified alpha0.

The first guarded run rejected GEM04's nonempty tail rather than silently calling it empty. A second attempt encountered the cached converter's intentional refusal to overwrite. The final producer exports any corrected interpretation into an owned ConversionStaging directory and copies its declared outputs into the owned final sprite directory, without deleting or moving sources. The final exhaustive validation completed exit0. All three jobs were collected; there is no background work left.

## Reproduction

```sh
python3 Tools/ResourceArt/prepare_resources.py --plan --fetch --prepare --validate --preview
```

This uses existing cached converter DLLs only. The parent still owns the resource ground pass, fog/depletion memory, amount-to-density scale, hover labels and native checks; this asset task does not claim to have fixed invisible harvesting in gameplay.
