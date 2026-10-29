// Sito di BS-BlockStrike: legge dati.json (generato da strumenti/crea-sito.js) e disegna le sezioni.

const $ = (s) => document.querySelector(s);
const el = (tag, attr = {}, ...figli) => {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attr)) {
    if (k === 'testo') e.textContent = v;
    else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
    else if (v !== false && v != null) e.setAttribute(k, v);
  }
  e.append(...figli.flat().filter((f) => f != null));
  return e;
};
const SVG = 'http://www.w3.org/2000/svg';
const svg = (tag, attr = {}) => {
  const e = document.createElementNS(SVG, tag);
  for (const [k, v] of Object.entries(attr)) e.setAttribute(k, v);
  return e;
};
const num = (n, dec = 0) => n.toLocaleString('it-IT', { minimumFractionDigits: dec, maximumFractionDigits: dec });
const secondi = (ms) => `${num(ms / 1000, 2)} s`;
const molt = (m) => `×${num(m, Math.round(m * 100) % 10 ? 2 : Math.round(m * 100) % 100 ? 1 : 0)}`;
const colpi = (n) => `${n} ${n === 1 ? 'colpo' : 'colpi'}`;
// Uccisione: "1 colpo" se basta un colpo, altrimenti tempo (e colpi se richiesti)
const esito = (u, conColpi = true) => (u.colpi === 1 ? '1 colpo' : conColpi ? `${colpi(u.colpi)} · ${secondi(u.tempo)}` : secondi(u.tempo));

// --- Tema chiaro / scuro: segue il sistema finché non si sceglie con il pulsante ---
const ICONE = {
  luna: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg>',
  sole: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>',
};
const sistemaScuro = matchMedia('(prefers-color-scheme: dark)');
const temaAttuale = () => document.documentElement.dataset.theme ?? (sistemaScuro.matches ? 'dark' : 'light');
function iconaTema() {
  const scuro = temaAttuale() === 'dark';
  $('#tema').innerHTML = scuro ? ICONE.sole : ICONE.luna;
  $('#tema').setAttribute('aria-label', scuro ? 'Tema chiaro' : 'Tema scuro');
}
$('#tema').addEventListener('click', () => {
  const nuovo = temaAttuale() === 'dark' ? 'light' : 'dark';
  document.documentElement.dataset.theme = nuovo;
  try { localStorage.setItem('bs-tema', nuovo); } catch { /* preferenza non salvata */ }
  iconaTema();
});
sistemaScuro.addEventListener('change', iconaTema);
iconaTema();

// L'impronta dei dati (messa da crea-sito.js) evita di leggere quelli vecchi dalla cache del browser
const versioneDati = document.querySelector('meta[name="versione-dati"]')?.content;
const d = await (await fetch(versioneDati ? `dati.json?v=${versioneDati}` : 'dati.json')).json();

$('#versione').textContent = `Versione ${d.versione} · aggiornato il ${new Date(d.data).toLocaleDateString('it-IT', { day: 'numeric', month: 'long', year: 'numeric' })}`;
$('#aggiornato').textContent = `BS-BlockStrike ${d.versione}, dati del ${new Date(d.data).toLocaleDateString('it-IT')}.`;
$('#salute').textContent = `${d.salute}`;

// --- Suggerimento al passaggio del mouse ---
const sugg = $('#suggerimento');
function mostraSugg(x, y, contenuto) {
  sugg.replaceChildren(...contenuto);
  sugg.hidden = false;
  const r = sugg.getBoundingClientRect();
  sugg.style.left = `${Math.min(x + 14, innerWidth - r.width - 8)}px`;
  sugg.style.top = `${Math.max(8, y - r.height - 12)}px`;
}
const nascondiSugg = () => (sugg.hidden = true);

// --- Mappe ---
const COLORI_SQUADRA = ['var(--squadra-0)', 'var(--squadra-1)'];

// Zone Hardpoint una sopra l'altra (es. una galleria sotto una piazza): in pianta si sovrappongono
const siToccano = (a, b) => a !== b && a.x[0] < b.x[1] && b.x[0] < a.x[1] && a.z[0] < b.z[1] && b.z[0] < a.z[1];
const zonaSotto = (m, h) => m.hardpoint.some((o) => siToccano(o, h) && o.y > h.y + 1);
const zonaSopra = (m, h) => m.hardpoint.some((o) => siToccano(o, h) && o.y < h.y - 1);

