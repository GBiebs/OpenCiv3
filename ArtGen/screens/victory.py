"""The end-of-game ("victory") screen, remade: a background for each kind of
ending, the frames our portraits are shown in, the speech bubble, the close
button and the Next button.

    python ArtGen/screens/build.py   (or victory.py alone)

Civ3's screen is one of seven 1024x768 backgrounds in Art/VictoryScreen/
(VictoryScreenLayout.Backgrounds), with the leaders' heads drawn at fixed
places on it: on a win the player's large head (200x240) at the top and
seven small ones (120x150) in two rows below; on a defeat (or retirement)
the player's head in the middle and the others round it. The remakes keep
those places, at twice the size, and set a dark niche into the art at each
of them, so the code places the heads exactly where it places Civ3's.
Into C7/ModernArt/Art/VictoryScreen/:

    conquer_victory.png      crossed swords and shields, on crimson
    culture_victory.png      a temple's columns and hearts, on plum
    diplomatic_victory.png   an olive wreath and sealed treaties, on navy
    space_victory.png        stars, a ringed planet and a rocket, on night
    domination_victory.png   a globe under a crown, on amber
    histograph_victory.png   hourglasses, a clock and rising lines, on teal
                             (the time limit's ending)
    defeat.png               cracked stone in the rain, on slate (a loss,
                             and a retirement)
    textbubble_top.png, textbubble_mid.png, textbubble_bot.png
                             the speech bubble, in Civ3's three pieces
                             (200 wide; 12, 16 and 35 high, the bottom one
                             with the tail)

and our own, which have no Civ3 file:

    frame_large_<style>.png, frame_small_<style>.png
                             the frames round the portraits (200x240 and
                             120x150), in four styles for the moods:
                             glory, love, angry and hurt
                             (VictoryScreenLayout.FrameFor)
    heart_badge.png          the heart the cultural winner's face is shown
                             in over each rival's head (64x58, with a round
                             window 32 across at (32, 26))
    exit.png                 the close button: normal, hovered and pressed
                             36x38 cells at x = 1, 38 and 75, as Civ3's
                             ExitX.pcx lays out its own
    next.png                 the Next button: normal and hovered 128x36
                             cells, one above the other (its word is
                             written by the game)

Everything is drawn with code; no image of Civ3's is read or traced.
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from canvas import SCALE  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT_DIR = os.path.join(REPO, 'C7', 'ModernArt', 'Art', 'VictoryScreen')

W, H = 1024, 768
SS = 2  # supersampling of the drawn shapes

# The heads' places (VictoryScreenLayout's VictoryPlaces and DefeatPlaces):
# the top left corners; place 0 is large (200x240), the rest small (120x150).
VICTORY_PLACES = [(409, 67), (281, 367), (449, 367), (614, 367), (218, 541), (371, 541), (528, 541), (681, 541)]
DEFEAT_PLACES = [(409, 302), (450, 57), (222, 141), (678, 141), (128, 355), (765, 355), (222, 562), (678, 562)]
LARGE, SMALL = (200, 240), (120, 150)

STONE = np.array([150, 140, 118]) / 255
STONE_DARK = np.array([48, 44, 36]) / 255


# ---- Helpers ----------------------------------------------------------------

def rgb(c):
    return np.asarray(c, np.float32) / 255


def noise(h, w, cell, seed, octaves=4, stretch=(1, 1)):
    """Soft value noise in about -1..1, features about `cell` pixels across."""
    total = np.zeros((h, w), np.float32)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        c = max(1.0, cell / 2 ** o)
        rng = np.random.RandomState(seed + 101 * o)
        cx, cy = max(1, c * stretch[0]), max(1, c * stretch[1])
        sw, sh = int(w / cx) + 3, int(h / cy) + 3
        small = Image.fromarray(rng.rand(sh, sw).astype(np.float32), 'F')
        big = np.asarray(small.resize((int(sw * cx), int(sh * cy)), Image.BICUBIC))[:h, :w]
        total += amp * (big - 0.5) * 2
        norm += amp
        amp *= 0.5
    return total / norm


class Layer:
    """Shapes drawn in Civ3 pixels onto a coverage mask the size of the
    output (twice Civ3's), supersampled."""

    def __init__(self, w, h):
        self.w, self.h = w, h
        self.k = SCALE * SS
        self.img = Image.new('L', (w * self.k, h * self.k), 0)
        self.d = ImageDraw.Draw(self.img)

    def _p(self, pts):
        return [(x * self.k, y * self.k) for x, y in pts]

    def poly(self, pts, fill=255):
        self.d.polygon(self._p(pts), fill=fill)

    def ellipse(self, box, fill=255):
        x0, y0, x1, y1 = box
        self.d.ellipse([x0 * self.k, y0 * self.k, x1 * self.k, y1 * self.k], fill=fill)

    def ring(self, box, width, fill=255):
        x0, y0, x1, y1 = box
        self.d.ellipse([x0 * self.k, y0 * self.k, x1 * self.k, y1 * self.k], outline=fill, width=max(1, int(width * self.k)))

    def rect(self, box, radius=0, fill=255):
        x0, y0, x1, y1 = box
        self.d.rounded_rectangle([x0 * self.k, y0 * self.k, x1 * self.k, y1 * self.k], radius=radius * self.k, fill=fill)

    def line(self, pts, width, fill=255):
        self.d.line(self._p(pts), fill=fill, width=max(1, int(width * self.k)), joint='curve')

    def arc(self, box, start, end, width, fill=255):
        x0, y0, x1, y1 = box
        self.d.arc([x0 * self.k, y0 * self.k, x1 * self.k, y1 * self.k], start, end, fill=fill, width=max(1, int(width * self.k)))

    def mask(self, blur=0.0):
        img = self.img.resize((self.w * SCALE, self.h * SCALE), Image.BOX)
        if blur > 0:
            img = img.filter(ImageFilter.GaussianBlur(blur * SCALE))
        return np.asarray(img).astype(np.float32) / 255


def over(base, color, alpha):
    a = np.clip(alpha, 0, 1)[..., None]
    c = color if np.ndim(color) == 3 else np.asarray(color, np.float32)[None, None, :]
    return base * (1 - a) + c * a


def emboss(base, m, color, alpha=1.0, relief=0.35):
    """Lays a shape on in a color, lit from the upper left: a light edge up
    and left, a dark one down and right, and a soft shadow under it."""
    sh = np.roll(np.roll(m, 3, 0), 3, 1)
    shadow = np.asarray(Image.fromarray((sh * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3))).astype(np.float32) / 255
    base = base * (1 - 0.35 * alpha * shadow[..., None])
    up = np.clip(m - np.roll(np.roll(m, 2, 0), 2, 1), 0, 1)
    down = np.clip(m - np.roll(np.roll(m, -2, 0), -2, 1), 0, 1)
    c = np.asarray(color, np.float32)
    shade = (1 + relief * up - relief * down)[..., None]
    col = np.clip(c[None, None, :] * shade, 0, 1)
    return over(base, col, m * alpha)


def blurred(m, r):
    return np.asarray(Image.fromarray((np.clip(m, 0, 1) * 255).astype(np.uint8)).filter(
        ImageFilter.GaussianBlur(r))).astype(np.float32) / 255


def grid(w, h):
    ys, xs = np.mgrid[0:h * SCALE, 0:w * SCALE].astype(np.float32)
    return (xs + 0.5) / SCALE, (ys + 0.5) / SCALE


def field(c_mid, c_edge, seed, center=(512, 300), mottle=0.06):
    """A field shading from c_mid round `center` to c_edge at the corners,
    mottled like old paint."""
    xs, ys = grid(W, H)
    d = np.clip(np.hypot((xs - center[0]) / 720, (ys - center[1]) / 560), 0, 1) ** 1.3
    col = rgb(c_mid)[None, None, :] * (1 - d[..., None]) + rgb(c_edge)[None, None, :] * d[..., None]
    n = noise(H * SCALE, W * SCALE, 140 * SCALE, seed) * mottle + noise(H * SCALE, W * SCALE, 6 * SCALE, seed + 7, 2) * mottle * 0.3
    return np.clip(col * (1 + n[..., None]), 0, 1)


def rays(base, center, color, count=28, alpha=0.18, reach=620):
    xs, ys = grid(W, H)
    ang = np.arctan2(ys - center[1], xs - center[0])
    r = np.hypot(xs - center[0], ys - center[1])
    stripes = (np.cos(ang * count) * 0.5 + 0.5) ** 3
    fall = np.clip(1 - r / reach, 0, 1) ** 1.2
    return over(base, color, stripes * fall * alpha)


def glow(base, center, radius, color, alpha):
    xs, ys = grid(W, H)
    d = np.clip(1 - np.hypot(xs - center[0], ys - center[1]) / radius, 0, 1) ** 2
    return base + (np.asarray(color)[None, None, :] - base) * (d * alpha)[..., None]


def niches(base, places):
    """A dark recess at each head's place, a little larger than the head,
    with a lit lip below and right."""
    for i, (x, y) in enumerate(places):
        w, h = LARGE if i == 0 else SMALL
        pad = 5
        outer = Layer(W, H)
        outer.rect((x - pad, y - pad, x + w + pad, y + h + pad), radius=6)
        m = outer.mask()
        soft = blurred(m, 6 * SCALE)
        base = base * (1 - 0.45 * soft[..., None])
        lip = Layer(W, H)
        lip.rect((x - pad + 2, y - pad + 2, x + w + pad + 2, y + h + pad + 2), radius=6)
        lm = np.clip(lip.mask() - m, 0, 1)
        base = over(base, (0.92, 0.88, 0.78), lm * 0.35)
        xs, ys = grid(W, H)
        t = np.clip((ys - (y - pad)) / (h + 2 * pad), 0, 1)
        well = np.stack([0.07 + 0.05 * t, 0.06 + 0.04 * t, 0.06 + 0.03 * t], -1)
        base = over(base, well, m)
        inner = Layer(W, H)
        inner.rect((x - pad + 1, y - pad + 1, x + w + pad - 1, y + h + pad - 1), radius=5)
        im = inner.mask()
        base = over(base, (0, 0, 0), np.clip(m - im, 0, 1) * 0.7)
    return base


def stone_frame(base, accent):
    """The stone border round the screen, with a tooled line and an inner
    rule in the ending's accent color."""
    xs, ys = grid(W, H)
    border = 14
    inside = (xs > border) & (xs < W - border) & (ys > border) & (ys < H - border)
    n = noise(H * SCALE, W * SCALE, 60 * SCALE, 77) * 0.05 + noise(H * SCALE, W * SCALE, 3 * SCALE, 78, 2, (12, 1)) * 0.02
    stone = STONE[None, None, :] * (1 + n[..., None]) * (1.04 - 0.08 * (xs / W * 0.4 + ys / H * 0.6))[..., None]
    # Bevel: lit along the outer top and left, shaded along the bottom and right.
    dl, dt, dr, db = xs, ys, W - xs, H - ys
    light = np.clip(1 - np.minimum(dl, dt) / 5, 0, 1)
    dark = np.clip(1 - np.minimum(dr, db) / 5, 0, 1)
    stone = stone + (1 - stone) * (light * 0.4)[..., None] - stone * (dark * 0.45)[..., None]
    # The inner edge, sunk: shaded top and left, lit bottom and right.
    il, it, ir, ib = xs - border, ys - border, W - border - xs, H - border - ys
    near = np.minimum(np.minimum(il, it), np.minimum(ir, ib))
    out = np.where(inside[..., None], base, stone)
    ring_in = inside & (near < 8)
    upper = np.minimum(il, it) < np.minimum(ir, ib)
    shade = np.clip(1 - near / 8, 0, 1) * ring_in
    out = np.where((upper & ring_in)[..., None], out * (1 - 0.55 * shade[..., None]), out)
    out = np.where((~upper & ring_in)[..., None], out + (1 - out) * 0.15 * shade[..., None], out)
    # A tooled groove round the stone.
    groove = (np.abs(np.minimum(np.minimum(xs, ys), np.minimum(W - xs, H - ys)) - 7) < 0.6) & ~inside
    out = np.where(groove[..., None], out * 0.6, out)
    # The accent rule, a little inside the field.
    rule = (np.abs(near - 10) < 0.7) & inside
    out = over(out, accent, rule * 0.55)
    return out


def save(img, name):
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)
    img.save(path, optimize=True)
    print('wrote', path)


