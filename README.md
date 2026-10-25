# Companion

**See who looted what in Monsters & Memories, and what it was worth, without touching the game.**

*Companion for Monsters & Memories (the project is `MnMCompanion`). Version 0.1.0 was called Loot Goblin.*

<p align="center"><img src="docs/images/window-value.png" alt="The Companion window showing each player's coin and item value" width="360"></p>

Companion sits beside the game and keeps a running tally of your group's loot: every item and every coin purse, who took it, which
monster it came from, and roughly what a vendor would pay for it. At the end of a night it can tell you exactly who got what.

> **Status: private beta.** Everything below works. While the project is private, the in-app "new version" check can't see the releases
> (GitHub hides them from anyone not signed in), so it quietly does nothing. Until the project goes public, get new versions by hand from
> the Releases page.

---

## Contents

1. [What it does](#what-it-does)
2. [Install it](#install-it)
3. [Your first minute](#your-first-minute)
4. [Finding your way around](#finding-your-way-around)
5. [The History window](#the-history-window)
6. [Sessions: starting a fresh tally](#sessions-starting-a-fresh-tally)
7. [How the numbers are worked out](#how-the-numbers-are-worked-out)
8. [Your privacy, and what it never does](#your-privacy-and-what-it-never-does)
9. [Sharing with the community data](#sharing-with-the-community-data)
10. [Updates](#updates)
11. [Something looks wrong](#something-looks-wrong)
12. [For the curious](#for-the-curious)

---

## What it does

When you play, the game quietly writes a diary of what happens to you and the people around you: items looted, coin picked up, things
sold to vendors, who joined the group. Those diary files are called **ledgers**, and they sit in a folder on your PC. Companion reads
them and turns them into something you can look at:

- **Who looted what**, for you and everyone in your group.
- **Coin and item value** per person, with a share bar so you can see at a glance who is ahead.
- **Which monsters** gave the most, and what they dropped.
- **A live feed** of every loot line as it happens.
- **A full history** of every day, with a page for each player.
- **How often a monster drops an item**, worked out from everything your group has looted ("drop rates").
- **What you sold and bought**, what vendors pay for each item, and how much you made.
- **What you gathered**, how fast, and what it's worth.
- **Your trades**, and what you dropped, picked up or destroyed.
- **A yes/no choice to add your own numbers to a community-run, open database** of drop rates and prices, and to use it (see [Sharing](#sharing-with-the-community-data)).
- **A copy button** that turns any of it into text you can paste into chat.

It only *reads*. It never touches the game, never reads the game's memory, and never presses a key for you.

---

## Install it

You need Windows 10 or 11 (64-bit). **Nothing else has to be installed.**

1. Open the project's [**Releases** page](https://github.com/ExploreTheFloor/MnMCompanion/releases) and download two files from the newest release:
   - **`MnMCompanion.exe`**, the program;
   - **`MnMCompanion.exe.sha256`**, its checksum (optional, see step 3).
2. Put `MnMCompanion.exe` anywhere you like (a folder on your desktop is fine) and double-click it.
3. *Optional: check the download wasn't damaged.* Open PowerShell in the folder and run
   `(Get-FileHash MnMCompanion.exe).Hash`. The long string it prints should match the one in `MnMCompanion.exe.sha256`
   (capital or small letters doesn't matter).

**Windows may warn you the first time** ("Windows protected your PC"). That happens to any program that hasn't paid for a code-signing
certificate. Click **More info**, then **Run anyway**. If you want to be sure, do step 3 first.

To uninstall, close it and delete the file. Its settings and saved history live in `%LOCALAPPDATA%\MnMCompanion\` (paste that into the
File Explorer address bar); delete that folder too if you want everything gone. If you used it when it was called Loot Goblin, the
first start moves your old `%LOCALAPPDATA%\LootWindow\` folder to that name, so your settings and saved history carry on.

---

## Your first minute

1. Start the game and play a little (or just start Companion; it reads yesterday's loot too).
2. Run **MnMCompanion.exe**. A narrow window appears on the right side of your screen, on top of other windows.
3. At the top left, next to the title, is your **character's name with a small arrow**. If you have more than one character, click it
   and pick the one you want.
4. That's it. Loot shows up within a couple of seconds of being looted, and the window refreshes itself.

If the window says it can't find any ledger, see [Something looks wrong](#something-looks-wrong).

---

## Finding your way around

### The title bar

<img src="docs/images/window-update.png" alt="The window with an update banner under the title bar" width="300" align="right">

From left to right: the **name** of the program, the **server**, your **character** (click either to switch), then on the right an **ⓘ** button
(about, and updates), a **pin** (keep the window on top of the game, or let other windows cover it) and **close**.
Companion **follows the character you're playing**: when the game starts writing a different character's ledger, the window switches to
them (and says so in the status line), and when it starts it opens on whichever character the game wrote last. If you pick a different
character yourself, it stops following so the window doesn't jump back; tick **Follow the character I'm playing** in either menu to turn it
back on. (If the character you're looking at is still being written, it stays, even with a second game open.)

Pick the **server** first, then the **character** to follow. Both lists come from the folders the game keeps on your PC (one folder per
server, one per character inside it), so they're always whatever the game has on your PC. Choosing a server follows its most recently played
character; the character list then shows only that server's characters.
Drag the title bar to move the window; drag any edge or the corner grip to resize it. It remembers where you left it.

When a new version is available, a strip appears under the title bar (picture on the right). More on that in [Updates](#updates).

### The header

Under the title bar you'll see the **day** you're looking at (use the arrows to go back or forward), a big number for the **total worth**
of everything looted, and a smaller line breaking it into **coin** and **items**. Next to the day is a menu that says **Whole day** or
**This session**; see [Sessions](#sessions-starting-a-fresh-tally).

The four buttons on the right of the header are:

| Button | What it does |
|---|---|
| **History** (clock) | Opens the big window with every day, every player, every item. |
| **New session** (circle arrow) | Saves what's been looted so far and starts a fresh tally. It asks first, and it can be undone. |
| **Copy summary** (pages) | Copies what you're looking at to the clipboard as plain text, ready to paste. |
| **Open ledger folder** (folder) | Opens the folder where the game keeps your ledger files. |

Hover over any button and a tooltip tells you what it does.

### The four tabs

| Tab | Shows |
|---|---|
| **Value** | One bar per player: how many monsters they looted, how many items, and what it was all worth. The bar is their share of the group's total. Click a name to open their page in History. |
| **Items** | Every kind of item looted, how many, what they're worth, and who took the most of them. |
| **Mobs** | Every kind of monster looted, how many times, and what each one is worth on average. |
| **Feed** | Every loot line, newest first, with the time, who, what, and what it came from. A box at the top filters it by player, item or monster. |

<p align="center">
  <img src="docs/images/window-items.png" alt="The Items tab" width="260">
  <img src="docs/images/window-mobs.png" alt="The Mobs tab" width="260">
  <img src="docs/images/window-feed.png" alt="The Feed tab" width="260">
</p>

### Those little symbols after a number

Not every item has a known price (more on that [below](#how-the-numbers-are-worked-out)), so Companion is honest about it:

- **`~`** after a number means *"includes an estimate"*: some items in the total were priced by comparison with similar items.
- **`+`** after a number means *"and some more"*: some items had no price at all and aren't counted.

So `2gp 16sp 62cp+` reads "at least this much." The footer tells you how many items are unpriced.

---

## The History window

Press the **History** button for the big window. Down the left is a **rail** with a page for each kind of thing that happens to you in the game:

| Page | What it's for |
|---|---|
| **Loot** | Who looted what, day by day, and how often monsters drop things. |
| **Trades** | What you gave other players and what you got. |
| **Vendors** | What you sold and bought, and what vendors pay for every item. |
| **Drops / Pickups / Destroys** | Items you put on the ground, picked up, or deleted. |
| **Harvest** | What you gathered, how fast, and what it's worth. |

Under each name on the rail is a small count, so you can see at a glance which pages have something.

### The bar across the top (every page except Loot)

<img src="docs/images/history-vendors.png" alt="The Vendors page, with the period bar across the top" width="100%">

The strip at the top chooses **when**: **Today**, **Yesterday**, **7 days**, **30 days**, **All time**, or **Pick a day** for any single day that has something on it. The box on the right **filters** by an item, trader or place (type "backpack" and every page shows only rows with a backpack in them). Your choice stays as you move between pages. Every page also has a **Copy summary** button, and every table sorts when you click a column heading.

### Loot

<img src="docs/images/history-loot.png" alt="The Loot page showing the whole group for one day" width="100%">

The Loot page has three columns:

- **Left: the days.** Every day you have loot for, with the day's total, and a box to filter by player, item or monster.
- **Middle: the group.** *Everyone* at the top, then each player with a coloured bar.
- **Right: the details** for whatever you picked, with tiles for the headline numbers (monsters looted, coin, item value, best item, and "your splits": your share of the coin purses) and tabs underneath.

Click **Everyone** and the tabs are **Looters**, **Items**, **Mobs**, **Drop rates** and **Loot log**. Click a **player** and you see their own **Items** (how many they took, the price of each, their share of the group's, and which monster they mostly came from), **Mobs** and **Loot log**.

<img src="docs/images/history-player.png" alt="The Loot page showing one player's items" width="100%">

#### Drop rates: "how often does this monster drop that?"

The **Drop rates** tab answers it from everything your group has looted, across **every day** in the ledger (a much bigger sample than a single night).

<img src="docs/images/history-drop-rates.png" alt="The Drop rates tab listing monsters" width="100%">

Pick **By monster** or **By item**:

- **By monster:** every kind of monster, with how many of its corpses were looted, the coin and items it gives on average, and its likeliest item. Click one to see **every item it has given**, ranked by how often.
- **By item:** every item, with the monsters it comes from. Click one to see where to find it.

<img src="docs/images/history-drop-detail.png" alt="One monster's drop table" width="100%">

For each item you get a **share** (out of all the corpses of that monster that were looted, how many had the item), a **range** (how sure that share is; it's wide when there aren't many corpses), how many corpses that is, how many items, and what a vendor pays for one.

**Please read this part, because it's honest about what the numbers can and can't be:**

- It's a share of **looted corpses, not of kills.** The game's diary only records what someone *took*. A corpse nobody opened leaves no trace, and an item someone chose to leave behind isn't recorded, so real drop rates are **at least** what you see.
- A corpse that two players both looted counts twice.
- Monsters are kept apart by **zone**, because the same name in two zones can have different loot.
- A **star (\*)** means fewer than 20 corpses were looted. The share is then a rough guess, and the range will tell you so.

### Trades

<img src="docs/images/history-trades.png" alt="The Trades page" width="100%">

One line per trade: what you **gave** and what you **received**. The game doesn't record the other player's *name*, only a number, so they show as **Trader #71358**. The same number is always the same player, so you can spot repeat customers. (The game writes two entries for each trade, one per side, and they are put back together here.)

### Vendors

The Vendors page has four tabs:

- **Sales:** every item you sold in the period, with how many, the price you got for one, and the total.
- **Purchases:** the same for what you bought (food, water, supplies).
- **Price book:** every price this PC has ever seen, for every character, all time. This is where item values everywhere in the app come from.
- **Log:** every single stack, newest first.

<img src="docs/images/history-price-book.png" alt="The Vendors page, Price book tab" width="100%">

The tiles show **Sold for**, **Bought for**, and the **Net** between them (what the vendors paid you minus what you spent). In the **Price book**, "Pays" is the middle price vendors paid for one, and "Range" is the lowest and highest. Different vendors pay slightly different amounts and tiny prices get rounded, so a range is normal. Sell one of an item you've never sold and it gets a price.

### Harvest

<img src="docs/images/history-harvest.png" alt="The Harvest page" width="100%">

What you gathered: how many harvests, how many items, the **time you actually spent gathering** (a break of more than 10 minutes isn't counted), and so **items per hour**. **Worth** uses the same vendor prices as the loot, with the same `~` (estimated) and `+` (some items unpriced) marks. The game records the item and the zone, but not which node it came from.

### Drops / Pickups / Destroys

<img src="docs/images/history-changes.png" alt="The Drops, Pickups and Destroys page" width="100%">

What you did with items you were holding: **dropped** them on the ground, **picked up** things off the ground, or **destroyed** them. None of it is about monsters. It's for answering "where did that item go?" **Taken back** counts the things you picked up within 10 minutes of dropping them. **Most discarded** ranks what's gone for good.

---

## Sessions: starting a fresh tally

A **session** is just "since I last pressed New session." It's how you answer *"what did we get from this one dungeon run?"* without it being
mixed in with the whole day.

- Press **New session** (or use the **Whole day** menu → *New session*) and Companion saves the old tally as a text file and starts again
  from zero. **Nothing is deleted**: the game's ledger still has everything, and the *Whole day* view still shows it all.
- Use the **Whole day / This session** menu to switch between the current session, an earlier one, or the whole day.
- Pressed it by accident? Open that menu and choose **Undo the last new session**; the two merge back together.
- The saved text files are in `%LOCALAPPDATA%\MnMCompanion\<server>-<character>\sessions\`.

Sessions start only when you press the button; they don't start by themselves when the group changes.

---

## How the numbers are worked out

### Where the loot comes from

The game's ledger records every item and every coin purse that anyone in range looted, along with the name of the kind of monster it came
from. Companion adds it up.

- **Your own loot** comes from your character's own ledger file.
- **Everyone else's** comes from the "social" ledger: what your group and the people around you looted.
- The game also moves older social entries into an **archive** folder from time to time. Those entries exist nowhere else, so Companion
  reads the archive too. (Skipping it would lose about half of what other players looted.)

### What counts as "a mob looted"

The ledger doesn't say *which* corpse something came from, only what kind of monster. So Companion treats one player's items and
purse from one kind of monster, looted within six seconds of each other, as one corpse. It's an excellent guess, not a certainty.

### What coin is worth

100 copper make 1 silver, 100 silver make 1 gold, 100 gold make 1 platinum. A coin purse is split between group members by the game; the
ledger only records *your* share of other players' purses, so Companion shows exactly that as "your splits."

### What an item is worth

There's no price list inside the game, so Companion works it out **from what vendors actually paid you**:

1. **Seen it sold:** the middle value of what vendors paid for that item (across all your characters on this PC). No mark.
2. **Never sold, but a similar item was:** items whose names start the same way (for example "Rusty Iron ...") sell for about the same, so it
   borrows the family's price. Shown with **`~`**.
3. **Nothing to go on:** left out of the total and marked with **`+`**.

Sell one of an unpriced item to any vendor and from then on it's priced, for every character. You don't have to do anything else.

The price is *what a vendor pays*, not what another player might pay. It's a floor, not a market value.

---

## Your privacy, and what it never does

**It reads files. That's all.** Companion:

- reads the game's ledger files, and nothing else the game writes;
- **never** touches the running game, its memory, your keyboard or your mouse;
- **never** sends your loot, your characters, your ledgers or anything about you anywhere, **unless you answer Yes** to sharing your data with the community (below), and even then only anonymous totals, never names;
- **never** installs anything by itself. Updates are only ever *offered*.

The one thing it does over the internet is ask GitHub *"is there a newer version of Companion?"* (see [Updates](#updates)). That's a plain
request that carries only the program's name and version number. You can switch it off.

The other players' names you see in the group come from your own ledger files; they never leave your PC.

**What it saves on your PC**, all in `%LOCALAPPDATA%\MnMCompanion\`:

| File | What |
|---|---|
| `settings.json` | Which character you picked, where the windows sit, whether to check for updates. |
| `<server>-<character>\` | Your session markers and the text files for archived sessions. |
| `ledger.db` | A private copy of **every ledger row** the app has read (see below). |

### The saved copy (`ledger.db`)

The game moves old rows out of its files and may delete files over time, so Companion keeps its own permanent copy of everything it reads,
in a small database file. It includes **every kind of entry** (loot, harvests, vendor sales and purchases, trades, level-ups, group changes,
and entries nobody has identified yet), for **every server and character** on the PC. Nothing in it is ever changed or deleted, and
nothing leaves your PC.

The History window's **Trades, Vendors, Drops / Pickups / Destroys and Harvest** pages read from it (the small panel and the Loot page read
the game's files directly). It's also there for views still to come, built from your own complete history. What the entries mean, and
how to look inside the file, is written up in [docs/ledger-notes.md](docs/ledger-notes.md).

---

## Sharing with the community data

The game's diary tells each player only a little about how the world works. Pooled across many players it tells a lot: how often each monster drops each item, what vendors really pay, what gathering yields. That is what the **community data** is: an open database, kept in a public project on GitHub ([ExploreTheFloor/MnMData](https://github.com/ExploreTheFloor/MnMData)), that anyone can use for non-commercial purposes ([CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/)).

**It's one question, Yes or No: "Share my data with the community?"**

| | **Yes** | **No** (the default) |
|---|---|---|
| Your totals | shared **for you, automatically**, and kept up to date | stay on your PC; **nothing is sent** |
| Community data | **you can use it** | **the app doesn't download or show it** |
| Being asked | never again | **every time you start the app**, until you say Yes |

The first time you start the app (and every time after, while the answer is No) a small window asks. **Yes** turns sharing on and it stops asking. **No**, or closing the window, keeps everything private and asks again next time. You can change your answer at any time: click the **ⓘ** in the title bar and tick or untick **Share my data with the community**.

**What gets shared is only totals of your own play**, shown to you in full before you decide (**ⓘ → What does sharing include?**, or the **What is shared** fold-out on the question):

- how often each kind of monster you looted dropped each item, and the coin in its purses;
- what vendors paid and charged you, per item and zone (the game doesn't record which vendor);
- what you gathered, per item and zone;
- item names with the game's own ids.

It **never** contains your character names, any other player's name, individual loot lines, trades, or when anything happened (only the day). Other players' loot is left out on purpose, so a corpse isn't counted once per party member. It's filed under a random id made on your PC, which isn't linked to your name or characters; each time your totals are shared they **replace** your last ones, so nothing is counted twice, and you can ask to have yours removed.

**An honest note about "No means no community data":** the community data lives in a public project under an open licence, so anyone can still download it by hand. What "No" means is that *Companion* won't fetch or use it for you.

> **Status:** the Yes/No question, the reminder and the rule are in. The part that actually sends a Yes player's totals needs a small community "drop box" to be set up first, so for now saying Yes saves your answer and the ⓘ window says "Sending isn't connected yet". Nothing is sent until it is. Until then, `MnMCompanion.exe --contribute <folder>` saves your totals as a file you can read or upload to the community project yourself.

---

## Updates

<img src="docs/images/window-update.png" alt="The update banner" width="260" align="right">

A few seconds after it starts, and every six hours while it's running, Companion asks GitHub whether a newer version has been released.
If there is one, a strip appears under the title bar:

> **Version 0.2.0 is ready (you have 0.1.0).**   `Update now`   `Later`   `Skip this version`

- **Update now** downloads the new program, **checks it against the checksum published with the release**, and only if that matches replaces
  the old one and restarts. If anything doesn't match (a damaged or tampered download), it throws the download away, keeps your current
  version, and tells you why.
- **Later** hides the strip until the next check.
- **Skip this version** stops it being offered again. A newer one after it still will be.
- Hover over the message to read the release notes.

Click the **ⓘ** in the title bar for **Check for updates now**, and an option to **switch automatic checking off**.
Nothing is ever installed unless you press **Update now**.

If you run Companion from somewhere it can't replace itself (a read-only folder, say), the button becomes **Open the download page** instead.

---

## Something looks wrong

**The window says it can't find a ledger, or my character isn't in the list.**
Companion looks in `C:\Users\<you>\AppData\LocalLow\Niche Worlds Cult\Monsters and Memories`. Click the server name, then
**Choose the game's data folder…** and point it at that folder (or wherever your game keeps its data). A character with no ledger yet
is still listed (marked *no ledger yet*): the game writes the file as you loot, sell, level up and so on, and the window picks it up by itself.

**"I don't see my old loot!"**
You probably pressed **New session**. The window then shows only the *current* session. Open the **This session** menu and pick
**Whole day** (or **Undo the last new session**). Nothing was lost.

**Some of my items show no price, or a `~`.**
See [What an item is worth](#what-an-item-is-worth). Sell one of that item to a vendor and it will be priced.

**The totals don't match what I remember.**
Prices are *what a vendor pays*, not what you could sell for to a player, and coin splits are only known for your own share. The
corpse count is an inference (see above). Totals with a `~` or a `+` are estimates.

**Windows says "Windows protected your PC".**
Normal for an unsigned program. **More info → Run anyway.** To be sure the file is the one that was published, check its checksum
([Install it](#install-it), step 3).

**My antivirus complained.**
Single-file programs like this one sometimes get flagged by cautious antivirus software. If you'd rather not run a downloaded program,
you can build it yourself from the source (below).

**The window is on the wrong monitor, or off the screen.**
It pulls itself back onto the desktop when it starts. If it's on the wrong screen, drag it by the title bar.

**It hasn't updated and there's a newer version.**
While the project is private, the automatic check can't see releases. Download the new `MnMCompanion.exe` from the Releases page and replace
the old one; your settings and history are kept.

---

## For the curious

### Command-line options

Run these from a command prompt or a shortcut. None of them is needed for normal use.

| Option | What it does |
|---|---|
| `--ledger <folder>` | Read the game's data from somewhere else. |
| `--character <name>` | Start on this character. |
| `--history` | Open the History window as soon as the app starts. |
| `--share` | Open the "Share my data with the community" window as soon as the app starts. |
| `--no-share-prompt` | Start without asking the sharing question (for scripts and pictures). |
| `--contribute <folder or file>` | Save the file you can share with the community data (named by your random id) and quit, without a window. |
| `--archive [--db <file>] [--report <file>]` | Copy every ledger row into the saved database once, write what it did to a report file, and quit without showing a window. |
| `--about [--report <file>]` | Write the version, whether this copy can update itself, and where its files are, to a file, and quit. |
| `--snapshot <png> [--view Value\|Items\|Mobs\|Feed] [--day yyyy-MM-dd] [--size WxH] [--window [--preview-update]]` | Draw the panel to a picture and quit (how the pictures in this README were made). |
| `--snapshot <png> --breakdown [--page Loot\|Trades\|Vendors\|Changes\|Harvest] [--member <name>] [--tab <tab>] [--drop "<monster>"] [--size WxH]` | The same for the History window: any page, any tab, or one monster's drop table. |

To try it without the game, `scripts/make-demo-ledger.py` writes a made-up game folder full of invented players:
`python scripts/make-demo-ledger.py demo-data`, then `MnMCompanion.exe --ledger demo-data --character Brindlewick`.
`scripts/make-screenshots.ps1` uses that to remake every picture in this README (nothing appears on your desktop and no real player is shown).

### Building it yourself

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build MnMCompanion.sln
dotnet test  MnMCompanion.sln
src\MnMCompanion\bin\Debug\net10.0-windows\MnMCompanion.exe
```

A run from the source folder can't replace itself, so it will send you to the download page instead of updating.

`LOOTWINDOW_REAL_LEDGER=<character>` makes one test read that character's real ledger and print what it found, for comparing against counts made
straight from the files.

### Making a release

1. Raise `<Version>` in `src\MnMCompanion\MnMCompanion.csproj` (for example `0.1.1`) and commit and push.
2. Run `scripts\build-release.ps1`. It runs the tests, builds the single-file `MnMCompanion.exe`, writes `MnMCompanion.exe.sha256`
   beside it in `dist\`, and checks the built program starts and reports that version.
3. Add `-Publish` to create the GitHub release `v<version>` with both files attached
   (`scripts\build-release.ps1 -Publish -Notes "What changed"`). The app's update check looks for exactly those two file names.

### How the project is laid out

```
src/MnMCompanion.Core     reads the ledger and works out the loot, drop rates and histories (no windows, no packages)
src/MnMCompanion.Data     the saved database and the history tables built from it (the only project that may use SQLite)
src/MnMCompanion.Update   asks GitHub for a newer version and downloads it, checked against its SHA-256
                        (the only project that may use the network, and only github.com)
src/MnMCompanion          the Windows app: the panel, the History window and its pages, the update banner
tests/MnMCompanion.Tests  the automatic tests
scripts/                build-release.ps1, make-demo-ledger.py, make-screenshots.ps1
docs/                   ledger-notes.md (what the ledger holds), images/
```

A few tests exist purely to keep the promises above: they fail the build if any source file reaches for the game, someone else's
database, or the network, anywhere except the two projects that are allowed to. Updating, checksums, and swapping the program in (and
back out if the new one won't start) are all tested without a network.

### Not yet

- One character at a time. A combined view of several characters needs the same party's loot de-duplicated across their folders.
- Sessions only start by hand.
- Drop rates use one character's ledger at a time. Pooling several characters' ledgers for bigger samples needs the same party's loot
  de-duplicated across their folders.
- There is no combined "net" figure across pages. The ledger records your own coin purses whole even when you're in a party, so
  adding them to vendor sales would mislead. The Vendors page shows sold, bought and net *from vendors*, which is certain.
- The Loot page and the small panel still read the game's files directly; they could move onto the saved copy later.
- No licence has been chosen, so for now it is all rights reserved.
