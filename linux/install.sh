#!/usr/bin/env bash
# Installs ClaudeUsage for the current user (no root needed). Run it from a clone of the repository, which builds
# the app (needs the .NET 10 SDK), or from an unpacked release archive, which carries the built app (needs the
# .NET 10 runtime). Running it again updates the installation.
#
#   linux/install.sh              install or update: app, menu entry, icon
#   linux/install.sh --autostart  the same, and start the app at login
#   linux/install.sh --uninstall  remove all of it (settings in ~/.config/ClaudeUsage stay)
#
# What goes where:
#   ~/.local/share/claudeusage/                          the app
#   ~/.local/share/applications/claudeusage.desktop      the menu entry
#   ~/.local/share/icons/hicolor/256x256/apps/claudeusage.png
#   ~/.config/autostart/claudeusage.desktop              only with --autostart
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
data=${XDG_DATA_HOME:-$HOME/.local/share}
config=${XDG_CONFIG_HOME:-$HOME/.config}
app_dir=$data/claudeusage
menu_entry=$data/applications/claudeusage.desktop
icon=$data/icons/hicolor/256x256/apps/claudeusage.png
autostart=$config/autostart/claudeusage.desktop

if [[ ${1:-} == --uninstall ]]; then
    rm -rf "$app_dir"
    rm -f "$menu_entry" "$icon" "$autostart"
    echo "Removed. Settings in $config/ClaudeUsage were kept."
    exit 0
fi

case $(uname -m) in
    aarch64) rid=linux-arm64 ;;
    x86_64) rid=linux-x64 ;;
    *) echo "unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

if [[ -f $here/ClaudeUsage.Linux.csproj ]]; then
    command -v dotnet > /dev/null || { echo "dotnet not found: install the .NET 10 SDK" >&2; exit 1; }
    echo "Building for $rid..."
    # Avalonia's build step reports anonymous build statistics unless told not to (the app itself sends nothing)
    export AVALONIA_TELEMETRY_OPTOUT=1
    dotnet publish "$here/ClaudeUsage.Linux.csproj" -c Release -r "$rid" --self-contained false -o "$tmp/app" --nologo -v quiet
    src=$tmp/app
    png=$here/claudeusage.png
else
    [[ -x $here/ClaudeUsage ]] || { echo "no ClaudeUsage.Linux.csproj and no built ClaudeUsage next to this script" >&2; exit 1; }
    src=$here
    png=$here/claudeusage.png
    [[ $src -ef $app_dir ]] && { echo "run install.sh from the unpacked release archive, not from the installed copy" >&2; exit 1; }
fi

# Replace the old version as a whole, so no file of it is left behind. A running instance keeps working
# until it exits (Linux keeps deleted files open); start it again to get the new version.
rm -rf "$app_dir"
mkdir -p "$app_dir"
cp -a "$src/." "$app_dir/"
chmod +x "$app_dir/ClaudeUsage"

mkdir -p "$(dirname "$icon")" "$(dirname "$menu_entry")"
cp "$png" "$icon"

desktop_entry() {
    cat <<EOF
[Desktop Entry]
Type=Application
Name=Claude Usage
Comment=Remaining usage limits of your Claude subscription
Exec="$app_dir/ClaudeUsage"
Icon=claudeusage
Terminal=false
Categories=Development;
EOF
}

desktop_entry > "$menu_entry"
if [[ ${1:-} == --autostart ]]; then
    mkdir -p "$(dirname "$autostart")"
    desktop_entry > "$autostart"
fi

echo "Installed to $app_dir"
echo "Start it from the menu (Programming → Claude Usage) or with: \"$app_dir/ClaudeUsage\" &"
[[ -f $autostart ]] && echo "It starts at login ($autostart)."
exit 0
