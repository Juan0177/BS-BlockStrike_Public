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

const d = await (await fetch('dati.json')).json();

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

function pianta(m) {
  const box = el('div', { class: 'pianta' }, el('img', { src: m.alto, alt: `Vista dall'alto di ${m.nome}`, width: m.larghezza, height: m.profondita }));
  const s = svg('svg', { viewBox: `0 0 ${m.larghezza} ${m.profondita}`, 'aria-hidden': 'true' });
  const k = m.larghezza / 60; // dimensioni dei segni in proporzione alla mappa
  for (const h of m.hardpoint) {
    s.append(svg('rect', { x: h.x[0], y: h.z[0], width: h.x[1] - h.x[0], height: h.z[1] - h.z[0], fill: 'none', stroke: '#ffffff', 'stroke-width': 0.9 * k, rx: 0.6 * k }));
    const t = svg('text', { x: (h.x[0] + h.x[1]) / 2, y: (h.z[0] + h.z[1]) / 2, 'text-anchor': 'middle', 'dominant-baseline': 'central', 'font-size': 3.2 * k, 'font-weight': 700, fill: '#ffffff', stroke: '#111214', 'stroke-width': 0.5 * k, 'paint-order': 'stroke' });
    t.textContent = h.nome;
    s.append(t);
  }
  for (const p of m.spawn) {
    if (p.squadra == null) continue;
    s.append(svg('circle', { cx: p.x, cy: p.z, r: 0.9 * k, fill: COLORI_SQUADRA[p.squadra], stroke: '#ffffff', 'stroke-width': 0.35 * k }));
  }
  box.append(s);
  return box;
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
      pianta(m),
      el('div', { class: 'legenda' },
        squadre ? el('span', {}, el('i', { style: 'background:var(--squadra-0)' }), 'Partenza squadra blu') : null,
        squadre ? el('span', {}, el('i', { style: 'background:var(--squadra-1)' }), 'Partenza squadra arancione') : null,
        m.hardpoint.length ? el('span', {}, el('i', { class: 'zona' }), 'Zona Hardpoint') : null))));
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
  const tag = [m.squadre ? 'A squadre' : 'Tutti contro tutti'];
  if (m.limitePunteggio) tag.push(m.id === 'hardpoint' ? `${m.limitePunteggio} punti` : `${m.limitePunteggio} uccisioni`);
  if (m.limiteTempo) tag.push(`${m.limiteTempo} minuti`);
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

function pannelloCorpo(a) {
  const fine = a.ttk.bustoBasso.tempo.length - 1;
  let dist = 0;
  const corpo = svg('svg', { viewBox: '0 0 120 214', role: 'img', 'aria-label': 'Moltiplicatori di danno per zona del corpo' });
  for (const [z, x, y, w, h] of SAGOMA) {
    const r = svg('rect', { x, y, width: w, height: h, rx: 3, fill: coloreMolt(a.moltiplicatori[z]) });
    r.append(Object.assign(svg('title'), { textContent: `${d.zone[z]} ${molt(a.moltiplicatori[z])}` }));
    corpo.append(r);
  }
  const zone = el('ul', { class: 'zone' });
  const uscita = el('output', { testo: '0 m' });
  const aggiorna = () => {
    uscita.textContent = `${dist} m`;
    zone.replaceChildren(...ZONE.map((z) => {
      const u = ttkA(a, z, dist);
      return el('li', {}, el('i', { style: `background:${coloreMolt(a.moltiplicatori[z])}` }), el('span', { testo: d.zone[z] }),
        el('span', { class: 'molt', testo: molt(a.moltiplicatori[z]) }),
        el('span', { class: 'esito', testo: esito(u) }));
    }));
  };
  aggiorna();
  const cursore = el('input', { type: 'range', min: 0, max: fine, value: 0, step: 1, 'aria-label': 'Distanza in metri', oninput: (e) => {
    dist = Number(e.target.value);
    aggiorna();
    graficoDist?.(dist);
  } });
  const scala = el('div', { class: 'scala' }, 'meno danno', [1, 2, 3, 4, 5].map((n) => el('i', { style: `background:var(--m${n})` })), 'più danno');

  // Tabella equivalente (colpi e TTK alle distanze tipiche)
  const distanze = d.distanzeTabella.filter((x) => x <= fine);
  const tabella = el('table', { class: 'tabella tabella-ttk' },
    el('caption', { testo: 'Colpi e TTK per zona e distanza' }),
    el('thead', {}, el('tr', {}, el('th', { scope: 'col', testo: 'Zona' }), distanze.map((x) => el('th', { scope: 'col', testo: `${x} m` })))),
    el('tbody', {}, ZONE.map((z) => el('tr', {}, el('th', { scope: 'row', testo: d.zone[z] }), distanze.map((x) => {
      const u = ttkA(a, z, x);
      return el('td', { testo: u.colpi === 1 ? '1 colpo' : `${u.colpi} · ${secondi(u.tempo)}` });
    })))));

  return el('div', {},
    el('h4', { testo: 'Moltiplicatori sul corpo' }),
    el('p', { class: 'nota', testo: a.tipo === 'pallini' ? 'Con tutti i pallini a segno.' : 'Danno del colpo × moltiplicatore della zona colpita.' }),
    el('label', { class: 'distanza' }, 'Distanza', cursore, uscita),
    el('div', { class: 'corpo' }, corpo, zone),
    scala,
    el('div', { class: 'tabella-scorre', style: 'margin-top:16px' }, tabella));
}

