#!/bin/sh
# randevu-node installer for macOS and Linux: downloads the latest release for this machine into ~/.local/bin (or $RANDEVU_NODE_DIR)
# and verifies its SHA-256 against the release's SHA256SUMS. Homebrew users: brew install codehepta/randebul/randevu-node.
set -eu
REPO="${RANDEVU_NODE_REPO:-codehepta/randebul-node}"
DIR="${RANDEVU_NODE_DIR:-$HOME/.local/bin}"
case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) RID=osx-arm64 ;;
  Darwin-x86_64) RID=osx-x64 ;;
  Linux-x86_64) RID=linux-x64 ;;
  Linux-aarch64|Linux-arm64) RID=linux-arm64 ;;
  *) echo "desteklenmeyen sistem: $(uname -s) $(uname -m)"; exit 1 ;;
esac
TAG=$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" | sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p' | head -n 1)
[ -n "$TAG" ] || { echo "sürüm bulunamadı"; exit 1; }
VERSION="${TAG#v}"
FILE="randevu-node-$VERSION-$RID.tar.gz"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
echo "indiriliyor: $TAG ($RID)"
curl -fsSL -o "$TMP/$FILE" "https://github.com/$REPO/releases/download/$TAG/$FILE"
curl -fsSL -o "$TMP/SHA256SUMS" "https://github.com/$REPO/releases/download/$TAG/SHA256SUMS"
EXPECTED=$(grep " $FILE\$" "$TMP/SHA256SUMS" | awk '{print $1}')
ACTUAL=$(shasum -a 256 "$TMP/$FILE" | awk '{print $1}')
[ "$EXPECTED" = "$ACTUAL" ] || { echo "SHA-256 uyuşmuyor"; exit 1; }
mkdir -p "$DIR"
tar -C "$TMP" -xzf "$TMP/$FILE"
mv "$TMP/randevu-node" "$DIR/randevu-node"
chmod +x "$DIR/randevu-node"
if [ "$(uname -s)" = Darwin ]; then xattr -d com.apple.quarantine "$DIR/randevu-node" 2>/dev/null || true; fi
echo "kuruldu: $DIR/randevu-node ($TAG)"
case ":$PATH:" in *":$DIR:"*) ;; *) echo "PATH'e ekleyin: export PATH=\"$DIR:\$PATH\"" ;; esac
