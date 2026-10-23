#!/usr/bin/env zsh
set -eu
script_dir=${0:A:h}
: ${XDG_CACHE_HOME:=${TMPDIR:?Set XDG_CACHE_HOME or TMPDIR}}
export XDG_CACHE_HOME
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_CLI_HOME="$XDG_CACHE_HOME/redalert-voxel-baker/dotnet-home"
export DOTNET_NOLOGO=1
export NUGET_PACKAGES="$XDG_CACHE_HOME/redalert-voxel-baker/packages"
export NUGET_HTTP_CACHE_PATH="$XDG_CACHE_HOME/redalert-voxel-baker/http-cache"
dotnet build "$script_dir/VoxelBaker.csproj" --nologo -v:q >&2
exec dotnet "$XDG_CACHE_HOME/redalert-voxel-baker/bin/Debug/net10.0/VoxelBaker.dll" "$@"
