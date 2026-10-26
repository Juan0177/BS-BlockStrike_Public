# BS-BlockStrike

FPS multiplayer da browser con grafica a blocchi, per giocare tra amici.

- **Sito**: https://juan0177.github.io/BS-BlockStrike_Public/ (mappe, modalità, armi con TTK, perk e scorestreak)
- **Launcher per Windows**: [BlockStrike.exe dall'ultima release](https://github.com/Juan0177/BS-BlockStrike_Public/releases/latest/download/BlockStrike.exe)

Questa repo contiene solo il sito e il launcher. Il gioco gira sul PC di chi ospita la partita: qui non ci sono
il codice del server, account, token o dati della rete dell'host.

## Come si entra in una partita

1. Scarica `BlockStrike.exe` e aprilo. L'exe non è firmato: al primo avvio Windows può mostrare "Windows ha
   protetto il PC", scegli **Ulteriori informazioni → Esegui comunque**.
2. Incolla il **codice invito** che ti manda l'host (`<ID rete>@<IP host>`). Si incolla una volta sola e resta
   salvato in `%LOCALAPPDATA%\BlockStrike\invito.txt`; si cambia dal link "Cambia codice invito".
3. Il launcher installa ZeroTier se manca (Windows chiede il permesso), entra nella rete privata dell'host e
   aspetta che l'host ti approvi e avvii il server. Poi apre il gioco in una finestra di Edge massimizzata.
4. Accedi con nickname e password: l'host deve averti aggiunto alla whitelist.

Il codice invito da solo non fa entrare nessuno: serve l'approvazione dell'host nel pannello ZeroTier e nella
whitelist del gioco.

## Contenuto

| Cartella | |
|---|---|
| `launcher/` | Sorgente del launcher (C# 5, .NET Framework 4 incluso in Windows) e icona |
| `.github/workflows/release.yml` | A ogni tag `v*` compila l'exe su Windows, esegue l'autotest e pubblica la release |
| branch `gh-pages` | Il sito, generato dalla configurazione del gioco a ogni aggiornamento |

Sito e launcher vengono aggiornati in automatico dalla repo del gioco: le modifiche fatte qui a mano vengono
sovrascritte.
