"""The Wonders of the World screen (F7), remade: its background and the box
each wonder is shown in.

    python ArtGen/screens/build.py   (or wonders.py alone)

Civ3's screen is Art/Advisors/wonders_background.pcx (1024x768: a frame
round a parchment page, a pale strip at the top for the title) with six
boxes on it, each Art/Advisors/wondersBOX.pcx (370x200: a framed card with
the wonder's name along the top, its picture on the left and what's known
of it on a panel to the right). The code (C7/UIElements/GameViews/
WondersView.cs) places its words and pictures at fixed spots on them, so the
remakes keep that layout, at twice the size, in C7/ModernArt/Art/Advisors/:

    the background's page   (58, 62) to (966, 712), the title strip above it
    a box's name            (16, 10) to (354, 38)
    a box's picture well    (14, 42) to (152, 186), the 128x128 picture at (19, 50)
    a box's text panel      (160, 42) to (356, 186)

The look is the one ArtGen's Civilopedia has (ArtGen/civilopedia/build.py,
whose helpers this uses): parchment set into a stone-colored frame that's
lit from the upper left.
"""
import importlib.util
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from canvas import SCALE  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT_DIR = os.path.join(REPO, 'C7', 'ModernArt', 'Art', 'Advisors')

_spec = importlib.util.spec_from_file_location('pedia_art', os.path.join(HERE, '..', 'civilopedia', 'build.py'))
pedia = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pedia)

PARCHMENT = pedia.PARCHMENT
PAPER = pedia.PAPER
FRAME = pedia.FRAME
FRAME_DARK = pedia.FRAME_DARK
GILT = np.array([196, 168, 108]) / 255

# The layout, in Civ3's pixels (x0, y0, x1, y1).
PAGE = (58, 62, 966, 712)
TITLE_STRIP = (168, 12, 856, 50)
BOX_FRAME = (3, 3, 367, 197)
BOX_NAME = (16, 10, 354, 38)
BOX_WELL = (14, 42, 152, 186)
BOX_PANEL = (160, 42, 356, 186)


def scaled(r):
    return tuple(v * SCALE for v in r)


def material(h, w, base, seed, mottle=0.035):
    """A color with the soft mottling and faint horizontal grain of the
    Civilopedia's frame."""
    mott = pedia.fbm(h, w, 90 * SCALE, seed, octaves=4)
    grain = pedia.fbm(h, w, 3 * SCALE, seed + 12, octaves=2, stretch=(14, 1))
    shade = 1 + mottle * mott + mottle * 0.5 * grain
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    shade *= 1.03 - 0.06 * (xs / w * 0.4 + ys / h * 0.6)
    return np.asarray(base)[None, None, :] * shade[..., None]


def to_image(rgb, alpha):
    rgba = np.dstack([np.clip(rgb, 0, 1), np.clip(alpha, 0, 1)[..., None]])
    rgba = (rgba * 255 + 0.5).astype(np.uint8)
    rgba[rgba[..., 3] == 0, :3] = 0
    return Image.fromarray(rgba, 'RGBA')


def background():
    w, h = 1024 * SCALE, 768 * SCALE
    shape = (h, w)
    rgb = material(h, w, FRAME, 11)
    whole = (0, 0, w, h)
    rgb = pedia.bevel(rgb, whole, 6.0 * SCALE, 0.4, raised=True)
    rgb = pedia.outline(rgb, whole, FRAME_DARK, 1.0)
    # A tooled line round the frame, as on a book's cover.
    inner = (12 * SCALE, 12 * SCALE, w - 12 * SCALE, h - 12 * SCALE)
    rgb = pedia.outline(rgb, inner, np.array([112, 104, 84]) / 255, 0.5, 4.0 * SCALE)
    rgb = pedia.outline(rgb, tuple(v + d for v, d in zip(inner, (2, 2, -2, -2))), np.array([212, 204, 182]) / 255, 0.5, 4.0 * SCALE)
    # The title strip, the page set into the frame.
    rgb = pedia.raised_plate(rgb, scaled(TITLE_STRIP), pedia.IVORY, radius=5.0 * SCALE, seed=1)
    rgb = pedia.sunken(rgb, scaled(PAGE), PARCHMENT, depth=1.6, seed=2, mottle=0.035)
    # A faint gilt rule a little inside the page's edge.
    x0, y0, x1, y1 = scaled(PAGE)
    rule = (x0 + 14 * SCALE, y0 + 14 * SCALE, x1 - 14 * SCALE, y1 - 14 * SCALE)
    rgb = pedia.outline(rgb, rule, GILT, 0.45, 6.0 * SCALE)
    return to_image(rgb, np.ones(shape, np.float32))


