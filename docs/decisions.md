# Decisions

ADR-lite. Entries are never edited or deleted; a later entry supersedes an earlier one by its ID.

## D-001 — The overlay is C# WPF on .NET 9
- **Decision:** the overlay is `overlay/Chencho.Overlay` (WPF, .NET 9, framework-dependent single-file `chencho-overlay.exe`). Its pure logic (layout, sprites, appearance, status rules, protocol, session list) lives in `overlay/Chencho.Core`, which has no WPF dependency and its own xUnit tests.
- **Why:** WPF has what the strip needs out of the box: layered transparent windows, rounded rects, key-spline animations, nearest-neighbour bitmaps, text with alpha and menus with images. The .NET 9 Desktop Runtime was already installed.
- **Discarded:** Go + raw Win32 (nothing to install, but rounded borders, easing, shadows and text would all be hand-drawn); Electron or Tauri (heavy for a 64-pixel strip).

## D-002 — The overlay supervises the agent
- **Decision:** `chencho setup` registers only the overlay to start at login, under `HKCU\…\Run\Chencho`. The overlay starts `chencho.exe agent` through `cmd /c … >> logs\agent.log` with no window, and restarts it when `/health` stops answering.
- **Why:** Windows has no per-user service manager that keeps a program alive. A console program launched at login shows a window. Task Scheduler logon triggers can need admin rights. The agent keeps running after "Quit Chencho", so hooks still land while the strip is closed.
- **Discarded:** a Scheduled Task (admin rights and a console window); a Run value for the agent itself (console window, no restart); a second agent binary built with a hidden console (duplicates ~100 MB).

## D-003 — Open and focus terminals, using a `pid` protocol field
- **Decision:**
  - `PanelSession` carries `pid: number | null`, the Claude process.
  - The overlay finds the window that hosts that process: `AttachConsole` → `GetConsoleWindow` → root owner (Windows Terminal, conhost), falling back to the nearest ancestor process with a window (VS Code).
  - A click brings that window to the front and selects the session's Windows Terminal tab through UI Automation.
  - The white "in front" ring follows the foreground window. Windows Terminal titles its window after the selected tab, which Claude Code titles "<glyph> <session name>".
  - Sessions that aren't running resume with `wt -w 0 new-tab -d <cwd> pwsh -NoExit -Command claude --resume <id>`, launched by the agent.
