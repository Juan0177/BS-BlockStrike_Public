- **Altri giocatori più fluidi**:
  - a inizio partita, dopo la playlist o dal poligono, tutti si muovono subito in modo fluido. Prima per i primi
    3-10 secondi scattavano e i colpi non avevano la compensazione della latenza;
  - un amico con la connessione che perde un pacchetto ogni tanto non si ferma più e non scatta in avanti un paio di
    secondi dopo: il server si ricorda più a lungo quanto serve aspettare i suoi comandi;
  - il ping pareggiato non fa più sobbalzare la scena dell'host quando il ping di un amico ha un picco, e i messaggi
    ritardati arrivano sempre nell'ordine giusto.
- **Diagnostica di rete per l'host**: avviando con `avvia.bat` e rispondendo S a «Avviare in modalità DEV?», sul
  secondo monitor si apre una finestra con le misure di rete di tutti, aggiornate 4 volte al secondo: ping, buchi
  negli input e fermate sul server, FPS e interpolazione di ogni giocatore, tick del server, con grafici, eventi e il
  pulsante Esporta per salvare gli ultimi 10 minuti in un file da analizzare (anche dopo che uno scatto è successo);
  su un monitor verticale la tabella si gira.
