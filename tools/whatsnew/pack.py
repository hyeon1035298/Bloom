"""Packs capture.mjs's frames into a looping animated WebP: frames the same as the one before are merged into its
duration, so a held moment costs one frame.

    python pack.py <frames dir> <out.webp> [--quality 82]
"""
import json
import sys
from pathlib import Path

from PIL import Image, ImageChops


def main():
    src, out = Path(sys.argv[1]), Path(sys.argv[2])
    quality = int(sys.argv[sys.argv.index("--quality") + 1]) if "--quality" in sys.argv else 82
    meta = json.loads((src / "meta.json").read_text())
    step = 1000 / meta["fps"]
    frames, durations = [], []
    for i, path in enumerate(sorted(src.glob("f*.png"))):
        im = Image.open(path).convert("RGB")
        if frames and ImageChops.difference(im, frames[-1]).getbbox() is None:
            durations[-1] += step
            continue
        frames.append(im)
        durations.append(step)
    # whole milliseconds that still add up to the loop
    ms, total, done = [], 0.0, 0
    for d in durations:
        total += d
        ms.append(round(total) - done)
        done += ms[-1]
    out.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(out, save_all=True, append_images=frames[1:], duration=ms, loop=0,
                   quality=quality, method=6, minimize_size=True)
    print(f"{out.name}: {len(frames)} frames, {done} ms, {out.stat().st_size // 1024} KB")


if __name__ == "__main__":
    main()
