-- Small fixes that take chores out of the game without changing its challenge, each with its own setting.
local Foundation = require("AincradFoundation")
local mod = Foundation.new("SmoothSailing")

local CONFIRM_WINDOW = "/Game/ROD/Widget/AvatarCustomize/GameModeSelectWindow/WBP_GameModeSelect_ConfirmWindow.WBP_GameModeSelect_ConfirmWindow_C"
local STRINGS = "/Game/ROD/DataAssets/StringTable/ST_GeneralLocalizeList.ST_GeneralLocalizeList"
-- The game's own one-line Death Game confirmation, already translated into every language it ships.
local SHORT_TEXT = "GenericMessage_NameEntry_0009"
-- Message windows with only a Close button, by their text in the game's string table.
local NOTICES = {
    GenericMessage_System_0000 = "autosave notice",
    GenericMessage_NameEntry_0001 = "name notice",
}
local CHECK_MS = 5
-- Windows fill in their texts within a few frames of being made.
local GIVE_UP_MS = 2000
local CLOSE_GIVE_UP_MS = 3000
-- EInputButtonKindInWidget::Virtual_Accept, EInputClickType::Single and EMenuWidgetState::Active.
local ACCEPT = 1
local SINGLE_CLICK = 1
local ACTIVE = 2

local function gameText(key)
    local texts = StaticFindObject("/Script/Engine.Default__KismetTextLibrary")
    return texts:TextFromStringTable(FName(STRINGS), key)
end

local function settingOn(setting)
    mod.refreshSettings()
    return not mod.failed and mod.settings.ENABLED and mod.settings[setting]
end

-- Runs check on the game thread every few milliseconds until it returns true or the window is gone. check gets the
-- milliseconds waited so far.
local function poll(window, check)
    local done, waited = false, 0
    LoopAsync(CHECK_MS, function()
        waited = waited + CHECK_MS
        ExecuteInGameThread(function()
            if not done then
                done = not window:IsValid() or check(waited)
            end
        end)
        return done
    end)
end

-- The window's parts aren't Blueprint variables, so they're found by name inside its widget tree.
local function part(window, class, name)
    local tree = window:GetFullName():match("%S+$") .. ".WidgetTree"
    for _, widget in ipairs(FindAllOf(class) or {}) do
        local path = widget:GetFullName():match("%S+$")
        if path:sub(1, #tree) == tree and path:sub(#tree + 1):match("^_%d+%.([%w_]+)$") == name then
            return widget
        end
    end
end

-- Keeps the warning box with its two points about lost saves and system stability, and drops the tips.
local function shorten(window, heading)
    heading:SetText(gameText(SHORT_TEXT))
    for _, found in ipairs({ { "RichTextBlock", "ComfirmTitle2" }, { "RichTextBlock", "ComfirmText2" },
        { "UserWidget", "KeyAssignScrollUp" }, { "UserWidget", "KeyAssignScrollDown" } }) do
        local widget = part(window, found[1], found[2])
        if widget then
            widget:SetVisibility(1)
        end
    end
    -- Both answers stay locked until the text has been scrolled to the end. The window only notices that through
    -- its own scroll function, and only once the shorter text has been laid out.
    ExecuteWithDelay(300, function()
        ExecuteInGameThread(function()
            if window:IsValid() then
                window:AddScrollOffset(100000)
            end
        end)
    end)
end

NotifyOnNewObject(CONFIRM_WINDOW, function(window)
    mod.refreshSettings()
    if mod.failed or not mod.settings.ENABLED then
        return
    end
    if not mod.settings.SHORT_DEATH_GAME_WARNING then
        window.ScrollSpeed = mod.settings.WARNING_SCROLL_SPEED
        return
    end
    -- Changed before the opening animation shows the long version.
    poll(window, function(waited)
        local heading = part(window, "RichTextBlock", "MessageText2")
        if heading and not heading:GetText():ToString():find("^W+$") then
            shorten(window, heading)
            mod.debug("Death Game warning shortened")
            return true
        end
        if waited >= GIVE_UP_MS then
            mod.fail("the Death Game warning's layout wasn't found, so a game update may have changed it")
            return true
        end
        return false
    end)
end)

-- Clicks the confirm button once, as the game's input sends it, so whatever waits on the answer gets it. Closing the
-- window any other way leaves the game waiting. Only once, and only when the window takes input: a second click goes
-- through to whatever opens next, such as the difficulty and mode choices.
local function close(window, what)
    local clicked = false
    poll(window, function(waited)
        if not window:IsInViewport() then
            mod.debug(what .. " skipped")
            return true
        end
        if waited >= CLOSE_GIVE_UP_MS then
            mod.log(what .. " didn't close, so it was left for you to close")
            return true
        end
        if not clicked and window:GetMenuWidgetState() == ACTIVE then
            clicked = true
            window:ClickEventNotify(window, ACCEPT, SINGLE_CLICK)
        end
        return false
    end)
end

local noticeTexts

NotifyOnNewObject("/Script/ROD.RODMessageWindowWidget", function(window)
    if not settingOn("SKIP_NOTICES") then
        return
    end
    if not noticeTexts then
        noticeTexts = {}
        for key, what in pairs(NOTICES) do
            noticeTexts[gameText(key):ToString()] = what
        end
    end
    poll(window, function(waited)
        -- Some windows built on the message window, like the mode choice, have no message text of their own.
        local text = window.MessageText
        if type(text) ~= "userdata" or not text:IsValid() then
            return true
        end
        -- Its text isn't set up until it's on screen, and reading it earlier gives garbage.
        if not window:IsInViewport() then
            return waited >= GIVE_UP_MS
        end
        local shown = text:GetText():ToString()
        if noticeTexts[shown] then
            close(window, noticeTexts[shown])
        else
            mod.debug("message window, not a notice: " .. shown:sub(1, 60))
        end
        return true
    end)
end)

mod.log("loaded")
