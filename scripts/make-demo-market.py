"""Writes a made-up copy of mnm-db.com's auction feed, for the README pictures and for trying the Market page without the site.

    python make-demo-market.py <output file> [yyyy-MM-ddTHH:mm:ssZ]

Every player here is invented; the items are ordinary game names, chosen to overlap the demo ledger (make-demo-ledger.py) so the
Yours tab has loot to price. The file has the feed's shape (listings, requests, stats, stacks, capture), so the app reads it like the real thing:

    MnMCompanion --ledger <demo folder> --character Brindlewick --snapshot out.png --window --mini Market --market <output file>
"""
import json
import random
import sys
from datetime import datetime, timedelta, timezone

OUT = sys.argv[1] if len(sys.argv) > 1 else "demo-auctions.json"
NOW = datetime.strptime(sys.argv[2], "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc) if len(sys.argv) > 2 else datetime.now(timezone.utc).replace(microsecond=0)
SERVER = "Demoserver"
rng = random.Random(2026)

PLAYERS = ["Avelin", "Borrik", "Cerys", "Dallin", "Elowen", "Faelan", "Gwil", "Hadria", "Ingram", "Jorah", "Kestrel", "Lirael",
           "Maelis", "Nerys", "Oswin", "Perrin", "Quill", "Rhoswen", "Sorrel", "Tavish", "Una", "Vashti", "Wren", "Yorath"]
STACK = 20

# Bulk materials are sold by the stack of 20: the listing's price is a stack's, its unit the price of one.
BULK = {"Copper Ore", "Wood", "Resin", "Bone Chips", "Cloth Scraps", "Wool Scraps", "Hide Scraps", "Rawhide Scraps", "Leather Scraps",
        "Cotton Scraps", "Iron Scraps", "Bronze Scraps", "Pungent Healing Herbs"}

# item: [(intent, price in copper (a stack's for bulk), days ago first seen, times read), ...]
MARKET = {
    "Copper Ore": [("sell", 500, 0.1, 2), ("sell", 450, 1.2, 14), ("sell", 600, 2.5, 6), ("sell", 400, 9.0, 3), ("buy", 300, 0.6, 1)],
    "Wood": [("sell", 200, 0.3, 5), ("sell", 250, 1.8, 9)],
    "Resin": [("sell", 150, 2.1, 4)],
    "Bone Chips": [("sell", 800, 0.5, 7), ("sell", 700, 1.1, 3), ("buy", 500, 2.0, 2)],
    "Cloth Scraps": [("sell", 300, 0.2, 11), ("sell", 350, 0.9, 4), ("sell", 280, 2.7, 2), ("buy", 200, 1.5, 3)],
    "Wool Scraps": [("sell", 500, 0.4, 6), ("sell", 550, 2.2, 2)],
    "Hide Scraps": [("sell", 400, 1.0, 5)],
    "Rawhide Scraps": [("sell", 600, 0.7, 8), ("buy", 450, 0.3, 1)],
    "Leather Scraps": [("sell", 700, 1.4, 3)],
    "Cotton Scraps": [("sell", 450, 6.5, 2)],
    "Iron Scraps": [("sell", 900, 0.8, 4)],
    "Rawhide Backpack": [("sell", 500, 0.1, 16), ("sell", 600, 0.6, 1), ("sell", 450, 1.9, 7), ("buy", 400, 1.0, 2)],
    "Worn Bow": [("sell", 12000, 2.3, 3)],
    "Weathered Medallion": [("sell", 250, 1.6, 2), ("sell", 300, 0.4, 1)],
    "Polished Medallion": [("sell", 350, 0.9, 2)],
    "Crocodile Hide": [("sell", 120, 0.5, 4), ("sell", 150, 1.3, 2)],
    "Pristine Crocodile Hide": [("sell", 800, 0.2, 1), ("buy", 600, 2.4, 3)],
    "Crocodile Tooth": [("sell", 40, 1.1, 2)],
    "Barnacle Bracer": [("sell", 30000, 0.3, 5), ("sell", 28000, 1.7, 2), ("buy", 20000, 0.1, 1)],
    "Iron Longsword": [("sell", 25000, 5.0, 3)],
    "Enchanted Rawhide Cap": [("sell", 120000, 1.2, 4)],
    "Torque of the False God": [("buy", 150000, 0.8, 2), ("buy", 140000, 2.9, 1)],
    "Clouded Crystallized Magic": [("sell", 2500, 0.2, 9), ("sell", 2500, 1.0, 3), ("sell", 3000, 2.6, 2), ("buy", 2000, 0.5, 1)],
    "Rusty Greatsword": [("sell", 1500, 12.0, 1)],
    "Mace of Malice": [("sell", 450000, 0.9, 2), ("buy", 400000, 1.4, 1)],
    "Wooden Bank Crate": [("sell", 8000, 0.6, 5), ("sell", 7500, 3.3, 2)],
}

# What the site's wiki says about an item: the shape of its stats block
CARDS = {
    "Copper Ore": {"weight": 0.25, "size": "SMALL", "tradeskills": ["Smelting"]},
    "Wood": {"weight": 0.5, "size": "MEDIUM", "tradeskills": ["Woodworking"]},
    "Resin": {"weight": 0.1, "size": "SMALL", "tradeskills": ["Woodworking"]},
    "Bone Chips": {"weight": 0.1, "size": "SMALL", "tradeskills": ["Jewelcrafting"]},
    "Cloth Scraps": {"weight": 0.1, "size": "SMALL", "tradeskills": ["Tailoring"]},
    "Wool Scraps": {"weight": 0.1, "size": "SMALL", "tradeskills": ["Tailoring"]},
    "Hide Scraps": {"weight": 0.1, "size": "SMALL", "tradeskills": ["Leatherworking"]},
    "Rawhide Scraps": {"weight": 0.1, "size": "SMALL", "tradeskills": ["Leatherworking"]},
    "Leather Scraps": {"weight": 0.1, "size": "SMALL", "tradeskills": ["Leatherworking"]},
    "Cotton Scraps": {"weight": 0.1, "size": "SMALL", "tradeskills": ["Tailoring"]},
    "Iron Scraps": {"weight": 0.3, "size": "SMALL", "tradeskills": ["Blacksmithing"]},
    "Rawhide Backpack": {"slot": "Backpack", "weight": 2.5, "size": "EXTRA LARGE", "container": {"capacity": 10, "maxSize": "LARGE", "weightReduction": None}, "class": "ALL", "race": "ALL", "tradeskills": ["Leatherworking"]},
    "Worn Bow": {"slot": "RANGE", "dmg": 5, "delay": 40, "skill": "Archery", "weight": 3.0, "class": "ALL", "race": "ALL"},
    "Weathered Medallion": {"weight": 0.2, "size": "SMALL"},
    "Polished Medallion": {"weight": 0.2, "size": "SMALL"},
    "Crocodile Hide": {"weight": 0.5, "size": "MEDIUM", "tradeskills": ["Leatherworking"]},
    "Pristine Crocodile Hide": {"weight": 0.5, "size": "MEDIUM", "tradeskills": ["Leatherworking"]},
    "Crocodile Tooth": {"weight": 0.1, "size": "SMALL"},
    "Barnacle Bracer": {"flags": ["MAGIC"], "slot": "WRIST", "ac": 13, "stats": {"STR": 3, "WIS": 2}, "hp": 10, "mana": 5, "weight": 0.5, "size": "SMALL", "class": "ALL", "race": "ALL"},
    "Iron Longsword": {"slot": "PRIMARY SECONDARY", "handed": "1H Slashing", "dmg": 6, "delay": 30, "stats": {"STR": 2, "STA": 1}, "weight": 5.5, "class": "WAR PAL", "race": "ALL", "tradeskills": ["Blacksmithing"], "vendor": 1250},
    "Enchanted Rawhide Cap": {"flags": ["MAGIC"], "slot": "HEAD", "ac": 6, "stats": {"INT": 3, "WIS": 3}, "weight": 0.8, "class": "ALL", "race": "ALL"},
    "Torque of the False God": {"flags": ["MAGIC", "LORE"], "slot": "NECK", "stats": {"STR": 4, "STA": 4, "CHA": -2}, "hp": 25, "weight": 0.3, "class": "ALL", "race": "ALL"},
    "Clouded Crystallized Magic": {"weight": 0.1, "size": "SMALL", "tradeskills": ["Enchanting"]},
    "Rusty Greatsword": {"slot": "PRIMARY", "handed": "2H Slashing", "dmg": 9, "delay": 45, "weight": 9.0, "class": "WAR", "race": "ALL"},
    "Mace of Malice": {"flags": ["MAGIC", "LORE"], "slot": "PRIMARY SECONDARY", "handed": "1H Blunt", "dmg": 11, "delay": 32, "stats": {"STR": 5}, "effect": "Malice (Proc. Level: 20)", "weight": 6.0, "class": "WAR PAL CLR", "race": "ALL"},
    "Wooden Bank Crate": {"slot": "Backpack", "weight": 4.0, "size": "GIANT", "container": {"capacity": 12, "maxSize": "GIANT", "weightReduction": None}, "class": "ALL", "race": "ALL", "tradeskills": ["Woodworking"]},
}


def coin(c):
    parts = []
    g, c = divmod(c, 10000)
    s, c = divmod(c, 100)
    if g: parts.append(f"{g}g")
    if s: parts.append(f"{s}s")
    if c: parts.append(f"{c}c")
    return " ".join(parts) or "0c"


def stamp(t):
    return t.strftime("%Y-%m-%dT%H:%M:%S.") + f"{t.microsecond // 1000:03d}Z"


listings = []
used = set()
for item, posts in MARKET.items():
    bulk = item in BULK
    for intent, price, days, count in posts:
        player = rng.choice([p for p in PLAYERS if (p, item) not in used])
        used.add((player, item))
        seen = NOW - timedelta(days=days, minutes=rng.randint(0, 59))
        row = {"server": SERVER, "intent": intent, "item": item, "price": price, "unit": price // STACK if bulk else price, "priceStr": coin(price)}
        if bulk:
            row.update({"stack": STACK, "perStack": True})
        row.update({"player": player, "seen": stamp(seen), "count": count, "matched": True})
        if not bulk and rng.random() < 0.15:
            row["qty"] = rng.choice([2, 3])
        listings.append(row)
# The kinds of row the feed also carries: a post without a price, one naming something the wiki doesn't know, one whose intent couldn't be read
listings.append({"server": SERVER, "intent": "sell", "item": "Flux", "price": None, "unit": None, "priceStr": None, "player": "Nerys", "seen": stamp(NOW - timedelta(hours=1)), "count": 1, "matched": True})
listings.append({"server": SERVER, "intent": "sell", "item": "TB", "price": 500, "unit": 500, "priceStr": "5s", "player": "Oswin", "seen": stamp(NOW - timedelta(hours=2)), "count": 1, "matched": False})
listings.append({"server": SERVER, "intent": None, "item": "Wolf Pelt", "price": 700, "unit": 700, "priceStr": "7s", "player": "Perrin", "seen": stamp(NOW - timedelta(hours=3)), "count": 1, "matched": True})
listings.sort(key=lambda r: r["seen"], reverse=True)

requests = [
    {"server": SERVER, "player": "Lirael", "plus": [7], "stats": [], "category": "jewelry", "text": "+7 Jewelry - Neck/Ear/Ring - All Stats - 2g Each or 9g Full Sets - Pst",
     "seen": stamp(NOW - timedelta(days=11)), "lastSeen": stamp(NOW - timedelta(days=2)), "count": 28},
    {"server": SERVER, "player": "Maelis", "plus": [3], "stats": ["str", "sta"], "category": "ring", "text": "WTB +3 Str/Sta ring, paying well", "seen": stamp(NOW - timedelta(days=1)), "lastSeen": stamp(NOW - timedelta(hours=5)), "count": 2},
    {"server": SERVER, "player": "Quill", "plus": [3], "stats": ["str", "sta"], "category": "ring", "text": "Looking for a +3 str/sta ring", "seen": stamp(NOW - timedelta(days=3)), "lastSeen": stamp(NOW - timedelta(days=1)), "count": 4},
    {"server": SERVER, "player": "Rhoswen", "plus": [5], "stats": ["wis", "int"], "category": "gear", "text": "Any +5 wis/int gear, pst", "seen": stamp(NOW - timedelta(days=4)), "lastSeen": stamp(NOW - timedelta(days=4)), "count": 1},
]

stats = {}
for item, card in CARDS.items():
    full = {"name": item, "hasPage": True, "icon": None, "flags": [], "slot": None, "handed": None, "dmg": None, "delay": None, "skill": None, "ac": None, "stats": {},
            "hp": None, "mana": None, "hpRegen": None, "manaRegen": None, "haste": None, "resists": {}, "instr": {}, "weight": None, "size": None, "container": None,
            "effect": None, "class": None, "race": None, "tradeskills": [], "zones": [], "from": [], "vendor": None}
    full.update(card)
    stats[item.lower()] = full

feed = {
    "generatedAt": stamp(NOW),
    "capture": {"liveCycles24h": 664, "readingCycles24h": 638, "offlineCycles24h": 0, "longestBlindMin": 52, "blindNowMin": 0, "lastLinesAt": stamp(NOW - timedelta(minutes=2))},
    "listings": listings,
    "requests": requests,
    "stats": stats,
    "stacks": {i.lower(): STACK for i in sorted(BULK)},
}
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(feed, f)
print(f"wrote {OUT}: {len(listings)} listings, {len(requests)} requests, {len(stats)} item cards")
