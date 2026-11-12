# Work log

## 2026-11-11 — Arranque de Chencho
- Fase 0: los hooks de Claude Code en Windows corren en Git Bash (bash 5.2.15, `SHELL=/bin/bash.exe`), así que un comando `curl … || true` de bash sirve (con `curl` del PATH, porque `/usr/bin/curl` no existe en Git Bash).
- En Windows, un `curl` a un puerto local cerrado tarda ~1 s; con el agente supervisado no debería pasar.

## 2026-11-11 — Fase 2: Chencho.Core en C#
- `overlay/Chencho.sln` con `Chencho.Core` (net9.0, sin dependencias) y `Chencho.Core.Tests` (xUnit 2.9.2). `overlay/.gitignore` para bin/obj.
- `shared/characters.json` (las plantillas de los bichos), embebido en Chencho.Core con LogicalName `characters.json`.
- Un .cs por módulo (Appearance, StripLayout, Protocol, SessionList, Status, AgentConfig, Sprites/Sprite, Sprites/CharacterTemplate) + `Geometry.cs` (Point/Size/Rect, origen abajo-izquierda) + `Clock.cs` (FrameAt, 4 fps, en fase con images/creatures.gif).
- Detalles de semántica: redondeo `MidpointRounding.AwayFromZero` (sombreados, grises), `Pixels` con igualdad estructural, `cycle()` con frames negativos, ante un empate gana el primer mínimo, JSON estricto (las sesiones ilegibles se saltan una a una), `ClientMessage.Json` con claves ordenadas y `/` escapado, orden de nombres con `StringComparer.CurrentCulture` + id ordinal.
- `PanelSession.Pid` (int?) desde `pid` opcional del protocolo.
- Tests de layout, sprites, apariencia, estados, protocolo y lista de sesiones, más pid, wire names, Tile() = 1.5, redondeo, igualdad de Pixels, Token(path) y Clock. 77/77 verdes, 0 warnings.

## 2026-11-11 — Fase 1: agente TS para Windows
- `config.ts`: datos en `%LOCALAPPDATA%\chencho` (bin, logs dentro); `panelUrls()` con `os.hostname()` y adaptadores cuyo nombre *empieza* por Ethernet/Wi-Fi primero (con "contiene" el switch de Hyper-V `vEthernet (Default Switch)` quedaba primero en esta máquina).
- `hooks-config.ts`: `curl` del PATH (Git Bash), 13 eventos, `timeout: 2`.
- `autostart.ts`: valor `HKCU\…\Run\Chencho` = `"<bin>\chencho-overlay.exe" [--lan]` con `reg.exe`; `start()` lanza el overlay con `Bun.spawn` detached (verificado: sobrevive a su padre, también bajo `node spawnSync` como npx); `stop()` mata overlay y luego todo `chencho.exe` salvo el propio y espera a que no quede ninguno (Windows no deja reemplazar ni borrar un exe en uso); `describe()` para status con tasklist CSV (los avisos localizados se ignoran).
- `install.ts`: `SHIPPED = chencho.exe, chencho-overlay.exe`; exige .NET 9 Desktop Runtime (`dotnet --list-runtimes` o `%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\9.*`, por si el PATH de la terminal es viejo); stop → copia (`.x.new` + rename) salvo si ya corre desde BIN_DIR → config → hooks → Run → start. Uninstall tolera que BIN_DIR esté en uso.
- `transcripts.ts`: lee la cabeza por trozos de 64 KB hasta la primera línea con `cwd` (hay transcripts donde aparece pasados 500 KB por imágenes pegadas; tope 16 MB) y solo los últimos 64 KB para el título (custom-title > agent-name > ai-title). 100 sesiones reales en ~40 ms (155 ms en frío); de 307 transcripts, ninguno sin título tiene una línea de título fuera de la cola.
- `open-session.ts`: ver D-006. Sesión viva → `already running in a terminal (pid N)`.
- `server.ts`: library desde transcripts; `adoptFromTranscript`; `open` no marca foco (eso lo decide la franja). Protocolo: `PanelSession.pid`.
- Señales: SIGINT, SIGBREAK, SIGTERM.
- npm `win32-x64`, `release.ts` de un solo target (salta el overlay con aviso si falta el csproj), CI en `windows-latest` con setup-dotnet y zip vía `Compress-Archive`.
- Verificado: 64/64 tests (`bun test agent npm`), `tsc --noEmit` limpio, `chencho.exe` compilado: version/help, agente con LOCALAPPDATA temporal (health, sesiones de `claude agents`, hook falso → 204, snapshot con pid, library real, open de sesión viva rechazado, pin de sesión vieja adoptada), `status`, release.ts (copia en scratch) y el launcher npm.
- Sin probar: `chencho setup`/`uninstall` reales (registro y settings.json), `reg add` con las comillas del valor, `dotnet publish` del overlay, el workflow de CI.