let graficoDist = null;

// TTK alla pancia in funzione della distanza: l'arma scelta in evidenza, le altre della stessa classe in grigio
function graficoTtk(a) {
  const altre = ARMI.filter((x) => x.id !== a.id && x.ttk && x.classe === a.classe);
  const serie = [a, ...altre];
  const W = 560, H = 300, M = { s: 44, d: 64, a: 12, b: 34 };
  const maxD = Math.max(...serie.map((x) => x.ttk.bustoBasso.tempo.length - 1));
  const maxT = Math.max(200, ...serie.flatMap((x) => x.ttk.bustoBasso.tempo));
  const passoT = maxT > 1500 ? 500 : maxT > 600 ? 200 : 100;
  const topT = Math.ceil(maxT / passoT) * passoT;
  const X = (dist) => M.s + (dist / maxD) * (W - M.s - M.d);
  const Y = (t) => H - M.b - (t / topT) * (H - M.a - M.b);
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
  const ts = svg('text', { x: W - M.d + 6, y: Y(0), 'dominant-baseline': 'central' });
  ts.textContent = 'distanza';
  griglia.append(ts);
  s.append(griglia, svg('g', { class: 'asse' }), svg('line', { x1: M.s, x2: W - M.d, y1: Y(0), y2: Y(0), stroke: 'var(--asse)' }));
  // Gradini: il TTK cambia a salti quando serve un colpo in più
  const percorso = (x) => {
    const t = x.ttk.bustoBasso.tempo;
    let p = `M${X(0)},${Y(t[0])}`;
    for (let i = 1; i < t.length; i++) p += `H${X(i)}V${Y(t[i])}`;
    return p;
  };
  for (const x of altre) s.append(svg('path', { d: percorso(x), class: 'altra' }));
  s.append(svg('path', { d: percorso(a), class: 'scelta' }));
  const ultimo = a.ttk.bustoBasso.tempo.length - 1;
  const et = svg('text', { x: X(ultimo) + 6, y: Y(a.ttk.bustoBasso.tempo[ultimo]), class: 'etichetta-scelta', 'dominant-baseline': 'central' });
  et.textContent = a.nome.split(' ')[0];
  s.append(et);

  // Croce e punto alla distanza del cursore o del mouse
  const croce = svg('line', { class: 'croce', y1: M.a, y2: H - M.b, visibility: 'hidden' });
  const punto = svg('circle', { class: 'punto', r: 4.5, visibility: 'hidden' });
  s.append(croce, punto);
  const segna = (dist) => {
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
  graficoDist = segna;
  const zonaMouse = svg('rect', { x: M.s, y: 0, width: W - M.s - M.d, height: H, fill: 'transparent' });
  zonaMouse.addEventListener('pointermove', (e) => {
    const r = s.getBoundingClientRect();
    const dist = Math.max(0, Math.min(maxD, Math.round(((e.clientX - r.left) / r.width * W - M.s) / (W - M.s - M.d) * maxD)));
    segna(dist);
    const righe = serie.map((x) => ({ x, t: ttkA(x, 'bustoBasso', dist) })).filter((r) => r.t).sort((p, q) => p.t.tempo - q.t.tempo);
    mostraSugg(e.clientX, e.clientY, [el('b', { testo: `${dist} m` }),
      ...righe.map(({ x, t }) => el('div', { class: x === a ? 'scelta' : null }, el('span', { testo: x.nome }), el('span', { testo: `${secondi(t.tempo)} · ${t.colpi}` })))]);
  });
  zonaMouse.addEventListener('pointerleave', () => {
    nascondiSugg();
    croce.setAttribute('visibility', 'hidden');
    punto.setAttribute('visibility', 'hidden');
  });
  s.append(zonaMouse);

  return el('div', { class: 'grafico' },
    el('h4', { testo: 'TTK alla pancia per distanza' }),
    el('p', { class: 'nota', testo: 'Il tempo sale a gradini quando con il calo del danno serve un colpo in più. Passa il mouse per i valori.' }),
    s,
    el('div', { class: 'legenda-grafico' },
      el('span', {}, el('i', { style: 'background:var(--accento)' }), a.nome),
      altre.length ? el('span', {}, el('i', { style: 'background:var(--asse)' }), `altre armi: ${d.armi.find((x) => x.classe === a.classe).nomeClasse.toLowerCase()}`) : null));
}

function dettaglio(a) {
  graficoDist = null;
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
    a.ttk ? el('div', { class: 'pannelli' }, pannelloCorpo(a), graficoTtk(a)) : null));
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

// --- Perk e scorestreak ---
$('#intro-perk').textContent = `Nel loadout si scelgono ${d.slotPerk} perk; si sbloccano salendo di livello.`;
for (const p of [...d.perk].sort((a, b) => a.livello - b.livello)) {
  $('#elenco-perk').append(el('article', { class: 'voce' },
    el('div', { class: 'testa' }, el('h4', { testo: p.nome }), el('span', { class: 'livello', testo: `livello ${p.livello}` })),
    el('p', { testo: p.descrizione })));
}
for (const k of d.killstreak) {
  $('#elenco-streak').append(el('li', {},
    el('div', { class: 'uccisioni' }, `${k.uccisioni}`, el('small', { testo: 'uccisioni' })),
    el('div', {}, el('h4', { testo: k.nome }), el('p', { testo: k.descrizione + (k.durata ? ` (${k.durata} s)` : '') })),
    el('span', { class: 'livello', testo: `livello ${k.livello}` })));
}
