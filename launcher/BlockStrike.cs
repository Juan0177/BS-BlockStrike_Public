// Launcher di BS-BlockStrike per Windows (vedi docs/GUIDA-HOST.md).
//   nessun argomento → amico: entra nella rete ZeroTier dell'host, aspetta il server, apre il gioco
//   --locale         → host: aspetta il server su localhost e apre il gioco
//   --entra <rete>   → passo con i permessi di amministratore: zerotier-cli join
//   --autotest [<zip> <impronta>] → verifica le funzioni pure (usato da test/launcher.test.js); con uno zip del
//                    server e la sua impronta prova anche l'installazione del pacchetto vero
//   --anteprima <attesa|invito|errore> [--locale] → solo l'aspetto della finestra, senza rete né file
//                    (per gli screenshot); si chiude da sola dopo 15 secondi
// Scritto in C# 5 per compilare con il csc.exe di .NET Framework 4 incluso in Windows.
// La classe Config (ID rete, IP host, porta) è generata da strumenti/crea-launcher.js. Con la config
// vuota (il launcher pubblico) l'amico incolla il codice invito dell'host, salvato in
// %LOCALAPPDATA%\BlockStrike\invito.txt: così l'exe pubblico non contiene niente della rete dell'host.
//
// Dalla 1.9 il gioco non si apre più dall'host: il launcher chiede GET /versione ({ versione, impronta }), se in
// %LOCALAPPDATA%\BlockStrike\gioco non c'è già quell'impronta scarica GET /pacchetto (zip), lo estrae in gioco.tmp,
// ricalcola l'impronta dai file e solo se è uguale lo mette al posto di gioco (un pacchetto a metà o cambiato
// durante lo scaricamento si scarta). Poi serve la cartella da http://127.0.0.1:3917 (ServerLocale, solo loopback)
// e apre Edge su http://127.0.0.1:3917/; l'host e la versione il gioco li legge da /launcher.json, che il launcher
// genera in memoria (mai un parametro nell'indirizzo: un link con un altro server manderebbe lì nick e password).
// Resta aperto (ridotto) finché il gioco è aperto. Il formato dell'impronta è descritto in server/pacchetto.js.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Launcher {
  public const int ANNULLATO_DA_UTENTE = 1223; // l'utente ha rifiutato la richiesta di Windows (UAC)

  [STAThread]
  static int Main(string[] args) {
    string modo = args.Length > 0 ? args[0] : "";
    if (modo == "--autotest") return Autotest(args.Length > 2 ? args[1] : null, args.Length > 2 ? args[2] : null);
    if (modo == "--entra") return Entra(args.Length > 1 ? args[1] : "");
    if (modo == "--avvia-zerotier") return AvviaZeroTier();
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    var finestra = new Finestra(Array.IndexOf(args, "--locale") >= 0, modo == "--anteprima" && args.Length > 1 ? args[1] : null);
    Application.Run(finestra);
    return finestra.Codice;
  }

  // Con i permessi di amministratore: su Windows zerotier-cli li richiede
  static int Entra(string rete) {
    if (!ReteValida(rete)) return 2;
    int codice = -1;
    for (int i = 0; i < 10; i++) { // il servizio appena installato può metterci qualche secondo
      codice = Esegui("cmd.exe", "/c \"\"" + ZeroTierCli() + "\" join " + rete + "\"", true);
      if (codice == 0) return 0;
      Thread.Sleep(1000);
    }
    return codice;
  }

  // Con i permessi di amministratore: avvia il servizio di ZeroTier (spento dopo un'installazione a metà o fermato
  // dall'antivirus) e aspetta che giri
  static int AvviaZeroTier() {
    Esegui("sc.exe", "start " + SERVIZIO_ZEROTIER, true);
    for (int i = 0; i < 10; i++) {
      if (ZeroTierAcceso()) return 0;
      Thread.Sleep(1000);
    }
    return 1;
  }

  // --- Funzioni pure ---

  public static bool ReteValida(string rete) {
    return rete != null && Regex.IsMatch(rete, "^[0-9a-f]{16}$");
  }

  // IPv4 senza zeri davanti: l'IP si confronta come testo con quelli delle schede di rete
  public static bool IpValido(string ip) {
    return ip != null && Regex.IsMatch(ip, @"^((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)$");
  }

  // Codice invito dell'host: "<ID rete>@<IP host>" oppure "<ID rete>@<IP host>:<porta>"
  // (lo stampa `npm run launcher -- --invito`)
  public static bool LeggiInvito(string testo, out string rete, out string host, out int porta) {
    rete = host = null;
    porta = 3000;
    var m = Regex.Match((testo ?? "").Trim(), @"^([0-9a-fA-F]{16})@([0-9.]+)(?::(\d{1,5}))?$");
    if (!m.Success) return false;
    rete = m.Groups[1].Value.ToLowerInvariant();
    host = m.Groups[2].Value;
    if (m.Groups[3].Success) porta = int.Parse(m.Groups[3].Value);
    return IpValido(host) && porta >= 1 && porta <= 65535;
  }

  // Sottorete /24, quella predefinita delle reti ZeroTier
  public static bool StessaSottorete(string ip, string host) {
    int a = ip.LastIndexOf('.'), b = host.LastIndexOf('.');
    return a > 0 && b > 0 && ip.Substring(0, a) == host.Substring(0, b);
  }

  // Qualsiasi risposta (anche 404) vuol dire che il server è acceso
  public static bool HaRisposto(HttpStatusCode? stato) {
    return stato.HasValue;
  }

  // Edge in modalità app, massimizzato (senza, la finestra si apre a metà schermo), con un profilo
  // separato che tiene il token di accesso e non si mescola all'Edge personale
  public static string ArgomentiEdge(string url, string profilo) {
    return "--app=" + url + " --start-maximized --user-data-dir=\"" + profilo + "\"";
  }

  // --- Pacchetto del gioco (vedi server/pacchetto.js) ---

  // Porta del server locale che serve il gioco: è LAUNCHER_PORTA in shared/config/rete.js, l'unica origine
  // (oltre all'host) a cui il server dell'host dà CORS e WebSocket
  public const int PORTA_LOCALE = 3917;
  public const string FILE_IMPRONTA = "impronta.txt", MANIFESTO = "pacchetto.json";
  public const long PACCHETTO_MAX = 64L * 1024 * 1024;

  public static string Sha256(byte[] dati) {
    using (var sha = SHA256.Create()) {
      var sb = new StringBuilder(64);
      foreach (byte b in sha.ComputeHash(dati)) sb.Append(b.ToString("x2"));
      return sb.ToString();
    }
  }

  // SHA-256 di "<percorso>\n<sha256 del contenuto>\n" per ogni file, in ordine ordinale di percorso, come il server
  public static string Impronta(IDictionary<string, string> hashPerPercorso) {
    var percorsi = new List<string>(hashPerPercorso.Keys);
    percorsi.Sort(StringComparer.Ordinal);
    var testo = new StringBuilder();
    foreach (string p in percorsi) testo.Append(p).Append('\n').Append(hashPerPercorso[p]).Append('\n');
    return Sha256(Encoding.UTF8.GetBytes(testo.ToString()));
  }

  public static bool ImprontaValida(string impronta) {
    return impronta != null && Regex.IsMatch(impronta, "^[0-9a-f]{64}$");
  }

  // Risposta di /versione: {"versione":"1.9.0","impronta":"<64 cifre esadecimali>"}
  public static bool LeggiVersione(string json, out string versione, out string impronta) {
    var v = Regex.Match(json ?? "", "\"versione\"\\s*:\\s*\"(\\d{1,4}\\.\\d{1,4}\\.\\d{1,4})\"");
    var i = Regex.Match(json ?? "", "\"impronta\"\\s*:\\s*\"([0-9a-f]{64})\"");
    versione = v.Success ? v.Groups[1].Value : null;
    impronta = i.Success ? i.Groups[1].Value : null;
    return v.Success && i.Success;
  }

  // Percorso relativo sicuro (di una voce dello zip o di un indirizzo già decodificato): solo nomi separati da '/',
  // niente '..', '.', radici, unità (C:), barre rovesciate, caratteri di controllo o vietati da Windows. null se non va
  public static string PercorsoRelativo(string p) {
    if (string.IsNullOrEmpty(p) || p.Length > 200 || p[0] == '/' || p.EndsWith("/")) return null;
    foreach (char ch in p) if (ch < 32 || ch == '\\' || ch == ':' || ch == '*' || ch == '?' || ch == '"' || ch == '<' || ch == '>' || ch == '|') return null;
    foreach (string parte in p.Split('/')) if (parte == "" || parte == "." || parte == ".." || parte.EndsWith(".") || parte.EndsWith(" ")) return null;
    return p;
  }

  // File da servire per il percorso di una richiesta ("/js/main.js?x=1"), dentro `cartella`; null se esce dalla
  // cartella o non è valido. "/" è index.html
  public static string PercorsoLocale(string cartella, string richiesta) {
    if (string.IsNullOrEmpty(richiesta) || richiesta[0] != '/') return null;
    int q = richiesta.IndexOfAny(new[] { '?', '#' });
    if (q >= 0) richiesta = richiesta.Substring(0, q);
    string rel;
    try { rel = Uri.UnescapeDataString(richiesta.Substring(1)); } catch (UriFormatException) { return null; }
    if (rel == "") rel = "index.html";
    if (PercorsoRelativo(rel) == null) return null;
    string radice = Path.GetFullPath(cartella).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    string completo = Path.GetFullPath(Path.Combine(radice, rel.Replace('/', Path.DirectorySeparatorChar)));
    return completo.StartsWith(radice, StringComparison.OrdinalIgnoreCase) ? completo : null;
  }

  public static string TipoMime(string percorso) {
    switch (Path.GetExtension(percorso).ToLowerInvariant()) {
      case ".html": return "text/html; charset=utf-8";
      case ".js": return "text/javascript; charset=utf-8";
      case ".css": return "text/css; charset=utf-8";
      case ".json": return "application/json; charset=utf-8";
      case ".svg": return "image/svg+xml";
      case ".png": return "image/png";
      case ".jpg": return "image/jpeg";
      case ".gif": return "image/gif";
      case ".webp": return "image/webp";
      case ".ico": return "image/x-icon";
      case ".mp3": return "audio/mpeg";
      case ".txt": return "text/plain; charset=utf-8";
      default: return "application/octet-stream";
    }
  }

  // /launcher.json del server locale: host a cui si collega il gioco e versione del launcher. `server` è "IP:porta"
  // già controllato (IpValido e porta), quindi niente da sfuggire nel JSON
  public const string FILE_LAUNCHER = "/launcher.json";
  // Risposta di /launcher.json di un altro launcher già acceso sulla porta locale?
  public static bool AltroLauncher(string json) {
    return json != null && Regex.IsMatch(json, "^\\{\"server\":\"[^\"]+\",\"versione\":\"[^\"]+\"\\}$");
  }

  // Testo dell'icona nell'area di notifica (NotifyIcon ne accetta al massimo 63 caratteri)
  public const string TESTO_NOTIFICA = "BlockStrike: gioco in corso";
  public static string ConfigGioco(string ip, int porta, string versione) {
    return "{\"server\":\"" + ip + ":" + porta + "\",\"versione\":\"" + versione + "\"}";
  }

  // La richiesta è /launcher.json (con o senza parametri)?
  public static bool ChiedeConfig(string richiesta) {
    if (richiesta == null) return false;
    int q = richiesta.IndexOfAny(new[] { '?', '#' });
    return (q >= 0 ? richiesta.Substring(0, q) : richiesta) == FILE_LAUNCHER;
  }

  // Intestazione Host ammessa dal server locale: solo il suo indirizzo (una pagina di un altro sito che si fa
  // risolvere in 127.0.0.1, il DNS rebinding, manda il proprio nome e viene respinta)
  public static bool HostAmmesso(string host, int porta) {
    return host == "127.0.0.1:" + porta || host == "localhost:" + porta;
  }

  // Impronta del pacchetto già installato in `cartellaGioco` (null se manca o è incompleto)
  public static string ImprontaInstallata(string cartellaGioco) {
    try {
      string f = Path.Combine(cartellaGioco, FILE_IMPRONTA);
      string i = File.Exists(f) && File.Exists(Path.Combine(cartellaGioco, "index.html")) ? File.ReadAllText(f).Trim() : null;
      return ImprontaValida(i) ? i : null;
    } catch (IOException) {
      return null;
    }
  }

  // Estrae lo zip in <cartellaGioco>.tmp e ricalcola l'impronta dai file: solo se è `attesa` la cartella prende il
  // posto di quella vecchia (con impronta.txt). Altrimenti (pacchetto a metà, di un'altra versione, voci con percorsi
  // non validi) la cartella temporanea si cancella, quella vecchia resta com'è e restituisce false
  public static bool InstallaPacchetto(Stream zip, string attesa, string cartellaGioco) {
    string tmp = cartellaGioco + ".tmp";
    Cancella(tmp);
    bool ok = false;
    try {
      var hash = new Dictionary<string, string>(StringComparer.Ordinal);
      long totale = 0;
      using (var archivio = new ZipArchive(zip, ZipArchiveMode.Read, true)) {
        foreach (var voce in archivio.Entries) {
          if (voce.FullName.EndsWith("/") && voce.Length == 0) continue; // cartella
          string rel = PercorsoRelativo(voce.FullName);
          if (rel == null || rel.Equals(FILE_IMPRONTA, StringComparison.OrdinalIgnoreCase) || hash.ContainsKey(rel)) return false;
          totale += voce.Length;
          if (totale > PACCHETTO_MAX) return false;
          byte[] dati;
          using (var s = voce.Open())
          using (var m = new MemoryStream()) {
            s.CopyTo(m);
            dati = m.ToArray();
          }
          string dest = Path.Combine(tmp, rel.Replace('/', Path.DirectorySeparatorChar));
          Directory.CreateDirectory(Path.GetDirectoryName(dest));
          File.WriteAllBytes(dest, dati);
          if (rel != MANIFESTO) hash[rel] = Sha256(dati);
        }
      }
      if (hash.Count == 0 || Impronta(hash) != attesa) return false;
      File.WriteAllText(Path.Combine(tmp, FILE_IMPRONTA), attesa);
      // Prima l'impronta: se la cancellazione si ferma a metà (file bloccato) la cartella vecchia non risulta valida
      string vecchia = Path.Combine(cartellaGioco, FILE_IMPRONTA);
      if (File.Exists(vecchia)) File.Delete(vecchia);
      Cancella(cartellaGioco);
      Directory.Move(tmp, cartellaGioco);
      ok = true;
      return true;
    } catch (InvalidDataException) {
      return false; // zip rovinato o troncato
    } finally {
      if (!ok) Cancella(tmp);
    }
  }

  static void Cancella(string cartella) {
    if (Directory.Exists(cartella)) Directory.Delete(cartella, true);
  }

  // Prova di InstallaPacchetto con zip fatti qui: impronta giusta, sbagliata, zip troncato e voci fuori cartella
  static bool AutotestPacchetto() {
    string base_ = Path.Combine(Path.GetTempPath(), "bs-autotest-" + Guid.NewGuid().ToString("N"));
    string gioco = Path.Combine(base_, "gioco");
    try {
      var file = new Dictionary<string, string> { { "index.html", "<html>" }, { "a/x.json", "A" }, { "b.js", "B" } };
      var hash = new Dictionary<string, string>();
      foreach (var f in file) hash[f.Key] = Sha256(Encoding.UTF8.GetBytes(f.Value));
      string imp = Impronta(hash);
      // Lo stesso valore del test di server/pacchetto.js (test/pacchetto.test.js)
      if (imp != "0229d25e30bc38c04f6d6f504cf206444863349004fda4c449d50c7a18974ef9") return Fallito("impronta");
      file[MANIFESTO] = "{\"impronta\":\"x\"}"; // il manifesto non conta nell'impronta
      byte[] zip = CreaZip(file);
      Directory.CreateDirectory(base_);
      if (InstallaPacchetto(new MemoryStream(zip), new string('0', 64), gioco) || Directory.Exists(gioco) || Directory.Exists(gioco + ".tmp")) return Fallito("impronta diversa");
      if (!InstallaPacchetto(new MemoryStream(zip), imp, gioco) || File.ReadAllText(Path.Combine(gioco, "a", "x.json")) != "A"
        || ImprontaInstallata(gioco) != imp || Directory.Exists(gioco + ".tmp")) return Fallito("installazione");
      // Un pacchetto troncato (scaricamento a metà) non tocca quello installato
      byte[] troncato = new byte[zip.Length / 2];
      Array.Copy(zip, troncato, troncato.Length);
      if (InstallaPacchetto(new MemoryStream(troncato), imp, gioco) || ImprontaInstallata(gioco) != imp) return Fallito("troncato");
      // Voci che escono dalla cartella o sovrascrivono impronta.txt: rifiutate, niente scritto fuori
      foreach (string nome in new[] { "../fuori.txt", "a/../../fuori.txt", "/fuori.txt", "C:/fuori.txt", "a\\..\\..\\fuori.txt", "impronta.txt" }) {
        var cattivo = new Dictionary<string, string>(file);
        cattivo[nome] = "x";
        if (InstallaPacchetto(new MemoryStream(CreaZip(cattivo)), imp, gioco) || ImprontaInstallata(gioco) != imp) return Fallito(nome);
      }
      return !File.Exists(Path.Combine(base_, "fuori.txt")) || Fallito("file fuori");
    } finally {
      Cancella(base_);
    }
  }

  static bool Fallito(string cosa) {
    Console.WriteLine("fallito: " + cosa);
    return false;
  }

  static byte[] CreaZip(Dictionary<string, string> file) {
    using (var m = new MemoryStream()) {
      using (var archivio = new ZipArchive(m, ZipArchiveMode.Create, true)) {
        foreach (var f in file) {
          using (var s = new StreamWriter(archivio.CreateEntry(f.Key).Open(), new UTF8Encoding(false))) s.Write(f.Value);
        }
      }
      return m.ToArray();
    }
  }

  static int Autotest(string zipVero, string improntaVera) {
    string r, h;
    int p;
    string ver, imp;
    string cartella = Path.Combine(Path.GetTempPath(), "bs-gioco");
    string radice = Path.GetFullPath(cartella) + Path.DirectorySeparatorChar;
    bool pacchetto = Sha256(new byte[0]) == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
      && LeggiVersione("{\"versione\":\"1.9.0\",\"impronta\":\"" + new string('a', 64) + "\"}", out ver, out imp) && ver == "1.9.0" && imp == new string('a', 64)
      && !LeggiVersione("{\"versione\":\"1.9.0\",\"impronta\":\"../x\"}", out ver, out imp)
      && PercorsoLocale(cartella, "/") == radice + "index.html"
      && PercorsoLocale(cartella, "/js/main.js?v=1") == radice + "js" + Path.DirectorySeparatorChar + "main.js"
      && PercorsoLocale(cartella, "/maps/mia%20mappa.json") != null
      && PercorsoLocale(cartella, "/../segreto.txt") == null
      && PercorsoLocale(cartella, "/js/../../segreto.txt") == null
      && PercorsoLocale(cartella, "/%2e%2e/segreto.txt") == null
      && PercorsoLocale(cartella, "/js%2f..%2f..%2fsegreto.txt") == null
      && PercorsoLocale(cartella, "/..%5csegreto.txt") == null
      && PercorsoLocale(cartella, "/C:/Windows/win.ini") == null
      && PercorsoLocale(cartella, "//server/condivisa") == null
      && PercorsoLocale(cartella, "/index.html%00.js") == null
      && PercorsoLocale(cartella, "js/main.js") == null
      && TipoMime("a/b.JS") == "text/javascript; charset=utf-8" && TipoMime("x.json") == "application/json; charset=utf-8"
      && TipoMime("i.html") == "text/html; charset=utf-8" && TipoMime("s.mp3") == "audio/mpeg" && TipoMime("m.jpg") == "image/jpeg"
      && TipoMime("f.svg") == "image/svg+xml" && TipoMime("x.exe") == "application/octet-stream"
      && ConfigGioco("10.147.17.1", 3000, "1.9.0") == "{\"server\":\"10.147.17.1:3000\",\"versione\":\"1.9.0\"}"
      && TESTO_NOTIFICA.Length <= 63
      && AltroLauncher(ConfigGioco("10.147.17.1", 3000, "1.9.0")) && !AltroLauncher(null) && !AltroLauncher("<html>")
      && ChiedeConfig("/launcher.json") && ChiedeConfig("/launcher.json?x=1") && !ChiedeConfig("/js/launcher.json") && !ChiedeConfig(null)
      && HostAmmesso("127.0.0.1:3917", PORTA_LOCALE) && HostAmmesso("localhost:3917", PORTA_LOCALE)
      && !HostAmmesso("evil.example:3917", PORTA_LOCALE) && !HostAmmesso(null, PORTA_LOCALE)
      && AutotestPacchetto();
    if (!pacchetto) Console.WriteLine("autotest del pacchetto fallito");
    bool vero = true;
    if (zipVero != null) {
      // Il pacchetto vero del server (fatto in JavaScript): si estrae e l'impronta torna; con un'altra si scarta
      string base_ = Path.Combine(Path.GetTempPath(), "bs-autotest-" + Guid.NewGuid().ToString("N"));
      string gioco = Path.Combine(base_, "gioco");
      try {
        byte[] zip = File.ReadAllBytes(zipVero);
        vero = InstallaPacchetto(new MemoryStream(zip), improntaVera, gioco) && ImprontaInstallata(gioco) == improntaVera
          && File.Exists(PercorsoLocale(gioco, "/")) && File.Exists(PercorsoLocale(gioco, "/maps/crocevia.json"))
          && !InstallaPacchetto(new MemoryStream(zip), new string('f', 64), gioco + "2") && !Directory.Exists(gioco + "2");
      } finally {
        Cancella(base_);
      }
      Console.WriteLine(vero ? "pacchetto vero ok" : "pacchetto vero FALLITO");
    }
    bool ok = pacchetto && vero && StessaSottorete("10.147.17.42", "10.147.17.1")
      && !StessaSottorete("10.147.18.42", "10.147.17.1")
      && !StessaSottorete("110.147.17.42", "10.147.17.1")
      && HaRisposto(HttpStatusCode.NotFound)
      && !HaRisposto(null)
      && LeggiInvito(" A1B2C3D4E5F6A7B8@10.147.17.1 ", out r, out h, out p) && r == "a1b2c3d4e5f6a7b8" && h == "10.147.17.1" && p == 3000
      && LeggiInvito("a1b2c3d4e5f6a7b8@10.147.17.1:3001", out r, out h, out p) && p == 3001
      && !LeggiInvito("a1b2c3d4e5f6a7b8@010.147.17.1", out r, out h, out p)
      && !LeggiInvito("a1b2c3d4e5f6a7b8@10.147.17.256", out r, out h, out p)
      && !LeggiInvito("a1b2c3d4@10.147.17.1", out r, out h, out p)
      && !LeggiInvito("a1b2c3d4e5f6a7b8@10.147.17.1:70000", out r, out h, out p)
      && !LeggiInvito("a1b2c3d4e5f6a7b8@10.147.17.1 & calc", out r, out h, out p)
      && !ReteValida("a1b2c3d4e5f6a7b8 & calc")
      && ArgomentiEdge("http://10.147.17.1:3000/", @"C:\p").Contains("--start-maximized");
    Console.WriteLine(ok ? "autotest ok" : "autotest FALLITO");
    return ok ? 0 : 1;
  }

  // --- Sistema ---

  public const string SERVIZIO_ZEROTIER = "ZeroTierOneService";

  public static bool ZeroTierAcceso() {
    try {
      using (var s = new ServiceController(SERVIZIO_ZEROTIER)) return s.Status == ServiceControllerStatus.Running;
    } catch (InvalidOperationException) {
      return false; // servizio non installato
    }
  }

  // Il PC dell'host risponde al ping? Windows può bloccare il ping: un no non è sicuro, un sì sì
  public static bool HostRisponde(string ip) {
    try {
      using (var p = new Ping()) return p.Send(ip, 1000).Status == IPStatus.Success;
    } catch (PingException) {
      return false;
    }
  }

  public static string ZeroTierCli() {
    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"ZeroTier\One\zerotier-cli.bat");
  }

  public static bool ServerRisponde(string url) {
    try {
      var r = (HttpWebRequest)WebRequest.Create(url);
      r.Timeout = 1500;
      r.Proxy = null; // l'host è in rete privata, mai dietro un proxy
      using (var risposta = (HttpWebResponse)r.GetResponse()) return HaRisposto(risposta.StatusCode);
    } catch (WebException e) {
      var risposta = e.Response as HttpWebResponse;
      return HaRisposto(risposta != null ? (HttpStatusCode?)risposta.StatusCode : null);
    }
  }

  // Apre il gioco in Edge con il profilo del launcher e restituisce la cartella del profilo (null se Edge non c'è e
  // il gioco si apre nel browser predefinito: allora non si sa quando viene chiuso)
  public static string ApriGioco(string url) {
    string edge = TrovaEdge();
    if (edge == null) {
      Apri(url);
      return null;
    }
    string profilo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BlockStrike\edge");
    Process.Start(edge, ArgomentiEdge(url, profilo));
    return profilo;
  }

  // Edge (Chromium) tiene aperto il file "lockfile" nella cartella del profilo finché gira, e lo cancella alla
  // chiusura: se c'è e non si riesce ad aprirlo, il gioco è ancora aperto
  public static bool ProfiloInUso(string profilo) {
    string f = Path.Combine(profilo, "lockfile");
    if (!File.Exists(f)) return false;
    try {
      using (new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return false;
    } catch (IOException) {
      return true;
    } catch (UnauthorizedAccessException) {
      return true;
    }
  }

  // GET con un tempo massimo; restituisce il corpo (null se l'host non risponde o risponde con un errore)
  public static string ScaricaTesto(string url, int ms) {
    try {
      var r = (HttpWebRequest)WebRequest.Create(url);
      r.Timeout = ms;
      r.Proxy = null;
      using (var risposta = (HttpWebResponse)r.GetResponse())
      using (var lettore = new StreamReader(risposta.GetResponseStream(), Encoding.UTF8)) return lettore.ReadToEnd();
    } catch (WebException) {
      return null;
    } catch (IOException) {
      return null;
    }
  }

  static string TrovaEdge() {
    const string chiave = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe";
    foreach (string radice in new[] { "HKEY_LOCAL_MACHINE\\", "HKEY_CURRENT_USER\\" }) {
      var percorso = Registry.GetValue(radice + chiave, "", null) as string;
      if (percorso != null && File.Exists(percorso)) return percorso;
    }
    string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe");
    return File.Exists(standard) ? standard : null;
  }

  public static int Esegui(string programma, string argomenti, bool nascosto) {
    try {
      var p = Process.Start(new ProcessStartInfo(programma, argomenti) { UseShellExecute = false, CreateNoWindow = nascosto });
      p.WaitForExit();
      return p.ExitCode;
    } catch (Win32Exception) {
      return -1; // programma non trovato (per esempio winget assente)
    }
  }

  public static void Apri(string url) {
    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Win32Exception) { } // nessun browser predefinito
  }
}

