#!/bin/bash
export DOTNET_ROOT=/home/kaue/tests/gsa/work/toolchain/dotnet
export NUGET_PACKAGES=/home/kaue/tests/gsa/work/nuget/packages
export XDG_DATA_HOME=/home/kaue/tests/gsa/work/xdg-data
export XDG_CONFIG_HOME=/home/kaue/tests/gsa/work/xdg-config
GODOT=$(find /home/kaue/tests/gsa/work/toolchain/godot -name 'Godot_v*-stable_mono_linux.x86_64' | head -n 1)
export PATH="$DOTNET_ROOT:$(dirname "$GODOT"):$PATH"
export XDG_RUNTIME_DIR=/run/user/1000
export WAYLAND_DISPLAY=wayland-0
export DISPLAY=:1
export XAUTHORITY=$(ls -t /run/user/1000/.mutter-Xwaylandauth.* 2>/dev/null | head -n 1)

cd /home/kaue/tests/gsa
exec "$GODOT" --path game res://Bootstrap.tscn
