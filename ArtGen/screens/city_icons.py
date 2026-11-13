"""The city screen's icons: Art/city screen/CityIcons.pcx, remade.

    python ArtGen/screens/build.py   (or city_icons.py alone)

Civ3's sheet is a strip of 25 cells, 31 pixels apart, each holding one icon
that the game crops by rectangle (C7/Lua/civ3/textures.lua): the commerce,
shield and food icons of the city screen's boxes and bars, their smaller
cousins drawn on the tiles a city works, the citizens' faces, the culture
note, the treasury and so on. The remake draws each icon in its original's
cell, within the original's footprint, at twice the resolution, so every
crop finds the same picture. They're our own drawings, not copies: a goblet
for luxuries, a flask for science, a stack of coins with one standing in
front, a heater shield, a slice of bread, round faces, a quaver, a crate of
grain and a chest of gold.

It's used with the Modern graphics option (and in standalone mode with it),
so the city screen's improvement list has its upkeep coins, culture notes
and faces in the remade style.
"""
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from canvas import Canvas, SCALE  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(REPO, 'C7', 'ModernArt', 'Art', 'city screen', 'CityIcons.png')

CELL = 31
SIZE = (776, 32)   # Civ3's sheet

GOLD = (222, 178, 70)
GOLD_EDGE = (150, 104, 34)
RED_COIN = (196, 70, 52)
RED_EDGE = (120, 34, 26)
BLUE = (70, 120, 196)
RED = (182, 52, 44)
CRUST = (176, 116, 56)
CRUMB = (236, 206, 148)
PALE = (226, 218, 196)
PALE_EDGE = (150, 140, 118)
FACE = (240, 204, 74)
CONTENT = (228, 160, 72)
INK = (24, 20, 16)


def pale(color, k=0.55):
    """A washed-out version of a color, for the empty and spare icons."""
    return tuple(int(c + (238 - c) * k) for c in color)


# ---- The shapes (cell coordinates, Civ3 pixels) ----

def coin_stack(cv, x0, x1, bottom, count, face, edge):
    """A stack of coins seen from the side, `count` high, from `bottom` up."""
    h = 2.6
    for i in range(count):
        y = bottom - i * h
        rim = cv.ellipse((x0, y - 3.2, x1, y))
        cv.fill(rim, edge, light=0.2, outline=0.4)
        top = cv.ellipse((x0 + 0.4, y - 3.6, x1 - 0.4, y - 1.4))
        cv.fill(top, face, light=0.35, outline=0)
    return bottom - count * h


def standing_coin(cv, box, face, edge):
    x0, y0, x1, y1 = box
    disc = cv.ellipse(box)
    cv.fill(disc, edge, light=0.25, outline=0.45)
    inner = cv.ellipse((x0 + 1.3, y0 + 1.3, x1 - 1.3, y1 - 1.3))
    cv.fill(inner, face, light=0.45, outline=0, gloss=0.35)
    # A raised ring and a mark struck on the face.
    w = (x1 - x0)
    ring = cv.arc((x0 + 2.4, y0 + 2.4, x1 - 2.4, y1 - 2.4), 0, 360, 0.5)
    cv.paint(ring, edge, 0.6)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    s = w * 0.12
    cv.paint(cv.poly([(cx, cy - s * 1.6), (cx + s, cy), (cx, cy + s * 1.6), (cx - s, cy)]), edge, 0.75)


def coins(cv, box, face, edge, big=True):
    """A stack of coins with one standing in front of it, to its right."""
    x0, y0, x1, y1 = box
    w = x1 - x0
    stack_w = w * 0.55
    n = 6 if big else 4
    coin_stack(cv, x0, x0 + stack_w, y1 - (1.0 if big else 0.6), n, face, edge)
    d = min(w * 0.62, (y1 - y0) * 0.72)
    standing_coin(cv, (x1 - d, y1 - d, x1, y1), face, edge)


def coin_shadow(cv, box):
    x0, y0, x1, y1 = box
    cv.shadow(cv.rect((x0, y0 + (y1 - y0) * 0.25, x1, y1), radius=2))


