"""Unit order buttons for standalone mode that the asset pack lacks.

The standalone asset pack's Art/interface/NormButtons.png has blank buttons
where Civ3's has Build Outpost, Build Colony, Build Airfield, Build Radar
Tower, Airlift, Sentry, Sentry (enemy only), Load, Unload, Build Army, Hurry
Building, Increase Scientific Output, Sacrifice, Upgrade, Build Barricade and Join City. Each of these is drawn as a
file of its own, Art/interface/<order>.png in C7/ModernArt: the normal, hover
and pressed buttons side by side, each 64x64 (twice Civ3's 32x32 button, as
all the remade art is). C7/Lua/standalone/textures.lua points the orders'
buttons at them.

It also draws the air missions' and the airdrop's buttons (see
air_missions.py), which are blank in the asset pack too, and the icons by a
city for its barracks, airport and harbor (see city_icons.py), and the
science advisor's tech boxes the pack has no art for (see tech_boxes.py),
and the worker automation commands' buttons (see worker_automation.py).

The buttons copy the asset pack's look: a golden ball with the order drawn on
it in dark brown.
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common'))
import civ3art  # noqa: E402

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import air_missions  # noqa: E402
import city_icons  # noqa: E402
import tech_boxes  # noqa: E402

CELL = 64          # one button at 2x
SS = 4             # supersampling
S = CELL * SS

GLYPH = (58, 36, 14, 255)


def ball(brightness):
    """A golden ball lit from the upper left, as big as the cell."""
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    px = img.load()
    c = S / 2
    r = S / 2 - 2 * SS
    lx, ly = -0.45, -0.55
    for y in range(S):
        for x in range(S):
            dx, dy = (x + 0.5 - c) / r, (y + 0.5 - c) / r
            d2 = dx * dx + dy * dy
            if d2 > 1:
                continue
            dz = math.sqrt(1 - d2)
            light = max(0.0, -(dx * lx + dy * ly) + dz * 0.7)
            shade = 0.55 + 0.45 * light
            rim = 1 - 0.25 * max(0.0, math.sqrt(d2) - 0.85) / 0.15
            k = shade * rim * brightness
            px[x, y] = (min(255, int(214 * k)), min(255, int(160 * k)), min(255, int(62 * k)), 255)
    return img


def outpost_glyph(draw):
    """A watchtower on a mound: legs, a platform and a pointed roof."""
    u = S / 64
    draw.polygon([(14 * u, 50 * u), (50 * u, 50 * u), (44 * u, 45 * u), (20 * u, 45 * u)], fill=GLYPH)
    for x0, x1 in ((23, 26), (38, 41)):
        draw.polygon([(x0 * u, 46 * u), ((x0 + 3) * u, 46 * u), ((x1 + 1) * u, 26 * u), ((x1 - 2) * u, 26 * u)], fill=GLYPH)
    draw.line([(25 * u, 38 * u), (39 * u, 32 * u)], fill=GLYPH, width=int(2 * u))
    draw.line([(25 * u, 32 * u), (39 * u, 38 * u)], fill=GLYPH, width=int(2 * u))
    draw.rectangle([20 * u, 21 * u, 44 * u, 27 * u], fill=GLYPH)
    draw.polygon([(17 * u, 21 * u), (47 * u, 21 * u), (32 * u, 9 * u)], fill=GLYPH)


def colony_glyph(draw):
    """Two huts on a piece of land, with a crate of goods."""
    u = S / 64
    draw.polygon([(10 * u, 48 * u), (54 * u, 48 * u), (48 * u, 43 * u), (16 * u, 43 * u)], fill=GLYPH)
    # the big hut
    draw.rectangle([15 * u, 30 * u, 31 * u, 44 * u], fill=GLYPH)
    draw.polygon([(12 * u, 31 * u), (34 * u, 31 * u), (23 * u, 19 * u)], fill=GLYPH)
    # the small hut
    draw.rectangle([35 * u, 35 * u, 47 * u, 44 * u], fill=GLYPH)
    draw.polygon([(33 * u, 36 * u), (49 * u, 36 * u), (41 * u, 27 * u)], fill=GLYPH)
    # a crate
    draw.rectangle([44 * u, 39 * u, 52 * u, 46 * u], fill=GLYPH)


def airfield_glyph(draw):
    """A control tower beside a runway, with a plane above it, as Civ3's
    button has."""
    u = S / 64
    # the runway, seen at an angle
    draw.polygon([(8 * u, 50 * u), (46 * u, 50 * u), (56 * u, 42 * u), (18 * u, 42 * u)], fill=GLYPH)
    # the control tower: a shaft with a cab on top
    draw.rectangle([14 * u, 24 * u, 19 * u, 44 * u], fill=GLYPH)
    draw.polygon([(10 * u, 24 * u), (23 * u, 24 * u), (21 * u, 17 * u), (12 * u, 17 * u)], fill=GLYPH)
    # a plane: fuselage, wings and tail
    draw.polygon([(26 * u, 27 * u), (54 * u, 22 * u), (56 * u, 24 * u), (28 * u, 30 * u)], fill=GLYPH)
    draw.polygon([(38 * u, 25 * u), (44 * u, 24 * u), (36 * u, 12 * u), (33 * u, 13 * u)], fill=GLYPH)
    draw.polygon([(38 * u, 27 * u), (44 * u, 26 * u), (40 * u, 36 * u), (37 * u, 36 * u)], fill=GLYPH)
    draw.polygon([(26 * u, 27 * u), (29 * u, 27 * u), (27 * u, 20 * u), (25 * u, 20 * u)], fill=GLYPH)


def radar_tower_glyph(draw):
    """A radar dish on a lattice tower."""
    u = S / 64
    draw.polygon([(16 * u, 52 * u), (48 * u, 52 * u), (44 * u, 47 * u), (20 * u, 47 * u)], fill=GLYPH)
    # the tower's legs and braces
    for x0, x1 in ((24, 29), (40, 35)):
        draw.line([(x0 * u, 48 * u), (x1 * u, 28 * u)], fill=GLYPH, width=int(3 * u))
    for y0, y1 in ((46, 39), (39, 32)):
        draw.line([(25 * u, y0 * u), (38 * u, y1 * u)], fill=GLYPH, width=int(2 * u))
        draw.line([(39 * u, y0 * u), (26 * u, y1 * u)], fill=GLYPH, width=int(2 * u))
    # the dish, a crescent turned up to the sky (a disc with a smaller
    # one cut out of it toward the upper right), and its feed
    draw.ellipse([13 * u, 5 * u, 45 * u, 37 * u], fill=GLYPH)
    draw.ellipse([19 * u, 1 * u, 49 * u, 31 * u], fill=(0, 0, 0, 0))
    draw.line([(26 * u, 24 * u), (40 * u, 12 * u)], fill=GLYPH, width=int(2 * u))
    draw.ellipse([38 * u, 8 * u, 45 * u, 15 * u], fill=GLYPH)


def airlift_glyph(draw):
    """A cargo plane flying along a dashed arc from one runway to another."""
    u = S / 64
    # the two runways, at either end of the flight
    draw.polygon([(6 * u, 54 * u), (22 * u, 54 * u), (25 * u, 50 * u), (9 * u, 50 * u)], fill=GLYPH)
    draw.polygon([(40 * u, 54 * u), (56 * u, 54 * u), (59 * u, 50 * u), (43 * u, 50 * u)], fill=GLYPH)
    # the flight path, a dashed arc between them
    for a0 in range(200, 340, 24):
        draw.arc([12 * u, 24 * u, 52 * u, 76 * u], a0, a0 + 12, fill=GLYPH, width=int(2 * u))
    # the plane: a fat fuselage, broad straight wings and a tall tail
    draw.polygon([(16 * u, 26 * u), (46 * u, 20 * u), (52 * u, 22 * u), (47 * u, 26 * u), (18 * u, 31 * u)], fill=GLYPH)
    draw.polygon([(28 * u, 24 * u), (36 * u, 23 * u), (30 * u, 8 * u), (25 * u, 9 * u)], fill=GLYPH)
    draw.polygon([(29 * u, 28 * u), (37 * u, 26 * u), (34 * u, 40 * u), (29 * u, 40 * u)], fill=GLYPH)
    draw.polygon([(16 * u, 26 * u), (20 * u, 26 * u), (18 * u, 15 * u), (14 * u, 15 * u)], fill=GLYPH)


def upright_sword(draw, u, x=32):
    """A sword standing on its point, as on Civ3's Sentry button."""
    draw.polygon([((x - 3) * u, 22 * u), ((x + 3) * u, 22 * u), ((x + 3) * u, 48 * u), (x * u, 55 * u), ((x - 3) * u, 48 * u)], fill=GLYPH)
    draw.rectangle([(x - 10) * u, 18 * u, (x + 10) * u, 22 * u], fill=GLYPH)   # the guard
    draw.rectangle([(x - 2) * u, 9 * u, (x + 2) * u, 18 * u], fill=GLYPH)      # the grip
    draw.ellipse([(x - 4) * u, 5 * u, (x + 4) * u, 12 * u], fill=GLYPH)        # the pommel


