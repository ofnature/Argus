using System.Collections.Generic;

namespace Argus.Core.Data;

/// <summary>
/// Airship sector model. Unlock chain and slot unlocks: Free Company Airships (consolegameswiki) cross-checked with
/// submarine.girin.dev and the community "Airship Log Data" sheet. Surveillance tiers and the favor ("Luck") line
/// per sector: submarine.girin.dev/en/airship (fetched 2026-09-05). The EXP rating model is a fit over 3,430 voyage
/// legs from the community log — see <see cref="ExpectedRating"/>. Keys are AirshipExplorationPoint row ids
/// (sector number − 1); row 22 is the Diadem and never appears here.
/// </summary>
public static class AirshipData
{
    public const uint StartRow = 127;

    /// <summary>The airship EXP bonus model is a statistical fit, not a data-mined rule. Surfaced as a badge in the UI.</summary>
    public const bool RatingIsEstimate = true;

    /// <summary>
    /// A leg's performance rating (Abysmal 0, Substandard 1, Satisfactory 2, Decent 3) adds +50% EXP per step.
    /// Confirmed on the voyage log: bonus EXP = Σ base × 0.5 × rating.
    /// </summary>
    public const double BonusPerRatingStep = 0.5;

    public const int MaxRating = 3;

    /// <summary>Surveillance needed for the tier-2 / tier-3 loot pools and the favor ("Luck") line, per sector.</summary>
    public sealed record LootTiers(int T2, int T3, int Favor);

    public static readonly Dictionary<uint, LootTiers> Tiers = new()
    {
        { 0,  new LootTiers(46, 54, 0) },
        { 1,  new LootTiers(46, 54, 0) },
        { 2,  new LootTiers(46, 54, 0) },
        { 3,  new LootTiers(46, 54, 0) },
        { 4,  new LootTiers(46, 54, 0) },
        { 5,  new LootTiers(46, 58, 0) },
        { 6,  new LootTiers(46, 58, 46) },
        { 7,  new LootTiers(46, 58, 46) },
        { 8,  new LootTiers(46, 58, 46) },
        { 9,  new LootTiers(46, 60, 46) },
        { 10, new LootTiers(46, 60, 46) },
        { 11, new LootTiers(46, 62, 46) },
        { 12, new LootTiers(46, 62, 46) },
        { 13, new LootTiers(46, 64, 46) },
        { 14, new LootTiers(46, 64, 46) },
        { 15, new LootTiers(46, 66, 46) },
        { 16, new LootTiers(0, 90, 74) },
        { 17, new LootTiers(0, 90, 74) },
        { 18, new LootTiers(0, 90, 74) },
        { 19, new LootTiers(0, 90, 74) },
        { 20, new LootTiers(0, 90, 74) },
        { 21, new LootTiers(0, 90, 74) },
        { 23, new LootTiers(0, 90, 74) },
        { 24, new LootTiers(0, 90, 74) },
    };

    /// <summary>
    /// Mean rating observed per Retrieval bucket in the community log (legs on EXP-giving sectors). Ratings follow the
    /// retrieval tier of the gathered items (Normal → mostly Abysmal, High → Substandard/Satisfactory, Optimal →
    /// Satisfactory/Decent) with a weather roll on top, so this is an expectation, never a guarantee.
    /// </summary>
    public static double ExpectedRating(int retrieval) => retrieval switch
    {
        < 80 => 0.80,
        < 90 => 0.93,
        < 100 => 1.20,
        < 120 => 1.30,
        _ => 1.80,
    };

    /// <summary>Which sector must be explored to unlock each sector. Rows 0 and 1 are open from the start.</summary>
    public static readonly Dictionary<uint, uint> UnlockedFrom = new()
    {
        { 2, 0 },   // C ← A
        { 3, 1 },   // D ← B
        { 4, 1 },   // E ← B
        { 5, 4 },   // F ← E
        { 6, 4 },   // G ← E
        { 7, 6 },   // H ← G   (exploring H registers airship #2)
        { 8, 7 },   // I ← H
        { 9, 6 },   // J ← G
        { 10, 5 },  // K ← F
        { 11, 9 },  // L ← J
        { 12, 10 }, // M ← K
        { 13, 10 }, // N ← K  (exploring N registers airship #3)
        { 14, 12 }, // O ← M
        { 15, 11 }, // P ← L
        { 16, 15 }, // Q ← P
        { 17, 16 }, // R ← Q  (exploring R registers airship #4)
        { 18, 14 }, // S ← O
        { 19, 18 }, // T ← S
        { 20, 19 }, // U ← T
        { 21, 19 }, // V ← T
        { 23, 18 }, // W ← S
        { 24, 20 }, // X ← U
    };

    /// <summary>Exploring these sectors registers an additional airship slot (2nd, 3rd, 4th).</summary>
    public static readonly Dictionary<uint, int> SlotUnlocks = new()
    {
        { 7, 2 },   // Sector 8
        { 13, 3 },  // Sector 14
        { 17, 4 },  // Sector 18
    };

    /// <summary>
    /// Progression order for "what to unlock next": the EXP sectors by rank first (they gate ranking up), then the
    /// slot-unlocking branches, then the rank-50 tail. Each entry is a sector to unlock; the sector to *visit* for it
    /// is <see cref="UnlockedFrom"/>.
    /// </summary>
    public static readonly uint[] MainLine =
    {
        2, 3, 4, 5, 6, 7,       // C D E F G H  (H = airship #2)
        8, 9, 10, 11, 12, 13,   // I J K L M N  (N = airship #3)
        14, 15, 16, 17,         // O P Q R      (R = airship #4)
        18, 19, 20, 21, 23, 24, // S T U V W X
    };
}
