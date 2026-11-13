-- Base paths
local ROOT = "Art/"
local ADVISORS = "Art/Advisors/"
local ADVISOR_TAB = "Art/SmallHeads/advisor_tab.pcx"

local BUTTONS = "Art/buttonsFINAL.pcx"
local CHECKBOXES = "Art/3checkboxes-USE.pcx"
local EXIT_BOX = "Art/exitBox-backgroundStates.pcx"
local INTERFACE = "Art/interface/"
local X_O = "Art/X-o_ALLstates-sprite.pcx"
local SCIENCE_NAV = "Art/Tech Chooser/scienceNAV.pcx"
local TILEINFO = "Art/TileInfo.pcx"

local CITY_SCREEN = "Art/city screen/"
local CITY_BUTTONS = "Art/city screen/cityMgmtButtons.pcx"
local CITY_PRODUCTION = "Art/city screen/ProdButton.pcx"
local CITY_SCREEN_ICONS = "Art/city screen/CityIcons.pcx"

local CITY_ICONS = "Art/Cities/city icons.pcx"
local CITY_BARRACKS_ICON = "Art/Cities/barracks.pcx"
local CITY_AIRPORT_HARBOR_ICONS = "Art/Cities/airAndHarb.pcx"

local CREDITS = "Art/Credits/"
local PALACE = "Art/PalaceView/"
local SPACE_RACE = "Art/SpaceShip/"

local POPUP_BORDERS = "Art/popupborders.pcx"

local CURSORS = "Art/Cursor.pcx"

-- Texture definitions
local textures = {}

textures.advisors = {
  dialog_box = ADVISORS .. "dialogbox.pcx",
  science = {
    background = {
      ancient = ADVISORS .. "science_ancient.pcx",
      middle = ADVISORS .. "science_middle.pcx",
      industrial = ADVISORS .. "science_industrial_new.pcx",
      modern = ADVISORS .. "science_modern.pcx",
    },
    navigation = {
      button = {
        normal = {
          path = SCIENCE_NAV,
          crop_region = { 0, 1, 129, 33 },
        },
        hover = {
          path = SCIENCE_NAV,
          crop_region = { 0, 35, 129, 33 },
        },
        pressed = {
          path = SCIENCE_NAV,
          crop_region = { 0, 69, 129, 33 },
        },
      },
      arrow_previous = {
        path = SCIENCE_NAV,
        crop_region = { 0, 103, 44, 9 },
      },
      arrow_next = {
        path = SCIENCE_NAV,
        crop_region = { 46, 103, 44, 9 },
      },
    },
  },
  military = {
    background = ADVISORS .. "military.pcx",
    -- Whether we're at peace or war with a civ, in the intelligence list.
    status = {
      peace = {
        path = ADVISORS .. "militaryNAV.pcx",
        crop_region = { 1, 1, 11, 11 },
        shadows = false,
      },
      war = {
        path = ADVISORS .. "militaryNAV.pcx",
        crop_region = { 13, 1, 11, 11 },
        shadows = false,
      },
    },
  },
  domestic = {
    background = {
      path = ADVISORS .. "domestic.pcx",
      shadows = false,
    },
    button = {
      normal = {
        path = ADVISORS .. "domesticBUTTON.pcx",
        crop_region = { 1, 1, 145, 24 },
      },
      hover = {
        path = ADVISORS .. "domesticBUTTON.pcx",
        crop_region = { 1, 26, 145, 24 },
      },
      pressed = {
        path = ADVISORS .. "domesticBUTTON.pcx",
        crop_region = { 1, 52, 145, 24 },
      },
    },
    minus = {
      normal = {
        path = ADVISORS .. "domestic_plusminus.pcx",
        crop_region = { 0, 0, 13, 9 },
        shadows = false,
      },
      hover = {
        path = ADVISORS .. "domestic_plusminus.pcx",
        crop_region = { 12, 0, 13, 9 },
        shadows = false,
      },
      pressed = {
        path = ADVISORS .. "domestic_plusminus.pcx",
        crop_region = { 24, 0, 13, 9 },
        shadows = false,
      },
    },
    plus = {
      normal = {
        path = ADVISORS .. "domestic_plusminus.pcx",
        crop_region = { 0, 8, 13, 14 },
        shadows = false,
      },
      hover = {
        path = ADVISORS .. "domestic_plusminus.pcx",
        crop_region = { 12, 8, 13, 14 },
        shadows = false,
      },
      pressed = {
        path = ADVISORS .. "domestic_plusminus.pcx",
        crop_region = { 24, 8, 13, 14 },
        shadows = false,
      },
    },
  },
  trade = {
    background = ADVISORS .. "trade.pcx",
  },
  foreign = {
    background = ADVISORS .. "foreign.pcx",
    navigation = {
      treaties = {
        path = ADVISORS .. "foreignTAB.pcx",
        crop_region = { 1, 1, 223, 236 },
      },
      trades = {
        path = ADVISORS .. "foreignTAB.pcx",
        crop_region = { 225, 1, 223, 236 },
      },
      details = {
        path = ADVISORS .. "foreignTAB.pcx",
        crop_region = { 449, 1, 223, 236 },
      }
    }
  },
  culture = {
    background = ADVISORS .. "culture.pcx",
  }
}