def heater_shield(cv, box):
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    pts = [(x0, y0), (x1, y0), (x1, y0 + h * 0.45), (x1 - w * 0.12, y0 + h * 0.72),
           (x0 + w / 2, y1), (x0 + w * 0.12, y0 + h * 0.72), (x0, y0 + h * 0.45)]
    return cv.poly(pts)


def shield(cv, box, color, empty=False):
    m = heater_shield(cv, box)
    x0, y0, x1, y1 = box
    if empty:
        cv.fill(m, PALE, light=0.12, outline=0.4, contour=PALE_EDGE)
        return
    cv.shadow(m)
    cv.fill(m, color, light=0.45, outline=0.45, gloss=0.3)
    # A pale band down the middle, as if the shield were painted in two.
    w = x1 - x0
    band = cv.poly([(x0 + w * 0.42, y0 + 0.8), (x0 + w * 0.58, y0 + 0.8), (x0 + w * 0.56, y1 - 2.2), (x0 + w * 0.44, y1 - 2.2)])
    cv.paint(band * m, (255, 255, 255), 0.35)
    rim = cv.grow(m, 0) - heater_shield(cv, (x0 + 1, y0 + 1, x1 - 1, y1 - 1.2))
    cv.paint(rim.clip(0, 1), (255, 255, 255), 0.18)


def slice_of_bread(cv, box):
    """A slice of bread: a square body under a domed, two-lobed top."""
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    body = cv.rect((x0 + w * 0.06, y0 + h * 0.32, x1 - w * 0.06, y1), radius=1.6)
    left = cv.ellipse((x0, y0, x0 + w * 0.62, y0 + h * 0.62))
    right = cv.ellipse((x0 + w * 0.38, y0, x1, y0 + h * 0.62))
    return (body + left + right).clip(0, 1)


def bread(cv, box, crust=CRUST, crumb=CRUMB, shadow=True, contour=(70, 44, 20)):
    x0, y0, x1, y1 = box
    m = slice_of_bread(cv, box)
    if shadow:
        cv.shadow(m)
    cv.fill(m, crust, light=0.3, outline=0.4, contour=contour)
    inner = slice_of_bread(cv, (x0 + 1.4, y0 + 1.4, x1 - 1.4, y1 - 1.3))
    cv.fill(inner, crumb, light=0.3, outline=0, gloss=0.2)
    # A few holes in the crumb.
    for fx, fy, r in ((0.35, 0.55, 0.5), (0.62, 0.68, 0.45), (0.5, 0.4, 0.4), (0.7, 0.45, 0.35)):
        cx, cy = x0 + (x1 - x0) * fx, y0 + (y1 - y0) * fy
        cv.paint(cv.ellipse((cx - r, cy - r * 0.8, cx + r, cy + r * 0.8)), crust, 0.45)
    return m


def flask(cv, box, liquid, glass=(226, 232, 238)):
    """An Erlenmeyer flask, part filled."""
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    cx = (x0 + x1) / 2
    neck = w * 0.17
    pts = [(cx - neck, y0 + 1), (cx + neck, y0 + 1), (cx + neck, y0 + h * 0.36),
           (x1, y1 - 1.6), (x1 - 1.2, y1), (x0 + 1.2, y1), (x0, y1 - 1.6), (cx - neck, y0 + h * 0.36)]
    body = cv.poly(pts)
    lip = cv.rect((cx - neck - 0.9, y0, cx + neck + 0.9, y0 + 1.6), radius=0.6)
    m = (body + lip).clip(0, 1)
    cv.shadow(m)
    cv.fill(m, glass, light=0.2, outline=0.42, contour=(60, 66, 80))
    level = y0 + h * 0.6
    fluid = body * (cv.ys > level)
    cv.fill(fluid, liquid, light=0.4, outline=0)
    cv.paint(cv.ellipse((x0 + 2, level - 0.5, x1 - 2, level + 0.7)) * body, (255, 255, 255), 0.35)
    # The glass catching the light up its left side.
    cv.paint(cv.bar((cx - neck + 0.7, y0 + 2), (cx - neck + 0.7, y0 + h * 0.36), 0.5), (255, 255, 255), 0.7)
    cv.paint(cv.bar((cx - neck + 0.2, y0 + h * 0.42), (x0 + 2.2, y1 - 3), 0.6), (255, 255, 255), 0.6)