def sentry_glyph(draw):
    """A sword standing guard."""
    upright_sword(draw, S / 64)


def sentry_enemy_only_glyph(draw):
    """A sword standing guard, with enemies closing in from either side."""
    u = S / 64
    upright_sword(draw, u)
    for sign in (-1, 1):
        tip = 32 + sign * 8
        tail = 32 + sign * 26
        draw.line([(tail * u, 38 * u), ((tip + sign * 6) * u, 38 * u)], fill=GLYPH, width=int(4 * u))
        draw.polygon([(tip * u, 38 * u), ((tip + sign * 8) * u, 31 * u), ((tip + sign * 8) * u, 45 * u)], fill=GLYPH)


def crate(draw, u):
    """An open-topped hold at the bottom of the button."""
    draw.polygon([(14 * u, 34 * u), (50 * u, 34 * u), (46 * u, 52 * u), (18 * u, 52 * u)], fill=GLYPH)
    draw.rectangle([18 * u, 34 * u, 46 * u, 38 * u], fill=(0, 0, 0, 0))


def load_glyph(draw):
    """An arrow curving down into a ship's hold."""
    u = S / 64
    crate(draw, u)
    draw.arc([10 * u, 8 * u, 40 * u, 44 * u], 200, 340, fill=GLYPH, width=int(5 * u))
    draw.line([(36 * u, 16 * u), (36 * u, 26 * u)], fill=GLYPH, width=int(5 * u))
    draw.polygon([(28 * u, 24 * u), (44 * u, 24 * u), (36 * u, 34 * u)], fill=GLYPH)


