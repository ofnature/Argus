"""Bake the submarine and airship loot tables into one compact JSON for Argus to embed.

Submarines: Infiziert90/FFXIVGachaSpreadsheet website/static/data/Submarines.json (crowd-sourced voyage records).
  Per surveillance pool: Amount = times the item was drawn, Records = draws in that pool, so Amount/Records is the
  per-draw probability. MinMax gives the quantity range per retrieval tier.
Airships: submarine.girin.dev per-sector tier lists (item names only, no rates), resolved to item ids via XIVAPI.
"""
import json
import time
import urllib.parse
import urllib.request

SCRATCH = 'C:/Users/korha/AppData/Local/Temp/claude/D--Dev-Argus/fc267f18-e6de-4fd6-9c99-932a5149bca2/scratchpad/'
OUT = 'D:/Dev/Argus/Argus/Core/Data/LootTable.json'


def fetch(url):
    # XIVAPI rejects urllib's default user agent with 403.
    req = urllib.request.Request(url, headers={'User-Agent': 'Argus-loot-bake/1.0 (FFXIV Dalamud plugin)'})
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.load(r)


def normalise(s):
    return ''.join(c for c in s.lower() if c.isalnum())


def lookup(name):
    """Exact name first; then a contains-search, since girin drops apostrophes (Potters Stone / Potter's Stone)."""
    for query in (f'Name="{name}"', f'Name~"{name}"'):
        url = f'https://v2.xivapi.com/api/search?sheets=Item&query={urllib.parse.quote(query)}&fields=Name&limit=20'
        try:
            results = fetch(url).get('results', [])
        except Exception as ex:
            print('  lookup failed', name, ex)
            return 0
        for r in results:
            if normalise(r['fields']['Name']) == normalise(name):
                return r['row_id']
        time.sleep(0.05)
    return 0


def resolve_names(names):
    """Item name -> row id via XIVAPI, cached on disk so re-bakes do not hammer it."""
    cache_path = SCRATCH + 'girin/item_ids.json'
    try:
        cache = json.load(open(cache_path, encoding='utf-8'))
    except OSError:
        cache = {}

    for name in names:
        if name in cache:
            continue
        cache[name] = lookup(name)
        if not cache[name]:
            print('  UNRESOLVED:', name)
        time.sleep(0.05)

    json.dump(cache, open(cache_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    return cache


def submarines():
    src = json.load(open(SCRATCH + 'loot/Submarines.json'))
    out = {}
    for sid, sector in src['Sectors'].items():
        tiers = {}
        for tier_name, pool in sector['Pools'].items():
            records = pool['Records']
            if records <= 0:
                continue
            rows = []
            for iid, r in pool['Rewards'].items():
                mm = r['MinMax']
                rows.append({
                    'i': int(iid),
                    # Per-draw probability of this item, rounded: five decimals is finer than the data warrants.
                    'p': round(r['Amount'] / records, 5),
                    # Quantity range per retrieval tier: poor, normal, optimal.
                    'q': [mm['Poor'][0], mm['Poor'][1], mm['Normal'][0], mm['Normal'][1], mm['Optimal'][0], mm['Optimal'][1]],
                })
            rows.sort(key=lambda x: -x['p'])
            tiers[tier_name[-1]] = rows
        if tiers:
            out[sid] = tiers
    return out


def airships(ids):
    src = json.load(open(SCRATCH + 'girin/airship_sectors.json', encoding='utf-8'))
    out = {}
    for s in src:
        # girin numbers sectors from 1; AirshipExplorationPoint rows are zero-based.
        row = s['sector'] - 1
        tiers = {}
        for tier, names in s['tiers'].items():
            resolved = sorted({ids[n] for n in names if ids.get(n)})
            if resolved:
                tiers[tier] = resolved
        if tiers:
            out[str(row)] = tiers
    return out


names = json.load(open(SCRATCH + 'girin/airship_item_names.json', encoding='utf-8'))
print(f'resolving {len(names)} airship item names')
ids = resolve_names(names)
unresolved = [n for n in names if not ids.get(n)]

table = {
    'Source': 'Submarines: Infiziert90/FFXIVGachaSpreadsheet (crowd-sourced). Airships: submarine.girin.dev tier lists.',
    'Submarine': submarines(),
    'Airship': airships(ids),
}
json.dump(table, open(OUT, 'w', encoding='utf-8'), separators=(',', ':'), ensure_ascii=False)

import os
print('sub sectors', len(table['Submarine']), 'airship sectors', len(table['Airship']))
print('unresolved airship names:', unresolved)
print('bytes', os.path.getsize(OUT))
