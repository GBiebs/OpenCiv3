"""The city screen's draft and governor buttons, remade.

    python ArtGen/screens/build.py   (or city_buttons.py alone)

Civ3's draft button (Art/city screen/draftButton.pcx) and Play the World's
governor button (civ3PTW/Art/city screen/govern.pcx) sit at the end of the
city screen's production bar. Each sheet holds the button's three states
side by side, which the game crops (C7/Lua/civ3/textures.lua): the draft
button's 19x20 cells at (1,1), (21,1) and (41,1) are normal, pressed and
hovered; the governor's 28x28 cells at (1,0), (30,0) and (59,0) the same.
The remakes are drawn in those cells at twice the size, the rest of each
sheet transparent, into C7/ModernArt/Art/city screen/draftButton.png and
govern.png.

Our own drawings: a small framed plate with a conscript shouldering his
rifle, and one with the bust of an official wearing his chain of office.
A plate is muted when idle, warmer and brighter under the mouse, and cool
blue when pressed (as Civ3's city screen buttons light up).
"""
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from canvas import Canvas, SCALE  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT_DIR = os.path.join(REPO, 'C7', 'ModernArt', 'Art', 'city screen')

FRAME = (150, 128, 84)
FRAME_DARK = (58, 46, 26)

# The plate's colors by state: (top, bottom) of its gradient, and the
# figure's color.
DRAFT_STATES = {
    'normal': ((150, 164, 92), (96, 110, 52), (36, 40, 20)),
    'hover': ((206, 214, 122), (146, 158, 70), (40, 44, 18)),
    'pressed': ((120, 196, 220), (52, 128, 168), (16, 48, 70)),
}
GOVERNOR_STATES = {
    'normal': ((222, 186, 128), (160, 112, 64), (64, 34, 18)),
    'hover': ((246, 214, 150), (204, 146, 80), (92, 44, 20)),
    'pressed': ((126, 196, 226), (50, 126, 170), (18, 52, 78)),
}


def plate(cv, w, h, state_colors, pressed):
    """A square plate in a beveled frame. Returns the plate's mask."""
    top, bottom, _ = state_colors
    outer = cv.rect((0.3, 0.3, w - 0.3, h - 0.3), radius=1.6)
    cv.fill(outer, FRAME, light=0.5, outline=0.35, contour=FRAME_DARK, gloss=0.2)
    inset = 1.6 if w < 24 else 2.2
    inner = cv.rect((inset, inset, w - inset, h - inset), radius=0.8)
    t = np.clip((cv.ys - inset) / (h - 2 * inset), 0, 1)[..., None]
    grad = (np.array(top) * (1 - t) + np.array(bottom) * t) / 255
    # A soft glow from the upper left.
    glow = np.clip(1 - np.hypot(cv.xs - w * 0.3, cv.ys - h * 0.3) / (w * 0.75), 0, 1)[..., None] * 0.25
    grad = grad + (1 - grad) * glow
    cv.over((0.12, 0.09, 0.05), cv.grow(inner, 0.35))
    cv.over(grad, inner)
    # Pressed: a shadow inside the top and left, as if pushed in.
    if pressed:
        lip = inner * np.clip(1 - np.minimum(cv.xs - inset, cv.ys - inset) / 1.6, 0, 1)
        cv.over((0, 0, 0), lip * 0.4)
    else:
        lip = inner * np.clip(1 - np.minimum(w - inset - cv.xs, h - inset - cv.ys) / 1.2, 0, 1)
        cv.over((0, 0, 0), lip * 0.22)
    return inner


