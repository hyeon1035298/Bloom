"""The taskbar folders' icon set (설정 › 작업 표시줄 › 아이콘 바꾸기 › 아이콘): one spec, written as
src/Bloom/Assets/folder-icons.json, which Bloom draws from (Ui/FolderIconSet.cs), and as SVG files per size and tile
to look at.

  python tools/folder-icons.py              writes the JSON
  python tools/folder-icons.py --svg <dir>  also writes <name>.<silver|graphite>.<256|24|16>.svg into <dir>

Every icon is a glyph on the v4 tile: a superellipse (n = 5.9) on a 256 canvas, 204 wide at 32 px and up,
213.3 at 24 px and 224 at 16 px, with a diagonal gradient. Two tiles: silver with a near-black glyph and
graphite with a white one. A glyph is layers of one tone each (1 the ink, 2-4 lighter), filled or stroked
(round caps and joins), less what its cuts cover.
"""
import json, math, sys

TILE = {256: 26.0, 24: 64 / 3, 16: 16.0}  # inset from the canvas edge
SILVER = {"top": "#F7F7F9", "bottom": "#BABAC0", "tones": ["#1C1C1F", "#3A3A3F", "#6A6A70", "#A9A9AF"]}
GRAPHITE = {"top": "#5E5E64", "bottom": "#3C3C42", "tones": ["#FFFFFF", "#C9C9CF", "#8E8E95", "#6E6E75"]}


def f(d, tone=1, cut=None, rule=None):
    l = {"tone": tone, "fill": d}
    if rule: l["rule"] = rule
    if cut: l["cut"] = cut
    return l


def s(d, w, tone=1, cut=None):
    l = {"tone": tone, "stroke": d, "w": w}
    if cut: l["cut"] = cut
    return l


def cf(d, rule=None):
    c = {"fill": d}
    if rule: c["rule"] = rule
    return c


def cs(d, w):
    return {"stroke": d, "w": w}


def n(v):
    return f"{v:.2f}".rstrip("0").rstrip(".")


def rrect(x0, y0, x1, y1, r):
    return (f"M{n(x0 + r)} {n(y0)}H{n(x1 - r)}A{n(r)} {n(r)} 0 0 1 {n(x1)} {n(y0 + r)}V{n(y1 - r)}"
            f"A{n(r)} {n(r)} 0 0 1 {n(x1 - r)} {n(y1)}H{n(x0 + r)}A{n(r)} {n(r)} 0 0 1 {n(x0)} {n(y1 - r)}"
            f"V{n(y0 + r)}A{n(r)} {n(r)} 0 0 1 {n(x0 + r)} {n(y0)}Z")


def circle(cx, cy, r):
    return f"M{n(cx - r)} {n(cy)}A{n(r)} {n(r)} 0 1 0 {n(cx + r)} {n(cy)}A{n(r)} {n(r)} 0 1 0 {n(cx - r)} {n(cy)}Z"


def ellipse(cx, cy, rx, ry):
    return f"M{n(cx - rx)} {n(cy)}A{n(rx)} {n(ry)} 0 1 0 {n(cx + rx)} {n(cy)}A{n(rx)} {n(ry)} 0 1 0 {n(cx - rx)} {n(cy)}Z"


def poly(*pts, close=False):
    d = "M" + " L".join(f"{n(x)} {n(y)}" for x, y in pts)
    return d + ("Z" if close else "")


