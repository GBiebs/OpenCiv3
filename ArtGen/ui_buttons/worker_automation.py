"""Buttons for the worker automation commands in standalone mode.

The standalone asset pack's NormButtons.png is blank where Civ3's has Road
to, Rail to, Build Trade, Irrigate to Nearest City, the three automated
clearings, This City Only, No Altering and No Altering/This City Only
(labels.txt's #UNIT_ACTIONS). Each is drawn as a file of its own, like the
other orders build.py draws: Art/interface/<order>.png in C7/ModernArt.

The automated kinds have a ring of dots around their picture, as Civ3's
automation buttons do.
"""
import math

import build

GLYPH = build.GLYPH
S = build.S


def dots_ring(draw, u, count=10, radius=24, size=2.6):
    """The ring of dots that marks an automated order."""
    for i in range(count):
        a = 2 * math.pi * i / count
        x, y = 32 + radius * math.cos(a), 32 + radius * math.sin(a)
        draw.ellipse([(x - size) * u, (y - size) * u, (x + size) * u, (y + size) * u], fill=GLYPH)


def arrow_up(draw, u, x=32, top=6):
    draw.polygon([((x - 8) * u, (top + 10) * u), ((x + 8) * u, (top + 10) * u), (x * u, top * u)], fill=GLYPH)


def road(draw, u, top=20):
    """A road running away into the distance."""
    draw.polygon([(18 * u, 56 * u), (25 * u, 56 * u), (30 * u, top * u), (28 * u, top * u)], fill=GLYPH)
    draw.polygon([(39 * u, 56 * u), (46 * u, 56 * u), (36 * u, top * u), (34 * u, top * u)], fill=GLYPH)


def road_to_glyph(draw):
    u = S / 64
    road(draw, u)
    arrow_up(draw, u)


def rail_to_glyph(draw):
    u = S / 64
    road(draw, u)
    for y, half in ((50, 15), (42, 12), (34, 9), (27, 6)):
        draw.rectangle([(32 - half) * u, y * u, (32 + half) * u, (y + 2) * u], fill=GLYPH)
    arrow_up(draw, u)


def house(draw, u, x, y, w=10):
    draw.rectangle([(x - w / 2) * u, y * u, (x + w / 2) * u, (y + w * 0.8) * u], fill=GLYPH)
    draw.polygon([((x - w / 2 - 2) * u, (y + 1) * u), ((x + w / 2 + 2) * u, (y + 1) * u), (x * u, (y - w * 0.6) * u)], fill=GLYPH)


def trade_network_glyph(draw):
    """Two towns joined by a road, in the ring."""
    u = S / 64
    dots_ring(draw, u)
    house(draw, u, 21, 24)
    house(draw, u, 43, 34)
    draw.line([(24 * u, 38 * u), (40 * u, 46 * u)], fill=GLYPH, width=int(3 * u))


def irrigate_to_city_glyph(draw):
    """Water running to a town, in the ring."""
    u = S / 64
    dots_ring(draw, u)
    house(draw, u, 40, 22)
    for y in (34, 42):
        draw.line([(16 * u, (y + 2) * u), (22 * u, (y - 2) * u), (28 * u, (y + 2) * u), (34 * u, (y - 2) * u), (40 * u, (y + 2) * u)],
                  fill=GLYPH, width=int(3 * u))


def clear_forest_glyph(draw):
    """A tree, in the ring."""
    u = S / 64
    dots_ring(draw, u)
    draw.polygon([(32 * u, 14 * u), (44 * u, 36 * u), (20 * u, 36 * u)], fill=GLYPH)
    draw.rectangle([29 * u, 36 * u, 35 * u, 46 * u], fill=GLYPH)


def clear_wetlands_glyph(draw):
    """Reeds over marsh water, in the ring."""
    u = S / 64
    dots_ring(draw, u)
    for x in (24, 32, 40):
        draw.line([(x * u, 36 * u), ((x - 2) * u, 18 * u)], fill=GLYPH, width=int(3 * u))
    for y in (40, 46):
        draw.line([(18 * u, y * u), (25 * u, (y - 3) * u), (32 * u, y * u), (39 * u, (y - 3) * u), (46 * u, y * u)], fill=GLYPH, width=int(3 * u))


def clear_damage_glyph(draw):
    """A cloud of smoke over the land, in the ring."""
    u = S / 64
    dots_ring(draw, u)
    for x, y, r in ((24, 30, 7), (33, 25, 8), (41, 31, 6), (32, 34, 7)):
        draw.ellipse([(x - r) * u, (y - r) * u, (x + r) * u, (y + r) * u], fill=GLYPH)
    draw.rectangle([18 * u, 43 * u, 46 * u, 46 * u], fill=GLYPH)


def this_city_glyph(draw):
    """One town, in the ring."""
    u = S / 64
    dots_ring(draw, u)
    house(draw, u, 32, 28, w=16)


def untouched_land(draw, u, y=36):
    """A diamond of land with nothing on it."""
    draw.polygon([(18 * u, y * u), (32 * u, (y - 7) * u), (46 * u, y * u), (32 * u, (y + 7) * u)], outline=GLYPH, width=int(3 * u))


def no_altering_glyph(draw):
    """Bare land, in the ring."""
    u = S / 64
    dots_ring(draw, u)
    untouched_land(draw, u, 32)


def city_no_altering_glyph(draw):
    """A town over bare land, in the ring."""
    u = S / 64
    dots_ring(draw, u)
    house(draw, u, 32, 18, w=10)
    untouched_land(draw, u, 39)


def main():
    build.build('unit_road_to', road_to_glyph)
    build.build('unit_rail_to', rail_to_glyph)
    build.build('unit_auto_trade_network', trade_network_glyph)
    build.build('unit_auto_irrigate_to_city', irrigate_to_city_glyph)
    build.build('unit_auto_clear_forest', clear_forest_glyph)
    build.build('unit_auto_clear_wetlands', clear_wetlands_glyph)
    build.build('unit_auto_clear_damage', clear_damage_glyph)
    build.build('unit_auto_city_only', this_city_glyph)
    build.build('unit_auto_no_altering', no_altering_glyph)
    build.build('unit_auto_city_no_altering', city_no_altering_glyph)


if __name__ == '__main__':
    main()
