# Authoritative original cell resources

Only the actual five-map resource images are included. Resolve original Rules.Image or overlay name, then original Art.Image or that image. TIB2_## aliases TIB##; GEM01 stays GEM01. No GOLD family fallback or substitution is present.

Twelve original 60x60 SHPs retain all 24 original frames. Frames0..11 are original density states; every frame12..23 was independently decoded and is EMPTY, not a real shadow. Manifests use shadowLayout=unspecified; renderer must use the explicit densityFrames list rather than assume all24 are density colors.

## Ground registration

Native canvas center is (30,30). The authored isometric ground diamond is in the lower half, nominal x0..60/y30..60. For a supplied projected CELL CENTER the source anchor is (30,45) = canvas center+(0,15). TIB01 full density crop(4,31,52,25) and GEM01(7,31,42,26) lie around this plane. Using anchor(30,30) shifts these ground piles15px forward. Preserve original sourceRect offsets; do not recenter their cropped bounding boxes. Every raw crop and its bounds relative to BOTH pivots are retained in source-frame-audit.json. Actual final scene registration is the parent renderer/native check.

## Density audit

All original indices and order are preserved. Eleven of twelve images monotonically gain opaque pixels through frames0..11. TIB02 is an original exception: frame0 has414 opaque pixels, frames1..11 have7..341. This is actual retained original data, not corrected/reordered in the image. densityFrames remains0..11. densityOrderByOpaquePixels records the measured order separately if the renderer needs it.

| Image | Source crop of frame11 | Opaque pixels by original frames0..11 | Tail |
|---|---|---|---|
| gem01 | 7,31,42,26 | 10,14,34,48,66,93,119,160,212,260,321,366 | empty |
| tib01 | 4,31,52,25 | 5,15,25,45,66,98,137,182,252,328,409,512 | empty |
| tib02 | 6,34,48,23 | 414,7,17,25,44,60,83,113,158,218,284,341 | empty |
| tib03 | 5,32,48,26 | 11,14,25,40,52,73,111,154,203,267,348,414 | empty |
| tib04 | 5,33,49,25 | 11,16,31,47,68,99,123,168,208,255,327,418 | empty |
| tib05 | 5,31,49,25 | 13,19,33,45,67,87,112,143,194,263,335,410 | empty |
| tib06 | 7,32,47,24 | 13,20,31,48,72,94,119,147,200,257,316,387 | empty |
| tib07 | 5,32,49,26 | 16,24,41,57,75,99,131,169,207,271,352,410 | empty |
| tib08 | 7,33,49,23 | 10,13,25,39,56,85,118,168,231,291,350,408 | empty |
| tib10 | 8,29,44,26 | 12,24,49,66,86,140,155,192,237,283,346,413 | empty |
| tib11 | 4,33,48,24 | 8,16,32,47,61,96,121,167,244,304,357,392 | empty |
| tib12 | 4,30,45,26 | 12,24,38,54,77,103,144,181,223,284,338,378 | empty |

## Reproduction

python3 Tools/ResourceArt/prepare_original_cell_resources.py --plan --fetch --prepare --validate --preview

Source SHA256/size, pinned MIX offsets and exact HTTP206 ranges are in Sources/sources.json. Original TEMPERAT.PAL (SHA256 5903b69868b84f494cfbb4e7100398015ef9775b37726019a0d7b5fb6cb33b55) is separately retained in the same exact-range source ledger and bound by every standard manifest. The earlier ISOTEM choice produced rainbow ore/gray gems despite preserving all original indices; UNITTEM also produces invalid magenta pixels. TEMPERAT correctly gives gold TIB and multicolor GEM, with all raw SHPs, frame order, crop offsets, anchors and density metadata preserved. validation.json independently compares all288 native frame-index arrays, all288 native RGBA frames, all288 remap masks, zero-index alpha and PNG CRC/dimensions. The helper uses only existing cached conversion DLLs. No tools/game build, Native launch or writes to any other Resources directory occur.
