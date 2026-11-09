return {
    label = "Smooth Sailing",
    summary = "Small fixes that take chores out of the game without changing its challenge.",
    apply = "live",
    settings = {
        { key = "ENABLED", label = "Smooth Sailing on", type = "bool" },
        { key = "SHORT_DEATH_GAME_WARNING", label = "Short Death Game warning", type = "bool" },
        { key = "SKIP_NOTICES", label = "Skip the autosave and name notices", type = "bool" },
        { key = "WARNING_SCROLL_SPEED", label = "Full Death Game warning scroll speed", type = "number", min = 20,
            max = 200, step = 10 },
        { key = "DEBUG_LOGS", label = "Extra logging", type = "bool" },
    },
}