def to_image(col, alpha=None):
    if alpha is None:
        alpha = np.ones(col.shape[:2], np.float32)
    rgba = np.dstack([np.clip(col, 0, 1), np.clip(alpha, 0, 1)[..., None]])
    rgba = (rgba * 255 + 0.5).astype(np.uint8)
    rgba[rgba[..., 3] == 0, :3] = 0
    return Image.fromarray(rgba, 'RGBA')


# ---- Motifs -------------------------------------------------------------------

def sword(L, a, b, width, guard):
    """A sword from hilt end a to point b."""
    (x0, y0), (x1, y1) = a, b
    dx, dy = x1 - x0, y1 - y0
    n = math.hypot(dx, dy)
    ux, uy = dx / n, dy / n
    px, py = -uy, ux
    hilt = 0.16 * n
    gx, gy = x0 + ux * hilt, y0 + uy * hilt
    # Blade, tapering to the point.
    L.poly([(gx + px * width, gy + py * width), (x1 - ux * width * 3 + px * width * 0.8, y1 - uy * width * 3 + py * width * 0.8),
            (x1, y1), (x1 - ux * width * 3 - px * width * 0.8, y1 - uy * width * 3 - py * width * 0.8),
            (gx - px * width, gy - py * width)])
    # Crossguard, grip and pommel.
    L.line([(gx + px * guard, gy + py * guard), (gx - px * guard, gy - py * guard)], width * 1.1)
    L.line([(x0, y0), (gx, gy)], width * 0.9)
    L.ellipse((x0 - width * 1.2, y0 - width * 1.2, x0 + width * 1.2, y0 + width * 1.2))