function pianta(m) {
  const box = el('div', { class: 'pianta' }, el('img', { src: m.alto, alt: `Vista dall'alto di ${m.nome}`, width: m.larghezza, height: m.profondita }));
  const s = svg('svg', { viewBox: `0 0 ${m.larghezza} ${m.profondita}`, 'aria-hidden': 'true' });
  const k = m.larghezza / 60; // dimensioni dei segni in proporzione alla mappa
  // Zone sovrapposte: quella di sotto col bordo tratteggiato, e le due lettere affiancate (sopra a sinistra)
  for (const h of m.hardpoint) {
    const giu = zonaSotto(m, h), su = zonaSopra(m, h);
    s.append(svg('rect', { x: h.x[0], y: h.z[0], width: h.x[1] - h.x[0], height: h.z[1] - h.z[0], fill: 'none', stroke: '#ffffff',
      'stroke-width': 0.9 * k, rx: 0.6 * k, ...(giu ? { 'stroke-dasharray': `${2 * k} ${1.2 * k}` } : {}) }));
    const sposta = giu ? 2.6 * k : su ? -2.6 * k : 0;
    const t = svg('text', { x: (h.x[0] + h.x[1]) / 2 + sposta, y: (h.z[0] + h.z[1]) / 2, 'text-anchor': 'middle', 'dominant-baseline': 'central', 'font-size': 3.2 * k, 'font-weight': 700, fill: '#ffffff', stroke: '#111214', 'stroke-width': 0.5 * k, 'paint-order': 'stroke' });
    t.textContent = giu ? `${h.nome} ↓` : h.nome;
    s.append(t);
  }
  // Siti bomba (Cerca e distruggi): riquadri rossi tratteggiati con la lettera
  for (const t of m.siti ?? []) {
    s.append(svg('rect', { x: t.x[0], y: t.z[0], width: t.x[1] - t.x[0], height: t.z[1] - t.z[0], fill: 'rgba(255,77,77,0.18)', stroke: '#ff4d4d',
      'stroke-width': 0.7 * k, 'stroke-dasharray': `${1.4 * k} ${0.9 * k}` }));
    const t2 = svg('text', { x: (t.x[0] + t.x[1]) / 2, y: (t.z[0] + t.z[1]) / 2, 'text-anchor': 'middle', 'dominant-baseline': 'central', 'font-size': 2.6 * k, 'font-weight': 700, fill: '#ff4d4d', stroke: '#111214', 'stroke-width': 0.45 * k, 'paint-order': 'stroke' });
    t2.textContent = t.nome;
    s.append(t2);
  }
  // Bandiere del Dominio: cerchio di cattura (3 m) con la lettera
  for (const f of m.bandiere ?? []) {
    s.append(svg('circle', { cx: f.x, cy: f.z, r: 3, fill: 'rgba(255,210,58,0.2)', stroke: '#ffd23a', 'stroke-width': 0.6 * k }));
    const t2 = svg('text', { x: f.x, y: f.z, 'text-anchor': 'middle', 'dominant-baseline': 'central', 'font-size': 2.6 * k, 'font-weight': 700, fill: '#ffd23a', stroke: '#111214', 'stroke-width': 0.45 * k, 'paint-order': 'stroke' });
    t2.textContent = f.nome;
    s.append(t2);
  }
  for (const p of m.spawn) {
    if (p.squadra == null) continue;
    s.append(svg('circle', { cx: p.x, cy: p.z, r: 0.9 * k, fill: COLORI_SQUADRA[p.squadra], stroke: '#ffffff', 'stroke-width': 0.35 * k }));
  }
  // Dove passa il taglio della sezione
  const S = m.sezione;
  if (S) {
    const linea = S.asse === 'z' ? { x1: S.pos, y1: 0, x2: S.pos, y2: m.profondita } : { x1: 0, y1: S.pos, x2: m.larghezza, y2: S.pos };
    s.append(svg('line', { ...linea, stroke: '#ffd166', 'stroke-width': 0.6 * k, 'stroke-dasharray': `${2.5 * k} ${1.5 * k}`, opacity: 0.9 }));
  }
  box.append(s);
  return box;
}

// Sezione: la mappa tagliata lungo la linea tratteggiata e vista di lato (altezze esagerate), con le zone alla loro quota
function sezione(m) {
  const S = m.sezione, E = S.esagera, H = S.altezza * E;
  const box = el('div', { class: 'pianta' }, el('img', { src: S.img, alt: `Sezione di ${m.nome}`, width: S.lunghezza, height: H }));
  const s = svg('svg', { viewBox: `0 0 ${S.lunghezza} ${H}`, 'aria-hidden': 'true' });
  const k = S.lunghezza / 60;
  for (const h of m.hardpoint) {
    const [a0, a1] = S.asse === 'z' ? h.x : h.z;
    if (S.pos < a0 || S.pos > a1) continue; // zona non attraversata dal taglio
    const [u0, u1] = S.asse === 'z' ? h.z : h.x;
    const y0 = (S.altezza - h.y1) * E, alt = (h.y1 - h.y) * E;
    s.append(svg('rect', { x: u0, y: y0, width: u1 - u0, height: alt, fill: 'rgba(255,255,255,0.12)', stroke: '#ffffff', 'stroke-width': 0.6 * k, rx: 0.5 * k }));
    const t = svg('text', { x: (u0 + u1) / 2, y: y0 + alt / 2, 'text-anchor': 'middle', 'dominant-baseline': 'central', 'font-size': Math.min(3 * k, alt * 0.8), 'font-weight': 700, fill: '#ffffff', stroke: '#111214', 'stroke-width': 0.45 * k, 'paint-order': 'stroke' });
    t.textContent = h.nome;
    s.append(t);
  }
  box.append(s);
  return box;
}

