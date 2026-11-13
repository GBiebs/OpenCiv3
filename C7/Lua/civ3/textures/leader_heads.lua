local IMAGE_SIZE = 115

local leader_heads = {}

-- The art has a column per era and a row per face: pleased, neutral and
-- angry. A LeaderPortrait (the Foreign Advisor's) picks the face; a Player or
-- Civilization is shown neutral.
function leader_heads:map_object_to_sprite(player_or_civ)
  local era_index = 0
  local path = ""
  local row = 1
  local type_name = player_or_civ:GetType().Name
  if (type_name == "Civilization") then
    era_index = 0
    path = player_or_civ.leaderArtFile
  elseif (type_name == "Player") then
    era_index = player_or_civ.EraIndex()
    path = player_or_civ.civilization.leaderArtFile
  elseif (type_name == "LeaderPortrait") then
    era_index = player_or_civ.player.EraIndex()
    path = player_or_civ.player.civilization.leaderArtFile
    row = player_or_civ.row
  else
    error "Expected a Player, Civilization or LeaderPortrait object"
  end

  local xOffset = era_index * IMAGE_SIZE
  local yOffset = row * IMAGE_SIZE

  return {
    path = path,
    crop_region = { xOffset, yOffset, IMAGE_SIZE, IMAGE_SIZE },
    shadows = false,
    transparent_color_indexes = { 255 },
  }
end

return leader_heads