def face(cv, box, color, mood):
    x0, y0, x1, y1 = box
    m = cv.ellipse(box)
    cv.shadow(m, offset=(0.6, 0.8), radius=0.5, strength=0.3)
    cv.fill(m, color, light=0.45, outline=0.45, contour=(70, 44, 16), gloss=0.35)
    w, h = x1 - x0, y1 - y0
    ey = y0 + h * 0.38
    for ex in (x0 + w * 0.34, x0 + w * 0.66):
        cv.paint(cv.ellipse((ex - 0.75, ey - 1.0, ex + 0.75, ey + 1.0)), INK)
    if mood == 'happy':
        cv.paint(cv.arc((x0 + w * 0.24, y0 + h * 0.3, x1 - w * 0.24, y1 - h * 0.2), 20, 160, 0.9), INK)
    else:
        cv.paint(cv.bar((x0 + w * 0.32, y0 + h * 0.7), (x1 - w * 0.32, y0 + h * 0.7), 0.85), INK)


# ---- The cells ----

def glass(cv):
    """Luxuries: a goblet of wine."""
    bowl = cv.poly([(9, 3), (23, 3), (22.5, 7), (20, 11.5), (16, 13.2), (12, 11.5), (9.5, 7)])
    stem = cv.rect((15, 12.5, 17, 24), radius=0.4)
    foot = cv.ellipse((10, 23.2, 22, 27.6))
    m = (bowl + stem + foot).clip(0, 1)
    cv.shadow(m)
    cv.fill(m, (214, 222, 230), light=0.25, outline=0.42, contour=(60, 66, 80))
    wine = cv.poly([(10.0, 6.0), (22.0, 6.0), (20, 11), (16, 12.6), (12, 11)])
    cv.fill(wine, (150, 40, 64), light=0.4, outline=0)
    cv.paint(cv.ellipse((10, 5.2, 22, 6.8)), (196, 92, 112), 0.8)
    cv.paint(cv.bar((11.2, 4), (12.4, 9.5), 0.6), (255, 255, 255), 0.7)


def beaker(cv):
    flask(cv, (8, 3, 23.5, 30), (92, 130, 204))


def gold_coins(cv):
    coin_shadow(cv, (5, 5, 27, 26))
    coins(cv, (5, 5, 27, 26), GOLD, GOLD_EDGE)


def red_coins(cv):
    coin_shadow(cv, (4, 5, 26, 26))
    coins(cv, (4, 5, 26, 26), RED_COIN, RED_EDGE)


def blue_shield(cv):
    shield(cv, (9, 10, 22, 25.5), BLUE)


def red_shield(cv):
    shield(cv, (9, 10, 22, 25.5), RED)


def food(cv):
    bread(cv, (7, 9, 24, 25.5))


def eaten_food(cv):
    """Food eaten: a slice with bites out of its top."""
    box = (6, 9, 23, 25.5)
    bread(cv, box)
    for cx, cy, r in ((9, 12, 4.2), (14.5, 10.5, 4.4), (20, 12.5, 4.2)):
        cv.erase(cv.ellipse((cx - r, cy - r, cx + r, cy + r)))
    # The bitten edge shows the crumb.
    edge = sum(cv.arc((cx - r, cy - r, cx + r, cy + r), 0, 180, 0.9) for cx, cy, r in ((9, 12, 4.4), (14.5, 10.5, 4.6), (20, 12.5, 4.4)))
    cv.paint(edge.clip(0, 1) * (cv.a > 0.5), (120, 80, 40), 0.9)


