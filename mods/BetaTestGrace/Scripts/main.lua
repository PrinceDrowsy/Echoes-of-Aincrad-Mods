local TAG = "[BetaTestGrace] "

local function log(text)
    print(TAG .. text .. "\n")
end

local function state()
    local quest = FindFirstOf("RODQuestManager")
    local game = FindFirstOf("RODGameState")
    local questId = quest and quest:IsValid() and quest:GetQuestID() or "none"
    local deathGame = "none"
    if game and game:IsValid() then
        deathGame = game:IsDeathGameMode()
    end
    return "quest=" .. tostring(questId) .. " deathGame=" .. tostring(deathGame)
end

local function trace(path, describe)
    local ok, err = pcall(RegisterHook, path, function(_, ...)
        log(path .. (describe and (" " .. describe(...)) or "") .. " | " .. state())
    end)
    log((ok and "hooked " or "NOT hooked ") .. path .. (ok and "" or (": " .. tostring(err))))
end

trace("/Script/ROD.RODWorldGameState:CheckGameOver", function(noDirection)
    return "noDirection=" .. tostring(noDirection:get())
end)
trace("/Script/ROD.RODInGamePlayerController:ClientShowGameOver")
trace("/Script/ROD.RODInGamePlayerController:ShowGameOver")
trace("/Script/ROD.RODInGamePlayerController:EndGameOver")
trace("/Script/ROD.RODSaveLoadSubsystem:RemoveCharacterData", function(index)
    return "index=" .. tostring(index:get())
end)
trace("/Script/ROD.RODSaveDataSelectMenuWidgetBase:OnCompletedRemoveSaveData")

local FIRST_BETA_QUEST = 10001

-- The config object can be created after this script starts, and could be recreated later.
local function exemptBetaQuests()
    local config = FindFirstOf("RODGameConfig")
    if not (config and config:IsValid()) then
        return
    end
    local ignored = config.IgnoreDeathGameModeQuests
    local listed = {}
    ignored:ForEach(function(_, id)
        listed[id:get()] = true
    end)
    local added = false
    for id = FIRST_BETA_QUEST, config.BetaTestLastQuestID do
        if not listed[id] then
            ignored[#ignored + 1] = id
            added = true
        end
    end
    if added then
        local ids = {}
        ignored:ForEach(function(_, id)
            ids[#ids + 1] = tostring(id:get())
        end)
        log("IgnoreDeathGameModeQuests=[" .. table.concat(ids, ",") .. "]")
    end
end

local last
LoopAsync(5000, function()
    ExecuteInGameThread(function()
        exemptBetaQuests()
        local current = state()
        if current ~= last then
            log(current)
            last = current
        end
    end)
    return false
end)