// Pianta e sezione: si passa dall'una all'altra con le frecce
function vistePianta(m) {
  const S = m.sezione;
  const dir = S?.asse === 'z' ? 'da nord (a sinistra) a sud' : 'da ovest (a sinistra) a est';
  const viste = [
    { titolo: 'Vista dall\'alto', nota: S ? 'La linea tratteggiata è il taglio della sezione.' : '', el: pianta(m) },
    ...(S ? [{ titolo: 'Sezione', nota: `Mappa tagliata lungo la linea tratteggiata e vista di lato, ${dir}; altezze ×${S.esagera}.`, el: sezione(m) }] : []),
  ];
  let i = 0;
  const titolo = el('span', { class: 'titolo-vista' });
  const nota = el('p', { class: 'nota-vista' });
  const mostra = (j) => {
    i = (j + viste.length) % viste.length;
    viste.forEach((v, n) => { v.el.hidden = n !== i; });
    titolo.textContent = viste[i].titolo;
    nota.textContent = viste[i].nota;
  };
  const freccia = (d, segno, etichetta) => el('button', { type: 'button', class: 'freccia-vista', 'aria-label': etichetta, onclick: () => mostra(i + d) }, segno);
  mostra(0);
  return el('div', { class: 'viste-pianta' },
    viste.length > 1 ? el('div', { class: 'barra-vista' }, freccia(-1, '‹', 'Vista precedente'), titolo, freccia(1, '›', 'Vista successiva')) : null,
    ...viste.map((v) => v.el), nota);
}

function galleria(m) {
  const voci = [...m.foto.map((src, i) => ({ src, alt: `${m.nome}, foto ${i + 1}` })), { src: m.giro, alt: `${m.nome}, giro della mappa`, giro: true }];
  const grande = el('img', { class: 'grande', alt: voci[0].alt, src: voci[0].src });
  const miniature = el('div', { class: 'miniature' });
  const contenitore = el('div', { class: 'galleria' }, grande, miniature);
  let pronte = 0;
  voci.forEach((v, i) => {
    const img = el('img', { src: v.src, alt: '', loading: 'lazy' });
    const b = el('button', { type: 'button', class: v.giro ? 'giro' : null, 'aria-label': v.alt, 'aria-pressed': String(i === 0), onclick: () => {
      grande.src = v.src;
      grande.alt = v.alt;
      for (const x of miniature.children) x.setAttribute('aria-pressed', String(x === b));
    } }, img);
    // Le foto si rifanno solo quando cambiano le mappe: se una manca, la miniatura sparisce
    img.addEventListener('error', () => b.remove());
    img.addEventListener('load', () => pronte++);
    miniature.append(b);
  });
  grande.addEventListener('error', () => {
    const prima = miniature.querySelector('button');
    if (prima) prima.click();
    else grande.replaceWith(el('div', { class: 'nessuna-foto', testo: 'Foto in arrivo' }));
  });
  return contenitore;
}

function mostraMappa(m) {
  const squadre = m.spawn.some((p) => p.squadra != null);
  $('#mappa').replaceChildren(el('div', { class: 'mappa riquadro' },
    galleria(m),
    el('div', { class: 'info-mappa' },
      el('h3', { testo: m.nome }),
      el('p', { testo: m.descrizione }),
      el('ul', { class: 'etichette' },
        el('li', { testo: `${num(m.larghezza)} × ${num(m.profondita)} m` }),
        m.hardpoint.length ? el('li', { testo: `${m.hardpoint.length} zone Hardpoint` }) : null),
      vistePianta(m),
      el('div', { class: 'legenda' },
        squadre ? el('span', {}, el('i', { style: 'background:var(--squadra-0)' }), 'Partenza squadra blu') : null,
        squadre ? el('span', {}, el('i', { style: 'background:var(--squadra-1)' }), 'Partenza squadra arancione') : null,
        m.hardpoint.length ? el('span', {}, el('i', { class: 'zona' }), 'Zona Hardpoint') : null,
        m.hardpoint.some((h) => zonaSotto(m, h)) ? el('span', {}, el('i', { class: 'zona sotto' }), 'Zona sotterranea (↓)') : null,
        m.bandiere?.length ? el('span', {}, el('i', { style: 'background:transparent;border:2px solid #ffd23a;border-radius:50%' }), 'Bandiera del Dominio') : null,
        m.siti?.length ? el('span', {}, el('i', { style: 'background:rgba(255,77,77,0.25);border:2px dashed #ff4d4d' }), 'Sito bomba (Cerca e distruggi)') : null))));
}

const schede = $('#schede-mappe');
d.mappe.forEach((m, i) => schede.append(el('button', { type: 'button', role: 'tab', 'aria-selected': String(i === 0), testo: m.nome, onclick: (e) => {
  for (const b of schede.children) b.setAttribute('aria-selected', String(b === e.currentTarget));
  mostraMappa(m);
} })));
mostraMappa(d.mappe[0]);

// --- Modalità ---
$('#intro-modalita').textContent = `Partite fino a ${d.perSquadra} contro ${d.perSquadra}: i posti vuoti li prendono i bot. A fine partita si vota la prossima combinazione di mappa e modalità.`;
for (const m of d.modalita) {
  const tag = [m.varianti ? 'A squadre o tutti contro tutti' : m.squadre ? 'A squadre' : 'Tutti contro tutti'];
  if (m.limitePunteggio) tag.push(`${m.limitePunteggio} ${m.unita ?? 'uccisioni'}`);
  // A round (Prima linea, Cerca e distruggi): il tempo è di ogni round
  if (m.roundPerVincere) tag.push(`${m.roundPerVincere} round vinti`);
  if (m.limiteTempo) tag.push(m.roundPerVincere ? `${m.limiteTempo} minuti a round` : `${m.limiteTempo} minuti`);
  if (m.durataZona) tag.push(`zona nuova ogni ${m.durataZona} s`);
  $('#elenco-modalita').append(el('article', { class: 'voce' },
    el('h4', { testo: m.nome }),
    el('p', { testo: m.descrizione }),
    m.scala ? el('p', { testo: `Scala: ${m.scala.join(' → ')}` }) : null,
    el('ul', { class: 'etichette' }, tag.map((t) => el('li', { testo: t })))));
}

