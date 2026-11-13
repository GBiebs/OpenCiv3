--[[
  To add new replacement texture to the config modify the "c7_texture_list" table.
--]]
local utils = require "utils"

local c7_texture_list = {
  "Art/buttonsFINAL.png",
  "Art/TileInfo.png",
  "Art/X-o_ALLstates-sprite.png",
  "Art/SmallHeads/popHeads.png",
  "Art/Terrain/Mountains-snow.png",
  "Art/Terrain/Mountains.png",
  "Art/Terrain/TerrainBuildings.png",
  "Art/Terrain/goodyhuts.png",
  "Art/Terrain/Volcanos forests.png",
  "Art/Terrain/Volcanos jungles.png",
  "Art/Terrain/Volcanos.png",
  "Art/Terrain/grassland forests.png",
  "Art/Terrain/hill forests.png",
  "Art/Terrain/hill jungle.png",
  "Art/Terrain/irrigation DESETT.png",
  "Art/Terrain/irrigation PLAINS.png",
  "Art/Terrain/irrigation TUNDRA.png",
  "Art/Terrain/irrigation.png",
  "Art/Terrain/marsh.png",
  "Art/Terrain/mountain forests.png",
  "Art/Terrain/mountain jungles.png",
  "Art/Terrain/mtnRivers.png",
  "Art/Terrain/deltaRivers.png",
  "Art/Terrain/plains forests.png",
  "Art/Terrain/tnt.png",
  "Art/Terrain/roads.png",
  "Art/Terrain/railroads.png",
  "Art/Terrain/tundra forests.png",
  "Art/Terrain/wCSO.png",
  "Art/Terrain/wOOO.png",
  "Art/Terrain/wSSS.png",
  "Art/Terrain/xdgc.png",
  "Art/Terrain/xdgp.png",
  "Art/Terrain/xdpc.png",
  "Art/Terrain/xggc.png",
  "Art/Terrain/xhills.png",
  "Art/Terrain/xpgc.png",
  "Art/Terrain/xtgc.png",
  "Art/Terrain/craters.png",
  "Art/Terrain/pollution.png",
  "Art/Cities/rMIDEAST.png",
  "Art/Cities/MIDEASTWALL.png",
  "Art/Cities/DESTROY.png",
  "Art/city screen/CityIcons.png",
  "Art/city screen/ProdButton.png",
  "Art/city screen/background.png",
  "Art/city screen/cityMgmtButtons.png",
  "Art/resources.png",
  "Art/WorldSetup/CLIMTEMPAGEDepress.png",
  "Art/WorldSetup/CLIMTEMPAGERollovers.png",
  "Art/WorldSetup/age.png",
  "Art/WorldSetup/background.png",
  "Art/WorldSetup/climate.png",
  "Art/WorldSetup/landmassWaterSMALL.png",
  "Art/WorldSetup/landmassWaterSMALLdepress.png",
  "Art/WorldSetup/landmassWaterSMALLrollovers.png",
  "Art/WorldSetup/landmassWaterlarge.png",
  "Art/WorldSetup/temperature.png",
  "Art/interface/NormButtons.png",
  "Art/interface/rolloverbuttons.png",
  "Art/interface/highlightedbuttons.png",
  "Art/interface/AirMissionButtons.png",
  "Art/interface/box left color.png",
  "Art/interface/box right color.png",
  "Art/interface/nextturn states color.png",
  "Art/interface/consoleButtons.png",
  "Art/SmallHeads/popupDOMESTIC.png",
  "Art/SmallHeads/popupTRADE.png",
  "Art/SmallHeads/popupMILITARY.png",
  "Art/SmallHeads/popupFOREIGN.png",
  "Art/SmallHeads/popupCULTURE.png",
  "Art/SmallHeads/popupSCIENCE.png",
  "Art/SmallHeads/advisor_tab.png",
  "Art/Advisors/domestic_icons_aux.png",
  "Art/Advisors/domesticBUTTON.png",
  "Art/Cities/city icons.png",
  "Art/popupborders.png",
  "Art/interface/menuButtons.png",
  "Art/Advisors/dialogbox.png",
  "Art/Advisors/domestic.png",
  "Art/Advisors/domestic_plusminus.png",
  "Art/Tech Chooser/scienceNAV.png",
  "Art/Credits/credits_background.png",
  "Art/city screen/ProductionQueueBox.png",
  "Art/Advisors/non_required.png",
  "Art/Advisors/techboxes.png",
  "Art/Advisors/military.png",
  "Art/Advisors/militaryNAV.png",
  "Art/interface/MovementLED.png",
  "Art/Advisors/science_ancient.png",
  "Art/Advisors/science_middle.png",
  "Art/Advisors/science_industrial_new.png",
  "Art/Advisors/science_modern.png",
  "Art/exitBox-backgroundStates.png",
  "Art/PlayerSetup/playerSetup.png",
  "Art/Diplomacy/talk_offer.png",
  "Art/Diplomacy/counter.png",
  "Art/PalaceView/bkgr.png",
  "Art/city screen/luxuryicons_small.png",
  "Art/Terrain/FogOfWar.png",
  "Art/Terrain/Territory.png",
  "Art/Units/units_32.png",
  "Art/city screen/buildings-small.png",
  "Art/city screen/buildings-large.png",
  "Art/Cursor.png",
  "Art/interface/box trans color.png",
  "Art/SpaceShip/SHIPbackground.png",
  "Art/Advisors/trade.png",
  "Art/Advisors/foreign.png",
  "Art/Advisors/foreignTAB.png",
  "Art/Advisors/culture.png",
  "Art/Advisors/demographics.png",
  "Art/Advisors/wonders_background.png",
  "Art/Advisors/domestic_plusminus.png",
  "Art/histograph-win5_final.png",
  "Art/histograph-top5_final.png",
  "Art/3checkboxes-USE.png",
  -- Not Civ3 art: buttons of our own (see own_button below).
  "Art/interface/unit_build_outpost.png",
  "Art/interface/unit_build_colony.png",
  "Art/interface/unit_join_city.png",
  "Art/interface/unit_build_airfield.png",
  "Art/interface/unit_build_radar_tower.png",
  "Art/interface/unit_airlift.png",
  "Art/interface/unit_sentry.png",
  "Art/interface/unit_sentry_enemy_only.png",
  "Art/interface/unit_load.png",
  "Art/interface/unit_unload.png",
  "Art/interface/unit_build_army.png",
  "Art/interface/unit_hurry_building.png",
  "Art/interface/unit_science_age.png",
  "Art/interface/unit_sacrifice.png",
  "Art/interface/unit_upgrade.png",
  "Art/interface/unit_build_barricade.png",
  "Art/interface/unit_road_to.png",
  "Art/interface/unit_rail_to.png",
  "Art/interface/unit_auto_trade_network.png",
  "Art/interface/unit_auto_irrigate_to_city.png",
  "Art/interface/unit_auto_clear_forest.png",
  "Art/interface/unit_auto_clear_wetlands.png",
  "Art/interface/unit_auto_clear_damage.png",
  "Art/interface/unit_auto_city_only.png",
  "Art/interface/unit_auto_no_altering.png",
  "Art/interface/unit_auto_city_no_altering.png",
  "Art/Cities/CityBuildingIcons.png"
}

