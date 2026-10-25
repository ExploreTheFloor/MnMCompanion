"""Writes a made-up game data folder, for the README pictures and for trying the app without the game.

    python make-demo-ledger.py <output folder> [yyyy-MM-dd]

Every player here is invented; the monsters and items are ordinary game names. The files look exactly like the ones the game
writes (one Character and one Social ledger for the day), so the app reads them like the real thing:

    MnMCompanion --ledger <output folder> --character Brindlewick
"""
import base64
import json
import os
import random
import sys
from datetime import datetime, timedelta, timezone

OUT = sys.argv[1] if len(sys.argv) > 1 else "demo-game-data"
DAY = datetime.strptime(sys.argv[2], "%Y-%m-%d") if len(sys.argv) > 2 else datetime.now().replace(hour=0, minute=0, second=0, microsecond=0)
OWNER = "Brindlewick"
SERVER = "demoserver"
TZ = timezone(timedelta(hours=-5))
rng = random.Random(2026)


def b64(text):
    return base64.b64encode(text.encode()).decode()


# who is in the party: name, class code, level, how often they get to the corpse first
PARTY = [
    (OWNER, "None", 31, 25), ("Tamsin", "clr", 33, 20), ("Orrin", "ftr", 32, 20),
    ("Pip", "rog", 30, 15), ("Vesna", "wiz", 31, 10), ("Garrick", "pal", 32, 10),
]

# zone, mob, chance of a purse, (min, max) copper in it, {item: chance per corpse}
CAMPS = [
    ("fallenpass", "a restless skeleton", 0.8, (3, 60),
     {"Bone Chips": .55, "Wool Scraps": .15, "Weathered Medallion": .12, "Hide Scraps": .10, "Simple Medallion": .10, "Bronze Scraps": .08}),
    ("valeofzintar", "a Dustrend legionary", 0.85, (5, 140),
     {"Raw Orc Meat": .17, "Cotton Scraps": .17, "Polished Medallion": .16, "Lacquered Medallion": .15, "Leather Scraps": .12, "Iron Scraps": .10}),
    ("shadeddunes", "a bandit", 0.65, (10, 220),
     {"Cloth Scraps": .37, "Antique Ceramic Fragment": .10, "Rawhide Scraps": .10, "Worn Bow": .08, "Raw Human Meat": .08}),
    ("nightharborw", "a crocodile hatchling", 0.0, (0, 0),
     {"Crocodile Tooth": .70, "Raw Crocodile Meat": .37, "Fish Bones": .35, "Crocodile Hide": .21, "Pristine Crocodile Hide": .01}),
]

# what a vendor pays for one (copper); items missing here are never sold, so the app has to estimate or leave them unpriced
PRICES = {
    "Bone Chips": 4, "Wool Scraps": 9, "Hide Scraps": 11, "Bronze Scraps": 14, "Weathered Medallion": 130, "Simple Medallion": 60,
    "Raw Orc Meat": 6, "Cotton Scraps": 9, "Polished Medallion": 140, "Lacquered Medallion": 120, "Leather Scraps": 10, "Iron Scraps": 12,
    "Cloth Scraps": 9, "Worn Bow": 210, "Rawhide Scraps": 8, "Crocodile Tooth": 15, "Raw Crocodile Meat": 5, "Crocodile Hide": 40,
}
SOLD = [i for i in PRICES if i not in ("Hide Scraps", "Crocodile Hide")]   # a couple of kinds never reach a vendor

item_ids = {}
rows_character, rows_social = [], []
counter = [10000]


def stamp(t):
    return t.strftime("%Y-%m-%dT%H:%M:%S.%f") + "0-05:00"   # the game writes seven fractional digits


def row(act, f02, detail, t, zone, level, actor, cls, kind):
    owner_file = kind == "Character"
    return {
        "f01": act, "f02": f02, "f03": json.dumps(detail, separators=(",", ":")), "f04": stamp(t), "f05": "zone_" + b64(zone), "f06": level,
        "f07": actor, "f08": 0 if owner_file else 2, "f09": OWNER, **({"f10": cls} if kind == "Social" or cls else {}),
    }