def shield(L, cx, cy, w, h):
    L.poly([(cx - w / 2, cy - h / 2), (cx + w / 2, cy - h / 2), (cx + w / 2, cy + h * 0.1),
            (cx, cy + h / 2), (cx - w / 2, cy + h * 0.1)])


def heart(L, cx, cy, s):
    """A heart about 2s wide, its notch at (cx, cy - 0.35s)."""
    L.ellipse((cx - s, cy - s * 0.85, cx + s * 0.08, cy + s * 0.2))
    L.ellipse((cx - s * 0.08, cy - s * 0.85, cx + s, cy + s * 0.2))
    L.poly([(cx - s * 0.97, cy - s * 0.18), (cx + s * 0.97, cy - s * 0.18), (cx, cy + s * 1.05)])


def leaf_branch(L, cx, cy, r, start, end, leaves, size, side):
    """Leaves along an arc of a circle (angles in degrees, y down)."""
    pts = []
    for i in range(41):
        t = math.radians(start + (end - start) * i / 40)
        pts.append((cx + r * math.cos(t), cy + r * math.sin(t)))
    L.line(pts, 2.2)
    for i in range(leaves):
        t = math.radians(start + (end - start) * (i + 0.5) / leaves)
        bx, by = cx + r * math.cos(t), cy + r * math.sin(t)
        tx, ty = -math.sin(t) * side, math.cos(t) * side
        for out in (1, -1):
            nx, ny = math.cos(t) * out, math.sin(t) * out
            tipx, tipy = bx + nx * size + tx * size * 0.6, by + ny * size + ty * size * 0.6
            mx, my = (bx + tipx) / 2, (by + tipy) / 2
            qx, qy = -(tipy - by) * 0.28, (tipx - bx) * 0.28
            L.poly([(bx, by), (mx + qx, my + qy), (tipx, tipy), (mx - qx, my - qy)])


