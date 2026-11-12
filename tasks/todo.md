# Chencho

## Próximos pasos
- [ ] Usarlo con las sesiones reales unos días; anclar con el **+** las que quieras ver siempre (las no ancladas solo aparecen mientras trabajan o te esperan, y 5 min después de terminar).
- [ ] Opcional: publicar en npm (`npm/chencho/package.json` + `scripts/release.ts` ya arman `chencho` y `chencho-win32-x64`; el workflow publica al subir un tag `v*`).
- [ ] Opcional: probar en un monitor con escala ≠ 100 % (los píxeles de sprite a 125 % no caen en píxeles enteros) y con la terminal de VS Code.

## Nombre Chencho + repo propio
- [x] Renombrado (binarios, carpetas, namespaces, datos en %LOCALAPPDATA%\chencho)
- [x] README en español
- [x] Reinstalar en esta PC: `uninstall` de la instalación anterior (datos conservados), anclajes, token y preferencias copiados a %LOCALAPPDATA%\chencho, `chencho setup`; `status` OK, 13 hooks con el comando nuevo, el resto de settings.json idéntico
- [x] Historia de git nueva (sin la historia anterior), commit inicial como telayna-i y push a github.com/Telayna-I/chencho

## Revisión de seguridad del commit inicial
- [x] cmd.exe por ruta completa y sin buscar en la carpeta de la sesión (D-013); verificado con un exe plantado
- [x] /hook rechaza páginas web y ids que no son UUID (D-013); hooks reales siguen llegando
- [x] 70/70, reinstalado, commit y push

## Sesión que volvía a «trabajando» al cerrarse
- [x] Causa: un poll de `claude agents` que arrancó antes del cierre llega después del SessionEnd y la revivía ~12 s
- [x] Fix en `reconcileOne`: dentro de GRACE_MS manda el SessionEnd (D-012); test nuevo (68/68)
- [x] Verificado con 3 sesiones `claude -p` seguidas: working → done → ended, sin volver

## Hooks sin depender de Git Bash
- [x] Comando nuevo que se lee igual en Git Bash y en PowerShell: `curl.exe -s -m 1 -o NUL -H "Content-Type: application/json" --data-binary "@-" http://127.0.0.1:7777/hook; exit 0` (D-011)
- [x] Test de `hooks-config` actualizado (comando nuevo; el comando viejo de bash se reemplaza en vez de duplicarse)
- [x] Verificado en bash, pwsh 7 y Windows PowerShell 5.1: exit 0 y sin salida también con el agente caído, payload UTF-8 intacto
- [x] Verificado vía Claude Code 2.1.322 con `"shell": "powershell"`: llegan UserPromptSubmit y Stop

## Español e inglés + menús que se cierran con un clic afuera
- [x] Core: Localization + Status/Summary/Ago por idioma + labels de personajes (83/83)
- [x] Overlay: Locale, textos, submenú Idioma en la bandeja (cambio en vivo)
- [x] CLI bilingüe (67/67) y README en español
- [x] MenuHost: los menús se cierran con un clic afuera (verificado con el submenú Personaje abierto y con la bandeja)

## Fase 0: verificaciones previas
- [x] .NET 9 SDK instalado (9.0.321)
- [x] Shell de los hooks en Windows: Git Bash (bash 5.2.15), así que un `curl … || true` de bash sirve (después reemplazado por un comando que también corre en PowerShell, D-011)
- [x] Agente compilado con bun-windows-x64 (sqlite, serve, claude agents)
- [x] AttachConsole → GetConsoleWindow → root owner da la ventana de WT exacta por pid, y el título de la pestaña; UIA expone las pestañas de WT con ese título
- [x] En la app real: no-activate (el foco nunca cambia con menús ni clics), escritorios virtuales (la franja no pertenece a ningún escritorio: GUID nulo), SetForegroundWindow, UIA Select de pestañas

## Fase 1: agente TS para Windows
- [x] config, hooks, autostart (clave Run), install/uninstall/status
- [x] library desde transcripts, open → wt + claude --resume (vía cmd.exe, D-006), `pid` en el protocolo
- [x] uninstall desde el propio bin: un cmd oculto borra la carpeta al salir
- [x] tests verdes (`bun test agent npm` 64/64, `tsc --noEmit` limpio)

## Fase 2: Chencho.Core (C#) + tests
- [x] Protocol, AgentConfig, Status, SessionList, Appearance, Sprite, CharacterTemplate, StripLayout (+ Geometry, Clock)
- [x] characters.json en shared/, embebido
- [x] tests verdes (`dotnet test overlay`: 77/77, 0 warnings)

## Fase 3: Chencho.Overlay (WPF)
- [x] StripView/StripController (layout, tiles, sprites, reloj 4 fps, plegado animado)
- [x] HoverLabel, CreatureMenu, LibraryMenu (con scroll)
- [x] TrayIcon (badge, visible de entrada, devuelve el foco), AgentLink, SingleInstance, AgentSupervisor
- [x] TerminalFocus (click → ventana y pestaña de WT, anillo de foco)
- [x] --gallery: los 48 cuadros coinciden píxel a píxel con images/creatures.gif (0 diferencias en 48×8×583 muestras; máx. 11 por canal, cuantización del GIF)
- [x] --focus: prueba del click con el código real

## Fase 4: build y empaquetado
- [x] release.ts Windows (bun --compile + dotnet publish), npm win32-x64
- [ ] CI en windows-latest: escrito, no corrido (no hay push)

## Verificación
- [x] tests (64 + 77), comparación con images/creatures.gif, setup/status/uninstall reales, hooks reales con una sesión descartable, todos los estados con hooks falsos, hover, menús, plegado, borde izquierdo, click → ventana/pestaña correcta, reanudar una sesión cerrada en WT, CPU (~0,05 % de un núcleo cada proceso)

## Review
- El overlay coincide píxel a píxel con images/creatures.gif (sprites, tiles, bordes, fase de la animación).
- Decisiones propias de Windows: un click enfoca la terminal y su pestaña de WT; reanudar abre una pestaña nueva de WT; ícono de bandeja con un badge pixel con la cantidad de sesiones que esperan; menús con el estilo de Windows 11; fuentes Segoe UI / Cascadia Mono.
- Memoria: overlay ~130 MB de working set (WPF), agente ~150 MB (un binario de Bun vacío ya usa ~100 MB en Windows).
