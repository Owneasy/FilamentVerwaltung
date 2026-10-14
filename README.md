# Filament-Verwaltung (VB.NET)

Windows-Desktop-App (VB.NET, Windows Forms, .NET 10) zur Verwaltung von 3D-Drucker-Filament.
Die Daten liegen in einer lokalen **SQLite-Datenbank** – eine einzige Datei, kein Datenbankserver nötig.

## Funktionen

- **Übersicht (Startseite):** Lagerbestand, laufende Drucke (live mit Fortschritt, Restzeit, Schicht, Temperaturen –
  aktive Drucke farbig hervorgehoben), Verbrauch diesen Monat und pro Woche, letzte Drucke und was nachgekauft werden sollte
- Filament anlegen, bearbeiten, duplizieren (neue Spule vom gleichen Typ), löschen
- Felder: **eindeutige ID** (automatisch `FIL-0001`, `FIL-0002`, … oder frei wählbar), **Hersteller**, Material,
  Farbe, Durchmesser, **Gewicht**, Restgewicht, Leerspulengewicht, **Preis**, Kaufdatum, Notiz
- Berechnet: Rest in %, Preis pro kg, Restwert
- Verbrauch buchen: Gramm abziehen, Spule nachwiegen (Leerspule wird abgezogen) oder Rest direkt eingeben
- Spalten ein-/ausblenden über „Spalten“ oder Rechtsklick auf einen Spaltenkopf (wird in der Datenbank gespeichert)
- Suche, Material- und Lagerortfilter, Sortierung per Klick auf die Spaltenköpfe
- Untermenü in der Seitenleiste: **Alle**, **Vorhanden** (mit Restbestand) und **Leer** – jeweils mit Anzahl
- Oberfläche im Web-Dashboard-Stil: Seitenleiste, Kennzahl-Kacheln, Füllstandsbalken (rot < 15 %, leere Spulen grau)
- Kennzahlen: Anzahl, Restbestand in kg, Restwert, fast leere Spulen
- **Drucke / Verlauf:** Jede Änderung am Restgewicht wird protokolliert (Druck, Wiegen, Korrektur) – mit Kosten,
  Druckzeit und verknüpfter Datei. Drucke lassen sich erfassen, bearbeiten und mit Rückbuchung löschen.
  Kennzahlen für den laufenden Monat; "Verlauf anzeigen" (Rechtsklick auf eine Spule) filtert nach Spule.
- **Slicen mit Bambu Studio:** In „Dateien“ ein STL/3MF/OBJ/STEP mit „Slicen …“ in der installierten Bambu Studio
  slicen – Drucker-, Prozess- und Filamentprofil (auch eigene Profile) und Druckplatte wählen, optional die Spule
  (Filamentprofil und Farbe werden vorgeschlagen). Das Ergebnis (3MF mit G-Code, Gewicht, Druckzeit) landet in
  „Dateien“. Die Profil-Vererbung wird von der App aufgelöst, weil die Bambu-Studio-Kommandozeile das nicht
  vollständig tut (sonst Gewicht 0 g). „Im Slicer öffnen“ öffnet Dateien in Bambu Studio, OrcaSlicer, PrusaSlicer oder Cura.
- **Drucker-Anbindung:** Bambu Lab (LAN, MQTT), Prusa (PrusaLink), Klipper (Moonraker) und OctoPrint. Live-Status je
  Drucker (Datei, Fortschritt, Restzeit, Temperaturen von Düse, Bett und ggf. Kammer), Spulen-Zuordnung je Drucker bzw. je AMS-Fach. Ist ein Druck fertig oder
  abgebrochen, erscheint ein Hinweis zum Buchen – Spule, Gewicht, Druckzeit und Datei sind vorausgefüllt
  (Gewicht aus tatsächlicher Extrusion, Slicer-Angaben des Druckers oder der passenden Datei in „Dateien“).
  Pro Drucker gibt es ein Protokoll für die Fehlersuche.
- **An Drucker senden:** G-Code/BGCODE bzw. geslicte 3MF direkt aus „Dateien“ hochladen und den Druck starten
  (OctoPrint, Moonraker, PrusaLink per HTTP; Bambu Lab per FTPS + MQTT mit Wahl der Platte, AMS-Zuordnung,
  Bettnivellierung, Zeitraffer). Bei Bambu muss der LAN-Modus (meist mit Entwicklermodus) aktiv sein. Nach dem Druck
  wird die gesendete Datei für die Buchung verwendet.
