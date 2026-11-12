# Lessons

(Patrones a evitar, se agregan después de cada corrección.)

- **Hooks en Windows: Git Bash o PowerShell.** Claude Code corre los hooks en Git Bash si está instalado y en PowerShell si no, y ahí un hook que falla puede bloquear la sesión. Todo comando de hook tiene que ser válido en los dos shells y salir siempre con 0: probarlo en bash, pwsh y Windows PowerShell 5.1 antes de darlo por bueno, no solo en la máquina donde hay Git Bash.
- **Un poll puede llegar viejo.** Un comando lento (`claude agents`) que arrancó antes de un evento devuelve un estado anterior a ese evento. Dentro de la ventana de gracia, el evento fresco del hook manda sobre el poll.
- **Un proceso que queda corriendo hereda la carpeta de quien lo lanzó.** Fijarle la carpeta de trabajo (la de sus binarios): si no, bloquea esa carpeta y busca ahí los exe que lanza por nombre. Todo `cmd.exe` u otro exe del sistema va por ruta completa, también en C#, no solo en el agente.
- **Probar la instalación desde un clon limpio**, en una carpeta con espacios y tildes, antes de decir que funciona en otra PC.
