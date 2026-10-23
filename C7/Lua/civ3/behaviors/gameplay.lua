-- A generic lua script for orchestrating behaviours during gameplay.
-- The idea for this is to host script paths that are not serialized in the json,
-- but rather "hardcoded" in the code

local time_unit = ENUMS.TimeUnit

local function game_data()
  return GAME_DATA()
end

local function rules()
  return GAME_DATA().rules
end

local gameplay = {}

local function disband_reward(context)
  local unit = context
  local unit_shield_cost = unit.unitType.shieldCost;

  local has_city = unit.location.HasCity
  local is_unit_on_own_city = unit.location.OwningPlayer() == unit.owner

  if has_city and is_unit_on_own_city then
    local city = unit.location.owningCity;
    local item_produced = city.itemBeingProduced

    if (item_produced:GetType().Name == "Building" and item_produced.IsGreatWonder() == true) then
      return
    end

    if (item_produced:GetType().Name == "Inflow") then
      return
    end

    local reward = math.floor(unit_shield_cost * rules().ShieldRateForDisbanding)
    game_data().SendMessageToUiFromLua("Our " .. unit.name .. " has been converted to " .. reward .. " shields.",
      unit.location)
    city.SetStoredShields(reward, true)
  end
end

-- Barbarians that reach a city carry away a quarter of its owner's gold.
local function ransack_gold(barbarian, city)
  return math.floor(city.owner.gold / 4)
end

-- A sold building returns its shield cost converted to gold at the rules'
-- shields-per-gold rate (4 in Civ3, the rate Wealth uses), as Civ3 does by
-- players' accounts: for example 5 gold for 20-shield walls.
local function building_sale_value(city, building, shield_cost)
  local shields_per_gold = rules().ShieldCostPerGold
  if shields_per_gold <= 0 then
    return 0
  end
  return math.floor(shield_cost / shields_per_gold)
end

