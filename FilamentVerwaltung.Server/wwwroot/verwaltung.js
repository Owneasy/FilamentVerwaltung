'use strict';
// Verwaltung im Browser – was sonst nur die Windows-App kann: Statistik, Wartung, Drucker anlegen, Drucke von Hand
// erfassen, Stromkosten, Home Assistant, Sicherungen und Datei-Details. Nutzt die Helfer aus app.js
// ($, esc, holen, schicken, api, inhalt, titel, blattOeffnen, toast, fehler …).

Object.assign(VERWALTUNG_SEITEN, {
  uebersicht: uebersichtSeite, mehr: mehrSeite, statistik: statistikSeite, wartung: wartungSeite, geraete: geraeteSeite,
  strom: stromSeite, homeassistant: haSeite, sicherungen: sicherungenSeite
});

const DRUCKER_TYPEN = [['Bambu', 'Bambu Lab (LAN, MQTT)'], ['PrusaLink', 'Prusa (PrusaLink)'], ['Moonraker', 'Klipper (Moonraker)'], ['OctoPrint', 'OctoPrint']];
const druckerTypName = typ => (DRUCKER_TYPEN.find(t => t[0] === typ) || [typ, typ])[1];
const zurueck = (ziel = 'mehr', text = 'Mehr') => `<a class="zurueck" href="#${ziel}">‹ ${esc(text)}</a>`;
const zahlKomma = (x, stellen = 0) => (x || 0).toLocaleString('de-DE', { minimumFractionDigits: stellen, maximumFractionDigits: stellen });
/** Datum für die API ohne Zeitzone (der Server speichert Ortszeit, wie die App). */
const ortszeit = d => new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 19);
const tageText = t => t < 1 ? 'heute' : t < 60 ? `${Math.round(t)} Tagen` : t < 730 ? `${Math.round(t / 30.4)} Monaten` : 'über 2 Jahren';

// ------------------------------------------------------------------ Mehr: Übersicht der weiteren Bereiche

async function mehrSeite() {
  titel('Mehr');
  const eintrag = (ziel, symbol, name, text, zusatz = '') => `<a class="karte mehr-eintrag" ${ziel.startsWith('#') ? `href="${ziel}"` : `href="#" data-aktion="${ziel}"`}>
      <span class="mehr-symbol">${symbol}</span>
      <span class="mehr-text"><b>${esc(name)}</b><small class="leise">${esc(text)}</small></span>
      ${zusatz}<span class="mehr-pfeil">›</span></a>`;
  inhalt(`<div id="mListe">
    ${eintrag('#uebersicht', '🏁', 'Übersicht', 'Lager, Drucker, Verbrauch und Nachkauf auf einen Blick')}
    ${eintrag('#statistik', '📊', 'Statistik', 'Verbrauch, Kosten, Fehldrucke und Reichweite')}
    ${eintrag('#wartung', '🔧', 'Wartung', 'Aufgaben, Herstellerpläne und Druckstunden', '<span id="mWartung"></span>')}
    ${eintrag('druck', '✍️', 'Druck erfassen', 'Einen Druck von Hand buchen')}
    ${eintrag('#geraete', '🖨️', 'Drucker verwalten', 'Anlegen, Verbindung testen, Leistung')}
    <h3 class="mehr-gruppe">Einstellungen</h3>
    ${eintrag('#strom', '⚡', 'Stromkosten', 'Strompreis und Leistung der Drucker')}
    ${eintrag('push', '🔔', 'Benachrichtigungen', 'Was aufs Handy gemeldet wird')}
    ${eintrag('#homeassistant', '🏠', 'Home Assistant', 'Drucker und Lager per MQTT melden')}
    ${eintrag('#sicherungen', '💾', 'Sicherungen', 'Datenbank sichern und wiederherstellen')}
    ${eintrag('#status', '🟢', 'Server-Status', 'Version, Dienste und Verbindungen')}
  </div>`);
  document.querySelectorAll('[data-aktion]').forEach(a => a.onclick = e => {
    e.preventDefault();
    if (a.dataset.aktion === 'druck') druckFormular();
    else benachrichtigungenZeigen();
  });
  try {
    const faellig = (await holen('wartung')).filter(w => w.faellig).length;
    if (faellig && $('#mWartung')) $('#mWartung').outerHTML = `<span class="badge rot">${faellig} fällig</span>`;
  } catch { /* ohne Zahl */ }
}

/** Rote Zahl an "Mehr": fällige Wartungen (alle 5 Minuten, wie in der App). */
async function wartungZaehlen() {
  if (!token || $('#leiste').hidden) return;
  try {
    const n = (await holen('wartung')).filter(w => w.faellig).length;
    for (const z of [$('#zahlMehr'), $('#slWartung')]) {
      z.textContent = n;
      z.hidden = !n;
    }
  } catch { /* älterer Server oder offline */ }
}

// Seitenleiste (breite Bildschirme): Aktionen ohne eigene Seite und die Version unten
document.addEventListener('DOMContentLoaded', () => {
  setTimeout(wartungZaehlen, 1500);
  setInterval(wartungZaehlen, 300000);
  document.querySelectorAll('[data-sl-aktion]').forEach(a => a.onclick = e => {
    e.preventDefault();
    if (a.dataset.slAktion === 'druck') druckFormular(); else benachrichtigungenZeigen();
  });
  fetch('/api/info').then(r => r.json()).then(i => { $('#slVersion').textContent = 'Version ' + i.version; }).catch(() => {});
});

// ------------------------------------------------------------------ Übersicht (Startseite der App)

/** Kalenderwoche nach ISO 8601. */
function kalenderwoche(d) {
  const t = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
  t.setUTCDate(t.getUTCDate() + 4 - (t.getUTCDay() || 7));
  return Math.ceil(((t - Date.UTC(t.getUTCFullYear(), 0, 1)) / 86400000 + 1) / 7);
}

/** Karte mit Titel, Untertitel und Link "Alle anzeigen" wie die Listenkarten der App. */
const listenKarte = (titelText, untertitel, ziel, linkText, inhaltHtml) => `<section class="karte">
    <div class="zeile karten-kopf"><div><h2>${esc(titelText)}</h2>${untertitel ? `<small class="leise">${esc(untertitel)}</small>` : ''}</div>
      <a class="link-klein" href="#${ziel}">${esc(linkText)} ›</a></div>
    ${inhaltHtml}</section>`;

