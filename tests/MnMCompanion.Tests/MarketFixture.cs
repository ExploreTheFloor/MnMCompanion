namespace MnMCompanion.Tests;

/// <summary>
/// A small stand-in for mnm-db.com's auctions.json, in its shape: listings (priced, unpriced, bulk by the stack, a bid, one the wiki doesn't know,
/// one whose intent couldn't be read), gear requests, item cards and stack sizes. Every name in it is invented.
/// </summary>
internal static class MarketFixture
{
    public const string GeneratedAt = "2026-10-31T19:00:04.996Z";

    public const string Json = """
    {
      "generatedAt": "2026-10-31T19:00:04.996Z",
      "capture": { "liveCycles24h": 664, "readingCycles24h": 638, "offlineCycles24h": 0, "longestBlindMin": 52, "blindNowMin": 0, "lastLinesAt": "2026-10-31T18:58:00.833Z" },
      "listings": [
        { "server": "Trem", "intent": "sell", "item": "Copper Ore", "price": 500, "unit": 25, "priceStr": "5s", "stack": 20, "perStack": true, "player": "Avelin", "seen": "2026-10-31T18:57:49.031Z", "count": 2, "matched": true },
        { "server": "Trem", "intent": "sell", "item": "Copper Ore", "price": 300, "unit": 15, "priceStr": "3s", "stack": 20, "perStack": true, "player": "Borrik", "seen": "2026-10-30T09:10:00.000Z", "count": 9, "matched": true, "qty": 3 },
        { "server": "Trem", "intent": "buy", "item": "Copper Ore", "price": 200, "unit": 10, "priceStr": "2s", "stack": 20, "perStack": true, "player": "Cerys", "seen": "2026-10-31T12:00:00.000Z", "count": 1, "matched": true },
        { "server": "Trem", "intent": "sell", "item": "Rawhide Backpack", "price": 500, "unit": 500, "priceStr": "5s", "player": "Dallin", "seen": "2026-10-30T05:24:55.719Z", "count": 16, "matched": true },
        { "server": "Trem", "intent": "sell", "item": "Rawhide Backpack", "price": 1200, "unit": 600, "priceStr": "12s", "player": "Elowen", "seen": "2026-10-31T17:00:00.000Z", "count": 1, "matched": true, "qty": 2 },
        { "server": "Trem", "intent": "buy", "item": "Barnacle Bracer", "price": 40000, "unit": 40000, "priceStr": "4g", "player": "Faelan", "seen": "2026-10-31T18:57:49.031Z", "count": 1, "matched": true },
        { "server": "Trem", "intent": "sell", "item": "Iron Longsword", "price": 25000, "unit": 25000, "priceStr": "2g 50s", "player": "Gwil", "seen": "2026-10-20T10:00:00.000Z", "count": 3, "matched": true, "assumed": true },
        { "server": "Trem", "intent": "sell", "item": "Flux", "price": null, "unit": null, "priceStr": null, "player": "Hadria", "seen": "2026-10-31T18:00:00.000Z", "count": 1, "matched": true },
        { "server": "Trem", "intent": "sell", "item": "TB", "price": 500, "unit": 500, "priceStr": "5s", "player": "Ingram", "seen": "2026-10-31T18:00:00.000Z", "count": 1, "matched": false },
        { "server": "Trem", "intent": null, "item": "Wolf Pelt", "price": 700, "unit": 700, "priceStr": "7s", "player": "Jorah", "seen": "2026-10-31T18:00:00.000Z", "count": 1, "matched": true },
        { "server": "Trem", "intent": "trade", "item": "Wolf Pelt", "price": 700, "unit": 700, "priceStr": "7s", "player": "Kestrel", "seen": "2026-10-31T18:00:00.000Z", "count": 1, "matched": true }
      ],
      "requests": [
        { "server": "Trem", "player": "Lirael", "plus": [7], "stats": [], "category": "jewelry", "text": "+7 Jewelry - Neck/Ear/Ring - All Stats - 2g Each", "seen": "2026-10-20T17:18:20.037Z", "lastSeen": "2026-10-31T05:02:16.993Z", "count": 28 },
        { "server": "Trem", "player": "Maelis", "plus": [3], "stats": ["str", "sta"], "category": "ring", "text": "+3 Str/Sta ring", "seen": "2026-10-30T19:21:09.254Z", "lastSeen": "2026-10-31T17:48:43.031Z", "count": 2 }
      ],
      "stats": {
        "copper ore": { "name": "Copper Ore", "hasPage": true, "icon": "https://static.wikitide.net/monstersandmemorieswiki/thumb/4/4d/Itemicon0118AB.png/64px-Itemicon0118AB.png", "flags": [], "slot": null, "handed": null, "dmg": null, "delay": null, "skill": null, "ac": null, "stats": {}, "hp": null, "mana": null, "weight": 0.25, "size": "SMALL", "container": null, "effect": null, "class": null, "race": null, "tradeskills": ["Smelting"], "zones": [], "from": [], "vendor": null },
        "rawhide backpack": { "name": "Rawhide Backpack", "hasPage": true, "icon": null, "flags": [], "slot": "Backpack", "weight": 2.5, "size": "EXTRA LARGE", "container": { "capacity": 10, "maxSize": "LARGE", "weightReduction": null }, "class": "ALL", "race": "ALL", "tradeskills": ["Leatherworking"], "vendor": null },
        "iron longsword": { "name": "Iron Longsword", "hasPage": true, "slot": "Primary", "handed": "1H Slashing", "dmg": 6, "delay": 30, "stats": { "str": 2, "sta": 1 }, "weight": 5.5, "class": "WAR PAL", "race": "ALL", "tradeskills": ["Blacksmithing"], "vendor": 1250 },
        "barnacle bracer": { "name": "Barnacle Bracer", "hasPage": true, "slot": "Wrist", "ac": 4, "stats": { "dex": 3 }, "weight": 1.0 }
      },
      "stacks": { "copper ore": 20, "wood": 20 }
    }
    """;
}