def unload_glyph(draw):
    """An arrow curving up out of a ship's hold."""
    u = S / 64
    crate(draw, u)
    draw.line([(28 * u, 40 * u), (28 * u, 22 * u)], fill=GLYPH, width=int(5 * u))
    draw.arc([28 * u, 8 * u, 56 * u, 40 * u], 200, 330, fill=GLYPH, width=int(5 * u))
    draw.polygon([(44 * u, 18 * u), (56 * u, 12 * u), (54 * u, 26 * u)], fill=GLYPH)


def build_army_glyph(draw):
    """Three soldiers in a rank, with a standard over them."""
    u = S / 64
    for x in (18, 32, 46):
        draw.ellipse([(x - 4) * u, 24 * u, (x + 4) * u, 32 * u], fill=GLYPH)
        draw.polygon([((x - 6) * u, 34 * u), ((x + 6) * u, 34 * u), ((x + 5) * u, 52 * u), ((x - 5) * u, 52 * u)], fill=GLYPH)
    draw.line([(32 * u, 24 * u), (32 * u, 8 * u)], fill=GLYPH, width=int(3 * u))
    draw.polygon([(33 * u, 8 * u), (46 * u, 12 * u), (33 * u, 17 * u)], fill=GLYPH)


def hurry_building_glyph(draw):
    """A brick wall going up, with a hammer over it."""
    u = S / 64
    for row, y in enumerate(range(36, 54, 6)):
        for x in range(10 + (row % 2) * 6, 52, 12):
            draw.rectangle([x * u, y * u, min(x + 10, 52) * u, (y + 4) * u], fill=GLYPH)
    # the hammer, its head up and to the right
    draw.line([(18 * u, 32 * u), (38 * u, 14 * u)], fill=GLYPH, width=int(4 * u))
    draw.polygon([(32 * u, 10 * u), (40 * u, 4 * u), (50 * u, 15 * u), (42 * u, 21 * u)], fill=GLYPH)


def science_age_glyph(draw):
    """A laboratory flask with bubbles rising from it, for Increase
    Scientific Output."""
    u = S / 64
    # the neck, then the body widening to a flat base
    draw.rectangle([27 * u, 14 * u, 37 * u, 28 * u], fill=GLYPH)
    draw.rectangle([24 * u, 12 * u, 40 * u, 16 * u], fill=GLYPH)
    draw.polygon([(27 * u, 27 * u), (37 * u, 27 * u), (52 * u, 52 * u), (12 * u, 52 * u)], fill=GLYPH)
    for x, y, r in ((30, 8, 2), (38, 4, 2.5), (24, 3, 1.5)):
        draw.ellipse([(x - r) * u, (y - r) * u, (x + r) * u, (y + r) * u], fill=GLYPH)


def join_city_glyph(draw):
    """A settler with an arrow pointing down into a town, as Civ3's Join
    City button has."""
    u = S / 64
    # the settler: a head and a body
    draw.ellipse([15 * u, 6 * u, 23 * u, 14 * u], fill=GLYPH)
    draw.polygon([(13 * u, 30 * u), (25 * u, 30 * u), (23 * u, 16 * u), (15 * u, 16 * u)], fill=GLYPH)
    # the arrow, down to the right into the town
    draw.line([(30 * u, 10 * u), (30 * u, 26 * u)], fill=GLYPH, width=int(4 * u))
    draw.polygon([(24 * u, 24 * u), (36 * u, 24 * u), (30 * u, 32 * u)], fill=GLYPH)
    # the town: two houses on a piece of land
    draw.polygon([(8 * u, 54 * u), (56 * u, 54 * u), (50 * u, 49 * u), (14 * u, 49 * u)], fill=GLYPH)
    draw.rectangle([16 * u, 38 * u, 30 * u, 50 * u], fill=GLYPH)
    draw.polygon([(13 * u, 39 * u), (33 * u, 39 * u), (23 * u, 30 * u)], fill=GLYPH)
    draw.rectangle([34 * u, 34 * u, 48 * u, 50 * u], fill=GLYPH)
    draw.polygon([(31 * u, 35 * u), (51 * u, 35 * u), (41 * u, 25 * u)], fill=GLYPH)