-- Capturing a city "liberates" the city's share of its owner's treasury: the
-- owner's gold times the city's share of the owner's citizens. Civ3 shows the
-- amount but its formula isn't documented; this is Civilization II's.
local function capture_gold(city, conqueror, owner_citizens)
  if owner_citizens <= 0 then
    return 0
  end
  return math.floor(city.owner.gold * #city.residents / owner_citizens)
end

-- The percent chance a building survives its city's capture. As in Civ3, the
-- palace and small wonders are always lost, and so are buildings that make
-- culture (so a conqueror gets no culture for free); the buildings that let
-- a city grow past a size limit (aqueduct, hospital) always survive.
-- Players report others as often lost; the exact chance isn't documented.
-- Great wonders always change hands and aren't asked about.
local function building_capture_survival_chance(city, building)
  if building.isCenterOfEmpire or building.isSmallWonder then
    return 0
  end
  if building.culturePerTurn > 0 then
    return 0
  end
  if building.allowsCitySize2 or building.allowsCitySize3 then
    return 100
  end
  return 50
end

local function get_display_time_text(raw_time)
  local bc = game_data().timeOptions.negativeLabel
  local ad = game_data().timeOptions.positiveLabel

  -- Years
  if (game_data().timeOptions.baseUnit == time_unit.Years) then
    game_data().timeOptions.SetTimeUnitCurrent(time_unit.Years, raw_time)
    return math.abs(raw_time) .. " " ..
        (game_data().timeOptions.currentYear < 0 and bc or ad)
  end

  -- Months
  if (game_data().timeOptions.baseUnit == time_unit.Months) then
    local month = game_data().timeOptions.startMonth
    local start_year = game_data().timeOptions.startYear
    game_data().timeOptions.SetTimeUnitCurrent(time_unit.Years, start_year)
    if (raw_time > 12) then
      month = raw_time % 12
      game_data().timeOptions.SetTimeUnitCurrent(time_unit.Years, start_year + (raw_time / 12))
    else
      month = raw_time
      game_data().timeOptions.SetTimeUnitCurrent(time_unit.Months, month)
    end
    local month_name = game_data().timeOptions.GetAbbrMonthNameByIndex(month) -- can be overriden with a local function if needed
    return month_name .. ", " .. game_data().timeOptions.currentYear .. " " ..
        (game_data().timeOptions.currentYear < 0 and bc or ad)
  end

  -- Weeks
  if (game_data().timeOptions.baseUnit == time_unit.Weeks) then
    local week = game_data().timeOptions.startWeek
    local start_year = game_data().timeOptions.startYear
    game_data().timeOptions.SetTimeUnitCurrent(time_unit.Years, start_year)
    if (raw_time > 52) then
      week = raw_time % 52
      game_data().timeOptions.SetTimeUnitCurrent(time_unit.Years, start_year + (raw_time / 52))
    else
      week = raw_time
      game_data().timeOptions.SetTimeUnitCurrent(time_unit.Weeks, week)
    end
    return "Week " .. week .. ", " .. game_data().timeOptions.currentYear .. " " ..
        (game_data().timeOptions.currentYear < 0 and bc or ad)
  end

  -- Days
  if (game_data().timeOptions.baseUnit == time_unit.Days) then
    local day = game_data().timeOptions.startDay
    local start_year = game_data().timeOptions.startYear
    game_data().timeOptions.SetTimeUnitCurrent(time_unit.Years, start_year)
    if (raw_time > 365) then
      day = raw_time % 365
      game_data().timeOptions.SetTimeUnitCurrent(time_unit.Years, start_year + (raw_time / 365))
    else
      day = raw_time
      game_data().timeOptions.SetTimeUnitCurrent(time_unit.Days, day)
    end
    return "Day " .. day .. ", " .. game_data().timeOptions.currentYear .. " " ..
        (game_data().timeOptions.currentYear < 0 and bc or ad)
  end

  -- Hours
  if (game_data().timeOptions.baseUnit == time_unit.Hours) then
    local hour = game_data().timeOptions.startHour
    local start_day = game_data().timeOptions.startDay
    game_data().timeOptions.SetTimeUnitCurrent(time_unit.Days, start_day)
    if (raw_time > 24) then
      hour = raw_time % 24
      game_data().timeOptions.SetTimeUnitCurrent(time_unit.Days, start_day + (raw_time / 24))
    else
      hour = raw_time
      game_data().timeOptions.SetTimeUnitCurrent(time_unit.Hours, hour)
    end
    return "Hour " .. hour .. ", Day " .. game_data().timeOptions.currentDay
  end

  return "-12345 AD"
end

gameplay = {
  units = {
    -- Context = MapUnit mapUnit
    disband = function(context)
      disband_reward(context)
    end,
  },
  barbarians = {
    -- Context = MapUnit barbarian, City city
    -- Returns the gold the barbarian takes from the city's owner when it ransacks the city.
    ransack_gold = function(barbarian, city)
      return ransack_gold(barbarian, city)
    end,
  },
  buildings = {
    -- Context = City city, Building building, int shieldCost (what the
    -- building costs the city's owner)
    -- Returns the gold the owner gets for selling the building.
    sale_value = function(city, building, shield_cost)
      return building_sale_value(city, building, shield_cost)
    end,
  },
  cities = {
    -- Context = City city (still its old owner's), Player conqueror,
    -- int ownerCitizens (all the old owner's citizens, this city's included)
    -- Returns the gold the conqueror takes from the old owner.
    capture_gold = function(city, conqueror, owner_citizens)
      return capture_gold(city, conqueror, owner_citizens)
    end,
    -- Context = City city, Building building (not a great wonder)
    -- Returns the percent chance (0-100) the building survives the capture.
    building_capture_survival_chance = function(city, building)
      return building_capture_survival_chance(city, building)
    end,
  },
  time = {
    -- Context = int time
    display_text = function(context)
      return get_display_time_text(context)
    end,
  }
}

return gameplay
