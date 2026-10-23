# Combat presentation effects

Implemented in the new [Battlefield.Effects.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.Effects.cs) and [ArtLibrary.Effects.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/ArtLibrary.Effects.cs). This task writes only those files, the new [prepare_effects.py](/storage/Users/currentUser/dev/redalert-godot-csharp/Tools/prepare_effects.py), files under [Generated/Effects](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Effects), and this report. Existing rendering, Main, simulation and tests remain owned by the lead/other agents.

## Exact hook contract

The lead integrates the private methods supplied by the Battlefield partial:

```csharp
// Existing Battlefield.CaptureEvents wrapper, called after each simulation step:
public void CaptureEvents() => CaptureWeaponEffects();

// Existing Battlefield._Draw, at the old effects block before DrawShroudEdges:
DrawWeaponEffects();
```

Remove the old `effects` list and its capture/draw loop from [Battlefield.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.cs). No additional process, ready, disposal, signal or simulation hook is needed. Capture deduplicates a world tick; drawing uses Godot's monotonic wall clock. Existing Main frame redraws animate the effects.

Required Main presentation API:

```csharp
public bool IsSpectating { get; }
public bool ViewVisible(int x, int y);
```

The partial also uses existing `Battle`, `VisualDetail`, `LocalPlayer`, `Graphics`, `Project`, `Size` and `Zoom` members. `ViewExplored` is not used: remembered terrain alone never permits a combat effect.

Required optional CombatEvent metadata, appended to the existing positional record:

```csharp
int SourceX = 0, int SourceY = 0,
UnitType? SourceUnit = null, BuildingType? SourceBuilding = null,
bool TargetInfantry = false, int SourceOwner = -1, bool Secondary = false
```

Coordinates use `Rules.Scale`, as do existing target X/Y. Populate primary metadata when constructing the event before removing damaged entities. `Explosion=true` means target destruction. `Secondary=true` suppresses all source effects; this supports splash without a second shot. `Source=0, Explosion=true, Secondary=true` produces a local destruction explosion without a muzzle, projectile or electricity. Ordinary metadata-free legacy events can resolve a source at capture, but a removed legacy source cannot be reconstructed. Complete metadata avoids the lookup entirely.

Public diagnostic properties are `Battlefield.ActiveWeaponEffects` and `Battlefield.WeaponEffectDrawCommands`. The latter counts a conservative bound for effect commands only; dog pose/shadow reserves two commands.

## Visible weapon differences

| Source | Presentation |
| --- | --- |
| Grizzly / Rhino | Short muzzle flash, a small moving cannon shell, spaced smoke puffs and a local blast/impact |
| Grand Cannon | Larger muzzle, elevated ballistic arc, shell ground shadow on high detail, and heavier impact |
| Apocalypse | Two laterally separated barrels; second flash/projectile starts 105 ms later. Both are cosmetic representations of one event |
| GI / Conscript / War Miner / Pillbox | Brief muzzle flash, short moving tracer segment, sparks/dust at the target |
| Sniper | Smaller white flash and thin short tracer, then a compact impact; no persistent source-to-target line |
| Tesla Tank / Tesla Coil | Animated jagged blue-white bolt, changing at 42 ms slices; high detail adds a halo and one short branch |
| Dog | Original DOG/ADOG attack pose on high detail plus local bite/claw dust; no ranged tracer |
| IFV | Moving small missile with an exhaust tail and smoke puffs |
| Flak Track | Short traveling round with dust and spark burst |
| Prism Tank | Moving cyan diamond pulse with an electric-looking local spark; no connecting beam |
| Vehicle/building destruction | Pixel blast followed by rising smoke and a bounded set of ballistic debris fragments |
| Infantry destruction | Local dust and sparks/bite marks, without a vehicle fireball |

Only Tesla types connect the source and target with a sustained beam. Cosmetic projectile flight ranges from 90–760 ms by source and distance. Damage still occurs on the authoritative simulation tick; there is no delayed damage, collision, reload or target tracking in this partial. Every source and target position is copied into the effect, so later movement/removal cannot change its endpoints.

## Fog, culling and budgets