-- The column of advisor faces on the left of every advisor screen. Each row of
-- advisor_tab.pcx is one advisor and each column one state of its button, in
-- 56 px cells holding 54 px faces.
textures.advisors.sidebar = {}
for row, advisor in ipairs({ "domestic", "trade", "military", "foreign", "culture", "science" }) do
  local function cell(col)
    return {
      path = ADVISOR_TAB,
      crop_region = { 1 + col * 56, 2 + (row - 1) * 56, 54, 54 },
      shadows = false,
    }
  end
  textures.advisors.sidebar[advisor] = {
    normal = cell(0),
    hover = cell(1),
    pressed = cell(2),
    active = cell(2),
  }
end

textures.screens = {
  -- Civ3's Wonders of the World (F7): the screen, and the box each wonder
  -- is shown in (370x200; see WondersView).
  wonders = {
    background = ADVISORS .. "wonders_background.pcx",
    box = ADVISORS .. "wondersBOX.pcx",
  },
  hall_of_fame = {
    background = ADVISORS .. "hall_o_fame_background.pcx",
  },
  -- Conquests' Campaign Record (Civ3 FUN_004b0ba0): its background, and
  -- its "Next" button's three states, 86x24 each, down campRec.pcx.
  campaign_record = {
    background = ADVISORS .. "CampRecBG.pcx",
    next = {
      normal = {
        path = INTERFACE .. "campRec.pcx",
        crop_region = { 0, 0, 86, 24 },
      },
      hover = {
        path = INTERFACE .. "campRec.pcx",
        crop_region = { 0, 24, 86, 24 },
      },
      pressed = {
        path = INTERFACE .. "campRec.pcx",
        crop_region = { 0, 48, 86, 24 },
      },
    },
  },
  standing = {
    victory_status = {
      background = ROOT .. "histograph-win5_final.pcx",
    },
    histogram = {
      background = ROOT .. "histograph-top5_final.pcx",
    }
  },
  space_race = {
    background = SPACE_RACE .. "SHIPbackground.pcx",
  },
  palace = {
    background = PALACE .. "bkgr.pcx",
  },
  demographics = {
    background = ADVISORS .. "demographics.pcx",
  },

}

