local terrain_improvements = {}

local TERRAIN = "Art/Terrain/"
local CITIES = "Art/Cities/"

--[[
TerrainBuildings.pcx contains multiple pieces of art in a grid, with each
item being 128x64 pixesl.

The basic version is:
  Fortress (ancient)     | Colony (an)   | Barb camp
  Fortress (medieval)    | Colony (me)   | Mine
  Fortress (industrial)  | Colony (in)   | Empty
  Fortress (modern)      | Colony (mo)   | Empty
--]]
local function make_entry(x, y)
  return {
    path = TERRAIN .. "TerrainBuildings.pcx",
    crop_region = { 128 * x, 64 * y, 128, 64 },
    shadows = false,
  }
end

terrain_improvements = {
  mine = make_entry(2, 1),
  fortress = make_entry(0, 0),
  barricade = make_entry(3, 0),
  -- Colonies by their owner's era (column 1).
  colony = {
    ancient = make_entry(1, 0),
    middle = make_entry(1, 1),
    industrial = make_entry(1, 2),
    modern = make_entry(1, 3),
  },
}

--[[
Play the World's "x_airfields and detect.PCX": the industrial and modern
airfields (128x64) in the top row; below, in 128x128 cells standing on their
lower half, the ancient, medieval and industrial outposts, and the radar
tower under the first of them.
--]]
local AIRFIELDS = TERRAIN .. "x_airfields and detect.PCX"
local function airfields_entry(x, y, height)
  return {
    path = AIRFIELDS,
    crop_region = { 128 * x, y, 128, height },
    shadows = false,
  }
end

terrain_improvements.airfield = {
  industrial = airfields_entry(0, 0, 64),
  modern = airfields_entry(1, 0, 64),
}
terrain_improvements.outpost = {
  ancient = airfields_entry(0, 64, 128),
  middle = airfields_entry(1, 64, 128),
  industrial = airfields_entry(2, 64, 128),
}
terrain_improvements.radarTower = airfields_entry(0, 192, 128)

terrain_improvements.irrigation = {
  grass = TERRAIN .. "irrigation.pcx",
  desert = TERRAIN .. "irrigation DESETT.pcx",
  plains = TERRAIN .. "irrigation PLAINS.pcx",
  tundra = TERRAIN .. "irrigation TUNDRA.pcx",
}

terrain_improvements.railroad = TERRAIN .. "railroads.pcx"

terrain_improvements.road = TERRAIN .. "roads.pcx"

terrain_improvements.ruins = CITIES .. "DESTROY.PCX"
terrain_improvements.pollution = TERRAIN .. "pollution.pcx"
terrain_improvements.craters = TERRAIN .. "craters.pcx"

return terrain_improvements