async function uebersichtSeite() {
  titel('Übersicht');
  const jetzt = new Date(), stunde = jetzt.getHours();
  const gruss = `${stunde < 11 ? 'Guten Morgen' : stunde < 18 ? 'Hallo' : 'Guten Abend'} – ${jetzt.toLocaleDateString('de-DE', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}`;
  inhalt(`<p class="leise untertitel">${esc(gruss)}</p><p class="leer">Lade …</p>`);
  let spulen, buchungen, sorten, drucker;
  try {
    [spulen, buchungen, sorten, drucker] = await Promise.all([holen('filament'), holen('buchungen'), holen('nachkauf').catch(() => []), holen('drucker')]);
  } catch (e) { return fehler(e); }

  // Kennzahlen
  const mitRest = spulen.filter(f => f.restgewicht > 0), fastLeer = mitRest.filter(f => f.restgewicht < 150).length;
  const monatsStart = new Date(jetzt.getFullYear(), jetzt.getMonth(), 1);
  const imMonat = buchungen.filter(b => istVerbrauch(b) && new Date(b.datum) >= monatsStart);
  const druckeMonat = imMonat.filter(b => b.art === 'Druck').length, kostenMonat = imMonat.reduce((s, b) => s + b.kosten, 0);
  const nachkaufen = sorten.filter(s => s.status === 'Nachkaufen'), bald = sorten.filter(s => s.status === 'Bald');

  // Verbrauch pro Woche (12 Kalenderwochen, aktuelle zuletzt)
  const montag = new Date(jetzt.getFullYear(), jetzt.getMonth(), jetzt.getDate() - (jetzt.getDay() + 6) % 7);
  const wochen = [];
  for (let i = 11; i >= 0; i--) {
    const von = new Date(montag.getFullYear(), montag.getMonth(), montag.getDate() - 7 * i), bis = new Date(von.getFullYear(), von.getMonth(), von.getDate() + 7);
    const woche = buchungen.filter(b => istVerbrauch(b) && new Date(b.datum) >= von && new Date(b.datum) < bis);
    const g = woche.reduce((s, b) => s + b.gramm, 0), kw = kalenderwoche(von);
    const tag = d => d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' });
    wochen.push({ wert: g, kurz: grammKurz(g), text: grammKurz(g), monat: String(kw),
      titel: `KW ${kw} (${tag(von)} – ${tag(new Date(bis - 86400000))})`, zusatz: `${woche.filter(b => b.art === 'Druck').length} Drucke` });
  }

  const letzte = buchungen.filter(b => b.art === 'Druck').sort((a, b) => b.datum.localeCompare(a.datum)).slice(0, 6);
  const knapp = [...nachkaufen, ...bald].sort((a, b) => (a.status === 'Nachkaufen' ? 0 : 1) - (b.status === 'Nachkaufen' ? 0 : 1) || a.rest - b.rest).slice(0, 6);

  inhalt(`<p class="leise untertitel">${esc(gruss)}</p>
    <div class="kennzahlen vier">
      <a href="#spulen"><span class="kz-titel">Lagerbestand</span><b>${grammKurz(mitRest.reduce((s, f) => s + f.restgewicht, 0))}</b>
        <small>${mitRest.length} ${mitRest.length === 1 ? 'Spule' : 'Spulen'} mit Rest${fastLeer ? ` · ${fastLeer} fast leer` : ''}</small></a>
      <a href="#drucker" id="uDruckerKz"><span class="kz-titel">Drucker</span><b>…</b><small>&nbsp;</small></a>
      <a href="#verlauf"><span class="kz-titel">Verbrauch diesen Monat</span><b>${grammKurz(imMonat.reduce((s, b) => s + b.gramm, 0))}</b>
        <small>${druckeMonat} ${druckeMonat === 1 ? 'Druck' : 'Drucke'}${kostenMonat > 0 ? ' · ' + euro(kostenMonat) : ''}</small></a>
      <a href="#nachkauf"><span class="kz-titel">Nachkaufen</span><b class="${nachkaufen.length ? 'fehler-text' : 'gut-text'}">${nachkaufen.length ? `${nachkaufen.length} ${nachkaufen.length === 1 ? 'Sorte' : 'Sorten'}` : 'Alles da'}</b>
        <small>${bald.length ? `${bald.length} weitere bald knapp` : nachkaufen.length ? 'jetzt nachbestellen' : 'nichts wird knapp'}</small></a>
    </div>
    <div class="uebersicht-raster">
      ${listenKarte('Drucker', 'Live-Status', 'drucker', 'Zu den Druckern', '<div id="uDrucker" class="ue-liste"><p class="leise">Verbinde …</p></div>')}
      <section class="karte"><h2>Verbrauch pro Woche</h2><small class="leise">Letzte 12 Kalenderwochen · antippen für Details</small>
        ${saeulen(wochen, 'Noch kein Verbrauch erfasst – buche Drucke oder wiege Spulen nach.')}<p class="leise saeulen-info"></p></section>
      ${listenKarte('Letzte Drucke', '', 'verlauf', 'Alle Drucke', letzte.length ? `<div class="ue-liste">${letzte.map(b => `<button class="ue-zeile" data-buchung="${b.id}">
          <span class="ue-marke">${new Date(b.datum).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' })}</span>
          <span class="ue-text"><b>${esc(b.bezeichnung || b.dateiName || 'Druck')}</b><small class="leise">${esc(b.filamentCode)} · ${esc(b.filamentText)}</small></span>
          <span class="ue-wert">${zahl(b.gramm)} g</span></button>`).join('')}</div>` : '<p class="leise">Noch keine Drucke gebucht.</p>')}
      ${listenKarte('Nachkaufen', 'Sorten, die knapp werden', 'nachkauf', 'Zur Nachkaufliste', knapp.length ? `<div class="ue-liste">${knapp.map(s => `<a class="ue-zeile" href="#nachkauf">
          <span class="badge ${s.status === 'Nachkaufen' ? 'rot' : 'gelb'}">${s.status === 'Nachkaufen' ? 'Nachkaufen' : 'Bald'}</span>
          <span class="ue-text"><b>${esc(s.bezeichnung)}</b><small class="leise">${s.spulen} ${s.spulen === 1 ? 'Spule' : 'Spulen'} · ${s.rest <= 0 ? 'aufgebraucht' : esc(s.reichweiteText)}</small></span>
          <span class="ue-wert">${zahl(s.rest)} g</span></a>`).join('')}</div>` : '<p class="leise">Alles gut – keine Sorte wird knapp.</p>')}
    </div>`);
  document.querySelectorAll('.saeule').forEach(s => s.onclick = () => { s.closest('section').querySelector('.saeulen-info').textContent = s.dataset.info; });

  // Drucker live (alle 5 s, solange die Seite offen ist)
  const aktive = drucker.filter(d => d.aktiv);
  const live = async () => {
    if (!$('#uDrucker')) return;
    let status = [];
    try { status = await holen('drucker/live'); } catch { return; }
    const von = id => (status.find(l => l.druckerId === id) || {}).status || { zustand: 0 };
    const laeuft = s => s.zustand === DRUCKT || s.zustand === PAUSIERT;
    const liste = aktive.map(d => ({ d, s: von(d.id) })).sort((a, b) => laeuft(b.s) - laeuft(a.s) || a.d.name.localeCompare(b.d.name));
    const druckend = liste.filter(x => laeuft(x.s)), offline = liste.filter(x => x.s.zustand === OFFLINE || x.s.zustand === 0).length;
    const kz = $('#uDruckerKz');
    if (!aktive.length) kz.innerHTML = '<span class="kz-titel">Drucker</span><b>–</b><small>kein Drucker angebunden</small>';
    else if (druckend.length) {
      const rest = Math.min(...druckend.map(x => x.s.restSekunden || Infinity));
      kz.innerHTML = `<span class="kz-titel">Drucker</span><b class="blau-text">${druckend.length === 1 ? '1 Druck läuft' : druckend.length + ' Drucke laufen'}</b>
        <small>${rest < Infinity ? 'fertig in ' + dauer(rest) : `von ${aktive.length} Druckern`}</small>`;
    } else kz.innerHTML = `<span class="kz-titel">Drucker</span><b>${offline === aktive.length ? 'Offline' : 'Bereit'}</b>
        <small>${aktive.length} Drucker${offline && offline < aktive.length ? ` · ${offline} offline` : ''}</small>`;
    $('#uDrucker').innerHTML = !aktive.length ? '<p class="leise">Noch kein Drucker angebunden – unter „Drucker verwalten“ hinzufügen.</p>'
      : liste.map(({ d, s }) => {
        const z = s.zustand, l = laeuft(s);
        const info = l ? [prozent(s.fortschritt), s.restSekunden ? 'noch ' + dauer(s.restSekunden) : '', s.schicht ? `Schicht ${s.schicht}${s.schichtenGesamt ? ' / ' + s.schichtenGesamt : ''}` : ''].filter(Boolean).join(' · ')
          : z === OFFLINE ? 'Nicht erreichbar' : z === FERTIG && s.datei ? 'Fertig: ' + s.datei : 'Kein Druckauftrag';
        return `<a class="ue-drucker" href="#drucker">
          <div class="zeile"><b>${esc(d.name)}</b><span class="badge ${ZUSTAND_FARBE[z] || 'grau'}">${ZUSTAND[z] || '?'}</span></div>
          ${l ? `<div class="datei">${esc(s.datei || '')}</div><div class="balken"><div style="width:${s.fortschritt * 100}%"></div></div>` : ''}
          <small class="leise">${esc(info)}</small></a>`;
      }).join('');
  };
  await live();
  if (!document.hidden) seitenTimer = setInterval(live, 5000);
}

// ------------------------------------------------------------------ Statistik (wie die Seite in der App)

const TAGE_BASIS = 90;

/** Waagerechte Balken: [{ name, wert, text, zusatz }] – Länge relativ zum größten Wert. */
function balkenListe(werte, leer) {
  if (!werte.length) return `<p class="leise">${esc(leer)}</p>`;
  const max = Math.max(...werte.map(w => w.wert), 1e-9);
  return `<div class="balken-liste">${werte.map(w => `<div class="balken-zeile">
      <div class="zeile"><span>${esc(w.name)}</span><b>${esc(w.text)}</b></div>
      <div class="balken"><div style="width:${Math.max(1.5, w.wert / max * 100)}%"></div></div>
      ${w.zusatz ? `<small class="leise">${esc(w.zusatz)}</small>` : ''}
    </div>`).join('')}</div>`;
}

/** Senkrechte Säulen für 12 Monate; Antippen zeigt den Wert. */
function saeulen(werte, leer, farbe = '') {
  if (!werte.some(w => w.wert > 0)) return `<p class="leise">${esc(leer)}</p>`;
  const max = Math.max(...werte.map(w => w.wert));
  return `<div class="saeulen ${farbe}">${werte.map(w => `<button class="saeule" data-info="${esc(w.titel + ': ' + w.text + (w.zusatz ? ' · ' + w.zusatz : ''))}">
      <span class="saeule-wert">${w.wert > 0 ? esc(w.kurz) : ''}</span>
      <span class="saeule-balken" style="height:${w.wert > 0 ? Math.max(3, w.wert / max * 100) : 0}%"></span>
      <small>${esc(w.monat)}</small></button>`).join('')}</div>`;
}

const grammKurz = g => g >= 1000 ? zahlKomma(g / 1000, 1) + ' kg' : zahl(g) + ' g';