- **Why:** Claude Code runs in terminals, so a click has to reach the terminal window, and the tab, that hosts the session's process; the agent already knows that process id from `claude agents --json`. The overlay can set the foreground window only because it just received the click.
- **Discarded:** the agent focusing windows through `bun:ffi` (no input rights, no UI Automation); reading `~/.claude/sessions` from the overlay instead of extending the protocol (duplicates the agent's knowledge).

## D-004 — The add button's library comes from Claude Code's transcripts
- **Decision:** the agent lists `~/.claude/projects/*/<uuid>.jsonl`, most recently modified first (up to 100). It takes `cwd` from the head of each file and the name from its tail, in this order: the last `custom-title`, else `agent-name`, else `ai-title`, else the folder's name.
- **Why:** it covers every Claude Code session on the PC, running or not.
- **Discarded:** the agent's own SQLite table, which forgets unpinned sessions after 24 h.

## D-005 — Hooks run a bash curl command (superseded by D-011)
- **Decision:** the hook command is `curl -s -m 1 -H 'Content-Type: application/json' --data-binary @- http://127.0.0.1:7777/hook >/dev/null 2>&1 || true`, a bash-only command, with `curl` from PATH.
- **Why:** verified that Claude Code on Windows runs command hooks in Git Bash (`BASH_VERSION=5.2.15`, `SHELL=/bin/bash.exe`). Git Bash has no `/usr/bin/curl`, so the path is left to PATH.
- **Discarded:** a `chencho hook` subcommand, which would start a ~100 MB Bun binary on every event.

## D-006 — Resume through cmd.exe, with the session's folder as the working directory (supersedes the resume command in D-003)
- **Decision:** `open-session.ts` runs `cmd.exe /d /c wt.exe -w 0 new-tab -d . <shell> -NoExit -Command claude --resume <id>`, or without Windows Terminal `cmd.exe /d /c start "" <shell> -NoExit -Command "claude --resume <id>"`, spawned with `cwd` set to the session's folder (from its transcript). `<shell>` is `pwsh.exe` when it is on PATH, else `powershell.exe`. PATH lookups use `lstat`, not `Bun.which`.
- **Why:** Store installs of `wt.exe` and `pwsh.exe` are app execution aliases in `%LOCALAPPDATA%\Microsoft\WindowsApps`: `Bun.which` and `existsSync` don't see them, and `Bun.spawn` can't start them even by full path, while cmd.exe can. Bun quotes an argument only when it has spaces, so a folder such as `D:\R&D` on cmd.exe's command line would be split at the `&`; passing it as the working directory keeps it off the command line. Verified: `wt -d .` opens the tab in the caller's directory (a folder named `a&b`), and `start` opens a visible console in `R&D x` even when cmd.exe itself is hidden.
- **Discarded:** `Bun.spawn(["wt.exe", …])` (fails: "Executable not found"); the cwd on cmd.exe's command line (breaks on `&`, `^`, `%`); resolving the alias target under `C:\Program Files\WindowsApps` (ACL-protected, versioned path).

## D-007 — The notification icon starts on the taskbar
- **Decision:** the first time Explorer lists the overlay's icon under `HKCU\Control Panel\NotifyIconSettings`, and only if that entry has no `IsPromoted` value yet, the overlay sets `IsPromoted = 1`.
- **Why:** Windows 11 puts new icons in the overflow, where the count of sessions waiting for you would go unseen. Leaving an existing value alone respects the user's choice if they hide it later.
- **Discarded:** asking users to drag the icon out themselves; setting the value on every start, which would override their choice.

## D-008 — The waiting count is a pixel badge on the icon
- **Decision:** the number of sessions waiting for you is an orange 7×7 badge with a 3×5 pixel digit (`+` above 9), drawn on the 16×16 silhouette. The tooltip spells it out: "Chencho · N need you".
- **Why:** Windows shows no text beside notification icons. A pixel font matches the creatures and stays legible at 16 px.

## D-009 — English and Spanish, translated where they're shown
- **Decision:**
  - The overlay and the CLI speak English or Spanish. The default is Windows' display language: the overlay uses `CultureInfo.CurrentUICulture`, the CLI uses `Intl` (which reports `es-MX` here even without `LANG`).
  - A `language` setting in `overlay.json` (`auto`, `en` or `es`), chosen under **Language/Idioma** in the notification-area menu, overrides it for both. `CHENCHO_LANG` overrides it for the CLI, which the tests use.
  - The agent keeps sending English on the wire. The overlay translates its fixed phrases ("Thinking…", "Subagent: x", "already running in a terminal (pid N)", …) in `Chencho.Core.Localization` when it shows them. Tool details and messages Claude Code wrote itself are shown as they came.
  - `characters.json` gains optional `labels` per language.
- **Why:**
  - The protocol stays language-free, so another device connected with `--pi` is unaffected.
  - The language can change live without a round trip to the agent.
  - The tests pin both languages.
- **Discarded:**
  - Localizing in the agent, which would need the overlay's language on the wire and would force one language on every connected device.
  - .resx resource files, which are heavy for about sixty strings in two languages; the pairs sit inline, side by side.

## D-010 — Menus hold the foreground while open
- **Decision:**
  - Every context menu (creature, + library, notification icon) opens through `MenuHost`. While the menu is open, an off-screen tool window of Chencho's takes the foreground.
  - When it loses the foreground, which happens on a click anywhere else, the menu closes.
  - On closing, if the off-screen window still has the foreground, the app that was in front gets it back.
- **Why:** a WPF menu opened from a window that never activates (the strip) didn't close on a click outside Chencho. Holding the foreground is also how native Windows menus work, and it gives the menu keyboard navigation and Escape.
- **Discarded:** a global low-level mouse hook while a menu is open. It would close the menu without taking the foreground, but it's heavier and still leaves Escape and the arrow keys to the terminal.

## D-011 — Hook command that reads the same in Git Bash and PowerShell (supersedes D-005)
- **Decision:** every hook runs
  `curl.exe -s -m 1 -o NUL -H "Content-Type: application/json" --data-binary "@-" http://127.0.0.1:7777/hook; exit 0`
  (command hook, synchronous, `timeout: 2`). Running `chencho setup` again replaces the D-005 command instead of adding a second one, since our hooks are recognised by their URL.
- **Why:** Claude Code runs hooks in PowerShell when Git Bash isn't installed, and there a failing synchronous hook can block the session. The D-005 command is a parse error in PowerShell (exit 1). Each piece of the new one works in both shells:
  - `curl.exe`, because in Windows PowerShell `curl` is an alias of `Invoke-WebRequest`;
  - `"@-"` quoted, because a bare `@` starts a splat in PowerShell;
  - `-o NUL` instead of `>/dev/null 2>&1`;
  - `; exit 0` instead of `|| true`;
  - no output at all, because Claude Code adds a hook's stdout to the conversation for some events.
- **Verified:** bash 87 ms, pwsh 7 373 ms, Windows PowerShell 5.1 255 ms per event; a UTF-8 payload (ñ, emoji) arrives intact; exit 0 and empty stdout also with the agent down. Through Claude Code 2.1.322 with `"shell": "powershell"`: UserPromptSubmit and Stop delivered.
- **Discarded:**
  - `"type": "http"` hooks, which add a "hook error" to every transcript while the agent is down;
  - the exec form (`command` + `args`) with `onFailure: "continue"`, which only newer Claude Code understands; an older one would run a bare `curl.exe` and exit 2, which blocks;
  - `"shell": "powershell"` on every hook, which costs ~300 ms per event even where Git Bash is installed.

## D-012 — A fresh SessionEnd wins over a poll that raced it
- **Decision:** when `claude agents --json` still lists a session whose SessionEnd hook arrived less than `GRACE_MS` (10 s) ago, the poll leaves it ended. After that window, a session that shows up in the poll again comes back to life as before.
- **Why:** `claude agents` takes a second or two to answer, so a poll that started just before a session closed lists it after its SessionEnd. That brought the creature back as "working" for about 12 s after each close (seen on the first try with a throwaway `claude -p` session).
- **Verified:** a new state test (`a poll that raced the session's end doesn't bring it back`); three throwaway sessions in a row went working → done → ended and stayed ended.
- **Discarded:** ignoring the poll for ended sessions altogether, which would keep a session resumed without hooks (e.g. after `setup` was undone and redone) ended until its next hook.
