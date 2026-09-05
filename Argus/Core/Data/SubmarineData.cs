using System.Collections.Generic;

namespace Argus.Core.Data;

/// <summary>
/// Submarine sector model ported from SubmarineTracker (MIT, Infi — https://github.com/Infiziert90/SubmarineTracker).
/// Breakpoints come from the community spreadsheet credited there. Keys are SubmarineExploration row ids.
/// Generated from the reference checkout; regenerate rather than editing by hand.
/// </summary>
public static class SubmarineData
{
    /// <summary>
    /// Per-sector thresholds: surveillance for the tier-2 / tier-3 loot pools, retrieval for Normal / Optimal yield,
    /// and the favor line. Optimal retrieval is the only guaranteed EXP bonus; the others are rolls.
    /// </summary>
    public sealed record Breakpoint(int T2, int T3, int Normal, int Optimal, int Favor)
    {
        public static readonly Breakpoint Empty = new(0, 0, 0, 0, 0);
    }

    public static readonly Dictionary<uint, Breakpoint> MapBreakpoints = new()
    {
        { 1, new Breakpoint(20, 80, 50, 80, 70) },
        { 2, new Breakpoint(20, 80, 50, 80, 70) },
        { 3, new Breakpoint(20, 85, 55, 85, 70) },
        { 4, new Breakpoint(20, 85, 55, 85, 70) },
        { 5, new Breakpoint(25, 90, 60, 90, 80) },
        { 6, new Breakpoint(25, 90, 60, 90, 80) },
        { 7, new Breakpoint(30, 95, 65, 95, 90) },
        { 8, new Breakpoint(30, 100, 70, 100, 90) },
        { 9, new Breakpoint(35, 110, 75, 105, 90) },
        { 10, new Breakpoint(50, 115, 80, 110, 90) },
        { 11, new Breakpoint(50, 90, 80, 110, 70) },
        { 12, new Breakpoint(55, 95, 90, 120, 80) },
        { 13, new Breakpoint(60, 100, 100, 130, 75) },
        { 14, new Breakpoint(60, 100, 100, 130, 85) },
        { 15, new Breakpoint(80, 115, 120, 160, 90) },
        { 16, new Breakpoint(60, 100, 100, 130, 85) },
        { 17, new Breakpoint(65, 105, 110, 140, 90) },
        { 18, new Breakpoint(85, 120, 135, 175, 95) },
        { 19, new Breakpoint(75, 110, 120, 155, 95) },
        { 20, new Breakpoint(90, 125, 140, 180, 100) },
        { 21, new Breakpoint(90, 120, 135, 175, 95) },
        { 22, new Breakpoint(105, 130, 140, 180, 100) },
        { 23, new Breakpoint(110, 140, 140, 180, 105) },
        { 24, new Breakpoint(120, 130, 145, 190, 105) },
        { 25, new Breakpoint(120, 135, 145, 190, 105) },
        { 26, new Breakpoint(135, 140, 150, 195, 110) },
        { 27, new Breakpoint(130, 145, 150, 195, 110) },
        { 28, new Breakpoint(130, 150, 155, 200, 120) },
        { 29, new Breakpoint(135, 150, 160, 200, 130) },
        { 30, new Breakpoint(140, 155, 170, 215, 135) },
        { 32, new Breakpoint(135, 150, 165, 205, 140) },
        { 33, new Breakpoint(140, 155, 170, 205, 140) },
        { 34, new Breakpoint(140, 160, 175, 210, 145) },
        { 35, new Breakpoint(145, 165, 180, 220, 145) },
        { 36, new Breakpoint(145, 160, 185, 220, 150) },
        { 37, new Breakpoint(145, 165, 180, 220, 145) },
        { 38, new Breakpoint(150, 170, 180, 220, 140) },
        { 39, new Breakpoint(160, 175, 190, 225, 150) },
        { 40, new Breakpoint(155, 170, 190, 220, 140) },
        { 41, new Breakpoint(160, 175, 190, 225, 150) },
        { 42, new Breakpoint(155, 170, 185, 230, 160) },
        { 43, new Breakpoint(160, 175, 185, 235, 165) },
        { 44, new Breakpoint(160, 170, 190, 240, 175) },
        { 45, new Breakpoint(165, 190, 195, 245, 170) },
        { 46, new Breakpoint(170, 185, 205, 250, 175) },
        { 47, new Breakpoint(165, 180, 185, 235, 165) },
        { 48, new Breakpoint(165, 180, 185, 235, 165) },
        { 49, new Breakpoint(170, 185, 190, 240, 165) },
        { 50, new Breakpoint(175, 190, 200, 250, 175) },
        { 51, new Breakpoint(180, 190, 200, 250, 175) },
        { 53, new Breakpoint(180, 190, 200, 250, 175) },
        { 54, new Breakpoint(180, 190, 200, 250, 175) },
        { 55, new Breakpoint(180, 190, 200, 250, 175) },
        { 56, new Breakpoint(180, 195, 205, 260, 178) },
        { 57, new Breakpoint(180, 195, 210, 260, 185) },
        { 58, new Breakpoint(180, 195, 210, 265, 185) },
        { 59, new Breakpoint(180, 195, 215, 270, 185) },
        { 60, new Breakpoint(180, 195, 220, 270, 185) },
        { 61, new Breakpoint(180, 195, 220, 270, 185) },
        { 62, new Breakpoint(180, 195, 220, 270, 185) },
        { 63, new Breakpoint(185, 200, 225, 275, 190) },
        { 64, new Breakpoint(185, 200, 230, 280, 190) },
        { 65, new Breakpoint(185, 200, 230, 280, 190) },
        { 66, new Breakpoint(190, 205, 235, 285, 195) },
        { 67, new Breakpoint(195, 210, 240, 290, 200) },
        { 68, new Breakpoint(195, 210, 245, 295, 200) },
        { 69, new Breakpoint(200, 215, 255, 300, 205) },
        { 70, new Breakpoint(205, 220, 255, 300, 210) },
        { 71, new Breakpoint(205, 220, 260, 305, 210) },
        { 72, new Breakpoint(205, 220, 260, 305, 210) },
        { 74, new Breakpoint(205, 220, 260, 305, 210) },
        { 75, new Breakpoint(205, 220, 260, 305, 210) },
        { 76, new Breakpoint(205, 220, 260, 305, 210) },
        { 77, new Breakpoint(210, 225, 265, 310, 215) },
        { 78, new Breakpoint(210, 225, 265, 310, 215) },
        { 79, new Breakpoint(210, 225, 265, 310, 215) },
        { 80, new Breakpoint(210, 225, 265, 310, 215) },
        { 81, new Breakpoint(215, 230, 270, 315, 220) },
        { 82, new Breakpoint(215, 230, 270, 315, 220) },
        { 83, new Breakpoint(215, 230, 270, 315, 220) },
        { 84, new Breakpoint(215, 230, 270, 315, 220) },
        { 85, new Breakpoint(215, 230, 270, 315, 220) },
        { 86, new Breakpoint(215, 230, 270, 315, 220) },
        { 87, new Breakpoint(220, 235, 275, 320, 225) },
        { 88, new Breakpoint(220, 235, 275, 320, 225) },
        { 89, new Breakpoint(220, 235, 275, 320, 225) },
        { 90, new Breakpoint(220, 235, 275, 320, 225) },
        { 91, new Breakpoint(220, 235, 275, 320, 225) },
        { 92, new Breakpoint(220, 235, 275, 320, 225) },
        { 93, new Breakpoint(220, 235, 275, 320, 225) },
        { 95, new Breakpoint(220, 235, 275, 320, 225) },
        { 96, new Breakpoint(220, 235, 275, 320, 225) },
        { 97, new Breakpoint(220, 235, 275, 320, 225) },
        { 98, new Breakpoint(225, 240, 280, 325, 230) },
        { 99, new Breakpoint(225, 237, 280, 325, 227) },
        { 100, new Breakpoint(225, 238, 280, 325, 230) },
        { 101, new Breakpoint(225, 240, 280, 325, 230) },
        { 102, new Breakpoint(226, 241, 281, 326, 231) },
        { 103, new Breakpoint(227, 242, 282, 327, 232) },
        { 104, new Breakpoint(228, 243, 283, 328, 233) },
        { 105, new Breakpoint(229, 244, 284, 329, 234) },
        { 106, new Breakpoint(230, 245, 285, 330, 235) },
        { 107, new Breakpoint(230, 245, 285, 330, 235) },
        { 108, new Breakpoint(231, 246, 286, 331, 236) },
        { 109, new Breakpoint(232, 247, 287, 332, 237) },
        { 110, new Breakpoint(233, 248, 288, 333, 238) },
        { 111, new Breakpoint(234, 249, 289, 334, 239) },
        { 112, new Breakpoint(234, 249, 289, 334, 239) },
        { 113, new Breakpoint(235, 250, 290, 335, 240) },
        { 114, new Breakpoint(235, 250, 290, 335, 240) },
        { 116, new Breakpoint(235, 250, 290, 335, 240) },
        { 117, new Breakpoint(235, 250, 290, 335, 240) },
        { 118, new Breakpoint(235, 250, 290, 335, 240) },
        { 119, new Breakpoint(236, 251, 291, 336, 241) },
        { 120, new Breakpoint(237, 252, 292, 337, 242) },
        { 121, new Breakpoint(238, 253, 293, 338, 243) },
        { 122, new Breakpoint(240, 255, 295, 340, 245) },
        { 123, new Breakpoint(241, 256, 296, 341, 246) },
        { 124, new Breakpoint(242, 257, 297, 342, 247) },
        { 125, new Breakpoint(243, 258, 298, 343, 248) },
        { 126, new Breakpoint(244, 259, 299, 344, 249) },
        { 127, new Breakpoint(245, 260, 300, 345, 250) },
        { 128, new Breakpoint(245, 260, 300, 345, 250) },
        { 129, new Breakpoint(246, 261, 301, 346, 251) },
        { 130, new Breakpoint(247, 262, 302, 347, 252) },
        { 131, new Breakpoint(248, 263, 303, 348, 253) },
        { 132, new Breakpoint(249, 264, 304, 349, 254) },
        { 133, new Breakpoint(249, 264, 304, 349, 254) },
        { 134, new Breakpoint(250, 265, 305, 350, 255) },
        { 135, new Breakpoint(250, 266, 305, 350, 255) },
        { 137, new Breakpoint(251, 266, 306, 351, 256) },
        { 138, new Breakpoint(252, 267, 307, 352, 257) },
        { 139, new Breakpoint(253, 268, 308, 353, 258) },
        { 140, new Breakpoint(254, 269, 309, 354, 259) },
        { 141, new Breakpoint(254, 269, 309, 354, 259) },
        { 142, new Breakpoint(255, 270, 310, 355, 260) },
        { 143, new Breakpoint(255, 270, 310, 355, 260) },
        { 144, new Breakpoint(256, 271, 311, 356, 261) },
        { 145, new Breakpoint(257, 272, 312, 357, 262) },
        { 146, new Breakpoint(258, 273, 313, 358, 263) },
        { 147, new Breakpoint(258, 273, 313, 358, 263) },
        { 148, new Breakpoint(259, 274, 314, 359, 264) },
        { 149, new Breakpoint(260, 275, 315, 360, 265) },
    };