// Finestra del launcher (vista 5.1 di docs/IDEE-INTERFACCIA.md): senza bordo di sistema, con la barra del titolo,
// i bottoni e il campo disegnati a mano nei colori del gioco. Logo e versione in alto, al centro il bottone Gioca
// con la barra di attesa, in basso lo stato con il pallino; per l'amico senza configurazione chiede prima il
// codice invito
class Finestra : Form {
  // Token della sezione 3 di docs/IDEE-INTERFACCIA.md
  public static readonly Color S0 = Rgb(0x0b0e12), S2 = Rgb(0x1a2029), S3 = Rgb(0x232b36), T1 = Rgb(0xeef2f6),
    T2 = Rgb(0x9aa6b4), T3 = Rgb(0x7d8896), ACCENTO = Rgb(0xf5a524), ACCENTO_SOPRA = Rgb(0xffb83f), ROSSO = Rgb(0xff4d5e),
    VERDE = Rgb(0x3ddc84), LINEA = Color.FromArgb(20, 255, 255, 255);
  static readonly string CARTELLA = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockStrike");
  static readonly string FILE_INVITO = Path.Combine(CARTELLA, "invito.txt");
  static readonly string CARTELLA_GIOCO = Path.Combine(CARTELLA, "gioco");
  const int BARRA_TITOLO = 36, PIEDE = 276;
  static readonly Rectangle PALLINO = new Rectangle(40, 294, 8, 8);

