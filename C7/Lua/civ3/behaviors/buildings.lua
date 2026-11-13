local YieldType = ENUMS.Tile_YieldType

local function rules()
  return GAME_DATA().rules
end

local buildings = {}

buildings.production_rules = {
  -- Next to the ocean, as in Civ3, unless the rules let a city further
  -- inland build them (Eternal War: within 3 tiles).
  must_be_coastal = function(city)
    return city.location:OceanWithin(rules().CoastalBuildingRange)
  end,

  -- "To build the Hoover Dam a city must have a river within its radius";
  -- "A city must have a river within its radius to build a Hydro Plant"
  -- (Civilopedia, BLDG_Hoover_Dam and BLDG_Hydro_Plant).
  must_be_near_river = function(city)
    return city:HasRiverInRadius()
  end,

  -- "A city must have fresh water within its radius to build a Nuclear
  -- Plant" (Conquests Civilopedia, BLDG_Nuclear_Plant).
  must_be_near_water = function(city)
    return city:HasFreshWaterInRadius()
  end,

  can_only_be_built_in_towns = function(city)
    return #city.residents <= rules().MaximumLevel1CitySize
  end,

  allows_city_size_2 = function(city)
    local is_town = #city.residents <= rules().MaximumLevel1CitySize
    local has_fresh_water = city.location:NeighborsFreshWater() or city.location:BordersRiver()
    return is_town and not has_fresh_water
  end,

  -- The Hospital and Shakespeare's Theater let a city grow past size 12, and
  -- the Civilopedia says both need "a population of 7 or higher" to build,
  -- i.e. the city must have grown past a town.
  allows_city_size_3 = function(city)
    return #city.residents > rules().MaximumLevel1CitySize
  end,
}

buildings.unit_production_effects = {
  veteran_ground_units = function(unit)
    if unit.unitType:IsLandUnit() then
      unit:Promote()
    end
  end,

  veteran_sea_units = function(unit)
    if unit.unitType:IsSeaUnit() then
      unit:Promote()
    end
  end,

  -- The Airport "Produces veteran air units"
  -- (https://civfanatics.com/civ3/civilopedia/improvements/).
  veteran_air_units = function(unit)
    if unit.unitType:IsAirUnit() then
      unit:Promote()
    end
  end,
}

-- NATIVE_DEFAULT marks a function that the game runs as C# instead, since
-- tile yields are worked out very often. If you change what such a function
-- does, take away its NATIVE_DEFAULT so the game runs your Lua (or change the
-- C# in C7Engine/Lua/NativeBehaviors.cs to match). A function put in its place
-- by an addon always runs as Lua.
buildings.tile_modifiers = {
  -- The Harbor: "Coastal, sea, and ocean squares produce one extra food."
  -- (Conquests Civilopedia, BLDG_Harbor), so an Ocean tile's none becomes
  -- one. A tile under fallout makes nothing, so it gets none either. The
  -- Harbor, Offshore Platform and Commercial Dock work only on bodies of
  -- water larger than 20 tiles, not lakes: Civ3 binary
  -- (doc/perez-findings/Civ3_Findings.md, Economy).
  increases_food_in_water = NATIVE_DEFAULT("buildings.tile_modifiers.increases_food_in_water", function(yield)
    if yield.type == YieldType.Food and yield.tile:IsInLargeBodyOfWater() and not yield.tile:HasFallout() then
      yield.bonus = yield.bonus + 1
    end
  end),

  increases_shields_in_water = NATIVE_DEFAULT("buildings.tile_modifiers.increases_shields_in_water", function(yield)
    if yield.type == YieldType.Production and yield.tile:IsInLargeBodyOfWater() then
      yield.bonus = yield.bonus + 1
    end
  end),

  increases_trade_in_water = NATIVE_DEFAULT("buildings.tile_modifiers.increases_trade_in_water", function(yield)
    if yield.type == YieldType.Commerce and yield.tile:IsInLargeBodyOfWater() and yield.baseYield > 0 then
      yield.bonus = yield.bonus + 1
    end
  end),

  -- The Colossus: +1 commerce on every tile of the city, land or water,
  -- that already makes commerce, the city center included. Civ3 adds it
  -- before the despotism penalty, so a tile it raises from 2 to 3 is
  -- brought back down to 2.
  increased_trade = NATIVE_DEFAULT("buildings.tile_modifiers.increased_trade", function(yield)
    if yield.type == YieldType.Commerce and yield.baseYield > 0 then
      yield.bonus = yield.bonus + 1
      local owner = yield.tile:OwningPlayer()
      if owner ~= nil and owner.government.hasTilePenalty and yield.baseYield == 2 then
        yield.penalty = yield.penalty + 1
      end
    end
  end),
}

return buildings