--- The civ colors of Civ3 (Art/Units/Palettes/ntp00-31.pcx, read by
--- ArtGen/notes/civ_colors.md): per color index its flat color (palette
--- entry 64: borders, the map and the UI) and the 16-shade ramp, light to
--- dark (entries 0-15), that the civ-colored parts of units are shaded with.
local civ_colors = {
  { flat = "E6E6E6", ramp = { "FBFBFB", "F8F8F8", "F5F5F5", "F2F2F2", "EFEFEF", "ECECEC", "E9E9E9", "E6E6E6", "D7D7D7", "C8C8C8", "B9B9B9", "AAAAAA", "9B9B9B", "8C8C8C", "7D7D7D", "7D7D7D" } }, -- 0
  { flat = "E30A0A", ramp = { "FF9C8A", "FB8475", "F76C60", "F3544B", "EF3C36", "EB2421", "E70C0C", "E30A0A", "D40A0A", "C50A0A", "B60A0A", "A70A0A", "980A0A", "890A0A", "7A0A0A", "740A0A" } }, -- 1
  { flat = "FF8C00", ramp = { "FFC471", "FFBB5F", "FFB24D", "FFA93B", "FFA029", "FF9717", "FF8E05", "FF8C00", "EA7F01", "D57202", "C06503", "AB5804", "964B05", "813E06", "6C3107", "6A3107" } }, -- 2
  { flat = "E6E600", ramp = { "FDFDA7", "FAFA8C", "F7F771", "F4F456", "F1F13B", "EEEE20", "EBEB05", "E6E600", "D6D301", "C6C002", "B6AD03", "A69A04", "968705", "867406", "766107", "715D0D" } }, -- 3
  { flat = "077607", ramp = { "35CC34", "2EBE2D", "27B026", "20A21F", "199418", "128611", "0B780A", "077607", "076E07", "076607", "075E07", "075607", "074E07", "074607", "073E07", "033903" } }, -- 4
  { flat = "1DD8DA", ramp = { "C7FFFF", "ABF9F9", "8FF3F3", "73EDED", "57E7E7", "3BE1E1", "1FDBDB", "1DD8DA", "1AC9CB", "17BABC", "14ABAD", "119C9E", "0E8D8F", "0B7E80", "086F71", "076C6C" } }, -- 5
  { flat = "3463FF", ramp = { "99DCF6", "89C8F7", "79B4F8", "69A0F9", "598CFA", "4978FB", "3964FC", "3463FF", "2D5BF0", "2653E1", "1F4BD2", "1843C3", "113BB4", "0A33A5", "032B96", "002792" } }, -- 6
  { flat = "FF4EED", ramp = { "FFAFF8", "FF9FF7", "FF8FF6", "FF7FF5", "FF6FF4", "FF5FF3", "FF4FF2", "FF4EED", "EE49DE", "DD44CF", "CC3FC0", "BB3AB1", "AA35A2", "993093", "882B84", "88277E" } }, -- 7
  { flat = "79009C", ramp = { "BF19EA", "B415DD", "A911D0", "9E0DC3", "9309B6", "8805A9", "7D019C", "79009C", "700090", "670084", "5E0078", "55006C", "4C0060", "430054", "3A0048", "350048" } }, -- 8
  { flat = "906A06", ramp = { "CCA747", "C29D3D", "B89333", "AE8929", "A47F1F", "9A7515", "906B0B", "906A06", "856206", "7A5A06", "6F5206", "644A06", "594206", "4E3A06", "433206", "3D2D04" } }, -- 9
  { flat = "78DD00", ramp = { "CDFF7B", "BFFA67", "B1F553", "A3F03F", "95EB2B", "87E617", "79E103", "78DD00", "70CE00", "68BF00", "60B000", "58A100", "509200", "488300", "407400", "3C7200" } }, -- 10
  { flat = "940808", ramp = { "DA5F52", "CF5146", "C4433A", "B9352E", "AE2722", "A31916", "980B0A", "940808", "8B0A0A", "820C0C", "790E0E", "701010", "671212", "5E1414", "551616", "521616" } }, -- 11
  { flat = "939393", ramp = { "C7C7C7", "BFBFBF", "B7B7B7", "AFAFAF", "A7A7A7", "9F9F9F", "979797", "939393", "898989", "7F7F7F", "757575", "6B6B6B", "616161", "575757", "4D4D4D", "4A4A4A" } }, -- 12
  { flat = "002792", ramp = { "637DD6", "536FCB", "4361C0", "3353B5", "2345AA", "13379F", "032994", "002792", "002484", "002176", "001E68", "001B5A", "00184C", "00153E", "001230", "000D30" } }, -- 13
  { flat = "0E8797", ramp = { "5AD7E8", "4ECADB", "42BDCE", "36B0C1", "2AA3B4", "1E96A7", "12899A", "0E8797", "0E808F", "0E7987", "0E727F", "0E6B77", "0E646F", "0E5D67", "0E565F", "0E515B" } }, -- 14
  { flat = "F3C2FC", ramp = { "FEEFFF", "FDE8FF", "FCE1FF", "FBDAFF", "FAD3FF", "F9CCFF", "F8C5FF", "F3C2FC", "EBB2F5", "E3A2EE", "DB92E7", "D382E0", "CB72D9", "C362D2", "BB52CB", "B84FCB" } }, -- 15
  { flat = "3C3C3C", ramp = { "888888", "7C7C7C", "707070", "646464", "585858", "4C4C4C", "404040", "3C3C3C", "383838", "343434", "303030", "2C2C2C", "282828", "242424", "202020", "202020" } }, -- 16
  { flat = "C000FF", ramp = { "DB63FF", "D753FF", "D343FF", "CF33FF", "CB23FF", "C713FF", "C303FF", "C000FF", "B600F2", "AC00E5", "A200D8", "9800CB", "8E00BE", "8400B1", "7A00A4", "76009E" } }, -- 17
  { flat = "CDB700", ramp = { "F6E766", "F0DF55", "EAD744", "E4CF33", "DEC722", "D8BF11", "D2B700", "CDB700", "C2AC00", "B7A100", "AC9600", "A18B00", "968000", "8B7500", "806A00", "7F6900" } }, -- 18
  { flat = "7E00FF", ramp = { "BF66FF", "B555FF", "AB44FF", "A133FF", "9722FF", "8D11FF", "8300FF", "7E00FF", "7300F4", "6800E9", "5D00DE", "5200D3", "4700C8", "3C00BD", "3100B2", "3000B1" } }, -- 19
  { flat = "ACD9EA", ramp = { "DFFFFF", "D7F9FC", "CFF3F9", "C7EDF6", "BFE7F3", "B7E1F0", "AFDBED", "ACD9EA", "A1CEDF", "96C3D4", "8BB8C9", "80ADBE", "75A2B3", "6A97A8", "5F8C9D", "5E8B9C" } }, -- 20
  { flat = "728637", ramp = { "B8C58D", "ADBB7F", "A2B171", "97A763", "8C9D55", "819347", "768939", "728637", "697D30", "607429", "576B22", "4E621B", "455914", "3C500D", "334706", "314300" } }, -- 21
  { flat = "FFC000", ramp = { "FFED66", "FFE655", "FFDF44", "FFD833", "FFD122", "FFCA11", "FFC300", "FFC000", "F4B500", "E9AA00", "DE9F00", "D39400", "C88900", "BD7E00", "B27300", "B17200" } }, -- 22
  { flat = "FF6C00", ramp = { "FFB266", "FFA755", "FF9C44", "FF9133", "FF8622", "FF7B11", "FF7000", "FF6C00", "F46100", "E95600", "DE4B00", "D34000", "C83500", "BD2A00", "B21F00", "B11E00" } }, -- 23
  { flat = "C4D495", ramp = { "F0FBCF", "E9F5C6", "E2EFBD", "DBE9B4", "D4E3AB", "CDDDA2", "C6D799", "C4D495", "B9C98A", "AEBE7F", "A3B374", "98A869", "8D9D5E", "829253", "778748", "768647" } }, -- 24
  { flat = "75FFAD", ramp = { "B9FFE0", "AEFFD8", "A3FFD0", "98FFC8", "8DFFC0", "82FFB8", "77FFB0", "75FFAD", "6AF4A2", "5FE997", "54DE8C", "49D381", "3EC876", "33BD6B", "28B260", "27B15F" } }, -- 25
  { flat = "BDC844", ramp = { "FEFFA8", "F4F698", "EAED88", "E0E478", "D6DB68", "CCD258", "C2C948", "BDC844", "AEBB41", "9FAE3E", "90A13B", "819438", "728735", "637A32", "546D2F", "506829" } }, -- 26
  { flat = "AF8652", ramp = { "E8CBA6", "DFC098", "D6B58A", "CDAA7C", "C49F6E", "BB9460", "B28952", "AF8652", "A47B47", "99703C", "8E6531", "835A26", "784F1B", "6D4410", "623905", "613804" } }, -- 27
  { flat = "6F530A", ramp = { "B7A470", "AB975F", "9F8A4E", "937D3D", "87702C", "7B631B", "6F560A", "6F530A", "664A0A", "5D410A", "54380A", "4B2F0A", "42260A", "391D0A", "30140A", "301407" } }, -- 28
  { flat = "485B2C", ramp = { "919F7E", "859471", "798964", "6D7E57", "61734A", "55683D", "495D30", "485B2C", "425227", "3C4922", "36401D", "303718", "2A2E13", "24250E", "1E1C09", "1A1C07" } }, -- 29
  { flat = "FBC7FF", ramp = { "FFF2FF", "FFEBFF", "FFE4FF", "FFDDFF", "FFD6FF", "FFCFFF", "FFC8FF", "FBC7FF", "F0BCF4", "E5B1E9", "DAA6DE", "CF9BD3", "C490C8", "B985BD", "AE7AB2", "AD79B1" } }, -- 30
  { flat = "C6A7ED", ramp = { "F1DCFF", "EAD4FC", "E3CCF9", "DCC4F6", "D5BCF3", "CEB4F0", "C7ACED", "C6A7ED", "BB9CE2", "B091D7", "A586CC", "9A7BC1", "8F70B6", "8465AB", "795AA0", "78599F" } }, -- 31
}

