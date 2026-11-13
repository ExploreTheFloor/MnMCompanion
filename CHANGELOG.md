# Changelog

What changed in each version of Companion, newest first. Each version's entry is also the text of its [GitHub release](https://github.com/ExploreTheFloor/MnMCompanion/releases),
which is what the update message shows when you hover over it.

Every entry says what it means for **your data**: whether anything new is shared with the community service (only ever after a Yes from you), and whether the program
now asks anywhere else on the internet. A line starting with **Sharing** or **Network** is there for that.

## 0.4.10 (2026-11-13)

**In short:** An On/Off switch for the timers, and a way to take the timer off a corpse, or off a whole kind of monster.

- **New: timers On/Off.** The timers window has a switch in its title bar. **Off**, no key starts a timer, no sound plays and corpses get no timer, and the window says so;
  **On** brings everything back, and a timer that was counting carries on from where it would be. Your timers, keys and sounds are kept either way. The title shows *Timers · off* while it is off.
  An update that installs by itself no longer waits for a timer while the timers are off.
- **New: not every corpse.** Each corpse's bar now has a **×** that takes the timer off that one corpse. **Right-click** a corpse's bar to **stop timing that kind of monster**
  ("Stop timing a bog crawler corpses"): from then on its corpses get no timer. The monsters left out are listed under **Corpses** in *Set up…*, each with a × that times them again.
  The **Corpses** heading in the window also has a quick **Turn off** for the corpse timer as a whole. See [Timers](README.md#timers).
  Both ideas came from a suggestion sent through the suggestion box.
- **Network:** nothing new: none of this asks anything on the internet, and nothing is read from the game. What you leave out is kept in your own `timers.json`.
- **Sharing:** nothing new is shared; a Yes you gave stays valid.

## 0.4.9 (2026-11-11)

**In short:** Make any window bigger or smaller and pick the font, and put any page in a window of its own.

- **New: window size.** Every window can be drawn from 70% to 250%, for a large second screen or a small overlay: hold **Ctrl** and turn the **mouse wheel** over a window, or press
  **Ctrl +**, **Ctrl −** and **Ctrl 0** (back to normal), or open **ⓘ → Size and font…**. The window grows or shrinks with its contents, so you see the same page only larger.
  **Each kind of window keeps its own size** (the small window, History, Timers, the timer setup, each page window, and the small dialogs), so a big window on the second screen doesn't
  make the timers big. Menus and tooltips follow the size of their window. The History window has the same button under its list of pages, and the timers window has it in its *Opacity* menu.
  See [Size and font](README.md#size-and-font).
- **New: font.** Pick the typeface for every window from a short list of the ones Windows has (Segoe UI, Arial, Calibri, Verdana, Tahoma and a few more); it changes at once. The icons keep
  their own font. A wide font needs a little more room, so widen the window if a row looks tight.
- **New: pages in their own windows.** Right-click any button in the bottom bar and choose *Open … in its own window*, or press the new **pop-out** button in the title bar to open the page you
  are on. It is a second copy of the page that stays up to date with the small window, with its own pin and size, and it remembers where you put it; windows that were open when you closed
  Companion come back at the next start. The **✕** closes one (the page is still in the small window). See [Pages in their own windows](README.md#pages-in-their-own-windows).
  The size, font and pop-out ideas all came from a suggestion sent through the new suggestion box.
- **Fixed: menus with items under them.** A menu item that has a list under it never opened it, so the timers window's *Key to hold* choice (Ctrl, Alt or Shift) and the Windows sound groups in
  the timer setup could not be reached. They open now.
- **Network:** nothing new: none of this asks anything on the internet, and nothing is read from the game.
- **Sharing:** nothing new is shared; a Yes you gave stays valid.

## 0.4.8 (2026-11-10)

**In short:** A suggestion box, and a window that shows what changed, once, after an update.

- **New: send a suggestion.** Click **ⓘ → Send a suggestion…**, write what you'd like Companion to do (or what bothers you) and press **Send**: it goes privately to the person
  who makes Companion, through the same small service the sharing uses. Nothing from your game or your ledger goes with it: only what you wrote, the program's version and your
  random id (the one sharing uses, so nobody can flood the box). You don't need to have said Yes to sharing. For a bug with files to attach, the window links to the issue page
  on GitHub. See [Sending a suggestion](README.md#sending-a-suggestion).
- **New: what's new, once.** The first time Companion runs after an update, a window shows this version's notes (every version since the last one you saw, if an update skipped
  some), with **OK** and a link to every version's notes. It opens once and never again for that version; **ⓘ → What's new in this version…** shows it whenever you like.
  The notes come from a copy of the change log inside the program, so no page is fetched.
- **Network:** a suggestion is sent **only when you press Send**, to the project's community service (`mnm-intake.mnm-companion.workers.dev`), and it carries only the three
  things above. The what's-new window asks nothing on the internet.
- **Sharing:** nothing new is shared; a Yes you gave stays valid.

## 0.4.7 (2026-11-01)

**In short:** A Market page with what players ask for items on the auction channel, and what your loot would fetch.

- **New: a Market page.** What players ask for items on the game's auction channel, from the public feed of **mnm-db.com** (a fan-made economy database), in three tabs. **Selling** lists
  every item someone is selling, ranked by how busy its market is, with a search and a picker for the kind (weapons, armor, jewelry, bags, materials); click a row for the lowest, middle and
  highest asks, the best bid, the seven-day trend and who is asking, and a **Copy price check** button for chat. **Buying** lists what players are bidding for. **Yours** lists the items you
  looted this session (or day), what each would fetch at the middle ask, and the total, so you can tell what is worth auctioning rather than selling to a vendor. A post is what a player
  *asked*, not what was paid, and the feed covers one server. See [The Market page](README.md#the-market-page).
- **New: Companion keeps its own price book** (`market.json`), because the feed holds only each player's current price: the by-day table, the trend and the price changes grow from the
  snapshots it takes.
- **Changed:** every page except Recipes and Market shows the same day and session. Market's *Yours* tab follows it too.
- **Network:** the Market page loads the feed by itself the first time you open it, then at most once per publish (every six hours) while it is open, asking only *has it changed?*, never twice
  within an hour. **mnm-db.com sees your internet address**, as if you had opened it in a browser; nothing from your ledger or your game is sent, only the program's name and version.
  To stop it, untick *Load auction prices from mnm-db.com* in the ⓘ menu.
- **Sharing:** nothing new is shared.

## 0.4.6 (2026-11-01)

**In short:** Click Ours or Total to copy the coin as one line for chat, and a click-through mode for the timers window.

- **New: copy the coin as one line.** Click **Ours** or **Total** at the top of the loot window (or the figures at the top of a day in History) and the clipboard gets one line to paste into chat:
  *Coin looted: 1gp 27sp 84cp | My split: 20sp 50cp | My share: 16%*. A `~` says some of it is estimated.
- **New: click-through for the timers window.** For one monitor, or a faint overlay on the game: in the **Opacity** menu under *Clicks*, choose *Pass clicks to the game*. Every click then goes
  through the window to the game, and **holding Ctrl (or Alt or Shift, your choice) lets you click the window itself**, to drag it, press a button or turn this off. It is off until you turn it on.
  See [Timers](README.md#timers).
- **Sharing:** nothing new is shared.
- **Network:** nothing new. While click-through is on, Companion asks Windows a few times a second whether the one key you chose is held down; it looks at no other key and records nothing.

## 0.4.5 (2026-10-29)

**In short:** A Recipes page, and a new session starts when a player joins your group.

- **New: a Recipes page.** Every tradeskill recipe of the community wiki, one skill at a time, in order of level in stretches of 25 (coloured from green to pink as a skill progresses), with a
  search over names, ingredients, kinds and notes: `copp bar` for everything made from Copper Bar, `weapons` for a whole kind, `50-75` for a range of levels. A click opens the rest the wiki lists
  about a recipe and a button copies it. See [The Recipes page](README.md#the-recipes-page).
- **New: a new session when a player joins your group.** When someone joins your group (or you join one), the loot so far is archived and every page starts again from that moment, as *New session*
  does. A setting in the ⓘ menu, on by default.
- **Network:** the recipe list comes from the wiki's public tradeskills data, loaded by itself the first time you open the page and kept on your PC (so it also works offline). After that the wiki is
  asked at most once a day, with a question it answers with nothing at all when its list hasn't changed, never twice within an hour, and for as long as it asks to be left alone. Nothing from your
  ledger or game is sent, only the program's name and version. To stop it, untick *Load the recipe list from the community wiki* in the ⓘ menu.
- **Note:** the recipes are the wiki's, by its community: they can be incomplete or out of date (a level shows as **?** where the wiki has none yet). Companion is not affiliated with the wiki or
  the game's makers.
- **Sharing:** nothing new is shared.

## 0.4.4 (2026-10-28)

**In short:** Ours and Total at the top of the loot window, group purses worked out properly, and clearer notes for monsters whose items the game does not record.

- **New: Ours and Total at the top of the loot window.** **Total** is what the whole group looted: every purse once, whole, plus what a vendor would pay for all the items. **Ours** is what reached
  *you*: your coin plus the items you looted. The **Value** tab and the Looters table now rank players by the items they looted, because the game only ever shows you your own share of a split purse.
- **Fixed: your own purses in a group.** The game's ledger writes a purse you split in a group as one you kept whole. Companion now works out whether you were in a group and, if so, counts the whole
  purse in the group's coin and only your share in yours. The ledger doesn't give that share, so it is estimated and marked with **~**. Alone, a purse is yours whole.
- **Changed: a more compact small window.** The counts sit at the right of the title, and the session chip and the two copy buttons share one row.
- **Fixed: monsters with an apostrophe in their name.** The game's ledger writes no item rows for monsters like the Skri'Var and Mur'Hua ones. Companion now says so ("items not recorded", a **+** on the
  counts, a line and a tile saying how many corpses are affected) instead of showing "0 items". Their coin is still counted. See
  [Why does it show no items for some monsters?](docs/FAQ.md#why-does-it-show-no-items-for-some-monsters)
- **Sharing:** nothing new is shared; a Yes you gave stays valid.

## 0.4.3 (2026-10-27)

**In short:** Search in every list and across every day. Sharing now says which server, so your Yes is asked again.

- **New: search in every list.** Every list in the History window and on the Loot page has a search box, and they all work the same way: every word you type has to be in the row, in any order and any
  case, and part of a word is enough. The History pages (Trades, Vendors, Harvest, Drops, Changes) also match the zone, and the character when more than one is in view.
- **New: search every day at once.** Type in the box above the day list on the Loot page and the first row becomes **All days**: the answer across every day you have. Click a player to see only their share.
- **Changed: sharing now says which server.** Each shared row says which server it is from, so figures can be told apart per server. Character names stay out, as before.
- **Sharing:** this is new data leaving your PC, so **a Yes from an earlier version is asked again**, with a list of only what is new, and nothing is sent until you answer. A No stays a No.
  See [Is my server shared?](docs/FAQ.md#is-my-server-shared)

## 0.4.2 (2026-10-27)

**In short:** A search box over the item lists.

- **New: search the items.** Loot → **Items** (for everyone, and for one player) and Drop rates → **By item** have a search box above the list ("scraps", or "raw croc meat"). It says how many items
  match ("4 of 22"), keeps what you typed while you sort or switch tabs, **Esc** clears it, and **Copy items** copies just the matches.
- **Sharing:** nothing new is shared.

## 0.4.1 (2026-10-27)

**In short:** Updates install by themselves, and timers arrive (everything off until you set it up).

- **New: updates install by themselves.** Companion downloads a newer version, checks it against the checksum published with the release, and restarts into it. If one of your own timers is counting down
  it waits until none is. To be asked first, untick **Install updates automatically** in the ⓘ menu; untick **Check for updates automatically** too and it never asks GitHub by itself.
- **New: timers.** Name your own timers with a length, a key that starts it (works while the game has the focus) and a sound for when it ends; turn on a timer for every corpse you loot; a **Buttons**
  row to start and stop timers with a click; the window has its own pin and an opacity button. **Everything starts switched off.** See [Timers](README.md#timers).
- **Network:** the automatic update is the only thing that asks GitHub, and you can switch it off.
- **Sharing:** nothing new is shared. Companion hears only the keys you set for timers.
- If you are on 0.4.0 or older, press **Update now** once; every version after this one installs itself.

## 0.4.0 (2026-10-27)

**In short:** Trades, drops and quests can be shared (after a new Yes), and Companion runs under Linux.

- **New: trades, drops and quests in the shared data, only if you say Yes.** Companion can share your trades (what each side handed over, with the day and the zone, never who it was with), what you
  dropped and destroyed, and what NPCs said to you in the order you heard it. **ⓘ → What does sharing include?** lists everything, and **Save a copy of my file…** writes exactly what would be sent.
- **New: Linux.** Companion runs under Proton or Wine, finds the game's data in the Steam, UMU and Lutris prefixes by itself, and `--diagnose` writes a report you can paste into an issue.
  The map page opens your browser there, because Microsoft's web view isn't available.
- **Changed:** the ledger notes now name two kinds of entry nobody had identified: your own deaths (zone, level and time) and party invites.
- **Sharing:** this shares more than before, so **Companion asks the sharing question again after you update, and sends nothing until you say Yes.**

## 0.3.2 (2026-10-26)

**In short:** A prerelease for Wine and Proton players: it finds the game by itself, and --diagnose writes a report.

A prerelease for people who run the game under Wine or Proton and are willing to report what happens; the in-app updater does not offer it, and nothing changes on Windows.

- **New: finds the game by itself under Wine or Proton**, looking through Steam's, UMU's, Lutris's and the plain `.wine` prefixes when the usual place has no characters. It never replaces a folder
  you chose yourself and writes nothing there.
- **New: `--diagnose`** writes `diagnose-report.txt` (counts only, no character or server names, your user name replaced by `<user>`) and quits. Safe to paste into an issue.
- If no ledger is found under Wine, the empty page now explains how to choose the folder by hand.

## 0.3.1 (2026-10-26)

**In short:** Smaller uploads when you share; nothing new is shared.

- **Changed: smaller uploads when you share.** Your totals are sent without extra spacing (about half the size), and what NPCs said to you goes as a second, separate small upload. An unchanged set
  of dialogue is no longer sent again with every update.
- **Sharing:** nothing new is shared, so you are not asked again. *Save a copy of my file…* now saves two files, the totals and the dialogue beside it.

## 0.3.0 (2026-10-26)

**In short:** Weekly running totals can be shared, so your earlier Yes is asked again.

- **New: weekly running totals are shared**, only when you say Yes: corpses looted (the game records no kills, so this is the kill count), the coin and items taken from them, what you gathered,
  vendor stacks sold and bought with their coin, and how many trades you made. They are kept as one small row per week, never a list of events.
- **Sharing:** this shares more than before, so **your earlier Yes is paused**. Companion asks again when it starts and says what was added; nothing is sent until you say Yes again.
  The sharing window lists the new totals before you decide, and `MnMCompanion.exe --contribute <folder>` saves exactly the file that would be sent.

## 0.2.0 (2026-10-26)

The first release from the public project.

- **New:** reads each character's journal (what NPCs have said to you) into its saved database: one NPC once however many characters met them, lined up with the monsters it has seen looted.
- **New: sharing is a Yes/No question, No by default.** With a Yes, your anonymous totals (and the NPC lines, with your character's name removed) are sent to the project's private community service
  every few hours, and community figures appear in History → Loot → Drop rates once at least 3 players stand behind them. Nothing is sent on a No.
- MIT licence; the source is in this repository. The program is not code-signed, so Windows SmartScreen may warn the first time you run it.

## 0.1.0 (2026-10-24)

The first tag of the public source. No download was published for it.
