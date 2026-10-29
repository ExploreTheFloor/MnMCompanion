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
Windows 10 or 11, 64-bit. Nothing else has to be installed (it carries what it needs inside the one file). It also runs under Proton or Wine on Linux: see the next question.

### Does it run on Linux?
It is a Windows program, and one player has it running under Proton on Linux. Add `MnMCompanion.exe` to Steam as a non-Steam game and give it the same Proton version the game uses. When it starts it looks through the
other Steam, UMU and Lutris prefixes for the game's data and uses the one played most recently; if it doesn't find yours, the window says how to pick the folder by hand (**ⓘ → Choose the game's data folder…**).
The Map page needs Microsoft's web view, which Proton doesn't have, so it offers to open the map in your browser instead. Run it once with the launch option `--diagnose` and it writes `diagnose-report.txt`
(counts only, your user name hidden) that you can paste into an issue if something doesn't work.

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
It updates itself. It checks GitHub a few seconds after it starts and every six hours; when there is a newer version it downloads it, checks it against the checksum published with the release,
and only then replaces the old one and restarts (waiting first if one of your timers is counting down). A strip under the title bar says what it is doing and which version you have afterwards.
To be asked first instead, click **ⓘ** and untick **Install updates automatically**; untick **Check for updates automatically** too and it never asks GitHub by itself. You can also use **ⓘ → Check for updates now**. If you are still on **version 0.1.0** (when it was called Loot Goblin), that version can't see newer releases:
download the current `MnMCompanion.exe` once and replace the old file; from then on it updates itself. So does **version 0.4.0**, which only offers the update: press **Update now** once, and every version after that installs by itself. See [Updates](../README.md#updates).

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
or at everyone together, open the **History** window (the **History** button in the title bar) and pick a server and character there.

### How do I see what a monster drops?
Open **History → Loot → Drop rates**, then click a monster. It shows how often each item came off that monster, with a range that is honest about small samples.
Those are shares of **looted corpses**, not of kills (see the next question).

### Why is there no kill count, and no buffs, cooldowns, damage, chat or position?
Because the game doesn't write them to any file Companion reads. The ledger records items looted, coin purses, vendor sales and purchases, harvests, trades, level-ups and
party changes; it has no kills, casts, buffs, damage or position, and only your own deaths with the zone and level, not who killed you (we looked through the rest of the game's folders as well, and none of them has these either).
The closest thing to a kill count is **corpses looted**, and that is what Companion counts, so a corpse nobody looted isn't counted.

### Why does some loot show "unpriced"?
Companion values an item at what a vendor pays for it, learned from what **you** sold. Sell one to a vendor and it is priced from then on. (If you share, an item's page in History also shows what the community says vendors pay.)
How values are worked out, including the `~` and `+` marks, is in [How the numbers are worked out](../README.md#how-the-numbers-are-worked-out).

### Why does it show no items for some monsters?
The game's own ledger can't record the loot of a monster with an apostrophe in its name (the Skri'Var and Mur'Hua creatures, for example): its parser trips on the apostrophe, so it writes no item rows for those corpses and doesn't name the monster, only the coin. Companion only reads the ledger, so it shows that coin under "an unknown creature", counts those corpses, and says so ("Items not recorded" on the History page, a line at the bottom of the small window). It can't recover the items. When the game fixes it, new loot shows up normally.

### How is coin counted when I'm in a party with autosplit?
A purse nobody split counts whole for the person who looted it. A purse the group split counts for no looter (they kept only part, and the ledger never says how much), and your own share
of it counts for you. A purse you looted yourself while in a group is split too, but the ledger doesn't show your share of it, so Companion estimates it (marked `~`) from the shares you got of other players' purses; looting alone, it's yours whole. The group total counts every purse once. Because only your own coin is known in full, players are ranked by what they looted (items), not by coin; your coin is in the **Ours** figure at the top, next to **Total** for the whole group.

### What's a session?
"Since I last pressed **New session**." It answers "what did we get from this one dungeon run?" without it mixing into the whole day. Nothing is deleted when you start one.
A player joining your group (or you joining one) starts a new session by itself; untick **ⓘ → Start a new session when a player joins the group** to start them only by hand.
See [Sessions](../README.md#sessions-starting-a-fresh-tally).

### Where do the recipes come from?
Not from your ledger (it has no recipes) and not from the game: from the community wiki's public tradeskills list (monstersandmemories.wiki). The **Recipes** page loads it by itself the first time you open it,
which contacts that site once, so the site sees your internet address, as it would if you opened the wiki in a browser; nothing from your ledger or your game is sent.
The list is kept on this PC, so the page works offline, and the wiki is asked again only when you open the page and the kept copy was last checked more than a day ago (or when you press the refresh button, at most once in ten minutes). By then the question is only whether the list has changed, which the wiki answers with nothing when it hasn't, and Companion keeps away for as long as the wiki asks it to, even after a restart. To stop it, untick *Load the recipe list from the community wiki* in the ⓘ menu.
A recipe's level is the wiki's *trivial* number, and a **?** means the wiki doesn't have one yet. See [The Recipes page](../README.md#the-recipes-page).

### Can I paste any of it into chat?
Yes: pages have **Copy** buttons that put the figures on the clipboard as text.

### Does it have timers?
Yes, two kinds, and every part of them starts switched off: nothing runs, no key is taken and no sound plays until you set something up. You can name your own timers (a buff, a cooldown, food), give each a length, a key that starts it and a sound for when it ends. And you can turn on a timer for **every corpse
you loot**, with a name, a length and a sound of your choice. What it can't do is start a buff timer by itself: the game writes nothing about casts or buffs to any file Companion reads, so you
start those yourself, with the key or, if you turn on **Buttons** in the Timers window, with a click on the timer's button (click it again to stop it). (A tool that reads the game's memory could
do it automatically; Companion deliberately never does that.)

### Can the timers window stay on top and be see-through?
Yes. It has its own pin (off until you press it) and an **Opacity** button that fades its background down to 10% while the bars and words stay solid, so you can read the timers over the game without hiding
what is behind them. The game has to be in a window or borderless for any window to show on top of it.

### Will the key I give a timer stop working in the game?
Yes, while Companion is running: Windows hands that key to Companion, so the game never receives it. Pick combinations the game doesn't use, such as `Ctrl+Alt+1`. (A letter or digit needs Ctrl or
Alt with it for this reason.) Companion only hears the keys you set, can't see anything else you type, and never sends a key press.

---

## Sharing with the community

### What is sharing?
A yes/no choice. **Yes** adds your own totals (how often each monster you looted dropped each item, what vendors paid you, what you gathered, item names, what NPCs said to you, what you dropped and destroyed, your trades with other players, and running
totals counted by week, with the server each of them is from) to a combined database that every sharing player gets figures back from. **No** (the default) means nothing leaves your PC and the community figures aren't used.
Details in [Sharing with the community data](../README.md#sharing-with-the-community-data).

<img src="images/window-sharing.png" alt="The sharing window, listing exactly what would be shared" width="300" align="right">

### Can I see exactly what would be sent before I decide?
Yes. **ⓘ → What does sharing include?** lists it in plain words (the picture on the right), and **Save a copy of my file…** writes exactly what would be sent so you can read it (the totals, what NPCs said to you as a second small file, and your trades as a third; they are sent as separate small uploads, without indentation). From a command line,
`MnMCompanion.exe --contribute <folder>` does the same.

### What is never sent?
Your character names or any other player's name or number (a trade says what each side handed over, never who the other player was), individual loot lines, the time of day of anything (only
the day the file was made, the weeks the running totals fall in, and the day of each trade), and anything from other players' ledgers. Your character's name inside what NPCs said to you is replaced before it is sent.

### Is what I drop or destroy shared?
Yes, if you say Yes to sharing: for each item and zone, how many units you dropped, how many you destroyed, and how many of the dropped ones you picked up again within ten minutes.
Dropping and destroying are always your own choice, so it shows what players treat as junk. There is no time and nothing about anyone else.

### Is my server shared?
Yes, if you say Yes to sharing: every row about your play (monsters, vendor prices, gathering, what you dropped and destroyed, and your trades) says which server it is from, by the game's own name for it. That is how the combined figures can be kept per server,
which matters for what players pay each other because prices differ between servers. It is the name of the world, not of you: your character names are still never sent. Item names and what NPCs said are the game's own text, the same on every server, so they carry no server. A Yes you gave before servers were
added has to be given again.

### Are my trades with other players shared?
Yes, if you say Yes to sharing: for each trade, the day it happened, the zone, the server, and what each side handed over (the coin, and the items with how many). The ledger gives the other player only as
a number, and that number is left out, so nothing in the file says who you traded with. The newest 3,000 trades are sent. You see them in the sharing window before you decide, and a Yes you gave
before trades were added has to be given again.

### Is it published?
No. The combined database is kept privately by the project and is not published. It is only used to build the figures sharing players get back.

### Why did it ask me again after I updated?
A Yes covers what you were shown. When a new version shares more than the one you said Yes to (version 0.3.0 added the weekly running totals, version 0.4.0 your trades with other players and what you dropped and destroyed, and version 0.4.3 which server each row is from), your earlier Yes no longer covers it, so
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