def empty_shield(cv):
    shield(cv, (8, 10, 22, 25.5), None, empty=True)


def empty_slice(cv, box):
    """An empty place for food: the slice's outline, pale like the empty
    shield."""
    cv.fill(slice_of_bread(cv, box), PALE, light=0.12, outline=0.4, contour=PALE_EDGE)


def empty_food(cv):
    empty_slice(cv, (6, 9, 24, 25.5))


def no_food(cv):
    empty_slice(cv, (7, 9, 24, 25.5))
    ring = cv.arc((7.5, 8.5, 24.5, 25.5), 0, 360, 1.8)
    slash = cv.bar((10.4, 11.4), (21.6, 22.6), 1.8)
    cv.fill((ring + slash).clip(0, 1), (200, 40, 32), light=0.2, outline=0.3, contour=(90, 20, 16))
    ring = cv.arc((7.5, 8.5, 24.5, 25.5), 0, 360, 1.8)
    slash = cv.bar((10.4, 11.4), (21.6, 22.6), 1.8)
    cv.fill((ring + slash).clip(0, 1), (200, 40, 32), light=0.2, outline=0.3, contour=(90, 20, 16))


def pollution(cv):
    """A warning sign with a skull on it."""
    tri = cv.poly([(15, 3), (29.5, 28), (0.5, 28)])
    cv.shadow(tri)
    cv.fill(tri, (60, 50, 30), light=0.1, outline=0.3, contour=(30, 24, 12))
    inner = cv.poly([(15, 6.2), (26.6, 26.4), (3.4, 26.4)])
    cv.fill(inner, (236, 196, 60), light=0.4, outline=0, gloss=0.25)
    skull = cv.ellipse((11.2, 12.2, 18.8, 19.6)) + cv.rect((12.6, 18, 17.4, 22), radius=0.8)
    cv.paint(skull.clip(0, 1), INK)
    for ex in (13.3, 16.7):
        cv.paint(cv.ellipse((ex - 1.1, 15.0, ex + 1.1, 17.4)), (236, 196, 60))
    cv.paint(cv.bar((15, 18.2), (15, 19.2), 0.8), (236, 196, 60))
    for tx in (13.8, 15.0, 16.2):
        cv.paint(cv.bar((tx, 20.6), (tx, 22.2), 0.45), (236, 196, 60))


def happy(cv):
    face(cv, (9.6, 8.6, 22.6, 23.2), FACE, 'happy')


def small_shield(cv):
    shield(cv, (9, 11, 21.5, 24.5), BLUE)


def small_coins(cv):
    coin_shadow(cv, (9, 10, 23.5, 24.5))
    coins(cv, (9, 10, 23.5, 24.5), GOLD, GOLD_EDGE, big=False)


def small_food(cv):
    bread(cv, (9, 10, 24.5, 24.5))


def small_beaker(cv):
    flask(cv, (10.5, 6, 21.5, 24.5), (92, 130, 204))


def small_red_coins(cv):
    coin_shadow(cv, (9, 10, 23.5, 24.5))
    coins(cv, (9, 10, 23.5, 24.5), RED_COIN, RED_EDGE, big=False)


def culture_note(cv):
    """Culture: a quaver, in ink."""
    head = cv.poly([(9.2, 20.6), (11.2, 18.6), (14.6, 18.2), (15.6, 19.6), (14.4, 22.2), (11.2, 23.4), (9.4, 22.6)])
    stem = cv.rect((14.0, 8.0, 15.4, 20.4))
    flag = cv.poly([(14.0, 8.0), (15.6, 8.0), (18.6, 10.6), (21.8, 14.6), (21.4, 18.4), (20.2, 15.6), (17.6, 13.4), (15.4, 12.6)])
    cv.paint((head + stem + flag).clip(0, 1), INK)


def content(cv):
    face(cv, (9.6, 8.6, 22.6, 23.2), CONTENT, 'content')


def pale_shield(cv):
    shield(cv, (9, 11, 21.5, 24.5), pale(BLUE, 0.45))