- Both endpoint observation flags are captured before recording a shot. A muzzle needs the source to have been visible then and to remain visible now; an impact has the equivalent target requirement.
- Connecting bolts, tracers and traveling projectiles require both observed endpoints to remain visible. A hidden source may produce only a local visible target impact. Hidden targets have no impact, trajectory or bolt. Moving projectiles, smoke puffs and debris additionally check their own cell visibility.
- Tesla paths sample visibility at half-cell spacing, capped at 32 intervals; a hidden intermediate cell suppresses the connecting bolt. Short rifle/sniper tracer segments are also visibility sampled.
- Spectator mode explicitly permits observation across the map. World changes, tick rollback, viewer changes, spectator changes and detail changes clear effect history, preventing old spectator data from entering a player view.
- Projected endpoint bounds, expanded for sprites and the maximum arc, cull offscreen effects before issuing drawing commands.
- Detail **0** clears effects. Detail **1** retains at most **128**, one trail/smoke puff, two destruction fragments and a 7-segment Tesla bolt. Detail **2** retains at most **384**, up to three trail/smoke puffs, five fragments, original dog poses and a 12-segment Tesla bolt with halo.
- Per-frame command budgets are **260** at medium and **900** at high detail, prioritizing the newest events. A fixed ring replaces the oldest record at capacity; capture inspects at most twice the configured capacity from a single event batch. Effects expire within **2.21 seconds** including the longest ballistic travel; Apocalypse's delayed second record expires sooner. There are no unbounded particle nodes or event dictionaries.
- One independent atlas texture is cached through ArtLibrary's existing `sprites` cache and existing disposal path. No per-frame image creation, atlas decoding, texture uploads, JSON parsing, particle allocation or LINQ work occurs. Original dog textures may join the existing owner-remapped cache once per used owner.

## Art sources and limitations

The retained source manifests and generated catalogs were inspected. Original standalone explosion, muzzle, piff and smoke SHPs were not present. The original [art.ini](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Details/art.ini) contains animation names, but those INI names do not supply their pixels. `ArtLibrary.DrawScenery` loads scenery from the interface cache and applies bottom/scenery anchors and hardcoded second-half shadows. That path is inappropriate for transparent transient weapon animation.

Actual original sprites are used for dog attacks. Original [DOGSequence](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Details/art.ini#L10071) specifies `FireUp=116,6,6`; both DOG and ADOG select this sequence. All 48 attack frames, 116–163, have retained pixel data and paired shadow frames in the existing generated atlases. Original source provenance is preserved in [manifest-extra.json](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/manifest-extra.json#L155):

| Sprite | Original SHA256 | Source range in fixed ra2.mix.part00 |
| --- | --- | --- |
| ADOG | `020c4ee87b55409f72bf1d15482f6ae7c7dc794bc8ecf172e2e0f80d133258b7` | `bytes=9382592-9459175` |
| DOG | `7fd747a5d874c8a3613fae87eb8ef438e9b31778c32150830afcd7a992383ff7` | `bytes=9305696-9382591` |

Source URL: [fixed source revision acfa5b8, ra2.mix.part00](https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/acfa5b834899e7fab4c8499ccb4b49c402927b44/game-res/ra2.mix.part00). These existing game pixels retain their original EA/Westwood ownership. No download was needed for this task.

All other effect sprites are independent procedural pixel artwork created offline by [prepare_effects.py](/storage/Users/currentUser/dev/redalert-godot-csharp/Tools/prepare_effects.py), using only Python's standard library. They are an approximation of period pixel effects, not recovered retail weapon animations. The [atlas](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Effects/atlas.png) contains **66 frames** in eight rows, **768×512 RGBA**, **48,089 bytes**. Its [manifest](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Effects/manifest.json) records generator/source status, row layout, generator fingerprint and atlas SHA256 `77ae74cbc2013033ef1509183711fc3043316d041331fc25990047f79e80ecff`.

The original dog attack is an overlay on the existing unit draw, with a small cosmetic lunge; the existing standing/moving pose is not replaced by this isolated partial. Muzzle anchors are approximate 2D offsets rather than recovered per-voxel barrel coordinates. Prism uses a compact moving pulse to meet the beam restriction, rather than a retail prism ray. Debris and smoke are brief effects, not persistent wreckage or gameplay cover. Destruction appears at the cosmetic impact time even though authoritative damage and removal have already happened. These are explicit visual limitations, with no changes to damage timing, simulation state, network protocol or state hashes.

## Scoped verification

Ran the generator successfully and checked only its output plus the retained dog metadata:

- PNG signature, all three chunk CRCs, 768×512 RGBA header, zlib stream length and scanline filters passed.
- Manifest atlas SHA256 and generator SHA256 matched the files.
- All eight sequence rows and 66 frame bounds fit the atlas; all nonterminal fade frames contain visible pixels.
- DOG and ADOG each have all 48 attack frames with data and valid paired shadow indices.
- The atlas was opened and visually inspected as a sprite sheet. This is not an in-game animation or timing review.

No build, Godot launch, broad test suite, replay/hash run or live in-game verification was performed, following the user's speed instruction. Lead integration must provide the two hooks and visibility API above. Regenerate artwork when needed with:

```sh
python3 Tools/prepare_effects.py
```
