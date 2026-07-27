#!/usr/bin/env sh
set -eu

if [ "$#" -lt 2 ]; then
    echo "ERROR: Missing required parameters" >&2
    exit 1
fi

NAME=$1
SOURCE=${2%/}
TFM=${3:-}

DLL_PATH="$SOURCE/$NAME"
if ! [ -f "$DLL_PATH" ]; then
    echo "ERROR: Source not found: $DLL_PATH" >&2
    exit 1
fi

PULSAR="$HOME/.config/Pulsar"

# Determine the destination Local plugin folder.
# Priority: explicit override -> per-framework routing.
#   net4x  (.NET Framework) -> Pulsar/Legacy/Local
#   others (.NET 5+)        -> Pulsar/Interim/Local when the Interim edition exists
if [ -n "${PULSAR_LOCAL_DIR:-}" ]; then
    PLUGIN_DIR="$PULSAR_LOCAL_DIR"
    mkdir -p "$PLUGIN_DIR"
else
    case "$TFM" in
        net4*)
            PLUGIN_DIR="$PULSAR/Legacy/Local"
            if [ ! -d "$PLUGIN_DIR" ]; then
                echo "Missing Local plugin folder: $PLUGIN_DIR" >&2
                echo "Set PULSAR_LOCAL_DIR to your Pulsar Local folder if it is elsewhere." >&2
                exit 2
            fi
            ;;
        *)
            if [ -d "$PULSAR/Interim" ]; then
                PLUGIN_DIR="$PULSAR/Interim/Local"
                mkdir -p "$PLUGIN_DIR"
            elif [ -d "$PULSAR/Local" ]; then
                PLUGIN_DIR="$PULSAR/Local"
            else
                echo "Pulsar Interim not installed, skipping $TFM deploy: $PULSAR/Interim" >&2
                echo "Set PULSAR_LOCAL_DIR to your Pulsar Local folder if it is elsewhere." >&2
                exit 0
            fi
            ;;
    esac
fi

echo "Copying \"$DLL_PATH\" to \"$PLUGIN_DIR/\""
cp -f "$DLL_PATH" "$PLUGIN_DIR/"
