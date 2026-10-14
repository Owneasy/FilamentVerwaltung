# Filament-Verwaltung (VB.NET)

Windows-Desktop-App (VB.NET, Windows Forms, .NET 10) zur Verwaltung von 3D-Drucker-Filament.
Die Daten liegen in einer lokalen **SQLite-Datenbank** – eine einzige Datei, kein Datenbankserver nötig.

## Funktionen

- Filament anlegen, bearbeiten, duplizieren (neue Spule vom gleichen Typ), löschen
- Felder: **eindeutige ID** (automatisch `FIL-0001`, `FIL-0002`, … oder frei wählbar), **Hersteller**, Material,
  Farbe, Durchmesser, **Gewicht**, Restgewicht, Leerspulengewicht, **Preis**, Kaufdatum, Notiz
- Berechnet: Rest in %, Preis pro kg, Restwert
- Verbrauch buchen: Gramm abziehen, Spule nachwiegen (Leerspule wird abgezogen) oder Rest direkt eingeben
- Spalten ein-/ausblenden über „Spalten“ oder Rechtsklick auf einen Spaltenkopf (wird in der Datenbank gespeichert)
- Suche, Materialfilter, leere Spulen ausblenden, Sortierung per Klick auf die Spaltenköpfe
- Oberfläche im Web-Dashboard-Stil: Seitenleiste, Kennzahl-Kacheln, Füllstandsbalken (rot < 15 %, leere Spulen grau)
- Kennzahlen: Anzahl, Restbestand in kg, Restwert, fast leere Spulen
- **Drucke / Verlauf:** Jede Änderung am Restgewicht wird protokolliert (Druck, Wiegen, Korrektur) – mit Kosten,
  Druckzeit und verknüpfter Datei. Drucke lassen sich erfassen, bearbeiten und mit Rückbuchung löschen.
  Kennzahlen für den laufenden Monat; "Verlauf anzeigen" (Rechtsklick auf eine Spule) filtert nach Spule.
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

## Projektaufbau

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
| `ModellAnsicht.vb` | STL/3MF/OBJ laden (`Netz`) und 3D-Vorschau mit eigenem Software-Renderer |
| `DateiAnalyse.vb` | Druckdaten aus G-Code und geslicten 3MF auslesen |
| `Datei.vb`, `Buchung.vb` | Datenmodelle für Dateien und Buchungen |
| `Theme.vb` | Farben, Schriften, Symbole – Aussehen zentral anpassen |
| `Steuerelemente.vb` | Eigene Steuerelemente: WebButton, Karte, KennzahlKarte, SuchFeld, WebTabelle, TabellenKarte, SeitenKopf |
| `FilamentDialog.vb` | Eingabemaske |
| `VerbrauchDialog.vb` | Verbrauch / Wiegen |

Die Oberflächen werden im Code aufgebaut (keine Designer-Dateien) – in Visual Studio per Rechtsklick → „Code anzeigen“ öffnen.
