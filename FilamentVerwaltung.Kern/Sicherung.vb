Imports System.Globalization
Imports System.IO
Imports System.Text.RegularExpressions

''' <summary>Eine Sicherungsdatei im Backup-Ordner.</summary>
Public Class SicherungInfo

    Public Const ArtAutomatisch As String = "Automatisch"
    Public Const ArtManuell As String = "Manuell"
    Public Const ArtVorWiederherstellung As String = "Vor Wiederherstellung"

    Public Property Pfad As String = ""
    Public Property Zeitpunkt As Date
    Public Property Groesse As Long
    Public Property Art As String = ArtAutomatisch

    Public ReadOnly Property Name As String
        Get
            Return Path.GetFileName(Pfad)
        End Get
    End Property

End Class

''' <summary>
''' Sicherungen der Datenbank im Ordner "Backups" neben der Datenbankdatei.
''' Automatisch einmal pro Tag beim Start; die letzten <see cref="AnzahlAutomatisch"/> automatischen
''' Sicherungen bleiben erhalten, manuelle Sicherungen werden nie automatisch gelöscht.
''' Der Ordner "Dateien" wird nicht gesichert (die Dateien darin ändern sich nicht).
''' </summary>
Public Module Sicherung

    Public Const AnzahlAutomatisch As Integer = 10
    Private Const Zeitformat As String = "yyyy-MM-dd_HHmmss"
    Private Const ZusatzManuell As String = "manuell"
    Private Const ZusatzVorWiederherstellung As String = "vor-wiederherstellung"

    Public Function Ordner(db As Datenbank) As String
        Return Path.Combine(If(Path.GetDirectoryName(db.Pfad), ""), "Backups")
    End Function

    Private Function Basisname(db As Datenbank) As String
        Return Path.GetFileNameWithoutExtension(db.Pfad)
    End Function

    ''' <summary>Legt eine Sicherung an. art: SicherungInfo.ArtAutomatisch / ArtManuell / ArtVorWiederherstellung.</summary>
    Public Function Erstellen(db As Datenbank, art As String) As SicherungInfo
        Directory.CreateDirectory(Ordner(db))
        Dim zeitpunkt = Date.Now
        Dim zusatz = If(art = SicherungInfo.ArtManuell, "_" & ZusatzManuell,
                        If(art = SicherungInfo.ArtVorWiederherstellung, "_" & ZusatzVorWiederherstellung, ""))
        Dim ziel = Path.Combine(Ordner(db), $"{Basisname(db)}_{zeitpunkt.ToString(Zeitformat, CultureInfo.InvariantCulture)}{zusatz}.db")
        db.SichernNach(ziel)
        Return New SicherungInfo With {.Pfad = ziel, .Zeitpunkt = zeitpunkt, .Groesse = New FileInfo(ziel).Length, .Art = art}
    End Function

    ''' <summary>Sichert, falls heute noch keine automatische Sicherung existiert, und räumt alte auf.</summary>
    Public Function AutomatischSichern(db As Datenbank) As SicherungInfo
        If Liste(db).Any(Function(s) s.Art = SicherungInfo.ArtAutomatisch AndAlso s.Zeitpunkt.Date = Date.Today) Then Return Nothing
        Dim neu = Erstellen(db, SicherungInfo.ArtAutomatisch)
        For Each alt In Liste(db).Where(Function(s) s.Art = SicherungInfo.ArtAutomatisch).Skip(AnzahlAutomatisch)
            Try
                File.Delete(alt.Pfad)
            Catch ex As Exception
                ' Gesperrte Datei: beim nächsten Mal erneut versuchen
            End Try
        Next
        Return neu
    End Function

    ''' <summary>Alle Sicherungen, neueste zuerst.</summary>
    Public Function Liste(db As Datenbank) As List(Of SicherungInfo)
        Dim ergebnis As New List(Of SicherungInfo)
        Dim ordnerPfad = Ordner(db)
        If Not Directory.Exists(ordnerPfad) Then Return ergebnis
        Dim muster As New Regex("^" & Regex.Escape(Basisname(db)) & "_(\d{4}-\d{2}-\d{2}_\d{6})(?:_(" & ZusatzManuell & "|" &
                                ZusatzVorWiederherstellung & "))?\.db$", RegexOptions.IgnoreCase)
        For Each datei In Directory.EnumerateFiles(ordnerPfad, "*.db")
            Dim m = muster.Match(Path.GetFileName(datei))
            If Not m.Success Then Continue For
            Dim zeitpunkt As Date
            If Not Date.TryParseExact(m.Groups(1).Value, Zeitformat, CultureInfo.InvariantCulture, DateTimeStyles.None, zeitpunkt) Then Continue For
            Dim zusatz = m.Groups(2).Value.ToLowerInvariant()
            ergebnis.Add(New SicherungInfo With {
                .Pfad = datei,
                .Zeitpunkt = zeitpunkt,
                .Groesse = New FileInfo(datei).Length,
                .Art = If(zusatz = ZusatzManuell, SicherungInfo.ArtManuell,
                          If(zusatz = ZusatzVorWiederherstellung, SicherungInfo.ArtVorWiederherstellung, SicherungInfo.ArtAutomatisch))
            })
        Next
        Return ergebnis.OrderByDescending(Function(s) s.Zeitpunkt).ToList()
    End Function

    ''' <summary>
    ''' Stellt eine Sicherung wieder her. Vorher wird der aktuelle Stand als "Vor Wiederherstellung" gesichert,
    ''' damit der Schritt rückgängig gemacht werden kann.
    ''' </summary>
    Public Sub Wiederherstellen(db As Datenbank, s As SicherungInfo)
        Dim grund As String = ""
        If Not Datenbank.IstGueltigeSicherung(s.Pfad, grund) Then
            Throw New InvalidDataException("Die Sicherung kann nicht verwendet werden: " & grund)
        End If
        Erstellen(db, SicherungInfo.ArtVorWiederherstellung)
        db.WiederherstellenAus(s.Pfad)
    End Sub

End Module