// --- Armi ---
const ARMI = d.armi;
const ZONE = Object.keys(d.zone);
const conTtk = (a) => !!a.ttk;
const ttkA = (a, zona, dist) => {
  if (!a.ttk) return null;
  const c = a.ttk[zona];
  return dist < c.tempo.length ? { tempo: c.tempo[dist], colpi: c.colpi[dist] } : null;
};
const cadenza = (a) => (a.rpmMax ? `${a.rpm}–${a.rpmMax}` : `${a.rpm}`) + (a.raffica ? ` (raffica da ${a.raffica})` : '');
const danno = (a) => (a.pallini ? `${a.danno}×${a.pallini}` : a.danno === a.dannoMin ? `${a.danno}` : `${a.danno}–${a.dannoMin}`);

const COLONNE = [
  { id: 'nome', titolo: 'Arma', valore: (a) => a.nome, testo: (a) => a.nome },
  { id: 'classe', titolo: 'Classe', valore: (a) => a.nomeClasse, testo: (a) => a.nomeClasse, classe: 'classe' },
  { id: 'danno', titolo: 'Danno', valore: (a) => a.danno * (a.pallini ?? 1), testo: danno },
  { id: 'rpm', titolo: 'Colpi/min', valore: (a) => a.rpm, testo: (a) => (a.rpmMax ? `${a.rpm}–${a.rpmMax}` : `${a.rpm}`) },
  { id: 'caricatore', titolo: 'Caricatore', valore: (a) => a.caricatore, testo: (a) => (a.caricatore ? `${a.caricatore}` : '—') },
  { id: 'ttk', titolo: 'TTK pancia', valore: (a) => ttkA(a, 'bustoBasso', 0)?.tempo ?? Infinity, testo: (a) => (conTtk(a) ? esito(ttkA(a, 'bustoBasso', 0), false) : '—') },
  { id: 'ttkTesta', titolo: 'TTK testa', valore: (a) => ttkA(a, 'testa', 0)?.tempo ?? Infinity, testo: (a) => (conTtk(a) ? esito(ttkA(a, 'testa', 0), false) : '—') },
  { id: 'portata', titolo: 'Portata', valore: (a) => a.portata, testo: (a) => `${num(a.portata, a.portata % 1 ? 1 : 0)} m` },
  { id: 'livello', titolo: 'Livello', valore: (a) => (a.sfida ? 999 : a.livello), testo: (a) => (a.sfida ? 'sfida' : `${a.livello}`) },
];

const statoArmi = { classe: null, ordine: null, verso: 1, scelta: ARMI[0].id };
const classi = [...new Map(ARMI.map((a) => [a.nomeClasse, a.nomeClasse])).keys()];
const filtri = $('#filtri-armi');
for (const c of [null, ...classi]) {
  filtri.append(el('button', { type: 'button', 'aria-pressed': String(c === null), testo: c ?? 'Tutte', onclick: (e) => {
    statoArmi.classe = c;
    for (const b of filtri.children) b.setAttribute('aria-pressed', String(b === e.currentTarget));
    const visibili = ARMI.filter((a) => !c || a.nomeClasse === c);
    if (!visibili.some((a) => a.id === statoArmi.scelta)) statoArmi.scelta = visibili[0].id;
    disegnaArmi();
  } }));
}

function disegnaArmi() {
  const t = $('#tabella-armi');
  let elenco = ARMI.filter((a) => !statoArmi.classe || a.nomeClasse === statoArmi.classe);
  const col = COLONNE.find((c) => c.id === statoArmi.ordine);
  if (col) elenco = [...elenco].sort((a, b) => {
    const x = col.valore(a), y = col.valore(b);
    return (typeof x === 'string' ? x.localeCompare(y) : x - y) * statoArmi.verso;
  });
  t.replaceChildren(
    el('thead', {}, el('tr', {}, COLONNE.map((c) => el('th', { scope: 'col', 'aria-sort': c.id === statoArmi.ordine ? (statoArmi.verso > 0 ? 'ascending' : 'descending') : null },
      el('button', { type: 'button', testo: c.titolo + (c.id === statoArmi.ordine ? (statoArmi.verso > 0 ? ' ↑' : ' ↓') : ''), onclick: () => {
        statoArmi.verso = statoArmi.ordine === c.id ? -statoArmi.verso : 1;
        statoArmi.ordine = c.id;
        disegnaArmi();
      } }))))),
    el('tbody', {}, elenco.map((a) => el('tr', { 'aria-selected': String(a.id === statoArmi.scelta), tabindex: 0,
      onclick: () => scegli(a.id), onkeydown: (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); scegli(a.id); } } },
      COLONNE.map((c, i) => el(i === 0 ? 'th' : 'td', { scope: i === 0 ? 'row' : null, class: c.classe ?? null, testo: c.testo(a) }))))));
  dettaglio(ARMI.find((a) => a.id === statoArmi.scelta));
}

function scegli(id) {
  statoArmi.scelta = id;
  disegnaArmi();
}

// Colore dei moltiplicatori: 5 gradini della stessa tinta
const gradino = (m) => (m < 0.9 ? 1 : m < 1 ? 2 : m < 1.1 ? 3 : m < 1.45 ? 4 : 5);
const coloreMolt = (m) => `var(--m${gradino(m)})`;