- **Etiketten mit QR-Code:** A4-Etikettenbögen (z. B. Avery 3474 / 3652 / L7163, auch angebrochene Bögen) oder
  Etikettendrucker (Brother 62 × 29 mm, Dymo 54 × 25 mm), mit Vorschau. Der QR-Code enthält die Spulen-ID.
- **Spule scannen:** Strg+K (oder „Spule suchen“) – QR-Code scannen oder ID eintippen, Enter öffnet die Spule zum
  Wiegen. Funktioniert auch im Suchfeld der Filament-Seite. USB-Scanner brauchen keinen Treiber.
- **Mehr Infos pro Spule:** Lagerort (mit Filter), Düsen-/Betttemperatur, Trocknungshinweis, Shop-Link.
- **Leerspulen-Vorlagen:** Beim Hersteller wird das Leerspulengewicht vorgeschlagen (Richtwerte, danach der zuletzt
  selbst eingetragene Wert).
- **Nachkauf:** Sorten (gleicher Hersteller/Material/Farbe/Ø), die unter den Mindestbestand fallen oder für weniger
  als 30 Tage reichen – mit letztem Preis, Shop-Link, „Gekauft – neue Spule anlegen“ und Einkaufsliste zum Kopieren.
- **Dunkles Design:** Hell, Dunkel oder wie in Windows eingestellt („Design“ in der Seitenleiste).
- **Statistik:** Verbrauch pro Monat (letzte 12 Monate), Verbrauch nach Material, Ø Verbrauch, Reichweite des
  Lagers, Kosten und Drucke im laufenden Jahr sowie eine Prognose je Spule, wann sie beim bisherigen Verbrauch
  (letzte 90 Tage) leer ist. Jedes Diagramm lässt sich auf eine Tabelle umschalten.
- **Dateien:** STL, 3MF, OBJ, STEP, G-Code … per Drag & Drop oder Knopf hinzufügen. Die Dateien werden in den
  Ordner `Dateien` neben der Datenbank kopiert. 3D-Vorschau für STL/3MF/OBJ (drehen, zoomen).
  Aus G-Code (PrusaSlicer, OrcaSlicer, Bambu Studio, Cura) und geslicten 3MF werden Gewicht, Druckzeit und
  Material ausgelesen; "Als Druck buchen" übernimmt sie und zieht den Verbrauch von der Spule ab.
- CSV-Export für Excel

Tastenkürzel: `Strg+N` neu · `Enter`/Doppelklick bearbeiten · `Strg+D` duplizieren · `Strg+G` Verbrauch/Wiegen ·
`Entf` löschen · `Strg+F` Suche · `F5` neu laden

## Starten

**Visual Studio 2026** (Workload „.NET-Desktopentwicklung“):
`FilamentVerwaltung.sln` öffnen → `F5`. Das NuGet-Paket `Microsoft.Data.Sqlite` wird automatisch geladen.

**Oder per Kommandozeile** (.NET 10 SDK):

```
dotnet run --project FilamentVerwaltung
```

**Eigenständige EXE erstellen:**