    public enum SectorType : uint
    {
        UnknownUnlock = 9876,
        Begin = 9000,
        Map = 9999,
    }

    /// <summary>
    /// Which sector has to be explored to unlock this one. <c>Sub</c>: exploring this sector also registers an extra
    /// submarine slot. <c>Map</c>: exploring this sector opens the next map. <c>Main</c>: on the main progression line.
    /// </summary>
    public sealed record UnlockedFrom(SectorType Sector, bool Sub = false, bool Map = false, bool Main = false)
    {
        public UnlockedFrom(uint sector, bool sub = false, bool map = false, bool main = false)
            : this((SectorType)sector, sub, map, main) { }
    }

    public static readonly Dictionary<uint, UnlockedFrom> SectorToUnlock = new()
    {
        { 0, new UnlockedFrom(SectorType.Map) },  // Map Deep-sea Site
        { 1, new UnlockedFrom(SectorType.Begin) },  // A    Default
        { 2, new UnlockedFrom(SectorType.Begin) },  // B    Default
        { 3, new UnlockedFrom(1) },  // C    Deep-sea Site 2          <-      The Ivory Shoals
        { 4, new UnlockedFrom(2) },  // D    The Lightless Basin      <-      Deep-sea Site 1
        { 5, new UnlockedFrom(2, main: true) },  // E    Deep-sea Site 3          <-      Deep-sea Site 1
        { 6, new UnlockedFrom(3) },  // F    The Southern Rimilala Trench <-      Deep-sea Site 2
        { 7, new UnlockedFrom(4) },  // G    The Umbrella Narrow      <-      The Lightless Basin
        { 8, new UnlockedFrom(7) },  // H    Offender's Rot          <-      The Umbrella Narrow
        { 9, new UnlockedFrom(5) },  // I    Neolith Island          <-      Deep-sea Site 3
        { 10, new UnlockedFrom(5, sub: true, main: true) },  // J    Unidentified Derelict      <-      Deep-sea Site 3
        { 11, new UnlockedFrom(9) },  // K    The Cobalt Shoals          <-      Neolith Island
        { 12, new UnlockedFrom(8) },  // L    The Mystic Basin          <-      Offender's Rot
        { 13, new UnlockedFrom(8) },  // M    Deep-sea Site 4          <-      Offender's Rot
        { 14, new UnlockedFrom(10, main: true) },  // N    The Central Rimilala Trench <-      Unidentified Derelict
        { 15, new UnlockedFrom(14, sub: true, main: true) },  // O    The Wreckage Of Discovery I <-      The Central Rimilala Trench
        { 16, new UnlockedFrom(11) },  // P    Komura                  <-      The Cobalt Shoals
        { 17, new UnlockedFrom(16) },  // Q    Kanayama                  <-      Komura
        { 18, new UnlockedFrom(12) },  // R    Concealed Bay              <-      The Mystic Basin
        { 19, new UnlockedFrom(15, main: true) },  // S    Deep-sea Site 5          <-      The Wreckage Of Discovery I
        { 20, new UnlockedFrom(19, sub: true, main: true) },  // T    Purgatory                  <-      Deep-sea Site 5
        { 21, new UnlockedFrom(19) },  // U    Deep-sea Site 6          <-      Deep-sea Site 5
        { 22, new UnlockedFrom(21) },  // V    The Rimilala Shelf      <-      Deep-sea Site 6
        { 23, new UnlockedFrom(14) },  // W    Deep-sea Site 7          <-      The Central Rimilala Trench
        { 24, new UnlockedFrom(23) },  // X    Glittersand Basin          <-      Deep-sea Site 7
        { 25, new UnlockedFrom(20, main: true) },  // Y    Flickering Dip          <-      Purgatory
        { 26, new UnlockedFrom(25, main: true) },  // Z    The Wreckage Of The Headway <-      Flickering Dip
        { 27, new UnlockedFrom(26, main: true) },  // AA   The Upwell              <-      The Wreckage Of The Headway
        { 28, new UnlockedFrom(27, main: true) },  // AB   The Rimilala Trench Bottom    <-      The Upwell
        { 29, new UnlockedFrom(27) },  // AC   Stone Temple              <-      The Upwell
        { 30, new UnlockedFrom(28, map: true, main: true) },  // AD   Sunken Vault              <-      The Rimilala Trench Bottom
        { 31, new UnlockedFrom(SectorType.Map) },  // Map Sea of Ash
        { 32, new UnlockedFrom(30, main: true) },  // A    South Isle Of Zozonan      <-   Sunken Vault
        { 33, new UnlockedFrom(32, main: true) },  // B    Wreckage Of The Windwalker <-   South Isle Of Zozonan
        { 34, new UnlockedFrom(33, main: true) },  // C    North Isle Of Zozonan      <-   Wreckage Of The Windwalker
        { 35, new UnlockedFrom(34) },  // D    Sea Of Ash 1              <-   North Isle Of Zozonan
        { 36, new UnlockedFrom(35) },  // E    The Southern Charnel Trench   <-   Sea Of Ash 1
        { 37, new UnlockedFrom(34, main: true) },  // F    Sea Of Ash 2              <-   North Isle Of Zozonan
        { 38, new UnlockedFrom(37, main: true) },  // G    Sea Of Ash 3              <-   Sea Of Ash 2
        { 39, new UnlockedFrom(38, main: true) },  // H    Ascetic's Demise          <-   Sea Of Ash 3
        { 40, new UnlockedFrom(38) },  // I    The Central Charnel Trench <-   Sea Of Ash 3
        { 41, new UnlockedFrom(40) },  // J    The Catacombs Of The Father <-   The Central Charnel Trench
        { 42, new UnlockedFrom(39, main: true) },  // K    Sea Of Ash 4              <-   Ascetic's Demise
        { 43, new UnlockedFrom(42, main: true) },  // L    The Midden Pit          <-   Sea Of Ash 4
        { 44, new UnlockedFrom(40) },  // M    The Lone Glove          <-   The Central Charnel Trench
        { 45, new UnlockedFrom(41) },  // N    Coldtoe Isle                 <-   The Catacombs Of The Father
        { 46, new UnlockedFrom(45) },  // O    Smuggler's Knot          <-   Coldtoe Isle
        { 47, new UnlockedFrom(43, main: true) },  // P    The Open Robe                 <-   The Midden Pit
        { 48, new UnlockedFrom(36) },  // Q    Nald'thal's Pipe             <-   The Southern Charnel Trench
        { 49, new UnlockedFrom(47, map: true, main: true) },  // R    The Slipped Anchor         <-   The Open Robe
        { 50, new UnlockedFrom(45) },  // S    Glutton's Belly             <-   Coldtoe Isle
        { 51, new UnlockedFrom(42) },  // T    The Blue Hole              <-   Sea Of Ash 4
        { 52, new UnlockedFrom(SectorType.Map) },  // Map Sea of Jade
        { 53, new UnlockedFrom(49, main: true) },  // A    The Isle Of Sacrament      <-   The Slipped Anchor
        { 54, new UnlockedFrom(53) },  // B    The Kraken's Tomb          <-   The Isle Of Sacrament
        { 55, new UnlockedFrom(53, main: true) },  // C    Sea Of Jade 1              <-   The Isle Of Sacrament
        { 56, new UnlockedFrom(55) },  // D    Rogo-Tumu-Here's Haunt  <-   Sea Of Jade 1
        { 57, new UnlockedFrom(55, main: true) },  // E    The Stone Barbs          <-   Sea Of Jade 1
        { 58, new UnlockedFrom(56) },  // F    Rogo-Tumu-Here's Repose  <-   Rogo-Tumu-Here's Haunt
        { 59, new UnlockedFrom(57, main: true) },  // G    Tangaroa's Prow          <-   The Stone Barbs
        { 60, new UnlockedFrom(57) },  // H    Sea Of Jade 2              <-   The Stone Barbs
        { 61, new UnlockedFrom(59) },  // I    The Blind Sound          <-   Tangaroa's Prow
        { 62, new UnlockedFrom(59, main: true) },  // J    Sea Of Jade 3              <-   Tangaroa's Prow
        { 63, new UnlockedFrom(61) },  // K    Moergynn's Forge          <-   The Blind Sound
        { 64, new UnlockedFrom(61) },  // L    Tangaroa's Beacon          <-   The Blind Sound
        { 65, new UnlockedFrom(62, main: true) },  // M    Sea Of Jade 4              <-   Sea Of Jade 3
        { 66, new UnlockedFrom(65) },  // N    The Forest Of Kelp      <-   Sea Of Jade 4
        { 67, new UnlockedFrom(64) },  // O    Sea Of Jade 5              <-   Tangaroa's Beacon
        { 68, new UnlockedFrom(66) },  // P    Bladefall Chasm          <-   The Forest Of Kelp
        { 69, new UnlockedFrom(64) },  // Q    Stormport                  <-   Tangaroa's Beacon
        { 70, new UnlockedFrom(65, main: true) },  // R    Wyrm's Rest              <-   Sea Of Jade 4
        { 71, new UnlockedFrom(69) },  // S    Sea Of Jade 6              <-   Stormport
        { 72, new UnlockedFrom(70, map: true, main: true) },  // T    The Devil's Crypt          <-   Wyrm's Rest
        { 73, new UnlockedFrom(SectorType.Map) },  // Map Sirensong Sea
        { 74, new UnlockedFrom(72, main: true) },  // A    Mastbound's Bounty      <-   The Devil's Crypt
        { 75, new UnlockedFrom(74, main: true) },  // B    Sirensong Sea 1          <-   Mastbound's Bounty
        { 76, new UnlockedFrom(74) },  // C    Sirensong Sea 2          <-   Mastbound's Bounty
        { 77, new UnlockedFrom(76) },  // D    Anthemoessa              <-   Sirensong Sea 2
        { 78, new UnlockedFrom(75) },  // E    Magos Trench              <-   Sirensong Sea 1
        { 79, new UnlockedFrom(75, main: true) },  // F    Thrall's Unrest          <-   Sirensong Sea 1
        { 80, new UnlockedFrom(76) },  // G    Crow's Drop              <-   Sirensong Sea 2
        { 81, new UnlockedFrom(77) },  // H    Sirensong Sea 3          <-   Anthemoessa
        { 82, new UnlockedFrom(81) },  // I    The Anthemoessa Undertow  <-   Sirensong Sea 3
        { 83, new UnlockedFrom(79, main: true) },  // J    Sirensong Sea 4          <-   Thrall's Unrest
        { 84, new UnlockedFrom(83) },  // K    Seafoam Tide              <-   Sirensong Sea 4
        { 85, new UnlockedFrom(83, main: true) },  // L    The Beak                  <-   Sirensong Sea 4
        { 86, new UnlockedFrom(81) },  // M    Seafarer's End          <-   Sirensong Sea 3
        { 87, new UnlockedFrom(82) },  // N    Drifter's Decay          <-   The Anthemoessa Undertow
        { 88, new UnlockedFrom(84) },  // O    Lugat's Landing          <-   Seafoam Tide
        { 89, new UnlockedFrom(85, main: true) },  // P    The Frozen Spring          <-   The Beak
        { 90, new UnlockedFrom(87) },  // Q    Sirensong Sea 5          <-   Drifter's Decay
        { 91, new UnlockedFrom(88) },  // R    Tidewind Isle              <-   Lugat's Landing
        { 92, new UnlockedFrom(88) },  // S    Bloodbreak              <-   Lugat's Landing
        { 93, new UnlockedFrom(89, map: true, main: true) },  // T    The Crystal Font          <-   The Frozen Spring
        { 94, new UnlockedFrom(SectorType.Map) },  // Map Lilac Sea
        { 95, new UnlockedFrom(93, main: true) },  // A    Weeping Trellis               <-       The Crystal Font
        { 96, new UnlockedFrom(95, main: true) },  // B    The Forsaken Isle             <-       Weeping Trellis
        { 97, new UnlockedFrom(95) },  // C    Fortune's Ford                <-       Weeping Trellis
        { 98, new UnlockedFrom(96) },  // D    The Lilac Sea 1               <-       The Forsaken Isle
        { 99, new UnlockedFrom(97) },  // E    Runner's Reach                <-       Fortune's Ford
        { 100, new UnlockedFrom(96, main: true) },  // F    Bellflower Flood              <-       The Forsaken Isle
        { 101, new UnlockedFrom(97) },  // G    The Lilac Sea 2               <-       Fortune's Ford
        { 102, new UnlockedFrom(101) },  // H    Lilac Sea 3                   <-       Lilac Sea 2
        { 103, new UnlockedFrom(98) },  // I    Northwest Bellflower          <-       Lilac Sea 1
        { 104, new UnlockedFrom(100, main: true) },  // J    Corolla Isle                  <-       Bellflower Flood
        { 105, new UnlockedFrom(101) },  // K    Southeast Bellflower          <-       Lilac Sea 2
        { 106, new UnlockedFrom(104, main: true) },  // L    The Floral Reef               <-       Corolla Isle
        { 107, new UnlockedFrom(105) },  // M    Wingsreach                    <-       Southeast Bellflower
        { 108, new UnlockedFrom(106) },  // N    The Floating Standard         <-       The Floral Reef
        { 109, new UnlockedFrom(107) },  // O    The Fluttering Bay            <-       Wingsreach
        { 110, new UnlockedFrom(103) },  // P    Lilac Sea 4                   <-       Northwest Bellflower
        { 111, new UnlockedFrom(106, main: true) },  // Q    Proudkeel                     <-       The Floral Reef
        { 112, new UnlockedFrom(109) },  // R    East Dodie's Abyss            <-       The Fluttering Bay
        { 113, new UnlockedFrom(108) },  // S    Lilac Sea 5                   <-       The Floating Standard
        { 114, new UnlockedFrom(111, map: true, main: true) },  // T    West Dodie's Abyss            <-       Proudkeel
        { 115, new UnlockedFrom(SectorType.Map) },  // Map South Indigo Deep
        { 116, new UnlockedFrom(114) },  // A    The Indigo Shallows           <-       West Dodie's Abyss
        { 117, new UnlockedFrom(116, main: true) },  // B    Voyagers' Reprieve            <-       The Indigo Shallows
        { 118, new UnlockedFrom(116) },  // C    North Delphinium Seashelf     <-       The Indigo Shallows
        { 119, new UnlockedFrom(117) },  // D    Rainbringer Rift              <-       Voyagers' Reprieve
        { 120, new UnlockedFrom(118) },  // E    South Indigo Deep 1           <-       North Delphinium Seashelf
        { 121, new UnlockedFrom(117, main: true) },  // F    The Central Blue              <-       Voyagers' Reprieve
        { 122, new UnlockedFrom(118) },  // G    South Indigo Deep 2           <-       North Delphinium Seashelf
        { 123, new UnlockedFrom(122) },  // H    The Talon                     <-       South Indigo Deep 2
        { 124, new UnlockedFrom(121, main: true) },  // I    Southern Central Blue         <-       The Central Blue
        { 125, new UnlockedFrom(122) },  // J    South Indigo Deep 3           <-       South Indigo Deep 2
        { 126, new UnlockedFrom(123) },  // K    the Talonspoint Depths        <-       The Talon
        { 127, new UnlockedFrom(124) },  // L    Saltfarer's Eye               <-       Southern Central Blue
        { 128, new UnlockedFrom(124, main: true) },  // M    Startail Shallows             <-       Southern Central Blue
        { 129, new UnlockedFrom(128, main: true) },  // N    Moonshadow Isle               <-       Startail Shallows
        { 130, new UnlockedFrom(127) },  // O    Emerald Drop                  <-       Saltfarer's Eye
        { 131, new UnlockedFrom(129) },  // P    South Indigo Deep 4           <-       Moonshadow Isle
        { 132, new UnlockedFrom(127) },  // Q    South Delphinium Seashelf     <-       Saltfarer's Eye
        { 133, new UnlockedFrom(129, main: true) },  // R    Startail Shelf                <-       Moonshadow Isle
        { 134, new UnlockedFrom(132) },  // S    Cradle of the Winds           <-       South Delphinium Seashelf
        { 135, new UnlockedFrom(133, map: true, main: true) },  // T    Startail Trench               <-       Startail Shelf
        { 136, new UnlockedFrom(SectorType.Map) },  // Map The Northern Empty
        { 137, new UnlockedFrom(135) },  // A    Eastern Blackblood Wells        <-       Startail Trench
        { 138, new UnlockedFrom(137) },  // B    Sea Wolf Cove                   <-       Eastern Blackblood Wells
        { 139, new UnlockedFrom(137) },  // C    Southernmost Hanthbyrt          <-       Eastern Blackblood Wells
        { 140, new UnlockedFrom(139) },  // D    Oeyaseik                        <-       Southernmost Hanthbyrt
        { 141, new UnlockedFrom(138) },  // E    Northeast Hanthbyrt             <-      Sea Wolf Cove
        { 142, new UnlockedFrom(140) },  // F    Vyrstrant                       <-       Oeyaseik
        { 143, new UnlockedFrom(142) },  // G    The Sunken Jawbone (G)          <-      Vyrstrant
        { 144, new UnlockedFrom(143) },  // H    the Vyrstrant Dropoff               <-      The Sunken Jawbone (G)
        { 145, new UnlockedFrom(141) },  // I    Axeblade Bight                      <-      Northeast Hanthbyrt
        { 146, new UnlockedFrom(145) },  // J    the Puffin Assembly                 <-      Axeblade Bight
        { 147, new UnlockedFrom(144) },  // K    southern Aerslaent Plunge           <-     the Vyrstrant Dropoff
        { 148, new UnlockedFrom(146) },  // L    the Solkronn Gallery                <-      the Puffin Assembly
        { 149, new UnlockedFrom(144) },  // M    central Den of the Greatsword       <-    the Vyrstrant Dropoff
    };
}