  public int Codice = 1;
  readonly bool host;
  readonly string anteprima;
  string rete = Config.Rete, ip = Config.Host;
  int porta = Config.Porta;
  volatile int giro; // cambia quando si ricomincia: il giro vecchio si ferma da solo
  ServerLocale locale; // serve la cartella del gioco a Edge finché il gioco è aperto
  NotifyIcon notifica; // icona nell'area di notifica mentre il gioco è aperto (vedi Nascondi)
  int raggio = 14; // angoli della finestra: 8 se li arrotonda Windows 11
  Color pallino = ACCENTO;

  readonly Font piccolo = new Font("Segoe UI", 9f);
  readonly Label stato = new Label();
  readonly Bottone gioca = new Bottone();
  readonly Caricamento barra = new Caricamento();
  readonly Panel pannelloInvito = new Panel();
  readonly TextBox campoInvito = new TextBox();
  readonly Label erroreInvito = new Label();
  readonly Bottone cambiaInvito = new Bottone();
  static readonly Rectangle CAMPO = new Rectangle(0, 50, 330, 40); // riquadro del campo nel pannello dell'invito

  public Finestra(bool host, string anteprima) {
    this.host = host;
    this.anteprima = anteprima;
    Text = "BS-BlockStrike";
    // L'icona è anche una risorsa dell'exe (/resource in crea-launcher.js): così il logo usa la versione a 48 px
    var risorsa = typeof(Finestra).Assembly.GetManifestResourceStream("icona.ico");
    if (risorsa != null) using (risorsa) Icon = new Icon(risorsa);
    FormBorderStyle = FormBorderStyle.None;
    ClientSize = new Size(520, 320);
    StartPosition = FormStartPosition.CenterScreen;
    BackColor = S0;
    ForeColor = T1;
    Font = new Font("Segoe UI", 9.75f);
    DoubleBuffered = true;

    var riduci = BottoneBarra("Riduci", 446, delegate (Graphics g, Pen p) { g.DrawLine(p, 11, 14, 21, 14); });
    riduci.Click += delegate { WindowState = FormWindowState.Minimized; };
    var chiudi = BottoneBarra("Chiudi", 482, delegate (Graphics g, Pen p) { g.DrawLine(p, 11, 9, 21, 19); g.DrawLine(p, 21, 9, 11, 19); });
    chiudi.Click += delegate { Close(); };

    var logo = new PictureBox { Location = new Point(40, 62), Size = new Size(52, 52), SizeMode = PictureBoxSizeMode.Zoom };
    if (Icon != null) logo.Image = new Icon(Icon, 48, 48).ToBitmap();
    var titolo = new Label { Text = "BLOCKSTRIKE", Location = new Point(102, 60), AutoSize = true, Font = new Font("Segoe UI Semibold", 20f) };
    var versione = new Label {
      Text = "Versione " + Config.Versione + " · " + (host ? "partita sul tuo PC" : "partita dell'host"),
      Location = new Point(106, 97), AutoSize = true, ForeColor = T2, Font = piccolo
    };

    gioca.SetBounds(40, 142, 440, 52);
    gioca.Text = "GIOCA";
    gioca.Font = new Font("Segoe UI Semibold", 14f);
    gioca.Click += delegate { Avvia(); }; // attivo solo dopo un errore, come "Riprova"
    barra.SetBounds(40, 204, 440, 3);

    // Codice invito: "<ID rete>@<IP host>", lo manda l'host; un passo a parte, solo la prima volta
    pannelloInvito.SetBounds(40, 134, 440, 136);
    pannelloInvito.Visible = false;
    var titoloInvito = new Label { Text = "Codice invito", Location = new Point(0, 0), AutoSize = true, Font = new Font("Segoe UI Semibold", 11f) };
    var spiega = new Label { Text = "Incolla il codice che ti ha mandato l'host: serve solo la prima volta.", Location = new Point(1, 24), AutoSize = true, ForeColor = T2, Font = piccolo };
    campoInvito.SetBounds(CAMPO.X + 12, CAMPO.Y + 11, CAMPO.Width - 24, 20);
    campoInvito.BorderStyle = BorderStyle.None;
    campoInvito.BackColor = S3;
    campoInvito.ForeColor = T1;
    campoInvito.Font = new Font("Consolas", 11f);
    campoInvito.AccessibleName = "Codice invito";
    campoInvito.GotFocus += delegate { pannelloInvito.Invalidate(); };
    campoInvito.LostFocus += delegate { pannelloInvito.Invalidate(); };
    pannelloInvito.Paint += delegate (object o, PaintEventArgs e) {
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      using (var forma = Arrotondato(CAMPO, 8))
      using (var fondo = new SolidBrush(S3))
      using (var bordo = new Pen(campoInvito.Focused ? ACCENTO : LINEA)) {
        e.Graphics.FillPath(fondo, forma);
        e.Graphics.DrawPath(bordo, forma);
      }
    };
    pannelloInvito.MouseDown += delegate (object o, MouseEventArgs e) { if (CAMPO.Contains(e.Location)) campoInvito.Focus(); };
    var entra = new Bottone { Text = "Entra", Font = new Font("Segoe UI Semibold", 10.5f) };
    entra.SetBounds(342, CAMPO.Y, 98, CAMPO.Height);
    entra.Click += delegate { SalvaInvito(); };
    erroreInvito.SetBounds(0, 96, 440, 40);
    erroreInvito.ForeColor = ROSSO;
    erroreInvito.Font = piccolo;
    pannelloInvito.Controls.AddRange(new Control[] { titoloInvito, spiega, campoInvito, entra, erroreInvito });
    AcceptButton = entra;

    stato.SetBounds(56, PIEDE + 1, 320, Height - PIEDE - 1);
    stato.TextAlign = ContentAlignment.MiddleLeft;
    stato.ForeColor = T2;
    stato.Font = piccolo;
    stato.AutoEllipsis = true;

    cambiaInvito.Text = "Cambia invito";
    cambiaInvito.SetBounds(384, PIEDE + 8, 96, 28);
    cambiaInvito.Font = piccolo;
    cambiaInvito.Sfondo = Color.Empty;
    cambiaInvito.SfondoSopra = S2;
    cambiaInvito.Colore = T2;
    cambiaInvito.Raggio = 4;
    cambiaInvito.Visible = false;
    cambiaInvito.Click += delegate { ChiediInvito(""); };

    Controls.AddRange(new Control[] { riduci, chiudi, logo, titolo, versione, gioca, barra, pannelloInvito, stato, cambiaInvito });
    Shown += delegate { Inizia(); };
    FormClosed += delegate {
      if (locale != null) locale.Ferma();
      if (notifica != null) notifica.Dispose(); // senza, l'icona resta nell'area di notifica fino al passaggio del mouse
    };
  }

