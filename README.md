# OpenHS Server

A community Hearthstone server: play the real Hearthstone client against our own C# server, offline, on your PC.
Goal: every card, mode and adventure working like the live game — starting with the Dalaran Heist.

- **Rules engine**: a fork of [SabberStone](https://github.com/HearthSim/SabberStone) (`Server/src/HsServer.Rules`), extended to the
  current card pool and checked against recorded live games.
- **Server**: login, collection, decks, packs, crafting, quests, ranked, the Dalaran Heist, the shop, events (`Server/src/HsServer`).
- **Client plugin**: a small BepInEx plugin that points a private copy of the game at this server (`Server/client`).

> Hearthstone is © Blizzard Entertainment. This project is not affiliated with or endorsed by Blizzard.
> No game files or card data are included: setup copies them from **your own** Hearthstone install.
> Mods are against Blizzard's terms of service: use a copy of the game and don't take risks with your main account.

## Getting started

Needs Windows, Hearthstone installed through Battle.net and the [.NET 9 SDK](https://dotnet.microsoft.com/download).

1. Copy your Hearthstone folder somewhere else, e.g. `D:\PrivateHS` (your real Hearthstone is never changed).
2. Put this repo inside the copy: `D:\PrivateHS\OpenHS-Server` (`git clone https://github.com/Renovice/OpenHS-Server`, or
   download the zip from the green Code button).
3. Run `OpenHS-Server\Server\setup.bat` once: it adds the mod loader and the OpenHS plugin to that copy, builds the server and
   exports the card database from it. (The copy opens once by itself and closes again.)
4. Run `OpenHS-Server\Server\run-local.bat`: the server starts and your copy of the game opens, connected to it.

If the repo is *not* inside a Hearthstone folder, `setup.bat` makes the copy itself (`Hearthstone main game` next to `Server`).

A new account starts with every mode and adventure open. Server commands (cards, currencies, events, resets) are in
[Server/ADMIN.md](Server/ADMIN.md) — type them in the server window or run `Server\admin.bat`.

After a Hearthstone patch: close the game and run `Server\update.bat` (updates the card database; a copy made by setup also gets the new game files).

## Contributing

- Help record the live game: [OpenHS Recorder](https://github.com/Renovice/OpenHS-Recorder) installs a recorder and packs your
  games (BattleTags removed) to send in. Every recording helps match cards and effects to the real thing.
- Card scripts live in `Server/src/HsServer.Rules/src/CardSets/HsServer/`. The rule: fix the shared mechanic, never one card by
  name. `Server/src/HsServer/Tools` has the checking tools (`HsServer golden`, `soak`, `livecheck`, `cards`...).
- Tests: `dotnet test Server/tests/HsServer.Tests` and `dotnet test Server/tests/HsServer.Rules.Tests -c Release`
  (tests that compare against recorded live games are skipped or fail without recordings).
- Open a pull request; describe what changed and how you checked it.

## Licence

AGPL-3.0 (the SabberStone rules engine is AGPL-3.0; see `LICENSE` and `Server/src/HsServer.Rules/LICENSE-SabberStone.txt`).
`Server/loader` holds BepInEx 5.4.23 and Unity Doorstop 4 (LGPL-2.1), unmodified, plus unstripped Mono libraries (MIT).