# ---- the icons already in use (v4), copied from their silver files ------------------------------------
SPARKLE = {
    256: [f("M119 79C119 120.76 135.24 137 177 137C135.24 137 119 153.24 119 195C119 153.24 102.76 137 61 137C102.76 137 119 120.76 119 79ZM175 58C175 72.28 181.72 79 196 79C181.72 79 175 85.72 175 100C175 85.72 168.28 79 154 79C168.28 79 175 72.28 175 58Z")],
    24: [f("M122.67 80C122.67 117.33 138.67 133.33 176 133.33C138.67 133.33 122.67 149.33 122.67 186.67C122.67 149.33 106.67 133.33 69.33 133.33C106.67 133.33 122.67 117.33 122.67 80ZM176 58.67C176 71.47 184.53 80 197.33 80C184.53 80 176 88.53 176 101.33C176 88.53 167.47 80 154.67 80C167.47 80 176 71.47 176 58.67Z")],
    16: [f("M128 56C128 103.52 152.48 128 200 128C152.48 128 128 152.48 128 200C128 152.48 103.52 128 56 128C103.52 128 128 103.52 128 56Z")],
}
_nib = "M56 88H200V120C200 160 159.68 200 128 236C96.32 200 56 160 56 120Z"
DESIGN = {
    256: {"t": [0.56, 128, 126], "layers": [
        f("M76 24H180A20 20 0 0 1 180 64H76A20 20 0 0 1 76 24Z"),
        f(_nib, cut=[cf("M108 152A20 20 0 1 0 148 152A20 20 0 1 0 108 152ZM118 152H138V244H118Z")])]},
    24: [f(rrect(85.33, 42.67, 170.67, 64, 10.67)),
         f("M85.33 74.67H170.67V106.67C170.67 152.27 142.93 187.73 128 208C113.07 187.73 85.33 152.27 85.33 106.67Z",
           cut=[cf("M122.67 128H133.33V186.67H122.67Z")])],
    16: [f(rrect(64, 32, 192, 64, 16)),
         f("M64 80H192V120C192 166.8 150.4 203.2 128 224C105.6 203.2 64 166.8 64 120Z", cut=[cf("M112 128H144V192H112Z")])],
}
_hex = "M128 31L183.43 63L183.43 127L128 159L72.57 127L72.57 63Z"
_cube = [f("M128 31L183.43 63L128 95L72.57 63Z", 3), f("M72.57 63L128 95L128 159L72.57 127Z", 2),
         f("M128 95L183.43 63L183.43 127L128 159Z", 1)]