```
dotnet publish FilamentVerwaltung -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Die EXE liegt dann unter `FilamentVerwaltung\bin\Release\net10.0-windows\win-x64\publish\`.

## Updates

Die App sucht beim Start auf GitHub (`Owneasy/FilamentVerwaltung`, einstellbar in `Updater.vb`) nach einem
neueren Release. Gibt es eins, erscheint oben ein Hinweis; im Update-Dialog wird die neue EXE geladen, geprüft
(Größe + SHA-256), gegen die laufende ausgetauscht und neu gestartet. Datenbank und Dateien bleiben unverändert.
Manuell: „Nach Updates suchen“ in der Seitenleiste.

**Neue Version veröffentlichen:**

```
.\release.ps1 -Version 1.1.0
```

Das Skript setzt die Version, baut eine eigenständige EXE (enthält .NET, ca. 50 MB) nach `artefakte\1.1.0\`
und zeigt die nächsten Schritte: auf GitHub ein Release mit Tag `v1.1.0` anlegen, die Datei
`FilamentVerwaltung.exe` anhängen und im Beschreibungstext die Neuerungen nennen (erscheinen im Update-Dialog).

Hinweise: Das Repository muss öffentlich sein (private Repos bräuchten ein Token). Die EXE muss in einem
beschreibbaren Ordner liegen (z. B. Dokumente oder Nextcloud, nicht „Programme“). Aus Visual Studio gestartet
wird nur geprüft, nicht installiert.

## Datenbank

Standardpfad: `Dokumente\FilamentVerwaltung\filament.db` (Button „DB-Ordner“ öffnet den Ordner).
Ein anderer Pfad kann als Startparameter übergeben werden:

```
FilamentVerwaltung.exe "D:\Daten\filament.db"
```

**Sicherungen:** Beim Start wird einmal pro Tag automatisch eine Sicherung im Ordner `Backups` neben der Datenbank
angelegt (die letzten 10 automatischen bleiben erhalten, manuelle werden nie automatisch gelöscht). Über
„Sicherungen“ in der Seitenleiste: jetzt sichern, wiederherstellen (der aktuelle Stand wird vorher gesichert),
Ordner öffnen. Der Ordner `Dateien` (STL, G-Code …) wird nicht mitgesichert – für ein vollständiges Backup ihn
zusätzlich kopieren. Die Datei während der Nutzung
direkt im Nextcloud-Ordner zu halten, funktioniert, kann aber bei gleichzeitigem Zugriff von zwei PCs Konflikte erzeugen.

## Server (Docker / Unraid) – im Aufbau

Statt der lokalen Datenbank kann ein Server die Daten halten (Datenbank, Dateien, Sicherungen), damit mehrere PCs
und auch unterwegs dieselben Daten nutzen. Stand: Daten, Dateien, Einstellungen und Sicherungen laufen über den Server;
die Drucker werden vorerst noch von der App aus überwacht (Umzug auf den Server folgt).

**Bauen und starten (z. B. im Unraid-Terminal):**

```
git clone https://github.com/Owneasy/FilamentVerwaltung.git /mnt/user/appdata/filament-src
cd /mnt/user/appdata/filament-src
docker build -t filament-server .
docker run -d --name filament-server --restart unless-stopped -p 8080:8080 \
  -v /mnt/user/appdata/filament:/data filament-server
```

- **Daten:** alles liegt im Volume `/data` (`filament.db`, `Dateien/`, `Backups/`). Einmal pro Tag wird automatisch gesichert.
- **Zugangs-Token:** Jede Anfrage braucht `Authorization: Bearer <Token>`. Ohne `FILAMENT_TOKEN` erzeugt der Server
  beim ersten Start eins und speichert es in `/data/token.txt` (steht auch im Container-Log).
- **Umgebungsvariablen:** `FILAMENT_TOKEN` (eigenes Token), `FILAMENT_DATEN` (Standard `/data`), `ASPNETCORE_HTTP_PORTS` (Standard `8080`).
- **Von unterwegs (Cloudflare-Tunnel):** Im Tunnel einen Public Hostname auf `http://<Unraid-IP>:8080` anlegen und
  zusätzlich per Cloudflare Access (Service-Token) schützen. Große Dateien werden in 16-MB-Stücken hochgeladen
  (Cloudflare lässt höchstens 100 MB pro Anfrage zu).
- **Test:** `http://<Unraid-IP>:8080/api/info` liefert Name und Version (ohne Token).

**App verbinden:** In der Seitenleiste „Verbindung“ → „Server“ wählen, Adresse im Heimnetz (z. B. `http://192.168.1.10:8080`),
Adresse unterwegs (z. B. `https://filament.meinedomain.de`), Token und ggf. Cloudflare-Service-Token eintragen, „Verbindung
testen“, speichern – die App startet neu. Die App versucht zuerst die Heimnetz-Adresse, sonst die für unterwegs, und wechselt
bei einem Verbindungsabbruch automatisch. Die Verbindung wird nur auf dem PC gespeichert (`%AppData%\FilamentVerwaltung\verbindung.json`,
Token und Secret mit Windows verschlüsselt); alle anderen Einstellungen (Design, Spalten, Slicer-Profile …) liegen auf dem
Server und sind damit auf jedem PC gleich. Dateien vom Server werden für Vorschau, Slicen und Senden in
`%LocalAppData%\FilamentVerwaltung\Dateien` zwischengespeichert. Ist der Server beim Start nicht erreichbar, erscheint der
Verbindungsdialog.## Projektaufbau

