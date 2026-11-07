#!/bin/sh
# Makes every picture of one release's update log: sh tools/whatsnew/make.sh <version>
# Scenes with words are made once per language (<name>-<lang>.webp), the others once.
set -e
here=$(dirname "$0")
out="$here/../../docs/media/whatsnew/$1"
[ -n "$1" ] || { echo "usage: make.sh <version>"; exit 2; }
case "$1" in
1.0.0)
  node "$here/capture.mjs" taskbar "$out/taskbar.webp"
  node "$here/capture.mjs" alttab "$out/alttab.webp"
  for lang in ko en; do
    node "$here/capture.mjs" memory "$out/memory-$lang.webp" --lang $lang
    node "$here/capture.mjs" brightness "$out/brightness-$lang.webp" --lang $lang
    node "$here/capture.mjs" coffee "$out/coffee-$lang.webp" --lang $lang
  done ;;
1.1.0)
  for lang in ko en; do
    node "$here/capture.mjs" snippets "$out/snippets-$lang.webp" --lang $lang
  done ;;
1.2.0)
  for lang in ko en; do
    node "$here/capture.mjs" reorder "$out/reorder-$lang.webp" --lang $lang
  done ;;
*) echo "no pictures listed for $1"; exit 2 ;;
esac
