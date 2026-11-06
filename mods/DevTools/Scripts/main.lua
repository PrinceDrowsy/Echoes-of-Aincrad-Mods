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

local MORPH_VALUES = { 1, 2, 3, -1, 0 }
local BUILD_MORPHS = { "BS_BOD_Belly", "BS_BOD_Butt", "BS_BOD_Hips", "BS_BOD_Thighs", "BS_BOD_Arms", "BS_BOD_ForeArms", "BS_BOD_Legs" }
local morphStep = 0

local function logAppearance(hero)
    local look = hero:GetAvatarData().AppearanceData
    log(string.format("appearance belly=%s butt=%s hips=%s thighs=%s arms=%s forearms=%s legs=%s scale=%s",
        look.Belly, look.Butts, look.Hips, look.Thighs, look.Arms, look.ForeArms, look.Legs, look.MeshScale))
end

-- Only changes the live meshes, so nothing here reaches the save.
local function setMorphs(names)
    local hero = FindFirstOf("RODHeroCharacter")
    if not (hero and hero:IsValid()) then
        log("no hero")
        return
    end
    logAppearance(hero)
    morphStep = morphStep % #MORPH_VALUES + 1
    local value = MORPH_VALUES[morphStep]
    local count = 0
    for _, mesh in ipairs(FindAllOf("SkeletalMeshComponent") or {}) do
        if mesh:IsValid() and mesh:GetOwner():GetAddress() == hero:GetAddress() then
            for _, name in ipairs(names) do
                mesh:SetMorphTarget(FName(name), value, false)
            end
            count = count + 1
        end
    end
    log("morph value " .. value .. " on " .. count .. " meshes")
end

bind(Key.F10, "belly morph", function()
    setMorphs({ "BS_BOD_Belly" })
end)

bind(Key.F11, "build morphs", function()
    setMorphs(BUILD_MORPHS)
end)

-- Cheats on Ctrl+Shift, through the game's real functions, since many of its debug leftovers do nothing in
-- this build. They write to the save, and the game has anti-cheat for online play, so solo only.
local COL = 10000
local EXP = 5000
-- Category is the EItemCategory number Cardinal shows for the item (33 is healing items).
local GIVE_ITEM = { Category = 33, ItemId = 1, Num = 5 }

local function cheat(key, label, action)
    RegisterKeyBind(key, { ModifierKey.CONTROL, ModifierKey.SHIFT }, function()
        ExecuteInGameThread(function()
            local state = FindFirstOf("RODPlayerState")
            if not (state and state:IsValid()) then
                log(label .. ": not in game")
                return
            end
            log(label)
            action(state)
        end)
    end)
end

cheat(Key.ONE, "add " .. COL .. " col", function(state)
    state:AddCol(COL)
end)

cheat(Key.TWO, "give item " .. GIVE_ITEM.Category .. "/" .. GIVE_ITEM.ItemId .. " x" .. GIVE_ITEM.Num, function()
    local items = FindFirstOf("RODAvatarItemManager")
    -- 4 is EExtraModificationLotteryType::ForceGrant.
    items:ServerAddChestItemsItemDataBase({ { Category = GIVE_ITEM.Category, ItemId = GIVE_ITEM.ItemId,
        Num = GIVE_ITEM.Num, HealHp = 0, HealSp = 0 } }, true, 4)
end)

cheat(Key.THREE, "add " .. EXP .. " exp", function(state)
    log("levelled up=" .. tostring(state:CalcHeroLevelUp(EXP)))
end)

cheat(Key.FOUR, "toggle noclip", function()
    local manager = FindFirstOf("RODCheatManager")
    if not (manager and manager:IsValid()) then
        log("no cheat manager in this build")
        return
    end
    manager:ToggleNoClip()
end)

-- dev.ps1 bench times the gap between the first two marks. Works without being in game, so it can start a load.
RegisterKeyBind(Key.F12, { ModifierKey.CONTROL }, function()
    log("mark")
end)

log("loaded")
