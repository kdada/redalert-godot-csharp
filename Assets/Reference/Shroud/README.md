# Original Red Alert 2 shroud and fog data

The retained SHROUD.SHP and FOG.SHP slices come from original `ra2.mix/conquer.mix` asset data at the verified global offsets in [sources.json](sources.json). The archive revision, byte ranges, lengths and SHA256 are pinned in the preparation tool. No archive is downloaded in full, and no engine code is fetched.

Each source is 88,712 bytes with header `(0,60,30,96)`. Frames 0–47 each contain a stored, uncompressed format-0 60×30 raster at offset `2312 + frame*1800`. Records 48–95 have zero crop dimensions and zero data offsets; their retained per-frame index files are empty, with no invented PNG or atlas region.

[palettes.yaml](palettes.yaml) is an OpenRA **DATA** snapshot used for the dedicated `TSShroudPalette` declaration, the shared shroud/fog palette names, and the 48-element extended-neighbor frame map. [misc.yaml](misc.yaml) supplies the shroud/fog source names. Their upstream master URLs are mutable, so the tool pins snapshot lengths and SHA256 and refuses changed data. Neither snapshot defines the exact native numeric alpha palette.

Observed source evidence:

- Shroud frame 0 contains exactly 900 indices of 127 in its diamond and 900 indices of 254 outside it. Its center `(30,15)` is 127; the four canvas corners are 254.
- Every frame 0–46 retains the same 900 index-254 exterior positions.
- Shroud edge gradients run from 2 through 127; fog gradients run from 64 through 127. The increasing intensity direction and the exterior sentinel support a black-alpha interpretation.
- Stored frame 47 consists of 1,800 indices of 254. It has the same DATA mask 255 as frame 0, but differs at 900 pixels. It must remain an explicit clear variant; the default mask dictionary selects frame 0 for full mask 255.

The generated atlas uses black RGB and the disclosed **inferred** alpha transfer `index == 254 ? 0 : (index*255 + 63)//127`. It makes the full-shroud diamond opaque and keeps native intensity gradients. The source bytes establish the relative intensity direction and exterior sentinel, but they do **not** prove this exact native numeric transfer function. Raw per-frame indices, source records and hashes are preserved so the conversion can be revised without losing evidence. Neither `unittem.pal` nor `cameo.pal` is used.

Reproduce from the project root:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 Tools/classic_art/prepare_shroud.py --fetch --prepare --validate
```

`--fetch` checks existing originals and downloads missing files only, using the required HTTP proxy and exact byte ranges. `--prepare` writes only to the authorized Shroud reference/generated scopes. `--validate` checks all 192 records, all raw index payloads, all 172,800 source raster pixels, both 255 variants, the retained YAML map, generated frame PNGs, atlas regions/padding, and the preview. The tool suppresses imported Python bytecode and does not build or launch the game.