  static Color Rgb(int rgb) {
    return Color.FromArgb(255, Color.FromArgb(rgb));
  }

  Bottone BottoneBarra(string nome, int x, Action<Graphics, Pen> icona) {
    var b = new Bottone { AccessibleName = nome, Icona = icona, Sfondo = Color.Empty, SfondoSopra = S2, Colore = T2, Raggio = 4, TabStop = false };
    b.SetBounds(x, 4, 32, 28);
    return b;
  }

  public static GraphicsPath Arrotondato(RectangleF r, float raggio) {
    float d = raggio * 2;
    var p = new GraphicsPath();
    p.AddArc(r.X, r.Y, d, d, 180, 90);
    p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    p.CloseFigure();
    return p;
  }

  // --- Finestra senza bordo ---

  [DllImport("dwmapi.dll")]
  static extern int DwmSetWindowAttribute(IntPtr finestra, int attributo, ref int valore, int dimensione);

  // WS_MINIMIZEBOX: senza, il clic sulla barra delle applicazioni non riduce la finestra
  protected override CreateParams CreateParams {
    get {
      var p = base.CreateParams;
      p.Style |= 0x20000;
      return p;
    }
  }

  // Angoli arrotondati: su Windows 11 li fa il sistema (lisci e con l'ombra), prima si ritaglia la finestra
  protected override void OnHandleCreated(EventArgs e) {
    base.OnHandleCreated(e);
    int tondi = 2; // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND
    if (DwmSetWindowAttribute(Handle, 33, ref tondi, 4) == 0) raggio = 8;
    else using (var forma = Arrotondato(new Rectangle(0, 0, Width, Height), raggio)) Region = new Region(forma);
  }