def column(L, x, y0, y1, w, broken=False, seed=0):
    if not broken:
        L.rect((x - w * 0.65, y0, x + w * 0.65, y0 + w * 0.35))
    L.rect((x - w * 0.55, y1 - w * 0.3, x + w * 0.55, y1))
    if broken:
        rng = np.random.RandomState(seed)
        top = y0 + w * 0.35 + (y1 - y0) * 0.35
        jag = [(x - w / 2, top + rng.uniform(-8, 8))]
        for i in range(1, 5):
            jag.append((x - w / 2 + w * i / 5, top + rng.uniform(-18, 6)))
        jag.append((x + w / 2, top + rng.uniform(-8, 8)))
        L.poly(jag + [(x + w / 2, y1 - w * 0.3), (x - w / 2, y1 - w * 0.3)])
    else:
        L.rect((x - w / 2, y0 + w * 0.35, x + w / 2, y1 - w * 0.3))


def fluting(L, x, y0, y1, w):
    for i in range(1, 4):
        fx = x - w / 2 + w * i / 4
        L.line([(fx, y0), (fx, y1)], 1.2)


# ---- The backgrounds ------------------------------------------------------------

def conquest():
    base = field((128, 40, 30), (34, 12, 10), 101)
    base = rays(base, (509, 187), rgb((255, 190, 120)), alpha=0.12)
    L = Layer(W, H)
    sword(L, (250, 470), (760, 30), 9, 34)
    sword(L, (768, 470), (258, 30), 9, 34)
    base = emboss(base, L.mask(), rgb((176, 150, 110)), 0.55)
    S = Layer(W, H)
    for cx in (130, 894):
        shield(S, cx, 200, 150, 190)
    sm = S.mask()
    base = emboss(base, sm, rgb((120, 70, 40)), 0.8)
    # On each shield a chevron under a star.
    B = Layer(W, H)
    for cx in (130, 894):
        B.poly([(cx - 75, 230), (cx, 170), (cx + 75, 230), (cx + 75, 256), (cx, 196), (cx - 75, 256)])
        star = []
        for i in range(10):
            a = math.radians(-90 + i * 36)
            r = 30 if i % 2 == 0 else 12
            star.append((cx + r * math.cos(a), 140 + r * math.sin(a)))
        B.poly(star)
    base = emboss(base, B.mask() * sm, rgb((214, 176, 96)), 0.85)
    # Spears leaning in the lower corners.
    P = Layer(W, H)
    for i in range(5):
        P.line([(40 + i * 26, 750), (110 + i * 30, 330)], 3)
        P.line([(984 - i * 26, 750), (914 - i * 30, 330)], 3)
    base = emboss(base, P.mask(), rgb((90, 60, 40)), 0.6)
    return base, VICTORY_PLACES, rgb((214, 176, 96))


def culture():
    base = field((128, 64, 112), (36, 16, 40), 202)
    base = rays(base, (509, 187), rgb((255, 210, 230)), alpha=0.1)
    L = Layer(W, H)
    # A temple's front either side of the top head.
    for x0 in (60, 700):
        L.poly([(x0, 70), (x0 + 132, 22), (x0 + 264, 70)])
        L.rect((x0, 70, x0 + 264, 84))
        for i in range(4):
            column(L, x0 + 33 + i * 66, 84, 330, 30)
        L.rect((x0 - 10, 330, x0 + 274, 346))
    base = emboss(base, L.mask(), rgb((214, 196, 200)), 0.55)
    F = Layer(W, H)
    for x0 in (60, 700):
        for i in range(4):
            fluting(F, x0 + 33 + i * 66, 100, 314, 30)
    base = over(base, (0.35, 0.25, 0.32), F.mask() * 0.35)
    # Hearts scattered over the lower part.
    rng = np.random.RandomState(5)
    Hh = Layer(W, H)
    for _ in range(40):
        x, y = rng.uniform(40, 984), rng.uniform(360, 730)
        heart(Hh, x, y, rng.uniform(8, 18))
    base = emboss(base, Hh.mask(), rgb((210, 90, 130)), 0.35)
    return base, VICTORY_PLACES, rgb((236, 180, 200))


def diplomatic():
    base = field((40, 70, 120), (10, 18, 40), 303)
    base = rays(base, (509, 187), rgb((210, 230, 255)), alpha=0.1)
    L = Layer(W, H)
    leaf_branch(L, 509, 187, 165, 110, 250, 9, 22, 1)
    leaf_branch(L, 509, 187, 165, 70, -70, 9, 22, -1)
    base = emboss(base, L.mask(), rgb((150, 170, 110)), 0.75)
    # Treaties with their seals, either side.
    P = Layer(W, H)
    for x0 in (70, 790):
        P.rect((x0, 90, x0 + 164, 300), radius=4)
    pm = P.mask()
    base = emboss(base, pm, rgb((226, 216, 186)), 0.7)
    T = Layer(W, H)
    for x0 in (70, 790):
        for i in range(9):
            T.line([(x0 + 18, 116 + i * 18), (x0 + 146 - (30 if i == 8 else 0), 116 + i * 18)], 2)
    base = over(base, (0.45, 0.42, 0.36), T.mask() * pm * 0.6)
    Sl = Layer(W, H)
    for x0 in (70, 790):
        Sl.ellipse((x0 + 110, 262, x0 + 160, 312))
    base = emboss(base, Sl.mask(), rgb((170, 40, 40)), 0.95)
    # Faint flags along the bottom.
    Fl = Layer(W, H)
    for i in range(10):
        x = 60 + i * 100
        Fl.line([(x, 740), (x, 700)], 2)
        Fl.poly([(x, 700), (x + 34, 706), (x, 716)])
    base = emboss(base, Fl.mask(), rgb((180, 190, 210)), 0.35)
    return base, VICTORY_PLACES, rgb((190, 205, 230))