THREE_D = {
    256: {"t": [0.66, 128, 132.5], "layers": [f("M44 163L128 114.5L212 163L128 211.5Z", 4, cut=[cf(_hex), cs(_hex, 40)])] + _cube},
    24: {"t": [0.68, 128, 127], "layers": [f("M26 163L128 104L230 163L128 222Z", 4, cut=[cf(_hex), cs(_hex, 36)])] + _cube},
    16: [f("M128 40L192 72L128 104L64 72Z", 3), f("M64 72L128 104L128 184L64 152Z", 2),
         f("M128 104L192 72L192 152L128 184Z", 1), f("M48 200H208V216H48Z", 4)],
}
_gear = "M180.65 148.02 L180.96 149.87 L181.14 151.73 L181.2 153.6 L181.14 155.47 L180.96 157.33 L180.65 159.18 L174.18 157.89 L173.85 159.29 L173.43 160.67 L172.93 162.02 L172.33 163.33 L171.65 164.6 L170.89 165.82 L176.38 169.49 L175.29 171.01 L174.1 172.46 L172.82 173.82 L171.46 175.1 L170.01 176.29 L168.49 177.38 L164.82 171.89 L163.6 172.65 L162.33 173.33 L161.02 173.93 L159.67 174.43 L158.29 174.85 L156.89 175.18 L158.18 181.65 L156.33 181.96 L154.47 182.14 L152.6 182.2 L150.73 182.14 L148.87 181.96 L147.02 181.65 L148.31 175.18 L146.91 174.85 L145.53 174.43 L144.18 173.93 L142.87 173.33 L141.6 172.65 L140.38 171.89 L136.71 177.38 L135.19 176.29 L133.74 175.1 L132.38 173.82 L131.1 172.46 L129.91 171.01 L128.82 169.49 L134.31 165.82 L133.55 164.6 L132.87 163.33 L132.27 162.02 L131.77 160.67 L131.35 159.29 L131.02 157.89 L124.55 159.18 L124.24 157.33 L124.06 155.47 L124 153.6 L124.06 151.73 L124.24 149.87 L124.55 148.02 L131.02 149.31 L131.35 147.91 L131.77 146.53 L132.27 145.18 L132.87 143.87 L133.55 142.6 L134.31 141.38 L128.82 137.71 L129.91 136.19 L131.1 134.74 L132.38 133.38 L133.74 132.1 L135.19 130.91 L136.71 129.82 L140.38 135.31 L141.6 134.55 L142.87 133.87 L144.18 133.27 L145.53 132.77 L146.91 132.35 L148.31 132.02 L147.02 125.55 L148.87 125.24 L150.73 125.06 L152.6 125 L154.47 125.06 L156.33 125.24 L158.18 125.55 L156.89 132.02 L158.29 132.35 L159.67 132.77 L161.02 133.27 L162.33 133.87 L163.6 134.55 L164.82 135.31 L168.49 129.82 L170.01 130.91 L171.46 132.1 L172.82 133.38 L174.1 134.74 L175.29 136.19 L176.38 137.71 L170.89 141.38 L171.65 142.6 L172.33 143.87 L172.93 145.18 L173.43 146.53 L173.85 147.91 L174.18 149.31ZM142.7 153.6A9.9 9.9 0 1 0 162.5 153.6A9.9 9.9 0 1 0 142.7 153.6Z"
_gear24 = "M209.22 156.35 L209.73 159.32 L210.03 162.32 L210.13 165.33 L210.03 168.35 L209.73 171.35 L209.22 174.32 L197.73 171.97 L197.07 174.61 L196.2 177.18 L195.13 179.68 L193.85 182.08 L192.37 184.37 L190.72 186.52 L199.72 194.04 L197.71 196.29 L195.56 198.4 L193.27 200.36 L190.85 202.16 L188.31 203.79 L185.67 205.25 L180.35 194.8 L177.87 195.93 L175.32 196.86 L172.69 197.57 L170.02 198.07 L167.31 198.34 L164.59 198.39 L164.33 210.12 L161.32 209.95 L158.33 209.58 L155.36 209.01 L152.45 208.24 L149.59 207.28 L146.81 206.12 L151.66 195.44 L149.23 194.21 L146.91 192.79 L144.72 191.19 L142.66 189.4 L140.76 187.46 L139.02 185.36 L129.69 192.47 L127.94 190.01 L126.37 187.44 L124.97 184.77 L123.75 182.01 L122.73 179.18 L121.89 176.28 L133.27 173.41 L132.71 170.75 L132.38 168.05 L132.27 165.33 L132.38 162.62 L132.71 159.92 L133.27 157.25 L121.89 154.39 L122.73 151.49 L123.75 148.65 L124.97 145.9 L126.37 143.22 L127.94 140.65 L129.69 138.19 L139.02 145.3 L140.76 143.21 L142.66 141.26 L144.72 139.48 L146.91 137.87 L149.23 136.45 L151.66 135.23 L146.81 124.54 L149.59 123.39 L152.45 122.43 L155.36 121.66 L158.33 121.08 L161.32 120.71 L164.33 120.54 L164.59 132.27 L167.31 132.33 L170.02 132.6 L172.69 133.1 L175.32 133.81 L177.87 134.74 L180.35 135.87 L185.67 125.42 L188.31 126.87 L190.85 128.51 L193.27 130.31 L195.56 132.27 L197.71 134.37 L199.72 136.62 L190.72 144.14 L192.37 146.3 L193.85 148.59 L195.13 150.99 L196.2 153.48 L197.07 156.06 L197.73 158.7ZM151.47 165.33A13.87 13.87 0 1 0 179.2 165.33A13.87 13.87 0 1 0 151.47 165.33Z"
SYSTEM = {
    256: [f(rrect(66.8, 70, 168, 158, 18.7), cut=[cf("M0 87.6H256V99.7H0Z"), cf(circle(152.6, 153.6, 36))]),
          f(_gear, rule="evenodd")],
    24: [f(rrect(53.33, 53.33, 181.33, 160, 21.33), cut=[cf("M0 74.67H256V85.33H0Z"), cf(circle(165.33, 165.33, 55.47))]),
         f(_gear24, rule="evenodd")],
    # 16 px: the window alone (its file has no gear in reach of a pixel)
    16: [f(rrect(48, 48, 176, 160, 24), cut=[cf("M0 80H256V96H0Z"), cf(circle(168, 168, 72))])],
}


def bars(spec, r):
    return "".join(rrect(x - r, y0, x + r, y1, r) for x, y0, y1 in spec)


