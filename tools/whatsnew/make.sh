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
1.3.0)
  for lang in ko en; do
    node "$here/capture.mjs" question "$out/question-$lang.webp" --lang $lang
  done ;;
1.4.0)
  for lang in ko en; do
    node "$here/capture.mjs" chat "$out/chat-$lang.webp" --lang $lang
  done ;;
1.4.3)
  for lang in ko en; do
    node "$here/capture.mjs" auto "$out/auto-$lang.webp" --lang $lang
  done ;;
1.5.0|1.5.1)
  for lang in ko en; do
    node "$here/capture.mjs" runcard "$out/runcard-$lang.webp" --lang $lang
  done ;;
1.6.0)
  # the first-run guide's own pages: take them first (pages.py says how), one folder per language under $TUTORIAL_SHOTS
  for lang in ko en; do
    python "$here/pages.py" "$TUTORIAL_SHOTS/$lang" "$out/tutorial-$lang.webp" --lang $lang
  done ;;
1.6.1)
  # the capture drawing bar's own frames: take them first, one capture folder for both languages in $DRAW_FRAMES:
  #   Bloom.exe --data <empty folder> --no-hook --no-tray --demo --lang <lang> --capture $DRAW_FRAMES --qa-set drawclip
  for lang in ko en; do
    python "$here/pack.py" "$DRAW_FRAMES/drawclip-$lang" "$out/draw-$lang.webp"
  done ;;
*) echo "no pictures listed for $1"; exit 2 ;;
esac