// Manichino a blocchi: rettangoli per zona (coordinate in unità del disegno)
const SAGOMA = [
  ['testa', 40, 4, 40, 36],
  ['bustoAlto', 30, 44, 60, 42],
  ['bustoBasso', 30, 86, 60, 34],
  ['braccia', 6, 44, 22, 74],
  ['braccia', 92, 44, 22, 74],
  ['gambe', 30, 124, 28, 86],
  ['gambe', 62, 124, 28, 86],
];

// La barra in cima ai pannelli (distanza e, per i pompa, anca o mirando) guida tutte le schede
let ascoltaDistanza = [];

// Scheda dei moltiplicatori: sagoma colorata e, per zona, colpi e TTK alla distanza scelta
function schedaMoltiplicatori(a) {
  const corpo = svg('svg', { viewBox: '0 0 120 214', role: 'img', 'aria-label': 'Moltiplicatori di danno per zona del corpo' });
  for (const [z, x, y, w, h] of SAGOMA) {
    const r = svg('rect', { x, y, width: w, height: h, rx: 3, fill: coloreMolt(a.moltiplicatori[z]) });
    r.append(Object.assign(svg('title'), { textContent: `${d.zone[z]} ${molt(a.moltiplicatori[z])}` }));
    corpo.append(r);
  }
  const zone = el('ul', { class: 'zone' });
  const titolo = el('span', { class: 'tenue' });
  ascoltaDistanza.push((dist) => {
    titolo.textContent = ` a ${dist} m`;
    zone.replaceChildren(...ZONE.map((z) => el('li', {}, el('i', { style: `background:${coloreMolt(a.moltiplicatori[z])}` }), el('span', { testo: d.zone[z] }),
      el('span', { class: 'molt', testo: molt(a.moltiplicatori[z]) }), el('span', { class: 'esito', testo: esito(ttkA(a, z, dist)) }))));
  });
  return el('section', { class: 'scheda' },
    el('h4', {}, 'Moltiplicatori sul corpo', titolo),
    el('p', { class: 'nota', testo: a.pallini ? 'Colpi e TTK con tutti i pallini a segno.' : 'Danno del colpo × moltiplicatore della zona colpita.' }),
    el('div', { class: 'corpo' }, corpo, zone),
    el('div', { class: 'scala' }, 'meno danno', [1, 2, 3, 4, 5].map((n) => el('i', { style: `background:var(--m${n})` })), 'più danno'));
}

// Scheda della tabella: colpi e TTK per zona alle distanze tipiche (per i pompa quelle utili, fino a 20 m)
function schedaTabella(a) {
  const distanze = (a.pallini ? [5, 10, 15, 20] : d.distanzeTabella).filter((x) => x <= distMax(a));
  return el('section', { class: 'scheda' },
    el('h4', { testo: 'Colpi e TTK per zona' }),
    el('p', { class: 'nota', testo: a.pallini ? 'Con tutti i pallini a segno: nella rosata quanti vanno a segno davvero.' : 'Tutti i colpi nella stessa zona.' }),
    el('div', { class: 'tabella-scorre' }, el('table', { class: 'tabella tabella-ttk' },
      el('thead', {}, el('tr', {}, el('th', { scope: 'col', testo: 'Zona' }), distanze.map((x) => el('th', { scope: 'col', testo: `${x} m` })))),
      el('tbody', {}, ZONE.map((z) => el('tr', {}, el('th', { scope: 'row', testo: d.zone[z] }), distanze.map((x) => {
        const u = ttkA(a, z, x);
        return el('td', { testo: u.colpi === 1 ? '1 colpo' : `${u.colpi} · ${secondi(u.tempo)}` });
      })))))));
}

// TTK alla pancia in funzione della distanza: l'arma scelta in evidenza, le altre della stessa classe in grigio
// Pompa senza slug: oltre 20 m e 2 s il grafico non dice niente di utile
const LIMITI_POMPA = { dist: 20, tempo: 2000 };
const distMax = (a) => Math.min(a.ttk.bustoBasso.tempo.length - 1, a.pallini ? LIMITI_POMPA.dist : Infinity);