AUDIO = {
    256: {"t": [0.55, 128, 128], "layers": [f(bars([(36.8, 88, 168), (82.4, 56, 200), (128, 24, 232), (173.6, 72, 184), (219.2, 100, 156)], 15))]},
    24: [f(bars([(64, 96, 160), (96, 74.67, 181.33), (128, 53.33, 202.67), (160, 85.33, 170.67), (192, 106.67, 149.33)], 10.67))],
    16: [f(bars([(80, 80, 176), (128, 48, 208), (176, 96, 160)], 16))],
}
# 최근 앱 (the mod's loose folder): ClockIcon in pill-taskbar.wh.cpp, one drawing at every size
_clock = [s(circle(128, 128, 48.5), 15), s("M128 101V128H148", 14)]
CLOCK = {256: _clock, 24: _clock, 16: _clock}


# ---- new icons ----------------------------------------------------------------------------------------
def code(k=1.0, w=16):
    def p(x, y): return (128 + (x - 128) * k, 128 + (y - 128) * k)
    return [s(poly(p(98, 90), p(62, 128), p(98, 166)), w), s(poly(p(158, 90), p(194, 128), p(158, 166)), w),
            s(poly(p(140, 82), p(116, 174)), w)]


def terminal(k=1.0, w=14, frame=None):
    x0, y0, x1, y1 = [128 + (v - 128) * k for v in (62, 74, 194, 182)]
    def p(x, y): return (128 + (x - 128) * k, 128 + (y - 128) * k)
    return [f(rrect(x0, y0, x1, y1, 22 * k),
              cut=[cs(poly(p(92, 106), p(116, 128), p(92, 150)), w), cs(poly(p(130, 152), p(162, 152)), w)])]


def game(k=1.0, w=11, detail=True):
    def p(x, y): return (128 + (x - 128) * k, 128 + (y - 128) * k)
    body = (f"M{n(p(96, 88)[0])} {n(p(96, 88)[1])}"
            f"H{n(p(160, 88)[0])}"
            f"C{n(p(192, 88)[0])} {n(p(192, 88)[1])} {n(p(204, 112)[0])} {n(p(204, 112)[1])} {n(p(208, 140)[0])} {n(p(208, 140)[1])}"
            f"C{n(p(213, 172)[0])} {n(p(213, 172)[1])} {n(p(190, 182)[0])} {n(p(190, 182)[1])} {n(p(172, 162)[0])} {n(p(172, 162)[1])}"
            f"L{n(p(162, 152)[0])} {n(p(162, 152)[1])}H{n(p(94, 152)[0])}"
            f"L{n(p(84, 162)[0])} {n(p(84, 162)[1])}"
            f"C{n(p(66, 182)[0])} {n(p(66, 182)[1])} {n(p(43, 172)[0])} {n(p(43, 172)[1])} {n(p(48, 140)[0])} {n(p(48, 140)[1])}"
            f"C{n(p(52, 112)[0])} {n(p(52, 112)[1])} {n(p(64, 88)[0])} {n(p(64, 88)[1])} {n(p(96, 88)[0])} {n(p(96, 88)[1])}Z")
    cuts = [cs(poly(p(80, 120), p(108, 120)), w), cs(poly(p(94, 106), p(94, 134)), w)]
    if detail:
        cuts += [cf(circle(*p(160, 112), 8 * k)), cf(circle(*p(178, 130), 8 * k))]
    else:
        cuts += [cf(circle(*p(168, 120), 11 * k))]
    return [f(body, cut=cuts)]


def video(k=1.0):
    x0, y0, x1, y1 = [128 + (v - 128) * k for v in (58, 80, 198, 176)]
    def p(x, y): return (128 + (x - 128) * k, 128 + (y - 128) * k)
    tri = poly(p(116, 104), p(148, 128), p(116, 152), close=True)
    return [f(rrect(x0, y0, x1, y1, 26 * k), cut=[cf(tri), cs(tri, 10 * k)])]


def photo(k=1.0, w=15, detail=True):
    def p(x, y): return (128 + (x - 128) * k, 128 + (y - 128) * k)
    x0, y0, x1, y1 = [128 + (v - 128) * k for v in (64, 76, 192, 180)]
    layers = [s(rrect(x0, y0, x1, y1, 20 * k), w),
              f(poly(p(64, 172), p(108, 124), p(140, 156), p(160, 138), p(192, 170), p(192, 180), p(64, 180), close=True))]
    if detail:
        layers.append(f(circle(*p(160, 106), 12 * k)))
    return layers


