# What the game's ledger holds

Worked out from real files on 2026-10-24: 76 files, 29,760 rows, all on one server (13 characters across two servers, but only six
had any ledger rows: the busiest 51 files, then 18, 4, and three with 1 each; the other seven, including the one on the second
server, had an empty Ledger folder). Everything here is kept in `ledger.db` (see the README);
this file is the reading guide to it. Counts are a snapshot and grow.

## Files and rows

`<server>\<character>\Ledger\` holds `<name>_Character_<date>.json` (what the character did), `<name>_Social_<date>.json` (the party and
players nearby, the character's own rows included as twins) and `Archive\ledger_<name>_<Character|Social>_archive_<stamp>.json` (the
oldest rows the game moves out of a dated file, 500 at a time; found for Social, and for Character on one character). Each file is
`{"c01":[rows], "c02":…, "c03":…, "c04":…, "c05":…, "c06":…}`; the `c0x` fields beside the row list are kept in `ledger_file.envelope`
(`c02` and `c03` look like the first and last write time, `c05` the character, `c04`/`c06` numbers nobody has explained).

A row has: `f01` act, `f02` a name or reference (prefixes `name_`, `zone_`, `ref_`; often base64), `f03` a JSON string of detail
fields (`d01`…, most strings base64), `f04` time with offset (100 ns), `f05` zone (`zone_<base64>`), `f06` the actor's level, `f07` the
actor, `f08` 0 for a Character file and 2 for Social, `f09` the file's owner, `f10` the actor's class code (absent from some Character
rows; the owner's own class reads `None` or empty). In the database the same row is in `raw` (as written), `detail` (f03 with base64
decoded) and the columns `act name zone level actor owner class ledger_type at_utc`.

## Acts

Certainty: **confirmed** = the payload or the game client's own list of ledger calls says so; **inferred** = fits every sample.
The `act_code` table holds the same list.

| Act | Meaning | Rows | Fields worth knowing |
|---|---|---:|---|
| act_13 | Item looted | 18,378 | `d02` mob (kind, not corpse), `d04` item, `d05` 24-hex item id, `d01` quantity (always 1). `f02` `ref_N` is an item reference, **not** a corpse id. Social: other players' loots |
| act_14 | Coin purse looted | 6,679 | `d13` mob, `d12` coin `pp,gp,sp,cp` (**100 per step**; `d11` counts 10 and is wrong), `d14` `npc_corpse` / `party_split`, `d15` the file owner's split. Exists only for corpses that had coin |
| act_27 | Harvest | 1,828 | `d04` item, `d01` quantity, `d03` `harvest`. Character only |
| act_01 | Level up | 663 | `d21` new level, `d22` old level. Social: other players' |
| act_24 | Sold to a vendor | 544 | `d04` item, `d01` quantity, `d03` coin words for the whole stack. Vendor name (`d02`) empty in every row |
| act_32 | Joined a party | 439 | `d63` `PartyJoin`, `d64` group reference, `d62` member count (after the event, inferred). Joining someone else's group writes only your own join row |
| act_11 | Item dropped | 332 | `d03` `user_confirmed_drop` |
| act_34 | Left a party | 197 | `d63` `PartyLeave` |
| act_16 | Trade completed (you gave) | 146 | `partner` (an id, not a name), `currency`, `items` list |
| act_12 | Item picked up | 138 | inferred |
| act_20 | Trade received | 104 | same fields as act_16, `summary` `received_from_trade` |
| act_35 | Party leader changed | 104 | `d63` `PartyLeaderChange` |
| act_15 | Item destroyed | 79 | `d03` `user_confirmed_destroy` |
| act_33 | Party disbanded | 46 | `d63` `PartyDisband` |
| act_28 | Bought from a vendor | 30 | like act_24 |
| act_31 | Party created | 15 | `d63` `PartyCreate` |
| act_36 | **Unidentified** | 25 | Social; a player name and class, no detail. Nobody knows what it records |
| act_02 | **Unidentified** | 13 | no detail, only a zone; not obviously a death |

The game client also defines `act_04`, `act_05`, `act_17`–`act_19`, `act_21`–`act_23`; none has ever appeared in a file here.

## What the ledger does not record

Deaths, zone changes, kills and experience, quest item hand-ins, and coin drops/pickups/destroys: the client has calls for them and
none ever reaches a file. A guild ledger type exists in the client; there are no guild files. Loot quantity is always 1. There is no
corpse id and no vendor name. Other players' sales, harvests, trades and drops are not in your files, and you see only your own share
of a split. There is no combat, damage or chat.

## Things that bite when reading it

- **Own rows come twice.** A character's own actions are in its Character file and again, a moment later, in its Social file
  (`actor = owner`). Count the Character copy and skip the Social twin. The database keeps both; filter on `kind` / `actor <> owner`.
- **Own purses carry no split.** The owner's own purses in a party are always `npc_corpse` with the whole amount. The owner's share of
  *other players'* purses is in their `party_split` rows' `d15`.
- **Archive and dated files can overlap.** On 2026-10-24 they did not (zero repeated rows), but an earlier check found overlap, so
  anything built on the table should count a row once. The database already does (one row per server, character, file kind and exact row).
- **A corpse is inferred.** One looter's rows from one mob name, each within six seconds of the last, a second purse starting another
  corpse. An items-only corpse followed within six seconds by another of the same name merges.
- **Rows without a readable time are skipped on import.** None exist today.

## Drop percentages are possible, with a caveat

Aggregating `act_13` and `act_14` by mob and zone gives how often each item came off a *looted corpse*. Method used (one character's folder:
own Character rows plus other players' Social rows, archive de-duplicated, 15,691 rows → 7,368 corpses → 137 mob/zone pairs):
group into corpses as above, then for each item count the corpses that gave it at least once, with a 95% Wilson interval.

| Mob (zone) | Corpses | Item | Share of corpses |
|---|---:|---|---|
| a restless skeleton (fallenpass) | 908 | Bone Chips | 54.7% (51–58) |
| | | Wool Scraps | 15.4% (13–18) |
| | | Weathered Medallion | 12.1% (10–14) |
| a Dustrend legionary (valeofzintar) | 770 | Raw Orc Meat | 17.0% (15–20) |
| | | Polished Medallion | 15.7% (13–18) |
| a crocodile hatchling (nightharborw) | 530 | Crocodile Tooth | 71.3% (67–75) |
| | | Crocodile Hide | 21.1% (18–25) |
| | | Pristine Crocodile Hide | 0.8% (0–2) |
| a bandit (shadeddunes) | 468 | Cloth Scraps | 37.4% (33–42) |
| | | Worn Bow | 8.3% (6–11) |

These are shares of **looted corpses, not of kills**. A corpse nobody looted leaves no row (the crocodile hatchlings have no purses at
all, so every counted corpse gave something and their shares read high). An item a looter chose to leave behind is never recorded, so
shares are floors. Key by mob *and* zone, show the corpse count, and hide shares below about 20 corpses.

## Querying `ledger.db`

SQLite with the JSON functions. Tested on the real file:

```sql
-- Rows per act, with what the act means
SELECT r.act, c.meaning, count(*) FROM ledger_row r LEFT JOIN act_code c USING (act) GROUP BY r.act ORDER BY 3 DESC;

-- Your own item loots by mob and item (the Character copy only)
SELECT json_extract(detail,'$.d02') AS mob, json_extract(detail,'$.d04') AS item, count(*) AS n
FROM ledger_row WHERE act = 'act_13' AND kind = 'Character' GROUP BY 1, 2 ORDER BY n DESC;

-- Harvest units by item
SELECT json_extract(detail,'$.d04') AS item, sum(json_extract(detail,'$.d01')) AS units
FROM ledger_row WHERE act = 'act_27' GROUP BY 1 ORDER BY units DESC;

-- Party history for one character
SELECT at_utc, act, actor, json_extract(detail,'$.d63') AS what, json_extract(detail,'$.d62') AS members
FROM ledger_row WHERE act IN ('act_31','act_32','act_33','act_34','act_35') AND character = 'Aldric' ORDER BY at_utc;

-- Other players' loots, by looter
SELECT actor, class, count(*) FROM ledger_row WHERE act = 'act_13' AND kind = 'Social' AND actor <> owner GROUP BY actor ORDER BY 3 DESC;
```

`at_utc` is ISO 8601 UTC with 7 fractional digits, so it sorts as text. `raw` always has the row exactly as the game wrote it.
