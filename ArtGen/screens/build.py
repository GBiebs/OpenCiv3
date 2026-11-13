"""The remade art of the Wonders of the World screen, the city screen and
the end-of-game screen.

    python ArtGen/screens/build.py

Writes, into C7/ModernArt (each at twice the size of the Civ3 file it
remakes, in the same layout, so the code crops and places it as it does
Civ3's):

    Art/Advisors/wonders_background.png   the Wonders of the World screen  (wonders.py)
    Art/Advisors/wondersBOX.png           the box each wonder is shown in  (wonders.py)
    Art/city screen/CityIcons.png         the city screen's icons          (city_icons.py)
    Art/city screen/draftButton.png       the draft button                 (city_buttons.py)
    Art/city screen/govern.png            the governor button              (city_buttons.py)
    Art/VictoryScreen/*.png               the end-of-game screen's seven
                                          backgrounds, its bubble, frames
                                          and buttons                      (victory.py)

They're used with the Modern graphics option, and in standalone mode for
the files the standalone asset pack doesn't have (the box and the two
buttons), as ModernGraphics uses the remade art for anything the pack
lacks. Everything is drawn with code (no image of Civ3's is read or traced),
so it builds without Civ3 installed.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import city_buttons  # noqa: E402
import city_icons  # noqa: E402
import victory  # noqa: E402
import wonders  # noqa: E402

if __name__ == '__main__':
    wonders.build()
    city_icons.build()
    city_buttons.build()
    victory.build()