-- The icon files of the advances and buildings of the bundled rules
-- (C7/ModernArt, made by ArtGen/civilopedia_icons/ui_icons.py from their
-- Civilopedia icons), by Civilopedia entry.
local icon_files = require "icon_files"

-- The civilizations that have a leader head of our own, by name (see
-- leader_heads below).
local leader_head_civs = {}
for _, name in ipairs({
  "America", "Arabia", "Aztecs", "Babylon", "Byzantines", "Carthage", "Celts", "China",
  "Egypt", "England", "France", "Germany", "Greece", "Hittites", "Inca", "India",
  "Iroquois", "Japan", "Korea", "Maya", "Mongols", "Netherlands", "Ottomans", "Persia",
  "Portugal", "Rome", "Russia", "Scandinavia", "Spain", "Sumeria", "Zululand",
}) do
  leader_head_civs[name] = true
end

-- MDJ's clans' banners, by Civilopedia entry (see leader_heads below).
local mdj_leader_heads = {}
for _, clan in ipairs({
  "Otomo", "Mori", "Ichijo", "Urakami", "Miyoshi", "Oda", "Imagawa", "Takeda",
  "Hojo", "Uesugi", "Date", "Tokugawa",
}) do
  mdj_leader_heads["RACE_" .. string.upper(clan)] = clan
