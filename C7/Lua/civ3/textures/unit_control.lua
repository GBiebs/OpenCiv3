local INTERFACE = "Art/interface/"

local function make_entry(x, y)
  local entry = {
    normal = {
      path = INTERFACE .. "NormButtons.PCX",
      alpha = INTERFACE .. "ButtonAlpha.pcx",
      crop_region = { x * 32, y * 32, 32, 32 },
    },
    hover = {
      path = INTERFACE .. "rolloverbuttons.PCX",
      alpha = INTERFACE .. "ButtonAlpha.pcx",
      crop_region = { x * 32, y * 32, 32, 32 },
    },
    pressed = {
      path = INTERFACE .. "highlightedbuttons.PCX",
      alpha = INTERFACE .. "ButtonAlpha.pcx",
      crop_region = { x * 32, y * 32, 32, 32 },
    },
  }

  return entry
end

local unit_control = {
  unit_hold = make_entry(0, 0),
  unit_wait = make_entry(1, 0),
  unit_fortify = make_entry(2, 0),
  unit_disband = make_entry(3, 0),
  unit_goto = make_entry(4, 0),
  -- Go to City (Ctrl-Shift-G) has no button of its own in Civ3; it shares
  -- Go To's picture.
  unit_goto_city = make_entry(4, 0),
  unit_explore = make_entry(5, 0),
  unit_sentry = make_entry(6, 0),
  unit_sentry_enemy_only = make_entry(2, 5),
  unit_load = make_entry(7, 0),
  
  unit_unload = make_entry(0, 1),
  unit_airlift = make_entry(1, 1),
  unit_pillage = make_entry(2, 1),
  unit_bombard = make_entry(3, 1),
  unit_build_army = make_entry(5, 1),
  unit_hurry_building = make_entry(6, 1),
  unit_upgrade = make_entry(7, 1),
  -- labels.txt's "Increase Scientific Output", after Seize Treasure, an
  -- unused control and Sacrifice in #UNIT_ACTIONS.
  unit_science_age = make_entry(3, 2),
  -- labels.txt's "Sacrifice", just before it.
  unit_sacrifice = make_entry(2, 2),

  unit_build_city = make_entry(5, 2),
  -- UNVERIFIED: the cell is the one next to Build City whose picture looks
  -- like a colony (small buildings on open land); no source names it.
  unit_build_colony = make_entry(4, 2),
  unit_build_road = make_entry(6, 2),
  unit_build_railroad = make_entry(7, 2),

  unit_build_fortress = make_entry(0, 3),
  unit_build_mine = make_entry(1, 3),
  unit_irrigate = make_entry(2, 3),
  unit_clear_forest = make_entry(3, 3),
  unit_clear_wetlands = make_entry(4, 3),
  unit_plant_forest = make_entry(5, 3),
  unit_clear_damage = make_entry(6, 3),
  unit_automate = make_entry(7, 3),
  unit_join_city = make_entry(0, 4),
  unit_build_airfield = make_entry(1, 4),
  unit_build_radar_tower = make_entry(2, 4),
  unit_build_outpost = make_entry(3, 4),
  unit_build_barricade = make_entry(4, 4),
  -- The buttons are in the order of the unit actions in labels.txt
  -- (#UNIT_ACTIONS), as all of the above are.
  unit_airdrop = make_entry(4, 1),
  unit_bombing = make_entry(5, 4),
  unit_recon = make_entry(6, 4),
  unit_air_superiority = make_entry(7, 4),
  unit_rebase = make_entry(0, 5),
  unit_precision_bombing = make_entry(1, 5),
  -- labels.txt's "Automate: Bombard", "Automate: Bombing" and "Automate:
  -- Precision Bombing" (#UNIT_ACTIONS 43, 54 and 55).
  unit_auto_bombard = make_entry(3, 5),
  unit_auto_bombing = make_entry(6, 6),
  unit_auto_precision_bombing = make_entry(7, 6),
  -- The worker automation commands, likewise.
  unit_road_to = make_entry(5, 5),
  unit_rail_to = make_entry(6, 5),
  unit_auto_trade_network = make_entry(7, 5),
  unit_auto_irrigate_to_city = make_entry(0, 6),
  unit_auto_clear_forest = make_entry(1, 6),
  unit_auto_clear_wetlands = make_entry(2, 6),
  unit_auto_clear_damage = make_entry(3, 6),
  unit_auto_city_only = make_entry(4, 6),
  unit_auto_no_altering = make_entry(5, 6),
  unit_auto_city_no_altering = make_entry(0, 7),

  movement_indicators = {
    path = INTERFACE .. "MovementLED.PCX",
    crop_region = { 0, 0, 36, 8 },
  },
}

return unit_control