def space():
    base = field((26, 30, 66), (2, 3, 10), 404, mottle=0.12)
    xs, ys = grid(W, H)
    neb = np.clip(noise(H * SCALE, W * SCALE, 220 * SCALE, 405) * 0.8 + 0.2, 0, 1)
    base = over(base, (0.42, 0.22, 0.55), neb * 0.18)
    rng = np.random.RandomState(9)
    St = Layer(W, H)
    for _ in range(420):
        x, y, r = rng.uniform(0, W), rng.uniform(0, H), rng.uniform(0.4, 1.6) ** 2
        St.ellipse((x - r, y - r, x + r, y + r))
    sm = St.mask()
    base = over(base, (1, 1, 0.95), sm + blurred(sm, 3) * 0.5)
    # The planet's horizon along the bottom, lit from the upper left.
    d = np.hypot(xs - 512, ys - 1700)
    planet = np.clip((1060 - d) * SCALE / 2, 0, 1)
    lit = np.clip(1 - np.hypot(xs - 300, ys - 700) / 900, 0, 1)
    pcol = np.stack([0.12 + 0.3 * lit, 0.25 + 0.35 * lit, 0.45 + 0.35 * lit], -1)
    base = over(base, pcol, planet)
    atmo = np.exp(-np.abs(d - 1060) / 8) * (ys > 500)
    base = over(base, (0.6, 0.85, 1.0), atmo * 0.6)
    # A ringed planet up at the left.
    R = Layer(W, H)
    R.ellipse((90, 70, 230, 210))
    base = emboss(base, R.mask(), rgb((200, 150, 100)), 1.0)
    Rg = Layer(W, H)
    Rg.d.ellipse([40 * Rg.k, 125 * Rg.k, 280 * Rg.k, 155 * Rg.k], outline=255, width=int(5 * Rg.k))
    base = over(base, (0.9, 0.8, 0.6), Rg.mask() * 0.8)
    # A rocket climbing at the right, its trail behind.
    Tr = Layer(W, H)
    Tr.line([(700, 340), (840, 150)], 14)
    base = over(base, (1, 0.8, 0.5), blurred(Tr.mask(), 10) * 0.5)
    Rk = Layer(W, H)
    cx, cy, a = 860, 122, math.radians(-54)
    ux, uy = math.cos(a), math.sin(a)
    px, py = -uy, ux
    body = [(cx + ux * 60 + px * 0, cy + uy * 60), (cx + ux * 30 + px * 14, cy + uy * 30 + py * 14),
            (cx - ux * 40 + px * 14, cy - uy * 40 + py * 14), (cx - ux * 40 - px * 14, cy - uy * 40 - py * 14),
            (cx + ux * 30 - px * 14, cy + uy * 30 - py * 14)]
    Rk.poly(body)
    for s in (1, -1):
        Rk.poly([(cx - ux * 20 + px * 14 * s, cy - uy * 20 + py * 14 * s), (cx - ux * 48 + px * 30 * s, cy - uy * 48 + py * 30 * s),
                 (cx - ux * 40 + px * 14 * s, cy - uy * 40 + py * 14 * s)])
    base = emboss(base, Rk.mask(), rgb((226, 228, 236)), 1.0)
    return base, VICTORY_PLACES, rgb((160, 200, 240))


def domination():
    base = field((150, 104, 40), (42, 26, 8), 505)
    base = rays(base, (509, 187), rgb((255, 230, 150)), count=36, alpha=0.16)
    xs, ys = grid(W, H)
    # A globe behind the top head, its lines of latitude and longitude.
    G = Layer(W, H)
    G.ellipse((319, 0, 699, 380))
    gm = G.mask()
    base = emboss(base, gm, rgb((176, 132, 60)), 0.6)
    Ln = Layer(W, H)
    for i in range(1, 6):
        e = 190 * math.sin(math.pi * i / 6) if False else 190 * abs(math.cos(math.pi * i / 6))
        Ln.d.ellipse([(509 - e) * Ln.k, 0, (509 + e) * Ln.k, 380 * Ln.k], outline=255, width=int(2 * Ln.k))
    for i in range(1, 6):
        y = 190 + 190 * math.cos(math.pi * i / 6)
        half = 190 * math.sin(math.pi * i / 6)
        Ln.line([(509 - half, y), (509 + half, y)], 2)
    base = over(base, (0.36, 0.24, 0.08), Ln.mask() * gm * 0.55)
    # Laurels either side below.
    L = Layer(W, H)
    leaf_branch(L, 509, 470, 470, 150, 205, 10, 18, 1)
    leaf_branch(L, 509, 470, 470, 30, -25, 10, 18, -1)
    base = emboss(base, L.mask(), rgb((210, 180, 90)), 0.55)
    # A crown at each upper corner.
    C = Layer(W, H)
    for cx in (120, 904):
        C.poly([(cx - 70, 190), (cx - 70, 110), (cx - 40, 150), (cx, 90), (cx + 40, 150), (cx + 70, 110), (cx + 70, 190)])
        C.rect((cx - 76, 190, cx + 76, 214), radius=4)
    base = emboss(base, C.mask(), rgb((232, 190, 80)), 0.9)
    J = Layer(W, H)
    for cx in (120, 904):
        for dx in (-40, 0, 40):
            J.ellipse((cx + dx - 7, 195, cx + dx + 7, 209))
    base = emboss(base, J.mask(), rgb((170, 30, 40)), 1.0)
    return base, VICTORY_PLACES, rgb((240, 210, 130))