function graficoTtk(a) {
  const altre = ARMI.filter((x) => x.id !== a.id && x.ttk && x.classe === a.classe);
  const serie = [a, ...altre];
  const W = 560, H = 230, M = { s: 44, d: 64, a: 12, b: 34 };
  const maxD = Math.max(...serie.map(distMax));
  const maxT = Math.min(a.pallini ? LIMITI_POMPA.tempo : Infinity, Math.max(200, ...serie.flatMap((x) => x.ttk.bustoBasso.tempo.slice(0, maxD + 1))));
  const passoT = maxT > 1500 ? 500 : maxT > 600 ? 200 : 100;
  // Un passo di margine in cima: una curva al massimo non finisce sulla riga più alta della griglia
  const topT = (Math.floor(maxT / passoT) + 1) * passoT;
  const X = (dist) => M.s + (dist / maxD) * (W - M.s - M.d);
  const Y = (t) => H - M.b - (Math.min(t, topT) / topT) * (H - M.a - M.b);
  const s = svg('svg', { viewBox: `0 0 ${W} ${H}`, role: 'img', 'aria-label': `TTK alla pancia di ${a.nome} per distanza` });
  const griglia = svg('g', { class: 'griglia-asse' });
  for (let t = 0; t <= topT; t += passoT) {
    griglia.append(svg('line', { x1: M.s, x2: W - M.d, y1: Y(t), y2: Y(t) }));
    const tx = svg('text', { x: M.s - 8, y: Y(t), 'text-anchor': 'end', 'dominant-baseline': 'central' });
    tx.textContent = `${num(t / 1000, passoT < 1000 ? 1 : 0)} s`;
    griglia.append(tx);
  }
  const passoD = maxD > 60 ? 20 : 10;
  for (let x = 0; x <= maxD; x += passoD) {
    const tx = svg('text', { x: X(x), y: H - M.b + 18, 'text-anchor': 'middle' });
    tx.textContent = `${x} m`;
    griglia.append(tx);
  }
  s.append(griglia, svg('g', { class: 'asse' }), svg('line', { x1: M.s, x2: W - M.d, y1: Y(0), y2: Y(0), stroke: 'var(--asse)' }));
  // Gradini: il TTK cambia a salti quando serve un colpo in più
  const percorso = (x) => {
    const t = x.ttk.bustoBasso.tempo;
    let p = `M${X(0)},${Y(t[0])}`;
    for (let i = 1; i <= Math.min(t.length - 1, maxD); i++) p += `H${X(i)}V${Y(t[i])}`;
    return p;
  };
  for (const x of altre) s.append(svg('path', { d: percorso(x), class: 'altra' }));
  s.append(svg('path', { d: percorso(a), class: 'scelta' }));
  const ultimo = distMax(a);
  const et = svg('text', { x: X(ultimo) + 6, y: Y(a.ttk.bustoBasso.tempo[ultimo]), class: 'etichetta-scelta', 'dominant-baseline': 'central' });
  et.textContent = a.nome.split(' ')[0];
  s.append(et);

  // Croce e punto alla distanza del cursore o del mouse
  const croce = svg('line', { class: 'croce', y1: M.a, y2: H - M.b, visibility: 'hidden' });
  const punto = svg('circle', { class: 'punto', r: 4.5, visibility: 'hidden' });
  s.append(croce, punto);
  const segna = (dist) => {
    dist = Math.min(dist, maxD);
    const t = ttkA(a, 'bustoBasso', dist);
    croce.setAttribute('x1', X(dist));
    croce.setAttribute('x2', X(dist));
    croce.setAttribute('visibility', 'visible');
    if (t) {
      punto.setAttribute('cx', X(dist));
      punto.setAttribute('cy', Y(t.tempo));
    }
    punto.setAttribute('visibility', t ? 'visible' : 'hidden');
  };
  ascoltaDistanza.push(segna);
  const zonaMouse = svg('rect', { x: M.s, y: 0, width: W - M.s - M.d, height: H, fill: 'transparent' });
  zonaMouse.addEventListener('pointermove', (e) => {
    const r = s.getBoundingClientRect();
    const dist = Math.max(0, Math.min(maxD, Math.round(((e.clientX - r.left) / r.width * W - M.s) / (W - M.s - M.d) * maxD)));
    segna(dist);
    const righe = serie.map((x) => ({ x, t: ttkA(x, 'bustoBasso', dist) })).filter((r) => r.t).sort((p, q) => p.t.tempo - q.t.tempo);
    mostraSugg(e.clientX, e.clientY, [el('b', { testo: `${dist} m` }),
      ...righe.map(({ x, t }) => el('div', { class: x === a ? 'scelta' : null }, el('span', { testo: x.nome }), el('span', { testo: esito(t) })))]);
  });
  zonaMouse.addEventListener('pointerleave', () => {
    nascondiSugg();
    croce.setAttribute('visibility', 'hidden');
    punto.setAttribute('visibility', 'hidden');
  });
  s.append(zonaMouse);

  return el('section', { class: 'scheda grafico' },
    el('h4', { testo: 'TTK alla pancia per distanza' }),
    el('p', { class: 'nota', testo: a.pallini ? `Con tutti i pallini a segno, fino a ${LIMITI_POMPA.dist} m: oltre, senza slug, la rosata è troppo larga.` : 'Sale a gradini quando con il calo del danno serve un colpo in più. Passa il mouse per i valori.' }),
    s,
    el('div', { class: 'legenda-grafico' },
      el('span', {}, el('i', { style: 'background:var(--accento)' }), a.nome),
      altre.length ? el('span', {}, el('i', { style: 'background:var(--tenue);opacity:0.6' }), `altre armi: ${d.armi.find((x) => x.classe === a.classe).nomeClasse.toLowerCase()}`) : null));
}

// Rosata dei pompa: sagoma vista di fronte con i pallini di uno sparo e le medie (calcolate con la logica del gioco)
const statoRosata = { dist: 10, mirando: true };
const COLORE_ZONA = { testa: 'var(--z-testa)', bustoAlto: 'var(--z-petto)', bustoBasso: 'var(--z-pancia)', braccia: 'var(--z-braccia)', gambe: 'var(--z-gambe)' };