async function statistikSeite() {
  titel('Statistik');
  inhalt(zurueck() + '<p class="leer">Lade …</p>');
  let buchungen, spulen;
  try { [buchungen, spulen] = await Promise.all([holen('buchungen'), holen('filament')]); } catch (e) { return fehler(e); }
  const heute = new Date(); heute.setHours(0, 0, 0, 0);
  const basisStart = new Date(heute); basisStart.setDate(basisStart.getDate() - TAGE_BASIS + 1);
  const jahresStart = new Date(heute.getFullYear(), 0, 1);
  const verbrauch = buchungen.filter(istVerbrauch).map(b => ({ ...b, d: new Date(b.datum) }));
  const drucke = buchungen.filter(b => b.art === 'Druck').map(b => ({ ...b, d: new Date(b.datum) }));

  // Kennzahlen
  const basis = verbrauch.filter(b => b.d >= basisStart);
  const proTag = basis.reduce((s, b) => s + b.gramm, 0) / TAGE_BASIS;
  const lager = spulen.reduce((s, f) => s + Math.max(0, f.restgewicht), 0);
  const kostenJahr = verbrauch.filter(b => b.d >= jahresStart).reduce((s, b) => s + b.kosten, 0);
  const druckeJahr = drucke.filter(b => b.d >= jahresStart);
  const fehlJahr = druckeJahr.filter(b => b.fehldruck);
  const stundenJahr = druckeJahr.reduce((s, b) => s + (b.druckzeit || 0), 0) / 3600;

  // 12 Monate
  const monate = [];
  for (let i = 11; i >= 0; i--) {
    const beginn = new Date(heute.getFullYear(), heute.getMonth() - i, 1), ende = new Date(heute.getFullYear(), heute.getMonth() - i + 1, 1);
    const im = verbrauch.filter(b => b.d >= beginn && b.d < ende);
    const fehl = drucke.filter(b => b.fehldruck && b.d >= beginn && b.d < ende);
    const alle = drucke.filter(b => b.d >= beginn && b.d < ende).length;
    monate.push({ beginn, im, fehl, alle });
  }
  const monatName = (d, lang) => d.toLocaleString('de-DE', lang ? { month: 'long', year: 'numeric' } : { month: 'short' }).replace('.', '');
  const zeitraumStart = monate[0].beginn;
  const zeitraum = verbrauch.filter(b => b.d >= zeitraumStart);

  // Nach Material (die 6 größten, Rest als "Andere")
  const materialVon = Object.fromEntries(spulen.map(f => [f.id, f.material || 'Ohne Angabe']));
  const nachMaterial = gruppieren(zeitraum, b => materialVon[b.filamentId] || 'Gelöschte Spulen');
  const gesamt = zeitraum.reduce((s, b) => s + b.gramm, 0) || 1;

  // Fehldrucke
  const fehlZeitraum = drucke.filter(b => b.fehldruck && b.d >= zeitraumStart);
  const druckeZeitraum = drucke.filter(b => b.d >= zeitraumStart);
  const nachGrund = gruppieren(fehlZeitraum.filter(b => b.fehldruckGrund), b => b.fehldruckGrund);
  const nachDrucker = gruppieren(fehlZeitraum, b => b.drucker || 'Ohne Angabe');

  // Reichweite je Spule (nur genutzte)
  const reichweite = spulen.filter(f => f.restgewicht > 0).map(f => {
    const g = basis.filter(b => b.filamentId === f.id).reduce((s, b) => s + b.gramm, 0) / TAGE_BASIS;
    return g > 0 ? { f, proWoche: g * 7, tage: f.restgewicht / g } : null;
  }).filter(Boolean).sort((a, b) => a.tage - b.tage);

  inhalt(zurueck() + `
    <div class="kennzahlen">
      <div><b>${grammKurz(proTag * 30.4)}</b><small>Ø pro Monat</small></div>
      <div><b>${proTag > 0 ? 'ca. ' + tageText(lager / proTag).replace(' Tagen', ' Tage').replace(' Monaten', ' Monate') : '–'}</b><small>Lager reicht</small></div>
      <div><b>${euro(kostenJahr)}</b><small>Kosten ${heute.getFullYear()}</small></div>
    </div>
    <div class="kennzahlen">
      <div><b>${druckeJahr.length}</b><small>Drucke ${heute.getFullYear()}</small></div>
      <div><b>${zahlKomma(stundenJahr)} h</b><small>Druckzeit</small></div>
      <div><b class="${fehlJahr.length ? 'fehler-text' : ''}">${fehlJahr.length}</b><small>Fehldrucke${druckeJahr.length ? ` (${Math.round(fehlJahr.length / druckeJahr.length * 100)} %)` : ''}</small></div>
    </div>
    <section class="karte"><h2>Verbrauch pro Monat</h2><small class="leise">Letzte 12 Monate · Drucke und Nachwiegen · antippen für Details</small>
      ${saeulen(monate.map(m => {
        const g = m.im.reduce((s, b) => s + b.gramm, 0), k = m.im.reduce((s, b) => s + b.kosten, 0), n = m.im.filter(b => b.art === 'Druck').length;
        return { wert: g, kurz: grammKurz(g), text: zahl(g) + ' g', zusatz: `Kosten ${euro(k)} · ${n} ${n === 1 ? 'Druck' : 'Drucke'}`, monat: monatName(m.beginn), titel: monatName(m.beginn, true) };
      }), 'Noch kein Verbrauch erfasst – buche Drucke oder wiege Spulen nach.')}
      <p class="leise saeulen-info" id="sInfo"></p></section>
    <section class="karte"><h2>Verbrauch nach Material</h2><small class="leise">Letzte 12 Monate · Anteil am Gesamtverbrauch</small>
      ${balkenListe(oberste(nachMaterial, 6).map(x => ({ name: x.name, wert: x.gramm, text: `${grammKurz(x.gramm)} · ${Math.round(x.gramm / gesamt * 100)} %`, zusatz: `Kosten ${euro(x.kosten)} · ${x.anzahl} Einträge` })), 'Noch kein Verbrauch erfasst.')}</section>
    <section class="karte"><h2>Fehldrucke pro Monat</h2><small class="leise">Letzte 12 Monate · verlorenes Filament</small>
      ${saeulen(monate.map(m => {
        const g = m.fehl.reduce((s, b) => s + b.gramm, 0);
        return { wert: g, kurz: grammKurz(g), text: zahl(g) + ' g', zusatz: `${m.fehl.length} von ${m.alle} Drucken · ${euro(m.fehl.reduce((s, b) => s + b.kosten, 0))}`, monat: monatName(m.beginn), titel: monatName(m.beginn, true) };
      }), 'Keine Fehldrucke in den letzten 12 Monaten.', 'rot')}</section>
    <section class="karte"><h2>Fehldrucke nach Grund</h2><small class="leise">Letzte 12 Monate · was am häufigsten schiefgeht</small>
      ${balkenListe(nachGrund.map(x => ({ name: x.name, wert: x.anzahl, text: `${x.anzahl}×`, zusatz: `${grammKurz(x.gramm)} · Kosten ${euro(x.kosten)}` })), 'Keine Fehldrucke mit Grund in den letzten 12 Monaten.')}</section>
    <section class="karte"><h2>Fehldrucke je Drucker</h2><small class="leise">Letzte 12 Monate · Anzahl und Anteil an allen Drucken</small>
      ${balkenListe(nachDrucker.map(x => {
        const alle = druckeZeitraum.filter(b => (b.drucker || 'Ohne Angabe').toLowerCase() === x.name.toLowerCase()).length;
        return { name: x.name, wert: x.anzahl, text: `${x.anzahl} von ${alle}`, zusatz: `${alle ? Math.round(x.anzahl / alle * 100) : 0} % · ${grammKurz(x.gramm)} · Kosten ${euro(x.kosten)}` };
      }), 'Keine Fehldrucke in den letzten 12 Monaten.')}</section>
    <section class="karte"><h2>Reichweite je Spule</h2><small class="leise">Beim Verbrauch der letzten ${TAGE_BASIS} Tage – nur Spulen, die in dieser Zeit genutzt wurden</small>
      ${reichweite.length ? `<div class="reichweite">${reichweite.map(r => `<a class="reichweite-zeile" href="#spule/${encodeURIComponent(r.f.code)}">
          <div><b>${esc(r.f.code)}</b> <span class="leise">${esc(spulenText(r.f))}</span>
            <small class="leise">${zahl(r.f.restgewicht)} g · Ø ${zahl(r.proWoche)} g pro Woche</small></div>
          <span class="badge ${r.tage < 14 ? 'rot' : r.tage < 45 ? 'gelb' : 'gruen'}">leer in ${tageText(r.tage)}</span></a>`).join('')}</div>`
        : `<p class="leise">In den letzten ${TAGE_BASIS} Tagen wurde keine Spule genutzt.</p>`}</section>`);
  document.querySelectorAll('.saeule').forEach(s => s.onclick = () => {
    const info = s.closest('section').querySelector('.saeulen-info') || s.closest('.saeulen');
    if (info.classList.contains('saeulen-info')) info.textContent = s.dataset.info; else toast(s.dataset.info);
  });
}

