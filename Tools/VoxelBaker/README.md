# VoxelBaker

Independent headless C#/.NET 10 CLI. No Godot, graphics context, native image library, or NuGet package is required. The implementation reads Westwood VXL + HVA + PAL and rasterizes exposed voxel faces with an orthographic camera and a per-pixel depth buffer. Each facing is rendered from 3D geometry; the tool never rotates an already rendered image.

Run from the repository root using zsh (the script needs no executable permission):

```zsh
zsh Tools/VoxelBaker/run.zsh --self-test
zsh Tools/VoxelBaker/run.zsh bake \
  --palette Assets/Reference/Formats/unittem.pal \
  --output Assets/Generated/Vehicles \
  --name grizzly \
  --part body=Assets/Reference/Battlefield/Voxels/gtnk.vxl \
  --part turret=Assets/Reference/Battlefield/Voxels/gtnktur.vxl \
  --part barrel=Assets/Reference/Battlefield/Voxels/gtnkbarl.vxl
```

To regenerate all checked-in sample atlases, run `zsh Tools/VoxelBaker/bake-samples.zsh`; this also includes the Soviet war miner when its source files exist.

The sibling HVA file is loaded automatically. Override it with `--hva body=PATH`, or explicitly use the identity pose with `--hva body=-`. A missing automatic sibling also uses identity, recorded in metadata. A supplied invalid/missing HVA fails. HVA sections bind to VXL limbs by index, with equal section counts; retail labels may differ (for example the flak track). Names are preserved as metadata rather than used to reject valid pairs. `--frame N` chooses one HVA animation frame for every part; animation sequences require separate invocations/names.

Defaults: 32 facings, eight atlas columns, elevation 30 degrees, azimuth zero, 12 pixels/world-unit, four pixels padding, six-bit PAL channels, remap indices 16..31. `--directions`, `--columns`, `--elevation`, `--azimuth`, `--scale`, `--padding`, `--cell`, `--palette-bits`, `--team-start`, `--team-count`, `--frame` override these values. `--cell N` requests a fixed square cell; fitting fails if it would crop geometry. Without `--cell`, the tool fits one common canvas over all parts and all facings, rounded up to eight pixels. Part and asset names accept ASCII letters/digits/underscores/hyphens.

All generated parts in one invocation share the same pixel dimensions and anchor. The anchor is the projected original model origin, including HVA placement; the tool never recenters individual limbs or parts. In Godot, select the facing rectangle and place each layer at `position - anchor`. Use nearest filtering and disable mipmaps for all channels. Separate turret/barrel assets permit independent facing selection; the composite atlas provides a preview with all parts at the same heading and a shared depth buffer. Arbitrary independent headings may need a project-specific turret attachment offset or draw ordering.

For each `NAME.PART`:

| Output | Meaning |
| --- | --- |
| `.png` | Lit RGBA atlas, palette index zero transparent |
| `.indices.png` | RGB all equal to original palette index; alpha is voxel coverage |
| `.indices.bin` | Original indices, one byte/pixel |
| `.normals.png`, `.normals.bin` | Original VXL normal indices, preserved without decoding the normal lookup table |
| `.team.png` | Team ramp offsets normalized to RGB 1..255, alpha zero outside team pixels |
| `.team.bin` | Zero outside remap, otherwise source index minus team start plus one |
| `.alpha.bin` | Coverage: zero or 255, one byte/pixel |

Raw files have no header and cover the full atlas in row-major order, including transparent pixels and unused tiles. PNG channels carry exact integer values, so import them as data textures without color conversion when sampling indices. Team ramp order is retained; the palette determines which end of the ramp is light. Team masks contain remap shade identity but do not include the directional lighting factor.

`NAME.json` uses schema `westwood-voxel-atlas/v1`, with atlas dimensions, common anchor, every facing rectangle/angle/opaque count, projection equations, complete expanded RGB palette, lighting, input SHA256 hashes, limb sizes/bounds/scales and all output filenames. `NAME.composite.png` contains all supplied parts with shared depth testing.

Coordinates: VXL grid corners map into the footer bounds, HVA row-major 3x4 affine transforms apply the pose, the footer scale converts to world units and Y is reflected. HVA translation is adjusted by footer scale and bounds/grid ratio. Positive heading rotates counterclockwise in world XY. Direction zero places world +X toward screen right. Screen X is `rotatedWorldX * pixelsPerWorldUnit`; screen Y is `(-sin(elevation)*rotatedWorldY - cos(elevation)*worldZ) * pixelsPerWorldUnit`, plus the common anchor. The JSON records the exact transform formula.

Lighting uses exposed face normals determined from neighbor occupancy, a fixed world-space light, ambient 0.38 and diffuse 0.62. This deliberately approximates the original normal-table lighting; VXL normal indices remain available for a future exact implementation. No shadows, ground contact shadow, palette animation, anti-aliasing, original engine light tables, or voxel damage state are synthesized. Transparent index zero is omitted from rendering. The source assets themselves are read-only and are not redistributed by the tool.

Build products are isolated by the local `Directory.Build.props` into `$XDG_CACHE_HOME/redalert-voxel-baker` (fallback `$TMPDIR`). Self-test fixtures and output are created under `$TMPDIR` (fallback cache) and removed after the test. The self-test builds an asymmetric synthetic VXL/HVA, rejects malformed/truncated/overflowing spans and matrices, renders 32 nonempty distinct projections, checks PNG CRC/decompression and verifies dimensions, raw index bytes, coverage, remap masks and JSON facing metadata. All input pointers and allocation budgets are bounded.

Format references consulted, without copying implementation code:

- [OpenRA VxlReader.cs](https://raw.githubusercontent.com/OpenRA/OpenRA/bleed/OpenRA.Mods.Cnc/FileFormats/VxlReader.cs)
- [OpenRA HvaReader.cs](https://raw.githubusercontent.com/OpenRA/OpenRA/bleed/OpenRA.Mods.Cnc/FileFormats/HvaReader.cs)
- [OpenRA Voxel.cs coordinate conventions](https://raw.githubusercontent.com/OpenRA/OpenRA/bleed/OpenRA.Mods.Cnc/Graphics/Voxel.cs)
