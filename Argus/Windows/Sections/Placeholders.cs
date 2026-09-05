namespace Argus.Windows.Sections;

// Pages that land in later phases. Kept as real sections so navigation is complete from the first build.

internal static class VesselsSection
{
    public static void Draw(Plugin plugin)
    {
        MainWindow.PageHeader("Vessels", "Builds, stats and EXP progress per vessel.");
        Styling.Text("Coming in the next phase.", Styling.TextMuted);
    }
}

internal static class PlannerSection
{
    public static void Draw(Plugin plugin)
    {
        MainWindow.PageHeader("Planner", "Suggested routes around the vessel's rank, range, fuel and unlocks.");
        Styling.Text("Coming in the next phase.", Styling.TextMuted);
    }
}

internal static class BuilderSection
{
    public static void Draw(Plugin plugin)
    {
        MainWindow.PageHeader("Builder", "Best part set for a target route or rank within airframe capacity.");
        Styling.Text("Coming in a later phase.", Styling.TextMuted);
    }
}

internal static class LootSection
{
    public static void Draw(Plugin plugin)
    {
        MainWindow.PageHeader("Loot", "What each voyage brought back, by sector and build.");
        Styling.Text("Coming in a later phase.", Styling.TextMuted);
    }
}
