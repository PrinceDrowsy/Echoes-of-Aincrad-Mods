-- Hotkeys over the game's leftover debug functions. For throwaway characters: these can advance
-- quests and write progress into the loaded save. Output goes to UE4SS.log.
local TAG = "[DevTools] "
local START_QUEST = 10005

local function log(text)
    print(TAG .. text .. "\n")
end

local function bind(key, label, action)
    RegisterKeyBind(key, { ModifierKey.CONTROL }, function()
        ExecuteInGameThread(function()
            local pc = FindFirstOf("RODInGamePlayerController")
            if not (pc and pc:IsValid()) then
                log(label .. ": not in game")
                return
            end
            log(label)
            action(pc)
        end)
    end)
end

bind(Key.F5, "state", function(pc)
    local quest = FindFirstOf("RODQuestManager")
    local game = FindFirstOf("RODGameState")
    local avatar = FindFirstOf("RODAvatarCharacter")
    log("quest=" .. tostring(quest and quest:IsValid() and quest:GetQuestID())
        .. " deathGame=" .. tostring(game and game:IsValid() and game:IsDeathGameMode())
        .. " dead=" .. tostring(avatar and avatar:IsValid() and avatar:IsDead()))
    log("map=" .. pc:GetWorld():GetFullName())
end)

bind(Key.F8, "start quest " .. START_QUEST, function(pc)
    pc:DebugQuestStart(START_QUEST, true)
end)

bind(Key.F9, "complete current quest", function(pc)
    pc:DebugQuestSuccess()
end)

log("loaded")