textures.ui = {
  button = {
    inactive = {
      path = BUTTONS,
      crop_region = { 1, 1, 20, 20 },
      shadows = false,
    },
    hover = {
      path = BUTTONS,
      crop_region = { 22, 1, 20, 20 },
      shadows = false,
    },
    pressed = {
      path = BUTTONS,
      crop_region = { 43, 1, 20, 20 },
      shadows = false,
    },
  },
  checkbox = {
    inactive = {
      path = CHECKBOXES,
      crop_region = { 1, 1, 15, 15 },
      shadows = false,
    },
    hover = {
      path = CHECKBOXES,
      crop_region = { 17, 1, 15, 15 },
      shadows = false,
    },
    pressed = {
      path = CHECKBOXES,
      crop_region = { 33, 1, 15, 15 },
      shadows = false,
    },
  },
  confirm = {
    normal = {
      path = X_O,
      crop_region = { 1, 1, 19, 19 },
    },
    hover = {
      path = X_O,
      crop_region = { 37, 1, 19, 19 },
    },
    pressed = {
      path = X_O,
      crop_region = { 73, 1, 19, 19 },
    },
  },
  cancel = {
    normal = {
      path = X_O,
      crop_region = { 21, 1, 15, 19 },
    },
    hover = {
      path = X_O,
      crop_region = { 57, 1, 15, 19 },
    },
    pressed = {
      path = X_O,
      crop_region = { 93, 1, 15, 19 },
    },
  },
  console = {
    normal = {
      path = INTERFACE .. "consoleButtons.pcx",
      crop_region = { 1, 1, 16, 16 },
    },
    hover = {
      path = INTERFACE .. "consoleButtons.pcx",
      crop_region = { 17, 1, 16, 16 },
    },
    pressed = {
      path = INTERFACE .. "consoleButtons.pcx",
      crop_region = { 33, 1, 16, 16 },
    },
  },
  exit = {
    normal = {
      path = EXIT_BOX,
      crop_region = { 0, 0, 72, 48 },
    },
    hover = {
      path = EXIT_BOX,
      crop_region = { 72, 0, 72, 48 },
    },
    pressed = {
      path = EXIT_BOX,
      crop_region = { 144, 0, 72, 48 },
    },
  },
  cursor = {
      bombard_deny = {
          path = CURSORS,
          crop_region = { 166, 1, 32, 32 },
      },
      bombard = {
          path = CURSORS,
          crop_region = { 199, 1, 32, 32 },
      }
  }
}

textures.terrain = require "textures.terrain"

textures.resources = require "textures.resources"

textures.credits = {
  background = CREDITS .. "credits_background.pcx",
}

textures.icons = {
  science = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 34, 2, 30, 30 },
    shadows = false,
  },
  beaker = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 34, 2, 29, 29 },
  },
  good_gold = {
	  path = CITY_SCREEN_ICONS,
	  crop_region = { 64, 2, 29, 29 },
  },
  commerce = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 67, 1, 21, 30 },
  },
  wasted_gold = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 95, 2, 30, 30 },
  },
  good_shield = {
	  path = CITY_SCREEN_ICONS,
	  crop_region = { 129, 5, 22, 22 },
  },
  shield = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 133, 1, 16, 30 },
  },
  wasted_shield = {
	  path = CITY_SCREEN_ICONS,
	  crop_region = { 160, 5, 22, 22 },
  },
  full_food = {
	  path = CITY_SCREEN_ICONS,
	  crop_region = { 191, 7, 20, 20 },
  },
  food = {
	  path = CITY_SCREEN_ICONS,
	  crop_region = { 195, 1, 21, 30 },
  },
  -- The smaller yield icons Civ3 draws on the tiles a city works, cropped
  -- tight so they can be laid out edge to edge.
  map_shield = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 412, 11, 13, 14 },
  },
  map_commerce = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 443, 10, 15, 15 },
  },
  map_food = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 474, 10, 16, 15 },
  },
  eaten_food = {
	  path = CITY_SCREEN_ICONS,
	  crop_region = { 223, 7, 20, 20 },
  },
  empty_shield = {
	  path = CITY_SCREEN_ICONS,
	  crop_region = { 253, 5, 22, 22 },
  },
  empty_food = {
	  path = CITY_SCREEN_ICONS,
	  crop_region = { 284, 7, 20, 20 },
  },
  no_food = {
	  path = CITY_SCREEN_ICONS,
	  crop_region = { 315, 7, 20, 20 },
  },
  luxury = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 376, 2, 30, 30 },
    shadows = false,
  },
  happy_face = {
	  path = CITY_SCREEN_ICONS,
	  crop_region = { 373, 2, 29, 29 },
  },
  effect_smiley_face = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 373, 1, 30, 30 },
  },
  effect_shield = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 404, 1, 30, 30 },
  },
  effect_good_gold = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 435, 1, 30, 30 },
  },
  effect_beaker = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 497, 1, 30, 30 },
  },
  effect_wasted_gold = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 528, 1, 30, 30 },
  },
  content_face = {
	  path = CITY_SCREEN_ICONS,
	  crop_region = { 591, 2, 29, 29 },
  },
  -- The city screen's improvements list, as Civ3's: under each building's
  -- name its upkeep (a red coin a gold), its culture (a note a point) and
  -- the citizens it makes content (a face each).
  roster_upkeep = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 536, 10, 15, 15 },
  },
  roster_culture = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 567, 8, 14, 16 },
    -- (Its black is a shadow shade in Civ3's palette: kept solid.)
    shadows = false,
  },
  roster_content_face = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 599, 9, 13, 15 },
  },
  -- What the government's tile penalty (despotism's) takes from a tile,
  -- drawn on the city screen's map in red as Civ3 draws lost shields and
  -- commerce. (Civ3 has no red food; the map makes one.)
  penalty_shield = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 164, 10, 14, 16 },
  },
  penalty_commerce = {
    path = CITY_SCREEN_ICONS,
    crop_region = { 536, 10, 15, 15 },
  },
  treasury = {
	  path = CITY_SCREEN_ICONS,
	  crop_region = { 746, 2, 29, 29 },
  },
  plus = {
    path = ADVISORS .. "domestic_icons_aux.pcx",
    crop_region = { 75, 1, 22, 22 },
    shadows = false,
  },
  minus = {
    path = ADVISORS .. "domestic_icons_aux.pcx",
    crop_region = { 51, 1, 22, 22 },
    shadows = false,
  },
  maintenance = {
    path = ADVISORS .. "domestic_icons_aux.pcx",
    crop_region = { 1, 1, 19, 34 },
  },
  capital_star = {
    path = CITY_ICONS,
    crop_region = { 20, 1, 18, 18 },
  },
}

