# Characters Bosses

Replaces **every act boss in Slay the Spire 2 with a boss played by one of the vanilla characters**.
Inspired by the Slay the Spire mod *Downfall*.

- Steam Workshop: **玩家角色Boss (Characters Bosses)** — item id `3830639796`
- Requires **[BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127)** (used for the settings screen; hard dependency)
- **One package supports both v0.107 (release) and v0.111 (beta)**: the game only loads an 11 KB shell DLL
  that detects the running version and loads the matching implementation. You don't have to pick.

## What it is

The Ironclad, Silent, Defect, Necrobinder and Regent can show up as your boss:

- they bring their **own deck and relics** and actually play cards from their own class;
- their plays follow a **fixed script**, so intents are predictable (no random AI);
- every boss has a **unique special effect** (e.g. "Lightning Rod", "Call to Arms", "All for One");
- their HP scales up act by act (Neow's Blessing), so they are considerably tankier than you.

There are **27 boss slots** in total, registered as "**act N + character #M**": **act 1 = 7 / act 2 = 9 / act 3 = 11**.
The full roster with each boss's special effect (and the console command to fight it) is in
[`docs/bosses.md`](docs/bosses.md) (written in Chinese).

## Install

**Workshop (recommended)**: subscribe to this mod + BaseLib, enable it in the in-game mod list, restart.

**Offline zip**: extract the whole `CharacterBosses` folder into
`<Steam>\steamapps\common\Slay the Spire 2\mods\`. All DLLs / `.json` / `.pck` in it are required.

**Uninstall**: delete `mods\CharacterBosses\` (config and local logs live in `%APPDATA%\SlayTheSpire2\`).

## Multiplayer

- Up to **4 players**. Boss **HP does not scale** — instead the **number of bosses** equals the player count.
- Each player is "tracking-locked" by one boss: that boss's single-target attacks, random attacks, orbs and
  single-target special effects only affect that player; AoE attacks still hit everyone.
- Kill a boss → the player it locked is released. A player goes down → that boss re-locks the surviving
  player with the fewest lock stacks.
- Within one fight each character appears **at most once**; the **main boss character never repeats across the run**.

> ⚠ **All players must run the same mod version and the same gameplay settings.** Different versions or
> settings roll different lineups ⇒ state divergence. If you do hit a desync, please send both peers'
> `mod_log.txt` (`%APPDATA%\SlayTheSpire2\logs\`).

## Settings (in-game: Mod Config → CharacterBosses)

| Option | Meaning |
|---|---|
| A1 Maps to replace + boss selection | Every map of all 3 acts is listed (**non-vanilla maps are off by default**). Each enabled map expands a checkbox grid underneath: **one column per character**, listing that character's designs for the act. At least 1 per character must stay checked (**2 in act 3**) — the UI refuses to go below that. The main boss is drawn from your picks; in multiplayer the host's selection wins, and partner bosses (2nd/3rd) still come from the whole act roster |
| A3 Boss music source | ① other vanilla bosses' music (default) ② Slay the Spire 1 boss music (③ custom — not available yet) |
| A4 Share the matching pool with vanilla bosses | When on, a replaced map randomly keeps its vanilla boss or uses a character boss; a sub-option can pin act 3 of Ascension 10 to one vanilla + one character fight |
| A5 Local fight statistics | Off by default. When on, one line per boss fight is written locally (act/fight, ascension, players, boss HP, turns, seed…) |
| A6 Upload statistics | Only meaningful with A5; **anonymous** (no SteamID / nickname), can be turned off anytime |

## Known issues

- **Desyncs can still happen in multiplayer.** Balancing the same numbers for solo and co-op is hard —
  that is exactly why A5 statistics collection exists.
- Compatibility with other mods is best-effort; attach `mod_log.txt` when reporting an issue.

## Credits / Disclaimer

This mod uses art assets from Slay the Spire 2 itself, music from Slay the Spire and Slay the Spire 2, and art
assets from the Slay the Spire mod "Downfall". All of these assets remain the property of their respective
creators (MegaCrit and the author(s) of Downfall). This mod is provided **free of charge for non-commercial
use only** and is never sold in any form; upon request from the original authors the related content can be
removed at any time. Everything else (code, boss designs, etc.) is original to this mod, aside from a few
bosses that take inspiration from designs in Downfall.

The code was written by DeepSeek-Harness; the author is the lead designer. Thanks to everyone who
contributed content, tested and reported bugs!

## Development

- Build / deploy / package / upload: `install-mod.bat` (install only; add `pack` to also package),
  `tools\pack-dist.ps1`
- Version control & release flow: [`docs/plans/git-github-workflow.md`](docs/plans/git-github-workflow.md)
- Conventions: `AGENTS.md`, `rules.md`, `rules_en.md`
- Design notes and investigations: `docs/plans/`
- Dual-target build: `src/CharacterBosses.csproj` builds `Sts2Target=107|111` into
  `CharacterBosses.Impl107.dll` / `CharacterBosses.Impl111.dll`; the shell in `loader/` loads one at runtime.

## License

See [`LICENSE`](LICENSE).