def add(act, f02, detail, t, zone, who, kind_for_owner_twin=True):
    name, cls, level, _ = next(p for p in PARTY if p[0] == who)
    if who == OWNER:
        rows_character.append(row(act, f02, detail, t, zone, level, who, "", "Character"))
        if kind_for_owner_twin:
            rows_social.append(row(act, f02, detail, t + timedelta(seconds=1), zone, level, who, cls, "Social"))
    else:
        rows_social.append(row(act, f02, detail, t, zone, level, who, cls, "Social"))


def coin(copper):
    return f"0,{copper // 10000},{copper % 10000 // 100},{copper % 100}"


def coin_words(copper):
    return f"0 platinum {copper // 10000} gold {copper % 10000 // 100} silver {copper % 100} copper"


start = DAY.replace(hour=19, minute=0)
t = start
add("act_01", "31", {"d21": 31, "d22": 30, "d23": ""}, start - timedelta(minutes=40), "fallenpass", OWNER)
for who, cls, level, _ in PARTY[1:]:
    add("act_32", "name_" + b64(who), {"d61": "", "d62": 2 + PARTY.index((who, cls, level, _)), "d63": b64("PartyJoin"), "d64": "ref_4242"}, start - timedelta(minutes=30), "fallenpass", who)

weights = [p[3] for p in PARTY]
for zone, mob, purse_chance, (lo, hi), table in CAMPS:
    for _ in range(80):
        t += timedelta(seconds=rng.randint(14, 38))
        who = rng.choices([p[0] for p in PARTY], weights)[0]
        at = t
        if rng.random() < purse_chance:
            total = rng.randint(lo, hi)
            split = total // 6
            ctx = "npc_corpse" if who == OWNER else "party_split"
            add("act_14", "name_" + b64(mob), {"d11": total, "d12": b64(coin(total)), "d13": b64(mob), "d14": b64(ctx), "d15": b64(coin(split) if ctx == "party_split" else "")}, at, zone, who)
            at += timedelta(seconds=1)
        for item, chance in table.items():
            if rng.random() < chance:
                counter[0] += 1
                iid = item_ids.setdefault(item, "".join(rng.choice("0123456789abcdef") for _ in range(24)))
                add("act_13", f"ref_{counter[0]}", {"d01": 1, "d02": b64(mob), "d03": "", "d04": item, "d05": iid}, at, zone, who)
                at += timedelta(seconds=1)
    t += timedelta(minutes=3)
    # between camps the owner sells a few stacks to a vendor
    sell_at = t
    for item in rng.sample(SOLD, 6):
        n = rng.randint(2, 9)
        sell_at += timedelta(seconds=rng.randint(2, 5))
        rows_character.append(row("act_24", item, {"d01": n, "d02": "", "d03": b64(coin_words(PRICES[item] * n)), "d04": item, "d05": ""}, sell_at, "nightharborw", 31, OWNER, "", "Character"))
    t = sell_at + timedelta(minutes=1)

def mine(act, f02, detail, at, zone):
    rows_character.append(row(act, f02, detail, at, zone, 31, OWNER, "", "Character"))


# gathering for the owner: a long session with a break in the middle, as the real ledger has
gather_t = start - timedelta(hours=2, minutes=10)
for n in range(46):
    gather_t += timedelta(seconds=rng.randint(40, 110)) + (timedelta(minutes=25) if n == 23 else timedelta())
    item = rng.choices(["Copper Ore", "Resin", "Wood", "Pungent Healing Herbs"], [4, 3, 3, 1])[0]
    counter[0] += 1
    mine("act_27", f"ref_{counter[0]}", {"d01": rng.choice([1, 1, 2, 3]), "d02": "", "d03": b64("harvest"), "d04": item, "d05": "".join(rng.choice("0123456789abcdef") for _ in range(24))},
         gather_t, "nightharborw" if n < 30 else "fallenpass")

# the gathered stock is sold, so the gathering page can say what it is worth
sale_t = gather_t + timedelta(minutes=4)
for item, n, each in (("Copper Ore", 26, 30), ("Resin", 24, 12), ("Wood", 19, 6), ("Pungent Healing Herbs", 12, 20)):
    sale_t += timedelta(seconds=4)
    mine("act_24", item, {"d01": n, "d02": "", "d03": b64(coin_words(each * n)), "d04": item, "d05": ""}, sale_t, "nightharborw")

