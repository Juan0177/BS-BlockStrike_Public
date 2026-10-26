// Launcher di BS-BlockStrike per Windows (vedi docs/GUIDA-HOST.md).
//   nessun argomento → amico: entra nella rete ZeroTier dell'host, aspetta il server, apre il gioco
//   --locale         → host: aspetta il server su localhost e apre il gioco
//   --entra <rete>   → passo con i permessi di amministratore: zerotier-cli join
//   --autotest       → verifica le funzioni pure (usato da test/launcher.test.js)
// Scritto in C# 5 per compilare con il csc.exe di .NET Framework 4 incluso in Windows.
// La classe Config (ID rete, IP host, porta) è generata da strumenti/crea-launcher.js. Con la config
// vuota (il launcher pubblico) l'amico incolla il codice invito dell'host, salvato in
// %LOCALAPPDATA%\BlockStrike\invito.txt: così l'exe pubblico non contiene niente della rete dell'host.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Launcher {
  public const int ANNULLATO_DA_UTENTE = 1223; // l'utente ha rifiutato la richiesta di Windows (UAC)

  [STAThread]
  static int Main(string[] args) {
    string modo = args.Length > 0 ? args[0] : "";
    if (modo == "--autotest") return Autotest();
    if (modo == "--entra") return Entra(args.Length > 1 ? args[1] : "");
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    var finestra = new Finestra(modo == "--locale");
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

  static int Autotest() {
    string r, h;
    int p;
    bool ok = StessaSottorete("10.147.17.42", "10.147.17.1")
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

  public static void ApriGioco(string url) {
    string edge = TrovaEdge();
    if (edge == null) {
      Apri(url);
      return;
    }
    string profilo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BlockStrike\edge");
    Process.Start(edge, ArgomentiEdge(url, profilo));
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

// Finestra del launcher: icona, stato del collegamento e barra di caricamento; per l'amico senza
// configurazione chiede prima il codice invito
class Finestra : Form {
  static readonly Color SFONDO = Color.FromArgb(23, 25, 30), TESTO = Color.FromArgb(236, 237, 239),
    TENUE = Color.FromArgb(150, 156, 165), ACCENTO = Color.FromArgb(232, 134, 42), CAMPO = Color.FromArgb(37, 40, 47);
  static readonly string CARTELLA = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockStrike");
  static readonly string FILE_INVITO = Path.Combine(CARTELLA, "invito.txt");

  public int Codice = 1;
  readonly bool host;
  string rete = Config.Rete, ip = Config.Host;
  int porta = Config.Porta;
  volatile int giro; // cambia quando si ricomincia: il giro vecchio si ferma da solo

  readonly Label stato = new Label();
  readonly Caricamento barra = new Caricamento();
  readonly Panel pannelloInvito = new Panel();
  readonly TextBox campoInvito = new TextBox();
  readonly Label erroreInvito = new Label();
  readonly LinkLabel cambiaInvito = new LinkLabel();
  readonly Button chiudi = new Button();

  public Finestra(bool host) {
    this.host = host;
    Text = "BS-BlockStrike";
    // L'icona è anche una risorsa dell'exe (/resource in crea-launcher.js): così il logo usa la versione a 48 px
    var risorsa = typeof(Finestra).Assembly.GetManifestResourceStream("icona.ico");
    if (risorsa != null) using (risorsa) Icon = new Icon(risorsa);
    ClientSize = new Size(460, 250);
    FormBorderStyle = FormBorderStyle.FixedSingle;
    MaximizeBox = false;
    StartPosition = FormStartPosition.CenterScreen;
    BackColor = SFONDO;
    ForeColor = TESTO;
    Font = new Font("Segoe UI", 9.75f);

    var logo = new PictureBox { Location = new Point(28, 26), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom };
    if (Icon != null) logo.Image = new Icon(Icon, 48, 48).ToBitmap();
    var titolo = new Label { Text = "BS-BlockStrike", Location = new Point(88, 26), AutoSize = true, Font = new Font("Segoe UI Semibold", 16f) };
    var sottotitolo = new Label { Text = host ? "Avvio della partita sul tuo PC" : "Collegamento alla partita dell'host", Location = new Point(90, 56), AutoSize = true, ForeColor = TENUE };

    stato.SetBounds(28, 100, 404, 44);
    barra.SetBounds(28, 152, 404, 4);

    // Codice invito: "<ID rete>@<IP host>", lo manda l'host
    pannelloInvito.SetBounds(28, 96, 404, 110);
    pannelloInvito.Visible = false;
    var spiega = new Label { Text = "Incolla il codice invito che ti ha mandato l'host:", Location = new Point(0, 4), AutoSize = true };
    campoInvito.SetBounds(0, 32, 300, 26);
    campoInvito.BackColor = CAMPO;
    campoInvito.ForeColor = TESTO;
    campoInvito.BorderStyle = BorderStyle.FixedSingle;
    campoInvito.Font = new Font("Consolas", 10.5f);
    var entra = Pulsante("Entra", true);
    entra.SetBounds(310, 31, 94, 28);
    entra.Click += delegate { SalvaInvito(); };
    erroreInvito.SetBounds(0, 66, 404, 40);
    erroreInvito.ForeColor = ACCENTO;
    pannelloInvito.Controls.AddRange(new Control[] { spiega, campoInvito, entra, erroreInvito });
    AcceptButton = entra;

    cambiaInvito.Text = "Cambia codice invito";
    cambiaInvito.SetBounds(28, 214, 200, 22);
    cambiaInvito.LinkColor = cambiaInvito.ActiveLinkColor = TENUE;
    cambiaInvito.Visible = false;
    cambiaInvito.LinkClicked += delegate { ChiediInvito(""); };

    chiudi.Text = "Chiudi";
    chiudi.SetBounds(338, 206, 94, 30);
    StilePulsante(chiudi, false);
    chiudi.Visible = false;
    chiudi.Click += delegate { Close(); };

    Controls.AddRange(new Control[] { logo, titolo, sottotitolo, stato, barra, pannelloInvito, cambiaInvito, chiudi });
    Shown += delegate { Inizia(); };
  }

  static Button Pulsante(string testo, bool principale) {
    var b = new Button { Text = testo };
    StilePulsante(b, principale);
    return b;
  }

  static void StilePulsante(Button b, bool principale) {
    b.FlatStyle = FlatStyle.Flat;
    b.FlatAppearance.BorderColor = principale ? ACCENTO : TENUE;
    b.BackColor = principale ? ACCENTO : SFONDO;
    b.ForeColor = principale ? SFONDO : TESTO;
    b.Cursor = Cursors.Hand;
  }

  void Inizia() {
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

  void ChiediInvito(string errore) {
    giro++;
    barra.Visible = stato.Visible = cambiaInvito.Visible = chiudi.Visible = false;
    pannelloInvito.Visible = true;
    erroreInvito.Text = errore;
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
    pannelloInvito.Visible = false;
    cambiaInvito.Visible = true;
    Avvia();
  }

  void Avvia() {
    int questo = ++giro;
    stato.Visible = barra.Visible = true;
    chiudi.Visible = false;
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
    Interfaccia(delegate { if (Attivo(questo)) stato.Text = testo; });
  }

  void Errore(int questo, string testo) {
    Interfaccia(delegate {
      if (!Attivo(questo)) return;
      stato.Text = testo;
      barra.Visible = false;
      chiudi.Visible = true;
    });
  }

  void Gioca(int questo, string url) {
    Interfaccia(delegate {
      if (!Attivo(questo)) return;
      stato.Text = "Apro il gioco...";
      Launcher.ApriGioco(url);
      Codice = 0;
      Close();
    });
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
        Gioca(questo, url);
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
        Errore(questo, "Installazione di ZeroTier non riuscita: installalo dalla pagina che si è aperta, poi riapri il launcher.");
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
        Errore(questo, "Serve il permesso di amministratore, una volta sola, per entrare nella rete. Riapri il launcher e accetta.");
        return;
      }
      if (codice != 0) {
        Errore(questo, "Non sono riuscito a entrare nella rete ZeroTier (codice " + codice + "). Controlla il codice invito.");
        return;
      }
    }
    string url = Indirizzo(ip);
    while (Attivo(questo)) {
      if (Launcher.ServerRisponde(url)) {
        Gioca(questo, url);
        return;
      }
      Stato(questo, NellaRete()
        ? "Host raggiungibile, ma il server è spento o la porta " + porta + " è bloccata dal firewall. Riprovo..."
        : "In attesa che l'host ti approvi nella rete ZeroTier...");
      Thread.Sleep(2000);
    }
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

// Barra di caricamento indeterminata: un tratto che scorre su una linea, a tinta unita
class Caricamento : Control {
  readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 16 };
  float pos;

  public Caricamento() {
    DoubleBuffered = true;
    timer.Tick += delegate { pos = (pos + 0.012f) % 1.4f; Invalidate(); };
    timer.Start();
  }

  protected override void OnPaint(PaintEventArgs e) {
    e.Graphics.Clear(Color.FromArgb(44, 47, 55));
    int largo = Width / 3, x = (int)((pos - 0.4f) * Width);
    using (var pennello = new SolidBrush(Color.FromArgb(232, 134, 42))) e.Graphics.FillRectangle(pennello, x, 0, largo, Height);
  }

  protected override void Dispose(bool disposing) {
    if (disposing) timer.Dispose();
    base.Dispose(disposing);
  }
}
