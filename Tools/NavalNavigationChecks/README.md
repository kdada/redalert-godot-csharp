# Original-map naval navigation regression

This is a short CPU map/pathfinding probe. It does not create a World, spawn entities, step a match, run Godot, or rebuild the game's simulation assembly. It compiles the actual map/pathfinding and catalog data sources into its own executable. World, economy, mobility and combat partials are excluded so those files can be edited independently.

Run from the project root:

```zsh
export DOTNET_CLI_HOME="${XDG_CACHE_HOME:?}/redalert-command/dotnet-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
dotnet run --project Tools/NavalNavigationChecks/NavalNavigationChecks.csproj -c Release -p:NuGetAudit=false -- . --expect-fixed
```

The fixture loads the original `mp01t4` JSON and rasterizes the twelve building foundations captured in `Reports/full-playthrough-20261022-151859-naval/report.json`, including shipyards `(61,76)` and `(116,137)`. It tests the stalled Destroyer route `(120,140)` → `(92,100)`.

The frozen pre-fix search is retained only as a causal diagnostic. With the old 5,000-pop cutoff, it stops after 3,322 valid expansions and 1,678 stale pops, returning no route. Independent four-neighbor connectivity reaches both endpoints in a 7,267-cell water component. Completing the same A* search takes 5,840 valid expansions / 8,446 pops and returns 222 path cells with cost 2,588. Excluding stale pops alone would still fail under a 5,000-expansion cutoff.

The fixed production `GridPathfinder` returns that exact complete-reference route. Additional checks validate every terrain edge, reverse routing, three repeated queries with identical routes, occupancy penalties, a disconnected water destination, and an unchanged land route. The unreachable query returns an empty route; the complete diagnostic exhausts only the 7,267 reachable cells. Wall-clock timings are printed for context and are not Godot performance claims.

For the historical before-fix run, omit `--expect-fixed`; that mode requires the production pathfinder to reproduce the old empty-route result. It intentionally fails once the fix is present.

`StableHash.cs` contains the exact small hash type from `Simulation/Replay.cs`, needed to compile the static map-object fingerprint declaration without pulling in Replay/World. The probe does not invoke fingerprinting. It uses no third-party engine/navigation code.