end

-- Build lookup table from c7_texture_list without extensions
local lookup = {}
for _, path in ipairs(c7_texture_list) do
  lookup[utils.strip_extension(path)] = path
end

local function path_transformer(path)
  local stripped = utils.strip_extension(path)
  return lookup[stripped] or path
end


--[[
  This function returns a table with texture definitions for "modern" graphics.

  It produces this table by copying the config of the civ3 assets and
  replacing the paths to the original Civilization 3 PCX textures with
  modern PNG versions.
--]]
return function(civ3_textures)
  local c7_textures = utils.transform_paths(civ3_textures, path_transformer)

  -- TODO: Add proper replacement for the asset
  c7_textures.terrain.river_delta = "Art/Terrain/mtnRivers.png"

  -- The replacement map yield icons fill more of their cells than Civ3's.
  c7_textures.icons.map_shield.crop_region = { 411, 9, 15, 18 }
  c7_textures.icons.map_commerce.crop_region = { 438, 5, 26, 24 }
  c7_textures.icons.map_food.crop_region = { 470, 7, 21, 21 }

  c7_textures.civ_colors = {}
  for i, color in ipairs(civ_colors) do
    c7_textures.civ_colors["color_" .. (i-1)] = { path = "", hex_color = color.flat, ramp = color.ramp }
  end

  c7_textures.animations.cursor = {
    path = "Art/Animations/Cursor.png",
    animation_rows = 2,
    animation_cols = 9,
    frame_duration = 0.6,
  }
  c7_textures.animations.disorder = {
    path = "Art/Animations/DisorderDefault.png",
    animation_rows = 1,
    animation_cols = 6,
    frame_duration = 0.6,
  }

  -- The standalone asset pack only has placeholders for Civ3's advance and
  -- building icons, which showed the words "Tech" and "Bldg" in the tech
  -- tree and the production menus (owner's report, 2026-10-12). Advances
  -- and buildings of the bundled rules show their own icons instead, the
  -- same pictures as their Civilopedia icons; any others (from an addon)
  -- still show the placeholders.
  function c7_textures.tech_icons.small:map_object_to_sprite(tech)
    local path = icon_files.advances[tech.CivilopediaEntry]
    return { path = path or "Art/Tech Chooser/Icons/placeholder.png" }
  end

  -- A building's entry, or for Wealth (an Inflow) the Civilopedia's.
  local function building_entry(building)
    if building:GetType().Name == "Inflow" then
      return "BLDG_" .. building.name
    end
    return building.civilopediaEntry
  end
  local function use_own_icons(size)
    local icons = c7_textures.building_icons[size]
    local placeholder = icons.map_object_to_sprite
    function icons:map_object_to_sprite(building)
      local files = icon_files.buildings[building_entry(building)]
      if files then
        return { path = files[size] }
      end
      return placeholder(self, building)
    end
  end
  use_own_icons("small")
  use_own_icons("large")

  -- Each civilization of the bundled rules has its own leader head
  -- (C7/ModernArt/Art/Leaderheads/<civ name>.png, made by
  -- ArtGen/leaderheads), whose 2x picture ModernGraphics loads at Civ3's
  -- 115x115. There's one per leader, the same in every era and mood. Any
  -- other civilization shows the placeholder.
  function c7_textures.leader_heads:map_object_to_sprite(player_or_civ)
    local civ = player_or_civ
    local type_name = player_or_civ:GetType().Name
    if type_name == "Player" then
      civ = player_or_civ.civilization
    elseif type_name == "LeaderPortrait" then
      -- Our portraits have one face for every mood.
      civ = player_or_civ.player.civilization
    end
    if civ and leader_head_civs[civ.name] then
      return { path = "Art/Leaderheads/" .. civ.name .. ".png" }
    end
    -- MDJ's clans (C7/Lua/mdj), whose names have slashes in them, by
    -- Civilopedia entry: RACE_ODA's banner is Oda.png (made by
    -- ArtGen/leaderheads/mdj_mon.py).
    local clan = civ and civ.civilopediaEntry and mdj_leader_heads[civ.civilopediaEntry]
    if clan then
      return { path = "Art/Leaderheads/" .. clan .. ".png" }
    end
    return {
      path = "Art/Advisors/placeholder_leaderhead.png",
    }
  end

  -- The icons by a city for its barracks, airport and harbor, which the
  -- asset pack lacks: a sheet of our own (C7/ModernArt/Art/Cities/
  -- CityBuildingIcons.png, made by ArtGen/ui_buttons/city_icons.py), drawn
  -- like Civ3's. Before, the buildings' own pictures were shown there,
  -- shrunk until they couldn't be made out.
  for column, name in ipairs({ "barracks", "airport", "harbor" }) do
    local sprite = { path = "Art/Cities/CityBuildingIcons.png", crop_region = { (column - 1) * 25, 0, 25, 17 } }
    c7_textures.city_building_icons[name].map_object_to_sprite = function(_, _)
      return sprite
    end
  end

  -- The asset pack's button sheet is blank where Civ3's has the air
  -- missions and the airdrop, so they have buttons of our own
  -- (C7/ModernArt/Art/interface/AirMissionButtons.png, made by
  -- ArtGen/ui_buttons/build.py): a column per button, a row per
  -- state.
  local air_mission_buttons = {
    "unit_bombing", "unit_precision_bombing", "unit_recon",
    "unit_air_superiority", "unit_rebase", "unit_airdrop",
  }
  for column, action in ipairs(air_mission_buttons) do
    local function cell(row)
      return {
        path = "Art/interface/AirMissionButtons.png",
        crop_region = { (column - 1) * 32, row * 32, 32, 32 },
      }
    end
    c7_textures.ui.unit_control[action] = { normal = cell(0), hover = cell(1), pressed = cell(2) }
  end
  -- Nor does it have Civ3's automated bombarding buttons, which show their
  -- orders' own buttons instead.
  c7_textures.ui.unit_control.unit_auto_bombard = c7_textures.ui.unit_control.unit_bombard
  c7_textures.ui.unit_control.unit_auto_bombing = c7_textures.ui.unit_control.unit_bombing
  c7_textures.ui.unit_control.unit_auto_precision_bombing = c7_textures.ui.unit_control.unit_precision_bombing
  -- The asset pack's NormButtons.png has blank buttons where Civ3's has
  -- Build Outpost, Build Colony, Build Airfield, Build Radar Tower,
  -- Airlift, Sentry, Sentry (enemy only), Load, Unload, Build Army, Hurry
  -- Building, Increase Scientific Output, Upgrade, Build Barricade and Join City, so those have buttons of their own
  -- (C7/ModernArt/Art/interface/<order>.png, made by
  -- ArtGen/ui_buttons/build.py): the normal, hover and pressed buttons side
  -- by side.
  local function own_button(order)
    local path = "Art/interface/" .. order .. ".png"
    return {
      normal = { path = path, crop_region = { 0, 0, 32, 32 } },
      hover = { path = path, crop_region = { 32, 0, 32, 32 } },
      pressed = { path = path, crop_region = { 64, 0, 32, 32 } },
    }
  end
  c7_textures.ui.unit_control.unit_build_outpost = own_button("unit_build_outpost")
  c7_textures.ui.unit_control.unit_build_colony = own_button("unit_build_colony")
  c7_textures.ui.unit_control.unit_join_city = own_button("unit_join_city")
  c7_textures.ui.unit_control.unit_build_airfield = own_button("unit_build_airfield")
  c7_textures.ui.unit_control.unit_build_radar_tower = own_button("unit_build_radar_tower")
  c7_textures.ui.unit_control.unit_airlift = own_button("unit_airlift")
  c7_textures.ui.unit_control.unit_sentry = own_button("unit_sentry")
  c7_textures.ui.unit_control.unit_sentry_enemy_only = own_button("unit_sentry_enemy_only")
  c7_textures.ui.unit_control.unit_load = own_button("unit_load")
  c7_textures.ui.unit_control.unit_unload = own_button("unit_unload")
  c7_textures.ui.unit_control.unit_build_army = own_button("unit_build_army")
  c7_textures.ui.unit_control.unit_hurry_building = own_button("unit_hurry_building")
  c7_textures.ui.unit_control.unit_science_age = own_button("unit_science_age")
  c7_textures.ui.unit_control.unit_sacrifice = own_button("unit_sacrifice")
  c7_textures.ui.unit_control.unit_upgrade = own_button("unit_upgrade")
  c7_textures.ui.unit_control.unit_build_barricade = own_button("unit_build_barricade")
  -- The worker automation commands (ArtGen/ui_buttons/worker_automation.py).
  for _, order in ipairs({ "unit_road_to", "unit_rail_to", "unit_auto_trade_network", "unit_auto_irrigate_to_city",
      "unit_auto_clear_forest", "unit_auto_clear_wetlands", "unit_auto_clear_damage", "unit_auto_city_only",
      "unit_auto_no_altering", "unit_auto_city_no_altering" }) do
    c7_textures.ui.unit_control[order] = own_button(order)
  end

  -- The asset pack's techboxes.png only has the small boxes of the Ancient
  -- era, so a bigger box (an advance with more than one thing to show) or a
  -- box of a later era was blank: the advance had no box, known or not, and
  -- its "not required" sign floated where the box's corner should have been
  -- (owner's report, 2026-10-31). Every box comes from a sheet of our own
  -- instead (C7/ModernArt/Art/Advisors/TechBoxesAllSizes.png, made by
  -- ArtGen/ui_buttons/tech_boxes.py from the pack's small boxes), laid out
  -- like Civ3's, so the crop regions stay as they are.
  for _, eras in pairs(c7_textures.tech_boxes) do
    if type(eras) == "table" then
      for _, sizes in pairs(eras) do
        for _, box in pairs(sizes) do
          box.path = "Art/Advisors/TechBoxesAllSizes.png"
        end
      end
    end
  end

  -- The asset pack's TerrainBuildings.png only has a placeholder where the
  -- colonies go. Named by Civ3's file, which the pack doesn't have, a colony
  -- comes from the remade sheet (C7/ModernArt/Art/Terrain/TerrainBuildings.png),
  -- as ModernGraphics uses the remade art for anything the pack lacks.
  -- There's no art for outposts, airfields or radar towers at all; the map
  -- draws them itself.
  c7_textures.terrain_improvements.colony.path = "Art/Terrain/TerrainBuildings.pcx"

  function c7_textures.borders:find_column(_)
    return 0
  end

  -- Instead of a place in Civ3's units_32.pcx, each unit art has its own
  -- thumbnail next to its sheets (C7/ModernArt/Art/Units/<art>/<art>_32.png,
  -- made by ArtGen/units/thumbnails.py), which ModernGraphics loads. It's the
  -- thumbnail of the art the player's unit is drawn with in their era, so
  -- it changes with the era like the unit does.
  -- Context - ItemContext (UnitPrototype proto, Player player)
  function c7_textures.unit_icons:map_object_to_sprite(context)
    local main_art = context.proto.art.mainArt
    local art = main_art.defaultName
    local variations = main_art.variations
    local era = context.player.eraCivilopediaName
    if variations and era and variations[era] then
      art = variations[era]
    end
    return { path = "Art/Units/" .. art .. "/" .. art .. "_32.png" }
  end

  return c7_textures
end