  // La striscia in alto trascina la finestra come la barra del titolo di sistema
  protected override void WndProc(ref Message m) {
    base.WndProc(ref m);
    const int WM_NCHITTEST = 0x84, HTCLIENT = 1, HTCAPTION = 2;
    if (m.Msg == WM_NCHITTEST && (int)m.Result == HTCLIENT && PointToClient(Cursor.Position).Y < BARRA_TITOLO) m.Result = (IntPtr)HTCAPTION;
  }

  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    TextRenderer.DrawText(g, Text, piccolo, new Rectangle(16, 0, 300, BARRA_TITOLO), T2, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    using (var linea = new Pen(LINEA)) {
      g.DrawLine(linea, 0, PIEDE, Width, PIEDE);
      g.SmoothingMode = SmoothingMode.AntiAlias;
      using (var bordo = Arrotondato(new Rectangle(0, 0, Width - 1, Height - 1), raggio)) g.DrawPath(linea, bordo);
    }
    using (var colore = new SolidBrush(pallino)) g.FillEllipse(colore, PALLINO);
  }

  // Riga in basso: pallino verde (server raggiungibile), ambra (attesa) o rosso (errore)
  void MostraStato(string testo, Color colore) {
    stato.Text = testo;
    pallino = colore;
    Invalidate(Rectangle.Inflate(PALLINO, 1, 1));
  }

