-- Adds the beta quests to the game's own list of quests exempt from Death Game.
local Foundation = require("AincradFoundation")
local mod = Foundation.new("BetaGrace")

local FIRST_BETA_QUEST = 10001
local CHECK_MS = 5000

local config
local added = {}
local protecting = false

local function setProtected(wanted)
    local list = config.IgnoreDeathGameModeQuests
    local listed = {}
    list:ForEach(function(_, id)
        listed[#listed + 1] = id:get()
    end)

    if wanted then
        local present = {}
        for _, id in ipairs(listed) do
            present[id] = true
        end
        for id = FIRST_BETA_QUEST, config.BetaTestLastQuestID do
            if not present[id] then
                list[#list + 1] = id
                added[id] = true
            end
        end
    else
        list:Empty()
        for _, id in ipairs(listed) do
            if not added[id] then
                list[#list + 1] = id
            end
        end
        added = {}
    end

    protecting = wanted
    mod.log(wanted and "protecting quests " .. FIRST_BETA_QUEST .. "-" .. config.BetaTestLastQuestID or "protection off")
    local ids = {}
    list:ForEach(function(_, id)
        ids[#ids + 1] = tostring(id:get())
    end)
    mod.debug("exempt quests: " .. table.concat(ids, ","))
end

-- The config object can appear after this script starts, and a stale reference needs finding again.
local function findConfig()
    config = FindFirstOf("RODGameConfig")
    if not (config and config:IsValid()) then
        config = nil
        return
    end
    added = {}
    protecting = false
    local last = config.BetaTestLastQuestID
    if type(last) ~= "number" or last < FIRST_BETA_QUEST or not config.IgnoreDeathGameModeQuests:IsValid() then
        mod.fail("the beta quest settings weren't found, so a game update may have changed them")
    end
end

LoopAsync(CHECK_MS, function()
    ExecuteInGameThread(function()
        mod.refreshSettings()
        if mod.failed then
            return
        end
        if not (config and config:IsValid()) then
            findConfig()
        end
        if config and not mod.failed and protecting ~= mod.settings.ENABLED then
            setProtected(mod.settings.ENABLED)
        end
    end)
    return false
end)