## 2026-11-11 — Fase 3: Chencho.Overlay (WPF) escrito y verificado contra el GIF del README
- `overlay/Chencho.Overlay` (net9.0-windows, WPF + NotifyIcon de WinForms, `chencho-overlay.exe`, manifiesto PerMonitorV2): Program, OverlayWindow (tool window + WS_EX_NOACTIVATE + MA_NOACTIVATE), StripView/StripController (layout de Core con origen abajo-izquierda, volteado a WPF), LayerBox (capa con relleno bajo el borde), HoverLabel, Menus (CreatureMenu, LibraryMenu, MenuTheme.xaml estilo menú de Windows 11 claro/oscuro), TrayIcon (silueta del Critter + badge pixel con el número que te espera), AgentLink, AgentSupervisor, TerminalFocus, Preferences (`overlay.json`), SingleInstance (mutex), Log, Gallery.
- Verificación visual: `--gallery` reproduce images/creatures.gif (escala 4, paso 122 px, fondo #121214) y un script compara cada cuadro del GIF (48, 250 ms) muestreando el centro de cada píxel de sprite y el borde de cada tile: 0 diferencias en la misma fase (frame offset 0). Lo único que difería era la curva interna del borde redondeado: el radio interno tiene que ser ≈ radio − ancho/2, no radio − ancho; corregido en LayerBox.

## 2026-11-11 — Integración, instalación real y verificación end-to-end
- Build de release: `chencho.exe` (Bun, ~114 MB) + `chencho-overlay.exe` (single-file framework-dependent, ~330 KB) en `dist/npm/chencho-win32-x64/bin`.
- Probado en la máquina real:
  - `chencho setup`: binarios en `%LOCALAPPDATA%\chencho\bin`, hooks en `settings.json` (con backup), clave Run, overlay y agente corriendo.
  - `chencho status`: muestra todo.
  - `chencho uninstall`: `settings.json` queda idéntico al de antes del setup, borra la clave Run y frena los procesos; desde el propio bin, un cmd oculto borra la carpeta al salir.
  - Reinstalado al final.
  - Hooks reales con una sesión `claude -p` descartable: idle → working «Thinking…» → «Bash: echo chencho-e2e» → done → ended.
  - Todos los estados inyectados con hooks falsos: parpadeo de permiso, pulso de pregunta, anillos verde y rojo.
- Hover, menú contextual, menú del + (agregado scroll: con 100 sesiones pasaba el alto de la pantalla), plegado, borde izquierdo y menú de la bandeja: en ninguno de estos cambia el foco. El menú de la bandeja ahora devuelve el foco a la app anterior al cerrarse.
- Escritorios virtuales: la ventana de la franja no pertenece a ningún escritorio (GUID nulo en IVirtualDesktopManager), así que se ve en todos.
- Foco de terminales:
  - Click en un bicho trae la ventana de WT correcta.
  - Con dos pestañas en una ventana, `--focus` selecciona la pestaña correcta y la detecta como «en frente».
  - Reanudar una sesión cerrada abre una pestaña nueva de WT con `claude --resume`.
- Ícono de bandeja: Windows 11 lo mandaba al desborde; ahora se promueve a la barra la primera vez (D-007). El badge de espera es pixel-art (D-008).
- Consumo: ~0,05 % de un núcleo cada proceso; memoria del agente dominada por el runtime de Bun (binario vacío ≈ 100 MB de working set).
- Limpieza:
  - `.gitignore` ya no ignora `docs/`;
  - borrados los transcripts de mis sesiones de prueba (hookprobe, chencho-e2e);
  - cerradas las pestañas y ventanas de prueba.

## 2026-11-11 — CLI bilingüe (inglés y español)
- `agent/src/i18n.ts`: `pickLanguage(preference, locale)` puro (preferencia "en"/"es" de `overlay.json` gana; si no, `es` cuando el subtag del locale es `es`), `language` calculado una vez (`CHENCHO_LANG=en|es` lo pisa) y `t(en, es)` en línea en cada mensaje.
- Traducido solo el texto que lee una persona: USAGE y errores de argumentos (`cli.ts`), `status.ts` (etiquetas `inicio/franja/agente/hooks/estado/registros`, columnas alineadas), `install.ts` (resúmenes de setup/uninstall, error de .NET, binarios faltantes, pregunta de borrar datos que acepta y/s/si/sí en español), `autostart.ts` (`describe()` y "Chencho no se detuvo"). Logs del agente y mensajes del WebSocket siguen en inglés.
- Tests en inglés en cualquier máquina: `bunfig.toml` (raíz y `agent/`) precarga `agent/src/test-setup.ts`, que fija `CHENCHO_LANG=en` antes de que se importe nada (bun comparte el registro de módulos entre archivos de test, así que fijarlo dentro de un test no alcanza).
- Verificado: `bun test agent npm` 67/67, `bunx tsc --noEmit` limpio, `chencho.exe` compilado: help/status/bogus en español (locale es-MX) y con `CHENCHO_LANG=en`; `overlay.json` con `language` en/auto/basura probado con un LOCALAPPDATA falso.
- Archivos: agent/src/{i18n.ts, i18n.test.ts, test-setup.ts, cli.ts, status.ts, install.ts, autostart.ts}, bunfig.toml, agent/bunfig.toml, docs/architecture.md.

## 2026-11-11 — Español e inglés + los menús se cierran con un clic afuera
- Pedido del usuario:
  - hacerlo multi-idioma (español e inglés);
  - que los menús (bicho → Personaje, etc.) se cierren al hacer clic fuera de la app (no lo hacían: la franja nunca toma el foco).
- Idioma (D-009):
  - `Chencho.Core.Localization` (Resolve, Phrase, ColorName); Status/Summary/Ago con parámetro de idioma; `labels.es` en characters.json; 6 tests nuevos (83/83).
  - Overlay: `Locale` (preferencia en overlay.json, automático = idioma de Windows), textos de la franja, etiqueta, menús y bandeja, y el submenú **Idioma** (Automático / English / Español) que cambia todo en vivo.
  - CLI (subagente): `agent/src/i18n.ts`, `CHENCHO_LANG` para los tests (preload en bunfig.toml), status/setup/uninstall/usage en los dos idiomas (67/67).
  - `README.es.md` (subagente) y README con la función de idioma y el campo `labels`.
- Menús (D-010): `MenuHost` para los menús de la franja, del + y de la bandeja.
- Verificado en la app real:
  - en español: etiqueta «Te hizo una pregunta», menú Abrir/Anclar/Personaje/Color, bandeja Conectado/Ocultar franja/Borde/Idioma/Salir de Chencho;
  - con el submenú Personaje abierto (3 ventanas), un clic afuera deja solo la franja;
  - el menú de la bandeja también se cierra con un clic afuera;
  - elegir English desde Idioma cambia la franja y la bandeja al instante, y queda guardado en overlay.json.
- Reinstalado con `chencho setup`; el idioma quedó en automático (español en esta PC).

## 2026-11-11 — Hooks compatibles con PowerShell
- Problema: Claude Code corre los hooks en PowerShell cuando Git Bash no está instalado, y ahí un hook sincrónico que falla puede bloquear la sesión. El comando anterior (`curl -s -m 1 -H '…' --data-binary @- … >/dev/null 2>&1 || true`) es un error de sintaxis en PowerShell (exit 1).
- Comando nuevo (D-011, reemplaza a D-005): `curl.exe -s -m 1 -o NUL -H "Content-Type: application/json" --data-binary "@-" http://127.0.0.1:7777/hook; exit 0`.
  - `curl.exe`, porque en Windows PowerShell `curl` es Invoke-WebRequest;
  - `"@-"` entre comillas, porque un `@` suelto arranca un splat;
  - `-o NUL` en vez de una redirección, y `; exit 0` en vez de `|| true`;
  - sin salida, porque Claude Code agrega el stdout de un hook a la conversación en algunos eventos.
- Descartados: hooks `"type": "http"` (un "hook error" en cada transcript con el agente caído); la forma exec `command` + `args` con `onFailure: "continue"` (solo la entiende un Claude Code nuevo; uno viejo correría `curl.exe` pelado y saldría con 2, que bloquea); `"shell": "powershell"` en todos los hooks (~300 ms por evento aun donde hay Git Bash).
- `hooks-config.test.ts` actualizado: el comando nuevo, y reinstalar reemplaza el comando viejo de bash en vez de duplicarlo.
- Verificado: bash 87 ms, pwsh 7 373 ms, Windows PowerShell 5.1 255 ms; payload UTF-8 (ñ, emoji) intacto; exit 0 y stdout vacío también con el agente caído; a través de Claude Code 2.1.322 con `"shell": "powershell"` llegan UserPromptSubmit y Stop.
- Archivos: agent/src/hooks-config.ts, agent/src/hooks-config.test.ts, docs/decisions.md (D-011), docs/architecture.md, README.md.

## 2026-11-11 — README en español y documentación propia
- README.md pasa a estar en español (título, GIF, qué es, requisitos y compatibilidad, instalación, uso, cómo funciona, personalizar los bichos, desarrollo, privacidad, licencia); se borra README.es.md. Comandos y rutas revisados contra `cli.ts`, `install.ts`, `config.ts`, `open-session.ts` y `scripts/release.ts`.
- La instalación documentada es la compilación local (`bun install --cwd agent`, `bun scripts/release.ts`, `dist\npm\chencho-win32-x64\bin\chencho.exe setup`); npm está preparado pero sin publicar. `(cd agent; bun install)` se reemplazó por `bun install --cwd agent`, porque lo primero es un error de sintaxis en PowerShell.
- Nueva sección «Requisitos y compatibilidad»: Windows 10/11 x64, .NET 9 Desktop Runtime, Claude Code nativo con `claude` en el PATH (WSL no se ve), Git Bash opcional, cualquier terminal, qué hace el clic según la terminal, cómo se reanuda una sesión cerrada, idioma.
- docs/architecture.md y docs/decisions.md reescritos para describir Chencho por sí mismo (stack, procesos, flujo de datos, módulos, datos); D-001…D-010 con el mismo fondo, D-005 marcada como reemplazada por D-011.
- Archivos: README.md, README.es.md (borrado), docs/architecture.md, docs/decisions.md, tasks/todo.md, tasks/worklog.md.

## 2026-11-11 — Nombre Chencho, repo propio y sesiones que revivían al cerrarse
- Nombre Chencho en todo: `chencho.exe` / `chencho-overlay.exe`, `overlay/Chencho.sln` con Chencho.Core, Chencho.Core.Tests y Chencho.Overlay, `agent/src/chencho.ts`, `npm/chencho` (paquetes `chencho` y `chencho-win32-x64`, sin scope), datos en `%LOCALAPPDATA%\chencho` (`chencho.db`), valor `Chencho` en la clave Run, mutex `Local\chencho-overlay`, `CHENCHO_LANG`.
- Código y documentación descritos por sí mismos (subagentes): comentarios, fixtures de tests y textos de `--pi` reescritos; README en español; LICENSE sin cambios (el aviso MIT tiene que quedar). Se quitaron las skills de desarrollo heredadas (`.agents/`, `.claude/skills`, `skills-lock.json`), el test de reloj que ya no tenía sentido (82/82) y `OrderFront` pasó a `BringToFront`.
- Fix (D-012): un poll de `claude agents` que arrancaba antes del cierre de una sesión llegaba después del SessionEnd y la mostraba «trabajando» ~12 s. Ahora, dentro de GRACE_MS, manda el SessionEnd. Test nuevo (68/68); verificado con 3 sesiones `claude -p` seguidas.
- Reinstalado en esta PC: `uninstall` de la instalación anterior conservando datos, copia de config.json, overlay.json y la base (con su WAL) a `%LOCALAPPDATA%\chencho`, `chencho setup`. `status`: franja y agente corriendo, hooks instalados, 22 sesiones; settings.json igual salvo los 13 hooks con el comando nuevo.
- Git: historia nueva desde cero (la anterior no tenía commits propios ni stashes), remoto `origin` = github.com/Telayna-I/chencho, commit inicial como telayna-i.
- Archivos: todo el árbol (renombres), agent/src/state.ts, agent/src/state.test.ts, docs/decisions.md (D-012), tasks/*.

## 2026-11-11 — Endurecimiento: cmd.exe sin búsqueda en la carpeta y /hook sin páginas web
- Origen: revisión automática de seguridad del commit inicial (2 hallazgos).
- cmd.exe ahora corre por ruta completa y con `NoDefaultCurrentDirectoryInExePath=1` (`CMD` / `CMD_ENV` en config.ts), al reanudar sesiones y al borrar `bin\` tras `uninstall`. Verificado con un `pwsh.exe` plantado en la carpeta de trabajo: sin la variable corría el plantado, con `CMD_ENV` corre el real (y los alias de WindowsApps se siguen encontrando por PATH). Claude Code define esa variable para sus hijos, por eso no se veía en las pruebas manuales; el agente arranca desde la clave Run y no la hereda.
- `/hook` responde 403 con encabezado `Origin` o sin `Content-Type: application/json`, e ignora `session_id` que no sea UUID. Verificado: hooks reales de una sesión `claude -p` siguen llegando (idle → working → done → ended); un POST con Origin da 403.
- Tests: 70/70 (2 nuevos: cmd.exe por ruta completa; /hook rechaza página web, texto plano e id inválido). D-013, línea nueva en Privacidad del README.
- Archivos: agent/src/config.ts, open-session.ts, open-session.test.ts, install.ts, server.ts, server.test.ts, README.md, docs/decisions.md.
