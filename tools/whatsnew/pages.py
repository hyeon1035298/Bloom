"""An update log picture made of the first-run guide's own pages: a window on a dark ground that shows the top of one
page after another, packed by pack.py.

    python pages.py <folder of the tutorial QA set's pictures> <out.webp> [--lang ko|en]

Take the pictures first, at twice their size (they are brought down to the window's width here):
    Bloom.exe --data <empty folder> --no-hook --no-tray --demo --lang <lang> --capture <folder> --qa-set tutorial --qa-scale 2
"""
import json
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

SIZE = (960, 400)
GROUND = (18, 18, 21)
EDGE = (44, 44, 48)  # the window's hairline
WIDTH = 800  # the window's: a quarter larger than the real one, so its words can be read at half size in 바뀐 점
AT = (80, 28)  # the window's corner: its lower part runs off the picture
RADIUS = 12
FPS = 15
FADE = 2  # frames one page takes to give way to the next: each costs about as much as a page, so few
PAGES = [("01-start", 1.8), ("02-open", 1.6), ("05-home", 1.6), ("07-addons-picked", 1.6), ("08-notify", 1.6), ("09-keys", 1.6), ("10-done", 1.8)]


def framed(page):
    """The page as a window on the ground: rounded, a hairline around it, a soft shadow under it."""
    page = page.resize((WIDTH, round(page.height * WIDTH / page.width)), Image.LANCZOS)
    w, h = page.size
    shape = Image.new("L", (w, h), 0)
    ImageDraw.Draw(shape).rounded_rectangle((0, 0, w - 1, h - 1), RADIUS, fill=255)
    shadow = Image.new("L", SIZE, 0)
    shadow.paste(shape.point(lambda v: v * 2 // 5), (AT[0], AT[1] + 10))
    out = Image.composite(Image.new("RGB", SIZE, (8, 8, 10)), Image.new("RGB", SIZE, GROUND), shadow.filter(ImageFilter.GaussianBlur(18)))
    rim = Image.new("L", (w + 2, h + 2), 0)
    ImageDraw.Draw(rim).rounded_rectangle((0, 0, w + 1, h + 1), RADIUS + 1, fill=255)
    out.paste(Image.new("RGB", rim.size, EDGE), (AT[0] - 1, AT[1] - 1), rim)
    out.paste(page, AT, shape)
    return out


def main():
    src, out = Path(sys.argv[1]), Path(sys.argv[2])
    lang = sys.argv[sys.argv.index("--lang") + 1] if "--lang" in sys.argv else "ko"
    shots = [(framed(Image.open(src / f"{lang}-dark-tut-{name}.png").convert("RGB")), hold) for name, hold in PAGES]
    with tempfile.TemporaryDirectory() as tmp:
        n = 0
        for i, (shot, hold) in enumerate(shots):  # a held page is one frame in the file (pack.py merges the same frames)
            after = shots[(i + 1) % len(shots)][0]
            for frame in [shot] * round(hold * FPS) + [Image.blend(shot, after, k / (FADE + 1)) for k in range(1, FADE + 1)]:
                frame.save(Path(tmp) / f"f{n:04d}.png")
                n += 1
        (Path(tmp) / "meta.json").write_text(json.dumps({"fps": FPS, "count": n}))
        sys.exit(subprocess.run([sys.executable, str(Path(__file__).with_name("pack.py")), tmp, str(out)]).returncode)


if __name__ == "__main__":
    main()
