#!/usr/bin/env zsh
set -eu
script_dir=${0:A:h}
repo_dir=${script_dir:h:h}
: ${XDG_CACHE_HOME:=${TMPDIR:?Set XDG_CACHE_HOME or TMPDIR}}
export XDG_CACHE_HOME
zsh "$script_dir/run.zsh" --help >/dev/null
baker="$XDG_CACHE_HOME/redalert-voxel-baker/bin/Debug/net10.0/VoxelBaker.dll"
voxels="$repo_dir/Assets/Reference/Battlefield/Voxels"
palette="$repo_dir/Assets/Reference/Formats/unittem.pal"
output="$repo_dir/Assets/Generated/Vehicles"
dotnet "$baker" bake --palette "$palette" --output "$output" --name grizzly \
  --part "body=$voxels/gtnk.vxl" --part "turret=$voxels/gtnktur.vxl" --part "barrel=$voxels/gtnkbarl.vxl"
dotnet "$baker" bake --palette "$palette" --output "$output" --name rhino \
  --part "body=$voxels/htnk.vxl" --part "turret=$voxels/htnktur.vxl" --part "barrel=$voxels/htnkbarl.vxl"
dotnet "$baker" bake --palette "$palette" --output "$output" --name chrono-miner \
  --part "body=$voxels/cmin.vxl"
if [[ -f "$voxels/harv.vxl" && -f "$voxels/harvtur.vxl" ]]; then
  dotnet "$baker" bake --palette "$palette" --output "$output" --name war-miner \
    --part "body=$voxels/harv.vxl" --part "turret=$voxels/harvtur.vxl"
fi
