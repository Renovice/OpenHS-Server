# Admin console

Change a player's cards, currencies, the event calendar, or start parts of their state over.

**Easiest: type the command in the server window** (the one `run-local.bat` opens), like a Minecraft server console:

```
give default EX1_116
dust default +1000
stop
```

`admin` in front is optional there. `stop` (or `exit`) shuts the server down; the game client stays open. What you type
stays on the `> ` line at the bottom while log lines print above it; Up / Down recall earlier commands, Esc clears the
line. Debug lines go only to the log file (`Server\logs\`).

**Or from any other command window** (in the `Server` folder), or a script:

```
admin.bat <command>
```

(`admin.bat` alone prints the short help. It runs `run\HsServer.exe admin <command>`.)

- **Server running:** the command goes to the server (port 1121). A logged-in client sees card and currency changes at once,
  like after crafting. Event and reset changes reach it at the next login.
- **Server stopped:** the command changes the saved files in `Server\state\` directly.
- **An older server without the admin port is running:** nothing is changed (stop it with `stop-local.bat` first), because
  that server would save over the change.

Players are accounts: `default` is made at the first login; make, pick and remove others with the `account` commands below
(or double-click `account.bat`). `run-local.bat player <name>` starts as `<name>` and makes it the active account.

---

## Accounts

| Command | What it does |
|---|---|
| `account` / `account list` | Every account: the active one (`*`), dust, gold, cards, online or offline, last played; deleted ones still restorable |
| `account new <name>` | A new account (every mode open, like a first login) |
| `account new <name> from <other>` | A new account with a copy of another's cards, decks and progress (its own ids) |
| `account login <name>` | Play as `<name>`: it becomes the active account (a plain `run-local.bat` logs in as it). A running game is logged out and logs in again as it. A new name is made on the spot |
| `account relog` | The running game logs in again (same account) |
| `account logout` | Disconnect the running game from the server |
| `account rename <old> <new>` | Rename (keeps its cards and progress). Not `default`, not while logged in |
| `account delete <name>` | Delete. It can be brought back for **24 hours**, then it is removed for good (at the next server start or account command). Not `default`, not while logged in |
| `account delete <name> now` | Delete for good at once |
| `account restore <name>` | Bring back a deleted account within its 24 hours |
| `account info <name>` | = `show <name>` |

Names: letters, digits, `-` and `_`, up to 24. The active account is in `Server/state/accounts/active.txt`; deleted ones wait in
`Server/state/accounts-deleted/`.

`account.bat`: a menu (P play, N new, R rename, D delete, U undo a delete) that lists the accounts and starts the game as the
one you pick.

```
admin.bat account
admin.bat account new Bob
admin.bat account login Bob
admin.bat account delete Bob
admin.bat account restore Bob
```

---

## Players

| Command | What it does |
|---|---|
| `players` | Every player: dust, gold, cards (different cards and copies), online or offline |
| `show <player>` | One player: the above plus renown, Battlegrounds tokens, event currency, Arena tickets, decks, save data keys |

```
admin.bat players
admin.bat show default
```

## Cards

| Command | What it does |
|---|---|
| `give <player> <card> [count] [premium]` | Add copies (count default 1) |
| `take <player> <card> [count] [premium]` | Remove copies (never below 0) |

- **card**: the card id (`EX1_116`), its number (dbf id, `559`), or its name. Names can be typed with or without quotes, any
  case. When several cards match (reprints like Leeroy Jenkins have 9 versions, or a partial name), nothing is changed and
  the matches are listed with their ids — then use the id.
- **premium**: `normal` (default), `golden`, `signature`, `diamond`.
- Given cards show as new (they glow) in the collection.
- There is no copy limit: the admin can give 5 copies.

```
admin.bat give default EX1_116
admin.bat give default EX1_116 2 golden
admin.bat take default EX1_116 1 golden
admin.bat give default leeroy          (lists the matching cards and their ids; nothing is given)
```

## Currencies

| Command | What it does |
|---|---|
| `currency <player> <kind> <amount>` | Set the balance |
| `currency <player> <kind> +N` / `-N` | Add / take (never below 0) |
| `dust <player> <amount\|+N\|-N>` | Short for `currency <player> dust ...` |

| kind | |
|---|---|
| `dust` | Arcane dust (crafting) |
| `gold` | Gold, shown in the shop (starts as your live balance); a change shows at once like dust |
| `runestones` | Runestones, the shop's other currency (starts as your live balance) |
| `renown` | Renown |
| `bgtokens` | Battlegrounds tokens |
| `bmec` (or `event`, `bloodstones`) | Event currency: the Black Market's Bloodstones (earned only in Ranked on live, so this server never adds any) |
| `tickets` (or `arena`) | Arena tickets |

```
admin.bat dust default 5000
admin.bat dust default +1000
admin.bat currency default tickets +5
admin.bat currency default bgtokens 20
```

## Packs

| Command | What it does |
|---|---|
| `packs <player>` | The player's unopened packs |
| `pack <player> <pack> <amount>` | Set how many packs of that type the player has |
| `pack <player> <pack> +N` / `-N` | Add / take packs |
| `packtypes [text]` | Pack types whose name contains the text: id, name, and `opens` if they can be opened here |

- **pack**: the pack's name (`Standard`, `Golden Standard`, `Wild`, `The Lost City`; any case, spaces allowed) or its id
  (`713`).
- New packs appear in the Open Packs screen at once. Open them in the client: the cards follow live's rules (rates, pity
  timers, duplicate protection — RESEARCH/hearthstone-rules.md §F).
- Class packs (`Hunter Pack`...) hold that class's Standard cards; year packs (`Year of the Phoenix`...) that year's
  expansions; Catch-Up packs (`Showdown in the Badlands Catch-up`...) 5–50 cards from the Standard expansions before
  theirs, more from sets you own less of. Not openable: Arena draft, Mercenaries, the shop bundles, the unused Epic Pack,
  and packs whose set hasn't launched yet (its open event is off, e.g. `BE` = Black Empire).

```
admin.bat packs default
admin.bat pack default Standard +10
admin.bat pack default "Golden Standard" 2
admin.bat pack default The Lost City 5
admin.bat packtypes golden
```

## Events

The event calendar decides what is on: what can be crafted, nerf refunds (a card disenchants for its full craft cost),
the Core set, and later quests and the shop. Dates start as live's (from the recorded login); changes are kept in
`Server\state\events.json`.

| Command | What it does |
|---|---|
| `event list` | The events that are on now, grouped by what they are, each with the name the game gives it |
| `event list all` | Every event, on or off, grouped the same way |
| `event list <text>` | Every event whose raw name or label contains the text, on or off (`event list black empire`) |
| `event on <name> [days]` | Start now, for that many days (no number: no end) |
| `event off <name>` | End now |
| `event set <name> <start> <end>` | Dates as `2026-10-01` or `2026-10-01T18:00` (UTC), `now`, or `-` (open) |

- The groups come from the client's own tables: **Events** (quests and an event reward track, e.g. "30 Years of Terror"),
  **Prize machines and bundles** (the Darkmoon paid pulls, e.g. "Eye of the Empire Bundle"), **Black Market**, **Expansions and
  packs** (a set's launch, its Standard years, a pack's opening). Events the tables never name are listed under **Other**.
- The server uses the change at once (e.g. crafting); a client sees it at its next login.
- A name the client's data never uses is accepted with a warning: it does nothing in the game.

Useful events:

| Event | What it does |
|---|---|
| `dust_refund_<patch>` (e.g. `dust_refund_36_4_2`) | Full-refund disenchant for the cards nerfed in that patch (`event list dust_refund` lists them) |
| `post_set_rotation_<year>` / `pre_set_rotation_<year>` | Which cards are in the Core set |
| `<set>_content_launch` (e.g. `be_content_launch`) | A set's launch: its cards can be crafted once it is on |

```
admin.bat event list
admin.bat event list dust_refund
admin.bat event on dust_refund_36_4_2 14
admin.bat event off dust_refund_36_4_2
admin.bat event set dust_refund_36_4_2 2026-10-01 2026-10-15
```

## Quests

Daily and weekly quests (the recorded login's are adopted at the first login, then they are the player's own). Changes
reach a logged-in client at once (the quest log, and the XP bar for a completed quest).

| Command | What it does |
|---|---|
| `quests <player>` | The player's quests (status, progress / goal) and quest pools (next new quest, rerolls) |
| `quest list <text>` | Quests whose text contains the text, with their ids (daily / weekly pools first) |
| `quest give <player> <quest id>` | Give a quest (new, progress 0) |
| `quest progress <player> <quest id> <n>` / `+N` | Set or add progress; reaching the goal completes it |
| `quest complete <player> <quest id>` | Complete it now: its reward-track XP is given |
| `quest new <player>` | The pools' timers run out now: new dailies / weeklies (a full pool gets none) |
| `quest reset <player>` | Quests start over from the recorded login (the client sees it after logging out and in) |

- Quests marked "(not counted yet)" in `quests` have no progress condition yet (other modes): complete them by hand.

```
admin.bat quest list mana
admin.bat quest give default 1035
admin.bat quest progress default 1035 +100
admin.bat quest complete default 1035
admin.bat quest new default
```

## Ranked

| Command | What it does |
|---|---|
| `ranked <player>` | The player's ranks this season per format: rank, stars, streak, Star Bonus, games / wins, hidden rating (± deviation), Legend number |
| `ladder [standard\|wild\|classic\|twist]` | This season's leaderboard of the server (rank, stars, rating), best first; default standard |
| `seasonend <player>` | Ends the Ranked season for the player now, as if the month were over: the season-end chest for the best rank of the season (Standard or Wild) is granted and shown at the next login, and the next medal starts a new season from Bronze 10 with its Star Bonus |

The hidden rating (Glicko) moves after every Ranked game; Legend numbers are a player's place among the server's Legend players by rating.

## Practice

| Command | What it does |
|---|---|
| `practice` | Which decks the Practice Innkeeper plays |
| `practice decks <name start>` | The Innkeeper plays one of the player's own decks whose name starts with this text and whose hero is of the Innkeeper's class, another than the deck you play when there is one (e.g. `practice decks Legacy`: against a Mage Innkeeper with "Legacy Mage A" you meet "Legacy Mage B") |
| `practice decks off` | Back to a random Basic + Classic deck of its class |

- Saved in `Server\state\practice.json`; the next Practice game uses it (no restart). Normal and Expert both use it.
- No matching deck for the Innkeeper's class: the random deck as before.

## Reset

| Command | What it does |
|---|---|
| `reset <player> collection` | Cards, dust, other currencies and favorites start over from the recorded login (plus the current Core set) |
| `reset <player> savedata` | Game save data (Heist runs, tutorial flags, options) starts over from the recordings (no Heist run in progress) |
| `reset <player> heist [normal\|heroic]` | Ends the player's Dalaran Heist run (both modes when none is given): the next queue starts a new run. Unlocked decks / Hero Powers and win counts are kept |
| `reset <player> decks` | Decks start over from the recorded login's deck list |
| `reset <player> luckydraw` | Darkmoon Faire Treasure boxes start over: won rewards and pull licenses go, the next login takes live's recorded boxes over again (rewards already granted stay in the collection) |
| `reset <player> blackmarket` | Black Market prices, purchases and haggles start over from live's recording (Bloodstones and granted rewards stay) |

The client sees a reset after logging out and in.

```
admin.bat reset default collection
```

---

*Keep this page up to date: every new admin command is added here when it is added to the code
(`Server/src/HsServer/Admin/AdminCommands.cs`, whose `Help` text is the short version).*
