#!/usr/bin/env zsh
set -eu
repo_dir=${0:A:h:h}
: ${XDG_CACHE_HOME:?Set XDG_CACHE_HOME}
baker="$XDG_CACHE_HOME/redalert-voxel-baker/bin/Debug/net10.0/VoxelBaker.dll"
if [[ ! -f "$baker" ]]; then
  zsh "$repo_dir/Tools/VoxelBaker/run.zsh" --help >/dev/null
fi
voxels="$repo_dir/Assets/Reference/Battlefield/Voxels"
palette="$repo_dir/Assets/Reference/Formats/unittem.pal"
output="$repo_dir/Assets/Generated/Vehicles"
dotnet "$baker" bake --palette "$palette" --output "$output" --name tesla-tank --part "body=$voxels/ttnk.vxl" --part "turret=$voxels/ttnktur.vxl"
dotnet "$baker" bake --palette "$palette" --output "$output" --name ifv --part "body=$voxels/fv.vxl" --part "turret=$voxels/fvtur.vxl"
dotnet "$baker" bake --palette "$palette" --output "$output" --name flak-track --part "body=$voxels/htk.vxl" --part "turret=$voxels/htktur.vxl" --part "barrel=$voxels/htkbarl.vxl"
dotnet "$baker" bake --palette "$palette" --output "$output" --name prism-tank --part "body=$voxels/sref.vxl" --part "turret=$voxels/sreftur.vxl"
dotnet "$baker" bake --palette "$palette" --output "$output" --name apocalypse --part "body=$voxels/mtnk.vxl" --part "turret=$voxels/mtnktur.vxl" --part "barrel=$voxels/mtnkbarl.vxl"
dotnet "$baker" bake --palette "$palette" --output "$output" --name grand-cannon --part "turret=$voxels/gtgcantur.vxl" --part "barrel=$voxels/gtgcanbarl.vxl"