  void Inizia() {
    if (anteprima != null) {
      Anteprima();
      return;
    }
    if (host || rete != "") {
      Avvia();
      return;
    }
    string salvato = File.Exists(FILE_INVITO) ? File.ReadAllText(FILE_INVITO) : "";
    if (Launcher.LeggiInvito(salvato, out rete, out ip, out porta)) {
      cambiaInvito.Visible = true;
      Avvia();
    } else ChiediInvito("");
  }

  void Anteprima() {
    var t = new System.Windows.Forms.Timer { Interval = 15000 };
    t.Tick += delegate { Close(); };
    t.Start();
    if (anteprima == "invito") {
      ChiediInvito("Codice non valido: deve essere come a1b2c3d4e5f6a7b8@10.147.17.1 (chiedilo all'host).");
      return;
    }
    cambiaInvito.Visible = !host;
    int questo = Attesa();
    if (anteprima == "errore") Errore(questo, "Serve il permesso di amministratore, una volta sola, per entrare nella rete. Premi Riprova e accetta.");
    else Stato(questo, host ? "Attendo che il server sia pronto..." : "In attesa che l'host ti approvi nella rete ZeroTier...");
  }

  void ChiediInvito(string errore) {
    giro++;
    gioca.Visible = barra.Visible = cambiaInvito.Visible = false;
    pannelloInvito.Visible = true;
    erroreInvito.Text = errore;
    MostraStato("Serve il codice invito dell'host", ACCENTO);
    campoInvito.Focus();
  }

  void SalvaInvito() {
    string r, h;
    int p;
    if (!Launcher.LeggiInvito(campoInvito.Text, out r, out h, out p)) {
      erroreInvito.Text = "Codice non valido: deve essere come a1b2c3d4e5f6a7b8@10.147.17.1 (chiedilo all'host).";
      return;
    }
    Directory.CreateDirectory(CARTELLA);
    File.WriteAllText(FILE_INVITO, campoInvito.Text.Trim());
    rete = r;
    ip = h;
    porta = p;
    cambiaInvito.Visible = true;
    Avvia();
  }

  // Bottone in attesa (tenue) con la barra che scorre; restituisce il nuovo giro
  int Attesa() {
    pannelloInvito.Visible = false;
    barra.Progresso = -1;
    gioca.Visible = barra.Visible = true;
    gioca.Text = "GIOCA";
    gioca.Enabled = false;
    MostraStato("Avvio...", ACCENTO);
    return ++giro;
  }

  void Avvia() {
    int questo = Attesa();
    var t = new Thread(delegate () {
      try {
        if (host) Host(questo); else Amico(questo);
      } catch (Exception e) {
        Errore(questo, "Errore imprevisto: " + e.Message);
      }
    });
    t.IsBackground = true;
    t.Start();
  }

  // --- Dal thread del collegamento ---

  bool Attivo(int questo) {
    return questo == giro && !IsDisposed;
  }

  void Interfaccia(Action a) {
    try { BeginInvoke(a); } catch (InvalidOperationException) { } // finestra già chiusa
  }

  void Stato(int questo, string testo) {
    Interfaccia(delegate { if (Attivo(questo)) MostraStato(testo, ACCENTO); });
  }

  void Errore(int questo, string testo) {
    Interfaccia(delegate {
      if (!Attivo(questo)) return;
      MostraStato(testo, ROSSO);
      barra.Visible = false;
      gioca.Text = "RIPROVA";
      gioca.Enabled = true;
      gioca.Focus();
    });
  }

  // Server raggiungibile (dal thread del collegamento): aggiorna il pacchetto del gioco se l'host ne ha uno diverso,
  // lo serve da 127.0.0.1 e apre Edge. `server` è l'IP dell'host (127.0.0.1 per l'host stesso)
  void Gioca(int questo, string server) {
    string url = Indirizzo(server);
    if (locale == null) {
      try {
        locale = new ServerLocale(CARTELLA_GIOCO, Launcher.PORTA_LOCALE);
      } catch (SocketException) {
        // Un launcher già aperto (anche nascosto nell'area di notifica) risponde con la sua /launcher.json
        string altro = Launcher.ScaricaTesto("http://127.0.0.1:" + Launcher.PORTA_LOCALE + Launcher.FILE_LAUNCHER, 2000);
        Errore(questo, Launcher.AltroLauncher(altro)
          ? "BlockStrike è già aperto: cerca la sua icona vicino all'orologio (doppio clic per vederlo, tasto destro → Chiudi), poi premi Riprova."
          : "La porta " + Launcher.PORTA_LOCALE + " è occupata da un altro programma: chiudilo e premi Riprova.");
        return;
      }
    }
    for (int tentativo = 0; ; tentativo++) {
      if (!Attivo(questo)) return;
      Stato(questo, "Controllo la versione del gioco dell'host...");
      string versione, impronta;
      if (!Launcher.LeggiVersione(Launcher.ScaricaTesto(url + "versione", 5000), out versione, out impronta)) {
        Errore(questo, "L'host ha un gioco più vecchio di questo launcher (o non risponde): chiedigli di aggiornarlo, poi premi Riprova.");
        return;
      }
      if (Launcher.ImprontaInstallata(CARTELLA_GIOCO) == impronta) break;
      Stato(questo, "Scarico il gioco " + versione + " dall'host...");
      byte[] zip = ScaricaPacchetto(questo, url + "pacchetto");
      if (zip == null) {
        if (Attivo(questo)) Errore(questo, "Scaricamento del gioco interrotto: controlla la connessione con l'host e premi Riprova.");
        return;
      }
      Stato(questo, "Controllo i file del gioco...");
      Progresso(questo, -1);
      bool installato;
      try {
        installato = Launcher.InstallaPacchetto(new MemoryStream(zip), impronta, CARTELLA_GIOCO);
      } catch (Exception e) {
        if (!(e is IOException) && !(e is UnauthorizedAccessException)) throw;
        Errore(questo, "Non riesco a scrivere i file del gioco in " + CARTELLA_GIOCO + " (" + e.Message + "). Chiudi il gioco se è aperto e premi Riprova.");
        return;
      }
      if (installato) break;
      // Pacchetto diverso da quello annunciato: l'host l'ha cambiato durante lo scaricamento, si riprova da capo
      if (tentativo == 2) {
        Errore(questo, "Il gioco scaricato non corrisponde a quello dell'host: premi Riprova.");
        return;
      }
    }
    Interfaccia(delegate {
      if (!Attivo(questo)) return;
      MostraStato("Apro il gioco...", VERDE);
      barra.Visible = false;
      Update();
      // L'host e la versione del launcher li legge il gioco da /launcher.json (il server rifiuta i launcher vecchi)
      locale.Configurazione = Launcher.ConfigGioco(server, porta, Config.Versione);
      string profilo = Launcher.ApriGioco("http://127.0.0.1:" + Launcher.PORTA_LOCALE + "/");
      Codice = 0;
      gioca.Text = "GIOCO APERTO";
      Nascondi(profilo == null);
      if (profilo != null) AspettaChiusura(questo, profilo);
    });
  }

  // Scarica /pacchetto con la barra di avanzamento; null se si interrompe (o se si è ricominciato nel frattempo)
  byte[] ScaricaPacchetto(int questo, string url) {
    try {
      var r = (HttpWebRequest)WebRequest.Create(url);
      r.Timeout = 15000;
      r.ReadWriteTimeout = 15000;
      r.Proxy = null;
      using (var risposta = (HttpWebResponse)r.GetResponse())
      using (var flusso = risposta.GetResponseStream())
      using (var dati = new MemoryStream()) {
        long totale = risposta.ContentLength;
        if (totale > Launcher.PACCHETTO_MAX) return null;
        var buffer = new byte[65536];
        int letti;
        while ((letti = flusso.Read(buffer, 0, buffer.Length)) > 0) {
          if (!Attivo(questo) || dati.Length + letti > Launcher.PACCHETTO_MAX) return null;
          dati.Write(buffer, 0, letti);
          if (totale > 0) Progresso(questo, (float)dati.Length / totale);
        }
        // Lo zip troncato lo scarta comunque l'impronta, ma così l'errore è quello giusto
        return totale < 0 || dati.Length == totale ? dati.ToArray() : null;
      }
    } catch (WebException) {
      return null;
    } catch (IOException) {
      return null;
    }
  }