def hourglass(L, cx, cy, w, h):
    L.rect((cx - w / 2 - 8, cy - h / 2 - 10, cx + w / 2 + 8, cy - h / 2), radius=3)
    L.rect((cx - w / 2 - 8, cy + h / 2, cx + w / 2 + 8, cy + h / 2 + 10), radius=3)
    L.poly([(cx - w / 2, cy - h / 2), (cx + w / 2, cy - h / 2), (cx + 6, cy), (cx + w / 2, cy + h / 2),
            (cx - w / 2, cy + h / 2), (cx - 6, cy)])


def time_limit():
    base = field((40, 96, 100), (8, 24, 28), 606)
    base = rays(base, (509, 187), rgb((210, 255, 240)), alpha=0.08)
    # A clock face behind the top head.
    C = Layer(W, H)
    C.ring((319, -3, 699, 377), 10)
    for i in range(12):
        a = math.radians(i * 30)
        r0, r1 = 160, 178 if i % 3 else 150
        C.line([(509 + r0 * math.cos(a), 187 + r0 * math.sin(a)), (509 + r1 * math.cos(a), 187 + r1 * math.sin(a))], 5)
    base = emboss(base, C.mask(), rgb((200, 210, 190)), 0.6)
    Hg = Layer(W, H)
    for cx in (130, 894):
        hourglass(Hg, cx, 200, 120, 220)
    base = emboss(base, Hg.mask(), rgb((210, 190, 140)), 0.7)
    Sd = Layer(W, H)
    for cx in (130, 894):
        Sd.poly([(cx - 30, 120), (cx + 30, 120), (cx + 4, 196), (cx - 4, 196)])
        Sd.poly([(cx, 210), (cx + 48, 300), (cx - 48, 300)])
    base = over(base, (0.85, 0.66, 0.36), Sd.mask() * 0.9)
    # Rising lines, as on the Histograph, across the lower part.
    rng = np.random.RandomState(3)
    colors = [(220, 90, 70), (90, 170, 220), (230, 200, 90), (140, 210, 120), (200, 120, 210)]
    for c in colors:
        Gl = Layer(W, H)
        y = rng.uniform(700, 740)
        pts = []
        for i in range(21):
            x = 30 + i * 48.2
            y -= rng.uniform(0, 26)
            pts.append((x, max(360, y)))
        Gl.line(pts, 3)
        base = over(base, rgb(c), Gl.mask() * 0.32)
    return base, VICTORY_PLACES, rgb((190, 230, 215))


def defeat():
    base = field((58, 64, 74), (10, 12, 16), 707, center=(509, 422), mottle=0.09)
    xs, ys = grid(W, H)
    # Cracks running out from the middle.
    rng = np.random.RandomState(13)
    Cr = Layer(W, H)
    for k in range(14):
        a = k / 14 * 2 * math.pi + rng.uniform(-0.15, 0.15)
        x, y = 509 + 150 * math.cos(a), 422 + 150 * math.sin(a)
        pts = [(x, y)]
        for _ in range(12):
            a += rng.uniform(-0.45, 0.45)
            step = rng.uniform(25, 45)
            x, y = x + step * math.cos(a), y + step * math.sin(a)
            pts.append((x, y))
        Cr.line(pts, rng.uniform(1.6, 3.2))
    cm = Cr.mask()
    base = over(base, (0.02, 0.02, 0.03), cm * 0.85)
    base = over(base, (0.6, 0.62, 0.66), np.clip(np.roll(np.roll(cm, 2, 0), 2, 1) - cm, 0, 1) * 0.25)
    # Broken columns in the corners.
    L = Layer(W, H)
    for i, x in enumerate((60, 964)):
        column(L, x, 40, 740, 56, broken=True, seed=i + 1)
    for i, x in enumerate((120, 904)):
        column(L, x, 520, 740, 40, broken=True, seed=i + 7)
    lm = L.mask()
    base = emboss(base, lm, rgb((120, 120, 124)), 0.75)
    # The columns' flutes and weathering.
    F = Layer(W, H)
    for x, w in ((60, 56), (964, 56), (120, 40), (904, 40)):
        for i in range(1, 4):
            F.line([(x - w / 2 + w * i / 4, 40), (x - w / 2 + w * i / 4, 740)], 2)
    base = over(base, (0.22, 0.22, 0.25), F.mask() * lm * 0.6)
    xs2, ys2 = grid(W, H)
    wear = np.clip(noise(H * SCALE, W * SCALE, 10 * SCALE, 708, 3), 0, 1)
    base = over(base, (0.3, 0.32, 0.3), wear * lm * 0.5)
    # A torn banner hanging at the top.
    Bn = Layer(W, H)
    Bn.poly([(330, 20), (690, 20), (690, 46), (640, 52), (600, 44), (560, 56), (510, 46), (470, 58), (430, 46), (380, 54), (330, 46)])
    base = emboss(base, Bn.mask(), rgb((90, 36, 36)), 0.8)
    # Rain.
    Rn = Layer(W, H)
    for _ in range(260):
        x, y, n = rng.uniform(-100, W), rng.uniform(0, H), rng.uniform(14, 34)
        Rn.line([(x, y), (x + n * 0.35, y + n)], 1)
    base = over(base, (0.7, 0.75, 0.85), Rn.mask() * 0.18)
    base = glow(base, (509, 422), 380, np.array([0.30, 0.32, 0.38]), 0.25)
    return base, DEFEAT_PLACES, rgb((150, 160, 180))