function gruppieren(liste, schluessel) {
  const map = new Map();
  for (const b of liste) {
    const name = schluessel(b), k = name.toLowerCase();
    const g = map.get(k) || { name, gramm: 0, kosten: 0, anzahl: 0 };
    g.gramm += Math.max(0, b.gramm); g.kosten += b.kosten || 0; g.anzahl++;
    map.set(k, g);
  }
  return [...map.values()].sort((a, b) => b.anzahl - a.anzahl || b.gramm - a.gramm);
}

function oberste(gruppen, n) {
  const sortiert = [...gruppen].sort((a, b) => b.gramm - a.gramm);
  if (sortiert.length <= n) return sortiert;
  const rest = sortiert.slice(n - 1);
  return [...sortiert.slice(0, n - 1), { name: 'Andere', gramm: rest.reduce((s, x) => s + x.gramm, 0), kosten: rest.reduce((s, x) => s + x.kosten, 0), anzahl: rest.reduce((s, x) => s + x.anzahl, 0) }];
}

// ------------------------------------------------------------------ Wartung: Aufgaben je Drucker, Herstellerpläne, Protokoll

async function wartungSeite() {
  titel('Wartung');
  inhalt(zurueck() + '<p class="leer">Lade …</p>');
  let drucker, wartung, protokoll;
  try {
    [drucker, wartung, protokoll] = await Promise.all([holen('drucker'), holen('wartung'), holen('wartung/protokoll').catch(() => [])]);
  } catch (e) { return fehler(e); }
  if (!drucker.length) return inhalt(zurueck() + '<p class="leer">Noch kein Drucker angelegt.<br><small>Unter „Mehr → Drucker verwalten“ anlegen.</small></p>');
  const karte = d => {
    const liste = wartung.filter(w => w.druckerId === d.id).sort((a, b) => b.anteil - a.anteil);
    return `<section class="karte">
      <div class="zeile"><div><h2>${esc(d.name)}</h2><small class="leise">${zahlKomma((d.drucksekunden || 0) / 3600)} Druckstunden gezählt</small></div>
        <button class="knopf klein" data-stunden="${d.id}">Stunden ändern</button></div>
      ${liste.length ? `<div class="wartung">${liste.map(w => `<div class="wartung-zeile">
          <button class="link-zeile" data-wartung-bearbeiten="${w.id}">
            <b>${esc(w.name)}</b>
            <small class="${w.faellig ? 'fehler-text' : w.bald ? 'gelb-text' : 'leise'}">${esc(w.restText)} · ${esc(w.intervallText)}</small>
            <div class="balken ${w.faellig ? 'knapp' : ''}"><div style="width:${Math.min(100, w.anteil * 100)}%"></div></div>
          </button>
          <button class="knopf klein" data-wartung="${w.id}">Erledigt</button>
        </div>`).join('')}</div>` : '<p class="leise">Noch keine Wartungsaufgaben.</p>'}
      <div class="knoepfe"><button class="knopf" data-wartung-neu="${d.id}">+ Aufgabe</button><button class="knopf" data-plan="${d.id}">Herstellerplan …</button></div>
    </section>`;
  };
  inhalt(zurueck() + drucker.map(karte).join('') + `<section class="karte"><h2>Protokoll</h2>
      ${protokoll.length ? `<ul class="protokoll">${protokoll.slice(0, 40).map(p => `<li><b>${esc(p.name)}</b>
          <small class="leise">${esc(p.druckerName)} · ${new Date(p.datum).toLocaleDateString('de-DE')} · bei ${zahlKomma(p.druckstunden)} h${p.notiz ? ' · ' + esc(p.notiz) : ''}</small></li>`).join('')}</ul>`
        : '<p class="leise">Noch nichts erledigt.</p>'}</section>`);
  document.querySelectorAll('[data-stunden]').forEach(b => b.onclick = () => druckstundenAendern(drucker.find(d => d.id === +b.dataset.stunden)));
  document.querySelectorAll('[data-wartung-bearbeiten]').forEach(b => b.onclick = () => wartungFormular(wartung.find(w => w.id === +b.dataset.wartungBearbeiten), drucker));
  document.querySelectorAll('[data-wartung-neu]').forEach(b => b.onclick = () => wartungFormular({ druckerId: +b.dataset.wartungNeu }, drucker));
  document.querySelectorAll('[data-plan]').forEach(b => b.onclick = () => herstellerPlan(drucker.find(d => d.id === +b.dataset.plan)));
}

async function druckstundenAendern(d) {
  const text = prompt(`Druckstunden von „${d.name}“ (z. B. vom Display des Druckers ablesen):`, zahlKomma((d.drucksekunden || 0) / 3600, 1));
  if (text == null) return;
  const stunden = parseFloat(text.replace(/\./g, '').replace(',', '.'));
  if (!(stunden >= 0)) return toast('Bitte eine Zahl eingeben.', true);
  try { await schicken(`drucker/${d.id}/druckstunden`, 'PUT', { wert: String(Math.round(stunden * 3600)) }); } catch (e) { return fehler(e); }
  toast('Druckstunden gespeichert.');
  wartungSeite();
}

async function wartungFormular(w, drucker) {
  const neu = !w.id;
  let vorlagen = [];
  if (neu) { try { vorlagen = (await holen('wartung/plaene')).vorlagen; } catch { /* ohne Vorlagen */ } }
  blattOeffnen(`<div class="zeile"><h2>${neu ? 'Neue Wartungsaufgabe' : 'Wartungsaufgabe'}</h2><button class="knopf" data-schliessen>Abbrechen</button></div>
    ${neu && vorlagen.length ? `<label>Vorlage<select id="wVorlage"><option value="">– eigene Aufgabe –</option>
      ${vorlagen.map((v, i) => `<option value="${i}">${esc(v.name)}</option>`).join('')}</select></label>` : ''}
    <label>Drucker<select id="wDrucker" ${neu ? '' : 'disabled'}>${drucker.map(d => `<option value="${d.id}" ${d.id === w.druckerId ? 'selected' : ''}>${esc(d.name)}</option>`).join('')}</select></label>
    <label>Aufgabe<input id="wName" value="${esc(w.name || '')}" placeholder="z. B. Düse reinigen"></label>
    <div class="zwei">
      <label>Alle … Druckstunden<input id="wStunden" type="number" inputmode="decimal" min="0" step="any" value="${w.intervallStunden ?? ''}"></label>
      <label>und/oder alle … Tage<input id="wTage" type="number" inputmode="numeric" min="0" value="${w.intervallTage ?? ''}"></label>
    </div>
    <p class="leise klein-text">Fällig ist, was zuerst eintritt. Leer lassen = nicht danach.</p>
    <label>Notiz<input id="wNotiz" value="${esc(w.notiz || '')}"></label>
    ${neu ? '' : `<p class="leise klein-text">Zuletzt erledigt: ${new Date(w.erledigtAm).toLocaleDateString('de-DE')} · ${esc(w.restText)}</p>`}
    <div class="knoepfe">${neu ? '' : '<button class="knopf gefahr" id="wLoeschen">Löschen</button>'}<button class="primaer" id="wOk">Speichern</button></div>`);
  if ($('#wVorlage')) $('#wVorlage').onchange = () => {
    const v = vorlagen[$('#wVorlage').value === '' ? -1 : +$('#wVorlage').value];
    if (!v) return;
    $('#wName').value = v.name; $('#wStunden').value = v.stunden ?? ''; $('#wTage').value = v.tage ?? '';
  };
  $('#wOk').onclick = async () => {
    const name = $('#wName').value.trim();
    const stunden = parseFloat($('#wStunden').value) || null, tage = parseInt($('#wTage').value, 10) || null;
    if (!name) return toast('Bitte die Aufgabe eintragen.', true);
    if (!stunden && !tage) return toast('Bitte Druckstunden oder Tage als Intervall eintragen.', true);
    try {
      await schicken('wartung', 'POST', { ...w, druckerId: +$('#wDrucker').value, name, intervallStunden: stunden, intervallTage: tage, notiz: $('#wNotiz').value.trim() });
    } catch (e) { return fehler(e); }
    blattSchliessen();
    toast('Gespeichert.');
    wartungSeite(); wartungZaehlen();
  };
  if ($('#wLoeschen')) $('#wLoeschen').onclick = async () => {
    if (!confirm(`„${w.name}“ löschen? Das Protokoll bleibt erhalten.`)) return;
    try { await schicken(`wartung/${w.id}`, 'DELETE'); } catch (e) { return fehler(e); }
    blattSchliessen();
    wartungSeite(); wartungZaehlen();
  };
}