  void Progresso(int questo, float frazione) {
    Interfaccia(delegate {
      if (!Attivo(questo)) return;
      barra.Progresso = frazione;
      barra.Invalidate();
    });
  }

  // Gioco aperto: la finestra sparisce del tutto (né finestra né barra delle applicazioni) e resta solo l'icona
  // nell'area di notifica, con «Chiudi» per fermare il launcher a mano. Il launcher continua a servire il gioco e
  // si chiude da solo quando Edge si chiude (AspettaChiusura). senzaEdge: aperto nel browser predefinito, che non si
  // sa quando viene chiuso: un fumetto ricorda di chiudere il launcher da lì
  void Nascondi(bool senzaEdge) {
    var menu = new ContextMenuStrip();
    menu.Items.Add("Chiudi", null, delegate { Close(); });
    notifica = new NotifyIcon {
      Icon = Icon ?? SystemIcons.Application,
      Text = Launcher.TESTO_NOTIFICA,
      ContextMenuStrip = menu,
      Visible = true
    };
    notifica.DoubleClick += delegate { Mostra(); };
    ShowInTaskbar = false;
    Hide();
    if (senzaEdge) Promemoria();
  }

  // Doppio clic sull'icona: la finestra torna visibile (e nella barra delle applicazioni), per vedere lo stato
  void Mostra() {
    ShowInTaskbar = true;
    Show();
    WindowState = FormWindowState.Normal;
    Activate();
  }

  void Promemoria() {
    if (notifica != null) notifica.ShowBalloonTip(8000, "BlockStrike", MESSAGGIO_PROMEMORIA, ToolTipIcon.Info);
  }

  const string MESSAGGIO_PROMEMORIA = "BlockStrike resta acceso per il gioco: quando hai finito chiudilo da questa icona (tasto destro → Chiudi).";

  // Finché Edge tiene aperto il profilo del gioco il launcher resta (nascosto) e serve i file; poi si chiude
  void AspettaChiusura(int questo, string profilo) {
    var t = new Thread(delegate () {
      var tempo = Stopwatch.StartNew();
      bool visto = false;
      while (Attivo(questo)) {
        Thread.Sleep(2000);
        bool aperto = Launcher.ProfiloInUso(profilo);
        visto = visto || aperto;
        // Se Edge non si fa vedere entro un minuto (aperto in un altro modo) si resta: lo chiude chi gioca
        if (visto && !aperto) {
          Interfaccia(delegate { if (Attivo(questo)) Close(); });
          return;
        }
        // Edge non rilevato in un minuto (profilo non trovato o gioco già chiuso): non si sa quando chiudere, quindi
        // si ricorda di chiudere dall'icona invece di restare nascosti senza dire niente
        if (!visto && tempo.Elapsed.TotalSeconds > 60) {
          Interfaccia(delegate {
            if (!Attivo(questo)) return;
            MostraStato("Gioco aperto: chiudi BlockStrike quando hai finito di giocare.", VERDE);
            Promemoria();
          });
          return;
        }
      }
    });
    t.IsBackground = true;
    t.Start();
  }

  string Indirizzo(string server) {
    return "http://" + server + ":" + porta + "/";
  }

  void Host(int questo) {
    // 127.0.0.1 e non localhost: .NET prova prima ::1, dove il server non ascolta, e resta appeso
    string url = Indirizzo("127.0.0.1");
    Stato(questo, "Attendo che il server sia pronto...");
    var tempo = Stopwatch.StartNew();
    while (tempo.Elapsed.TotalSeconds < 30 && Attivo(questo)) {
      if (Launcher.ServerRisponde(url)) {
        Gioca(questo, "127.0.0.1");
        return;
      }
      Thread.Sleep(1000);
    }
    Errore(questo, "Il server non risponde: guarda la finestra del server per l'errore.");
  }

  void Amico(int questo) {
    if (!File.Exists(Launcher.ZeroTierCli())) {
      Stato(questo, "Installo ZeroTier, la rete privata dell'host (Windows potrebbe chiedere il permesso)...");
      Launcher.Esegui("winget", "install -e --id ZeroTier.ZeroTierOne --accept-source-agreements --accept-package-agreements", true);
      if (!File.Exists(Launcher.ZeroTierCli())) {
        Launcher.Apri("https://www.zerotier.com/download/");
        Errore(questo, "Installazione di ZeroTier non riuscita: installalo dalla pagina che si è aperta, poi premi Riprova.");
        return;
      }
    }
    if (!NellaRete()) {
      Stato(questo, "Entro nella rete dell'host (solo la prima volta Windows chiede il permesso)...");
      int codice;
      try {
        var p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--entra " + rete) { Verb = "runas", UseShellExecute = true });
        p.WaitForExit();
        codice = p.ExitCode;
      } catch (Win32Exception e) {
        if (e.NativeErrorCode != Launcher.ANNULLATO_DA_UTENTE) throw;
        Errore(questo, "Serve il permesso di amministratore, una volta sola, per entrare nella rete. Premi Riprova e accetta.");
        return;
      }
      if (codice != 0) {
        Errore(questo, "Non sono riuscito a entrare nella rete ZeroTier (codice " + codice + "). Controlla il codice invito.");
        return;
      }
    }
    string url = Indirizzo(ip);
    var nellaRete = new Stopwatch();
    while (Attivo(questo)) {
      if (Launcher.ServerRisponde(url)) {
        Gioca(questo, ip);
        return;
      }
      if (!Launcher.ZeroTierAcceso() && !AvviaZeroTier(questo)) return;
      if (!NellaRete()) {
        nellaRete.Reset();
        Stato(questo, "In attesa che l'host ti approvi nella rete ZeroTier...");
      } else {
        nellaRete.Start();
        Stato(questo, Launcher.HostRisponde(ip)
          ? "Il PC dell'host risponde, ma il gioco no: il server è spento o la porta " + porta + " è bloccata. Avvisa l'host. Riprovo..."
          : nellaRete.Elapsed.TotalSeconds < 30
          ? "Sei nella rete, cerco il PC dell'host..."
          : "Il PC dell'host non risponde. Controlla che l'host abbia acceso il server; se è acceso, riavvia il PC, spegni le VPN e il firewall dell'antivirus (McAfee, Norton...) o prova un'altra connessione. Riprovo...");
      }
      Thread.Sleep(2000);
    }
  }

  // Servizio di ZeroTier spento: lo avvia con i permessi di amministratore (Windows chiede il permesso)
  bool AvviaZeroTier(int questo) {
    Stato(questo, "ZeroTier è spento: lo avvio (Windows potrebbe chiedere il permesso)...");
    try {
      Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--avvia-zerotier") { Verb = "runas", UseShellExecute = true }).WaitForExit();
    } catch (Win32Exception e) {
      if (e.NativeErrorCode != Launcher.ANNULLATO_DA_UTENTE) throw;
    }
    if (Launcher.ZeroTierAcceso()) return true;
    Errore(questo, "ZeroTier non parte. Spesso è l'antivirus (McAfee, Norton...) che lo blocca: ripristinalo dalla quarantena o aggiungi un'eccezione, poi riavvia il PC e premi Riprova.");
    return false;
  }