def box():
    w, h = 370 * SCALE, 200 * SCALE
    shape = (h, w)
    frame = scaled(BOX_FRAME)
    alpha = pedia.rect_mask(shape, frame, 6.0 * SCALE)
    # A soft shadow down and to the right, on the page under the box.
    shadow = pedia.blur(pedia.rect_mask(shape, tuple(v + 2 * SCALE for v in frame), 6.0 * SCALE), 2.0 * SCALE)
    rgb = material(h, w, FRAME, 31)
    rgb = pedia.bevel(rgb, frame, 3.0 * SCALE, 0.45, raised=True, radius=6.0 * SCALE)
    rgb = pedia.outline(rgb, frame, FRAME_DARK, 1.0, 6.0 * SCALE)
    x0, y0, x1, y1 = frame
    inner = (x0 + 5 * SCALE, y0 + 5 * SCALE, x1 - 5 * SCALE, y1 - 5 * SCALE)
    rgb = pedia.outline(rgb, inner, GILT * 0.8, 0.7, 3.0 * SCALE)
    # The card inside the frame, the name written along its top.
    card = (x0 + 7 * SCALE, y0 + 7 * SCALE, x1 - 7 * SCALE, y1 - 7 * SCALE)
    rgb = pedia.sunken(rgb, card, PARCHMENT, depth=1.0, seed=3, mottle=0.03)
    # The picture's niche: darker, lit in the middle like the Civilopedia's.
    rgb = pedia.sunken(rgb, scaled(BOX_WELL), np.array([150, 136, 108]) / 255, depth=1.0, seed=4, mottle=0.03)
    wx0, wy0, wx1, wy1 = scaled(BOX_WELL)
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.hypot((xs - (wx0 + wx1) / 2 + 10 * SCALE) / (90 * SCALE), (ys - (wy0 + wy1) / 2 + 10 * SCALE) / (90 * SCALE))
    glow = np.clip(1 - d, 0, 1) ** 1.5 * 0.4 * pedia.rect_mask(shape, (wx0 + 2, wy0 + 2, wx1 - 2, wy1 - 2))
    rgb = rgb + (1 - rgb) * glow[..., None] * 0.6
    # The panel the owner, date and city are written on.
    rgb = pedia.sunken(rgb, scaled(BOX_PANEL), PAPER, depth=0.7, seed=5, mottle=0.02)
    # An engraved rule under the name.
    rgb = pedia.engraved_line(rgb, 39 * SCALE - 1, BOX_WELL[0] * SCALE, BOX_PANEL[2] * SCALE)
    out = np.where(alpha[..., None] > 0, rgb, 0)
    total = alpha + shadow * 0.45 * (1 - alpha)
    color = (out * alpha[..., None]) / np.maximum(total, 1e-6)[..., None]
    return to_image(color, total)


def build():
    os.makedirs(OUT_DIR, exist_ok=True)
    out = []
    for name, make in (('wonders_background.png', background), ('wondersBOX.png', box)):
        img = make()
        path = os.path.join(OUT_DIR, name)
        img.save(path, optimize=True)
        print('wrote', path)
        out.append(img)
    return out


if __name__ == '__main__':
    build()