textures.city_screen = {
  background = "Art/city screen/background.pcx",
  buttons = {
    close = {
      normal = {
        path = CITY_BUTTONS,
        crop_region = { 155, 1, 38, 48 },
      },
      hover = {
        path = CITY_BUTTONS,
        crop_region = { 155, 50, 38, 48 },
      },
      pressed = {
        path = CITY_BUTTONS,
        crop_region = { 155, 99, 38, 48 },
      },
    },
    previous = {
      normal = {
        path = CITY_BUTTONS,
        crop_region = { 1, 1, 48, 48 },
      },
      hover = {
        path = CITY_BUTTONS,
        crop_region = { 1, 50, 48, 48 },
      },
      pressed = {
        path = CITY_BUTTONS,
        crop_region = { 1, 99, 48, 48 },
      },
    },
    next = {
      normal = {
        path = CITY_BUTTONS,
        crop_region = { 42, 1, 48, 48 },
      },
      hover = {
        path = CITY_BUTTONS,
        crop_region = { 42, 50, 48, 48 },
      },
      pressed = {
        path = CITY_BUTTONS,
        crop_region = { 42, 99, 48, 48 },
      },
    },
    production = {
      normal = {
        path = CITY_PRODUCTION,
        crop_region = { 1, 0, 114, 95 },
      },
      hover = {
        path = CITY_PRODUCTION,
        crop_region = { 116, 0, 115, 95 },
      },
      pressed = {
        path = CITY_PRODUCTION,
        crop_region = { 231, 0, 115, 95 },
      },
    },
    -- Civ3's draft button and Play the World's governor button, at the end
    -- of the production bar: normal, pressed and hovered side by side.
    draft = {
      normal = {
        path = CITY_SCREEN .. "draftButton.pcx",
        crop_region = { 1, 1, 19, 20 },
      },
      hover = {
        path = CITY_SCREEN .. "draftButton.pcx",
        crop_region = { 41, 1, 19, 20 },
      },
      pressed = {
        path = CITY_SCREEN .. "draftButton.pcx",
        crop_region = { 21, 1, 19, 20 },
      },
    },
    governor = {
      normal = {
        path = CITY_SCREEN .. "govern.pcx",
        crop_region = { 1, 0, 28, 28 },
      },
      hover = {
        path = CITY_SCREEN .. "govern.pcx",
        crop_region = { 59, 0, 28, 28 },
      },
      pressed = {
        path = CITY_SCREEN .. "govern.pcx",
        crop_region = { 30, 0, 28, 28 },
      },
    },
    -- The City View button, cut as Civ3 FUN_00418160 cuts it.
    view = {
      normal = {
        path = CITY_SCREEN .. "xandview.pcx",
        crop_region = { 0, 1, 72, 42 },
      },
      hover = {
        path = CITY_SCREEN .. "xandview.pcx",
        crop_region = { 72, 1, 72, 42 },
      },
      pressed = {
        path = CITY_SCREEN .. "xandview.pcx",
        crop_region = { 144, 1, 72, 42 },
      },
    },
  },
  production_queue = CITY_SCREEN .. "ProductionQueueBox.pcx",
}