  bool NellaRete() {
    foreach (var scheda in NetworkInterface.GetAllNetworkInterfaces()) {
      if (scheda.OperationalStatus != OperationalStatus.Up) continue;
      foreach (var ind in scheda.GetIPProperties().UnicastAddresses) {
        if (ind.Address.AddressFamily == AddressFamily.InterNetwork && Launcher.StessaSottorete(ind.Address.ToString(), ip)) return true;
      }
    }
    return false;
  }
}

// Barra di caricamento: indeterminata (un tratto che scorre su una linea sottile, a tinta unita) o, con
// Progresso tra 0 e 1, piena fino a quel punto (scaricamento del gioco)
class Caricamento : Control {
  readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 16 };
  float pos;
  public float Progresso = -1; // < 0: indeterminata

  public Caricamento() {
    DoubleBuffered = true;
    timer.Tick += delegate { pos = (pos + 0.012f) % 1.4f; Invalidate(); };
    timer.Start();
  }

  protected override void OnVisibleChanged(EventArgs e) {
    timer.Enabled = Visible; // nascosta non ridisegna
    base.OnVisibleChanged(e);
  }

  protected override void OnPaint(PaintEventArgs e) {
    e.Graphics.Clear(Finestra.S2);
    int largo = Width / 3, x = (int)((pos - 0.4f) * Width);
    if (Progresso >= 0) {
      x = 0;
      largo = (int)(Math.Min(1f, Progresso) * Width);
    }
    using (var pennello = new SolidBrush(Finestra.ACCENTO)) e.Graphics.FillRectangle(pennello, x, 0, largo, Height);
  }

  protected override void Dispose(bool disposing) {
    if (disposing) timer.Dispose();
    base.Dispose(disposing);
  }
}

// Server HTTP minimo che serve la cartella del gioco solo su 127.0.0.1 (TcpListener: niente permessi di
// amministratore né richiesta del firewall, a differenza di HttpListener). Solo GET e HEAD, un file per connessione,
// percorsi controllati da Launcher.PercorsoLocale e Host controllato da Launcher.HostAmmesso
class ServerLocale {
  readonly TcpListener ascolto;
  readonly string cartella;
  readonly int porta;
  // Contenuto di /launcher.json (Launcher.ConfigGioco), deciso quando si sa a quale host collegarsi
  public volatile string Configurazione;

  public ServerLocale(string cartella, int porta) {
    this.cartella = cartella;
    this.porta = porta;
    ascolto = new TcpListener(IPAddress.Loopback, porta);
    ascolto.Start(); // SocketException se la porta è occupata
    var t = new Thread(Ciclo);
    t.IsBackground = true;
    t.Start();
  }

  public void Ferma() {
    try { ascolto.Stop(); } catch (SocketException) { }
  }

  void Ciclo() {
    while (true) {
      TcpClient c;
      try {
        c = ascolto.AcceptTcpClient();
      } catch (SocketException) {
        return; // fermato
      } catch (ObjectDisposedException) {
        return;
      }
      ThreadPool.QueueUserWorkItem(delegate { Servi(c); });
    }
  }

  void Servi(TcpClient c) {
    try {
      using (c) {
        c.ReceiveTimeout = 10000;
        c.SendTimeout = 10000;
        var flusso = c.GetStream();
        string richiesta = LeggiIntestazioni(new BufferedStream(flusso, 4096)); // GET e HEAD non hanno corpo
        if (richiesta == null) return;
        string[] righe = richiesta.Split(new[] { "\r\n" }, StringSplitOptions.None);
        string[] prima = righe[0].Split(' ');
        string host = null;
        for (int i = 1; i < righe.Length; i++) {
          if (righe[i].StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) host = righe[i].Substring(5).Trim();
        }
        if (prima.Length != 3 || (prima[0] != "GET" && prima[0] != "HEAD")) {
          Rispondi(flusso, "405 Method Not Allowed", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Metodo non ammesso"), false);
          return;
        }
        bool testa = prima[0] == "HEAD";
        if (!Launcher.HostAmmesso(host, porta)) {
          Rispondi(flusso, "403 Forbidden", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Vietato"), testa);
          return;
        }
        string config = Configurazione;
        if (Launcher.ChiedeConfig(prima[1]) && config != null) {
          Rispondi(flusso, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(config), testa);
          return;
        }
        string file = Launcher.PercorsoLocale(cartella, prima[1]);
        if (file == null || !File.Exists(file)) {
          Rispondi(flusso, "404 Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Non trovato"), testa);
          return;
        }
        Rispondi(flusso, "200 OK", Launcher.TipoMime(file), File.ReadAllBytes(file), testa);
      }
    } catch (IOException) {
    } catch (SocketException) {
    } catch (ObjectDisposedException) {
    } catch (UnauthorizedAccessException) {
    }
  }

  // Intestazioni della richiesta (fino alla riga vuota), al massimo 16 KB; null se la connessione si chiude prima
  static string LeggiIntestazioni(Stream flusso) {
    var dati = new MemoryStream();
    int b, fine = 0;
    while (dati.Length < 16384 && (b = flusso.ReadByte()) >= 0) {
      dati.WriteByte((byte)b);
      fine = (b == '\r' && (fine == 0 || fine == 2)) || (b == '\n' && (fine == 1 || fine == 3)) ? fine + 1 : 0;
      if (fine == 4) return Encoding.ASCII.GetString(dati.ToArray(), 0, (int)dati.Length - 4);
    }
    return null;
  }

  // Sempre riconvalidato (no-store): quando il pacchetto cambia Edge non usa file vecchi dalla sua cache
  static void Rispondi(Stream flusso, string stato, string tipo, byte[] corpo, bool soloIntestazioni) {
    string intestazioni = "HTTP/1.1 " + stato + "\r\nContent-Type: " + tipo + "\r\nContent-Length: " + corpo.Length
      + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
    byte[] testa = Encoding.ASCII.GetBytes(intestazioni);
    flusso.Write(testa, 0, testa.Length);
    if (!soloIntestazioni) flusso.Write(corpo, 0, corpo.Length);
    flusso.Flush();
  }
}

// Bottone disegnato a mano: rettangolo arrotondato pieno (principale: ambra con il testo scuro) o trasparente con
// lo sfondo solo al passaggio del mouse; disattivato è tenue. Resta un Button per tastiera, Invio e lettori di schermo
class Bottone : Button {
  public Color Sfondo = Finestra.ACCENTO, SfondoSopra = Finestra.ACCENTO_SOPRA, Colore = Finestra.S0;
  public int Raggio = 8;
  public Action<Graphics, Pen> Icona; // disegnata al posto del testo, in coordinate del bottone
  bool sopra;

  public Bottone() {
    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    FlatStyle = FlatStyle.Flat;
    Cursor = Cursors.Hand;
  }

  protected override void OnMouseEnter(EventArgs e) {
    sopra = true;
    Invalidate();
    base.OnMouseEnter(e);
  }

  protected override void OnMouseLeave(EventArgs e) {
    sopra = false;
    Invalidate();
    base.OnMouseLeave(e);
  }

  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    g.Clear(Parent != null ? Parent.BackColor : Finestra.S0);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    Color sfondo = !Enabled ? Finestra.S2 : sopra ? SfondoSopra : Sfondo, colore = Enabled ? Colore : Finestra.T3;
    var r = new Rectangle(0, 0, Width - 1, Height - 1);
    if (sfondo.A > 0) using (var forma = Finestra.Arrotondato(r, Raggio)) using (var pennello = new SolidBrush(sfondo)) g.FillPath(pennello, forma);
    if (Focused && ShowFocusCues) using (var forma = Finestra.Arrotondato(Rectangle.Inflate(r, -2, -2), Raggio - 2)) using (var anello = new Pen(Finestra.T1)) g.DrawPath(anello, forma);
    if (Icona != null) using (var tratto = new Pen(colore, 1.5f)) Icona(g, tratto);
    else TextRenderer.DrawText(g, Text, Font, ClientRectangle, colore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
  }
}