async function herstellerPlan(d) {
  let plaene;
  try { plaene = (await holen('wartung/plaene?typ=' + encodeURIComponent(d.typ))).plaene; } catch (e) { return fehler(e); }
  blattOeffnen(`<div class="zeile"><h2>Herstellerplan für ${esc(d.name)}</h2><button class="knopf" data-schliessen>Abbrechen</button></div>
    <p class="leise">Übernimmt die Wartungsaufgaben aus der Dokumentation des Herstellers. Aufgaben mit gleichem Namen werden nicht doppelt angelegt.</p>
    ${plaene.map((p, i) => `<section class="karte plan">
        <div class="zeile"><h2>${esc(p.modell)}</h2>${p.typ === d.typ ? '<span class="badge blau">passt</span>' : ''}</div>
        <ul class="plan-liste">${p.aufgaben.map(a => `<li>${esc(a.name)} <small class="leise">· alle ${a.stunden ? zahl(a.stunden) + ' h' : ''}${a.stunden && a.tage ? ' oder ' : ''}${a.tage ? a.tage + ' Tage' : ''}</small></li>`).join('')}</ul>
        <div class="knoepfe"><a class="knopf" href="${esc(p.quelle)}" target="_blank" rel="noopener">Quelle</a><button class="primaer" data-plan-nr="${i}">Übernehmen</button></div>
      </section>`).join('')}`);
  document.querySelectorAll('[data-plan-nr]').forEach(b => b.onclick = async () => {
    const p = plaene[+b.dataset.planNr];
    try {
      const anzahl = await (await schicken('wartung/plaene', 'POST', { druckerId: d.id, modell: p.modell })).json();
      blattSchliessen();
      toast(anzahl ? `${anzahl} Aufgaben angelegt.` : 'Alle Aufgaben waren schon vorhanden.');
      if (location.hash === '#wartung') wartungSeite(); else location.hash = 'wartung';
    } catch (e) { fehler(e); }
  });
}

// ------------------------------------------------------------------ Drucker verwalten

const DRUCKER_HILFE = {
  Bambu: 'Am Drucker unter Einstellungen → Netzwerk den LAN-Modus bzw. Entwicklermodus aktivieren und IP-Adresse sowie Zugangscode (8 Zeichen) ablesen. Die Seriennummer steht unter Einstellungen → Gerät.',
  PrusaLink: 'Am Drucker unter Einstellungen → Netzwerk → PrusaLink: IP-Adresse, Benutzer (meist „maker“) und Passwort ablesen. Ältere Geräte mit API-Key: Benutzer leer lassen und den Key als Passwort eintragen.',
  OctoPrint: 'In OctoPrint unter Einstellungen → Application Keys (oder API) einen Schlüssel erzeugen und hier eintragen.',
  Moonraker: 'Adresse von Mainsail bzw. Fluidd. Ein API-Key ist nur nötig, wenn in Moonraker die Anmeldung aktiviert ist.'
};

async function geraeteSeite() {
  titel('Drucker verwalten');
  inhalt(zurueck() + '<p class="leer">Lade …</p>');
  let drucker, live;
  try { [drucker, live] = await Promise.all([holen('drucker'), holen('drucker/live').catch(() => [])]); } catch (e) { return fehler(e); }
  const status = id => (live.find(l => l.druckerId === id) || {}).status;
  inhalt(zurueck() + `<button class="primaer breit" id="gNeu">+ Drucker hinzufügen</button>` +
    (drucker.length ? drucker.map(d => {
      const s = status(d.id), z = s?.zustand ?? 0;
      return `<button class="karte geraet" data-geraet="${d.id}">
        <div class="zeile"><div style="min-width:0"><b>${esc(d.name)}</b>
          <small class="leise">${esc(druckerTypName(d.typ))} · ${esc(d.adresse)}${d.watt ? ` · ${d.watt} W` : ''}</small></div>
          <span class="badge ${d.aktiv ? ZUSTAND_FARBE[z] || 'grau' : 'grau'}">${d.aktiv ? ZUSTAND[z] || '?' : 'Nicht überwacht'}</span></div>
      </button>`;
    }).join('') : '<p class="leer">Noch kein Drucker angelegt.</p>'));
  $('#gNeu').onclick = () => druckerFormular({ typ: 'Bambu', aktiv: true, benutzer: 'maker' });
  document.querySelectorAll('[data-geraet]').forEach(b => b.onclick = () => druckerFormular(drucker.find(d => d.id === +b.dataset.geraet)));
}

async function druckerFormular(d) {
  const neu = !d.id;
  let vorschlaege = [];
  blattOeffnen(`<div class="zeile"><h2>${neu ? 'Drucker hinzufügen' : esc(d.name)}</h2><button class="knopf" data-schliessen>Abbrechen</button></div>
    <label>Name<input id="dName" value="${esc(d.name || '')}" placeholder="z. B. X1 Carbon Werkstatt"></label>
    <label>Art<select id="dTyp">${DRUCKER_TYPEN.map(([w, n]) => `<option value="${w}" ${w === d.typ ? 'selected' : ''}>${esc(n)}</option>`).join('')}</select></label>
    <p class="leise klein-text" id="dHilfe"></p>
    <label>Adresse<input id="dAdresse" value="${esc(d.adresse || '')}" placeholder="z. B. 192.168.1.50" autocapitalize="off" autocorrect="off"></label>
    <label id="dSerienZeile">Seriennummer<input id="dSerien" value="${esc(d.seriennummer || '')}" autocapitalize="characters" autocorrect="off"></label>
    <label id="dBenutzerZeile">Benutzer<input id="dBenutzer" value="${esc(d.benutzer || '')}" autocapitalize="off" autocorrect="off"></label>
    <label><span id="dZugangText">Zugangscode</span><input id="dZugang" type="password" value="${esc(d.zugang || '')}" autocomplete="off"></label>
    <label class="schalter"><input type="checkbox" id="dZeigen"> Zugang anzeigen</label>
    <label>Leistung beim Drucken (Watt, für die Stromkosten)<input id="dWatt" type="number" inputmode="numeric" min="0" value="${d.watt || ''}"></label>
    <label class="schalter"><input type="checkbox" id="dAktiv" ${d.aktiv !== false ? 'checked' : ''}> Drucker überwachen</label>
    <button class="knopf breit" id="dTesten">Verbindung testen</button>
    <p id="dTest" class="leise"></p>
    <p class="leise klein-text">Die Zugangsdaten werden auf dem Server gespeichert.</p>
    <div class="knoepfe">${neu ? '' : '<button class="knopf gefahr" id="dLoeschen">Entfernen</button>'}<button class="primaer" id="dOk">Speichern</button></div>`);
  const typ = () => $('#dTyp').value;
  const typGewechselt = () => {
    $('#dHilfe').textContent = DRUCKER_HILFE[typ()] || '';
    $('#dSerienZeile').hidden = typ() !== 'Bambu';
    $('#dBenutzerZeile').hidden = typ() !== 'PrusaLink';
    $('#dZugangText').textContent = typ() === 'Bambu' ? 'Zugangscode' : typ() === 'PrusaLink' ? 'Passwort' : 'API-Key';
    $('#dAdresse').placeholder = typ() === 'OctoPrint' ? 'z. B. 192.168.1.80 oder octopi.local' : typ() === 'Moonraker' ? 'z. B. 192.168.1.50:7125' : 'IP-Adresse, z. B. 192.168.1.60';
  };
  $('#dTyp').onchange = typGewechselt;
  typGewechselt();
  $('#dZeigen').onchange = e => { $('#dZugang').type = e.target.checked ? 'text' : 'password'; };
  if (!neu) { try { vorschlaege = await holen('strom/vorschlaege'); } catch { /* ohne Vorschlag */ } }
  const v = vorschlaege.find(x => x.druckerId === d.id);
  if (v && !d.watt) $('#dWatt').placeholder = `Vorschlag: ${v.watt}`;
  const ausFeldern = () => ({
    ...d, name: $('#dName').value.trim(), typ: typ(), adresse: $('#dAdresse').value.trim(),
    seriennummer: typ() === 'Bambu' ? $('#dSerien').value.trim().toUpperCase() : '',
    benutzer: typ() === 'PrusaLink' ? $('#dBenutzer').value.trim() : '',
    zugang: $('#dZugang').value.trim(), aktiv: $('#dAktiv').checked, watt: Math.max(0, parseInt($('#dWatt').value, 10) || 0)
  });
  $('#dTesten').onclick = async () => {
    const x = ausFeldern();
    if (!x.adresse) { $('#dTest').className = 'fehler'; $('#dTest').textContent = 'Bitte zuerst die Adresse eintragen.'; return; }
    $('#dTesten').disabled = true;
    $('#dTest').className = 'leise';
    $('#dTest').textContent = 'Der Server verbindet sich mit dem Drucker …';
    try {
      const s = await (await schicken('drucker/testen', 'POST', x)).json();
      const ok = s.zustand !== OFFLINE && s.zustand !== 0;
      $('#dTest').className = ok ? 'gut-text' : 'fehler';
      $('#dTest').textContent = ok ? `✓ Verbunden: ${ZUSTAND[s.zustand] || '?'}${s.datei ? ' · ' + s.datei : ''}${s.faecher?.length ? ` · ${s.faecher.length} Fächer erkannt` : ''}`
        : '✗ ' + (s.meldung || 'Keine Verbindung.');
    } catch (e) { $('#dTest').className = 'fehler'; $('#dTest').textContent = '✗ ' + e.message; }
    $('#dTesten').disabled = false;
  };
  $('#dOk').onclick = async () => {
    const x = ausFeldern();
    if (!x.name) return toast('Bitte einen Namen eingeben.', true);
    if (!x.adresse) return toast('Bitte die Adresse (IP oder Hostname) eintragen.', true);
    if (x.typ === 'Bambu' && (!x.seriennummer || !x.zugang)) return toast('Für Bambu Lab werden Seriennummer und Zugangscode benötigt.', true);
    let gespeichert;
    try { gespeichert = await (await schicken('drucker', 'POST', x)).json(); } catch (e) { return fehler(e); }
    blattSchliessen();
    toast('Drucker gespeichert.');
    geraeteSeite();
    if (neu) herstellerPlan(gespeichert);   // wie in der App: gleich den Wartungsplan anbieten
  };
  if ($('#dLoeschen')) $('#dLoeschen').onclick = async () => {
    if (!confirm(`Drucker „${d.name}“ entfernen? Bereits gebuchte Drucke bleiben erhalten.`)) return;
    try { await schicken(`drucker/${d.id}`, 'DELETE'); } catch (e) { return fehler(e); }
    blattSchliessen();
    toast('Drucker entfernt.');
    geraeteSeite();
  };
}

