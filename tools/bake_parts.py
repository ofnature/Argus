"""Generate Argus/Core/Data/PartItems.cs: part sheet row -> Item row, for both vessel types.

Submarines: taken from SubmarineTracker's Submarines.PartIdToItemId (MIT, Infi).
Airships: AirshipExplorationPart rows are laid out in blocks of six by rank (Bronco, Invincible, Enterprise,
  Invincible II, Odyssey, Tatanora) per slot, with Viltgance appended as rows 25-28; the item ids follow the same
  blocks. Every entry is verified against the game's own item name before the file is written.
"""
import json
import re
import urllib.parse
import urllib.request

SCRATCH = 'C:/Users/korha/AppData/Local/Temp/claude/D--Dev-Argus/fc267f18-e6de-4fd6-9c99-932a5149bca2/scratchpad/'
OUT = 'D:/Dev/Argus/Argus/Core/Data/PartItems.cs'
FIXTURE = 'D:/Dev/Argus/Argus.Tests/Fixtures/gamedata.json'
ST = 'D:/Dev/Argus/external/SubmarineTracker/SubmarineTracker/Data/Submarine.cs'

names_cache_path = SCRATCH + 'item_names.json'
try:
    NAMES = json.load(open(names_cache_path, encoding='utf-8'))
except OSError:
    NAMES = {}


def item_name(item_id):
    key = str(item_id)
    if key not in NAMES:
        req = urllib.request.Request(f'https://v2.xivapi.com/api/sheet/Item/{item_id}?fields=Name',
                                     headers={'User-Agent': 'Argus-parts-bake/1.0'})
        with urllib.request.urlopen(req, timeout=30) as r:
            NAMES[key] = json.load(r)['fields']['Name']
    return NAMES[key]


# --- submarines: lift the table out of the SubmarineTracker checkout ------------------------------------------------
sub_map = {}
for line in open(ST, encoding='utf-8'):
    m = re.match(r'\s*\{\s*(\d+),\s*(\d+)\s*\}', line)
    if m:
        sub_map[int(m.group(1))] = int(m.group(2))
assert len(sub_map) == 40, f'expected 40 submarine parts, got {len(sub_map)}'

# --- airships: blocks of six by rank per slot, Viltgance appended -----------------------------------------------
CLASS_NAMES = {1: 'Bronco', 4: 'Invincible', 2: 'Enterprise', 5: 'Invincible II', 3: 'Odyssey', 6: 'Tatanora', 7: 'Viltgance'}
SLOT_WORD = {0: 'Hull', 2: 'Forecastle', 3: 'Aftcastle'}  # slot 1 is named per class (Sail/Propellers/Bladder/...)
SLOT_BASE = {0: 10156, 1: 10162, 2: 10168, 3: 10174}
VILTGANCE = {0: 14003, 1: 14004, 2: 14005, 3: 14006}

parts = [p for p in json.load(open(FIXTURE))['Parts'] if p['Type'] == 'Airship']
by_rank_order = [1, 4, 2, 5, 3, 6]

air_map = {}
for p in parts:
    cls, slot, row = p['Class'], p['Slot'], p['Id']
    if cls == 7:
        air_map[row] = VILTGANCE[slot]
    else:
        air_map[row] = SLOT_BASE[slot] + by_rank_order.index(cls)

# verify every airship entry against the real item name
for p in parts:
    row, item = p['Id'], air_map[p['Id']]
    name = item_name(item)
    expected_class = CLASS_NAMES[p['Class']]
    assert name.startswith(expected_class + '-type'), f'row {row} -> {item} "{name}" is not a {expected_class} part'
    word = SLOT_WORD.get(p['Slot'])
    if word:
        assert name.endswith(word), f'row {row} -> {item} "{name}" is not a {word}'
    print(f'  row {row:>2} class {p["Class"]} slot {p["Slot"]} rank {p["Rank"]:>2} -> {item} {name}')

json.dump(NAMES, open(names_cache_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

lines = ['using System.Collections.Generic;', '', 'using Argus.Core.Model;', '', 'namespace Argus.Core.Data;', '',
         '/// <summary>',
         '/// Part sheet row to the inventory item that installs it. The parts windows list items by name, so swapping a',
         '/// part needs the item behind the <c>SubmarinePart</c> / <c>AirshipExplorationPart</c> row.',
         '///',
         '/// <para>Submarine ids come from SubmarineTracker (MIT, Infi). Airship ids were derived from the sheet layout',
         '/// (six classes by rank per slot, Viltgance appended) and every one was verified against the item name.</para>',
         '/// </summary>',
         'public static class PartItems',
         '{',
         '    private static readonly Dictionary<uint, uint> SubmarineParts = new()',
         '    {']
for row in sorted(sub_map):
    lines.append(f'        {{ {row}, {sub_map[row]} }},')
lines += ['    };', '',
          '    private static readonly Dictionary<uint, uint> AirshipParts = new()',
          '    {']
for p in sorted(parts, key=lambda x: x['Id']):
    row = p['Id']
    lines.append(f'        {{ {row}, {air_map[row]} }}, // {item_name(air_map[row])}')
lines += ['    };', '',
          '    /// <summary>The item that installs a part, or 0 when the row is unknown.</summary>',
          '    public static uint ItemFor(VesselType type, uint partRow)',
          '    {',
          '        var table = type == VesselType.Airship ? AirshipParts : SubmarineParts;',
          '        return table.TryGetValue(partRow, out var item) ? item : 0;',
          '    }',
          '',
          '    public static int Count(VesselType type) => (type == VesselType.Airship ? AirshipParts : SubmarineParts).Count;',
          '}', '']
open(OUT, 'w', encoding='utf-8').write('\n'.join(lines))
print(f'\nwrote {OUT}: {len(sub_map)} submarine parts, {len(air_map)} airship parts')
