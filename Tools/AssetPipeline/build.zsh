#!/usr/bin/zsh -f
set -eu
: "${XDG_CACHE_HOME:?Set XDG_CACHE_HOME to a writable cache directory}"
: "${TMPDIR:?Set TMPDIR to a writable temporary directory}"
project_dir="${0:A:h}"
artifact_dir="${XDG_CACHE_HOME%/}/redalert-godot-csharp/asset-pipeline"
export DOTNET_CLI_HOME="${XDG_CACHE_HOME%/}/redalert-godot-csharp/dotnet-home"
export NUGET_PACKAGES="${XDG_CACHE_HOME%/}/redalert-godot-csharp/nuget"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false

dotnet build "${project_dir}/AssetPipeline.csproj" \
  --artifacts-path "${artifact_dir}" --disable-build-servers \
  "-p:RestoreConfigFile=${project_dir}/NuGet.Config" -p:NuGetAudit=false -v:minimal
if (( $# > 0 )); then
  dotnet "${artifact_dir}/bin/AssetPipeline/debug/Ra2AssetPipeline.dll" "$@"
fi
