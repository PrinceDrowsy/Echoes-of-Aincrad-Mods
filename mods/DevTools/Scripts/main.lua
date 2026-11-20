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
    local received = {}
    game.QuestReceiveList:ForEach(function(_, id)
        table.insert(received, id:get())
    end)
    log("received quests=" .. table.concat(received, ","))
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
-- Category is the EItemCategory number Cardinal shows for the item. Potions are usables (1); heal (33) is the
-- crystal pickup, which never goes into a chest.
local GIVE_ITEM = { Category = 1, ItemId = 1, Num = 5 }
-- A playable quest is always a QST_Free_ number: 1NNNN for main quests, 2NNNN for side quests.
local GIVE_QUEST = 20001
-- A side story from DA_SubStoryManage and the step to set it to. Its giver's conversation sets 2022 to 2000 when it
-- hands out quest 20050.
local SET_SIDE_STORY = { ID = "2022", Progress = 2000 }
-- A MainStory_ step. Town warping opens at 29000, but a step's opening event can lock the gate until it ends
-- (TargetDenyGimmicks). 33000 is the first step after that with no lock and no main quest running.
local SET_STORY = 33000
-- The hand mirror scene runs as step 23000's opening event and ends in the character creator.
local MIRROR_STORY = 23000

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

-- What an NPC handing out a side quest does: adds it to the received list.
cheat(Key.FIVE, "receive quest " .. GIVE_QUEST, function()
    FindFirstOf("RODInGamePlayerController"):ServerAddQuestList({ GIVE_QUEST }, {})
end)

local function setStory(step)
    local game = FindFirstOf("RODGameState")
    log("story progress was " .. game.MainStoryProgress)
    FindFirstOf("RODInGamePlayerController"):ServerDebugSetMainStoryProgress(step)
    log("story progress is " .. game.MainStoryProgress)
end

-- Skips the story ahead, which can leave quests and events out of step, so throwaway saves only.
cheat(Key.SIX, "set story progress " .. SET_STORY, function()
    setStory(SET_STORY)
end)

-- The event only starts when a save loads at that step, so return to the title and load again after this.
cheat(Key.NINE, "set story to the hand mirror scene", function()
    setStory(MIRROR_STORY)
end)

-- Experiment for Two Faces: loads the creator's level directly, as the mirror event's ChangeLevel does. The
-- creator moves the story to 24000 when the look is confirmed and to 25000 after the tunnel, so the step it was opened
-- from is kept to go back to.
local returnStory

-- The world's game state isn't there in the title or creator levels, but the game instance outlives every level.
local function savedStory()
    local instance = FindFirstOf("RODGameInstance")
    return instance and instance:IsValid() and instance:GetMainStoryProgress()
end

-- The playing character's entry in the save data in memory, which the save subsystem keeps across every level.
local function characterSave()
    local characters = FindFirstOf("RODSaveLoadSubsystem").CurrentSaveGame.CharacterSaveData
    local slot = FindFirstOf("RODGameInstance").CurrentCharacterSlotIndex
    characters:ForEach(function(index, character)
        log("save slot " .. index - 1 .. " story " .. character:get().MainStoryProgress
            .. (index - 1 == slot and ", playing" or ""))
    end)
    return characters[slot + 1]
end

cheat(Key.ZERO, "open the character creator level", function(state)
    returnStory = FindFirstOf("RODGameState").MainStoryProgress
    log("story progress is " .. returnStory .. ", saved " .. characterSave().MainStoryProgress)
    -- A candidate for where the beta model's look comes from, since HeroData's initial avatar isn't it.
    local default = FindFirstOf("RODSaveLoadSubsystem").DefaultUserInfo
    if default:IsValid() then
        local look = default:GetAvatarData()
        log("default user info head gear " .. look.AvatarPartsData.HeadGearID .. " pupil " .. look.AppearanceData.Pupil)
    else
        log("no default user info")
    end
    local statics = StaticFindObject("/Script/Engine.Default__GameplayStatics")
    statics:OpenLevel(state, FName("/Game/ROD/Maps/Main/ROD/PL_CharacterMake"), true, "")
end)

