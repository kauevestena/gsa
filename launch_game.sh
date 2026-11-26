#!/usr/bin/env bash
set -eu
repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd "$repo_dir"
export DOTNET_ROOT="${DOTNET_ROOT:-$repo_dir/work/toolchain/dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$repo_dir/work/nuget/packages}"
export XDG_DATA_HOME="${XDG_DATA_HOME:-$repo_dir/work/xdg-data}"
export XDG_CONFIG_HOME="${XDG_CONFIG_HOME:-$repo_dir/work/xdg-config}"
editors=("$repo_dir"/work/toolchain/godot/*/Godot_v*-stable_mono_linux.x86_64)
editor=${GODOT:-${editors[0]}}
if [[ ! -x "$editor" ]]; then
    echo "Install the pinned editor with python3 scripts/install_toolchain.py or set GODOT to its executable." >&2
    exit 1
fi
extra_args=()
if [[ " $* " != *" --headless "* ]]; then
    unset LIBGL_ALWAYS_SOFTWARE GALLIUM_DRIVER MESA_LOADER_DRIVER_OVERRIDE __GLX_VENDOR_LIBRARY_NAME
    if [[ -n "${WAYLAND_DISPLAY:-}" ]]; then
        if [[ -z "${DRI_PRIME:-}" && -e /dev/dri/by-path/pci-0000:00:02.0-render ]]; then
            export DRI_PRIME=pci-0000_00_02_0
        fi
        if [[ " $* " != *" --display-driver "* ]]; then
            extra_args+=(--display-driver wayland)
        fi
    fi
fi
exec "$editor" ${extra_args[@]+"${extra_args[@]}"} --path "$repo_dir/game" "$@"
