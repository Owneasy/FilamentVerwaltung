Imports System.Globalization
Imports System.IO
Imports Microsoft.Data.Sqlite

''' <summary>
''' Zugriff auf die SQLite-Datenbank (eine einzelne Datei, kein Server nötig).
''' </summary>
Public Class Datenbank
    Implements IDatenquelle

    Private Const CodePraefix As String = "FIL-"
    Private ReadOnly _verbindung As String

    Public ReadOnly Property Pfad As String

    Public ReadOnly Property Ort As String Implements IDatenquelle.Ort
        Get
            Return Pfad
        End Get
    End Property

    Public Sub New(dateiPfad As String)
        Pfad = dateiPfad
        Dim ordner = Path.GetDirectoryName(dateiPfad)
        If Not String.IsNullOrEmpty(ordner) Then Directory.CreateDirectory(ordner)

        Dim builder As New SqliteConnectionStringBuilder With {
            .DataSource = dateiPfad,
            .Mode = SqliteOpenMode.ReadWriteCreate
        }
        _verbindung = builder.ToString()

        TabelleAnlegen()
    End Sub

    Private Function Oeffnen() As SqliteConnection
        Dim c As New SqliteConnection(_verbindung)
        c.Open()
        Return c
    End Function

    Private Sub TabelleAnlegen()
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText =
"CREATE TABLE IF NOT EXISTS Filament (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    Code          TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    Hersteller    TEXT    NOT NULL DEFAULT '',
    Material      TEXT    NOT NULL DEFAULT '',
    Farbe         TEXT    NOT NULL DEFAULT '',
    Durchmesser   REAL    NOT NULL DEFAULT 1.75,
    Gewicht       INTEGER NOT NULL DEFAULT 1000,
    Restgewicht   INTEGER NOT NULL DEFAULT 1000,
    Spulengewicht INTEGER NOT NULL DEFAULT 0,
    Preis         REAL    NOT NULL DEFAULT 0,
    Kaufdatum     TEXT,
    Notiz         TEXT    NOT NULL DEFAULT ''
);
CREATE TABLE IF NOT EXISTS Einstellungen (
    Schluessel TEXT PRIMARY KEY,
    Wert       TEXT NOT NULL DEFAULT ''
);
CREATE TABLE IF NOT EXISTS Datei (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Name         TEXT    NOT NULL,
    Pfad         TEXT    NOT NULL,
    Typ          TEXT    NOT NULL DEFAULT '',
    Groesse      INTEGER NOT NULL DEFAULT 0,
    Hinzugefuegt TEXT    NOT NULL,
    Gramm        REAL,
    Druckzeit    INTEGER,
    Material     TEXT    NOT NULL DEFAULT '',
    Notiz        TEXT    NOT NULL DEFAULT ''
);
CREATE TABLE IF NOT EXISTS Buchung (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    FilamentId  INTEGER NOT NULL,
    Datum       TEXT    NOT NULL,
    Art         TEXT    NOT NULL,
    Gramm       INTEGER NOT NULL,
    Bezeichnung TEXT    NOT NULL DEFAULT '',
    DateiId     INTEGER,
    Druckzeit   INTEGER,
    Kosten      REAL    NOT NULL DEFAULT 0,
    Notiz       TEXT    NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS IX_Buchung_Filament ON Buchung (FilamentId);
CREATE TABLE IF NOT EXISTS SpulenVorlage (
    Hersteller TEXT    PRIMARY KEY COLLATE NOCASE,
    Gewicht    INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS Drucker (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Name         TEXT    NOT NULL,
    Typ          TEXT    NOT NULL,
    Adresse      TEXT    NOT NULL DEFAULT '',
    Zugang       TEXT    NOT NULL DEFAULT '',
    Benutzer     TEXT    NOT NULL DEFAULT '',
    Seriennummer TEXT    NOT NULL DEFAULT '',
    Aktiv        INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS PushAbo (
    Endpunkt  TEXT PRIMARY KEY,
    P256dh    TEXT NOT NULL,
    Auth      TEXT NOT NULL,
    Geraet    TEXT NOT NULL DEFAULT '',
    Erstellt  TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS DruckEnde (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    Zeitpunkt TEXT    NOT NULL,
    Daten     TEXT    NOT NULL
);
CREATE TABLE IF NOT EXISTS DruckerSpule (
    DruckerId  INTEGER NOT NULL,
    Slot       INTEGER NOT NULL,
    FilamentId INTEGER NOT NULL,
    PRIMARY KEY (DruckerId, Slot)
);"
            cmd.ExecuteNonQuery()

            ' Spalten, die nach Version 0.0.2 dazugekommen sind
            SpalteErgaenzen(c, "Filament", "Lagerort", "TEXT NOT NULL DEFAULT ''")
            SpalteErgaenzen(c, "Filament", "DuesenTemp", "INTEGER NOT NULL DEFAULT 0")
            SpalteErgaenzen(c, "Filament", "BettTemp", "INTEGER NOT NULL DEFAULT 0")
            SpalteErgaenzen(c, "Filament", "Trocknung", "TEXT NOT NULL DEFAULT ''")
            SpalteErgaenzen(c, "Filament", "ShopLink", "TEXT NOT NULL DEFAULT ''")
            SpalteErgaenzen(c, "Filament", "RfidKennung", "TEXT NOT NULL DEFAULT ''")
            SpalteErgaenzen(c, "Buchung", "Foto", "TEXT NOT NULL DEFAULT ''")
            SpalteErgaenzen(c, "Filament", "Barcode", "TEXT NOT NULL DEFAULT ''")
            Dim druckerNeu = SpalteErgaenzen(c, "Buchung", "Drucker", "TEXT NOT NULL DEFAULT ''")
            Dim fehldruckNeu = SpalteErgaenzen(c, "Buchung", "Fehldruck", "INTEGER NOT NULL DEFAULT 0")
            If druckerNeu OrElse fehldruckNeu Then BuchungenNachruesten(c)
        End Using
    End Sub

    ''' <summary>Fügt eine Spalte hinzu, falls sie noch fehlt (Datenbanken älterer Versionen).</summary>
    Private Shared Function SpalteErgaenzen(c As SqliteConnection, tabelle As String, spalte As String, definition As String) As Boolean
        Using cmd = c.CreateCommand()
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tabelle}') WHERE name = $name"
            cmd.Parameters.AddWithValue("$name", spalte)
            If Convert.ToInt64(cmd.ExecuteScalar()) > 0 Then Return False
            cmd.CommandText = $"ALTER TABLE {tabelle} ADD COLUMN {spalte} {definition}"
            cmd.Parameters.Clear()
            cmd.ExecuteNonQuery()
            Return True
        End Using
    End Function

    ''' <summary>
    ''' Einmalig beim Hinzufügen der Spalten: Drucker und Fehldruck für bereits erkannte Drucke aus der Notiz übernehmen
    ''' ("Erkannt am Drucker X, abgebrochen bei 50 %. …").
    ''' </summary>
    Private Shared Sub BuchungenNachruesten(c As SqliteConnection)
        Dim muster As New Text.RegularExpressions.Regex("^Erkannt am Drucker (.+?)(, abgebrochen bei [^.]*)?\. ")
        Dim treffer As New List(Of (Id As Long, Drucker As String, Abgebrochen As Boolean))
        Using cmd = c.CreateCommand()
            cmd.CommandText = "SELECT Id, Notiz FROM Buchung WHERE Notiz LIKE 'Erkannt am Drucker %'"
            Using r = cmd.ExecuteReader()
                While r.Read()
                    Dim m = muster.Match(If(r.IsDBNull(1), "", r.GetString(1)))
                    If m.Success Then treffer.Add((r.GetInt64(0), m.Groups(1).Value, m.Groups(2).Success))
                End While
            End Using
        End Using
        For Each x In treffer
            Befehl(c, Nothing, "UPDATE Buchung SET Drucker = $d, Fehldruck = $f WHERE Id = $id",
                   "$d", x.Drucker, "$f", If(x.Abgebrochen, 1, 0), "$id", x.Id).ExecuteNonQuery()
        Next
    End Sub

    ' ------------------------------------------------------------------ Leerspulen-Vorlagen

    ''' <summary>Richtwerte für leere Spulen (werden durch eigene Werte überschrieben, sobald man eine Spule speichert).</summary>
    Public Shared ReadOnly StandardLeerspulen As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase) From {
        {"3DJake", 230}, {"Anycubic", 180}, {"Bambu Lab", 250}, {"Creality", 160}, {"Das Filament", 230},
        {"Elegoo", 160}, {"eSun", 220}, {"Extrudr", 230}, {"Fiberlogy", 250}, {"Geeetech", 170},
        {"Jayo", 160}, {"Overture", 170}, {"Polymaker", 140}, {"Prusament", 200}, {"Sunlu", 160}}

    ''' <summary>Leerspulengewicht für einen Hersteller: eigener gespeicherter Wert, sonst Richtwert, sonst Nothing.</summary>
    Public Function LeerspulenVorlage(hersteller As String, ByRef eigenerWert As Boolean) As Integer? Implements IDatenquelle.LeerspulenVorlage
        eigenerWert = False
        If String.IsNullOrWhiteSpace(hersteller) Then Return Nothing
        Using c = Oeffnen()
            Dim wert = Befehl(c, Nothing, "SELECT Gewicht FROM SpulenVorlage WHERE Hersteller = $h", "$h", hersteller.Trim()).ExecuteScalar()
            If wert IsNot Nothing Then
                eigenerWert = True
                Return Convert.ToInt32(wert)
            End If
        End Using
        Dim standard As Integer
        If StandardLeerspulen.TryGetValue(hersteller.Trim(), standard) Then Return standard
        Return Nothing
    End Function

    ''' <summary>Liest eine gespeicherte Einstellung (Nothing, wenn sie noch nicht existiert).</summary>
    Public Function EinstellungLesen(schluessel As String) As String Implements IDatenquelle.EinstellungLesen
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "SELECT Wert FROM Einstellungen WHERE Schluessel = $schluessel"
            cmd.Parameters.AddWithValue("$schluessel", schluessel)
            Return TryCast(cmd.ExecuteScalar(), String)
        End Using
    End Function

    Public Sub EinstellungSpeichern(schluessel As String, wert As String) Implements IDatenquelle.EinstellungSpeichern
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "INSERT INTO Einstellungen (Schluessel, Wert) VALUES ($schluessel, $wert) " &
                              "ON CONFLICT(Schluessel) DO UPDATE SET Wert = excluded.Wert"
            cmd.Parameters.AddWithValue("$schluessel", schluessel)
            cmd.Parameters.AddWithValue("$wert", If(wert, ""))
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    Public Function AlleLaden() As List(Of Filament) Implements IDatenquelle.AlleLaden
        Dim liste As New List(Of Filament)
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "SELECT Id, Code, Hersteller, Material, Farbe, Durchmesser, Gewicht, Restgewicht, " &
                              "Spulengewicht, Preis, Kaufdatum, Notiz, Lagerort, DuesenTemp, BettTemp, Trocknung, ShopLink, RfidKennung, Barcode " &
                              "FROM Filament ORDER BY Code"
            Using r = cmd.ExecuteReader()
                While r.Read()
                    liste.Add(New Filament With {
                        .Id = r.GetInt64(0),
                        .Code = Text(r, 1),
                        .Hersteller = Text(r, 2),
                        .Material = Text(r, 3),
                        .Farbe = Text(r, 4),
                        .Durchmesser = Math.Round(CDec(r.GetDouble(5)), 2),
                        .Gewicht = r.GetInt32(6),
                        .Restgewicht = r.GetInt32(7),
                        .Spulengewicht = r.GetInt32(8),
                        .Preis = Math.Round(CDec(r.GetDouble(9)), 2),
                        .Kaufdatum = DatumLesen(Text(r, 10)),
                        .Notiz = Text(r, 11),
                        .Lagerort = Text(r, 12),
                        .DuesenTemp = r.GetInt32(13),
                        .BettTemp = r.GetInt32(14),
                        .Trocknung = Text(r, 15),
                        .ShopLink = Text(r, 16),
                        .RfidKennung = Text(r, 17),
                        .Barcode = Text(r, 18)
                    })
                End While
            End Using
        End Using
        Return liste
    End Function

    ''' <summary>
    ''' Legt das Filament neu an (Id = 0) oder aktualisiert es.
    ''' Ändert sich dabei das Restgewicht, wird eine Korrektur-Buchung angelegt.
    ''' </summary>
    Public Sub Speichern(f As Filament) Implements IDatenquelle.Speichern
        Using c = Oeffnen(), t = c.BeginTransaction(), cmd = c.CreateCommand()
            cmd.Transaction = t
            Dim altRest As Integer? = Nothing
            If f.Id <> 0 Then
                Dim wert = Befehl(c, t, "SELECT Restgewicht FROM Filament WHERE Id = $id", "$id", f.Id).ExecuteScalar()
                If wert IsNot Nothing Then altRest = Convert.ToInt32(wert)
            End If

            If f.Id = 0 Then
                cmd.CommandText =
                    "INSERT INTO Filament (Code, Hersteller, Material, Farbe, Durchmesser, Gewicht, Restgewicht, " &
                    "Spulengewicht, Preis, Kaufdatum, Notiz, Lagerort, DuesenTemp, BettTemp, Trocknung, ShopLink, RfidKennung, Barcode) " &
                    "VALUES ($code, $hersteller, $material, $farbe, $durchmesser, $gewicht, $rest, $spule, $preis, $kauf, " &
                    "$notiz, $lagerort, $duese, $bett, $trocknung, $shop, $rfid, $barcode); SELECT last_insert_rowid();"
            Else
                cmd.CommandText =
                    "UPDATE Filament SET Code = $code, Hersteller = $hersteller, Material = $material, Farbe = $farbe, " &
                    "Durchmesser = $durchmesser, Gewicht = $gewicht, Restgewicht = $rest, Spulengewicht = $spule, " &
                    "Preis = $preis, Kaufdatum = $kauf, Notiz = $notiz, Lagerort = $lagerort, DuesenTemp = $duese, " &
                    "BettTemp = $bett, Trocknung = $trocknung, ShopLink = $shop, RfidKennung = $rfid, Barcode = $barcode WHERE Id = $id"
                cmd.Parameters.AddWithValue("$id", f.Id)
            End If

            cmd.Parameters.AddWithValue("$code", f.Code.Trim())
            cmd.Parameters.AddWithValue("$hersteller", If(f.Hersteller, "").Trim())
            cmd.Parameters.AddWithValue("$material", If(f.Material, "").Trim())
            cmd.Parameters.AddWithValue("$farbe", If(f.Farbe, "").Trim())
            cmd.Parameters.AddWithValue("$durchmesser", CDbl(f.Durchmesser))
            cmd.Parameters.AddWithValue("$gewicht", f.Gewicht)
            cmd.Parameters.AddWithValue("$rest", f.Restgewicht)
            cmd.Parameters.AddWithValue("$spule", f.Spulengewicht)
            cmd.Parameters.AddWithValue("$preis", CDbl(f.Preis))
            cmd.Parameters.AddWithValue("$kauf", f.Kaufdatum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            cmd.Parameters.AddWithValue("$notiz", If(f.Notiz, ""))
            cmd.Parameters.AddWithValue("$lagerort", If(f.Lagerort, "").Trim())
            cmd.Parameters.AddWithValue("$duese", Math.Max(0, f.DuesenTemp))
            cmd.Parameters.AddWithValue("$bett", Math.Max(0, f.BettTemp))
            cmd.Parameters.AddWithValue("$trocknung", If(f.Trocknung, "").Trim())
            cmd.Parameters.AddWithValue("$shop", If(f.ShopLink, "").Trim())
            cmd.Parameters.AddWithValue("$rfid", If(f.RfidKennung, "").Trim().ToUpperInvariant())
            cmd.Parameters.AddWithValue("$barcode", If(f.Barcode, "").Trim())

            If f.Id = 0 Then
                f.Id = Convert.ToInt64(cmd.ExecuteScalar())
            Else
                cmd.ExecuteNonQuery()
            End If

            ' Leerspulengewicht als Vorlage für diesen Hersteller merken
            If f.Spulengewicht > 0 AndAlso Not String.IsNullOrWhiteSpace(f.Hersteller) Then
                Befehl(c, t, "INSERT INTO SpulenVorlage (Hersteller, Gewicht) VALUES ($h, $g) " &
                             "ON CONFLICT(Hersteller) DO UPDATE SET Gewicht = excluded.Gewicht",
                       "$h", f.Hersteller.Trim(), "$g", f.Spulengewicht).ExecuteNonQuery()
            End If

            If altRest.HasValue AndAlso altRest.Value <> f.Restgewicht Then
                Dim gramm = altRest.Value - f.Restgewicht
                BuchungEinfuegen(c, t, New Buchung With {
                    .FilamentId = f.Id, .Art = Buchung.ArtKorrektur, .Gramm = gramm,
                    .Bezeichnung = "Restgewicht im Filament geändert",
                    .Kosten = Kosten(gramm, f.Preis, f.Gewicht)})
            End If
            t.Commit()
        End Using
    End Sub

    ''' <summary>Merkt die RFID-Kennung an der Spule (eine Kennung gehört nur zu einer Spule; leer = vergessen).</summary>
    Public Sub RfidKennungSetzen(filamentId As Long, kennung As String) Implements IDatenquelle.RfidKennungSetzen
        Dim k = If(kennung, "").Trim().ToUpperInvariant()
        Using c = Oeffnen(), t = c.BeginTransaction()
            If k.Length > 0 Then Befehl(c, t, "UPDATE Filament SET RfidKennung = '' WHERE RfidKennung = $k AND Id <> $id", "$k", k, "$id", filamentId).ExecuteNonQuery()
            Befehl(c, t, "UPDATE Filament SET RfidKennung = $k WHERE Id = $id", "$k", k, "$id", filamentId).ExecuteNonQuery()
            t.Commit()
        End Using
    End Sub

    Public Sub Loeschen(id As Long) Implements IDatenquelle.Loeschen
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "DELETE FROM Filament WHERE Id = $id"
            cmd.Parameters.AddWithValue("$id", id)
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>True, wenn die ID bereits von einem anderen Filament verwendet wird.</summary>
    Public Function CodeExistiert(code As String, ausserId As Long) As Boolean Implements IDatenquelle.CodeExistiert
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "SELECT COUNT(*) FROM Filament WHERE Code = $code AND Id <> $id"
            cmd.Parameters.AddWithValue("$code", code.Trim())
            cmd.Parameters.AddWithValue("$id", ausserId)
            Return Convert.ToInt64(cmd.ExecuteScalar()) > 0
        End Using
    End Function

    ''' <summary>Liefert die nächste freie ID im Format FIL-0001.</summary>
    Public Function NaechsterCode() As String Implements IDatenquelle.NaechsterCode
        Dim hoechste = 0
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "SELECT Code FROM Filament WHERE Code LIKE 'FIL-%'"
            Using r = cmd.ExecuteReader()
                While r.Read()
                    Dim nummer As Integer
                    Dim code = r.GetString(0)
                    If Integer.TryParse(code.Substring(CodePraefix.Length), nummer) AndAlso nummer > hoechste Then
                        hoechste = nummer
                    End If
                End While
            End Using
        End Using
        Return CodePraefix & (hoechste + 1).ToString("0000", CultureInfo.InvariantCulture)
    End Function

    ' ------------------------------------------------------------------ Buchungen (Drucke, Wiegen, Korrekturen)

    Private Const DatumZeitFormat As String = "yyyy-MM-dd HH:mm:ss"

    ''' <summary>
    ''' Setzt das Restgewicht einer Spule und protokolliert die Differenz als Buchung
    ''' (Verbrauch abziehen = Druck, Nachwiegen / direkte Eingabe = Wiegen).
    ''' </summary>
    Public Sub RestgewichtBuchen(filamentId As Long, neuesRestgewicht As Integer, art As String, bezeichnung As String) Implements IDatenquelle.RestgewichtBuchen
        Using c = Oeffnen(), t = c.BeginTransaction()
            Dim spule = SpuleLesen(c, t, filamentId)
            Dim gramm = spule.Restgewicht - neuesRestgewicht
            If gramm = 0 AndAlso art <> Buchung.ArtDruck Then Return
            Befehl(c, t, "UPDATE Filament SET Restgewicht = $rest WHERE Id = $id",
                   "$rest", Math.Max(0, neuesRestgewicht), "$id", filamentId).ExecuteNonQuery()
            BuchungEinfuegen(c, t, New Buchung With {
                .FilamentId = filamentId, .Art = art, .Gramm = gramm, .Bezeichnung = If(bezeichnung, "").Trim(),
                .Kosten = Kosten(gramm, spule.Preis, spule.Gewicht)})
            t.Commit()
        End Using
    End Sub

    ''' <summary>
    ''' Speichert einen Druck und passt das Restgewicht an. Beim Bearbeiten wird der alte Verbrauch
    ''' zuerst zurückgebucht (auch wenn der Druck einer anderen Spule zugeordnet wird).
    ''' </summary>
    Public Sub DruckSpeichern(b As Buchung) Implements IDatenquelle.DruckSpeichern
        Using c = Oeffnen(), t = c.BeginTransaction()
            If b.Id <> 0 Then
                Using r = Befehl(c, t, "SELECT FilamentId, Gramm FROM Buchung WHERE Id = $id", "$id", b.Id).ExecuteReader()
                    If r.Read() Then
                        Dim altFilament = r.GetInt64(0), altGramm = r.GetInt32(1)
                        r.Close()
                        Zurueckbuchen(c, t, altFilament, altGramm)
                    End If
                End Using
            End If

            Dim spule = SpuleLesen(c, t, b.FilamentId)
            b.Kosten = Kosten(b.Gramm, spule.Preis, spule.Gewicht)
            Befehl(c, t, "UPDATE Filament SET Restgewicht = MAX(0, Restgewicht - $gramm) WHERE Id = $id",
                   "$gramm", b.Gramm, "$id", b.FilamentId).ExecuteNonQuery()

            If b.Id = 0 Then
                BuchungEinfuegen(c, t, b)
            Else
                Befehl(c, t, "UPDATE Buchung SET FilamentId = $filament, Datum = $datum, Art = $art, Gramm = $gramm, " &
                             "Bezeichnung = $bezeichnung, DateiId = $datei, Druckzeit = $zeit, Kosten = $kosten, Notiz = $notiz, Foto = $foto, Drucker = $drucker, Fehldruck = $fehl " &
                             "WHERE Id = $id",
                       "$filament", b.FilamentId, "$datum", b.Datum.ToString(DatumZeitFormat, CultureInfo.InvariantCulture),
                       "$art", b.Art, "$gramm", b.Gramm, "$bezeichnung", If(b.Bezeichnung, "").Trim(),
                       "$datei", b.DateiId, "$zeit", b.Druckzeit, "$kosten", CDbl(b.Kosten), "$notiz", If(b.Notiz, ""),
                       "$foto", If(b.Foto, ""), "$drucker", If(b.Drucker, ""), "$fehl", If(b.Fehldruck, 1, 0), "$id", b.Id).ExecuteNonQuery()
            End If
            t.Commit()
        End Using
    End Sub

    ''' <summary>Löscht eine Buchung; optional wird der Verbrauch der Spule wieder gutgeschrieben.</summary>
    Public Sub BuchungLoeschen(id As Long, zurueckbuchenJa As Boolean) Implements IDatenquelle.BuchungLoeschen
        Using c = Oeffnen(), t = c.BeginTransaction()
            If zurueckbuchenJa Then
                Using r = Befehl(c, t, "SELECT FilamentId, Gramm FROM Buchung WHERE Id = $id", "$id", id).ExecuteReader()
                    If r.Read() Then
                        Dim filament = r.GetInt64(0), gramm = r.GetInt32(1)
                        r.Close()
                        Zurueckbuchen(c, t, filament, gramm)
                    End If
                End Using
            End If
            Befehl(c, t, "DELETE FROM Buchung WHERE Id = $id", "$id", id).ExecuteNonQuery()
            t.Commit()
        End Using
    End Sub

    Public Function BuchungenLaden() As List(Of Buchung) Implements IDatenquelle.BuchungenLaden
        Dim liste As New List(Of Buchung)
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText =
                "SELECT b.Id, b.FilamentId, b.Datum, b.Art, b.Gramm, b.Bezeichnung, b.DateiId, b.Druckzeit, b.Kosten, b.Notiz, " &
                "f.Code, f.Hersteller, f.Material, f.Farbe, d.Name, b.Foto, b.Drucker, b.Fehldruck " &
                "FROM Buchung b LEFT JOIN Filament f ON f.Id = b.FilamentId LEFT JOIN Datei d ON d.Id = b.DateiId " &
                "ORDER BY b.Datum DESC, b.Id DESC"
            Using r = cmd.ExecuteReader()
                While r.Read()
                    Dim d As Date
                    If Not Date.TryParseExact(Text(r, 2), DatumZeitFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then
                        d = DatumLesen(Text(r, 2))
                    End If
                    Dim vorhanden = Not r.IsDBNull(10)
                    liste.Add(New Buchung With {
                        .Id = r.GetInt64(0),
                        .FilamentId = r.GetInt64(1),
                        .Datum = d,
                        .Art = Text(r, 3),
                        .Gramm = r.GetInt32(4),
                        .Bezeichnung = Text(r, 5),
                        .DateiId = If(r.IsDBNull(6), CType(Nothing, Long?), r.GetInt64(6)),
                        .Druckzeit = If(r.IsDBNull(7), CType(Nothing, Integer?), r.GetInt32(7)),
                        .Kosten = Math.Round(CDec(r.GetDouble(8)), 2),
                        .Notiz = Text(r, 9),
                        .FilamentCode = If(vorhanden, Text(r, 10), "(gelöscht)"),
                        .FilamentText = If(vorhanden, String.Join(" ", {Text(r, 11), Text(r, 12), Text(r, 13)}.Where(Function(x) x.Length > 0)), ""),
                        .DateiName = Text(r, 14),
                        .Foto = Text(r, 15),
                        .Drucker = Text(r, 16),
                        .Fehldruck = r.GetInt64(17) <> 0
                    })
                End While
            End Using
        End Using
        Return liste
    End Function

    Private Function SpuleLesen(c As SqliteConnection, t As SqliteTransaction, id As Long) As (Restgewicht As Integer, Preis As Decimal, Gewicht As Integer)
        Using r = Befehl(c, t, "SELECT Restgewicht, Preis, Gewicht FROM Filament WHERE Id = $id", "$id", id).ExecuteReader()
            If Not r.Read() Then Throw New InvalidOperationException("Die Spule wurde nicht gefunden (evtl. gelöscht).")
            Return (r.GetInt32(0), CDec(r.GetDouble(1)), r.GetInt32(2))
        End Using
    End Function

    Private Sub Zurueckbuchen(c As SqliteConnection, t As SqliteTransaction, filamentId As Long, gramm As Integer)
        Befehl(c, t, "UPDATE Filament SET Restgewicht = MAX(0, MIN(Gewicht, Restgewicht + $gramm)) WHERE Id = $id",
               "$gramm", gramm, "$id", filamentId).ExecuteNonQuery()
    End Sub

    Private Sub BuchungEinfuegen(c As SqliteConnection, t As SqliteTransaction, b As Buchung)
        b.Id = Convert.ToInt64(Befehl(c, t,
            "INSERT INTO Buchung (FilamentId, Datum, Art, Gramm, Bezeichnung, DateiId, Druckzeit, Kosten, Notiz, Foto, Drucker, Fehldruck) " &
            "VALUES ($filament, $datum, $art, $gramm, $bezeichnung, $datei, $zeit, $kosten, $notiz, $foto, $drucker, $fehl); SELECT last_insert_rowid();",
            "$filament", b.FilamentId, "$datum", b.Datum.ToString(DatumZeitFormat, CultureInfo.InvariantCulture),
            "$art", b.Art, "$gramm", b.Gramm, "$bezeichnung", If(b.Bezeichnung, "").Trim(), "$datei", b.DateiId,
            "$zeit", b.Druckzeit, "$kosten", CDbl(b.Kosten), "$notiz", If(b.Notiz, ""), "$foto", If(b.Foto, ""), "$drucker", If(b.Drucker, ""), "$fehl", If(b.Fehldruck, 1, 0)).ExecuteScalar())
    End Sub

    Private Shared Function Kosten(gramm As Integer, preis As Decimal, gewicht As Integer) As Decimal
        If gewicht <= 0 Then Return 0D
        Return Math.Round(preis * gramm / gewicht, 2)
    End Function

    ''' <summary>Erstellt einen Befehl mit Parametern (abwechselnd Name, Wert; Nothing wird zu NULL).</summary>
    Private Shared Function Befehl(c As SqliteConnection, t As SqliteTransaction, sql As String,
                                   ParamArray parameter As Object()) As SqliteCommand
        Dim cmd = c.CreateCommand()
        cmd.Transaction = t
        cmd.CommandText = sql
        For i = 0 To parameter.Length - 2 Step 2
            cmd.Parameters.AddWithValue(CStr(parameter(i)), If(parameter(i + 1), DBNull.Value))
        Next
        Return cmd
    End Function

    ' ------------------------------------------------------------------ Drucker

    Public Function DruckerLaden() As List(Of Drucker) Implements IDatenquelle.DruckerLaden
        Dim liste As New List(Of Drucker)
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "SELECT Id, Name, Typ, Adresse, Zugang, Benutzer, Seriennummer, Aktiv FROM Drucker ORDER BY Name"
            Using r = cmd.ExecuteReader()
                While r.Read()
                    liste.Add(New Drucker With {
                        .Id = r.GetInt64(0), .Name = Text(r, 1), .Typ = Text(r, 2), .Adresse = Text(r, 3),
                        .Zugang = Text(r, 4), .Benutzer = Text(r, 5), .Seriennummer = Text(r, 6), .Aktiv = r.GetInt64(7) <> 0})
                End While
            End Using
        End Using
        Return liste
    End Function

    Public Sub DruckerSpeichern(d As Drucker) Implements IDatenquelle.DruckerSpeichern
        Using c = Oeffnen()
            If d.Id = 0 Then
                d.Id = Convert.ToInt64(Befehl(c, Nothing,
                    "INSERT INTO Drucker (Name, Typ, Adresse, Zugang, Benutzer, Seriennummer, Aktiv) " &
                    "VALUES ($n, $t, $a, $z, $b, $s, $akt); SELECT last_insert_rowid();",
                    "$n", d.Name.Trim(), "$t", d.Typ, "$a", d.Adresse.Trim(), "$z", d.Zugang.Trim(), "$b", d.Benutzer.Trim(),
                    "$s", d.Seriennummer.Trim(), "$akt", If(d.Aktiv, 1, 0)).ExecuteScalar())
            Else
                Befehl(c, Nothing, "UPDATE Drucker SET Name = $n, Typ = $t, Adresse = $a, Zugang = $z, Benutzer = $b, " &
                                   "Seriennummer = $s, Aktiv = $akt WHERE Id = $id",
                       "$n", d.Name.Trim(), "$t", d.Typ, "$a", d.Adresse.Trim(), "$z", d.Zugang.Trim(), "$b", d.Benutzer.Trim(),
                       "$s", d.Seriennummer.Trim(), "$akt", If(d.Aktiv, 1, 0), "$id", d.Id).ExecuteNonQuery()
            End If
        End Using
    End Sub

    Public Sub DruckerLoeschen(id As Long) Implements IDatenquelle.DruckerLoeschen
        Using c = Oeffnen(), t = c.BeginTransaction()
            Befehl(c, t, "DELETE FROM DruckerSpule WHERE DruckerId = $id", "$id", id).ExecuteNonQuery()
            Befehl(c, t, "DELETE FROM Drucker WHERE Id = $id", "$id", id).ExecuteNonQuery()
            t.Commit()
        End Using
    End Sub

    ''' <summary>Spulen-Zuordnung je Fach (Slot → FilamentId).</summary>
    Public Function SpulenZuordnung(druckerId As Long) As Dictionary(Of Integer, Long) Implements IDatenquelle.SpulenZuordnung
        Dim ergebnis As New Dictionary(Of Integer, Long)
        Using c = Oeffnen()
            Using r = Befehl(c, Nothing, "SELECT Slot, FilamentId FROM DruckerSpule WHERE DruckerId = $id", "$id", druckerId).ExecuteReader()
                While r.Read()
                    ergebnis(r.GetInt32(0)) = r.GetInt64(1)
                End While
            End Using
        End Using
        Return ergebnis
    End Function

    ''' <summary>Ordnet einem Fach eine Spule zu (filamentId = Nothing entfernt die Zuordnung).</summary>
    Public Sub SpuleZuordnen(druckerId As Long, slot As Integer, filamentId As Long?) Implements IDatenquelle.SpuleZuordnen
        Using c = Oeffnen()
            If filamentId.HasValue Then
                Befehl(c, Nothing, "INSERT INTO DruckerSpule (DruckerId, Slot, FilamentId) VALUES ($d, $s, $f) " &
                                   "ON CONFLICT(DruckerId, Slot) DO UPDATE SET FilamentId = excluded.FilamentId",
                       "$d", druckerId, "$s", slot, "$f", filamentId.Value).ExecuteNonQuery()
            Else
                Befehl(c, Nothing, "DELETE FROM DruckerSpule WHERE DruckerId = $d AND Slot = $s", "$d", druckerId, "$s", slot).ExecuteNonQuery()
            End If
        End Using
    End Sub

    ' ------------------------------------------------------------------ Push-Benachrichtigungen (Server)

    ''' <summary>Gerät für Push-Benachrichtigungen anmelden (Endpunkt und Schlüssel aus dem Browser).</summary>
    Public Sub PushAboSpeichern(a As PushAbo)
        Using c = Oeffnen()
            Befehl(c, Nothing, "INSERT INTO PushAbo (Endpunkt, P256dh, Auth, Geraet, Erstellt) VALUES ($e, $p, $a, $g, $z) " &
                               "ON CONFLICT(Endpunkt) DO UPDATE SET P256dh = excluded.P256dh, Auth = excluded.Auth, Geraet = excluded.Geraet",
                   "$e", a.Endpunkt, "$p", a.P256dh, "$a", a.Auth, "$g", If(a.Geraet, ""),
                   "$z", Date.Now.ToString(DatumZeitFormat, CultureInfo.InvariantCulture)).ExecuteNonQuery()
        End Using
    End Sub

    Public Function PushGeraeteLaden() As List(Of PushGeraet)
        Dim liste As New List(Of PushGeraet)
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "SELECT Geraet, Erstellt FROM PushAbo ORDER BY Erstellt"
            Using r = cmd.ExecuteReader()
                While r.Read()
                    liste.Add(New PushGeraet With {.Geraet = Text(r, 0), .Erstellt = Text(r, 1)})
                End While
            End Using
        End Using
        Return liste
    End Function

    Public Function PushAbosLaden() As List(Of PushAbo)
        Dim liste As New List(Of PushAbo)
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "SELECT Endpunkt, P256dh, Auth, Geraet FROM PushAbo"
            Using r = cmd.ExecuteReader()
                While r.Read()
                    liste.Add(New PushAbo With {.Endpunkt = r.GetString(0), .P256dh = r.GetString(1), .Auth = r.GetString(2), .Geraet = Text(r, 3)})
                End While
            End Using
        End Using
        Return liste
    End Function

    Public Sub PushAboLoeschen(endpunkt As String)
        Using c = Oeffnen()
            Befehl(c, Nothing, "DELETE FROM PushAbo WHERE Endpunkt = $e", "$e", endpunkt).ExecuteNonQuery()
        End Using
    End Sub

    ' ------------------------------------------------------------------ Offene Druckenden (Server)

    ''' <summary>Merkt ein erkanntes Druckende, bis es an einem PC gebucht oder verworfen wurde (setzt ende.Id).</summary>
    Public Sub DruckEndeSpeichern(ende As DruckEnde)
        Using c = Oeffnen()
            ende.Id = Convert.ToInt64(Befehl(c, Nothing,
                "INSERT INTO DruckEnde (Zeitpunkt, Daten) VALUES ($zeit, $daten); SELECT last_insert_rowid();",
                "$zeit", ende.Zeitpunkt.ToString(DatumZeitFormat, CultureInfo.InvariantCulture),
                "$daten", System.Text.Json.JsonSerializer.Serialize(ende)).ExecuteScalar())
        End Using
    End Sub

    Public Function OffeneDruckEnden() As List(Of DruckEnde)
        Dim liste As New List(Of DruckEnde)
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "SELECT Id, Daten FROM DruckEnde ORDER BY Id"
            Using r = cmd.ExecuteReader()
                While r.Read()
                    Dim ende = System.Text.Json.JsonSerializer.Deserialize(Of DruckEnde)(r.GetString(1))
                    If ende Is Nothing Then Continue While
                    ende.Id = r.GetInt64(0)
                    liste.Add(ende)
                End While
            End Using
        End Using
        Return liste
    End Function

    ''' <summary>Gespeichertes Druckende ändern (z. B. wenn das Foto nachträglich dazukommt).</summary>
    Public Sub DruckEndeAktualisieren(ende As DruckEnde)
        Using c = Oeffnen()
            Befehl(c, Nothing, "UPDATE DruckEnde SET Daten = $daten WHERE Id = $id",
                   "$daten", System.Text.Json.JsonSerializer.Serialize(ende), "$id", ende.Id).ExecuteNonQuery()
        End Using
    End Sub

    ' ------------------------------------------------------------------ Fotos vom Druckende

    ''' <summary>Ordner neben der Datenbank für die Kamerabilder vom Druckende.</summary>
    Public ReadOnly Property FotosOrdner As String
        Get
            Return Path.Combine(If(Path.GetDirectoryName(Pfad), ""), "Fotos")
        End Get
    End Property

    ''' <summary>Foto als JPEG; Nothing, wenn es fehlt. Nur einfache Dateinamen (kein Pfad).</summary>
    Public Function FotoLaden(name As String) As Byte() Implements IDatenquelle.FotoLaden
        If String.IsNullOrWhiteSpace(name) OrElse name <> Path.GetFileName(name) Then Return Nothing
        Dim datei = Path.Combine(FotosOrdner, name)
        Return If(File.Exists(datei), File.ReadAllBytes(datei), Nothing)
    End Function

    ''' <summary>Foto nachträglich an eine Buchung hängen (automatisch gebuchter Druck).</summary>
    Public Sub BuchungFotoSetzen(buchungId As Long, name As String)
        Using c = Oeffnen()
            Befehl(c, Nothing, "UPDATE Buchung SET Foto = $f WHERE Id = $id", "$f", If(name, ""), "$id", buchungId).ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>Löscht das Foto, wenn keine Buchung es mehr verwendet.</summary>
    Public Sub FotoLoeschenWennUnbenutzt(name As String)
        If String.IsNullOrWhiteSpace(name) OrElse name <> Path.GetFileName(name) Then Return
        Using c = Oeffnen()
            If Convert.ToInt64(Befehl(c, Nothing, "SELECT COUNT(*) FROM Buchung WHERE Foto = $f", "$f", name).ExecuteScalar()) > 0 Then Return
        End Using
        Dim datei = Path.Combine(FotosOrdner, name)
        If File.Exists(datei) Then File.Delete(datei)
    End Sub

    ''' <summary>Entfernt ein offenes Druckende; False, wenn es schon erledigt war (z. B. an einem anderen PC).</summary>
    Public Function DruckEndeErledigt(id As Long) As Boolean
        Using c = Oeffnen()
            Return Befehl(c, Nothing, "DELETE FROM DruckEnde WHERE Id = $id", "$id", id).ExecuteNonQuery() > 0
        End Using
    End Function

    ' ------------------------------------------------------------------ Sicherung

    Public Function SicherungenLaden() As List(Of SicherungInfo) Implements IDatenquelle.SicherungenLaden
        Return Sicherung.Liste(Me)
    End Function

    Public Function SicherungErstellen(art As String) As SicherungInfo Implements IDatenquelle.SicherungErstellen
        Return Sicherung.Erstellen(Me, art)
    End Function

    Public Sub SicherungWiederherstellen(s As SicherungInfo) Implements IDatenquelle.SicherungWiederherstellen
        Sicherung.Wiederherstellen(Me, s)
    End Sub
    ''' <summary>Schreibt eine konsistente Kopie der Datenbank (SQLite-Backup, auch während Zugriffen sicher).</summary>
    Public Sub SichernNach(zielPfad As String)
        Using quelle = Oeffnen(), ziel As New SqliteConnection(Verbindung(zielPfad, SqliteOpenMode.ReadWriteCreate))
            ziel.Open()
            quelle.BackupDatabase(ziel)
        End Using
    End Sub

    ''' <summary>Ersetzt den Inhalt der Datenbank durch den einer Sicherung.</summary>
    Public Sub WiederherstellenAus(sicherungPfad As String)
        Using quelle As New SqliteConnection(Verbindung(sicherungPfad, SqliteOpenMode.ReadOnly)), ziel = Oeffnen()
            quelle.Open()
            quelle.BackupDatabase(ziel)
        End Using
        ' Sicherungen älterer Versionen: fehlende Tabellen ergänzen
        TabelleAnlegen()
    End Sub

    ''' <summary>Prüft, ob die Datei eine unbeschädigte Datenbank dieser Anwendung ist.</summary>
    Public Shared Function IstGueltigeSicherung(pfad As String, ByRef grund As String) As Boolean
        Try
            Using c As New SqliteConnection(Verbindung(pfad, SqliteOpenMode.ReadOnly))
                c.Open()
                Using cmd = c.CreateCommand()
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Filament'"
                    If Convert.ToInt64(cmd.ExecuteScalar()) = 0 Then
                        grund = "Die Datei enthält keine Filament-Daten."
                        Return False
                    End If
                    cmd.CommandText = "PRAGMA quick_check"
                    Dim ergebnis = Convert.ToString(cmd.ExecuteScalar())
                    If Not String.Equals(ergebnis, "ok", StringComparison.OrdinalIgnoreCase) Then
                        grund = "Die Datei ist beschädigt (" & ergebnis & ")."
                        Return False
                    End If
                End Using
            End Using
            grund = ""
            Return True
        Catch ex As Exception
            grund = ex.Message
            Return False
        End Try
    End Function

    ''' <summary>Verbindung ohne Pooling, damit die Datei danach sofort wieder frei ist.</summary>
    Private Shared Function Verbindung(pfad As String, modus As SqliteOpenMode) As String
        Return New SqliteConnectionStringBuilder With {.DataSource = pfad, .Mode = modus, .Pooling = False}.ToString()
    End Function

    ' ------------------------------------------------------------------ Dateien (CAD, STL, G-Code …)

    ''' <summary>Ordner neben der Datenbank, in den hinzugefügte Dateien kopiert werden.</summary>
    Public ReadOnly Property DateienOrdner As String Implements IDatenquelle.DateienOrdner
        Get
            Return Path.Combine(If(Path.GetDirectoryName(Pfad), ""), "Dateien")
        End Get
    End Property

    Public Function DateiPfad(d As Datei) As String Implements IDatenquelle.DateiPfad
        Return Path.Combine(DateienOrdner, d.Pfad)
    End Function

    ''' <summary>Lokal liegt die Datei immer direkt im Dateien-Ordner.</summary>
    Public Function DateiVorhanden(d As Datei) As Boolean Implements IDatenquelle.DateiVorhanden
        Return True
    End Function

    Public Function DateiLadenAsync(d As Datei, fortschritt As IProgress(Of Double), abbruch As Threading.CancellationToken) As Task(Of String) Implements IDatenquelle.DateiLadenAsync
        Return Task.FromResult(DateiPfad(d))
    End Function

    Public Function DateienLaden() As List(Of Datei) Implements IDatenquelle.DateienLaden
        Dim liste As New List(Of Datei)
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "SELECT Id, Name, Pfad, Typ, Groesse, Hinzugefuegt, Gramm, Druckzeit, Material, Notiz " &
                              "FROM Datei ORDER BY Hinzugefuegt DESC"
            Using r = cmd.ExecuteReader()
                While r.Read()
                    Dim d As Date
                    If Not Date.TryParseExact(Text(r, 5), DatumZeitFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then
                        d = Date.Today
                    End If
                    liste.Add(New Datei With {
                        .Id = r.GetInt64(0),
                        .Name = Text(r, 1),
                        .Pfad = Text(r, 2),
                        .Typ = Text(r, 3),
                        .Groesse = r.GetInt64(4),
                        .Hinzugefuegt = d,
                        .Gramm = If(r.IsDBNull(6), CType(Nothing, Double?), r.GetDouble(6)),
                        .Druckzeit = If(r.IsDBNull(7), CType(Nothing, Integer?), r.GetInt32(7)),
                        .Material = Text(r, 8),
                        .Notiz = Text(r, 9)
                    })
                End While
            End Using
        End Using
        Return liste
    End Function

    ''' <summary>Kopiert eine Datei in den Dateien-Ordner, liest Druckdaten aus und legt den Eintrag an.</summary>
    Public Function DateiImportieren(quelle As String) As Datei Implements IDatenquelle.DateiImportieren
        Directory.CreateDirectory(DateienOrdner)
        Dim name = Path.GetFileName(quelle)
        Dim zielName = name
        Dim nummer = 2
        While File.Exists(Path.Combine(DateienOrdner, zielName))
            zielName = $"{Path.GetFileNameWithoutExtension(name)} ({nummer}){Path.GetExtension(name)}"
            nummer += 1
        End While
        Dim ziel = Path.Combine(DateienOrdner, zielName)
        File.Copy(quelle, ziel)

        Dim info = DateiAnalyse.Analysieren(ziel)
        Dim d As New Datei With {
            .Name = name,
            .Pfad = zielName,
            .Typ = Path.GetExtension(name).TrimStart("."c).ToUpperInvariant(),
            .Groesse = New FileInfo(ziel).Length,
            .Hinzugefuegt = Date.Now,
            .Gramm = info.Gramm,
            .Druckzeit = info.Druckzeit,
            .Material = info.Material
        }
        Try
            Using c = Oeffnen()
                d.Id = Convert.ToInt64(Befehl(c, Nothing,
                    "INSERT INTO Datei (Name, Pfad, Typ, Groesse, Hinzugefuegt, Gramm, Druckzeit, Material, Notiz) " &
                    "VALUES ($name, $pfad, $typ, $groesse, $hinzu, $gramm, $zeit, $material, ''); SELECT last_insert_rowid();",
                    "$name", d.Name, "$pfad", d.Pfad, "$typ", d.Typ, "$groesse", d.Groesse,
                    "$hinzu", d.Hinzugefuegt.ToString(DatumZeitFormat, CultureInfo.InvariantCulture),
                    "$gramm", d.Gramm, "$zeit", d.Druckzeit, "$material", d.Material).ExecuteScalar())
            End Using
        Catch
            ' Ohne Datenbankeintrag soll keine verwaiste Kopie zurückbleiben
            File.Delete(ziel)
            Throw
        End Try
        Return d
    End Function

    Public Sub DateiNotizSpeichern(id As Long, notiz As String) Implements IDatenquelle.DateiNotizSpeichern
        Using c = Oeffnen()
            Befehl(c, Nothing, "UPDATE Datei SET Notiz = $notiz WHERE Id = $id", "$notiz", If(notiz, ""), "$id", id).ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>Entfernt den Eintrag und die Datei im Dateien-Ordner. Drucke behalten ihre Daten, verlieren nur die Verknüpfung.</summary>
    Public Sub DateiLoeschen(d As Datei) Implements IDatenquelle.DateiLoeschen
        Using c = Oeffnen(), t = c.BeginTransaction()
            Befehl(c, t, "UPDATE Buchung SET DateiId = NULL WHERE DateiId = $id", "$id", d.Id).ExecuteNonQuery()
            Befehl(c, t, "DELETE FROM Datei WHERE Id = $id", "$id", d.Id).ExecuteNonQuery()
            t.Commit()
        End Using
        Dim pfadDatei = DateiPfad(d)
        If File.Exists(pfadDatei) Then File.Delete(pfadDatei)
    End Sub

    Private Shared Function Text(r As SqliteDataReader, spalte As Integer) As String
        If r.IsDBNull(spalte) Then Return ""
        Return r.GetString(spalte)
    End Function

    Private Shared Function DatumLesen(wert As String) As Date
        Dim d As Date
        If Date.TryParseExact(wert, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then Return d
        Return Date.Today
    End Function

End Class