def sacrifice_glyph(draw):
    """A stepped temple with a flame on the altar at its top, for
    Sacrifice."""
    u = S / 64
    for i, (left, right) in enumerate(((8, 56), (14, 50), (20, 44), (26, 38))):
        top = 54 - 8 * (i + 1)
        draw.rectangle([left * u, top * u, right * u, (top + 7) * u], fill=GLYPH)
    # the flame
    draw.polygon([(32 * u, 4 * u), (40 * u, 16 * u), (38 * u, 22 * u), (26 * u, 22 * u),
                  (24 * u, 16 * u), (29 * u, 12 * u)], fill=GLYPH)


def upgrade_glyph(draw):
    """A bolt of lightning, as on Civ3's Upgrade button."""
    u = S / 64
    draw.polygon([(36 * u, 6 * u), (18 * u, 34 * u), (30 * u, 34 * u), (24 * u, 58 * u),
                  (46 * u, 26 * u), (34 * u, 26 * u), (42 * u, 6 * u)], fill=GLYPH)


def barricade_glyph(draw):
    """A barricade of sharpened stakes crossed over a log."""
    u = S / 64
    draw.polygon([(8 * u, 50 * u), (56 * u, 50 * u), (50 * u, 46 * u), (14 * u, 46 * u)], fill=GLYPH)
    for x in (16, 32, 48):
        for sign in (-1, 1):
            draw.line([((x - sign * 9) * u, 48 * u), ((x + sign * 7) * u, 18 * u)], fill=GLYPH, width=int(4 * u))
            draw.polygon([((x + sign * 5) * u, 20 * u), ((x + sign * 10) * u, 12 * u), ((x + sign * 9) * u, 22 * u)], fill=GLYPH)
    draw.rectangle([8 * u, 32 * u, 56 * u, 37 * u], fill=GLYPH)


def button(glyph, brightness, offset):
    img = ball(brightness)
    layer = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    glyph(ImageDraw.Draw(layer))
    # A pressed button's picture sits a little lower.
    layer = layer.transform(layer.size, Image.AFFINE, (1, 0, 0, 0, 1, -offset * SS))
    # A faint light edge under the picture, as if pressed into the metal.
    edge = layer.split()[3].filter(ImageFilter.GaussianBlur(SS))
    light = Image.new('RGBA', (S, S), (255, 230, 160, 0))
    light.putalpha(edge.point(lambda a: a // 3))
    img.alpha_composite(light, (SS, SS))
    img.alpha_composite(layer)
    return img.resize((CELL, CELL), Image.LANCZOS)


def build(name, glyph):
    sheet = Image.new('RGBA', (CELL * 3, CELL), (0, 0, 0, 0))
    for i, (brightness, offset) in enumerate(((1.0, 0), (1.15, 0), (0.85, 1))):
        sheet.paste(button(glyph, brightness, offset), (CELL * i, 0))
    path = os.path.join(civ3art.MODERN_ART, 'Art', 'interface', name + '.png')
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sheet.save(path, optimize=True)
    print('wrote', path)


if __name__ == '__main__':
    build('unit_build_outpost', outpost_glyph)
    build('unit_build_colony', colony_glyph)
    build('unit_build_airfield', airfield_glyph)
    build('unit_build_radar_tower', radar_tower_glyph)
    build('unit_airlift', airlift_glyph)
    build('unit_sentry', sentry_glyph)
    build('unit_sentry_enemy_only', sentry_enemy_only_glyph)
    build('unit_load', load_glyph)
    build('unit_unload', unload_glyph)
    build('unit_build_army', build_army_glyph)
    build('unit_hurry_building', hurry_building_glyph)
    build('unit_science_age', science_age_glyph)
    build('unit_sacrifice', sacrifice_glyph)
    build('unit_upgrade', upgrade_glyph)
    build('unit_build_barricade', barricade_glyph)
    build('unit_join_city', join_city_glyph)
    air_missions.main()
    city_icons.main()
    tech_boxes.main()
    import worker_automation  # noqa: E402  (it uses this module's build)
    worker_automation.main()