BACKGROUNDS = {
    'conquer_victory.png': conquest,
    'culture_victory.png': culture,
    'diplomatic_victory.png': diplomatic,
    'space_victory.png': space,
    'domination_victory.png': domination,
    'histograph_victory.png': time_limit,
    'defeat.png': defeat,
}


def build_background(make):
    base, places, accent = make()
    xs, ys = grid(W, H)
    # A vignette, so the edges recede.
    v = np.clip(np.hypot((xs - 512) / 560, (ys - 384) / 430) - 0.75, 0, 1)
    base = base * (1 - 0.5 * v[..., None])
    base = niches(base, places)
    base = stone_frame(base, accent)
    return to_image(base)


# ---- The frames ---------------------------------------------------------------

# Each style: the frame's light and dark colors, and its inner rim.
FRAME_STYLES = {
    'glory': ((248, 214, 120), (130, 90, 30), (70, 46, 14)),
    'love': ((246, 180, 196), (150, 70, 96), (90, 30, 50)),
    'angry': ((150, 140, 140), (46, 40, 42), (170, 30, 24)),
    'hurt': ((168, 166, 160), (70, 70, 72), (40, 40, 42)),
}


def frame(w, h, style):
    light, dark, rim = (rgb(c) for c in FRAME_STYLES[style])
    t = 10 if w >= 200 else 7
    xs, ys = grid(w, h)
    near = np.minimum(np.minimum(xs, ys), np.minimum(w - xs, h - ys))
    ring = np.clip((t - near) * SCALE, 0, 1)
    # The frame's face: a rounded profile across its width, lit from the
    # upper left.
    u = np.clip(near / t, 0, 1)
    profile = np.clip(np.sin(u * math.pi), 0, 1) ** 0.7
    upper = np.minimum(xs, ys) < np.minimum(w - xs, h - ys)
    lit = np.where(upper, 0.75 - 0.5 * u, 0.25 + 0.5 * u)
    k = np.clip(0.35 + 0.65 * profile * lit + 0.15 * profile, 0, 1)
    col = dark[None, None, :] * (1 - k[..., None]) + light[None, None, :] * k[..., None]
    n = noise(h * SCALE, w * SCALE, 8 * SCALE, 31, 2) * 0.05
    col = np.clip(col * (1 + n[..., None]), 0, 1)
    # Thin dark lines at its outer and inner edges.
    col = over(col, (0.08, 0.06, 0.04), np.exp(-near * SCALE / 1.2) * 0.8)
    col = over(col, rim, np.exp(-np.abs(near - t) * SCALE / 1.4) * 0.9 * (near < t + 0.5))
    # Inside the frame: a soft inner shadow over the portrait's edges.
    inner = np.clip((near - t) * SCALE, 0, 1)
    shade = np.exp(-(near - t) / 4) * inner * 0.55
    alpha = ring + shade * (1 - ring)
    col = np.where((ring > 0)[..., None], col, 0)
    col = col * ring[..., None] / np.maximum(alpha, 1e-6)[..., None]

    # The mood's emblem.
    L = Layer(w, h)
    big = w >= 200
    s = 1.0 if big else 0.62
    if style == 'glory':
        # A star on the bottom of the frame.
        star = []
        for i in range(10):
            a = math.radians(-90 + i * 36)
            r = (11 if i % 2 == 0 else 4.6) * s
            star.append((w / 2 + r * math.cos(a), h - t / 2 - 1 * s + r * math.sin(a)))
        L.poly(star)
        ecol = rgb((255, 226, 120))
    elif style == 'love':
        heart(L, w / 2, h - 8 * s, 14 * s)
        ecol = rgb((214, 60, 96))
    elif style == 'angry':
        for cx, cy in ((t / 2, t / 2), (w - t / 2, t / 2), (t / 2, h - t / 2), (w - t / 2, h - t / 2)):
            L.ellipse((cx - 3.2 * s, cy - 3.2 * s, cx + 3.2 * s, cy + 3.2 * s))
        ecol = rgb((190, 170, 160))
    else:
        # A strip of bandage across the upper right corner.
        L.poly([(w - 46 * s, 0), (w - 24 * s, 0), (w, 24 * s), (w, 46 * s)])
        ecol = rgb((236, 228, 210))
    em = L.mask()
    col = emboss(col, em, ecol, 1.0)
    alpha = np.clip(alpha + em, 0, 1)
    if style == 'hurt':
        Cr = Layer(w, h)
        rng = np.random.RandomState(w)
        for x0, y0 in ((t * 0.5, h * 0.3), (w - t * 0.5, h * 0.62), (w * 0.4, h - t * 0.5)):
            pts = [(x0, y0)]
            for _ in range(4):
                x0, y0 = x0 + rng.uniform(-4, 4) * s, y0 + rng.uniform(3, 7) * s
                pts.append((x0, y0))
            Cr.line(pts, 1.2)
        col = over(col, (0.05, 0.05, 0.05), Cr.mask() * ring * 0.9)
    return to_image(col, alpha)