# supplies bought before the night and again halfway through
for when, shop in ((start - timedelta(minutes=25), "nightharborw"), (start + timedelta(hours=1, minutes=40), "nightharborw")):
    at = when
    for item, n, copper in (("Water Flask", 20, 100), ("Wild Carrots", 20, 180), ("Bandage", 10, 60)):
        at += timedelta(seconds=rng.randint(3, 8))
        mine("act_28", item, {"d01": n, "d02": "", "d03": b64(coin_words(copper)), "d04": item, "d05": ""}, at, shop)

# clearing the bag: drops, a couple taken back, and a few things destroyed
bag = start + timedelta(minutes=50)
for item, n in (("Raw Orc Meat", 6), ("Bone Chips", 12), ("Raw Crocodile Meat", 4), ("Fish Bones", 9), ("Wool Scraps", 5), ("Worn Fine Wood Buckler", 1), ("Rusty Greatsword", 1)):
    bag += timedelta(seconds=rng.randint(20, 90))
    counter[0] += 1
    mine("act_11", f"ref_{counter[0]}", {"d01": n, "d02": "", "d03": b64("user_confirmed_drop"), "d04": item, "d05": "".join(rng.choice("0123456789abcdef") for _ in range(24))}, bag, "valeofzintar")
    if item in ("Worn Fine Wood Buckler", "Raw Orc Meat"):
        counter[0] += 1
        mine("act_12", f"ref_{counter[0]}", {"d01": 1, "d02": "", "d03": "", "d04": item, "d05": "".join(rng.choice("0123456789abcdef") for _ in range(24))}, bag + timedelta(minutes=3), "valeofzintar")
for k, (item, n) in enumerate((("Incarnal Residue", 3), ("Incarnal Residue", 2), ("Trainee's Candle", 1), ("Well-Worn Cloth Trousers", 1))):
    counter[0] += 1
    mine("act_15", f"ref_{counter[0]}", {"d01": n, "d02": "", "d03": b64("user_confirmed_destroy"), "d04": item, "d05": "".join(rng.choice("0123456789abcdef") for _ in range(24))},
         start + timedelta(hours=1, minutes=15 + 20 * k), "shadeddunes")

# trades: the ledger writes one row for what you gave and one for what you got, in the same second, naming the same partner
def trade(at, partner, gave_coin, gave_items, got_coin, got_items):
    def side(act, coin, items, summary):
        return {"partner": partner, "itemCount": sum(q for _, q in items), "currency": b64(coin), "summary": b64(summary),
                "tradingPp": 0, "tradingGp": 0, "tradingSp": 0, "tradingCp": 0,
                "items": [{"publicLinkID": "".join(rng.choice("0123456789abcdef") for _ in range(24)), "name": n, "quantity": q} for n, q in items]}
    counter[0] += 1
    mine("act_16", "name_" + b64("trader"), side("act_16", gave_coin, gave_items, f"trade_completed_with_{sum(q for _, q in gave_items)}_items"), at, "nightharborw")
    mine("act_20", "name_" + b64("trader"), side("act_20", got_coin, got_items, "received_from_trade"), at, "nightharborw")

trade(start - timedelta(minutes=70), "ref_71358", "0,0,7,0", [], "0,0,0,0", [("Rawhide Backpack", 1)])
trade(start - timedelta(minutes=48), "ref_20917", "0,0,0,0", [("Worn Bow", 1)], "0,0,4,0", [])
trade(start + timedelta(minutes=95), "ref_38206", "0,0,0,0", [("Polished Medallion", 2)], "0,1,2,0", [])
trade(start + timedelta(hours=2, minutes=5), "ref_71358", "0,0,5,0", [], "0,0,0,0", [("Copper Scimitar", 1), ("Rawhide Backpack", 1)])
trade(start + timedelta(hours=2, minutes=30), "ref_65143", "0,0,0,24", [], "0,0,0,0", [("Rat Ear", 6)])

folder = os.path.join(OUT, SERVER, OWNER, "Ledger")
os.makedirs(folder, exist_ok=True)
for kind, rows in (("Character", rows_character), ("Social", rows_social)):
    rows.sort(key=lambda r: r["f04"])
    doc = {"c01": rows, "c02": rows[0]["f04"], "c03": rows[-1]["f04"], "c04": 0, "c05": OWNER, "c06": 1}
    with open(os.path.join(folder, f"{OWNER}_{kind}_{DAY:%Y-%m-%d}.json"), "w", encoding="utf-8") as f:
        json.dump(doc, f, indent=2)
print(f"wrote {len(rows_character)} character rows and {len(rows_social)} social rows to {folder}")
