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
let dateienSuche = '';
let spulenCache = [];

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
  $('#glocke').hidden = false;
  $('#glocke').onclick = benachrichtigungenZeigen;
  if ('serviceWorker' in navigator) navigator.serviceWorker.register('sw.js').then(glockeAktualisieren).catch(() => {});
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
    b.classList.toggle('aktiv', b.dataset.seite === ({ spule: 'spulen', nachkauf: 'spulen', verlauf: 'buchen' }[name] || name)));
  window.scrollTo(0, 0);
  if (name === 'buchen') buchenSeite();
  else if (name === 'spulen') spulenSeite();
  else if (name === 'dateien') dateienSeite();
  else if (name === 'verlauf') verlaufSeite();
  else if (name === 'nachkauf') nachkaufSeite();
  else if (name === 'spule') spuleSeite(decodeURIComponent(arg || ''));
  else druckerSeite();
}

window.addEventListener('hashchange', navigieren);
document.addEventListener('click', e => {
  const z = e.target.closest('[data-seite],[data-schliessen],[data-kamera],[data-buchen],[data-verwerfen],[data-senden],[data-wisch],[data-fach-waehlen],[data-fach-uebernehmen],[data-buchung],[data-gekauft]');
  if (!z) return;
  if (z.dataset.seite) { if (location.hash === '#' + z.dataset.seite) navigieren(); else location.hash = z.dataset.seite; }
  else if (z.hasAttribute('data-schliessen')) blattSchliessen();
  else if (z.dataset.kamera) kameraZeigen(+z.dataset.kamera);
  else if (z.dataset.buchen) buchenFormular(+z.dataset.buchen);
  else if (z.dataset.verwerfen) verwerfen(+z.dataset.verwerfen);
  else if (z.dataset.senden) sendenFormular(+z.dataset.senden);
  else if (z.dataset.wisch) wischAktion(z.closest('.wisch'));
  else if (z.dataset.fachWaehlen) fachWaehlen(...z.dataset.fachWaehlen.split(':').map(Number));
  else if (z.dataset.buchung) buchungZeigen(+z.dataset.buchung);
  else if (z.dataset.gekauft) gekauft(z.dataset.gekauft);
  else if (z.dataset.fachUebernehmen) fachZuordnen(...z.dataset.fachUebernehmen.split(':').map(Number));
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

function druckerKarte(d, s, spulen = []) {
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
      ${z !== OFFLINE && z !== 0 ? faecherHtml(d, s, spulen) : ''}
      ${d.typ === 'Bambu' ? `<button class="knopf" data-kamera="${d.id}">${ICON.kamera} Kamera</button>` : ''}
    </section>`;
}

// ------------------------------------------------------------------ AMS-Fächer: Zuordnung Fach → Spule
// Die Überwachung (Server) liefert im Status die Zuordnung und Vorschläge nach Material und Farbe;
// RFID-Spulen ordnet sie selbst zu, sobald sie ihre Kennung einmal gelernt hat.

const fachFarbe = hex => hex && hex.length === 9 ? '#' + hex.slice(3) : '';   // "#AARRGGBB" -> "#RRGGBB"

function faecherHtml(d, s, spulen) {
  const faecher = s.faecher && s.faecher.length ? s.faecher : [{ slot: 0, beschreibung: 'Spule', belegt: true }];
  const zuordnung = s.zuordnung || {}, vorschlaege = s.vorschlaege || {};
  const name = id => { const f = spulen.find(x => x.id === id); return f ? `${f.code} · ${spulenText(f)}` : ''; };
  return `<div class="faecher">${faecher.map(f => {
      const zugeordnet = zuordnung[f.slot], vorschlag = !zugeordnet ? vorschlaege[f.slot] : null;
      const aktiv = s.slot === f.slot;
      return `<div class="fach${aktiv ? ' aktiv' : ''}">
          <span class="fach-farbe" style="${fachFarbe(f.farbeHex) ? 'background:' + fachFarbe(f.farbeHex) : ''}"></span>
          <div class="fach-text">
            <small>${esc(f.beschreibung)}${f.rest != null ? ` · ${f.rest} %` : ''}${aktiv ? ' · aktiv' : ''}</small>
            <b>${zugeordnet ? esc(name(zugeordnet) || 'Spule #' + zugeordnet) : '<span class="leise">keine Spule zugeordnet</span>'}</b>
            ${vorschlag && name(vorschlag) ? `<div class="vorschlag">Vorschlag: ${esc(name(vorschlag))}
              <button class="knopf klein" data-fach-uebernehmen="${d.id}:${f.slot}:${vorschlag}">Übernehmen</button></div>` : ''}
          </div>
          ${f.belegt !== false ? `<button class="knopf klein" data-fach-waehlen="${d.id}:${f.slot}">Ändern</button>` : ''}
        </div>`;
    }).join('')}</div>`;
}

async function fachZuordnen(druckerId, slot, filamentId) {
  try {
    await schicken(`drucker/${druckerId}/spulen/${slot}`, 'PUT', { filamentId: filamentId || null });
    toast(filamentId ? 'Spule zugeordnet.' : 'Zuordnung entfernt.');
  } catch (e) { return fehler(e); }
  blattSchliessen();
  if ((location.hash.slice(1) || 'drucker') === 'drucker') navigieren();
}

async function fachWaehlen(druckerId, slot) {
  let live, spulen;
  try { [live, spulen] = await Promise.all([holen('drucker/live'), holen('filament')]); } catch (e) { return fehler(e); }
  const s = (live.find(l => l.druckerId === druckerId) || {}).status || {};
  const fach = (s.faecher || []).find(f => f.slot === slot) || { slot, beschreibung: 'Spule' };
  const zugeordnet = (s.zuordnung || {})[slot], vorschlag = (s.vorschlaege || {})[slot];
  // Passende (gleiches Material) zuerst, dann der Rest
  const typ = (fach.typ || '').toUpperCase().replace(/[\s-]/g, '');
  const passt = f => typ && (f.material || '').toUpperCase().replace(/[\s-]/g, '').includes(typ);
  const angeboten = spulen.filter(f => f.restgewicht > 0 || f.id === zugeordnet)
    .sort((a, b) => (b.id === vorschlag) - (a.id === vorschlag) || passt(b) - passt(a) || a.code.localeCompare(b.code));
  blattOeffnen(`<div class="zeile"><h2>${esc(fach.beschreibung)}</h2><button class="knopf" data-schliessen>Abbrechen</button></div>
    ${fach.kennung ? '<p class="leise">Spule mit RFID-Chip – nach dem Zuordnen erkennt die App sie künftig selbst, auch in einem anderen Fach.</p>' : ''}
    <label>Spule<select id="fachSpule">
      <option value="0">– keine Spule –</option>
      ${angeboten.map(f => `<option value="${f.id}" ${f.id === (zugeordnet || vorschlag) ? 'selected' : ''}>${esc(f.code)} · ${esc(spulenText(f))} (${zahl(f.restgewicht)} g)${f.id === vorschlag ? ' – Vorschlag' : ''}</option>`).join('')}
    </select></label>
    <div class="knoepfe"><button class="primaer" id="fachOk">Speichern</button></div>`);
  $('#fachOk').onclick = () => fachZuordnen(druckerId, slot, +$('#fachSpule').value);
}

async function druckerSeite() {
  titel('Drucker');
  const aktualisieren = async () => {
    try {
      const [drucker, live, spulen] = await Promise.all([holen('drucker'), holen('drucker/live'), holen('filament')]);
      spulenCache = spulen;
      druckerListe = drucker;
      if (!drucker.length) return inhalt('<p class="leer">Noch kein Drucker angelegt.<br><small>Drucker legst du in der Windows-App unter „Drucker“ an.</small></p>');
      const status = Object.fromEntries(live.map(l => [l.druckerId, l.status]));
      inhalt(drucker.map(d => druckerKarte(d, status[d.id], spulen)).join(''));
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
  titel('Buchen');
  $('#inhalt').innerHTML = reiter('buchen');
  const laden = async () => {
    try {
      const enden = await holen('druckenden');
      zahlBuchen(enden.length);
      if (!enden.length) return inhalt(reiter('buchen') + '<p class="leer">Keine Drucke zum Buchen.<br><small>Fertige oder abgebrochene Drucke erscheinen hier automatisch.</small></p>');
      if (document.querySelector('.wisch.offen')) return;   // nicht unter dem Finger neu aufbauen
      inhalt(reiter('buchen') + enden.map(e => wischen(`<section class="karte">
          <div class="zeile">
            <div><h2>${esc(e.datei || 'Druck')}</h2><small class="leise">${esc(e.druckerName)} · ${datumZeit(e.zeitpunkt)}</small></div>
            <span class="badge ${e.abgebrochen ? 'rot' : 'gruen'}">${e.abgebrochen ? 'Abgebrochen bei ' + prozent(e.fortschritt) : 'Fertig'}</span>
          </div>
          <div class="knoepfe">
            <button class="primaer" data-buchen="${e.id}">Buchen …</button>
            <button class="knopf" data-verwerfen="${e.id}">Verwerfen</button>
          </div>
        </section>`, 'druckende', e.id, e.datei, 'Verwerfen')).join(''));
    } catch (e) { fehler(e); }
  };
  await laden();
  if (!$('#leiste').hidden && !document.hidden) seitenTimer = setInterval(laden, 10000);
}

/** Foto vom Druckende in ein <img> laden (die API braucht das Token, deshalb nicht direkt als src). */
async function fotoLaden(name, img) {
  try {
    const url = URL.createObjectURL(await (await api('fotos/' + encodeURIComponent(name))).blob());
    if (img && img.isConnected) { img.src = url; img.onload = () => URL.revokeObjectURL(url); } else URL.revokeObjectURL(url);
  } catch { if (img) img.remove(); }
}

async function buchenFormular(id) {
  let vorschlag, spulen;
  try {
    [vorschlag, spulen] = await Promise.all([holen(`druckenden/${id}/vorschlag`), holen('filament')]);
  } catch (e) { navigieren(); return fehler(e); }
  const auswahl = spulen.filter(f => f.restgewicht > 0 || f.id === vorschlag.filamentId).sort((a, b) => a.code.localeCompare(b.code));
  blattOeffnen(`<h2>Druck buchen</h2>
    ${vorschlag.foto ? '<img class="druckfoto" id="bFoto" alt="Foto vom Druckende">' : ''}
    <label>Bezeichnung<input id="bBezeichnung" value="${esc(vorschlag.bezeichnung)}"></label>
    <label>Spule<select id="bSpule">
      <option value="0">– Spule wählen –</option>
      ${auswahl.map(f => `<option value="${f.id}" ${f.id === vorschlag.filamentId ? 'selected' : ''}>${esc(f.code)} · ${esc(spulenText(f))} (${zahl(f.restgewicht)} g)</option>`).join('')}
    </select></label>
    <label>Verbrauch in Gramm<input id="bGramm" type="number" inputmode="numeric" min="0" value="${vorschlag.gramm}"></label>
    ${vorschlag.druckzeit ? `<p class="leise">Druckzeit: ${dauer(vorschlag.druckzeit)}</p>` : ''}
    <p class="leise">${esc(vorschlag.notiz)}</p>
    <div class="knoepfe"><button class="knopf" data-schliessen>Abbrechen</button><button class="primaer" id="bOk">Buchen</button></div>`);
  if (vorschlag.foto) fotoLaden(vorschlag.foto, $('#bFoto'));
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

// ------------------------------------------------------------------ Dateien: druckfertige Dateien an einen Drucker senden
// Wie "An Drucker senden" in der App: G-Code / BGCODE (Klipper, OctoPrint, PrusaLink) oder geslicte 3MF (Bambu Lab).
// Der Server lädt die Datei aus "Dateien" selbst auf den Drucker – es wird nichts übers Handy übertragen.

const GCODE_TYPEN = ['GCODE', 'GCO', 'G', 'BGCODE'];
const istDruckbar = d => d.hatDruckdaten || GCODE_TYPEN.includes(d.typ);
const ZUSTAND_BEREIT = [2, 5, 6];   // Bereit, Fertig, Abgebrochen

async function dateienSeite() {
  titel('Dateien');
  inhalt(`<div class="suche"><input id="dSuche" type="search" placeholder="Dateiname, Material …" value="${esc(dateienSuche)}">
      <button class="primaer" id="dHochladen">+ Hochladen</button><input type="file" id="dDatei" hidden multiple></div>
    <p class="leise klein-text">Druckfertige Dateien (G-Code und geslicte 3MF). Antippen, um sie an einen Drucker zu senden.</p>
    <div id="dListe"><p class="leer">Lade …</p></div>`);
  let dateien;
  try { dateien = (await holen('dateien')).filter(istDruckbar); } catch (e) { return fehler(e); }
  const zeigen = () => {
    const q = dateienSuche.toLowerCase();
    const treffer = dateien.filter(d => !q || [d.name, d.material, d.notiz].some(x => (x || '').toLowerCase().includes(q)));
    $('#dListe').innerHTML = treffer.length ? treffer.map(d => wischen(`<button class="karte datei-eintrag" data-senden="${d.id}">
        <b>${esc(d.name)}</b>
        <small class="leise">${[d.typ, d.gramm ? zahl(d.gramm) + ' g' : '', dauer(d.druckzeit), d.material, datumZeit(d.hinzugefuegt)].filter(Boolean).map(esc).join(' · ')}</small>
      </button>`, 'datei', d.id, d.name)).join('')
      : `<p class="leer">${dateien.length ? 'Keine passenden Dateien.' : 'Noch keine druckfertigen Dateien.<br><small>G-Code oder geslicte 3MF fügst du in der Windows-App unter „Dateien“ hinzu.</small>'}</p>`;
  };
  $('#dSuche').oninput = e => { dateienSuche = e.target.value; zeigen(); };
  $('#dHochladen').onclick = () => $('#dDatei').click();
  $('#dDatei').onchange = e => dateienHochladen([...e.target.files]);
  zeigen();
}

async function sendenFormular(dateiId) {
  let datei, drucker, live, spulen, platten;
  try {
    [datei, drucker, live, spulen, platten] = await Promise.all([
      holen('dateien').then(l => l.find(d => d.id === dateiId)), holen('drucker'), holen('drucker/live'),
      holen('filament'), holen(`dateien/${dateiId}/platten`)]);
  } catch (e) { return fehler(e); }
  if (!datei) return toast('Die Datei gibt es nicht mehr.', true);
  drucker = drucker.filter(d => d.aktiv);
  const kopf = `<div class="zeile"><div><h2>${esc(datei.name)}</h2>
      <small class="leise">${[datei.gramm ? zahl(datei.gramm) + ' g' : '', dauer(datei.druckzeit), datei.material].filter(Boolean).map(esc).join(' · ')}</small></div>
      <button class="knopf" id="sAbbrechen">Abbrechen</button></div>`;
  if (!drucker.length) {
    blattOeffnen(kopf + '<p class="fehler">Es ist kein Drucker angebunden (Drucker legst du in der Windows-App an).</p>');
    $('#sAbbrechen').onclick = blattSchliessen;
    return;
  }
  const liveVon = id => live.find(l => l.druckerId === id) || {};
  const endung = '.' + datei.name.split('.').pop().toLowerCase();
  const passt = d => (liveVon(d.id).endungen || []).includes(endung) && (d.typ !== 'Bambu' || platten.length > 0);
  const bereit = d => ZUSTAND_BEREIT.includes(liveVon(d.id).status?.zustand);
  let index = drucker.findIndex(d => passt(d) && bereit(d));
  if (index < 0) index = Math.max(0, drucker.findIndex(passt));

  blattOeffnen(kopf + `
    <label>Drucker<select id="sDrucker">${drucker.map((d, i) => `<option value="${i}" ${i === index ? 'selected' : ''}>${esc(d.name)} (${esc(d.typ)})</option>`).join('')}</select></label>
    <div id="sStatus" class="leise" style="margin-top:4px"></div>
    <div id="sBambu">
      ${platten.length ? `<label>Platte<select id="sPlatte">${platten.map(p => `<option value="${p.nummer}">Platte ${p.nummer}${p.gramm ? ' · ' + zahl(p.gramm) + ' g' : ''}${p.sekunden ? ' · ' + dauer(p.sekunden) : ''}</option>`).join('')}</select></label>` : ''}
      <div id="sFaecher"></div>
      <label class="schalter"><input type="checkbox" id="sNivellieren" checked> Bett nivellieren</label>
      <label class="schalter"><input type="checkbox" id="sZeitraffer"> Zeitraffer aufnehmen</label>
      <label class="schalter"><input type="checkbox" id="sFluss"> Fluss-Kalibrierung</label>
    </div>
    <p id="sWarnung"></p>
    <div class="balken" id="sBalken" hidden><div style="width:0"></div></div>
    <p id="sFortschritt" class="leise"></p>
    <div class="knoepfe"><button class="primaer" id="sStart">${ICON.drucken} Druck starten</button></div>`);

  let auftrag = null, faecherAuswahl = [];
  const gewaehlt = () => drucker[+$('#sDrucker').value];
  const platte = () => platten.find(p => p.nummer === +($('#sPlatte')?.value || 1));

  const faecherAufbauen = async (d, s) => {
    let zuordnung = {};
    try { zuordnung = await holen(`drucker/${d.id}/spulen`); } catch { /* ohne Vorschlag */ }
    const verfuegbar = (s?.faecher || []).filter(f => !f.beschreibung.endsWith('(leer)'));
    if (!verfuegbar.some(f => f.slot === 254)) verfuegbar.push({ slot: 254, beschreibung: 'Externe Spule' });
    const p = platte();
    const filamente = p?.filamente?.length ? p.filamente : [{ id: 1, typ: datei.material || '', gramm: 0 }];
    faecherAuswahl = filamente.map(pf => {
      // Vorschlag wie in der App: gleiches Material bei zugeordneter Spule > gleiches Material im Fach > aktives Fach
      let bester = 0, punkteBester = -1;
      const optionen = verfuegbar.map((f, i) => {
        const spule = spulen.find(x => x.id === zuordnung[f.slot]);
        const typ = (pf.typ || '').toUpperCase();
        const punkte = (typ && spule && spule.material.toUpperCase().includes(typ) ? 4 : 0) +
          (typ && f.beschreibung.toUpperCase().includes(typ) ? 2 : 0) + (s?.slot === f.slot ? 1 : 0);
        if (punkte > punkteBester) { punkteBester = punkte; bester = i; }
        return { slot: f.slot, text: f.beschreibung + (spule ? ` – ${spule.code} ${spulenText(spule)}` : '') };
      });
      return { pf, optionen, bester };
    });
    $('#sFaecher').innerHTML = faecherAuswahl.map((a, i) => `<label>
        ${a.pf.farbeHex ? `<span class="punkt-klein" style="background:${esc(a.pf.farbeHex)}"></span>` : ''}Filament ${a.pf.id}: ${esc(a.pf.typ || '?')}${a.pf.gramm ? ` (${zahl(a.pf.gramm)} g)` : ''} → AMS-Fach
        <select data-fach="${i}">${a.optionen.map((o, j) => `<option value="${o.slot}" ${j === a.bester ? 'selected' : ''}>${esc(o.text)}</option>`).join('')}</select>
      </label>`).join('');
  };

  const pruefen = async () => {
    const d = gewaehlt(), s = liveVon(d.id).status, z = s?.zustand ?? 0;
    const laeuft = z === DRUCKT || z === PAUSIERT;
    $('#sStatus').textContent = 'Status: ' + (ZUSTAND[z] || '?') + (laeuft && s.datei ? ' – ' + s.datei : '');
    $('#sStatus').style.color = bereit(d) ? 'var(--gruen)' : 'var(--rot)';
    $('#sBambu').hidden = d.typ !== 'Bambu';
    if (d.typ === 'Bambu') await faecherAufbauen(d, s);
    let problem = '';
    if (!passt(d)) {
      problem = d.typ === 'Bambu' ? 'Für Bambu Lab wird eine in Bambu Studio geslicte 3MF (mit G-Code) benötigt.'
        : `${d.typ} druckt nur ${(liveVon(d.id).endungen || []).join(' / ')}-Dateien aus dem passenden Slicer.`;
    } else if (d.typ === 'Bambu' && platte()?.ohneDruckerstart) {
      problem = 'Diese Datei enthält nicht den Start-G-Code des Druckers – der Drucker würde kein Filament laden. Bitte in der App neu slicen.';
    } else if (z === OFFLINE || z === 0) {
      problem = 'Der Drucker ist gerade nicht erreichbar.';
    } else if (laeuft) {
      problem = 'Der Drucker ist beschäftigt – warte, bis der aktuelle Druck fertig ist.';
    }
    $('#sWarnung').className = problem ? 'fehler' : 'warnung';
    $('#sWarnung').textContent = problem || '⚠ Der Druck startet sofort nach dem Hochladen. Ist die Druckplatte frei, sauber und richtig eingelegt?';
    $('#sStart').disabled = !!problem;
  };
  $('#sDrucker').onchange = pruefen;
  if ($('#sPlatte')) $('#sPlatte').onchange = pruefen;
  await pruefen();

  $('#sAbbrechen').onclick = async () => {
    if (!auftrag) return blattSchliessen();
    try { await schicken(`sendeauftraege/${auftrag}`, 'DELETE'); } catch { /* läuft evtl. schon nicht mehr */ }
  };

  $('#sStart').onclick = async () => {
    const d = gewaehlt();
    const optionen = {
      platte: platte()?.nummer || 1,
      nivellieren: $('#sNivellieren').checked,
      zeitraffer: $('#sZeitraffer').checked,
      flussKalibrierung: $('#sFluss').checked,
      amsZuordnung: d.typ === 'Bambu'
        ? faecherAuswahl.map((a, i) => ({ id: a.pf.id, slot: +document.querySelector(`[data-fach="${i}"]`).value })).sort((x, y) => x.id - y.id).map(x => x.slot)
        : []
    };
    document.querySelectorAll('#blattInhalt select, #blattInhalt input').forEach(e => e.disabled = true);
    $('#sStart').disabled = true;
    $('#sBalken').hidden = false;
    $('#sFortschritt').textContent = 'Der Server lädt die Datei auf den Drucker …';
    try {
      auftrag = (await (await schicken(`drucker/${d.id}/senden`, 'POST', { dateiId: datei.id, optionen })).json()).id;
      while (true) {
        await pause(1000);
        if ($('#blatt').hidden) return;   // Fenster geschlossen – der Server macht trotzdem weiter
        const info = await holen(`sendeauftraege/${auftrag}`);
        $('#sBalken').firstElementChild.style.width = (info.fortschritt * 100) + '%';
        $('#sFortschritt').textContent = info.fortschritt >= 0.999 ? 'Sende Startbefehl …' : `Lade auf den Drucker hoch … ${prozent(info.fortschritt)}`;
        if (info.fertig) {
          if (info.fehler) throw new Error(info.fehler);
          blattSchliessen();
          toast(`Druck auf ${d.name} gestartet.`);
          location.hash = 'drucker';
          return;
        }
      }
    } catch (e) {
      auftrag = null;
      $('#sBalken').hidden = true;
      $('#sFortschritt').textContent = '';
      $('#sWarnung').className = 'fehler';
      $('#sWarnung').textContent = 'Senden fehlgeschlagen: ' + e.message + ' Details stehen im Protokoll des Druckers (Windows-App).';
      document.querySelectorAll('#blattInhalt select, #blattInhalt input').forEach(e => e.disabled = false);
      $('#sStart').disabled = false;
    }
  };
}

// ------------------------------------------------------------------ Spulen

function spulenKarte(f) {
  const anteil = f.gewicht > 0 ? f.restgewicht / f.gewicht : 0;
  const klasse = f.restgewicht <= 0 ? 'leer' : anteil < 0.15 ? 'knapp' : '';
  return wischen(`<a class="karte spule" href="#spule/${encodeURIComponent(f.code)}">
      <div><b>${esc(f.code)}</b> <span class="leise">${esc(spulenText(f))}</span>${f.lagerort ? `<br><small class="leise">${esc(f.lagerort)}</small>` : ''}</div>
      <div class="rest">${zahl(f.restgewicht)} g<br><small class="leise">${prozent(anteil)}</small></div>
      <div class="balken ${klasse}"><div style="width:${Math.min(100, anteil * 100)}%"></div></div>
    </a>`, 'spule', f.id, f.code);
}

async function spulenSeite() {
  titel('Spulen');
  inhalt(reiter('spulen') + `<div class="suche">
      <input id="suche" type="search" placeholder="ID, Hersteller, Farbe, Lagerort …" value="${esc(spulenSuche)}">
      <button class="knopf" id="scannen" title="QR-Code oder Barcode scannen">${ICON.qr}</button>
    </div>
    <div class="zeile liste-kopf"><label class="schalter"><input type="checkbox" id="nurVorhanden" ${nurVorhanden ? 'checked' : ''}> nur mit Restbestand</label>
      <span class="knopf-gruppe"><button class="knopf" id="etiketten">${ICON.drucken} Etiketten</button><button class="primaer" id="neueSpule">+ Neu</button></span></div>
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
  $('#neueSpule').onclick = () => spuleFormular(null, spulen);
  zeigen();
}

/**
 * Kamera öffnen und den ersten QR-Code oder Strichcode (EAN/UPC/Code 128) lesen.
 * Liefert den Text oder null (abgebrochen / Kamera nicht verfügbar – dann gibt es schon einen Hinweis).
 */
async function scanAuslesen(titelText, hinweis) {
  if (!window.isSecureContext) { toast('Die Kamera geht im Browser nur über https – bitte die Cloudflare-Adresse benutzen.', true); return null; }
  let stream;
  try {
    stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment', width: { ideal: 1280 } } });
  } catch (e) {
    toast('Kamera nicht verfügbar: ' + e.message, true);
    return null;
  }
  // Ein offenes Formular (z. B. "Neue Spule") bleibt erhalten: der Scanner liegt als eigene Ebene darüber
  const ebene = document.createElement('div');
  ebene.className = 'blatt scan-ebene';
  ebene.innerHTML = `<div class="blatt-hinter"></div><div class="blatt-flaeche">
      <div class="zeile"><h2>${esc(titelText)}</h2><button class="knopf" id="scanAbbrechen">Abbrechen</button></div>
      <video id="scanVideo" playsinline muted></video>
      <p class="leise">${esc(hinweis)}</p></div>`;
  document.body.appendChild(ebene);
  const lauf = { aktiv: true, stream };
  scanLauf = lauf;
  const schliessen = () => { scanStopp(); ebene.remove(); };
  ebene.querySelector('#scanAbbrechen').onclick = schliessen;
  ebene.querySelector('.blatt-hinter').onclick = schliessen;
  const video = ebene.querySelector('#scanVideo');
  video.srcObject = stream;
  try { await video.play(); } catch { /* startet trotzdem */ }
  let erkennen;
  try {
    erkennen = await codeErkenner();
  } catch (e) {
    schliessen();
    toast(e.message, true);
    return null;
  }
  while (lauf.aktiv) {
    try {
      const gelesen = await erkennen(video);
      if (gelesen && lauf.aktiv) {
        schliessen();
        if (navigator.vibrate) navigator.vibrate(60);
        return gelesen.trim();
      }
    } catch { /* Bild noch nicht bereit */ }
    await pause(200);
  }
  return null;
}

/** Scan-Knopf der Spulen-Seite: Spulen-QR öffnet die Spule, Verpackungs-Barcode legt eine neue Spule dieser Sorte an. */
async function scannen(spulen) {
  const wert = await scanAuslesen('Scannen', 'QR-Code einer Spule oder Barcode einer Verpackung ins Bild halten.');
  if (!wert) return;
  const f = spulen.find(x => x.code.toLowerCase() === wert.toLowerCase());
  if (f) { location.hash = 'spule/' + encodeURIComponent(f.code); return; }
  if (istBarcode(wert)) return neueSpuleNachBarcode(wert, spulen);
  spulenSuche = wert;
  if ($('#suche')) { $('#suche').value = wert; $('#suche').dispatchEvent(new Event('input')); }
  toast(`Keine Spule mit der ID „${wert}“.`, true);
}

const istBarcode = text => /^\d{8,14}$/.test(text);

/** Neue Spule wie die zuletzt gekaufte mit diesem Barcode – oder leer mit eingetragenem Barcode. */
function neueSpuleNachBarcode(barcode, spulen) {
  const vorlage = spulen.filter(x => x.barcode === barcode)
    .sort((a, b) => (b.kaufdatum || '').localeCompare(a.kaufdatum || '') || b.id - a.id)[0];
  if (vorlage) toast(`Bekannte Sorte: ${spulenText(vorlage)}`);
  else toast('Neuer Barcode – bitte die Angaben einmal eintragen. Beim nächsten Scan sind sie ausgefüllt.');
  return vorlage ? spuleFormular(vorlage, spulen, true) : spuleFormular(null, spulen, false, { barcode });
}

// Code-Erkennung: eingebaut, wo vorhanden (Chrome/Edge auf Android), sonst jsQR für QR-Codes und ZXing für
// Strichcodes (iPhone/Safari, Firefox). Die Bibliotheken werden erst beim ersten Scannen geladen.
const skripte = {};
function skriptHolen(pfad, globalName) {
  if (window[globalName]) return Promise.resolve(window[globalName]);
  return skripte[pfad] ??= new Promise((ok, fehlschlag) => {
    const s = document.createElement('script');
    s.src = pfad;
    s.onload = () => ok(window[globalName]);
    s.onerror = () => { delete skripte[pfad]; fehlschlag(new Error('Die Code-Erkennung konnte nicht geladen werden.')); };
    document.head.appendChild(s);
  });
}

async function codeErkenner() {
  const gewuenscht = ['qr_code', 'ean_13', 'ean_8', 'upc_a', 'upc_e', 'code_128'];
  if ('BarcodeDetector' in window) {
    try {
      const formate = (await BarcodeDetector.getSupportedFormats()).filter(f => gewuenscht.includes(f));
      if (formate.includes('qr_code') && formate.includes('ean_13')) {
        const erkenner = new BarcodeDetector({ formats: formate });
        return async video => (await erkenner.detect(video))[0]?.rawValue || null;
      }
    } catch { /* weiter mit den Bibliotheken */ }
  }
  const [jsQR, ZX] = await Promise.all([skriptHolen('lib/jsQR.js', 'jsQR'), skriptHolen('lib/zxing.min.js', 'ZXing')]);
  const leser = new ZX.MultiFormatReader();
  leser.setHints(new Map([[ZX.DecodeHintType.POSSIBLE_FORMATS,
    [ZX.BarcodeFormat.EAN_13, ZX.BarcodeFormat.EAN_8, ZX.BarcodeFormat.UPC_A, ZX.BarcodeFormat.UPC_E, ZX.BarcodeFormat.CODE_128]]]));
  const leinwand = document.createElement('canvas');
  const ctx = leinwand.getContext('2d', { willReadFrequently: true });
  return async video => {
    if (!video.videoWidth) return null;
    // Strichcodes brauchen etwas mehr Auflösung als QR-Codes – 960 px reichen und schonen den Akku
    const f = Math.min(1, 960 / Math.max(video.videoWidth, video.videoHeight));
    const w = leinwand.width = Math.round(video.videoWidth * f);
    const h = leinwand.height = Math.round(video.videoHeight * f);
    ctx.drawImage(video, 0, 0, w, h);
    const bild = ctx.getImageData(0, 0, w, h);
    const qr = jsQR(bild.data, w, h, { inversionAttempts: 'dontInvert' });
    if (qr && qr.data) return qr.data;
    const grau = new Uint8ClampedArray(w * h);
    for (let i = 0, j = 0; i < grau.length; i++, j += 4) grau[i] = (bild.data[j] * 299 + bild.data[j + 1] * 587 + bild.data[j + 2] * 114) / 1000;
    try {
      return leser.decodeWithState(new ZX.BinaryBitmap(new ZX.HybridBinarizer(new ZX.RGBLuminanceSource(grau, w, h)))).getText();
    } catch {
      return null;   // kein Code in diesem Bild
    }
  };
}
function scanStopp() {
  document.querySelectorAll('.scan-ebene').forEach(e => e.remove());
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
    <div class="knoepfe"><button class="knopf" id="bearbeiten">Bearbeiten</button><button class="knopf" id="duplizieren">Neue Spule wie diese</button></div>
    <button class="knopf breit" id="etikett">${ICON.drucken} Etikett drucken</button>`);
  $('#bearbeiten').onclick = async () => spuleFormular(f, await holen('filament'), false);
  $('#duplizieren').onclick = async () => spuleFormular(f, await holen('filament'), true);
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


// ------------------------------------------------------------------ Spule anlegen / bearbeiten (wie FilamentDialog der App)

const STANDARD_HERSTELLER = ['3DJake', 'Anycubic', 'Bambu Lab', 'Creality', 'Das Filament', 'Elegoo', 'eSun', 'Extrudr',
  'Fiberlogy', 'Geeetech', 'Jayo', 'Overture', 'Polymaker', 'Prusament', 'Sunlu'];
const STANDARD_MATERIALIEN = ['PLA', 'PLA+', 'PLA Silk', 'PLA Matt', 'PETG', 'ABS', 'ASA', 'TPU', 'PA (Nylon)', 'PC', 'HIPS', 'PVA', 'CF-PLA', 'CF-PETG'];

function vorschlaege(liste) {
  const eindeutig = new Map();
  liste.filter(x => x && x.trim()).forEach(x => { const k = x.trim().toLowerCase(); if (!eindeutig.has(k)) eindeutig.set(k, x.trim()); });
  return [...eindeutig.values()].sort((a, b) => a.localeCompare(b, 'de', { sensitivity: 'base' }))
    .map(x => `<option value="${esc(x)}">`).join('');
}

/**
 * vorlage = null: neue Spule; mit vorlage und kopie = false: bearbeiten; kopie = true: neue Spule wie diese
 * (gleiche Angaben, neue ID, voll, heute gekauft).
 */
async function spuleFormular(vorlage, alleSpulen, kopie = false, extra = {}) {
  const neu = !vorlage || kopie;
  const f = vorlage ? { ...vorlage } : { id: 0, hersteller: '', material: '', farbe: '', durchmesser: 1.75, gewicht: 1000, restgewicht: 1000,
    spulengewicht: 0, preis: 0, notiz: '', lagerort: '', duesenTemp: 0, bettTemp: 0, trocknung: '', shopLink: '' };
  if (neu) {
    f.id = 0;
    try { f.code = (await holen('filament/naechster-code')).wert; } catch (e) { return fehler(e); }
    f.restgewicht = f.gewicht;
    f.kaufdatum = new Date().toISOString().slice(0, 10);
  }
  Object.assign(f, extra);
  const datum = (f.kaufdatum || new Date().toISOString()).slice(0, 10);
  blattOeffnen(`<div class="zeile"><h2>${!vorlage ? 'Neue Spule' : kopie ? 'Neue Spule wie ' + esc(vorlage.code) : 'Spule bearbeiten'}</h2>
      <button class="knopf" data-schliessen>Abbrechen</button></div>
    <datalist id="lHersteller">${vorschlaege(STANDARD_HERSTELLER.concat(alleSpulen.map(x => x.hersteller)))}</datalist>
    <datalist id="lMaterial">${vorschlaege(STANDARD_MATERIALIEN.concat(alleSpulen.map(x => x.material)))}</datalist>
    <datalist id="lLagerort">${vorschlaege(alleSpulen.map(x => x.lagerort))}</datalist>
    <div class="formular">
      <label>ID / Nummer<input id="fCode" value="${esc(f.code)}" autocapitalize="characters"></label>
      <label>Hersteller<input id="fHersteller" list="lHersteller" value="${esc(f.hersteller)}"></label>
      <label>Material<input id="fMaterial" list="lMaterial" value="${esc(f.material)}"></label>
      <label>Farbe<input id="fFarbe" value="${esc(f.farbe)}"></label>
      <div class="zwei">
        <label>Durchmesser<select id="fDurchmesser"><option value="1.75">1,75 mm</option><option value="2.85" ${f.durchmesser >= 2.5 ? 'selected' : ''}>2,85 mm</option></select></label>
        <label>Preis (€)<input id="fPreis" type="number" inputmode="decimal" min="0" step="0.01" value="${f.preis || ''}"></label>
      </div>
      <div class="zwei">
        <label>Gewicht netto (g)<input id="fGewicht" type="number" inputmode="numeric" min="0" value="${f.gewicht}"></label>
        <label>Restgewicht (g)<input id="fRest" type="number" inputmode="numeric" min="0" value="${f.restgewicht}"></label>
      </div>
      <div class="zwei">
        <label>Leerspule (g)<input id="fSpule" type="number" inputmode="numeric" min="0" value="${f.spulengewicht || ''}"><small class="leise" id="fVorlage"></small></label>
        <label>Kaufdatum<input id="fKauf" type="date" value="${datum}"></label>
      </div>
      <label>Lagerort<input id="fLagerort" list="lLagerort" value="${esc(f.lagerort)}"></label>
      <div class="zwei">
        <label>Düse (°C)<input id="fDuese" type="number" inputmode="numeric" min="0" value="${f.duesenTemp || ''}"></label>
        <label>Bett (°C)<input id="fBett" type="number" inputmode="numeric" min="0" value="${f.bettTemp || ''}"></label>
      </div>
      <label>Trocknung<input id="fTrocknung" value="${esc(f.trocknung)}" placeholder="z. B. 4 h bei 55 °C"></label>
      <label>Shop-Link<input id="fShop" type="url" value="${esc(f.shopLink)}" placeholder="https://…"></label>
      <label>Barcode der Verpackung<span class="suche"><input id="fBarcode" inputmode="numeric" value="${esc(f.barcode)}" placeholder="EAN – zum Wiedererkennen der Sorte">
        <button type="button" class="knopf" id="fBarcodeScan" title="Barcode scannen">${ICON.qr}</button></span></label>
      <label>Notiz<textarea id="fNotiz" rows="3">${esc(f.notiz)}</textarea></label>
    </div>
    <div class="knoepfe"><button class="primaer" id="fSpeichern">Speichern</button></div>`);

  // War die Spule voll, wandert das Restgewicht beim Ändern des Gewichts mit
  let letztesGewicht = f.gewicht;
  $('#fGewicht').oninput = () => {
    if (+$('#fRest').value === letztesGewicht) $('#fRest').value = $('#fGewicht').value;
    letztesGewicht = +$('#fGewicht').value;
  };
  // Leerspulengewicht des Herstellers vorschlagen – eigene Eingaben bleiben stehen
  let automatisch = -1;
  const vorlageAnwenden = async () => {
    const hersteller = $('#fHersteller').value.trim();
    let v = { gewicht: null, eigenerWert: false };
    if (hersteller) { try { v = await holen('leerspulen?hersteller=' + encodeURIComponent(hersteller)); } catch { /* ohne Vorlage */ } }
    const feld = $('#fSpule');
    if (!feld) return;
    const aktuell = +feld.value || 0;
    if (v.gewicht == null) {
      $('#fVorlage').textContent = '';
      if (automatisch > 0 && aktuell === automatisch) feld.value = '';
      automatisch = -1;
      return;
    }
    $('#fVorlage').textContent = v.eigenerWert ? `zuletzt verwendet: ${v.gewicht} g` : `Richtwert: ca. ${v.gewicht} g`;
    if (aktuell === 0 || aktuell === automatisch) { feld.value = v.gewicht; automatisch = v.gewicht; }
  };
  $('#fHersteller').onchange = vorlageAnwenden;
  // Barcode scannen; bei einer neuen Spule und bekannter Sorte die Angaben übernehmen
  $('#fBarcodeScan').onclick = async () => {
    const code = await scanAuslesen('Barcode scannen', 'Barcode der Verpackung ins Bild halten.');
    if (!code || !$('#fBarcode')) return;
    $('#fBarcode').value = code;
    const bekannt = alleSpulen.filter(x => x.barcode === code && x.id !== f.id)
      .sort((a, b) => (b.kaufdatum || '').localeCompare(a.kaufdatum || '') || b.id - a.id)[0];
    if (!bekannt || !neu || $('#fHersteller').value.trim()) return;
    const setze = (id, wert) => { const e = $(id); if (e) e.value = wert ?? ''; };
    setze('#fHersteller', bekannt.hersteller); setze('#fMaterial', bekannt.material); setze('#fFarbe', bekannt.farbe);
    setze('#fDurchmesser', bekannt.durchmesser >= 2.5 ? '2.85' : '1.75'); setze('#fPreis', bekannt.preis || '');
    setze('#fGewicht', bekannt.gewicht); setze('#fRest', bekannt.gewicht); setze('#fSpule', bekannt.spulengewicht || '');
    setze('#fDuese', bekannt.duesenTemp || ''); setze('#fBett', bekannt.bettTemp || ''); setze('#fTrocknung', bekannt.trocknung);
    setze('#fShop', bekannt.shopLink); setze('#fLagerort', bekannt.lagerort);
    toast(`Bekannte Sorte übernommen: ${spulenText(bekannt)}`);
  };
  vorlageAnwenden();

  $('#fSpeichern').onclick = async () => {
    const wert = id => $(id).value.trim();
    const zahlWert = id => Math.max(0, Math.round(+$(id).value || 0));
    const daten = {
      ...f,
      code: wert('#fCode'), hersteller: wert('#fHersteller'), material: wert('#fMaterial'), farbe: wert('#fFarbe'),
      durchmesser: +$('#fDurchmesser').value, gewicht: zahlWert('#fGewicht'), restgewicht: zahlWert('#fRest'),
      spulengewicht: zahlWert('#fSpule'), preis: Math.max(0, +(+$('#fPreis').value || 0).toFixed(2)), kaufdatum: $('#fKauf').value || new Date().toISOString().slice(0, 10),
      lagerort: wert('#fLagerort'), duesenTemp: zahlWert('#fDuese'), bettTemp: zahlWert('#fBett'),
      trocknung: wert('#fTrocknung'), shopLink: wert('#fShop'), barcode: wert('#fBarcode'), notiz: $('#fNotiz').value
    };
    if (!daten.code) return toast('Bitte eine ID / Nummer eingeben.', true);
    if (!daten.hersteller) return toast('Bitte einen Hersteller angeben.', true);
    if (daten.shopLink && !/^https?:\/\//i.test(daten.shopLink)) return toast('Der Shop-Link muss mit http:// oder https:// beginnen.', true);
    if (daten.restgewicht > daten.gewicht) return toast('Das Restgewicht darf nicht größer als das Gewicht sein.', true);
    $('#fSpeichern').disabled = true;
    try {
      if (await holen(`filament/code-existiert?code=${encodeURIComponent(daten.code)}&ausserId=${daten.id}`)) {
        $('#fSpeichern').disabled = false;
        return toast(`Die ID „${daten.code}“ ist bereits vergeben. Bitte eine andere wählen.`, true);
      }
      const gespeichert = await (await schicken('filament', 'POST', daten)).json();
      blattSchliessen();
      toast(neu ? `Spule ${gespeichert.code} angelegt.` : 'Gespeichert.');
      if (location.hash === '#spule/' + encodeURIComponent(gespeichert.code)) navigieren();
      else location.hash = 'spule/' + encodeURIComponent(gespeichert.code);
    } catch (e) {
      $('#fSpeichern').disabled = false;
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
    <div class="knoepfe"><button class="knopf" id="eBild">Als Bild</button><button class="primaer" id="eDrucken">Drucken</button></div>
    <div id="eBilder"></div>`);

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

  $('#eBild').onclick = async () => {
    const liste = auswahl();
    if (!liste.length) return toast('Bitte mindestens eine Spule auswählen.', true);
    $('#eBild').disabled = true;
    try {
      schicken('einstellungen/Etiketten.Format', 'PUT', { wert: String($('#eFormat').value) }).catch(() => {});
      await etikettenBilderZeigen(liste, format(), $('#eRahmen').checked);
    } catch (e) { fehler(e); }
    $('#eBild').disabled = false;
  };

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


// ------------------------------------------------------------------ Etiketten als Bild (für andere Drucker / Apps)
// Gleicher Aufbau wie etikettHtml, aber auf eine Leinwand in 300 dpi gezeichnet → PNG in Etikettengröße.

const BILD_DPI = 300;

function textKuerzen(g, text, breite) {
  if (g.measureText(text).width <= breite) return text;
  while (text.length > 1 && g.measureText(text + '…').width > breite) text = text.slice(0, -1);
  return text + '…';
}

function etikettBild(f, qr, fmt, rahmen) {
  const pxProMm = BILD_DPI / 25.4, mm = v => v * pxProMm;
  const leinwand = document.createElement('canvas');
  leinwand.width = Math.round(mm(fmt.breite));
  leinwand.height = Math.round(mm(fmt.hoehe));
  const g = leinwand.getContext('2d');
  g.fillStyle = '#fff';
  g.fillRect(0, 0, leinwand.width, leinwand.height);
  if (rahmen) { g.strokeStyle = '#c8c8c8'; g.lineWidth = mm(0.2); g.strokeRect(mm(0.1), mm(0.1), leinwand.width - mm(0.2), leinwand.height - mm(0.2)); }

  const pad = Math.max(1.8, fmt.hoehe * 0.07), skala = fmt.hoehe / 37;
  const qrGroesse = fmt.hoehe - 2 * pad, modul = mm(qrGroesse) / qr.length;
  g.fillStyle = '#000';
  qr.forEach((zeile, y) => {
    for (let x = 0; x < zeile.length; x++) {
      if (zeile[x] === '1') g.fillRect(mm(pad * 0.6) + x * modul, mm(pad) + y * modul, modul + 0.6, modul + 0.6);
    }
  });

  const tx = mm(pad * 0.6 + qrGroesse + pad * 0.8), breite = mm(fmt.breite - pad) - tx, unterkante = mm(fmt.hoehe - pad * 0.5);
  const pxVonPt = pt => pt * skala * 0.352778 * pxProMm;
  let y = mm(pad * 0.9);
  const zeile = (text, pt, fett, farbe, x = tx, b = breite) => {
    const px = pxVonPt(pt), hoehe = px * 1.33;
    if (text) {
      g.font = `${fett ? 600 : 400} ${px}px system-ui, -apple-system, "Segoe UI", Roboto, sans-serif`;
      g.fillStyle = farbe;
      g.textBaseline = 'middle';
      g.fillText(textKuerzen(g, text, b), x, y + hoehe / 2);
    }
    y += hoehe;
  };
  zeile(f.code, 13, true, '#000');
  y += mm(0.4 * skala);
  zeile(f.hersteller, 8.5, true, '#000');
  const farbe = farbeZuName(f.farbe), hoeheNormal = pxVonPt(8) * 1.33;
  let einzug = 0;
  if (farbe) {
    const d = hoeheNormal * 0.62;
    g.beginPath();
    g.arc(tx + d / 2, y + hoeheNormal / 2, d / 2, 0, Math.PI * 2);
    g.fillStyle = farbe;
    g.fill();
    g.lineWidth = mm(0.15);
    g.strokeStyle = 'rgba(0,0,0,0.47)';
    g.stroke();
    einzug = d + mm(1);
  }
  zeile([f.material, f.farbe].filter(Boolean).join(' · '), 8, false, '#000', tx + einzug, breite - einzug);
  const details = [`Ø ${Number(f.durchmesser || 1.75).toFixed(2).replace('.', ',')} mm`, `${zahl(f.gewicht)} g`];
  if (f.temperaturen) details.push(f.temperaturen);
  y += mm(0.3 * skala);
  zeile(details.join(' · '), 6.5, false, '#555');
  const kauf = f.kaufdatum ? new Date(f.kaufdatum) : null;
  const unten = [f.lagerort, kauf && kauf.getFullYear() > 1 ? `gekauft ${String(kauf.getMonth() + 1).padStart(2, '0')}/${kauf.getFullYear()}` : '']
    .filter(Boolean).join(' · ');
  if (unten && y + pxVonPt(6.5) * 1.33 <= unterkante) zeile(unten, 6.5, false, '#555');

  return new Promise((ok, fehlschlag) => leinwand.toBlob(b => b ? ok(b) : fehlschlag(new Error('Das Bild konnte nicht erzeugt werden.')), 'image/png'));
}

let etikettBildUrls = [];

async function etikettenBilderZeigen(liste, fmt, rahmen) {
  const qr = await (await schicken('qr', 'POST', liste.map(f => f.code))).json();
  const dateien = [];
  for (const f of liste) {
    const bild = await etikettBild(f, qr[f.code], fmt, rahmen);
    dateien.push(new File([bild], `Etikett ${f.code}.png`, { type: 'image/png' }));
  }
  etikettBildUrls.forEach(u => URL.revokeObjectURL(u));
  etikettBildUrls = dateien.map(d => URL.createObjectURL(d));
  // Teilen erst auf einen eigenen Fingertipp – Safari erlaubt navigator.share nur direkt nach einer Berührung
  const teilenGeht = !!(navigator.canShare && navigator.canShare({ files: dateien }));
  $('#eBilder').innerHTML = `<p class="leise">${dateien.length} Bild${dateien.length === 1 ? '' : 'er'} · ${fmt.breite} × ${fmt.hoehe} mm · ${BILD_DPI} dpi.
      ${teilenGeht ? 'Teilen z. B. an die App des Etikettendruckers, per AirDrop oder in Fotos/Dateien.' : 'Zum Speichern ein Bild antippen.'}
      Auf dem iPhone kannst du ein Bild auch lange drücken und sichern.</p>
    <div class="knoepfe"><button class="primaer" id="eTeilen">${teilenGeht ? 'Teilen / Sichern' : 'Herunterladen'}</button></div>
    <div class="bilder">${etikettBildUrls.map((u, i) => `<a href="${u}" download="${esc(dateien[i].name)}"><img src="${u}" alt="${esc(dateien[i].name)}"></a>`).join('')}</div>`;
  $('#eTeilen').onclick = async () => {
    if (teilenGeht) {
      try { await navigator.share({ files: dateien, title: 'Spulen-Etiketten' }); }
      catch (e) { if (e.name !== 'AbortError') toast('Teilen nicht möglich: ' + e.message, true); }
    } else {
      document.querySelectorAll('#eBilder .bilder a').forEach(a => a.click());
    }
  };
  $('#eBilder').scrollIntoView({ behavior: 'smooth', block: 'start' });
}


// ------------------------------------------------------------------ Nach links wischen zum Löschen (wie in Handy-Apps)
// Kurz nach links wischen legt den roten Knopf frei, ein Tipp darauf löscht (nach Rückfrage). Senkrechtes Scrollen
// bleibt beim Browser (touch-action: pan-y); funktioniert auch mit der Maus.

const WISCH_BREITE = 96;
let wischZug = null;

function wischen(inhaltHtml, art, id, name, aktion = 'Löschen') {
  return `<div class="wisch" data-art="${art}" data-id="${id}" data-name="${esc(name)}">
      <button class="wisch-aktion" data-wisch="1" tabindex="-1">${aktion}</button>
      <div class="wisch-inhalt">${inhaltHtml}</div>
    </div>`;
}

function wischSchliessen(w) {
  w.classList.remove('offen');
  w.querySelector('.wisch-inhalt').style.transform = '';
}

document.addEventListener('pointerdown', e => {
  if (e.target.closest('.wisch-aktion')) return;          // Tipp auf "Löschen" – Karte offen lassen
  const inhaltEl = e.target.closest('.wisch-inhalt');
  document.querySelectorAll('.wisch.offen').forEach(w => { if (!inhaltEl || w !== inhaltEl.parentElement) wischSchliessen(w); });
  if (!inhaltEl || (e.pointerType === 'mouse' && e.button !== 0)) return;
  const w = inhaltEl.parentElement;
  wischZug = { w, inhaltEl, x0: e.clientX, y0: e.clientY, start: w.classList.contains('offen') ? -WISCH_BREITE : 0, dx: 0, richtung: null };
});

document.addEventListener('pointermove', e => {
  const z = wischZug;
  if (!z) return;
  const dx = e.clientX - z.x0, dy = e.clientY - z.y0;
  if (!z.richtung) {
    if (Math.abs(dx) < 8 && Math.abs(dy) < 8) return;
    z.richtung = Math.abs(dx) > Math.abs(dy) ? 'x' : 'y';
    if (z.richtung === 'x') z.inhaltEl.classList.add('zieht');
  }
  if (z.richtung !== 'x') return;
  z.dx = dx;
  z.inhaltEl.style.transform = `translateX(${Math.min(0, Math.max(-WISCH_BREITE * 1.4, z.start + dx))}px)`;
});

function wischEnde() {
  const z = wischZug;
  wischZug = null;
  if (!z || z.richtung !== 'x') return;
  z.inhaltEl.classList.remove('zieht');
  if (z.start + z.dx < -WISCH_BREITE / 2) {
    z.w.classList.add('offen');
    z.inhaltEl.style.transform = `translateX(-${WISCH_BREITE}px)`;
  } else {
    wischSchliessen(z.w);
  }
  // Der Klick, der nach dem Loslassen kommt, soll die Karte nicht öffnen
  z.w.dataset.gewischt = '1';
  setTimeout(() => delete z.w.dataset.gewischt, 400);
}
document.addEventListener('pointerup', wischEnde);
document.addEventListener('pointercancel', wischEnde);

// Klick auf eine offene oder gerade gewischte Karte: nur schließen, nicht öffnen
document.addEventListener('click', e => {
  const w = e.target.closest('.wisch');
  if (!w || e.target.closest('.wisch-aktion')) return;
  if (w.dataset.gewischt || w.classList.contains('offen')) {
    e.preventDefault();
    e.stopPropagation();
    if (!w.dataset.gewischt) wischSchliessen(w);
  }
}, true);

async function wischAktion(w) {
  const art = w.dataset.art, id = +w.dataset.id, name = w.dataset.name;
  if (art === 'druckende') { wischSchliessen(w); return verwerfen(id); }
  if (art === 'buchung') { wischSchliessen(w); return buchungLoeschen(id); }
  const frage = art === 'spule'
    ? `Spule ${name} löschen?\n\nBisherige Drucke bleiben im Verlauf erhalten.`
    : `Datei „${name}“ löschen?\n\nSie wird auch vom Server entfernt. Bereits gebuchte Drucke behalten ihre Daten.`;
  if (!confirm(frage)) return wischSchliessen(w);
  try {
    await schicken(art === 'spule' ? `filament/${id}` : `dateien/${id}`, 'DELETE');
  } catch (e) {
    wischSchliessen(w);
    return fehler(e);
  }
  w.style.height = w.offsetHeight + 'px';
  w.classList.add('weg');
  requestAnimationFrame(() => { w.style.height = '0'; });
  toast(art === 'spule' ? `Spule ${name} gelöscht.` : 'Datei gelöscht.');
  setTimeout(navigieren, 250);
}


// ------------------------------------------------------------------ Benachrichtigungen (Web Push)
// Der Server schickt beim Druckende eine Nachricht an alle angemeldeten Geräte. Auf dem iPhone geht das nur,
// wenn die Seite über "Zum Home-Bildschirm" abgelegt ist und von dort geöffnet wird (iOS 16.4+).

const istIphone = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
const alsApp = () => navigator.standalone === true || matchMedia('(display-mode: standalone)').matches;
const pushMoeglich = () => 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window && window.isSecureContext;

function schluesselBytes(b64) {
  const s = (b64 + '='.repeat((4 - b64.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
  return Uint8Array.from(atob(s), c => c.charCodeAt(0));
}

async function pushAbo() {
  if (!pushMoeglich()) return null;
  const reg = await navigator.serviceWorker.getRegistration();
  return reg ? reg.pushManager.getSubscription() : null;
}

async function glockeAktualisieren() {
  const an = !!(await pushAbo().catch(() => null)) && Notification.permission === 'granted';
  $('#glocke').classList.toggle('an', an);
}

async function benachrichtigungenZeigen() {
  let hinweis = '', an = false;
  if (!window.isSecureContext) hinweis = 'Benachrichtigungen gehen nur über https – bitte die Cloudflare-Adresse benutzen.';
  else if (istIphone && !alsApp()) hinweis = 'Auf dem iPhone: in Safari auf Teilen (□↑) → „Zum Home-Bildschirm“ tippen und die Seite von dort öffnen. Dann hier einschalten.';
  else if (!pushMoeglich()) hinweis = 'Dieser Browser kann keine Benachrichtigungen empfangen.';
  else if (Notification.permission === 'denied') hinweis = 'Benachrichtigungen sind für diese Seite blockiert. Auf dem iPhone unter Einstellungen → Mitteilungen → Filament wieder erlauben.';
  else an = !!(await pushAbo().catch(() => null)) && Notification.permission === 'granted';

  blattOeffnen(`<div class="zeile"><h2>Benachrichtigungen</h2><button class="knopf" data-schliessen>Schließen</button></div>
    <p class="leise">Wenn ein Druck fertig ist oder abbricht, bekommt dieses Gerät eine Nachricht – auch wenn die Seite geschlossen ist.
      Antippen öffnet „Buchen“.</p>
    ${hinweis ? `<p class="warnung">${esc(hinweis)}</p>` : `<p><b>Status:</b> ${an ? '<span style="color:var(--gruen)">eingeschaltet</span>' : 'aus'}</p>
    <div class="knoepfe">
      ${an ? '<button class="knopf" id="pushAus">Ausschalten</button><button class="primaer" id="pushTest">Test senden</button>'
           : '<button class="primaer" id="pushAn">Einschalten</button>'}
    </div>`}
    <h2 style="margin-top:20px">Wofür?</h2>
    <p class="leise klein-text">Gilt für alle Geräte.</p>
    <div id="pushRegeln" class="modi"><p class="leise">Lade …</p></div>`);
  pushRegelnZeigen();

  if ($('#pushAn')) $('#pushAn').onclick = async () => {
    // iOS verlangt die Rückfrage direkt beim Antippen – deshalb als Erstes
    const erlaubnis = await Notification.requestPermission();
    if (erlaubnis !== 'granted') return toast('Benachrichtigungen wurden nicht erlaubt.', true);
    try {
      const reg = await navigator.serviceWorker.ready;
      const schluessel = (await holen('push/schluessel')).wert;
      let abo = await reg.pushManager.getSubscription();
      if (!abo) abo = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: schluesselBytes(schluessel) });
      const j = abo.toJSON();
      await schicken('push/abo', 'POST', { endpunkt: j.endpoint, p256dh: j.keys.p256dh, auth: j.keys.auth,
        geraet: istIphone ? 'iPhone' : (navigator.userAgentData?.platform || navigator.platform || 'Browser') });
      toast('Benachrichtigungen eingeschaltet.');
    } catch (e) { fehler(e); }
    glockeAktualisieren();
    benachrichtigungenZeigen();
  };
  if ($('#pushAus')) $('#pushAus').onclick = async () => {
    try {
      const abo = await pushAbo();
      if (abo) {
        await schicken('push/abmelden', 'POST', { endpunkt: abo.endpoint });
        await abo.unsubscribe();
      }
      toast('Benachrichtigungen ausgeschaltet.');
    } catch (e) { fehler(e); }
    glockeAktualisieren();
    benachrichtigungenZeigen();
  };
  if ($('#pushTest')) $('#pushTest').onclick = async () => {
    try {
      const anzahl = +(await (await schicken('push/test', 'POST', {})).json()).wert;
      toast(anzahl ? `Test an ${anzahl} Gerät${anzahl === 1 ? '' : 'e'} gesendet.` : 'Kein Gerät erreicht – bitte aus- und wieder einschalten.', !anzahl);
    } catch (e) { fehler(e); }
  };
}


// Schalter für die Benachrichtigungen (Einstellungen auf dem Server, Standard wie dort: offline aus, Rest an)
const PUSH_REGELN = [
  ['Push.Druckende', 'Druck fertig oder abgebrochen', true],
  ['Push.SpuleKnapp', 'Spule fast leer bzw. leer (nach einer Buchung)', true],
  ['Push.DruckerFehler', 'Fehler am Drucker', true],
  ['Push.DruckerOffline', 'Drucker länger als 3 Minuten nicht erreichbar', false]
];

async function pushRegelnZeigen() {
  let werte, grenze;
  try {
    werte = await Promise.all(PUSH_REGELN.map(([k]) => holen('einstellungen/' + k).then(x => x.wert)));
    grenze = (await holen('einstellungen/Push.SpuleKnappGrenze')).wert || '100';
  } catch (e) { return fehler(e); }
  if (!$('#pushRegeln')) return;
  $('#pushRegeln').innerHTML = PUSH_REGELN.map(([k, text, standard], i) => `<label><input type="checkbox" data-regel="${k}"
      ${(werte[i] ? werte[i] === '1' : standard) ? 'checked' : ''}> ${esc(text)}</label>`).join('') +
    `<label style="display:block;margin-top:8px;color:var(--leise);font-size:13.5px">„Fast leer“ unter (g)
      <input id="pushGrenze" type="number" inputmode="numeric" min="1" value="${esc(grenze)}"></label>`;
  document.querySelectorAll('[data-regel]').forEach(box => box.onchange = () =>
    schicken('einstellungen/' + box.dataset.regel, 'PUT', { wert: box.checked ? '1' : '0' }).catch(fehler));
  $('#pushGrenze').onchange = () => {
    const g = Math.max(1, parseInt($('#pushGrenze').value, 10) || 100);
    $('#pushGrenze').value = g;
    schicken('einstellungen/Push.SpuleKnappGrenze', 'PUT', { wert: String(g) }).catch(fehler);
  };
}


// ------------------------------------------------------------------ Reiter (Offen | Verlauf, Spulen | Nachkauf)

function reiter(gruppe) {
  const aktuell = location.hash.slice(1).split('/')[0] || 'drucker';
  const eintraege = gruppe === 'buchen' ? [['buchen', 'Offen'], ['verlauf', 'Verlauf']] : [['spulen', 'Spulen'], ['nachkauf', 'Nachkauf']];
  return `<div class="reiter">${eintraege.map(([ziel, text]) => `<a href="#${ziel}" class="${aktuell === ziel ? 'aktiv' : ''}">${text}</a>`).join('')}</div>`;
}

// ------------------------------------------------------------------ Verlauf: alle Buchungen (Drucke, Wiegen, Korrekturen)

let verlaufSuche = '';
const istVerbrauch = b => b.art !== 'Korrektur' && b.gramm > 0;
const euro = x => (x || 0).toLocaleString('de-DE', { style: 'currency', currency: 'EUR' });

async function verlaufSeite() {
  titel('Verlauf');
  inhalt(reiter('buchen') + '<p class="leer">Lade …</p>');
  let buchungen;
  try { buchungen = await holen('buchungen'); } catch (e) { return fehler(e); }
  const heute = new Date(), monatsStart = new Date(heute.getFullYear(), heute.getMonth(), 1);
  const monat = buchungen.filter(b => new Date(b.datum) >= monatsStart);
  const drucke = monat.filter(b => b.art === 'Druck').length;
  const gramm = monat.filter(istVerbrauch).reduce((s, b) => s + b.gramm, 0);
  const kosten = monat.filter(istVerbrauch).reduce((s, b) => s + b.kosten, 0);
  inhalt(reiter('buchen') + `
    <div class="kennzahlen">
      <div><b>${drucke}</b><small>Drucke im ${heute.toLocaleString('de-DE', { month: 'long' })}</small></div>
      <div><b>${zahl(gramm)} g</b><small>verbraucht</small></div>
      <div><b>${euro(kosten)}</b><small>Kosten</small></div>
    </div>
    <div class="suche"><input id="vSuche" type="search" placeholder="Bezeichnung, Spule, Datei …" value="${esc(verlaufSuche)}"></div>
    <div id="vListe"></div>`);
  const zeigen = () => {
    const q = verlaufSuche.toLowerCase();
    const treffer = buchungen.filter(b => !q || [b.bezeichnung, b.filamentCode, b.filamentText, b.dateiName, b.notiz, b.art]
      .some(x => (x || '').toLowerCase().includes(q))).slice(0, 200);
    $('#vListe').innerHTML = treffer.length ? treffer.map(b => wischen(`<button class="karte buchung-eintrag" data-buchung="${b.id}">
        <div class="zeile">
          <div style="min-width:0"><b>${esc(b.bezeichnung || b.art)}${b.foto ? ' 📷' : ''}</b>
            <small class="leise">${datumZeit(b.datum)} · ${esc(b.filamentCode)} ${esc(b.filamentText)}</small></div>
          <div class="rest"><b>${b.gramm < 0 ? '+' + zahl(-b.gramm) : zahl(b.gramm)} g</b><br><small class="leise">${b.art === 'Druck' ? euro(b.kosten) : esc(b.art)}</small></div>
        </div>
      </button>`, 'buchung', b.id, b.bezeichnung || b.art)).join('') +
      (buchungen.length > 200 && !q ? '<p class="leer"><small>Die ältesten Einträge findest du über die Suche.</small></p>' : '')
      : '<p class="leer">Keine Einträge.</p>';
  };
  $('#vSuche').oninput = e => { verlaufSuche = e.target.value; zeigen(); };
  zeigen();
}

async function buchungZeigen(id) {
  let b;
  try { b = (await holen('buchungen')).find(x => x.id === id); } catch (e) { return fehler(e); }
  if (!b) return toast('Diesen Eintrag gibt es nicht mehr.', true);
  const zeilen = [['Datum', datumZeit(b.datum)], ['Art', b.art], ['Spule', `${b.filamentCode} ${b.filamentText}`.trim()],
    ['Verbrauch', `${zahl(b.gramm)} g`], ['Kosten', b.art === 'Druck' ? euro(b.kosten) : ''], ['Druckzeit', dauer(b.druckzeit)],
    ['Datei', b.dateiName], ['Notiz', b.notiz]].filter(z => z[1]);
  blattOeffnen(`<div class="zeile"><h2>${esc(b.bezeichnung || b.art)}</h2><button class="knopf" data-schliessen>Schließen</button></div>
    ${b.foto ? '<img class="druckfoto" id="vFoto" alt="Foto vom Druckende">' : ''}
    <dl>${zeilen.map(z => `<dt>${z[0]}</dt><dd>${esc(z[1])}</dd>`).join('')}</dl>
    <div class="knoepfe"><button class="knopf gefahr" id="vLoeschen">Löschen …</button></div>`);
  if (b.foto) fotoLaden(b.foto, $('#vFoto'));
  $('#vLoeschen').onclick = () => buchungLoeschen(id);
}

function buchungLoeschen(id) {
  blattOeffnen(`<h2>Eintrag löschen</h2>
    <p class="leise">Soll der Verbrauch der Spule wieder gutgeschrieben werden (z. B. bei einer falschen Buchung)?</p>
    <div class="knoepfe senkrecht">
      <button class="primaer" data-loeschen="1">Löschen und zurückbuchen</button>
      <button class="knopf" data-loeschen="0">Nur aus dem Verlauf löschen</button>
      <button class="knopf" data-schliessen>Abbrechen</button>
    </div>`);
  document.querySelectorAll('[data-loeschen]').forEach(k => k.onclick = async () => {
    try {
      await schicken(`buchungen/${id}?zurueckbuchen=${k.dataset.loeschen === '1'}`, 'DELETE');
      blattSchliessen();
      toast(k.dataset.loeschen === '1' ? 'Gelöscht und zurückgebucht.' : 'Gelöscht.');
      navigieren();
    } catch (e) { fehler(e); }
  });
}

// ------------------------------------------------------------------ Nachkauf: Sorten, die knapp werden

let nachkaufAlle = false;
const NACHKAUF_FARBE = { Nachkaufen: 'rot', Bald: 'gelb', Genug: 'gruen' };

async function nachkaufSeite() {
  titel('Nachkauf');
  inhalt(reiter('spulen') + '<p class="leer">Lade …</p>');
  let sorten;
  try { sorten = await holen('nachkauf'); } catch (e) { return fehler(e); }
  const knapp = sorten.filter(s => s.status === 'Nachkaufen');
  const zeigen = () => {
    const liste = nachkaufAlle ? sorten : sorten.filter(s => s.status !== 'Genug');
    $('#nListe').innerHTML = liste.length ? liste.map(s => `<section class="karte">
        <div class="zeile">
          <div style="min-width:0"><b>${esc(s.bezeichnung)}</b>
            <small class="leise">Ø ${String(s.durchmesser).replace('.', ',')} mm · ${s.spulen} Spule${s.spulen === 1 ? '' : 'n'} · ${zahl(s.rest)} g · ${esc(s.reichweiteText)}</small>
            <small class="leise">${s.proMonat ? zahl(s.proMonat) + ' g/Monat · ' : ''}zuletzt ${euro(s.letzterPreis)}</small></div>
          <span class="badge ${NACHKAUF_FARBE[s.status] || 'grau'}">${esc(s.status)}</span>
        </div>
        <div class="knoepfe">
          ${s.shopLink ? `<a class="knopf" href="${esc(s.shopLink)}" target="_blank" rel="noopener">Shop öffnen</a>` : ''}
          <button class="primaer" data-gekauft="${esc(s.schluessel)}">Gekauft – neue Spule</button>
        </div>
      </section>`).join('')
      : '<p class="leer">Alles ausreichend vorrätig. 👍</p>';
  };
  inhalt(reiter('spulen') + `
    <div class="zeile liste-kopf">
      <label class="schalter"><input type="checkbox" id="nAlle" ${nachkaufAlle ? 'checked' : ''}> auch ausreichende Sorten</label>
      ${knapp.length ? '<button class="knopf" id="nListeKopieren">Einkaufsliste</button>' : ''}
    </div>
    <div id="nListe"></div>`);
  $('#nAlle').onchange = e => { nachkaufAlle = e.target.checked; zeigen(); };
  if ($('#nListeKopieren')) $('#nListeKopieren').onclick = async () => {
    const text = 'Filament nachkaufen:\n' + knapp.map(s => `- ${s.bezeichnung} (Ø ${String(s.durchmesser).replace('.', ',')} mm)${s.shopLink ? ' ' + s.shopLink : ''}`).join('\n');
    try {
      if (navigator.share) await navigator.share({ title: 'Einkaufsliste Filament', text });
      else { await navigator.clipboard.writeText(text); toast('Einkaufsliste kopiert.'); }
    } catch (e) { if (e.name !== 'AbortError') toast('Teilen nicht möglich: ' + e.message, true); }
  };
  nachkaufSorten = sorten;
  zeigen();
}

let nachkaufSorten = [];
async function gekauft(schluessel) {
  const s = nachkaufSorten.find(x => x.schluessel === schluessel);
  if (!s || !s.vorlage) return;
  try { spuleFormular(s.vorlage, await holen('filament'), true); } catch (e) { fehler(e); }
}

// ------------------------------------------------------------------ Dateien vom Handy hochladen (in Stücken, wie die App)

async function dateienHochladen(dateien) {
  if (!dateien.length) return;
  const STUECK = 16 * 1024 * 1024;   // wie Api.UploadStueckBytes – unter der 100-MB-Grenze von Cloudflare
  blattOeffnen(`<h2>Hochladen</h2><p id="uText" class="leise"></p><div class="balken"><div id="uBalken" style="width:0"></div></div>`);
  let fertig = 0;
  for (const datei of dateien) {
    try {
      const id = (await (await schicken('uploads', 'POST', {})).json()).id;
      for (let offset = 0; offset < datei.size || offset === 0; offset += STUECK) {
        $('#uText').textContent = `${datei.name} – ${prozent(datei.size ? offset / datei.size : 0)}`;
        $('#uBalken').style.width = (datei.size ? offset / datei.size * 100 : 0) + '%';
        const antwort = await fetch(`/api/uploads/${id}?offset=${offset}`, {
          method: 'PUT', body: datei.slice(offset, offset + STUECK),
          headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/octet-stream' } });
        if (!antwort.ok) throw new Error((await antwort.text()) || `Fehler ${antwort.status}`);
        if (datei.size === 0) break;
      }
      $('#uBalken').style.width = '100%';
      await schicken(`uploads/${id}/fertig`, 'POST', { name: datei.name });
      fertig++;
    } catch (e) {
      toast(`${datei.name}: ${e.message}`, true);
    }
  }
  blattSchliessen();
  if (fertig) toast(`${fertig} Datei${fertig === 1 ? '' : 'en'} hochgeladen.`);
  navigieren();
}

starten();