-- The City View's exit button (Civ3 FUN_005551b0 cuts it; it's at
-- (972, 15) on Civ3's full screen views).
textures.city_view = {
  exit = {
    normal = {
      path = "Art/City View/City Interface/ExitX.pcx",
      crop_region = { 1, 1, 36, 38 },
    },
    hover = {
      path = "Art/City View/City Interface/ExitX.pcx",
      crop_region = { 42, 1, 36, 38 },
    },
    pressed = {
      path = "Art/City View/City Interface/ExitX.pcx",
      crop_region = { 83, 1, 36, 38 },
    },
  },
}

textures.world_setup = require "textures.world_setup"
textures.player_setup = require "textures.player_setup"

textures.diplomacy = {
  deal = "Art/Diplomacy/counter.pcx",
  offer = "Art/Diplomacy/talk_offer.pcx",
}

-- The Espionage screen (Play the World's Art/Espionage). The parts are where
-- the artist's notes on the sheet put them: the opponents' buttons, the
-- embassy and spy icons, the bars of the target list, and the mission boxes.
local ESPIONAGE = "Art/Espionage/"
local ESPIONAGE_PARTS = ESPIONAGE .. "X_ESPIONAGE PARTS.PCX"
local ESPIONAGE_PARTS2 = ESPIONAGE .. "X_ESPIONAGE PARTS2.PCX"
textures.espionage = {
  background = ESPIONAGE .. "X_ESPIONAGEbkg.pcx",
  civ = {
    normal = { path = ESPIONAGE_PARTS, crop_region = { 2, 0, 321, 48 } },
    selected = { path = ESPIONAGE_PARTS, crop_region = { 2, 56, 321, 48 } },
    hover = { path = ESPIONAGE_PARTS, crop_region = { 2, 336, 321, 48 } },
  },
  embassy = { path = ESPIONAGE_PARTS, crop_region = { 1, 121, 24, 23 } },
  spy = { path = ESPIONAGE_PARTS, crop_region = { 1, 158, 24, 23 } },
  bar = {
    normal = { path = ESPIONAGE_PARTS, crop_region = { 1, 195, 256, 24 } },
    selected = { path = ESPIONAGE_PARTS, crop_region = { 1, 230, 256, 24 } },
    hover = { path = ESPIONAGE_PARTS, crop_region = { 1, 265, 256, 24 } },
    disabled = { path = ESPIONAGE_PARTS, crop_region = { 1, 300, 256, 25 } },
  },
  box = {
    normal = { path = ESPIONAGE_PARTS, crop_region = { 291, 249, 24, 23 } },
    hover = { path = ESPIONAGE_PARTS, crop_region = { 291, 283, 24, 23 } },
    checked = { path = ESPIONAGE_PARTS2, crop_region = { 0, 0, 24, 23 } },
    checked_hover = { path = ESPIONAGE_PARTS2, crop_region = { 0, 25, 24, 23 } },
  },
}

textures.upper_left_navigation = {
  menu = {
    path = INTERFACE .. "menuButtons.pcx",
    alpha = INTERFACE .. "menuButtonsAlpha.pcx",
    crop_region = { 0, 1, 35, 29 },
  },
  civilopedia = {
    path = INTERFACE .. "menuButtons.pcx",
    alpha = INTERFACE .. "menuButtonsAlpha.pcx",
    crop_region = { 36, 1, 35, 29 },
  },
  advisor = {
    normal = {
      path = INTERFACE .. "menuButtons.pcx",
      alpha = INTERFACE .. "menuButtonsAlpha.pcx",
      crop_region = { 73, 1, 35, 29 },
    },
    hover = {
      path = INTERFACE .. "menuButtons.pcx",
      alpha = INTERFACE .. "menuButtonsAlpha.pcx",
      alpha_row_offset = 60,
      crop_region = { 73, 61, 35, 29 },
    },
    pressed = {
      path = INTERFACE .. "menuButtons.pcx",
      alpha = INTERFACE .. "menuButtonsAlpha.pcx",
      alpha_row_offset = 120,
      crop_region = { 73, 121, 35, 29 },
    },
  },
}

textures.lower_right_infobox = {
  box = {
    path = INTERFACE .. "box right color.pcx",
    alpha = INTERFACE .. "box right alpha.pcx",
  },
  next_turn = {
    off = {
      path = INTERFACE .. "nextturn states color.pcx",
      alpha = INTERFACE .. "nextturn states alpha.pcx",
      crop_region = { 0, 0, 47, 28 },
    },
    on = {
      path = INTERFACE .. "nextturn states color.pcx",
      alpha = INTERFACE .. "nextturn states alpha.pcx",
      crop_region = { 47, 0, 47, 28 },
    },
    blink = {
      path = INTERFACE .. "nextturn states color.pcx",
      alpha = INTERFACE .. "nextturn states alpha.pcx",
      crop_region = { 94, 0, 47, 28 },
    },
  },
}

textures.lower_left_infobox = {
    box = {
      path = INTERFACE .. "box left color.pcx",
      alpha = INTERFACE .. "box left alpha.pcx",
    },
}

textures.transport_infobox = {
    box = {
      path = INTERFACE .. "box trans color.pcx",
      alpha = INTERFACE .. "box trans alpha.pcx",
    },
}

textures.tileinfo = {
      box = {
        path = TILEINFO
      },
}

textures.popup_background = {
  top_left = {
    path = POPUP_BORDERS,
    crop_region = { 251, 1, 61, 44 },
  },
  top_center = {
    path = POPUP_BORDERS,
    crop_region = { 313, 1, 61, 44 },
  },
  top_right = {
    path = POPUP_BORDERS,
    crop_region = { 375, 1, 61, 44 },
  },
  middle_left = {
    path = POPUP_BORDERS,
    crop_region = { 251, 46, 61, 44 },
  },
  middle_center = {
    path = POPUP_BORDERS,
    crop_region = { 313, 46, 61, 44 },
  },
  middle_right = {
    path = POPUP_BORDERS,
    crop_region = { 375, 46, 61, 44 },
  },
  bottom_left = {
    path = POPUP_BORDERS,
    crop_region = { 251, 91, 61, 44 },
  },
  bottom_center = {
    path = POPUP_BORDERS,
    crop_region = { 313, 91, 61, 44 },
  },
  bottom_right = {
    path = POPUP_BORDERS,
    crop_region = { 375, 91, 61, 44 },
  },
}

textures.animations = {
  cursor = {
    path = "Art/Animations/Cursor/Cursor.flc",
  },
  disorder = {
    path = "Art/Animations/Disorder/DisorderDefault.flc",
  },
}

-- The icons by a city for its barracks, harbor and airport. They're
-- picked for the building, so art without them can show the building's icon.
local function city_building_icon(path, crop_region)
  return {
    extra_data = { path = path, crop_region = crop_region },
    map_object_to_sprite = function(self, building)
      -- Their white and dark purple edge are in the palette's shadow range, but Civ3 draws them as they are.
      return { path = self.extra_data.path, crop_region = self.extra_data.crop_region, shadows = false }
    end,
  }
end

textures.city_building_icons = {
  barracks = city_building_icon(CITY_BARRACKS_ICON, { 1, 1, 25, 17 }),
  airport = city_building_icon(CITY_AIRPORT_HARBOR_ICONS, { 1, 1, 25, 17 }),
  harbor = city_building_icon(CITY_AIRPORT_HARBOR_ICONS, { 27, 1, 25, 17 }),
}

textures.popheads = require "textures.popheads"
textures.cities = require "textures.cities"
textures.advisor_heads = require "textures.advisor_heads"
textures.ui.unit_control = require "textures.unit_control"
textures.terrain_improvements = require "textures.terrain_improvements"
textures.civ_colors = require "textures.civ_colors"
textures.unit_icons = require "textures.unit_icons"
textures.building_icons = require "textures.building_icons"
textures.tech_icons = require "textures.tech_icons"
textures.tech_boxes = require "textures.tech_boxes"
textures.leader_heads = require "textures.leader_heads"
textures.borders = require "textures.borders"

return textures
