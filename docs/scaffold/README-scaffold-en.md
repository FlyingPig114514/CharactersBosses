# Slay the Spire 2 Mod Scaffold

A zero-config scaffold for Slay the Spire 2 mod development with AI-assisted vibe coding. Clone, run `install.bat`, start coding.

## Acknowledgments

- **[STS2MCP](https://github.com/Gennadiyev/STS2MCP)** - Game control MCP (play cards, read state, navigate menus)
- **[ILSpy-Mcp](https://github.com/maces/ILSpy-Mcp)** - .NET assembly decompilation MCP
- **[Modding-Tutorial](https://github.com/fresh-milkshake/Modding-Tutorial)** - Reference documentation and examples

## Quick Start

```powershell
.\install.bat
```

This single command:

1. Installs .NET 10 SDK + .NET 8 runtime (isolated to `tools/dotnet/`)
2. Downloads Godot 4.5.1 Mono, ILSPy
3. Builds ILSpy MCP Server and STS2MCP
4. Configures MCP servers in `opencode.jsonc`
5. Builds and installs STS2MenuControl Mod
6. Builds and installs the scaffold Mod
7. **Launches the game and verifies mod loading** (Deep Verification)

**Note**: Steam must be running and logged in for Deep Verification.

## Project Structure

```
SlaytheSpire2ModVibeCoding/
├── src/                            # Scaffold Mod (your code goes here)
│   ├── ModEntry.cs                  # Mod entry point - DO NOT modify logic
│   ├── Hooks/                       # YOUR CODE GOES HERE (split by feature)
│   │   └── .gitkeep
│   ├── Sts2ModScaffold.csproj       # .NET 10 project - DO NOT modify
│   └── com.vibecoding.sts2mod.json  # Mod manifest
├── tools/
│   ├── STS2MenuControl/             # Main menu control Mod (cloned from GitHub at install)
│   ├── STS2Mcp/                     # STS2MCP game control Mod (auto-installed)
│   ├── pck_builder/                 # PCK builder script
│   ├── dotnet/                      # Isolated .NET SDK/runtime
│   ├── godot/                       # Godot 4.5.1 Mono
│   ├── ilspy/                       # ILSpy decompiler
│   └── launch_sts2.ps1 etc.         # Helper scripts
├── references/                      # Game DLLs (gitignored)
├── docs/plans/                      # Implementation plans
├── rules.md / rules_en.md           # Development rules (MUST READ)
├── AGENTS.md                        # AI Agent workflow guide
├── install.bat                      # One-click setup
├── install-mod.bat                  # Rebuild & install
└── uninstall-mod.bat                # Uninstall all Mods
```

## Daily Workflow

```powershell
# 1. Edit files in src/Hooks/

# 2. Rebuild and install
.\install-mod.bat

# 3. Launch game
.\tools\launch_sts2.ps1

# 4. Check logs
.\tools\read_sts2_logs.ps1
```

## MCP Servers

| Server | Port | Purpose |
|--------|------|---------|
| **ILSpy MCP** | - | Decompile `sts2.dll` to explore game internals and verify hook signatures |
| **STS2MCP** | 15526 | In-game control (play cards, end turn, map navigation, events, etc.) |
| **STS2MenuControl** | 8081 | Main menu control (new game, character select, multiplayer, timeline, etc.) |

### STS2MenuControl API

> Repo: [https://github.com/L4ntern0/STS2-MenuControl](https://github.com/L4ntern0/STS2-MenuControl)

HTTP API on `localhost:8081` for controlling the main menu:

```
GET  /api/v1/menu                    # Get current menu state
POST /api/v1/menu                    # Execute menu action
GET  /health                         # Health check
```

#### Singleplayer Actions

| Action | Params | Description |
|--------|--------|-------------|
| `open_character_select` | - | Open character selection (singleplayer) |
| `select_character` | `option_index` | Select a character |
| `embark` | - | Start a new game |
| `continue_run` | - | Continue existing save |
| `abandon_run` | - | Abandon current save (shows confirm dialog) |
| `open_timeline` | - | Open timeline screen |
| `choose_timeline_epoch` | `option_index` | Select a timeline epoch |
| `close_main_menu_submenu` | - | Close current submenu |
| `return_to_main_menu` | - | Return to main menu (from game over) |

#### Multiplayer Actions

| Action | Params | Description |
|--------|--------|-------------|
| `open_multiplayer_host` | `mode`, `max_players`, `port` | Create multiplayer room (LAN/Steam) |
| `set_ready` | - | Mark as ready |
| `set_unready` | - | Mark as not ready |
| `get_lobby_status` | - | Query room status and player list |

#### General Actions

| Action | Params | Description |
|--------|--------|-------------|
| `confirm_modal` | - | Confirm a modal dialog |
| `dismiss_modal` | - | Dismiss a modal dialog |

#### State Response

`GET /api/v1/menu` returns multiplayer info on character select:

```json
{
  "is_multiplayer": true,
  "lobby_type": "host",
  "max_players": 4,
  "net_type": "Host",
  "players": [
    {"id": 1, "character": "IRONCLAD", "is_ready": false, "is_local": true}
  ]
}
```

### STS2MCP API

HTTP API on `localhost:15526` for in-game control:

```
GET  /api/v1/singleplayer         # Get game state
POST /api/v1/singleplayer         # Execute game action
```

Common actions: `combat_play_card`, `combat_end_turn`, `choose_map_node`, `choose_event_option`, `proceed`, `select_card`, `confirm_selection`, `skip_card_reward`.

## Full Test Flow (Automated)

The complete automated flow from main menu to first combat victory:

```
1. STS2MenuControl: open_character_select
2. STS2MenuControl: select_character(option_index=0)
3. STS2MenuControl: embark
4. STS2MCP:        choose_event_option(index=0)     # Neow
5. STS2MCP:        proceed / select_card + confirm   # Handle card selection
6. STS2MCP:        choose_map_node(index=0)         # Choose map node
7. STS2MCP:        combat_play_card + combat_end_turn  # Combat loop
8. STS2MCP:        proceed                         # Claim rewards
```

## Vibe Coding with AI

This scaffold is designed for AI-assisted development (OpenCode, Claude, etc.):

1. Describe your mod in natural language
2. AI uses ILSpy MCP to find correct hooks and verify signatures
3. AI writes code in `src/Hooks/`
4. AI builds and installs via `install-mod.bat`
5. AI tests using STS2MenuControl + STS2MCP (fully automated)
6. You verify in-game

**Important**: AI agents must read `rules.md` and `AGENTS.md` before development.

## Hooks Reference

All hooks are defined on `MegaCrit.Sts2.Core.Hooks.Hook`.

### Hook Types

1. **Event Hooks** (return `Task`, use Postfix) - `AfterCardPlayed`, `BeforeCombatStart`, etc.
2. **Value Modify Hooks** (return value, use `ref __result`) - `ModifyDamage`, `ModifyBlock`, etc.
3. **Boolean Gate Hooks** (return `bool`, allow/deny) - `ShouldDie`, `ShouldPlay`, etc.

### Example Hook

```csharp
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage))]
public static class ModifyDamagePatch
{
    public static void Postfix(ref decimal __result)
    {
        __result *= 2;
    }
}
```

See `rules.md` for the complete hook list.

## Scripts Reference

| Script | Purpose |
|--------|---------|
| `install.bat` | Full environment setup (first run only) |
| `install-mod.bat` | Rebuild & install all mods (daily use) |
| `uninstall-mod.bat` | Remove all mods from game |
| `tools/launch_sts2.ps1` | Launch game via Steam |
| `tools/close_sts2.ps1` | Force-close game |
| `tools/wait_sts2.ps1` | Wait for game process |
| `tools/read_sts2_logs.ps1` | View game logs |
| `tools/rename-scaffold.ps1` | Rename scaffold to your mod name |

## Requirements

- **Windows** (Godot and game are Windows-only)
- **Git** (must be in PATH)
- **Slay the Spire 2** via Steam
- **Steam** logged in (for game launch and Deep Verification)

Everything else (.NET, Godot, ILSpy) is installed automatically.

## Troubleshooting

### Mod Not Loading

1. Check `godot.log` for `ERROR` and `WARNING`
2. Verify `id` in manifest matches folder/DLL/PCK names
3. Confirm `has_dll: true` and `has_pck: true` are set

### Hooks Not Working

1. Verify signature with ILSpy MCP - **never guess**
2. Check `[HarmonyPatch]` attribute is correct
3. Confirm hook is triggered via `Logger.Log()` output

### Build Failed

1. Run `install.bat` first
2. Verify `references/` contains `sts2.dll`, `0Harmony.dll`, `GodotSharp.dll`

## Documentation

- **`rules.md`** / **`rules_en.md`** - Development rules and constraints (MUST READ)
- **`AGENTS.md`** - AI Agent workflow guide
- **`docs/plans/`** - Implementation plans directory

## License

MIT
