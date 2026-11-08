-- Small fixes that take chores out of the game without changing its challenge, each with its own setting.
local Foundation = require("AincradFoundation")
local mod = Foundation.new("SmoothSailing")

local scripts = debug.getinfo(1, "S").source:gsub("^@", ""):gsub("[^/\\]+$", "")

local CONFIRM_WINDOW = "/Game/ROD/Widget/AvatarCustomize/GameModeSelectWindow/WBP_GameModeSelect_ConfirmWindow.WBP_GameModeSelect_ConfirmWindow_C"
local STRINGS = "/Game/ROD/DataAssets/StringTable/ST_GeneralLocalizeList.ST_GeneralLocalizeList"
-- The game's own one-line Death Game confirmation, already translated into every language it ships.
local SHORT_TEXT = "GenericMessage_NameEntry_0009"
-- Kept in the mod's folder, so a fresh install shows the full warning again.
local READ_FILE = scripts .. "death-game-warning-read.txt"
local CHECK_MS = 5
-- The window fills in its texts within a few frames of being made. Past this, its layout has changed.
local GIVE_UP_MS = 2000

local function warningRead()
    local file = io.open(READ_FILE, "rb")
    if file then
        file:close()
        return true
    end
    return false
end

local function markWarningRead()
    local file = io.open(READ_FILE, "wb")
    if file then
        file:write("The full Death Game warning has been read. Delete this file to see it again.\n")
        file:close()
        mod.log("full Death Game warning read, the short one is shown from now on")
    end
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
    local texts = StaticFindObject("/Script/Engine.Default__KismetTextLibrary")
    heading:SetText(texts:TextFromStringTable(FName(STRINGS), SHORT_TEXT))
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

-- Neither answer can be given before scrolling to the end, so the window closing means it was read.
local function watchFullWarning(window)
    local closed = false
    LoopAsync(500, function()
        ExecuteInGameThread(function()
            if not closed and not (window:IsValid() and window:IsInViewport()) then
                closed = true
                markWarningRead()
            end
        end)
        return closed
    end)
end

NotifyOnNewObject(CONFIRM_WINDOW, function(window)
    mod.refreshSettings()
    if mod.failed or not mod.settings.ENABLED then
        return
    end
    if not (mod.settings.SHORT_DEATH_GAME_WARNING and warningRead()) then
        window.ScrollSpeed = mod.settings.WARNING_SCROLL_SPEED
        mod.debug("full Death Game warning, scroll speed " .. mod.settings.WARNING_SCROLL_SPEED)
        if not warningRead() then
            watchFullWarning(window)
        end
        return
    end
    -- Changed before the opening animation shows the long version.
    local done, waited = false, 0
    LoopAsync(CHECK_MS, function()
        waited = waited + CHECK_MS
        ExecuteInGameThread(function()
            if done or not window:IsValid() then
                done = true
                return
            end
            local heading = part(window, "RichTextBlock", "MessageText2")
            if heading and not heading:GetText():ToString():find("^W+$") then
                done = true
                shorten(window, heading)
                mod.debug("Death Game warning shortened")
            elseif waited >= GIVE_UP_MS then
                done = true
                mod.fail("the Death Game warning's layout wasn't found, so a game update may have changed it")
            end
        end)
        return done
    end)
end)

mod.log("loaded")
