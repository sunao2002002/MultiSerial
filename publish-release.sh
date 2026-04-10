#!/usr/bin/env bash

set -euo pipefail

configuration="Release"
runtime="win-x64"
self_contained="false"
skip_zip="false"
package_label=""
no_restore="false"

usage() {
  cat <<'EOF'
Usage: ./publish-release.sh [options]

Options:
  -c, --configuration <value>  Build configuration. Default: Release
  -r, --runtime <value>        Runtime identifier. Default: win-x64
      --self-contained         Publish as self-contained.
      --skip-zip               Skip zip package creation.
      --package-label <value>  Package label suffix. Default: current timestamp.
      --no-restore             Skip dotnet restore.
  -h, --help                   Show this help.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -c|--configuration)
      configuration="$2"
      shift 2
      ;;
    -r|--runtime)
      runtime="$2"
      shift 2
      ;;
    --self-contained)
      self_contained="true"
      shift
      ;;
    --skip-zip)
      skip_zip="true"
      shift
      ;;
    --package-label)
      package_label="$2"
      shift 2
      ;;
    --no-restore)
      no_restore="true"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_path="$script_dir/SerialApp.Desktop.csproj"
artifacts_root="$script_dir/artifacts"
publish_root="$artifacts_root/publish"
package_root="$artifacts_root/packages"
publish_dir="$publish_root/${configuration}-${runtime}"

if [[ ! -f "$project_path" ]]; then
  echo "Project file not found: $project_path" >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet command was not found. Install .NET SDK 10 first." >&2
  exit 1
fi

mkdir -p "$publish_root" "$package_root"
rm -rf "$publish_dir"

echo "Project: $project_path"
echo "Configuration: $configuration"
echo "Runtime: $runtime"
echo "SelfContained: $self_contained"
echo "PublishDir: $publish_dir"

if [[ "$no_restore" != "true" ]]; then
  echo "Restoring dependencies..."
  dotnet restore "$project_path"
fi

echo "Publishing release output..."
dotnet publish "$project_path" \
  -c "$configuration" \
  -r "$runtime" \
  --self-contained "$self_contained" \
  -o "$publish_dir"

zip_path=""

if [[ "$skip_zip" != "true" ]]; then
  if [[ -n "$package_label" ]]; then
    label="$package_label"
  else
    label="$(date +%Y%m%d-%H%M%S)"
  fi

  zip_name="SerialApp.Desktop-${configuration}-${runtime}-${label}.zip"
  zip_path="$package_root/$zip_name"
  rm -f "$zip_path"

  echo "Creating zip package..."

  if command -v zip >/dev/null 2>&1; then
    (
      cd "$publish_dir"
      zip -qr "$zip_path" .
    )
  elif command -v powershell.exe >/dev/null 2>&1; then
    publish_dir_win="$(cygpath -w "$publish_dir")"
    zip_path_win="$(cygpath -w "$zip_path")"
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \
      "Compress-Archive -Path '$publish_dir_win\\*' -DestinationPath '$zip_path_win' -Force"
  else
    echo "Neither zip nor powershell.exe is available for packaging." >&2
    exit 1
  fi
fi

echo "Publish completed."
echo "OutputDir: $publish_dir"

if [[ -n "$zip_path" ]]; then
  echo "ZipPath: $zip_path"
fi