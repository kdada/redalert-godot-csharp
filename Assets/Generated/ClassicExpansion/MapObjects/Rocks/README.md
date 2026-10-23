# Original overlay rocks

Input is the ground cell center. Preserve native canvas center and original SourceRect offset; no crop-center adjustment. RUBBLE_OVERLAY uses the exact SROCK01 image with drawFlat=true.

All source images use isotem.pal. Native source crops and both color/shadow frames are preserved. Ordinary rocks use native canvas center; these authored canvases place their base near that origin. Final native registration is validated by the caller with its projected CELL CENTER. No raster bounds recenter, synthesis or recoloring.

| Name | Canvas | Color crop x,y,w,h | Ground anchor |
|---|---|---|---|
| TROCK01 | 96×96 | 33,40,28,15 | 48.0,48.0 |
| TROCK02 | 96×96 | 34,33,27,25 | 48.0,48.0 |
| TROCK03 | 96×96 | 37,34,24,24 | 48.0,48.0 |
| TROCK04 | 96×96 | 34,30,28,26 | 48.0,48.0 |
| TROCK05 | 96×96 | 34,34,27,22 | 48.0,48.0 |
| SROCK01 | 120×120 | 48,41,28,27 | 60.0,60.0 |
| SROCK02 | 120×120 | 45,39,30,26 | 60.0,60.0 |
| SROCK03 | 120×120 | 51,48,25,20 | 60.0,60.0 |
| SROCK04 | 120×120 | 48,44,26,22 | 60.0,60.0 |
| SROCK05 | 120×120 | 46,49,32,19 | 60.0,60.0 |

RUBBLE_OVERLAY shares SROCK01 without a duplicate sprite conversion and retains its original DrawFlat flag.

Only this Rocks directory and its independent raw-source Rocks directory are written. Existing cached conversion DLLs are required; no build or native game launch occurs.
