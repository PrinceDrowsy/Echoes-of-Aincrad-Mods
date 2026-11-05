-- Logging, Mod Menu settings and clear failure for every mod. dev.ps1 copies this into each mod's Scripts folder.
local Foundation = {}

local function readFile(path)
    local file = io.open(path, "rb")
    if not file then
        return nil
    end
    local text = file:read("*a")
    file:close()
    return text
end

-- Settings files are data, so they load with an empty environment and can't reach anything.
local function readTable(path)
    local text = readFile(path)
    if not text then
        return nil
    end
    local chunk = load(text, "@" .. path, "t", {})
    if not chunk then
        return nil
    end
    local ok, result = pcall(chunk)
    return ok and type(result) == "table" and result or nil
end

function Foundation.new(name)
    local scripts = debug.getinfo(2, "S").source:gsub("^@", ""):gsub("[^/\\]+$", "")
    local mod = { settings = {}, failed = false }
    local revision

    function mod.log(text)
        print("[" .. name .. "] " .. text .. "\n")
    end

    function mod.debug(text)
        if mod.settings.DEBUG_LOGS then
            mod.log(text)
        end
    end

    -- Stops the mod's features for this session and says why. Showing this in the game isn't done yet.
    function mod.fail(reason)
        if not mod.failed then
            mod.failed = true
            mod.log("TURNED OFF: " .. reason)
        end
    end

    -- Returns true when the settings changed. Mod Menu holds a lock while it replaces runtime.lua,
    -- and an overlay value of the wrong type is ignored so a bad file can't break the mod.
    function mod.refreshSettings()
        if readFile(scripts .. "runtime.lua.lock") then
            return false
        end
        local current = readFile(scripts .. "runtime.rev") or ""
        if current == revision then
            return false
        end
        revision = current
        local settings = readTable(scripts .. "config.lua")
        if not settings then
            mod.fail("config.lua is missing or invalid")
            return false
        end
        for key, value in pairs(readTable(scripts .. "runtime.lua") or {}) do
            if type(value) == type(settings[key]) then
                settings[key] = value
            end
        end
        mod.settings = settings
        return true
    end

    mod.refreshSettings()
    return mod
end

return Foundation