// ------------------------------------------------------------------ Druck von Hand erfassen oder bearbeiten

/** b: vorhandene Buchung (bearbeiten) oder Vorgaben für einen neuen Druck (z. B. { dateiId }). */
async function druckFormular(b = {}) {
  let spulen, dateien, drucker;
  try { [spulen, dateien, drucker] = await Promise.all([holen('filament'), holen('dateien'), holen('drucker').catch(() => [])]); } catch (e) { return fehler(e); }
  const neu = !b.id;
  const auswahl = spulen.filter(f => f.restgewicht > 0 || f.id === b.filamentId).sort((x, y) => x.code.localeCompare(y.code));
  const zeit = b.druckzeit || 0;
  const datum = b.datum ? String(b.datum).slice(0, 16) : ortszeit(new Date()).slice(0, 16);
  const druckerNamen = [...new Set([...drucker.map(d => d.name), b.drucker].filter(Boolean))];
  blattOeffnen(`<div class="zeile"><h2>${neu ? 'Druck erfassen' : 'Druck bearbeiten'}</h2><button class="knopf" data-schliessen>Abbrechen</button></div>
    <label>Datei (optional – übernimmt Gewicht und Druckzeit)<select id="pDatei"><option value="">– ohne Datei –</option>
      ${[...dateien].sort((x, y) => x.name.localeCompare(y.name)).map(d => `<option value="${d.id}" ${d.id === b.dateiId ? 'selected' : ''}>${esc(d.name)}${d.gramm ? ' · ' + zahl(d.gramm) + ' g' : ''}</option>`).join('')}</select></label>
    <label>Bezeichnung<input id="pBezeichnung" value="${esc(b.bezeichnung || '')}" placeholder="z. B. Halterung"></label>
    <label>Spule<select id="pSpule"><option value="0">– Spule wählen –</option>
      ${auswahl.map(f => `<option value="${f.id}" ${f.id === b.filamentId ? 'selected' : ''}>${esc(f.code)} · ${esc(spulenText(f))} (${zahl(f.restgewicht)} g)</option>`).join('')}</select></label>
    <label>Verbrauch in Gramm<input id="pGramm" type="number" inputmode="numeric" min="0" value="${b.gramm ?? ''}"></label>
    <div class="zwei">
      <label>Druckzeit Stunden<input id="pStunden" type="number" inputmode="numeric" min="0" value="${zeit ? Math.floor(zeit / 3600) : ''}"></label>
      <label>Minuten<input id="pMinuten" type="number" inputmode="numeric" min="0" max="59" value="${zeit ? Math.floor(zeit % 3600 / 60) : ''}"></label>
    </div>
    <label>Drucker (für Statistik und Stromkosten)<select id="pDrucker"><option value="">– ohne Angabe –</option>
      ${druckerNamen.map(n => `<option ${n === b.drucker ? 'selected' : ''}>${esc(n)}</option>`).join('')}</select></label>
    <label>Datum<input id="pDatum" type="datetime-local" value="${esc(datum)}"></label>
    <label class="schalter"><input type="checkbox" id="pFehl" ${b.fehldruck ? 'checked' : ''}> Fehldruck (abgebrochen oder misslungen)</label>
    ${grundAuswahl('pGrund', b.fehldruckGrund, !b.fehldruck)}
    <label>Notiz<input id="pNotiz" value="${esc(b.notiz || '')}"></label>
    <div id="pVorschau" class="vorschau"></div>
    <div class="knoepfe"><button class="primaer" id="pOk">${neu ? 'Buchen' : 'Speichern'}</button></div>`);
  const vorschau = () => {
    const f = spulen.find(x => x.id === +$('#pSpule').value), g = parseInt($('#pGramm').value, 10) || 0;
    if (!f) { $('#pVorschau').textContent = ''; return; }
    const basis = f.restgewicht + (!neu && b.filamentId === f.id ? b.gramm : 0), danach = basis - g;
    const material = f.gewicht > 0 ? f.preis * g / f.gewicht : 0;
    $('#pVorschau').className = 'vorschau' + (danach < 0 ? ' fehler-text' : '');
    $('#pVorschau').textContent = danach < 0 ? `Material ${euro(material)} · Achtung: nur ${zahl(basis)} g auf der Spule!`
      : `Material ${euro(material)} · Rest danach ${zahl(danach)} g (${prozent(f.gewicht > 0 ? danach / f.gewicht : 0)})`;
  };
  $('#pDatei').onchange = () => {
    const d = dateien.find(x => x.id === +$('#pDatei').value);
    if (!d) return;
    if (!$('#pBezeichnung').value.trim()) $('#pBezeichnung').value = d.name.replace(/(\.gcode)?\.[^.]+$/i, '');
    if (d.gramm) $('#pGramm').value = Math.round(d.gramm);
    if (d.druckzeit) { $('#pStunden').value = Math.floor(d.druckzeit / 3600); $('#pMinuten').value = Math.floor(d.druckzeit % 3600 / 60); }
    // Passende Spule vorschlagen, wenn noch keine gewählt ist
    if (!+$('#pSpule').value && d.material) {
      const passend = auswahl.find(f => (f.material || '').toUpperCase().includes(d.material.toUpperCase()));
      if (passend) $('#pSpule').value = passend.id;
    }
    vorschau();
  };
  if (neu && b.dateiId) $('#pDatei').onchange();
  $('#pSpule').onchange = vorschau;
  $('#pGramm').oninput = vorschau;
  $('#pFehl').onchange = e => { $('#pGrund').closest('label').hidden = !e.target.checked; };
  vorschau();
  $('#pOk').onclick = async () => {
    const filamentId = +$('#pSpule').value, gramm = parseInt($('#pGramm').value, 10);
    if (!filamentId) return toast('Bitte die Spule wählen, mit der gedruckt wurde.', true);
    if (!(gramm >= 0)) return toast('Bitte den Verbrauch in Gramm eingeben.', true);
    const sekunden = (parseInt($('#pStunden').value, 10) || 0) * 3600 + (parseInt($('#pMinuten').value, 10) || 0) * 60;
    const fehldruck = $('#pFehl').checked;
    const daten = {
      ...b, art: 'Druck', filamentId, gramm, bezeichnung: $('#pBezeichnung').value.trim(),
      dateiId: +$('#pDatei').value || null, druckzeit: sekunden > 0 ? sekunden : null, drucker: $('#pDrucker').value,
      datum: $('#pDatum').value ? $('#pDatum').value + ':00' : ortszeit(new Date()),
      fehldruck, fehldruckGrund: fehldruck ? $('#pGrund').value : '', notiz: $('#pNotiz').value.trim()
    };
    $('#pOk').disabled = true;
    try {
      const gespeichert = await (await schicken('buchungen/druck', 'POST', daten)).json();
      blattSchliessen();
      toast(`${neu ? 'Gebucht' : 'Gespeichert'}: ${gramm} g · ${euro(gespeichert.kosten)}`);
      navigieren();
    } catch (e) { $('#pOk').disabled = false; fehler(e); }
  };
}

// ------------------------------------------------------------------ Stromkosten

