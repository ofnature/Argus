namespace Argus.Windows.Sections;

// Pages that land in later phases. Kept as real sections so navigation is complete from the first build.

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
