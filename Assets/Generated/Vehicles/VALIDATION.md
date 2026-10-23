# Headless voxel bake validation

Generated from the original read-only VXL/HVA inputs under `Assets/Reference/Battlefield/Voxels` and `Assets/Reference/Formats/unittem.pal` using the independent BCL-only .NET 10 CLI in `Tools/VoxelBaker`.

Reproduce:

```zsh
zsh Tools/VoxelBaker/run.zsh --self-test
zsh Tools/VoxelBaker/bake-samples.zsh
```

Build: .NET 10.0.401, zero warnings/errors. Build output and SDK/NuGet caches live under `$XDG_CACHE_HOME/redalert-voxel-baker`; self-test temporary files use `$TMPDIR` and are removed.

Self-test: **136 checks pass**. Strict VXL/HVA bounds, truncation, span overflow and matrix validation; 32 nonempty and distinct synthetic 3D projections; PNG CRC/zlib roundtrip; PNG/JSON dimensions; index and coverage bytes; team mask consistency. A separate malformed-input CLI invocation returns exit code 1 and creates no output directory.

Original sample output:

| Asset | Parts | Cell | Atlas | Shared anchor |
| --- | --- | --- | --- | --- |
| grizzly | body, turret, barrel | 80×48 | 640×192 | (37,33) |
| rhino | body, turret, barrel | 72×56 | 576×224 | (35,34) |
| chrono-miner | body | 72×64 | 576×256 | (36,39) |
| war-miner | body, turret | 80×64 | 640×256 | (39,41) |

Every part has 32 nonempty, byte-distinct facing images. Independent Python `struct`/`zlib` decoding verified PNG CRC and decompression, metadata dimensions/rectangles/opaque counts, raw channel lengths, alpha and remap consistency. Grizzly/rhino/chrono-miner additionally passed transparent margins on every cell edge and exact index PNG/raw equality. No GUI or graphics context was used.

| Part | Opaque pixels per facing | Team pixels across atlas |
| --- | --- | --- |
| grizzly body | 635–861 | 3185 |
| grizzly turret | 195–246 | 695 |
| grizzly barrel | 47–88 | 0 |
| rhino body | 885–1048 | 4218 |
| rhino turret | 247–331 | 1731 |
| rhino barrel | 24–78 | 0 |
| chrono-miner body | 890–1667 | 3608 |
| war-miner body | 828–1630 | 1155 |
| war-miner turret | 110–161 | 3156 |

Primary interfaces are `grizzly.json`, `rhino.json`, `chrono-miner.json`, `war-miner.json`, all schema `westwood-voxel-atlas/v1`. Each asset also has a `.composite.png` preview. Part filenames are `NAME.PART.png`, `NAME.PART.indices.png/.bin`, `NAME.PART.normals.png/.bin`, `NAME.PART.team.png/.bin`, `NAME.PART.alpha.bin`.

Default camera: orthographic, elevation 30°, heading increment 11.25°, 12 pixels/world-unit. Positive headings rotate in world XY; zero projects model +X to screen right (the tank barrels extend +X). Place a facing cell at `unitPosition - atlas.anchor` for all layers. Input hashes, complete expanded palette, projection equations and the HVA/limb scale convention are recorded in each JSON.

Limits: exposed-face lighting approximates Westwood's normal-table shading; original VXL normal indices are preserved but not decoded for lighting. One HVA animation frame is selected per invocation. No original engine shadow/light tables, damage states, antialiasing or ground shadows are synthesized. The composite depth-tests all parts; independently rotated Godot layers may require attachment offsets or ordering appropriate to the game's turret behavior.