function schedaRosata(a) {
  const R = a.rosata, fine = R.da + R.anca.length - 1;
  const disegno = el('div', { class: 'disegno-rosata' });
  const numeri = el('dl', { class: 'numeri-rosata' });
  const titolo = el('span', { class: 'tenue' });
  const aggiorna = () => {
    const r = (statoRosata.mirando ? R.mira : R.anca)[statoRosata.dist - R.da];
    titolo.textContent = ` a ${statoRosata.dist} m, ${statoRosata.mirando ? 'mirando' : "dall'anca"}`;
    const meta = Math.max(1.05, r.raggio + 0.4), cy = 1.0, punto = Math.max(0.03, meta * 0.022);
    const s = svg('svg', { viewBox: `${-meta} ${-(cy + meta)} ${2 * meta} ${2 * meta}`, role: 'img', 'aria-label': `Pallini di uno sparo a ${statoRosata.dist} m` });
    s.append(svg('line', { x1: -meta, x2: meta, y1: 0, y2: 0, class: 'terra' }));
    for (const z of d.rosata.sagoma) s.append(svg('rect', { x: z.x, y: -(z.y + z.h), width: z.w, height: z.h, fill: COLORE_ZONA[z.zona] }));
    s.append(svg('circle', { cx: 0, cy: -d.rosata.mira, r: r.raggio, class: 'cerchio' }));
    for (const [x, y, aSegno] of r.esempio) s.append(svg('circle', { cx: x, cy: -y, r: punto, class: aSegno ? 'pallino a-segno' : 'pallino' }));
    disegno.replaceChildren(s);
    numeri.replaceChildren(...[
      ['Pallini a segno', `${num(r.aSegno, 1)} di ${a.pallini}`],
      ['Danni a sparo', num(r.danno)],
      ['Uccide con 1 sparo', `${num(r.unoSparo * 100)}%`],
      ['Spari per uccidere', r.spari >= 10 ? 'più di 10' : `${num(r.spari, 1)} (${num(r.ttk, 2)} s)`],
    ].map(([k, v]) => el('div', {}, el('dt', { testo: k }), el('dd', { testo: v }))));
  };
  ascoltaDistanza.push((dist) => {
    statoRosata.dist = Math.min(Math.max(dist, R.da), fine);
    aggiorna();
  });
  return el('section', { class: 'scheda' },
    el('h4', {}, 'Rosata', titolo),
    el('p', { class: 'nota', testo: `Medie su ${d.rosata.spari} spari contro un avversario in piedi, mirando al centro del petto; il disegno è uno sparo di esempio.` }),
    el('div', { class: 'corpo-rosata' },
      el('div', {}, disegno, el('div', { class: 'legenda-grafico' },
        el('span', {}, el('i', { class: 'punto-pieno' }), 'a segno'), el('span', {}, el('i', { class: 'punto-vuoto' }), 'mancato'))),
      numeri));
}

// Barra della distanza (e di anca o mirando per i pompa) e le schede: moltiplicatori | TTK, tabella | rosata
function pannelliArma(a) {
  const fine = distMax(a);
  let dist = a.pallini ? Math.min(10, fine) : 0;
  const schede = [schedaMoltiplicatori(a), graficoTtk(a), schedaTabella(a), a.rosata ? schedaRosata(a) : null];
  const uscita = el('output');
  const avvisa = () => {
    uscita.textContent = `${dist} m`;
    for (const f of ascoltaDistanza) f(dist);
  };
  const cursore = el('input', { type: 'range', min: 0, max: fine, value: dist, step: 1, 'aria-label': 'Distanza in metri',
    oninput: (e) => { dist = Number(e.target.value); avvisa(); } });
  const mira = a.rosata ? el('div', { class: 'filtri piccoli', role: 'group', 'aria-label': 'Come si spara' }) : null;
  const segnaMira = () => { for (const b of mira.children) b.setAttribute('aria-pressed', String((b.dataset.mira === '1') === statoRosata.mirando)); };
  if (mira) {
    for (const [v, t] of [['0', 'Anca'], ['1', 'Mirando']]) {
      mira.append(el('button', { type: 'button', 'data-mira': v, testo: t, onclick: () => { statoRosata.mirando = v === '1'; segnaMira(); avvisa(); } }));
    }
    segnaMira();
  }
  avvisa();
  return el('div', {},
    el('div', { class: 'controlli-arma' }, el('label', { class: 'distanza' }, 'Distanza', cursore, uscita), mira),
    el('div', { class: `pannelli${a.rosata ? '' : ' senza-rosata'}` }, schede.filter(Boolean)));
}

function dettaglio(a) {
  ascoltaDistanza = [];
  const stat = [
    ['Danno', a.esplosione ? `${a.danno} al centro, ${a.dannoMin} al bordo` : `${danno(a)}${a.dannoMin !== a.danno ? ` (cala da ${a.cadutaInizio} a ${a.cadutaFine} m)` : ''}`],
    ['Colpi al minuto', cadenza(a)],
    a.caricatore ? ['Caricatore', `${a.caricatore} + ${a.riserva}`] : null,
    a.caricatore ? ['Ricarica', a.colpoPerColpo ? `${num(a.ricarica, 2)} s a cartuccia` : `${num(a.ricarica, 1)} s`] : null,
    a.esplosione ? ['Esplosione', `raggio ${num(a.esplosione.raggio, 1)} m`] : null,
    ['Portata', `${num(a.portata, a.portata % 1 ? 1 : 0)} m`],
    ['Mobilità', `${Math.round(a.velocita * 100)}%`],
    ['Tempo di mira', `${num(a.tempoMira, 2)} s`],
    ['Estrazione', `${num(a.estrazione, 2)} s`],
    ['Sblocco', a.sfida ? 'con la sfida' : `livello ${a.livello}`],
  ].filter(Boolean);
  $('#arma').replaceChildren(el('div', { class: 'arma riquadro' },
    el('div', { class: 'arma-testa' }, el('h3', { testo: a.nome }), el('span', { class: 'classe', testo: `${a.nomeClasse} · ${a.slot === 'primaria' ? 'primaria' : 'secondaria'}` })),
    el('p', { class: 'speciale', testo: a.speciale ?? (a.tipo === 'mischia' ? 'Un colpo, una uccisione.' : a.esplosione ? 'Il razzo vola dritto ed esplode all\'impatto.' : '') }),
    el('dl', { class: 'statistiche' }, stat.map(([k, v]) => el('div', {}, el('dt', { testo: k }), el('dd', { testo: v })))),
    aspetto(a),
    a.ttk ? pannelliArma(a) : null));
}

