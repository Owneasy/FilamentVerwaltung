'use strict';
// Filament-Verwaltung – Handy-Ansicht. Spricht dieselbe REST-API wie die Windows-App (/api, Token im Header).

const ZUSTAND = ['Verbinde …', 'Offline', 'Bereit', 'Druckt', 'Pausiert', 'Fertig', 'Abgebrochen', 'Fehler'];
const ZUSTAND_FARBE = ['grau', 'rot', 'gruen', 'blau', 'gelb', 'gruen', 'rot', 'rot'];
const DRUCKT = 3, PAUSIERT = 4, FERTIG = 5, OFFLINE = 1;
const ICON = {
  kamera: '<svg viewBox="0 0 24 24"><path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"/><circle cx="12" cy="13" r="4"/></svg>',
  drucken: '<svg viewBox="0 0 24 24"><path d="M6 9V3h12v6M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2M6 14h12v7H6z"/></svg>',
  qr: '<svg viewBox="0 0 24 24"><path d="M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h3v3h-3zM20 14v.01M14 20h.01M17 17h4v4h-4"/></svg>'
};

let token = localStorage.getItem('fv-token') || '';
let seitenTimer = null;
let zaehlTimer = null;
let kameraLauf = null;
let scanLauf = null;
let druckerListe = [];
let spulenSuche = '';
let nurVorhanden = true;

const $ = s => document.querySelector(s);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const pause = ms => new Promise(r => setTimeout(r, ms));

// ------------------------------------------------------------------ Server

class AnmeldeFehler extends Error {}

async function api(pfad, optionen = {}) {
  const kopf = { Authorization: 'Bearer ' + token };
  if (optionen.body !== undefined) kopf['Content-Type'] = 'application/json';
  let antwort;
  try {
    antwort = await fetch('/api/' + pfad, { ...optionen, headers: kopf, cache: 'no-store' });
  } catch {
    // Netzwerkfehler – oder Cloudflare Access will eine neue Anmeldung (Umleitung auf eine andere Domain)
    verbunden(false);
    throw new Error('Keine Verbindung zum Server. Seite neu laden (ggf. erneut anmelden).');
  }
  verbunden(true);
  if (antwort.status === 401) throw new AnmeldeFehler('Das Token wurde abgelehnt.');
  if (!antwort.ok) {
    const text = await antwort.text();
    throw new Error(text && !text.trimStart().startsWith('<') ? text : `Der Server meldet Fehler ${antwort.status}.`);
  }
  return antwort;
}
const holen = async pfad => (await api(pfad)).json();
const schicken = (pfad, methode, daten) => api(pfad, { method: methode, body: daten === undefined ? undefined : JSON.stringify(daten) });

function verbunden(ok) {
  $('#verbindung').className = 'status ' + (ok ? 'ok' : 'weg');
}

function fehler(e) {
  if (e instanceof AnmeldeFehler) return anmeldenZeigen(e.message);
  toast(e.message || String(e), true);
}

// ------------------------------------------------------------------ Anzeige-Helfer

function titel(t) { $('#titel').textContent = t; document.title = t + ' – Filament'; }
function inhalt(html) { $('#inhalt').innerHTML = html; }

let toastTimer;
function toast(text, istFehler = false) {
  const t = $('#toast');
  t.textContent = text;
  t.className = 'toast' + (istFehler ? ' fehler' : '');
  t.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.hidden = true, istFehler ? 6000 : 2500);
}

function blattOeffnen(html) {
  $('#blattInhalt').innerHTML = html;
  $('#blatt').hidden = false;
}
function blattSchliessen() {
  kameraStopp();
  scanStopp();
  $('#blatt').hidden = true;
  $('#blattInhalt').innerHTML = '';
}

