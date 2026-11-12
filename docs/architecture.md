# Architecture

Chencho is a Windows app that shows one animated pixel-art creature per Claude Code session on a strip at the edge of the screen, so you can see at a glance which sessions are working and which are waiting for you. It is two processes: an agent that knows the sessions, and an overlay that draws them.

## Stack

- **Agent and CLI:** TypeScript on Bun, compiled with `bun build --compile` into one `chencho.exe`. SQLite through `bun:sqlite`.
- **Overlay:** C# and WPF on .NET 9 (framework-dependent single-file `chencho-overlay.exe`), with a WinForms `NotifyIcon` for the notification-area icon. Win32 and UI Automation for terminal focus.
- **Shared:** the wire contract (`shared/protocol.ts`, mirrored in C#) and the creature templates (`shared/characters.json`).
- **Packaging:** `scripts/release.ts` builds both binaries and two npm packages (`chencho`, a launcher, and `chencho-win32-x64`, the binaries); `.github/workflows/release.yml` publishes them on a `v*` tag.

## Processes

```
Claude Code (any terminal)
  └─ hooks (curl.exe POST, in Git Bash or PowerShell) ──▶ chencho.exe agent  127.0.0.1:7777/hook
claude agents --json (every 3 s) ──▶   ├─ SessionHub (state machine) ─▶ SQLite %LOCALAPPDATA%\chencho\chencho.db
~/.claude/projects/*/*.jsonl ──────▶   ├─ library (the + menu)
                                       └─ WebSocket 127.0.0.1:7778/ws?token=… ◀── chencho-overlay.exe
                                                                                    ├─ strip + hover label (WPF)
                                                                                    ├─ notification-area icon
                                                                                    ├─ TerminalFocus (Win32 + UI Automation)
                                                                                    └─ AgentSupervisor (keeps the agent alive)
```

At login, `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Chencho` starts `chencho-overlay.exe`. The overlay starts `chencho.exe agent` if `/health` doesn't answer, and restarts it when it stops answering (D-002).

With `chencho setup --pi`, the overlay is registered with `--lan` and passes it to the agent, which then opens the WebSocket on `0.0.0.0` so another device on the local network (such as a Raspberry Pi display) can connect with the same token. The hook endpoint always stays on 127.0.0.1.

## Data flow

1. **Hooks.** `chencho setup` adds a command hook for 13 events to `~/.claude/settings.json` (after backing it up). Claude Code runs it in Git Bash, or in PowerShell where Git Bash isn't installed; the command reads the same in both (D-011): `curl.exe -s -m 1 -o NUL -H "Content-Type: application/json" --data-binary "@-" http://127.0.0.1:7777/hook; exit 0`. It pipes the event's JSON to the agent, prints nothing and always exits 0.
2. **Polling.** Every 3 s the agent runs `claude agents --json` for names, process ids, which sessions are alive, and to correct missed hooks.
3. **State.** `SessionHub` turns hooks and polls into session states (idle, working, needs permission, needs input, compacting, done, error, ended) and keeps pins and icons in SQLite.
4. **Overlay.** The overlay holds a WebSocket to the agent: it receives a snapshot and then each change (`snapshot`, `session`, `removed`, `focus`, `library`, `error`), and sends `open`, `pin`, `icon` and `library`.
5. **Clicks.** A running session's click is handled in the overlay: it finds the window hosting the Claude process and brings it (and its Windows Terminal tab) to the front (D-003). A stopped session's click is sent as `open`, and the agent resumes it with `claude --resume` in a new terminal tab (D-006).
6. **The + library.** The agent lists Claude Code's transcripts, most recent first (D-004).

## Modules

| Path | Language | Role |
|---|---|---|
| `shared/protocol.ts` | TS | Wire contract (snapshot, session, removed, focus, library, error / open, pin, icon, library), including each session's `pid`. Mirrored in `overlay/Chencho.Core/Protocol.cs`. |
| `shared/characters.json` | JSON | The 16×16 creature templates. Embedded in Chencho.Core. |
| `agent/src/state.ts`, `hub.ts`, `store.ts` | TS | Session state machine, the hub that holds sessions and broadcasts changes, and the SQLite store for pins and icons. |
| `agent/src/server.ts`, `protocol-guard.ts`, `poller.ts` | TS | The hook endpoint (127.0.0.1:7777), the WebSocket server (7778, token-checked, validated messages) and the `claude agents --json` poller. |
| `agent/src/hooks-config.ts`, `settings.ts` | TS | The hook command and merging it into, or out of, `~/.claude/settings.json` with a backup. |
| `agent/src/config.ts`, `autostart.ts`, `install.ts`, `status.ts` | TS | Paths under `%LOCALAPPDATA%\chencho`, the Run value, `setup`, `status` and `uninstall`. |
| `agent/src/cli.ts`, `chencho.ts` | TS | Command parsing (`setup [--pi]`, `status`, `uninstall [--purge]`, `agent [--lan]`, `version`) and the entry point. |
| `agent/src/i18n.ts` | TS | The CLI's language (en/es): `CHENCHO_LANG`, else `language` in `overlay.json`, else the system locale. Only text a person reads from the CLI is translated; logs and wire messages stay English. Tests run in English through the `bunfig.toml` preload (`agent/src/test-setup.ts`). |
| `agent/src/transcripts.ts` | TS | The + library from Claude Code's transcripts. |
| `agent/src/open-session.ts` | TS | Resumes a stopped session in a new Windows Terminal tab, or a new console window without Windows Terminal. |
| `overlay/Chencho.Core` | C# | Pure logic with no WPF: layout, sprites, appearance, status rules, protocol, session list, localization. |
| `overlay/Chencho.Overlay` | C# WPF | The strip window, hover label, menus (`MenuHost`), notification icon, agent link, supervisor and terminal focus. `--gallery` and `--focus` are development aids. |
| `overlay/Chencho.Core.Tests` | C# xUnit | Tests for Chencho.Core. |
| `npm/chencho` | JS | The npm launcher that runs the binary from `chencho-win32-x64`. |
| `scripts/release.ts` | TS | Builds `chencho.exe` (Bun `--compile`) and `chencho-overlay.exe` (`dotnet publish`) into `dist/npm/chencho-win32-x64/bin`, plus the launcher package. |

## Data

`%LOCALAPPDATA%\chencho\` holds:
- `bin\` (`chencho.exe`, `chencho-overlay.exe`)
- `config.json` (the pairing token)
- `chencho.db` (pins and icons)
- `overlay.json` (the edge, the collapsed state and the language)
- `logs\agent.log` and `logs\overlay.log`

## Languages

The overlay and the CLI speak English or Spanish, following Windows' display language unless `language` in `overlay.json` says otherwise (D-009). The agent always sends English on the wire; the overlay translates its fixed phrases when it shows them.

## Coordinates

`Chencho.Core.StripLayout` works in points with a bottom-left origin (y up). `StripView` flips the rects into WPF's top-left origin. Screen positions are centred vertically, so they read the same in both.