async function stromSeite() {
  titel('Stromkosten');
  inhalt(zurueck() + '<p class="leer">Lade …</p>');
  let preis, drucker, vorschlaege, buchungen;
  try {
    [preis, drucker, vorschlaege, buchungen] = await Promise.all([holen('einstellungen/Strompreis').then(x => x.wert),
      holen('drucker'), holen('strom/vorschlaege').catch(() => []), holen('buchungen').catch(() => [])]);
  } catch (e) { return fehler(e); }
  const mit = buchungen.filter(b => b.art === 'Druck' && b.stromkosten > 0);
  const stunden = mit.reduce((s, b) => s + (b.druckzeit || 0), 0) / 3600;
  const v = id => vorschlaege.find(x => x.druckerId === id) || {};
  inhalt(zurueck() + `<section class="karte">
      <p class="leise" style="margin-top:0">Zu den Materialkosten eines Drucks kommt der Strom: einmal Aufheizen plus Drucken (Druckzeit × Ø Leistung), mal Strompreis.</p>
      <label>Strompreis (€/kWh)<input id="sPreis" type="number" inputmode="decimal" min="0" step="0.001" value="${esc((preis || '').replace(',', '.'))}" placeholder="z. B. 0.32"></label>
    </section>
    <section class="karte"><h2>Leistung der Drucker</h2>
      <p class="leise klein-text">Aufheizen: vor allem das Heizbett, kurz mit hoher Leistung. Drucken: Durchschnitt über den Druck. Am genauesten mit einem Steckdosen-Messgerät. Graue Werte sind Vorschläge.</p>
      ${drucker.length ? drucker.map(d => {
        const heizVorschlag = !d.aufheizWatt && !d.aufheizMinuten, vorschlag = !d.watt;
        return `<div class="strom-drucker" data-drucker="${d.id}">
          <b>${esc(d.name)}</b>
          <div class="drei">
            <label>Aufheizen W<input data-feld="aufheizWatt" type="number" inputmode="numeric" min="0" value="${heizVorschlag ? v(d.id).aufheizWatt ?? '' : d.aufheizWatt}" class="${heizVorschlag ? 'vorschlag-wert' : ''}"></label>
            <label>für min<input data-feld="aufheizMinuten" type="number" inputmode="numeric" min="0" value="${heizVorschlag ? v(d.id).aufheizMinuten ?? '' : d.aufheizMinuten}" class="${heizVorschlag ? 'vorschlag-wert' : ''}"></label>
            <label>Drucken W<input data-feld="watt" type="number" inputmode="numeric" min="0" value="${vorschlag ? v(d.id).watt ?? '' : d.watt}" class="${vorschlag ? 'vorschlag-wert' : ''}"></label>
          </div>
          <small class="leise" data-beispiel></small>
        </div>`;
      }).join('') : '<p class="leise">Noch kein Drucker angelegt.</p>'}
      ${mit.length ? `<p class="leise klein-text">Bisher im Schnitt ${euro(mit.reduce((s, b) => s + b.stromkosten, 0) / mit.length)} Strom pro Druck${stunden > 0 ? ` bzw. ${euro(mit.reduce((s, b) => s + b.stromkosten, 0) / stunden)} pro Druckstunde` : ''} (aus ${mit.length} Drucken)</p>` : ''}
      <label class="schalter"><input type="checkbox" id="sNachrechnen" checked> Bisherige Drucke nachrechnen</label>
      <div class="knoepfe"><button class="primaer" id="sOk">Speichern</button></div>
    </section>`);
  const beispiel = () => {
    const p = parseFloat($('#sPreis').value) || 0;
    document.querySelectorAll('.strom-drucker').forEach(z => {
      const wert = f => parseInt(z.querySelector(`[data-feld="${f}"]`).value, 10) || 0;
      z.querySelector('[data-beispiel]').textContent = p > 0
        ? `Aufheizen ${euro(wert('aufheizWatt') * wert('aufheizMinuten') / 60 / 1000 * p)} + ${euro(wert('watt') / 1000 * p)} pro Druckstunde`
        : 'Ohne Strompreis werden keine Stromkosten berechnet.';
    });
  };
  document.querySelectorAll('.strom-drucker input, #sPreis').forEach(i => i.oninput = () => { i.classList.remove('vorschlag-wert'); beispiel(); });
  beispiel();
  $('#sOk').onclick = async () => {
    const p = Math.max(0, parseFloat($('#sPreis').value) || 0);
    $('#sOk').disabled = true;
    try {
      await schicken('einstellungen/Strompreis', 'PUT', { wert: p ? String(p) : '' });
      for (const z of document.querySelectorAll('.strom-drucker')) {
        const d = drucker.find(x => x.id === +z.dataset.drucker);
        const wert = f => Math.max(0, parseInt(z.querySelector(`[data-feld="${f}"]`).value, 10) || 0);
        const neu = { watt: wert('watt'), aufheizWatt: wert('aufheizWatt'), aufheizMinuten: wert('aufheizMinuten') };
        if (neu.watt !== d.watt || neu.aufheizWatt !== (d.aufheizWatt || 0) || neu.aufheizMinuten !== (d.aufheizMinuten || 0))
          await schicken('drucker', 'POST', { ...d, ...neu });
      }
      if ($('#sNachrechnen').checked) {
        const anzahl = await (await schicken('buchungen/stromkosten', 'POST', {})).json();
        toast(anzahl ? `Gespeichert – Stromkosten von ${anzahl} Druck${anzahl === 1 ? '' : 'en'} neu berechnet.` : 'Gespeichert.');
      } else toast('Gespeichert.');
      stromSeite();
    } catch (e) { $('#sOk').disabled = false; fehler(e); }
  };
}

// ------------------------------------------------------------------ Home Assistant (MQTT)

const HA_FELDER = [['aktiv', 'HA.Aktiv'], ['host', 'HA.Host'], ['port', 'HA.Port'], ['benutzer', 'HA.Benutzer'], ['passwort', 'HA.Passwort'], ['praefix', 'HA.Praefix']];

async function haSeite() {
  titel('Home Assistant');
  inhalt(zurueck() + '<p class="leer">Lade …</p>');
  let werte, status;
  try {
    werte = Object.fromEntries(await Promise.all(HA_FELDER.map(async ([k, s]) => [k, (await holen('einstellungen/' + s)).wert || ''])));
    status = await holen('homeassistant/status').catch(() => null);
  } catch (e) { return fehler(e); }
  inhalt(zurueck() + `<section class="karte">
      <p class="leise" style="margin-top:0">Der Server meldet Drucker (Status, Fortschritt, Temperaturen) und Lager (Spulen, Verbrauch, Kosten, Fehldrucke) über MQTT.
        In Home Assistant erscheinen die Geräte von selbst unter Einstellungen → Geräte (MQTT-Integration, z. B. mit dem Add-on „Mosquitto broker“).</p>
      ${status ? `<div class="dienst">${werte.aktiv === '1' ? `<span class="status ${status.verbunden ? 'ok' : 'weg'}"></span>` : '<span class="status"></span>'}
        <div><b>Zustand</b><small class="leise">${esc(status.meldung)}</small></div></div>` : ''}
      <label class="schalter"><input type="checkbox" id="hAktiv" ${werte.aktiv === '1' ? 'checked' : ''}> An Home Assistant melden</label>
      <label>MQTT-Broker (IP oder Name)<input id="hHost" value="${esc(werte.host)}" placeholder="z. B. 192.168.1.20" autocapitalize="off" autocorrect="off"></label>
      <div class="zwei">
        <label>Port<input id="hPort" type="number" inputmode="numeric" value="${esc(werte.port || '1883')}"></label>
        <label>Präfix<input id="hPraefix" value="${esc(werte.praefix || 'homeassistant')}" autocapitalize="off"></label>
      </div>
      <label>Benutzer<input id="hBenutzer" value="${esc(werte.benutzer)}" autocapitalize="off" autocorrect="off"></label>
      <label>Passwort<input id="hPasswort" type="password" value="${esc(werte.passwort)}" autocomplete="off"></label>
      <p id="hTest" class="leise"></p>
      <div class="knoepfe"><button class="knopf" id="hTesten">Verbindung testen</button><button class="primaer" id="hOk">Speichern</button></div>
    </section>`);
  const ausFeldern = () => ({ aktiv: $('#hAktiv').checked, host: $('#hHost').value.trim(), port: parseInt($('#hPort').value, 10) || 1883,
    benutzer: $('#hBenutzer').value.trim(), passwort: $('#hPasswort').value, praefix: $('#hPraefix').value.trim() || 'homeassistant' });
  $('#hTesten').onclick = async () => {
    $('#hTesten').disabled = true;
    $('#hTest').className = 'leise'; $('#hTest').textContent = 'Verbinde …';
    try {
      const s = await (await schicken('homeassistant/testen', 'POST', ausFeldern())).json();
      $('#hTest').className = s.verbunden ? 'gut-text' : 'fehler';
      $('#hTest').textContent = (s.verbunden ? '✓ ' : '✗ ') + s.meldung;
    } catch (e) { $('#hTest').className = 'fehler'; $('#hTest').textContent = '✗ ' + e.message; }
    $('#hTesten').disabled = false;
  };
  $('#hOk').onclick = async () => {
    const v = ausFeldern();
    if (v.aktiv && !v.host) return toast('Bitte den MQTT-Broker eintragen.', true);
    try {
      for (const [k, s] of HA_FELDER) await schicken('einstellungen/' + s, 'PUT', { wert: k === 'aktiv' ? (v.aktiv ? '1' : '0') : String(v[k]) });
    } catch (e) { return fehler(e); }
    toast('Gespeichert – der Server verbindet sich neu.');
    setTimeout(haSeite, 2500);
  };
}

// ------------------------------------------------------------------ Sicherungen