-- The creator's story moves happen in native code, and the creator level has no game state, but the save data in
-- memory outlives every level. StartNextQuest loads the new step's story asset, and the world takes its quest from
-- that asset, so both the step and the asset are set back once it has loaded, before the world does.
RegisterHook("/Script/ROD.RODAvaterCustomizeFlowManager:OnMainStoryChangedDelegate", function()
    if not returnStory then
        return
    end
    local instance = FindFirstOf("RODGameInstance")
    local character = characterSave()
    log("saved story " .. character.MainStoryProgress .. ", story asset " .. instance:GetMainStoryAsset():GetFullName())
    character.MainStoryProgress = returnStory
    local name = "MainStory_" .. returnStory
    instance.MainStoryAsset = LoadAsset("/Game/ROD/DataAssets/Story/Main/" .. name .. "." .. name)
    log("set back to " .. returnStory .. ", story asset " .. instance:GetMainStoryAsset():GetFullName()
        .. ", game instance story " .. tostring(savedStory()))
    returnStory = nil
end)

-- Until GameConfig.InitialAvatarProgress the game shows HeroData's initial avatar instead of the saved look, and the
-- data leaves it empty for the built-in default. Filling it with the look just made shows that as the beta avatar.
-- The creator level hasn't loaded HeroData, so it's loaded here, and filled again in the world in case the edit
-- didn't survive the level change.
local HERO_DATA = "/Game/ROD/DataAssets/Character/Avatar/AvatarData/HeroData.HeroData"
local betaLook = false

local function showBetaLook(where)
    local look = characterSave().AvatarData
    local hero = StaticFindObject(HERO_DATA)
    if not hero:IsValid() then
        log("loading hero data in the " .. where)
        hero = LoadAsset(HERO_DATA)
    end
    hero.InitialAvatarVoice = look.Voice
    hero.InitialAvatarPartsData = look.AvatarPartsData
    hero.InitialAvatarAppearanceData = look.AppearanceData
    log(string.format("%s: initial avatar head gear %s pupil %s, saved look head gear %s pupil %s", where,
        hero.InitialAvatarPartsData.HeadGearID, hero.InitialAvatarAppearanceData.Pupil,
        look.AvatarPartsData.HeadGearID, look.AppearanceData.Pupil))
end

RegisterHook("/Script/ROD.RODAvaterCustomizeFlowManager:FinishCreateAvatarDataBegin", function()
    if returnStory then
        betaLook = true
        showBetaLook("creator")
    end
end)

-- The story the world loads with, to see whether the step that was set back held, and the beta look filled in again.
NotifyOnNewObject("/Script/ROD.RODInGamePlayerController", function()
    log("world controller created at story " .. tostring(savedStory()))
    ExecuteInGameThread(function()
        -- Whatever the creator passed with the level change, such as the quest to start.
        log("level options " .. FindFirstOf("GameModeBase").OptionsString:ToString())
        if betaLook then
            showBetaLook("world")
        end
    end)
    if betaLook then
        -- Whether the filled-in HeroData is still the one in memory once the hero has spawned, or was collected and
        -- loaded again from disk.
        ExecuteWithDelay(10000, function()
            ExecuteInGameThread(function()
                local hero = StaticFindObject(HERO_DATA)
                log("hero data later: " .. (hero:IsValid() and "initial head gear "
                    .. hero.InitialAvatarPartsData.HeadGearID or "not loaded"))
            end)
        end)
    end
end)

-- A Flow graph (event, quest or tutorial script) to play on the spot, for testing changes to one without reaching
-- it in the story. Events play out of their story context, so throwaway saves only.
local PLAY_FLOW = "/Game/ROD/Flow/FlowGraphs/Event/PEV/pev_01_01_17/Flow_pev_01_01_17"