// Foto dell'arma: base e, se monta accessori, la configurazione per stabilità e danno e quella per velocità.
// Una foto che manca (sito costruito senza --foto) non lascia il riquadro vuoto
function aspetto(a) {
  if (!a.aspetto?.length) return null;
  return el('div', { class: 'aspetto' },
    a.aspetto.map((c) => {
      const fig = el('figure', {},
        el('img', { src: c.foto, alt: `${a.nome}: ${c.nome.toLowerCase()}`, loading: 'lazy', width: 720, height: 300, onerror: () => fig.remove() }),
        el('figcaption', {}, el('b', { testo: c.nome }), el('span', { testo: c.accessori.length ? c.accessori.join(' · ') : 'senza accessori' })));
      return fig;
    }));
}

disegnaArmi();

// --- Granate ---
for (const g of [...d.granate].sort((a, b) => (a.tipo === b.tipo ? a.livello - b.livello : a.tipo === 'letale' ? -1 : 1))) {
  const tag = [g.tipo === 'letale' ? 'Letale' : 'Tattica', `${g.quantita} per vita`];
  if (g.danno) tag.push(`fino a ${g.danno} danni`);
  if (g.raggio) tag.push(`raggio ${num(g.raggio, g.raggio % 1 ? 1 : 0)} m`);
  $('#elenco-granate').append(el('article', { class: 'voce' },
    el('div', { class: 'testa' }, el('h4', { testo: g.nome }), el('span', { class: 'livello', testo: `livello ${g.livello}` })),
    el('p', { testo: g.descrizione }),
    el('ul', { class: 'etichette' }, tag.map((t) => el('li', { testo: t })))));
}

// --- Armi DEV Edition ---
$('#come-armi-dev').textContent = d.armiDev.comeMappa;
for (const a of d.armiDev.armi) {
  const fig = el('figure', {}, el('img', { src: a.foto, alt: a.nome, loading: 'lazy', onerror: () => fig.remove() }));
  $('#elenco-armi-dev').append(el('article', { class: 'arma-dev voce' },
    fig,
    el('div', {},
      el('h3', { class: 'nome-gradiente', testo: a.nome }),
      a.come ? el('p', { testo: a.come }) : null,
      el('ul', {}, a.punti.map((p) => el('li', { testo: p }))),
      a.accessori.length ? el('dl', { class: 'accessori-dev' }, a.accessori.map((x) => el('div', {}, el('dt', { testo: x.nome }), el('dd', { testo: x.descrizione })))) : null)));
}

// --- Perk e scorestreak ---
const categoriePerk = Object.values(d.categoriePerk);
$('#intro-perk').textContent = `Nel loadout si scelgono ${d.slotPerk} perk, uno per categoria (${categoriePerk.join(', ')}); si sbloccano salendo di livello.`;
for (const [cat, nome] of Object.entries(d.categoriePerk)) {
  const perk = d.perk.filter((p) => p.categoria === cat).sort((a, b) => a.livello - b.livello);
  $('#elenco-perk').append(el('h4', { class: 'categoria-perk', testo: nome }), el('div', { class: 'griglia' }, perk.map((p) => el('article', { class: 'voce' },
    el('div', { class: 'testa' }, el('h4', { testo: p.nome }), el('span', { class: 'livello', testo: `livello ${p.livello}` })),
    el('p', { testo: p.descrizione })))));
}
const ps = d.puntiSerie;
$('#intro-streak').textContent = `Si ottengono con i punti di serie, che si azzerano morendo: ${ps.uccisione} a uccisione, ${ps.assist} per un assist, `
  + `${ps.obiettivo} per un obiettivo e ${ps.velivolo} per un UAV abbattuto. Se ne scelgono ${d.slotKillstreak} e si usano con 3, 4 e 5; `
  + 'una volta pronte restano finché non le usi.';
// Abilità: una nel loadout, si usa con H e si ricarica nel tempo
$('#intro-abilita').textContent = `Se ne sceglie una nel loadout e si usa con H; poi si ricarica nel tempo, e ogni uccisione toglie il ${Math.round(d.scontoAbilita * 100)}% della ricarica che resta.`;
$('#elenco-abilita').append(...d.abilita.map((a) => el('article', { class: 'voce' },
  el('div', { class: 'testa' }, el('h4', { testo: a.nome }), el('span', { class: 'livello', testo: `livello ${a.livello}` })),
  el('p', { testo: `${a.descrizione}. Ricarica ${a.ricarica} s.` }))));
for (const k of d.killstreak) {
  $('#elenco-streak').append(el('li', {},
    el('div', { class: 'uccisioni' }, `${k.punti}`, el('small', { testo: 'punti' })),
    el('div', {}, el('h4', { testo: k.nome }), el('p', { testo: k.descrizione + (k.durata ? ` (${k.durata} s)` : '') })),
    el('span', { class: 'livello', testo: `livello ${k.livello}` })));
}
