-- Writes the game's type mappings (.usmap) for Cardinal, once per game version.
local Foundation = require("AincradFoundation")
local mod = Foundation.new("CardinalMappings")

local folder = debug.getinfo(1, "S").source:gsub("^@", ""):gsub("[^/\\]+$", "")
local STAMP = folder .. "dumped.txt"
local EXE = folder .. "../../../../EchoesofAincrad-Win64-Shipping.exe"
-- Classes keep loading for a while after start-up, and a dump taken too early would miss them.
local DELAY_MS = 15000

local function readFile(path)
    local file = io.open(path, "rb")
    if not file then
        return nil
    end
    local text = file:read("*a")
    file:close()
    return text
end

-- The exe's size stands in for the game version. It's far cheaper than hashing, but an update
-- that kept the exact size would be missed.
local function gameStamp()
    local file = io.open(EXE, "rb")
    if not file then
        return nil
    end
    local size = file:seek("end")
    file:close()
    return tostring(size)
end

local stamp = gameStamp()
if not stamp then
    mod.log("couldn't find the game exe, so game updates won't trigger a new dump")
    stamp = "unknown"
end

if readFile(STAMP) == stamp then
    mod.debug("mappings are current")
    return
end

ExecuteWithDelay(DELAY_MS, function()
    ExecuteInGameThread(function()
        DumpUSMAP()
        mod.log("wrote new mappings for Cardinal")
        local file = io.open(STAMP, "wb")
        if not file then
            mod.log("couldn't save " .. STAMP .. ", so the mappings will be written again next launch")
            return
        end
        file:write(stamp)
        file:close()
    end)
end)