async function sicherungenSeite() {
  titel('Sicherungen');
  inhalt(zurueck() + '<p class="leer">Lade …</p>');
  let liste;
  try { liste = await holen('sicherungen'); } catch (e) { return fehler(e); }
  liste.sort((a, b) => new Date(b.zeitpunkt) - new Date(a.zeitpunkt));
  inhalt(zurueck() + `<p class="leise klein-text">Der Server sichert die Datenbank jeden Tag selbst. Vor einer Wiederherstellung wird der aktuelle Stand zusätzlich gesichert.</p>
    <button class="primaer breit" id="sJetzt">Jetzt sichern</button>
    ${liste.length ? liste.map(s => `<section class="karte"><div class="zeile">
        <div style="min-width:0"><b>${new Date(s.zeitpunkt).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' })}</b>
          <small class="leise">${esc(s.art)} · ${bytes(s.groesse)}</small></div>
        <button class="knopf klein" data-wiederherstellen="${esc(s.name)}">Wiederherstellen</button></div></section>`).join('')
      : '<p class="leer">Noch keine Sicherung.</p>'}`);
  $('#sJetzt').onclick = async () => {
    $('#sJetzt').disabled = true;
    try { await schicken('sicherungen', 'POST', { art: 'Manuell' }); toast('Gesichert.'); } catch (e) { fehler(e); }
    sicherungenSeite();
  };
  document.querySelectorAll('[data-wiederherstellen]').forEach(b => b.onclick = async () => {
    if (!confirm('Den Stand dieser Sicherung wiederherstellen? Alles, was danach geändert wurde, geht verloren (der jetzige Stand wird vorher gesichert).')) return;
    try { await schicken('sicherungen/wiederherstellen', 'POST', { name: b.dataset.wiederherstellen }); } catch (e) { return fehler(e); }
    toast('Wiederhergestellt.');
    sicherungenSeite();
  });
}

// ------------------------------------------------------------------ Datei-Details (Antippen in "Dateien")

async function dateiDetails(id) {
  let datei, buchungen, ordner;
  try {
    [datei, buchungen, ordner] = await Promise.all([holen('dateien').then(l => l.find(d => d.id === id)), holen('buchungen').catch(() => []), holen('ordner').catch(() => null)]);
  } catch (e) { return fehler(e); }
  if (!datei) return toast('Die Datei gibt es nicht mehr.', true);
  const drucke = buchungen.filter(b => b.dateiId === id).sort((a, b) => b.datum.localeCompare(a.datum));
  const fehl = drucke.filter(b => b.fehldruck).length;
  const druckbar = istDruckbar(datei);
  const zeilen = [['Verbrauch laut Slicer', datei.gramm ? zahl(datei.gramm) + ' g' : ''], ['Druckzeit laut Slicer', dauer(datei.druckzeit)],
    ['Material', datei.material], ['Gedruckt', drucke.length ? `${drucke.length}×${fehl ? ` (${fehl} Fehldruck${fehl > 1 ? 'e' : ''})` : ''} · zuletzt ${new Date(drucke[0].datum).toLocaleDateString('de-DE')}` : 'noch nie']].filter(z => z[1]);
  blattOeffnen(`<div class="zeile"><div style="min-width:0"><h2 class="umbruch">${esc(datei.name)}</h2>
      <small class="leise">${esc(datei.typ)} · ${bytes(datei.groesse)} · hinzugefügt am ${new Date(datei.hinzugefuegt).toLocaleDateString('de-DE')}</small></div>
      <button class="knopf" data-schliessen>Schließen</button></div>
    <div class="datei-gross" id="ddBild"></div>
    <dl>${zeilen.map(z => `<dt>${z[0]}</dt><dd>${esc(z[1])}</dd>`).join('')}</dl>
    ${ordner ? `<label>Ordner<select id="ddOrdner"><option value="">– ohne Ordner –</option>${ordner.map(o => `<option value="${esc(o)}" ${o.toLowerCase() === (datei.ordner || '').toLowerCase() ? 'selected' : ''}>${esc(o.replace(/\//g, ' › '))}</option>`).join('')}</select></label>` : ''}
    <label>Notiz<textarea id="ddNotiz" rows="2" placeholder="Notiz zur Datei">${esc(datei.notiz || '')}</textarea></label>
    <div class="knoepfe">${druckbar ? `<button class="primaer" id="ddSenden">${ICON.drucken} ${drucke.length ? 'Nochmal drucken …' : 'An Drucker senden …'}</button>` : ''}
      <button class="knopf" id="ddBuchen">Als Druck buchen</button></div>
    <div class="knoepfe"><button class="knopf" id="ddLaden">Herunterladen</button><button class="knopf gefahr" id="ddLoeschen">Löschen</button></div>
    ${druckbar ? '' : '<p class="leise klein-text">Zum Drucken die Datei erst slicen (in der Windows-App oder im Slicer) und die druckfertige Datei hochladen.</p>'}`);
  const platz = document.createElement('span');
  platz.dataset.bild = id;
  $('#ddBild').append(platz);
  vorschaubildLaden(platz);
  vorschaubilder.get(String(id))?.then(url => { if (!url && $('#ddBild')) $('#ddBild').hidden = true; });
  if ($('#ddOrdner')) $('#ddOrdner').onchange = async e => {
    try { await schicken(`dateien/${id}/ordner`, 'PUT', { wert: e.target.value }); toast('Ordner geändert.'); } catch (x) { fehler(x); }
  };
  $('#ddNotiz').onchange = async e => {
    try { await schicken(`dateien/${id}/notiz`, 'PUT', { wert: e.target.value }); toast('Notiz gespeichert.'); } catch (x) { fehler(x); }
  };
  if ($('#ddSenden')) $('#ddSenden').onclick = () => sendenFormular(id);
  $('#ddBuchen').onclick = () => druckFormular({ dateiId: id });
  $('#ddLaden').onclick = async () => {
    try {
      const url = URL.createObjectURL(await (await api(`dateien/${id}/inhalt`)).blob());
      const a = Object.assign(document.createElement('a'), { href: url, download: datei.name });
      document.body.append(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 30000);
    } catch (e) { fehler(e); }
  };
  $('#ddLoeschen').onclick = async () => {
    if (!confirm(`„${datei.name}“ löschen? Gebuchte Drucke bleiben im Verlauf.`)) return;
    try { await schicken(`dateien/${id}`, 'DELETE'); } catch (e) { return fehler(e); }
    blattSchliessen();
    toast('Gelöscht.');
    navigieren();
  };
}

// ------------------------------------------------------------------ Dateien: Ordner umbenennen / verschieben / entfernen

async function ordnerUmbenennen(pfad) {
  const neu = prompt('Neuer Name bzw. Pfad (mit „/“ in einen anderen Ordner verschieben, z. B. „Werkstatt/Halter“):', pfad);
  if (neu == null || !neu.trim() || neu.trim() === pfad) return;
  try { await schicken('ordner', 'PUT', { alt: pfad, neu: neu.trim() }); } catch (e) { return fehler(e); }
  dateienOrdner = neu.split('/').map(x => x.trim()).filter(Boolean).join('/');
  toast('Ordner geändert.');
  dateienSeite();
}

async function ordnerEntfernen(pfad) {
  if (!confirm(`Ordner „${pfad.replace(/\//g, ' › ')}“ entfernen? Dateien und Unterordner wandern eine Ebene nach oben – gelöscht wird nichts.`)) return;
  try { await schicken('ordner?pfad=' + encodeURIComponent(pfad), 'DELETE'); } catch (e) { return fehler(e); }
  dateienOrdner = ordnerEltern(pfad);
  toast('Ordner entfernt.');
  dateienSeite();
}

// ------------------------------------------------------------------ Spulen: CSV-Export (wie in der App, für Excel)

function spulenCsv(spulen) {
  const zelle = x => /[;"\n]/.test(String(x ?? '')) ? `"${String(x).replace(/"/g, '""')}"` : String(x ?? '');
  const dezimal = x => (x || 0).toFixed(2).replace('.', ',');
  const kopf = 'ID;Hersteller;Material;Farbe;Durchmesser (mm);Gewicht (g);Restgewicht (g);Spulengewicht (g);Preis (EUR);Preis/kg (EUR);Restwert (EUR);Kaufdatum;Notiz';
  const zeilen = spulen.map(f => [zelle(f.code), zelle(f.hersteller), zelle(f.material), zelle(f.farbe), dezimal(f.durchmesser), f.gewicht, f.restgewicht,
    f.spulengewicht || 0, dezimal(f.preis), dezimal(f.gewicht > 0 ? f.preis / f.gewicht * 1000 : 0), dezimal(f.gewicht > 0 ? f.preis * f.restgewicht / f.gewicht : 0),
    f.kaufdatum ? new Date(f.kaufdatum).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' }) : '', zelle(f.notiz)].join(';'));
  const blob = new Blob(['﻿' + [kopf, ...zeilen].join('\r\n')], { type: 'text/csv;charset=utf-8' });   // BOM: Umlaute in Excel
  const url = URL.createObjectURL(blob);
  const a = Object.assign(document.createElement('a'), { href: url, download: `Filament_${ortszeit(new Date()).slice(0, 10)}.csv` });
  document.body.append(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 30000);
}
