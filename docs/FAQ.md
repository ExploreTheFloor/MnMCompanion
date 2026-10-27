# Frequently asked questions

Short answers, with a link to the long one where there is one. The [README](../README.md) has everything in full.

**Contents:** [About](#about) · [Installing and updating](#installing-and-updating) · [Using it](#using-it) · [Sharing with the community](#sharing-with-the-community) · [When something looks wrong](#when-something-looks-wrong)

---

## About

### What is Companion?
A small Windows program that sits beside Monsters & Memories and shows what the game's own diary files say: who looted what, what it was worth, what each monster
dropped, what you sold, bought, gathered and traded, with a History window for every day. See [What it does](../README.md#what-it-does).

### Will it get me in trouble with the game?
Companion only **reads two kinds of files the game writes for you** (the ledger and each character's NPC journal). It does not look at the game's memory or the screen,
does not touch the running game, and never presses a key or moves the mouse for you. That is the whole design, and a test in the source fails the build if any code reaches for
anything else. It is a fan-made tool, not made or endorsed by the game's owners, so the game's own rules are theirs to set: if they ever say a tool like this isn't allowed,
stop using it.

### Is it free? Is it open source?
Free, and open source under the [MIT licence](../LICENSE). The source is in this repository.

### Which computers does it run on?
Windows 10 or 11, 64-bit. Nothing else has to be installed (it carries what it needs inside the one file).

### Who makes it?
An independent fan project. Questions and bug reports go through [GitHub issues](https://github.com/ExploreTheFloor/MnMCompanion/issues/new/choose).

---

## Installing and updating

### Windows says "Windows protected your PC". Is that bad?
It happens to any program from a new publisher that hasn't paid for a code-signing certificate, and Companion isn't signed. Click **More info**, then **Run anyway**.
If you want to be sure the file is the one that was published, check its checksum: in PowerShell, `(Get-FileHash MnMCompanion.exe).Hash` should match the digits in
`MnMCompanion.exe.sha256` from the same release page. If you'd rather not run a downloaded program at all, you can [build it from the source](../README.md#building-it-yourself).

### My antivirus flagged it.
Single-file programs sometimes get flagged by cautious antivirus software. The same checksum check above applies, and building it yourself from the source is always an option.

### How do I update?
It checks GitHub a few seconds after it starts and every six hours, and a strip appears under the title bar when there is a newer version: **Update now** downloads it,
checks it against the checksum published with the release, and only then replaces the old one and restarts. Nothing is ever installed unless you press that button.
You can also use **ⓘ → Check for updates now**. If you are still on **version 0.1.0** (when it was called Loot Goblin), that version can't see newer releases:
download the current `MnMCompanion.exe` once and replace the old file; from then on it updates itself. See [Updates](../README.md#updates).

### How do I uninstall it?
Close it and delete `MnMCompanion.exe`. Its settings and saved history are in `%LOCALAPPDATA%\MnMCompanion\` (paste that into the File Explorer address bar); delete that folder too
if you want everything gone.

### Where does it keep my data?
Only in `%LOCALAPPDATA%\MnMCompanion\`, on your PC: a settings file, a database that keeps a copy of every ledger row it has read (the game throws old rows away; the copy keeps them),
and your saved sessions. Nothing leaves your PC unless you say Yes to [sharing](#sharing-with-the-community).

---

## Using it

### It says it can't find a ledger.
The game has to have run (and you to have played a little) so that it has written one. Companion looks in
`%USERPROFILE%\AppData\LocalLow\Niche Worlds Cult\Monsters and Memories`; if your game keeps its data somewhere else, use **ⓘ → Choose the game's data folder…**.
More in [Something looks wrong](../README.md#something-looks-wrong).

### The window shows a different character from the one I want.
The small window **follows the character you are playing**: the game rewrites the ledger of whoever is logged in, and Companion switches to them. To look at another character,
or at everyone together, open the **History** window (the clock in the title bar) and pick a server and character there.

### How do I see what a monster drops?
Open **History → Loot → Drop rates**, then click a monster. It shows how often each item came off that monster, with a range that is honest about small samples.
Those are shares of **looted corpses**, not of kills (see the next question).

### Why is there no kill count, and no buffs, cooldowns, damage, chat or position?
Because the game doesn't write them to any file Companion reads. The ledger records items looted, coin purses, vendor sales and purchases, harvests, trades, level-ups and
party changes; it has no kills, deaths, casts, buffs, damage or position (we looked through the rest of the game's folders as well, and none of them has these either).
The closest thing to a kill count is **corpses looted**, and that is what Companion counts, so a corpse nobody looted isn't counted.

### Why does some loot show "unpriced"?
Companion values an item at what a vendor pays for it, learned from what **you** sold. Sell one to a vendor and it is priced from then on. (If you share, an item's page in History also shows what the community says vendors pay.)
How values are worked out, including the `~` and `+` marks, is in [How the numbers are worked out](../README.md#how-the-numbers-are-worked-out).

### How is coin counted when I'm in a party with autosplit?
A purse nobody split counts whole for the person who looted it. A purse the group split counts for no looter (they kept only part, and the ledger never says how much), and your own share
of it counts for you. The group total counts every purse once.

### What's a session?
"Since I last pressed **New session**." It answers "what did we get from this one dungeon run?" without it mixing into the whole day. Nothing is deleted when you start one.
See [Sessions](../README.md#sessions-starting-a-fresh-tally).

### Can I paste any of it into chat?
Yes: pages have **Copy** buttons that put the figures on the clipboard as text.

### Does it have timers for my buffs?
No. The game writes nothing about casts or buffs to any file, so the only way would be for you to start every timer by hand, which we decided wasn't worth the bother. (A tool that reads the
game's memory could do it automatically; Companion deliberately never does that.)

---

## Sharing with the community

### What is sharing?
A yes/no choice. **Yes** adds your own totals (how often each monster you looted dropped each item, what vendors paid you, what you gathered, item names, what NPCs said to you, your trades with other players, and running
totals counted by week) to a combined database that every sharing player gets figures back from. **No** (the default) means nothing leaves your PC and the community figures aren't used.
Details in [Sharing with the community data](../README.md#sharing-with-the-community-data).

<img src="images/window-sharing.png" alt="The sharing window, listing exactly what would be shared" width="300" align="right">

### Can I see exactly what would be sent before I decide?
Yes. **ⓘ → What does sharing include?** lists it in plain words (the picture on the right), and **Save a copy of my file…** writes exactly what would be sent so you can read it (the totals, what NPCs said to you as a second small file, and your trades as a third; they are sent as separate small uploads, without indentation). From a command line,
`MnMCompanion.exe --contribute <folder>` does the same.

### What is never sent?
Your character names or any other player's name or number (a trade says what each side handed over, never who the other player was), individual loot lines, the time of day of anything (only
the day the file was made, the weeks the running totals fall in, and the day of each trade), and anything from other players' ledgers. Your character's name inside what NPCs said to you is replaced before it is sent.

### Are my trades with other players shared?
Yes, if you say Yes to sharing: for each trade, the day it happened, the zone, and what each side handed over (the coin, and the items with how many). The ledger gives the other player only as
a number, and that number is left out, so nothing in the file says who you traded with. The newest 3,000 trades are sent. You see them in the sharing window before you decide, and a Yes you gave
before trades were added has to be given again.

### Is it published?
No. The combined database is kept privately by the project and is not published. It is only used to build the figures sharing players get back.

### Why did it ask me again after I updated?
A Yes covers what you were shown. When a new version shares more than the one you said Yes to (version 0.3.0 added the weekly running totals), your earlier Yes no longer covers it, so
sharing pauses and the question comes back saying what was added. Nothing is sent until you say Yes again.

### How do I stop sharing?
Click **ⓘ** and untick **Share my data with the community**. Nothing more is sent. If you also want what was already sent removed, see the next question.

### How do I get my shared data removed?
Open an issue with the **Remove my shared data** form and give the first eight characters of your random id (the sharing window shows it: "Your random id is 74040de9…"). Your id isn't linked
to your name or characters, so that is how your file is found; it is removed and your id blocked from being counted again.

---

## When something looks wrong

### The window is off the screen or on the wrong monitor.
It pulls itself back onto the desktop when it starts. If it is on the wrong screen, drag it by the title bar.

### The numbers don't match what I remember.
Check [How the numbers are worked out](../README.md#how-the-numbers-are-worked-out) first (a corpse is inferred from rows that arrive within six seconds of each other; coin from split purses
is counted as described above). If they still look wrong, please [open an issue](https://github.com/ExploreTheFloor/MnMCompanion/issues/new/choose).

### I found a bug or want a feature.
Please [open an issue](https://github.com/ExploreTheFloor/MnMCompanion/issues/new/choose). Say which version you have (**ⓘ** shows it) and what you did. Don't paste your ledger files or
character names; a description or a picture of the window is enough.