def conscript(cv, color, dx=0.0, dy=0.0):
    """A soldier in a helmet, rifle on his shoulder, seen from the side."""
    def p(x, y):
        return (x + dx, y + dy)
    body = cv.poly([p(7.6, 8.4), p(11.6, 8.2), p(12.6, 12.6), p(11.8, 15.0), p(12.2, 18.6),
                    p(10.6, 18.6), p(9.8, 15.4), p(9.0, 18.6), p(7.4, 18.6), p(7.8, 14.6), p(6.8, 12.0)])
    head = cv.ellipse((p(7.9, 4.6)[0], p(7.9, 4.6)[1], p(10.9, 7.8)[0], p(10.9, 7.8)[1]))
    helmet = cv.poly([p(7.0, 5.6), p(7.8, 3.4), p(9.6, 2.8), p(11.4, 3.6), p(12.2, 5.4)])
    rifle = cv.bar(p(6.4, 12.4), p(14.4, 1.6), 0.9)
    stock = cv.bar(p(6.0, 13.2), p(7.4, 11.0), 1.6)
    m = (body + head + helmet + rifle + stock).clip(0, 1)
    cv.paint(m, color)
    # A highlight on the front of the coat.
    cv.paint(cv.bar(p(11.2, 9.2), p(11.8, 12.4), 0.5) * m, (255, 255, 255), 0.18)


def official(cv, color, dx=0.0, dy=0.0):
    """The bust of an official: head, high collar, shoulders, a chain of
    office across his chest."""
    def p(x, y):
        return (x + dx, y + dy)
    shoulders = cv.poly([p(5.6, 25.6), p(6.4, 20.0), p(9.2, 17.6), p(12.0, 16.8), p(16.0, 16.8),
                         p(18.8, 17.6), p(21.6, 20.0), p(22.4, 25.6)])
    neck = cv.rect((p(12.2, 13.0)[0], p(12.2, 13.0)[1], p(15.8, 17.8)[0], p(15.8, 17.8)[1]))
    head = cv.ellipse((p(10.0, 5.0)[0], p(10.0, 5.0)[1], p(18.0, 14.6)[0], p(18.0, 14.6)[1]))
    hair = cv.poly([p(9.8, 9.4), p(10.4, 5.8), p(14.0, 4.2), p(17.6, 5.8), p(18.2, 9.4), p(17.2, 7.6), p(14.0, 6.6), p(10.8, 7.6)])
    m = (shoulders + neck + head).clip(0, 1)
    cv.paint(m, color)
    cv.paint(hair, color, 1.0)
    # The chain of office: a gold arc with a medallion.
    chain = cv.arc((p(9.6, 13.6)[0], p(9.6, 13.6)[1], p(18.4, 22.6)[0], p(18.4, 22.6)[1]), 20, 160, 0.8)
    cv.paint(chain * shoulders, (226, 190, 90), 0.95)
    cv.fill(cv.ellipse((p(12.9, 21.0)[0], p(12.9, 21.0)[1], p(15.1, 23.2)[0], p(15.1, 23.2)[1])), (232, 198, 96),
            light=0.5, outline=0.25, contour=(90, 64, 20))
    # Light on the near cheek.
    cv.paint(cv.ellipse((p(11.0, 7.6)[0], p(11.0, 7.6)[1], p(13.4, 11.6)[0], p(13.4, 11.6)[1])) * head, (255, 255, 255), 0.12)


def button(w, h, draw_figure, colors, state):
    cv = Canvas(w, h)
    plate(cv, w, h, colors, state == 'pressed')
    shift = 0.4 if state == 'pressed' else 0.0
    draw_figure(cv, colors[2], shift, shift)
    return cv.image()


def sheet(size, cells, w, h, draw_figure, states):
    out = Image.new('RGBA', (size[0] * SCALE, size[1] * SCALE), (0, 0, 0, 0))
    for (x, y), state in cells:
        out.alpha_composite(button(w, h, draw_figure, states[state], state), (x * SCALE, y * SCALE))
    return out


def build():
    os.makedirs(OUT_DIR, exist_ok=True)
    draft = sheet((61, 100), [((1, 1), 'normal'), ((21, 1), 'pressed'), ((41, 1), 'hover')],
                  19, 20, conscript, DRAFT_STATES)
    governor = sheet((200, 28), [((1, 0), 'normal'), ((30, 0), 'pressed'), ((59, 0), 'hover')],
                     28, 28, official, GOVERNOR_STATES)
    for name, img in (('draftButton.png', draft), ('govern.png', governor)):
        path = os.path.join(OUT_DIR, name)
        img.save(path, optimize=True)
        print('wrote', path)
    return draft, governor


if __name__ == '__main__':
    build()
