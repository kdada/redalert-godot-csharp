# Retained vehicle pose models

Run from the repository root with Python 3.11 or newer (standard library only):

```sh
python3 -B Tools/VehiclePose/prepare_models.py --self-test
python3 -B Tools/VehiclePose/prepare_models.py
```

The generator writes only `Assets/Generated/VehiclePoses/`. It does not invoke
C#, dotnet, Godot, a native baker, a build, or any network command. Its source
and in-memory checks live in `Tools/VehiclePose/`.

## Inputs and provenance

The Python decoder and face iterator port this repository's
`Tools/VoxelBaker/Formats.cs` and `Renderer.BuildFaces` in
`Tools/VoxelBaker/Renderer.cs`. Each run reads and hashes both original sources
and stops if their reviewed versions have changed. This makes drift explicit;
review the port before updating the pinned source hashes.

Discover voxel manifests with schema `westwood-voxel-atlas/v1` in
`Assets/Generated/Vehicles/*.json` and recursively under
`Assets/Generated/ClassicExpansion/`. Model keys are the exact manifest names.
Include every manifest with a body part, including IFV turret variants
`fv_turret1` through `fv_turret3`, prism charge variants `sref_turret1` through
`sref_turret3`, both MCVs (`mcv`, `smcv`), no-spawn variants, civilian vehicles,
and the retained water/air vehicles. Skip turret-only building manifests,
including `grand-cannon`; their source hashes are still verified.

Index every retained `.vxl`, `.hva`, and `.pal` below `Assets/Reference` by
`(case-folded basename, SHA256)`. Keep all matching paths: a duplicate basename
never replaces a different hash. Every discovered voxel manifest's VXL/HVA
and palette hashes must resolve before generation starts. The palette source
must expand to the exact manifest `expandedRgb` bytes. No original asset is
changed or discarded. The catalog records all indexed paths and hashes,
manifest hashes, port source hashes, and per-model source hashes.

## Geometry

Decode checked original VXL skip/run columns and duplicate run counts. Preserve
separate occupancy and color arrays, so an occupied palette-zero voxel still
occludes its neighbor. Validate bounds, finite floats, file/body ranges, limb
counts, a 128 MiB input limit, a 32-million-cell per-input limit, and a
three-million-face per-part limit. Normal index bytes are consumed but do not
control face lighting, matching the original renderer.

HVA sections bind by position, even if their labels differ. Validate all HVA
frames, retain frame zero, and require matching section counts. An absent HVA
or explicit `identityPose` uses the identity matrix. Preserve original model
coordinates, all six face directions, corner order, and inverse-transpose
normals. Do not project, rotate to a facing, recenter, flatten, or fit the model.
With `step = (max - min) / gridSize` and `p = min + grid * step`:

```text
translation = HVA.translation * step * footerScale
world       = footerScale * reflectY(HVA.linear * p + translation)
normal      = normalize(inverseTranspose(reflectY * HVA.linear) * sideNormal)
```

The two footer scale factors in the translation expression are intentional and
match `Renderer.BuildFaces`. Coordinate arithmetic rounds to Float32 at the
same scalar operation boundaries. Stream faces directly into gzip in buffers
of approximately 64 KiB. Keep one decoded part and at most 8192 cached corner
transforms for its current limb; do not retain face lists or raw model blobs.

## Binary format

A model is named `<manifest.name>.vpm.gz`. Gzip has an empty stored filename,
zero modification time and compression level 9 for repeatable model bytes.
Its decompressed payload is little-endian, tightly packed, with no alignment
padding:

| Field | Encoding | Bytes |
| --- | --- | ---: |
| Magic | ASCII `RAVP1`, followed by three NULs | 8 |
| Part count | Int32 | 4 |
| Projection scale | Float32, manifest `pixelsPerWorldUnit` | 4 |
| Elevation | Float32, manifest `elevationDegrees` | 4 |
| Azimuth | Float32, manifest `azimuthDegrees` | 4 |
| Palette | decoded manifest `expandedRgb`, RGB triples | 768 |
| Each part: role | byte: body=0, turret=1, barrel=2 | 1 |
| Each part: face count | Int32 | 4 |
| Each face: corners A/B/C/D | 12 Float32 values, xyz per corner | 48 |
| Each face: normal | 3 Float32 values, xyz | 12 |
| Each face: palette index | byte | 1 |

Each face is exactly 61 bytes. The total raw length is
`792 + 5 * partCount + 61 * totalFaceCount`. Part order follows the manifest;
limb, voxel and side orders follow the original face builder. Projection
parameters describe the existing atlas camera and are not baked into geometry.

## Catalog and validation

`Assets/Generated/VehiclePoses/catalog.json` has integer `schema: 1` and a
`models` dictionary keyed by exact manifest name. Each entry provides `file`,
`faces`, integer `parts`, `compressedBytes`, `rawBytes`, `sourceHashes`, raw and
compressed SHA256, projection values, team-color range, source manifest, and
`partDetails` with roles, face counts, retained source paths and limb metadata.
`file` is relative to the catalog's directory. `totals` sums unique model files;
there are no extra synthetic alias files. Expansion manifest names already
identify the complete variants expected by the artwork consumer.

Each generated file is immediately read back through gzip to its EOF (including
CRC validation). Check every header, palette byte, role, face count, finite
coordinate, nonzero palette index, unit normal, exact payload length, raw SHA256
and compressed SHA256. The catalog includes aggregate sizes, generation time,
source-reference verification counts, and every retained source file hash.
The self-test runs entirely in memory and covers malformed/truncated VXL/HVA,
multiple spans, empty columns, transparent occupancy, frame-zero selection,
label mismatch, identity poses, translation/rotation/scale/reflection, normal
inverse transpose, original model origin, side winding, and binary layout.