def heart_badge():
    w, h = 64, 58
    L = Layer(w, h)
    heart(L, 32, 22, 30)
    m = L.mask()
    xs, ys = grid(w, h)
    hole = np.clip((16 - np.hypot(xs - 32, ys - 26)) * SCALE, 0, 1)
    col = np.zeros((h * SCALE, w * SCALE, 3), np.float32)
    col = emboss(col, m, rgb((208, 56, 92)), 1.0, relief=0.5)
    # A gilt rim round the window.
    rimm = np.clip((18.5 - np.hypot(xs - 32, ys - 26)) * SCALE, 0, 1) - hole
    col = over(col, rgb((240, 204, 110)), rimm)
    return to_image(col, np.clip(m - hole, 0, 1))


# ---- The bubble and buttons -----------------------------------------------------

BUBBLE_FILL = rgb((252, 248, 236))
BUBBLE_LINE = rgb((92, 70, 48))


def bubble_part(h, top=False, bottom=False):
    w = 200
    xs, ys = grid(w, h)
    r = 10
    # The outline: a rounded box whose top or bottom may be in this piece.
    y0 = 1 if top else -50
    y1 = 11 if bottom else h + 50
    cx = np.clip(xs, 1 + r, w - 1 - r)
    cy = np.clip(ys, y0 + r, y1 - r)
    d = np.hypot(xs - cx, ys - cy) - r
    box = np.clip((0.5 - d) * SCALE / 1.0, 0, 1)
    edge = np.clip((0.5 - np.abs(d + 1.2)) * SCALE / 1.4 + 0.3, 0, 1) * (d > -2.4)
    if bottom:
        # The tail, from the box's bottom down to a point.
        L = Layer(w, h)
        L.poly([(84, 8), (112, 8), (96, 33)])
        tail = L.mask()
        Li = Layer(w, h)
        Li.poly([(87.5, 7), (108.5, 7), (96, 29.5)])
        tail_in = Li.mask()
        alpha = np.clip(box + tail, 0, 1)
        col = np.where((box > 0)[..., None], BUBBLE_FILL, BUBBLE_FILL)
        col = over(col, BUBBLE_LINE, edge * (1 - tail_in))
        col = over(col, BUBBLE_LINE, np.clip(tail - tail_in, 0, 1) * (ys > 10))
        col = over(col, BUBBLE_FILL, tail_in)
    else:
        alpha = box
        col = np.broadcast_to(BUBBLE_FILL, (h * SCALE, w * SCALE, 3)).copy()
        col = over(col, BUBBLE_LINE, edge)
    # A faint warm shade towards the bottom of the bubble.
    return to_image(col, alpha)


def exit_sheet():
    w, h = 112, 40
    out = np.zeros((h * SCALE, w * SCALE, 3), np.float32)
    alpha = np.zeros((h * SCALE, w * SCALE), np.float32)
    looks = [((150, 112, 60), (236, 220, 180)), ((214, 168, 80), (255, 248, 220)), ((110, 80, 40), (210, 196, 160))]
    for i, (plate, mark) in enumerate(looks):
        x = 1 + 37 * i
        L = Layer(w, h)
        L.rect((x + 1, 2, x + 35, 36), radius=7)
        m = L.mask()
        out = emboss(out, m, rgb(plate), 1.0, relief=0.5)
        X = Layer(w, h)
        X.line([(x + 11, 11), (x + 25, 27)], 4)
        X.line([(x + 25, 11), (x + 11, 27)], 4)
        out = emboss(out, X.mask() * m, rgb(mark), 1.0, relief=0.3)
        alpha = np.maximum(alpha, m)
    return to_image(out, alpha)


def next_sheet():
    w, h = 128, 72
    out = np.zeros((h * SCALE, w * SCALE, 3), np.float32)
    alpha = np.zeros((h * SCALE, w * SCALE), np.float32)
    for i, (plate, mark) in enumerate((((132, 100, 52), (240, 226, 186)), ((200, 156, 70), (255, 250, 226)))):
        y = 36 * i
        L = Layer(w, h)
        L.rect((2, y + 2, 126, y + 34), radius=9)
        m = L.mask()
        out = emboss(out, m, rgb(plate), 1.0, relief=0.45)
        R = Layer(w, h)
        R.rect((6, y + 6, 122, y + 30), radius=6)
        rm = R.mask()
        ring = np.clip(rm - np.roll(np.roll(rm, -1, 0), -1, 1), 0, 1) + np.clip(rm - np.roll(np.roll(rm, 1, 0), 1, 1), 0, 1)
        out = over(out, rgb((250, 230, 160)), ring * 0.5)
        C = Layer(w, h)
        C.poly([(102, y + 10), (114, y + 18), (102, y + 26), (98, y + 23), (106, y + 18), (98, y + 13)])
        out = emboss(out, C.mask(), rgb(mark), 1.0, relief=0.3)
        alpha = np.maximum(alpha, m)
    return to_image(out, alpha)


def build():
    for name, make in BACKGROUNDS.items():
        save(build_background(make), name)
    for style in FRAME_STYLES:
        save(frame(*LARGE, style), f'frame_large_{style}.png')
        save(frame(*SMALL, style), f'frame_small_{style}.png')
    save(heart_badge(), 'heart_badge.png')
    save(bubble_part(12, top=True), 'textbubble_top.png')
    save(bubble_part(16), 'textbubble_mid.png')
    save(bubble_part(35, bottom=True), 'textbubble_bot.png')
    save(exit_sheet(), 'exit.png')
    save(next_sheet(), 'next.png')


if __name__ == '__main__':
    if len(sys.argv) > 1:
        for name in sys.argv[1:]:
            save(build_background(BACKGROUNDS[name]), name)
    else:
        build()