def pale_coins(cv):
    coins(cv, (9, 10, 23.5, 24.5), pale(GOLD, 0.45), pale(GOLD_EDGE, 0.45), big=False)


def pale_food(cv):
    bread(cv, (9, 10, 24.5, 24.5), crust=pale(CRUST, 0.45), crumb=pale(CRUMB, 0.45), shadow=False)


def crate(cv):
    """Food kept: a wooden crate of grain."""
    box = cv.rect((3, 10, 28, 27), radius=1)
    cv.shadow(box)
    cv.fill(box, (166, 116, 64), light=0.35, outline=0.45, contour=(64, 40, 18))
    for y in (14.2, 18.4, 22.6):
        cv.paint(cv.bar((4, y), (27, y), 0.5), (100, 64, 30), 0.7)
    for x in (4.6, 26.4):
        cv.fill(cv.rect((x - 1.2, 10.5, x + 1.2, 26.5)), (136, 90, 44), light=0.2, outline=0.3, contour=(64, 40, 18))
    grain = cv.ellipse((5, 6, 26, 13)) * (cv.ys < 11)
    cv.fill(grain, (226, 190, 96), light=0.4, outline=0.35, contour=(120, 84, 30))
    for gx, gy in ((9, 8.8), (13, 7.6), (17, 8.2), (21, 8.9), (11, 10), (19, 10.1), (15, 9.6)):
        cv.paint(cv.ellipse((gx - 0.8, gy - 0.5, gx + 0.8, gy + 0.5)), (250, 226, 150), 0.8)


def chest(cv):
    """The treasury: an open chest of gold."""
    lid = cv.poly([(5, 11), (7, 3.5), (26, 3.5), (27, 11)])
    cv.shadow(lid)
    cv.fill(lid, (120, 78, 40), light=0.35, outline=0.45, contour=(56, 34, 14))
    cv.paint(cv.bar((6.5, 6.5), (26.5, 6.5), 0.9), (200, 170, 96), 0.9)
    gold = cv.ellipse((5, 9, 27, 17))
    cv.fill(gold, GOLD, light=0.5, outline=0.35, contour=GOLD_EDGE, gloss=0.4)
    for gx, gy in ((10, 11.6), (15, 10.8), (20, 11.8), (12.5, 13.6), (18, 13.4), (23, 13.2)):
        cv.paint(cv.ellipse((gx - 1.5, gy - 0.9, gx + 1.5, gy + 0.9)), GOLD_EDGE, 0.55)
    body = cv.rect((4, 14, 28, 29), radius=1)
    cv.shadow(body)
    cv.fill(body, (138, 90, 46), light=0.35, outline=0.45, contour=(56, 34, 14))
    for x in (6.5, 25.5):
        cv.fill(cv.rect((x - 1, 14, x + 1, 29)), (176, 148, 84), light=0.3, outline=0.25, contour=(80, 60, 24))
    cv.fill(cv.rect((14, 17, 18, 22), radius=0.6), (200, 170, 96), light=0.4, outline=0.3, contour=(80, 60, 24))
    cv.paint(cv.ellipse((15.4, 18.4, 16.6, 19.8)), INK, 0.8)


CELLS = [
    glass, beaker, gold_coins, red_coins, blue_shield, red_shield, food, eaten_food,
    empty_shield, empty_food, no_food, pollution, happy, small_shield, small_coins,
    small_food, small_beaker, small_red_coins, culture_note, content, pale_shield,
    pale_coins, pale_food, crate, chest,
]


def build():
    sheet = Image.new('RGBA', (SIZE[0] * SCALE, SIZE[1] * SCALE), (0, 0, 0, 0))
    for i, draw in enumerate(CELLS):
        cv = Canvas(CELL, SIZE[1])
        draw(cv)
        sheet.alpha_composite(cv.image(), (i * CELL * SCALE, 0))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.save(OUT, optimize=True)
    print('wrote', OUT)
    return sheet


if __name__ == '__main__':
    build()