def chat(k=1.0, detail=True):
    def p(x, y): return (128 + (x - 128) * k, 128 + (y - 128) * k)
    r = 30 * k
    x0, y0, x1, y1 = p(58, 72)[0], p(58, 72)[1], p(198, 162)[0], p(198, 162)[1]
    bubble = (f"M{n(x0 + r)} {n(y0)}H{n(x1 - r)}A{n(r)} {n(r)} 0 0 1 {n(x1)} {n(y0 + r)}V{n(y1 - r)}"
              f"A{n(r)} {n(r)} 0 0 1 {n(x1 - r)} {n(y1)}H{n(p(112, 0)[0])}L{n(p(80, 190)[0])} {n(p(0, 190)[1])}"
              f"L{n(p(84, 162)[0])} {n(y1)}H{n(x0 + r)}A{n(r)} {n(r)} 0 0 1 {n(x0)} {n(y1 - r)}V{n(y0 + r)}"
              f"A{n(r)} {n(r)} 0 0 1 {n(x0 + r)} {n(y0)}Z")
    cuts = [cf(circle(*p(x, 117), 11 * k)) for x in (96, 128, 160)] if detail else [cs(poly(p(98, 117), p(158, 117)), 18 * k)]
    return [f(bubble, cut=cuts)]


def globe(k=1.0, w=14):
    r = 56 * k
    return [s(circle(128, 128, r), w + 1), s(ellipse(128, 128, r * 0.42, r), w),
            s(poly((128 - r, 128), (128 + r, 128)), w)]


def document(k=1.0, w=12, lines=2):
    def p(x, y): return (128 + (x - 128) * k, 128 + (y - 128) * k)
    # the page's corners get their roundness from a stroke of the same tone around it
    page = poly(p(88, 62), p(140, 62), p(172, 94), p(172, 194), p(88, 194), close=True)
    ys = [132, 162][:lines]
    lines_cut = [cs(poly(p(110, y), p(150, y)), w) for y in ys]
    notch = cs(poly(p(140, 62), p(140, 94), p(172, 94)), 12 * k)
    return [f(page, cut=lines_cut + [notch]), s(page, 16 * k, cut=lines_cut + [notch])]


def folder(k=1.0):
    def p(x, y): return (128 + (x - 128) * k, 128 + (y - 128) * k)
    back = (f"M{n(p(70, 70)[0])} {n(p(70, 70)[1])}H{n(p(108, 70)[0])}"
            f"L{n(p(124, 86)[0])} {n(p(124, 86)[1])}H{n(p(186, 86)[0])}"
            f"A{n(14 * k)} {n(14 * k)} 0 0 1 {n(p(200, 100)[0])} {n(p(200, 100)[1])}V{n(p(0, 172)[1])}"
            f"A{n(14 * k)} {n(14 * k)} 0 0 1 {n(p(186, 186)[0])} {n(p(186, 186)[1])}H{n(p(70, 186)[0])}"
            f"A{n(14 * k)} {n(14 * k)} 0 0 1 {n(p(56, 172)[0])} {n(p(56, 172)[1])}V{n(p(0, 84)[1])}"
            f"A{n(14 * k)} {n(14 * k)} 0 0 1 {n(p(70, 70)[0])} {n(p(70, 70)[1])}Z")
    return [f(back, cut=[cs(poly(p(56, 112), p(200, 112)), 12 * k)])]


NEW = {
    "code": {256: {"t": [0.86, 128, 128], "layers": code()}, 24: code(1.18, 21), 16: code(1.34, 30)},
    "terminal": {256: {"t": [0.86, 128, 128], "layers": terminal()}, 24: terminal(1.16, 19), 16: terminal(1.3, 26)},
    "game": {256: {"t": [0.86, 128, 128], "layers": game()}, 24: game(1.12, 15), 16: game(1.22, 22, False)},
    "video": {256: {"t": [0.86, 128, 128], "layers": video()}, 24: video(1.14), 16: video(1.26)},
    "photo": {256: {"t": [0.86, 128, 128], "layers": photo()}, 24: photo(1.16, 20), 16: photo(1.3, 28, False)},
    "chat": {256: {"t": [0.86, 128, 128], "layers": chat()}, 24: chat(1.14), 16: chat(1.26, False)},
    "globe": {256: {"t": [0.86, 128, 128], "layers": globe()}, 24: globe(1.18, 19), 16: globe(1.32, 27)},
    "document": {256: {"t": [0.86, 128, 128], "layers": document()}, 24: document(1.14, 16), 16: document(1.24, 24, 1)},
    "folder": {256: {"t": [0.86, 128, 128], "layers": folder()}, 24: folder(1.12), 16: folder(1.22)},
}

