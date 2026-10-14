Imports System.Globalization
Imports System.IO
Imports Microsoft.Data.Sqlite

''' <summary>
''' Zugriff auf die SQLite-Datenbank (eine einzelne Datei, kein Server nötig).
''' </summary>
Public Class Datenbank

    Private Const CodePraefix As String = "FIL-"
    Private ReadOnly _verbindung As String

    Public ReadOnly Property Pfad As String

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
CREATE INDEX IF NOT EXISTS IX_Buchung_Filament ON Buchung (FilamentId);"
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>Liest eine gespeicherte Einstellung (Nothing, wenn sie noch nicht existiert).</summary>
    Public Function EinstellungLesen(schluessel As String) As String
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "SELECT Wert FROM Einstellungen WHERE Schluessel = $schluessel"
            cmd.Parameters.AddWithValue("$schluessel", schluessel)
            Return TryCast(cmd.ExecuteScalar(), String)
        End Using
    End Function

    Public Sub EinstellungSpeichern(schluessel As String, wert As String)
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "INSERT INTO Einstellungen (Schluessel, Wert) VALUES ($schluessel, $wert) " &
                              "ON CONFLICT(Schluessel) DO UPDATE SET Wert = excluded.Wert"
            cmd.Parameters.AddWithValue("$schluessel", schluessel)
            cmd.Parameters.AddWithValue("$wert", If(wert, ""))
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    Public Function AlleLaden() As List(Of Filament)
        Dim liste As New List(Of Filament)
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "SELECT Id, Code, Hersteller, Material, Farbe, Durchmesser, Gewicht, Restgewicht, " &
                              "Spulengewicht, Preis, Kaufdatum, Notiz FROM Filament ORDER BY Code"
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
                        .Notiz = Text(r, 11)
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
    Public Sub Speichern(f As Filament)
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
                    "Spulengewicht, Preis, Kaufdatum, Notiz) VALUES ($code, $hersteller, $material, $farbe, " &
                    "$durchmesser, $gewicht, $rest, $spule, $preis, $kauf, $notiz); SELECT last_insert_rowid();"
            Else
                cmd.CommandText =
                    "UPDATE Filament SET Code = $code, Hersteller = $hersteller, Material = $material, Farbe = $farbe, " &
                    "Durchmesser = $durchmesser, Gewicht = $gewicht, Restgewicht = $rest, Spulengewicht = $spule, " &
                    "Preis = $preis, Kaufdatum = $kauf, Notiz = $notiz WHERE Id = $id"
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

            If f.Id = 0 Then
                f.Id = Convert.ToInt64(cmd.ExecuteScalar())
            Else
                cmd.ExecuteNonQuery()
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

    Public Sub Loeschen(id As Long)
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "DELETE FROM Filament WHERE Id = $id"
            cmd.Parameters.AddWithValue("$id", id)
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>True, wenn die ID bereits von einem anderen Filament verwendet wird.</summary>
    Public Function CodeExistiert(code As String, ausserId As Long) As Boolean
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText = "SELECT COUNT(*) FROM Filament WHERE Code = $code AND Id <> $id"
            cmd.Parameters.AddWithValue("$code", code.Trim())
            cmd.Parameters.AddWithValue("$id", ausserId)
            Return Convert.ToInt64(cmd.ExecuteScalar()) > 0
        End Using
    End Function

    ''' <summary>Liefert die nächste freie ID im Format FIL-0001.</summary>
    Public Function NaechsterCode() As String
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
    Public Sub RestgewichtBuchen(filamentId As Long, neuesRestgewicht As Integer, art As String, bezeichnung As String)
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
    Public Sub DruckSpeichern(b As Buchung)
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
                             "Bezeichnung = $bezeichnung, DateiId = $datei, Druckzeit = $zeit, Kosten = $kosten, Notiz = $notiz " &
                             "WHERE Id = $id",
                       "$filament", b.FilamentId, "$datum", b.Datum.ToString(DatumZeitFormat, CultureInfo.InvariantCulture),
                       "$art", b.Art, "$gramm", b.Gramm, "$bezeichnung", If(b.Bezeichnung, "").Trim(),
                       "$datei", b.DateiId, "$zeit", b.Druckzeit, "$kosten", CDbl(b.Kosten), "$notiz", If(b.Notiz, ""),
                       "$id", b.Id).ExecuteNonQuery()
            End If
            t.Commit()
        End Using
    End Sub

    ''' <summary>Löscht eine Buchung; optional wird der Verbrauch der Spule wieder gutgeschrieben.</summary>
    Public Sub BuchungLoeschen(id As Long, zurueckbuchenJa As Boolean)
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

    Public Function BuchungenLaden() As List(Of Buchung)
        Dim liste As New List(Of Buchung)
        Using c = Oeffnen(), cmd = c.CreateCommand()
            cmd.CommandText =
                "SELECT b.Id, b.FilamentId, b.Datum, b.Art, b.Gramm, b.Bezeichnung, b.DateiId, b.Druckzeit, b.Kosten, b.Notiz, " &
                "f.Code, f.Hersteller, f.Material, f.Farbe, d.Name " &
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
                        .DateiName = Text(r, 14)
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
            "INSERT INTO Buchung (FilamentId, Datum, Art, Gramm, Bezeichnung, DateiId, Druckzeit, Kosten, Notiz) " &
            "VALUES ($filament, $datum, $art, $gramm, $bezeichnung, $datei, $zeit, $kosten, $notiz); SELECT last_insert_rowid();",
            "$filament", b.FilamentId, "$datum", b.Datum.ToString(DatumZeitFormat, CultureInfo.InvariantCulture),
            "$art", b.Art, "$gramm", b.Gramm, "$bezeichnung", If(b.Bezeichnung, "").Trim(), "$datei", b.DateiId,
            "$zeit", b.Druckzeit, "$kosten", CDbl(b.Kosten), "$notiz", If(b.Notiz, "")).ExecuteScalar())
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

    ' ------------------------------------------------------------------ Sicherung

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
    Public ReadOnly Property DateienOrdner As String
        Get
            Return Path.Combine(If(Path.GetDirectoryName(Pfad), ""), "Dateien")
        End Get
    End Property

    Public Function DateiPfad(d As Datei) As String
        Return Path.Combine(DateienOrdner, d.Pfad)
    End Function

    Public Function DateienLaden() As List(Of Datei)
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
    Public Function DateiImportieren(quelle As String) As Datei
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

    Public Sub DateiNotizSpeichern(id As Long, notiz As String)
        Using c = Oeffnen()
            Befehl(c, Nothing, "UPDATE Datei SET Notiz = $notiz WHERE Id = $id", "$notiz", If(notiz, ""), "$id", id).ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>Entfernt den Eintrag und die Datei im Dateien-Ordner. Drucke behalten ihre Daten, verlieren nur die Verknüpfung.</summary>
    Public Sub DateiLoeschen(d As Datei)
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