cheat(Key.E, "play " .. PLAY_FLOW:match("[^/]+$"), function()
    local flow = LoadAsset(PLAY_FLOW .. "." .. PLAY_FLOW:match("[^/]+$"))
    FindFirstOf("FlowSubsystem"):StartRootFlow(FindFirstOf("RODInGamePlayerController"), flow, true)
    -- Where the script got to: a talk step that never finishes stays active.
    ExecuteWithDelay(2000, function()
        ExecuteInGameThread(function()
            for _, talk in ipairs(FindAllOf("FlowNode_Talk_C") or {}) do
                log(string.format("%s: %s, text %s", talk:GetFullName(), tostring(talk.ActivationState),
                    talk.TextID:ToString()))
            end
        end)
    end)
end)

-- What a giver's conversation does besides handing out the quest. Throwaway saves only, like the story jump.
cheat(Key.EIGHT, "set side story " .. SET_SIDE_STORY.ID .. " to " .. SET_SIDE_STORY.Progress, function()
    FindFirstOf("RODInGamePlayerController"):ServerDebugSetSubStoryProgress(FName(SET_SIDE_STORY.ID), SET_SIDE_STORY.Progress)
end)

-- Dyeing unlocks through a side story step (RODGameConfig.ReleaseEquipmentColorChange). This sets the same release
-- flag, so the menu opens normally afterwards. Opening the palette before release crashes the game.
cheat(Key.SEVEN, "release outfit dyeing", function(state)
    log("ReleaseEquipmentChangeColor was " .. state.ReleaseEquipmentChangeColor)
    state.ReleaseEquipmentChangeColor = 1
    state:OnRep_ReleaseEquipmentChangeColor()
end)

-- dev.ps1 bench times the gap between the first two marks. Works without being in game, so it can start a load.
RegisterKeyBind(Key.F12, { ModifierKey.CONTROL }, function()
    log("mark")
end)

-- Which face part meshes are showing, to see how a part ID picks its mesh. Works in the creator, which has no player
-- controller.
RegisterKeyBind(Key.F3, { ModifierKey.CONTROL }, function()
    ExecuteInGameThread(function()
        for _, component in ipairs(FindAllOf("SkeletalMeshComponent") or {}) do
            local mesh = component.SkeletalMesh
            if mesh and mesh:IsValid() and mesh:GetFullName():find("/Humans/Heads/") then
                log("part " .. mesh:GetFullName():match("[^.]+$") .. " on " .. component:GetOuter():GetFullName():match("[^.:]+$"))
            end
        end
    end)
end)

-- Logs each step the title, name entry and creator flows start, to see where a New Game could open the creator.
local FLOW_STEPS = {
    RODOutGameFlowManager = { "Attention", "AboutAutoSave", "PlayerNameEntry", "CharaDataSave",
        "CheckChunk1InstallComplete", "AvatarCustomize", "PreTitleEvent", "Title", "PostTitleEvent", "GenerateSaveData",
        "CheckDemoSaveData" },
    RODPlayerNameEntryFlowManager = { "Headsup", "NameEntry", "DifficultySelect", "GameModeSelect", "PrologueSkip",
        "TransferMessage" },
    RODAvaterCustomizeFlowManager = { "CheckAvaterData", "LoadAvater", "ArgusLogo", "OpenAvaterCustomizeMenu",
        "CreateAvatarData", "FinishCreateAvatarData", "StartDirectionApplication", "SelectApplication",
        "StartDirectionPlayGame", "StartNextQuest" },
}

for class, steps in pairs(FLOW_STEPS) do
    for _, step in ipairs(steps) do
        RegisterHook("/Script/ROD." .. class .. ":" .. step .. "Begin", function()
            log("flow " .. class .. " " .. step .. " at story " .. tostring(savedStory()))
        end)
    end
end

-- When and from which story step the character creator takes over, for opening it at other points.
for _, class in ipairs({ "/Script/ROD.RODGameMode_AvatarCustomize", "/Script/ROD.RODAvaterCustomizeFlowManager",
    "/Script/ROD.RODPlayerNameEntryFlowManager" }) do
    NotifyOnNewObject(class, function(object)
        local game = FindFirstOf("RODGameState")
        log("created " .. object:GetFullName() .. " at story "
            .. tostring(game and game:IsValid() and game.MainStoryProgress))
    end)
end

log("loaded")