Drei Projekte: `FilamentVerwaltung` (Windows-App), `FilamentVerwaltung.Kern` (Modelle, Datenbank, Drucker-Anbindung, Dateianalyse –
ohne Windows-Abhängigkeiten, von App und Server genutzt; Schnittstellen `IDatenquelle` / `IDruckerDienst` in `Schnittstellen.vb`)
und `FilamentVerwaltung.Server` (ASP.NET Core, `DatenController.vb` = REST-API).

| Datei | Inhalt |
|---|---|
| `Program.vb` | Start, Datenbankpfad |
| `Filament.vb` | Datenmodell inkl. berechneter Werte |
| `Datenbank.vb` | SQLite-Zugriff (Tabelle anlegen, laden, speichern, löschen, nächste ID) |
| `MainForm.vb` | Hauptfenster: Seitenleiste mit Navigation, zeigt die gewählte Seite |
| `FilamentSeite.vb` | Seite „Filament“: Kennzahlen, Tabelle, Suche, Filter, Export |
| `DruckeSeite.vb` | Seite „Drucke“: Verlauf aller Buchungen, Kennzahlen |
| `DruckDialog.vb` | Druck erfassen / bearbeiten |
| `DateienSeite.vb` | Seite „Dateien“: Liste, 3D-Vorschau, Details, Drag & Drop |
| `UebersichtSeite.vb` | Startseite „Übersicht“: Kennzahlen, Drucker live, Verbrauch pro Woche, letzte Drucke, Nachkauf |
| `StatistikSeite.vb` | Seite „Statistik“: Kennzahlen, Diagramme, Reichweite je Spule |
| `Diagramme.vb` | Säulen- und Balkendiagramm (selbst gezeichnet) mit Tooltip und Tabellenansicht |
| `Sicherung.vb`, `SicherungenDialog.vb` | Automatische und manuelle Sicherungen, Wiederherstellen |
| `NachkaufSeite.vb` | Seite „Nachkauf“: Bestand und Reichweite je Sorte, Einkaufsliste |
| `Etiketten.vb`, `EtikettenDialog.vb` | Etikettenformate, QR-Code (QRCoder), Druck und Vorschau |
| `SchnellsucheDialog.vb` | Spule per Scan / ID finden (Strg+K) |
| `Drucker.vb` | Drucker, Status, Druckende, Umrechnung Länge/Volumen → Gramm |
| `DruckerVerbindungen.vb` | Verbindungen: Moonraker, OctoPrint, PrusaLink (HTTP), Bambu Lab (MQTT/TLS, MQTTnet; Upload per FTPS mit FluentFTP) |
| `DruckerUeberwachung.vb` | Regelmäßige Abfrage, Erkennung von Druckende, Buchungsvorschlag |
| `DruckerSeite.vb`, `DruckerDialog.vb` | Seite „Drucker“ mit Live-Karten, Drucker hinzufügen / testen |
| `Slicer.vb` | Slicer finden, Bambu-Studio-Profile lesen/auflösen, Slicen über die Kommandozeile |
| `SlicerDialog.vb` | Dialog „Slicen mit Bambu Studio“ |
| `SendenDialog.vb` | Dialog „An Drucker senden“ (Drucker, Platte, AMS-Zuordnung, Optionen) |
| `Updater.vb`, `UpdateDialog.vb` | Updates über GitHub Releases |
| `ModellAnsicht.vb` | STL/3MF/OBJ laden (`Netz`) und 3D-Vorschau mit eigenem Software-Renderer |
| `DateiAnalyse.vb` | Druckdaten aus G-Code und geslicten 3MF auslesen |
| `Datei.vb`, `Buchung.vb` | Datenmodelle für Dateien und Buchungen |
| `Theme.vb` | Farben, Schriften, Symbole – Aussehen zentral anpassen |
| `Steuerelemente.vb` | Eigene Steuerelemente: WebButton, Karte, KennzahlKarte, SuchFeld, WebTabelle, TabellenKarte, SeitenKopf |
| `FilamentDialog.vb` | Eingabemaske |
| `VerbrauchDialog.vb` | Verbrauch / Wiegen |

Die Oberflächen werden im Code aufgebaut (keine Designer-Dateien) – in Visual Studio per Rechtsklick → „Code anzeigen“ öffnen.
