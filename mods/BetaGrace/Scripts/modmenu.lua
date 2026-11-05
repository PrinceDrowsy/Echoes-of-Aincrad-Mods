return {
    label = "Beta Grace",
    summary = "Dying during the beta won't delete your Death Game save. Turn off to restore the risk.",
    apply = "live",
    settings = {
        { key = "ENABLED", label = "Protect Death Game saves during the beta", type = "bool" },
        { key = "DEBUG_LOGS", label = "Extra logging", type = "bool" },
    },
}