ICONS = {"ai-sparkle": SPARKLE, "design": DESIGN, "3d": THREE_D, "system": SYSTEM, "audio": AUDIO, **NEW}


def norm(v):
    return v if isinstance(v, dict) else {"layers": v}


def tile_path(inset, curve=5.9, steps=360):
    c, h = 128.0, 128.0 - inset
    pts = []
    for i in range(steps):
        a = 2 * math.pi * i / steps
        ca, sa = math.cos(a), math.sin(a)
        x = c + h * math.copysign(abs(ca) ** (2 / curve), ca)
        y = c + h * math.copysign(abs(sa) ** (2 / curve), sa)
        pts.append((x, y))
    return "M" + " L".join(f"{x:.2f} {y:.2f}" for x, y in pts) + "Z"


def svg(icon, size, palette):
    v = norm(icon[size])
    inset = TILE[size]
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">',
           f'<defs><linearGradient id="t" gradientUnits="userSpaceOnUse" x1="{inset}" y1="{inset}" x2="{256 - inset}" y2="{256 - inset}">'
           f'<stop offset="0" stop-color="{palette["top"]}"/><stop offset="1" stop-color="{palette["bottom"]}"/></linearGradient></defs>',
           f'<path fill="url(#t)" d="{tile_path(inset)}"/>']
    if "t" in v:
        k, cx, cy = v["t"]
        out.append(f'<g transform="translate({cx} {cy}) scale({k}) translate(-128 -128)">')
    for i, l in enumerate(v["layers"]):
        color = palette["tones"][l["tone"] - 1]
        mask = ""
        if l.get("cut"):
            mid = f"m{i}"
            parts = ['<rect x="-256" y="-256" width="768" height="768" fill="#fff"/>']
            for c in l["cut"]:
                if "fill" in c:
                    parts.append(f'<path fill="#000" fill-rule="{c.get("rule", "nonzero")}" d="{c["fill"]}"/>')
                else:
                    parts.append(f'<path fill="none" stroke="#000" stroke-width="{c["w"]}" stroke-linecap="round" stroke-linejoin="round" d="{c["stroke"]}"/>')
            out.append(f'<mask id="{mid}" maskUnits="userSpaceOnUse" x="-256" y="-256" width="768" height="768">{"".join(parts)}</mask>')
            mask = f' mask="url(#{mid})"'
        if "fill" in l:
            out.append(f'<path fill="{color}" fill-rule="{l.get("rule", "nonzero")}"{mask} d="{l["fill"]}"/>')
        else:
            out.append(f'<path fill="none" stroke="{color}" stroke-width="{l["w"]}" stroke-linecap="round" stroke-linejoin="round"{mask} d="{l["stroke"]}"/>')
    if "t" in v:
        out.append("</g>")
    out.append("</svg>")
    return "\n".join(out)


if __name__ == "__main__":
    import os
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    spec = {"curve": 5.9, "tile": {str(k): v for k, v in TILE.items()}, "silver": SILVER, "graphite": GRAPHITE,
            "icons": {k: {str(sz): norm(v) for sz, v in ic.items()} for k, ic in ICONS.items()}}
    with open(os.path.join(root, "src", "Bloom", "Assets", "folder-icons.json"), "w", encoding="utf8", newline="\n") as fh:
        json.dump(spec, fh, ensure_ascii=False, separators=(",", ":"))
        fh.write("\n")
    if len(sys.argv) > 2 and sys.argv[1] == "--svg":
        outdir = sys.argv[2]
        os.makedirs(outdir, exist_ok=True)
        # the clock is the mod's own drawing (ClockIcon in pill-taskbar.wh.cpp), here only to look at beside the set
        for name, icon in {**ICONS, "clock": CLOCK}.items():
            for size in (256, 24, 16):
                for tname, pal in (("silver", SILVER), ("graphite", GRAPHITE)):
                    with open(os.path.join(outdir, f"{name}.{tname}.{size}.svg"), "w", encoding="utf8") as fh:
                        fh.write(svg(icon, size, pal))
