// Launcher di BS-BlockStrike per Windows (vedi docs/GUIDA-HOST.md).
//   nessun argomento → amico: entra nella rete ZeroTier dell'host, aspetta il server, apre il gioco
//   --locale         → host: aspetta il server su localhost e apre il gioco
//   --entra <rete>   → passo con i permessi di amministratore: zerotier-cli join
//   --autotest       → verifica le funzioni pure (usato da test/launcher.test.js)
//   --anteprima <attesa|invito|errore> [--locale] → solo l'aspetto della finestra, senza rete né file
//                    (per gli screenshot); si chiude da sola dopo 15 secondi
// Scritto in C# 5 per compilare con il csc.exe di .NET Framework 4 incluso in Windows.
// La classe Config (ID rete, IP host, porta) è generata da strumenti/crea-launcher.js. Con la config
// vuota (il launcher pubblico) l'amico incolla il codice invito dell'host, salvato in
// %LOCALAPPDATA%\BlockStrike\invito.txt: così l'exe pubblico non contiene niente della rete dell'host.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.ServiceProcess;
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
  const int BARRA_TITOLO = 36, PIEDE = 276;
  static readonly Rectangle PALLINO = new Rectangle(40, 294, 8, 8);

  public int Codice = 1;
  readonly bool host;
  readonly string anteprima;
  string rete = Config.Rete, ip = Config.Host;
  int porta = Config.Porta;
  volatile int giro; // cambia quando si ricomincia: il giro vecchio si ferma da solo
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

  void Gioca(int questo, string url) {
    Interfaccia(delegate {
      if (!Attivo(questo)) return;
      MostraStato("Server raggiungibile: apro il gioco...", VERDE);
      Update();
      // La versione del launcher: se è vecchia il gioco avvisa di scaricare quella nuova
      Launcher.ApriGioco(url + "?launcher=" + Config.Versione);
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
        Gioca(questo, url);
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

// Barra di caricamento indeterminata: un tratto che scorre su una linea sottile, a tinta unita
class Caricamento : Control {
  readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 16 };
  float pos;

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
    using (var pennello = new SolidBrush(Finestra.ACCENTO)) e.Graphics.FillRectangle(pennello, x, 0, largo, Height);
  }

  protected override void Dispose(bool disposing) {
    if (disposing) timer.Dispose();
    base.Dispose(disposing);
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