function dauer(sek) {
  if (!sek || sek <= 0) return '';
  const h = Math.floor(sek / 3600), m = Math.floor(sek % 3600 / 60);
  if (h > 0) return `${h} h ${String(m).padStart(2, '0')} min`;
  return m > 0 ? `${m} min` : `${sek} s`;
}
const prozent = x => Math.round((x || 0) * 100) + ' %';
const zahl = x => Math.round(x || 0).toLocaleString('de-DE');
const datumZeit = s => new Date(s).toLocaleString('de-DE', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
const spulenText = f => [f.hersteller, f.material, f.farbe].filter(Boolean).join(' ');

function zahlBuchen(n) {
  const z = $('#zahlBuchen');
  z.textContent = n;
  z.hidden = !n;
}

// ------------------------------------------------------------------ Start, Anmeldung, Navigation

function anmeldenZeigen(meldung) {
  stoppen();
  titel('Anmelden');
  $('#leiste').hidden = true;
  inhalt(`<section class="karte">
      <h2>Mit dem Server verbinden</h2>
      <p class="leise">Zugangs-Token eingeben – es steht in <b>token.txt</b> im Datenordner des Servers bzw. im Container-Log.
      Das Handy merkt es sich.</p>
      <label>Zugangs-Token<input id="tokenFeld" type="password" autocomplete="current-password"></label>
      ${meldung ? `<p class="fehler">${esc(meldung)}</p>` : ''}
      <div class="knoepfe"><button class="primaer" id="anmelden">Anmelden</button></div>
    </section>`);
  $('#anmelden').onclick = () => {
    token = $('#tokenFeld').value.trim();
    localStorage.setItem('fv-token', token);
    starten();
  };
  $('#tokenFeld').onkeydown = e => { if (e.key === 'Enter') $('#anmelden').click(); };
}

async function starten() {
  if (!token) return anmeldenZeigen();
  try {
    const design = (await holen('einstellungen/Design')).wert || 'Hell';
    document.documentElement.dataset.design = design === 'Dunkel' ? 'dunkel' : design === 'Hell' ? 'hell' : '';
  } catch (e) {
    return e instanceof AnmeldeFehler ? anmeldenZeigen(e.message) : inhalt(`<p class="leer">${esc(e.message)}</p>`);
  }
  $('#leiste').hidden = false;
  druckendenZaehlen();
  if (!zaehlTimer) zaehlTimer = setInterval(druckendenZaehlen, 15000);
  navigieren();
}

async function druckendenZaehlen() {
  try { zahlBuchen((await holen('druckenden')).length); } catch { /* Anzeige bleibt */ }
}

function stoppen() {
  clearInterval(seitenTimer);
  seitenTimer = null;
  blattSchliessen();
}

function navigieren() {
  if (!token) return anmeldenZeigen();
  stoppen();
  const [name, arg] = (location.hash.slice(1) || 'drucker').split('/');
  document.querySelectorAll('.leiste button').forEach(b =>
    b.classList.toggle('aktiv', b.dataset.seite === (name === 'spule' ? 'spulen' : name)));
  window.scrollTo(0, 0);
  if (name === 'buchen') buchenSeite();
  else if (name === 'spulen') spulenSeite();
  else if (name === 'spule') spuleSeite(decodeURIComponent(arg || ''));
  else druckerSeite();
}

window.addEventListener('hashchange', navigieren);
document.addEventListener('click', e => {
  const z = e.target.closest('[data-seite],[data-schliessen],[data-kamera],[data-buchen],[data-verwerfen]');
  if (!z) return;
  if (z.dataset.seite) { if (location.hash === '#' + z.dataset.seite) navigieren(); else location.hash = z.dataset.seite; }
  else if (z.hasAttribute('data-schliessen')) blattSchliessen();
  else if (z.dataset.kamera) kameraZeigen(+z.dataset.kamera);
  else if (z.dataset.buchen) buchenFormular(+z.dataset.buchen);
  else if (z.dataset.verwerfen) verwerfen(+z.dataset.verwerfen);
});
// Im Hintergrund nichts abfragen (spart Akku und Datenvolumen); beim Zurückkehren neu laden –
// ein offenes Formular (z. B. Buchen) bleibt dabei stehen, nur Kamera und Scanner werden beendet
document.addEventListener('visibilitychange', () => {
  if (document.hidden) {
    clearInterval(seitenTimer);
    seitenTimer = null;
    if (kameraLauf || scanLauf) blattSchliessen();
  } else if (token && !$('#leiste').hidden && $('#blatt').hidden) {
    navigieren();
  }
});

// ------------------------------------------------------------------ Drucker

function phaseText(s) {
  if (s.phase) return s.phase;
  if (!(s.zustand === DRUCKT || s.zustand === PAUSIERT) || s.fortschritt > 0.005) return '';
  const heizt = (s.temperaturen || []).filter(t => t.heiztAuf && t.ist < t.soll).map(t => t.name);
  if (heizt.includes('Düse')) return 'Düse heizt auf';
  if (heizt.includes('Bett')) return 'Heizbett heizt auf';
  return heizt.length ? heizt[0] + ' heizt auf' : '';
}

function druckerKarte(d, s) {
  if (!d.aktiv) {
    return `<section class="karte"><div class="zeile"><h2>${esc(d.name)}</h2><span class="badge grau">Nicht überwacht</span></div></section>`;
  }
  s = s || { zustand: 0, temperaturen: [] };
  const z = s.zustand, laeuft = z === DRUCKT || z === PAUSIERT;
  const teile = [];
  if (laeuft) {
    teile.push(prozent(s.fortschritt));
    if (s.restSekunden) teile.push('noch ' + dauer(s.restSekunden));
  } else if (z === FERTIG && s.dauerSekunden) {
    teile.push('fertig nach ' + dauer(s.dauerSekunden));
  }
  if (s.grammGeplant) teile.push(zahl(s.grammGeplant) + ' g laut Slicer');
  const werte = (z !== OFFLINE ? s.temperaturen || [] : []).map(t =>
    `<span><small>${esc(t.name)}</small> <b class="${t.heiztAuf ? 'gelb-text' : ''}">${t.heiztAuf ? (t.ist < t.soll ? '↑ ' : '↓ ') : ''}${esc(t.wertText)}</b></span>`);
  if (laeuft && s.schicht > 0) werte.push(`<span><small>Schicht</small> <b>${s.schicht}${s.schichtenGesamt ? ' / ' + s.schichtenGesamt : ''}</b></span>`);
  const phase = phaseText(s);
  return `<section class="karte">
      <div class="zeile">
        <div><h2>${esc(d.name)}</h2><small class="leise">${esc(d.typ)}</small></div>
        <span class="badge ${ZUSTAND_FARBE[z] || 'grau'}">${ZUSTAND[z] || '?'}</span>
      </div>
      <div class="datei">${esc(s.datei || (z === OFFLINE ? 'Nicht erreichbar' : 'Kein Druckauftrag'))}</div>
      ${laeuft || z === FERTIG ? `<div class="balken"><div style="width:${(z === FERTIG ? 1 : s.fortschritt) * 100}%"></div></div>` : ''}
      ${teile.length ? `<div class="leise">${teile.join(' · ')}</div>` : ''}
      ${phase ? `<div class="phase">${esc(phase)}</div>` : ''}
      ${werte.length ? `<div class="werte">${werte.join('')}</div>` : ''}
      ${z === OFFLINE && s.meldung ? `<div class="fehler">${esc(s.meldung)}</div>` : ''}
      ${d.typ === 'Bambu' ? `<button class="knopf" data-kamera="${d.id}">${ICON.kamera} Kamera</button>` : ''}
    </section>`;
}

async function druckerSeite() {
  titel('Drucker');
  const aktualisieren = async () => {
    try {
      const [drucker, live] = await Promise.all([holen('drucker'), holen('drucker/live')]);
      druckerListe = drucker;
      if (!drucker.length) return inhalt('<p class="leer">Noch kein Drucker angelegt.<br><small>Drucker legst du in der Windows-App unter „Drucker“ an.</small></p>');
      const status = Object.fromEntries(live.map(l => [l.druckerId, l.status]));
      inhalt(drucker.map(d => druckerKarte(d, status[d.id])).join(''));
    } catch (e) { fehler(e); }
  };
  await aktualisieren();
  if (!$('#leiste').hidden && !document.hidden) seitenTimer = setInterval(aktualisieren, 5000);
}

function kameraZeigen(id) {
  const d = druckerListe.find(x => x.id === id);
  blattOeffnen(`<div class="zeile"><h2>${esc(d ? d.name : 'Drucker')} – Kamera</h2><button class="knopf" data-schliessen>Schließen</button></div>
    <div class="kamera"><img id="kameraBild" alt=""><div id="kameraStatus" class="leise">Verbinde mit Kamera …</div></div>`);
  const lauf = { aktiv: true, url: null };
  kameraLauf = lauf;
  (async () => {
    while (lauf.aktiv) {
      try {
        const antwort = await api(`drucker/${id}/kamera`);
        if (!lauf.aktiv) break;
        if (antwort.status === 204) {
          $('#kameraStatus').textContent = decodeURIComponent(antwort.headers.get('X-Kamera-Status') || 'Warte auf Bild …');
        } else {
          const url = URL.createObjectURL(await antwort.blob());
          if (!lauf.aktiv) { URL.revokeObjectURL(url); break; }
          $('#kameraBild').src = url;
          if (lauf.url) URL.revokeObjectURL(lauf.url);
          lauf.url = url;
          $('#kameraStatus').textContent = 'Live · ' + new Date().toLocaleTimeString('de-DE');
        }
      } catch (e) {
        if (!lauf.aktiv) break;
        $('#kameraStatus').textContent = e.message;
        await pause(2000);
      }
      await pause(1000);
    }
  })();
}

function kameraStopp() {
  if (!kameraLauf) return;
  kameraLauf.aktiv = false;
  if (kameraLauf.url) URL.revokeObjectURL(kameraLauf.url);
  kameraLauf = null;
}

// ------------------------------------------------------------------ Fertige Drucke buchen

async function buchenSeite() {
  titel('Fertige Drucke');
  const laden = async () => {
    try {
      const enden = await holen('druckenden');
      zahlBuchen(enden.length);
      if (!enden.length) return inhalt('<p class="leer">Keine Drucke zum Buchen.<br><small>Fertige oder abgebrochene Drucke erscheinen hier automatisch.</small></p>');
      inhalt(enden.map(e => `<section class="karte">
          <div class="zeile">
            <div><h2>${esc(e.datei || 'Druck')}</h2><small class="leise">${esc(e.druckerName)} · ${datumZeit(e.zeitpunkt)}</small></div>
            <span class="badge ${e.abgebrochen ? 'rot' : 'gruen'}">${e.abgebrochen ? 'Abgebrochen bei ' + prozent(e.fortschritt) : 'Fertig'}</span>
          </div>
          <div class="knoepfe">
            <button class="primaer" data-buchen="${e.id}">Buchen …</button>
            <button class="knopf" data-verwerfen="${e.id}">Verwerfen</button>
          </div>
        </section>`).join(''));
    } catch (e) { fehler(e); }
  };
  await laden();
  if (!$('#leiste').hidden && !document.hidden) seitenTimer = setInterval(laden, 10000);
}

async function buchenFormular(id) {
  let vorschlag, spulen;
  try {
    [vorschlag, spulen] = await Promise.all([holen(`druckenden/${id}/vorschlag`), holen('filament')]);
  } catch (e) { navigieren(); return fehler(e); }
  const auswahl = spulen.filter(f => f.restgewicht > 0 || f.id === vorschlag.filamentId).sort((a, b) => a.code.localeCompare(b.code));
  blattOeffnen(`<h2>Druck buchen</h2>
    <label>Bezeichnung<input id="bBezeichnung" value="${esc(vorschlag.bezeichnung)}"></label>
    <label>Spule<select id="bSpule">
      <option value="0">– Spule wählen –</option>
      ${auswahl.map(f => `<option value="${f.id}" ${f.id === vorschlag.filamentId ? 'selected' : ''}>${esc(f.code)} · ${esc(spulenText(f))} (${zahl(f.restgewicht)} g)</option>`).join('')}
    </select></label>
    <label>Verbrauch in Gramm<input id="bGramm" type="number" inputmode="numeric" min="0" value="${vorschlag.gramm}"></label>
    ${vorschlag.druckzeit ? `<p class="leise">Druckzeit: ${dauer(vorschlag.druckzeit)}</p>` : ''}
    <p class="leise">${esc(vorschlag.notiz)}</p>
    <div class="knoepfe"><button class="knopf" data-schliessen>Abbrechen</button><button class="primaer" id="bOk">Buchen</button></div>`);
  $('#bOk').onclick = async () => {
    const filamentId = +$('#bSpule').value;
    const gramm = parseInt($('#bGramm').value, 10);
    if (!filamentId) return toast('Bitte eine Spule wählen.', true);
    if (!(gramm >= 0)) return toast('Bitte den Verbrauch in Gramm eingeben.', true);
    $('#bOk').disabled = true;
    try {
      await schicken(`druckenden/${id}/buchen`, 'POST', { ...vorschlag, filamentId, gramm, bezeichnung: $('#bBezeichnung').value.trim() });
      blattSchliessen();
      toast(`Gebucht: ${gramm} g`);
      druckendenZaehlen();
      navigieren();
    } catch (e) {
      $('#bOk').disabled = false;
      if (e.message.includes('schon')) { blattSchliessen(); navigieren(); }
      fehler(e);
    }
  };
}

async function verwerfen(id) {
  if (!confirm('Diesen Druck verwerfen? Es wird nichts gebucht.')) return;
  try {
    await schicken(`druckenden/${id}`, 'DELETE');
  } catch (e) {
    if (!e.message.includes('404')) fehler(e);
  }
  druckendenZaehlen();
  navigieren();
}

// ------------------------------------------------------------------ Spulen

function spulenKarte(f) {
  const anteil = f.gewicht > 0 ? f.restgewicht / f.gewicht : 0;
  const klasse = f.restgewicht <= 0 ? 'leer' : anteil < 0.15 ? 'knapp' : '';
  return `<a class="karte spule" href="#spule/${encodeURIComponent(f.code)}">
      <div><b>${esc(f.code)}</b> <span class="leise">${esc(spulenText(f))}</span>${f.lagerort ? `<br><small class="leise">${esc(f.lagerort)}</small>` : ''}</div>
      <div class="rest">${zahl(f.restgewicht)} g<br><small class="leise">${prozent(anteil)}</small></div>
      <div class="balken ${klasse}"><div style="width:${Math.min(100, anteil * 100)}%"></div></div>
    </a>`;
}

async function spulenSeite() {
  titel('Spulen');
  inhalt(`<div class="suche">
      <input id="suche" type="search" placeholder="ID, Hersteller, Farbe, Lagerort …" value="${esc(spulenSuche)}">
      <button class="knopf" id="scannen" title="QR-Code scannen">${ICON.qr}</button>
    </div>
    <div class="zeile liste-kopf"><label class="schalter"><input type="checkbox" id="nurVorhanden" ${nurVorhanden ? 'checked' : ''}> nur mit Restbestand</label>
      <button class="knopf" id="etiketten">${ICON.drucken} Etiketten</button></div>
    <div id="liste"><p class="leer">Lade …</p></div>`);
  let spulen;
  try { spulen = await holen('filament'); } catch (e) { return fehler(e); }
  const gefiltert = () => {
    const q = spulenSuche.toLowerCase();
    return spulen.filter(f => (!nurVorhanden || f.restgewicht > 0) &&
      (!q || [f.code, f.hersteller, f.material, f.farbe, f.lagerort, f.notiz].some(x => (x || '').toLowerCase().includes(q))));
  };
  const zeigen = () => {
    const treffer = gefiltert();
    $('#liste').innerHTML = treffer.length ? treffer.map(spulenKarte).join('') : '<p class="leer">Keine passenden Spulen.</p>';
  };
  $('#suche').oninput = e => { spulenSuche = e.target.value; zeigen(); };
  $('#suche').onkeydown = e => {
    // Enter mit einer exakten ID (z. B. vom Hand-Scanner) öffnet die Spule direkt
    if (e.key !== 'Enter') return;
    const f = spulen.find(x => x.code.toLowerCase() === spulenSuche.trim().toLowerCase());
    if (f) location.hash = 'spule/' + encodeURIComponent(f.code);
  };
  $('#nurVorhanden').onchange = e => { nurVorhanden = e.target.checked; zeigen(); };
  $('#scannen').onclick = () => scannen(spulen);
  $('#etiketten').onclick = () => etikettenDialog(gefiltert(), new Set());
  zeigen();
}

async function scannen(spulen) {
  if (!window.isSecureContext) return toast('Die Kamera geht im Browser nur über https – bitte die Cloudflare-Adresse benutzen.', true);
  let stream;
  try {
    stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
  } catch (e) {
    return toast('Kamera nicht verfügbar: ' + e.message, true);
  }
  blattOeffnen(`<div class="zeile"><h2>QR-Code scannen</h2><button class="knopf" data-schliessen>Abbrechen</button></div>
    <video id="scanVideo" playsinline muted></video>
    <p class="leise">QR-Code auf der Spule ins Bild halten.</p>`);
  const lauf = { aktiv: true, stream };
  scanLauf = lauf;
  const video = $('#scanVideo');
  video.srcObject = stream;
  try { await video.play(); } catch { /* startet trotzdem */ }
  let erkennen;
  try {
    erkennen = await qrErkenner();
  } catch (e) {
    blattSchliessen();
    return toast(e.message, true);
  }
  while (lauf.aktiv) {
    try {
      const gelesen = await erkennen(video);
      if (gelesen && lauf.aktiv) {
        const wert = gelesen.trim();
        blattSchliessen();
        const f = spulen.find(x => x.code.toLowerCase() === wert.toLowerCase());
        if (f) location.hash = 'spule/' + encodeURIComponent(f.code);
        else { spulenSuche = wert; $('#suche').value = wert; $('#suche').dispatchEvent(new Event('input')); toast(`Keine Spule mit der ID „${wert}“.`, true); }
        return;
      }
    } catch { /* Bild noch nicht bereit */ }
    await pause(250);
  }
}

// QR-Erkennung: eingebaut, wo vorhanden (Chrome/Edge auf Android), sonst jsQR (iPhone/Safari, Firefox) –
// die Bibliothek wird erst beim ersten Scannen geladen. Liefert eine Funktion video -> Text oder null.
let jsqrLaden = null;
function jsqrHolen() {
  if (window.jsQR) return Promise.resolve(window.jsQR);
  return jsqrLaden ??= new Promise((ok, fehlschlag) => {
    const s = document.createElement('script');
    s.src = 'lib/jsQR.js';
    s.onload = () => ok(window.jsQR);
    s.onerror = () => { jsqrLaden = null; fehlschlag(new Error('Die QR-Erkennung konnte nicht geladen werden.')); };
    document.head.appendChild(s);
  });
}

async function qrErkenner() {
  if ('BarcodeDetector' in window) {
    try {
      if ((await BarcodeDetector.getSupportedFormats()).includes('qr_code')) {
        const erkenner = new BarcodeDetector({ formats: ['qr_code'] });
        return async video => (await erkenner.detect(video))[0]?.rawValue || null;
      }
    } catch { /* weiter mit jsQR */ }
  }
  const jsQR = await jsqrHolen();
  const leinwand = document.createElement('canvas');
  const ctx = leinwand.getContext('2d', { willReadFrequently: true });
  return async video => {
    if (!video.videoWidth) return null;
    // Auf höchstens 640 px verkleinern – reicht für Etiketten und schont den Akku
    const f = Math.min(1, 640 / Math.max(video.videoWidth, video.videoHeight));
    leinwand.width = Math.round(video.videoWidth * f);
    leinwand.height = Math.round(video.videoHeight * f);
    ctx.drawImage(video, 0, 0, leinwand.width, leinwand.height);
    const bild = ctx.getImageData(0, 0, leinwand.width, leinwand.height);
    return jsQR(bild.data, bild.width, bild.height, { inversionAttempts: 'dontInvert' })?.data || null;
  };
}
function scanStopp() {
  if (!scanLauf) return;
  scanLauf.aktiv = false;
  scanLauf.stream.getTracks().forEach(t => t.stop());
  scanLauf = null;
}

async function spuleSeite(code) {
  titel(code || 'Spule');
  let f;
  try { f = (await holen('filament')).find(x => x.code.toLowerCase() === code.toLowerCase()); } catch (e) { return fehler(e); }
  if (!f) return inhalt(`<p class="leer">Keine Spule mit der ID „${esc(code)}“.</p>`);
  const anteil = f.gewicht > 0 ? f.restgewicht / f.gewicht : 0;
  const zeilen = [
    ['Lagerort', f.lagerort], ['Durchmesser', f.durchmesser ? String(f.durchmesser).replace('.', ',') + ' mm' : ''],
    ['Leerspule', f.spulengewicht ? zahl(f.spulengewicht) + ' g' : 'nicht eingetragen'], ['Temperaturen', f.temperaturen],
    ['Trocknung', f.trocknung], ['Notiz', f.notiz]
  ].filter(z => z[1]);
  inhalt(`<section class="karte">
      <h2>${esc(spulenText(f)) || esc(f.code)}</h2>
      <div class="gross">${zahl(f.restgewicht)} g <small>von ${zahl(f.gewicht)} g · ${prozent(anteil)}</small></div>
      <div class="balken ${f.restgewicht <= 0 ? 'leer' : anteil < 0.15 ? 'knapp' : ''}"><div style="width:${Math.min(100, anteil * 100)}%"></div></div>
      ${zeilen.length ? `<dl>${zeilen.map(z => `<dt>${z[0]}</dt><dd>${esc(z[1])}</dd>`).join('')}</dl>` : ''}
    </section>
    <section class="karte">
      <h2>Restgewicht aktualisieren</h2>
      <div class="modi">
        <label><input type="radio" name="modus" value="gewogen" ${f.spulengewicht ? 'checked' : ''}> Gewogen – Gesamtgewicht inkl. Leerspule</label>
        <label><input type="radio" name="modus" value="verbrauch" ${f.spulengewicht ? '' : 'checked'}> Verbrauch abziehen</label>
        <label><input type="radio" name="modus" value="direkt"> Restgewicht direkt</label>
      </div>
      <input id="wert" type="number" inputmode="numeric" min="0">
      <div id="vorschau" class="vorschau"></div>
      <label>Wofür? (optional, erscheint im Verlauf)<input id="wBezeichnung" placeholder="z. B. Halterung, Fehldruck …"></label>
      <div class="knoepfe"><button class="primaer" id="wOk">Übernehmen</button></div>
    </section>
    <button class="knopf breit" id="etikett">${ICON.drucken} Etikett drucken</button>`);
  $('#etikett').onclick = () => etikettenDialog([f], new Set([f.id]));

  const modus = () => document.querySelector('input[name=modus]:checked').value;
  const neuerRest = () => {
    const w = parseInt($('#wert').value, 10) || 0;
    const r = modus() === 'verbrauch' ? f.restgewicht - w : modus() === 'gewogen' ? w - f.spulengewicht : w;
    return Math.max(0, r);
  };
  const vorschau = () => {
    const n = neuerRest();
    $('#vorschau').textContent = `Neues Restgewicht: ${zahl(n)} g (${prozent(f.gewicht > 0 ? n / f.gewicht : 0)})` +
      (modus() === 'gewogen' && !f.spulengewicht ? ' – Achtung: kein Leerspulengewicht eingetragen' : '');
  };
  const vorbelegen = () => {
    $('#wert').value = modus() === 'verbrauch' ? '' : modus() === 'gewogen' ? f.restgewicht + f.spulengewicht : f.restgewicht;
    vorschau();
  };
  document.querySelectorAll('input[name=modus]').forEach(r => r.onchange = () => { vorbelegen(); $('#wert').focus(); $('#wert').select(); });
  $('#wert').oninput = vorschau;
  vorbelegen();

  $('#wOk').onclick = async () => {
    if ($('#wert').value === '') return toast('Bitte einen Wert eingeben.', true);
    const neu = neuerRest();
    $('#wOk').disabled = true;
    try {
      await schicken(`filament/${f.id}/restgewicht`, 'POST', {
        neuesRestgewicht: neu,
        art: modus() === 'verbrauch' ? 'Druck' : 'Wiegen',
        bezeichnung: $('#wBezeichnung').value.trim()
      });
      toast(`Gespeichert: ${zahl(neu)} g`);
      spuleSeite(f.code);
    } catch (e) {
      $('#wOk').disabled = false;
      fehler(e);
    }
  };
}


// ------------------------------------------------------------------ Etiketten drucken
// Gleiche Formate (gleiche Reihenfolge, Einstellung "Etiketten.Format" wird mit der App geteilt) und gleicher
// Aufbau wie in der Windows-App (Etiketten.vb): QR-Code links, Angaben rechts. Alle Maße in mm.

const ETIKETT_FORMATE = [
  { name: 'A4-Bogen · 3 × 8 · 70 × 37 mm (z. B. Avery 3474)', breite: 70, hoehe: 37, spalten: 3, zeilen: 8, randLinks: 0, randOben: 0.5, abstandX: 0, abstandY: 0 },
  { name: 'A4-Bogen · 3 × 7 · 70 × 42,3 mm (z. B. Avery 3652)', breite: 70, hoehe: 42.3, spalten: 3, zeilen: 7, randLinks: 0, randOben: 0.45, abstandX: 0, abstandY: 0 },
  { name: 'A4-Bogen · 2 × 7 · 99,1 × 38,1 mm (z. B. Avery L7163)', breite: 99.1, hoehe: 38.1, spalten: 2, zeilen: 7, randLinks: 4.65, randOben: 15.15, abstandX: 2.5, abstandY: 0 },
  { name: 'Etikettendrucker · 62 × 29 mm (z. B. Brother DK-11209)', breite: 62, hoehe: 29, einzeln: true },
  { name: 'Etikettendrucker · 54 × 25 mm (z. B. Dymo 11352)', breite: 54, hoehe: 25, einzeln: true }
];

// Farbpunkt wie in der App (Theme.FarbeZuName)
const FARBNAMEN = [['schwarz', '#111827'], ['black', '#111827'], ['weiß', '#FFFFFF'], ['weiss', '#FFFFFF'], ['white', '#FFFFFF'],
  ['natur', '#F5F0E1'], ['transparent', '#E0F2FE'], ['klar', '#E0F2FE'], ['dunkelgrau', '#4B5563'], ['grau', '#9CA3AF'], ['grey', '#9CA3AF'],
  ['gray', '#9CA3AF'], ['silber', '#C0C4CC'], ['gold', '#D4A72C'], ['dunkelblau', '#1E3A8A'], ['hellblau', '#7DD3FC'], ['blau', '#2563EB'],
  ['blue', '#2563EB'], ['türkis', '#14B8A6'], ['tuerkis', '#14B8A6'], ['dunkelgrün', '#166534'], ['hellgrün', '#86EFAC'], ['grün', '#16A34A'],
  ['gruen', '#16A34A'], ['green', '#16A34A'], ['gelb', '#FACC15'], ['yellow', '#FACC15'], ['orange', '#F97316'], ['dunkelrot', '#991B1B'],
  ['rot', '#DC2626'], ['red', '#DC2626'], ['rosa', '#F9A8D4'], ['pink', '#EC4899'], ['magenta', '#D946EF'], ['lila', '#9333EA'],
  ['violett', '#7C3AED'], ['purple', '#9333EA'], ['braun', '#92400E'], ['brown', '#92400E'], ['holz', '#B07A4A'], ['beige', '#E7D8B9']];
const farbeZuName = name => (FARBNAMEN.find(([wort]) => (name || '').toLowerCase().includes(wort)) || [])[1];

function etikettenSeiten(anzahl, fmt, start) {
  if (!anzahl) return 0;
  return fmt.einzeln ? anzahl : Math.ceil((anzahl + start - 1) / (fmt.spalten * fmt.zeilen));
}

async function etikettenDialog(spulen, ausgewaehlt) {
  let index = 0;
  try { index = parseInt((await holen('einstellungen/Etiketten.Format')).wert, 10); } catch { /* Standard */ }
  if (!(index >= 0 && index < ETIKETT_FORMATE.length)) index = 0;
  blattOeffnen(`<div class="zeile"><h2>Etiketten drucken</h2><button class="knopf" data-schliessen>Schließen</button></div>
    <label>Format<select id="eFormat">${ETIKETT_FORMATE.map((f, i) => `<option value="${i}" ${i === index ? 'selected' : ''}>${esc(f.name)}</option>`).join('')}</select></label>
    <label id="eStartFeld">Erstes freies Etikett auf dem Bogen (1 = oben links)<input id="eStart" type="number" inputmode="numeric" min="1" value="1"></label>
    <label class="schalter"><input type="checkbox" id="eRahmen"> Rahmen um jedes Etikett (zum Ausschneiden)</label>
    ${spulen.length > 1 ? `<div class="zeile auswahl-kopf"><b>Spulen</b><button class="knopf" id="eAlle">Alle auswählen</button></div>
      <div class="auswahl">${spulen.map(f => `<label class="schalter"><input type="checkbox" value="${f.id}" ${ausgewaehlt.has(f.id) ? 'checked' : ''}>
        <span><b>${esc(f.code)}</b> <span class="leise">${esc(spulenText(f))}</span></span></label>`).join('')}</div>` : ''}
    <p class="leise" id="eInfo"></p>
    <div class="knoepfe"><button class="primaer" id="eDrucken">Drucken</button></div>`);

  const format = () => ETIKETT_FORMATE[+$('#eFormat').value];
  const auswahl = () => spulen.length > 1
    ? spulen.filter(f => document.querySelector(`.auswahl input[value="${f.id}"]`).checked) : spulen;
  const start = () => Math.max(1, Math.min(parseInt($('#eStart').value, 10) || 1, format().einzeln ? 1 : format().spalten * format().zeilen));
  const info = () => {
    const fmt = format(), n = auswahl().length;
    $('#eStartFeld').hidden = !!fmt.einzeln;
    const seiten = etikettenSeiten(n, fmt, start());
    $('#eInfo').textContent = n ? `${n} Etikett${n === 1 ? '' : 'en'} · ${seiten} ${fmt.einzeln ? 'Etikett' + (seiten === 1 ? '' : 'en') + ' im Drucker' : (seiten === 1 ? 'Bogen' : 'Bögen')}` : 'Keine Spule ausgewählt.';
  };
  $('#eFormat').onchange = info;
  $('#eStart').oninput = info;
  if ($('#eAlle')) {
    $('#eAlle').onclick = () => {
      const boxen = [...document.querySelectorAll('.auswahl input')];
      const alle = boxen.every(b => b.checked);
      boxen.forEach(b => b.checked = !alle);
      info();
    };
    document.querySelectorAll('.auswahl input').forEach(b => b.onchange = info);
  }
  info();

  $('#eDrucken').onclick = async () => {
    const liste = auswahl();
    if (!liste.length) return toast('Bitte mindestens eine Spule auswählen.', true);
    $('#eDrucken').disabled = true;
    try {
      schicken('einstellungen/Etiketten.Format', 'PUT', { wert: String($('#eFormat').value) }).catch(() => {});
      await etikettenDrucken(liste, format(), start(), $('#eRahmen').checked);
    } catch (e) { fehler(e); }
    $('#eDrucken').disabled = false;
  };
}

function qrPfad(zeilen) {
  // Zusammenhängende Module einer Zeile als ein Rechteck; minimal höher, damit keine feinen Lücken entstehen
  let d = '';
  zeilen.forEach((zeile, y) => {
    for (let x = 0; x < zeile.length; x++) {
      if (zeile[x] !== '1') continue;
      let l = 1;
      while (zeile[x + l] === '1') l++;
      d += `M${x} ${y}h${l + 0.04}v1.04h-${l + 0.04}z`;
      x += l - 1;
    }
  });
  return d;
}

function etikettHtml(f, qr, fmt, x, y, rahmen) {
  const H = fmt.hoehe, W = fmt.breite;
  const pad = Math.max(1.8, H * 0.07), skala = H / 37;
  const qrGroesse = H - 2 * pad;
  const tx = pad * 0.6 + qrGroesse + pad * 0.8;
  const farbe = farbeZuName(f.farbe);
  const details = [`Ø ${Number(f.durchmesser || 1.75).toFixed(2).replace('.', ',')} mm`, `${zahl(f.gewicht)} g`];
  if (f.temperaturen) details.push(f.temperaturen);
  const kauf = f.kaufdatum ? new Date(f.kaufdatum) : null;
  const unten = [f.lagerort, kauf && kauf.getFullYear() > 1 ? `gekauft ${String(kauf.getMonth() + 1).padStart(2, '0')}/${kauf.getFullYear()}` : '']
    .filter(Boolean).join(' · ');
  const punkt = 8 * skala * 0.352778 * 1.33 * 0.62; // ~62 % der Zeilenhöhe der normalen Schrift, in mm
  return `<div class="etikett${rahmen ? ' rahmen' : ''}" style="left:${x}mm;top:${y}mm;width:${W}mm;height:${H}mm">
      <svg viewBox="0 0 ${qr.length} ${qr.length}" style="left:${pad * 0.6}mm;top:${pad}mm;width:${qrGroesse}mm;height:${qrGroesse}mm"><path d="${qrPfad(qr)}"/></svg>
      <div class="text" style="left:${tx}mm;top:${pad * 0.9}mm;width:${W - pad - tx}mm;bottom:${pad * 0.5}mm">
        <div style="font-size:${13 * skala}pt;font-weight:600">${esc(f.code)}</div>
        <div style="font-size:${8.5 * skala}pt;font-weight:600;margin-top:${0.4 * skala}mm">${esc(f.hersteller)}</div>
        <div style="font-size:${8 * skala}pt">${farbe ? `<span class="punkt" style="width:${punkt}mm;height:${punkt}mm;background:${farbe}"></span>` : ''}${esc([f.material, f.farbe].filter(Boolean).join(' · '))}</div>
        <div class="klein" style="font-size:${6.5 * skala}pt;margin-top:${0.3 * skala}mm">${esc(details.join(' · '))}</div>
        ${unten ? `<div class="klein" style="font-size:${6.5 * skala}pt">${esc(unten)}</div>` : ''}
      </div>
    </div>`;
}

async function etikettenDrucken(liste, fmt, start, rahmen) {
  const qr = await (await schicken('qr', 'POST', liste.map(f => f.code))).json();
  const seiten = [];
  if (fmt.einzeln) {
    liste.forEach(f => seiten.push(etikettHtml(f, qr[f.code], fmt, 0, 0, rahmen)));
  } else {
    const proBogen = fmt.spalten * fmt.zeilen;
    let platz = start - 1, seite = '';
    liste.forEach(f => {
      if (platz === proBogen) { seiten.push(seite); seite = ''; platz = 0; }
      const sp = platz % fmt.spalten, ze = Math.floor(platz / fmt.spalten);
      seite += etikettHtml(f, qr[f.code], fmt, fmt.randLinks + sp * (fmt.breite + fmt.abstandX), fmt.randOben + ze * (fmt.hoehe + fmt.abstandY), rahmen);
      platz++;
    });
    if (seite) seiten.push(seite);
  }
  const breite = fmt.einzeln ? fmt.breite : 210, hoehe = fmt.einzeln ? fmt.hoehe : 297;
  let druck = $('#druck');
  if (!druck) { druck = document.createElement('div'); druck.id = 'druck'; document.body.appendChild(druck); }
  druck.innerHTML = `<style>@page { size: ${breite}mm ${hoehe}mm; margin: 0; }</style>` +
    seiten.map(s => `<div class="druckseite" style="width:${breite}mm;height:${hoehe}mm">${s}</div>`).join('');
  await pause(100);   // Layout fertig werden lassen
  window.print();
  if (navigator.standalone) toast('Erscheint kein Druckdialog: die Seite einmal direkt in Safari öffnen und dort drucken.');
}

starten();
