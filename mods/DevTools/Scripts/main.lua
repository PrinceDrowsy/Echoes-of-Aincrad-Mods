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

local CAN_SWITCH = { [0] = "OK", "FailAttacking", "FailNoPartner", "FailNoTarget", "FailVoiceWait", "FailCantAction", "FailCantAttack" }
bind(Key.F6, "switch", function()
    local hero = FindFirstOf("RODHeroCharacter")
    if not (hero and hero:IsValid()) then
        log("no hero")
        return
    end
    local result = hero:CanSwitch()
    log("CanSwitch=" .. tostring(CAN_SWITCH[result] or result))
    if result == 0 then
        hero:PlaySwitchAction()
    end
end)

bind(Key.F7, "chat message", function()
    local cockpit = FindFirstOf("RODCockpitWidgetBase")
    if not (cockpit and cockpit:IsValid()) then
        log("no cockpit")
        return
    end
    cockpit:OnChatMessageReceivedDelegate({
        UniqueID = 900001,
        MessageLogKind = 0,
        Message = FText("Papa, this message came from a mod!"),
        Sender = FText("Yui"),
        Address = FText(""),
        ChatTargetKind = 0,
        bUnread = true,
    })
end)

bind(Key.F8, "start quest " .. START_QUEST, function(pc)
    pc:DebugQuestStart(START_QUEST, true)
end)

bind(Key.F9, "complete current quest", function(pc)
    pc:DebugQuestSuccess()
end)

log("loaded")
