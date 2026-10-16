Imports System.IO
Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.Extensions.Logging

''' <summary>Ein Eintrag im Protokoll des Servers, wie ihn die Status-Seite zeigt.</summary>
Public Class ProtokollEintrag
    Public Property Zeit As Date
    ''' <summary>"Info", "Warnung" oder "Fehler".</summary>
    Public Property Stufe As String = ""
    Public Property Quelle As String = ""
    Public Property Text As String = ""
End Class

''' <summary>
''' Merkt sich die letzten Protokoll-Einträge im Speicher: Warnungen und Fehler aus allen Bereichen, dazu die
''' Meldungen der Filament-Verwaltung selbst (Druck beendet, gebucht, Sicherung …). Für die Status-Seite – so muss
''' man bei Problemen nicht ins Container-Log.
''' </summary>
Public Class ServerProtokoll
    Implements ILoggerProvider

    Private Const MaxEintraege As Integer = 200
    Private ReadOnly _eintraege As New LinkedList(Of ProtokollEintrag)

    Public Function CreateLogger(categoryName As String) As ILogger Implements ILoggerProvider.CreateLogger
        Return New Schreiber(Me, categoryName)
    End Function

    Public Function Eintraege() As List(Of ProtokollEintrag)
        SyncLock _eintraege
            Return _eintraege.Reverse().ToList()
        End SyncLock
    End Function

    Private Sub Merken(e As ProtokollEintrag)
        SyncLock _eintraege
            _eintraege.AddLast(e)
            While _eintraege.Count > MaxEintraege
                _eintraege.RemoveFirst()
            End While
        End SyncLock
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
    End Sub

    Private Class Schreiber
        Implements ILogger

        Private ReadOnly _protokoll As ServerProtokoll
        Private ReadOnly _quelle As String
        Private ReadOnly _eigene As Boolean

        Public Sub New(protokoll As ServerProtokoll, quelle As String)
            _protokoll = protokoll
            _eigene = quelle.StartsWith("FilamentVerwaltung", StringComparison.Ordinal)
            ' "FilamentVerwaltung.DruckerStart" → "DruckerStart", "Microsoft.AspNetCore.Server.Kestrel" bleibt
            _quelle = If(_eigene, quelle.Substring(quelle.LastIndexOf("."c) + 1), quelle)
        End Sub

        Public Function IsEnabled(logLevel As LogLevel) As Boolean Implements ILogger.IsEnabled
            Return logLevel >= LogLevel.Warning OrElse (_eigene AndAlso logLevel >= LogLevel.Information)
        End Function

        Public Sub Log(Of TState)(logLevel As LogLevel, eventId As EventId, state As TState, exception As Exception,
                                  formatter As Func(Of TState, Exception, String)) Implements ILogger.Log
            If Not IsEnabled(logLevel) Then Return
            Dim text = formatter(state, exception)
            If exception IsNot Nothing AndAlso Not text.Contains(exception.Message) Then text &= " – " & exception.Message
            _protokoll.Merken(New ProtokollEintrag With {
                .Zeit = Date.Now,
                .Stufe = If(logLevel >= LogLevel.Error, "Fehler", If(logLevel = LogLevel.Warning, "Warnung", "Info")),
                .Quelle = _quelle,
                .Text = text})
        End Sub

        Public Function BeginScope(Of TState)(state As TState) As IDisposable Implements ILogger.BeginScope
            Return Nothing
        End Function
    End Class
End Class

Public Class SpeicherBelegung
    Public Property Name As String = ""
    Public Property Bytes As Long
End Class

Public Class DruckerVerbindung
    Public Property Name As String = ""
    Public Property Typ As String = ""
    Public Property Aktiv As Boolean
    Public Property Zustand As String = ""
    Public Property Erreichbar As Boolean
    Public Property Meldung As String = ""
End Class

Public Class ServerZustand
    Public Property Version As String = ""
    Public Property Gestartet As Date
    Public Property LaufzeitSekunden As Long
    Public Property DatenOrdner As String = ""
    Public Property Speicher As New List(Of SpeicherBelegung)
    Public Property FreiBytes As Long?
    Public Property LetzteSicherung As Date?
    Public Property Sicherungen As Integer
    Public Property Drucker As New List(Of DruckerVerbindung)
    Public Property PushGeraete As Integer
    Public Property HomeAssistant As HaStatus
    Public Property AutomatischBuchen As Boolean
    Public Property OffeneDrucke As Integer
    Public Property Protokoll As New List(Of ProtokollEintrag)
End Class

''' <summary>Status-Seite: Version, Laufzeit, Speicher, Sicherung, Drucker, Push, Home Assistant und die letzten Meldungen.</summary>
<ApiController>
<Route("api/status")>
Public Class StatusController
    Inherits ControllerBase

    ''' <summary>Start des Servers (Prozess), nicht der ersten Abfrage.</summary>
    Private Shared ReadOnly Start As Date = System.Diagnostics.Process.GetCurrentProcess().StartTime

    Private ReadOnly _db As Datenbank
    Private ReadOnly _optionen As ServerOptionen
    Private ReadOnly _ueberwachung As DruckerUeberwachung
    Private ReadOnly _ha As HomeAssistantDienst
    Private ReadOnly _protokoll As ServerProtokoll

    Public Sub New(db As Datenbank, optionen As ServerOptionen, ueberwachung As DruckerUeberwachung, ha As HomeAssistantDienst, protokoll As ServerProtokoll)
        _db = db
        _optionen = optionen
        _ueberwachung = ueberwachung
        _ha = ha
        _protokoll = protokoll
    End Sub

    <HttpGet>
    Public Function Laden() As ServerZustand
        Dim z As New ServerZustand With {
            .Version = ServerVersion(),
            .Gestartet = Start,
            .LaufzeitSekunden = CLng((Date.Now - Start).TotalSeconds),
            .DatenOrdner = _optionen.DatenOrdner,
            .HomeAssistant = _ha.Status,
            .AutomatischBuchen = _db.EinstellungLesen(BuchenEinstellungen.Automatisch) = "1",
            .Protokoll = _protokoll.Eintraege()
        }

        ' Speicher: Datenbank und jeder Unterordner des Datenordners
        Try
            Dim ordner As New DirectoryInfo(_optionen.DatenOrdner)
            Dim datenbank = ordner.GetFiles("filament.db*").Sum(Function(f) f.Length)
            z.Speicher.Add(New SpeicherBelegung With {.Name = "Datenbank", .Bytes = datenbank})
            For Each u In ordner.GetDirectories().OrderBy(Function(d) d.Name)
                z.Speicher.Add(New SpeicherBelegung With {.Name = u.Name, .Bytes = OrdnerGroesse(u)})
            Next
            Dim rest = ordner.GetFiles().Where(Function(f) Not f.Name.StartsWith("filament.db", StringComparison.OrdinalIgnoreCase)).Sum(Function(f) f.Length)
            If rest > 0 Then z.Speicher.Add(New SpeicherBelegung With {.Name = "Sonstiges", .Bytes = rest})
            z.FreiBytes = New DriveInfo(ordner.FullName).AvailableFreeSpace
        Catch ex As Exception
            ' Speicher unbekannt – Rest trotzdem zeigen
        End Try

        Try
            Dim sicherungen = _db.SicherungenLaden()
            z.Sicherungen = sicherungen.Count
            If sicherungen.Count > 0 Then z.LetzteSicherung = sicherungen.Max(Function(s) s.Zeitpunkt)
        Catch ex As Exception
        End Try

        For Each d In _db.DruckerLaden()
            Dim s = If(d.Aktiv, _ueberwachung.Status(d.Id), Nothing)
            z.Drucker.Add(New DruckerVerbindung With {
                .Name = d.Name, .Typ = d.TypName, .Aktiv = d.Aktiv,
                .Zustand = If(Not d.Aktiv, "Abgeschaltet", If(s Is Nothing, "Unbekannt", DruckerStatus.ZustandText(s.Zustand))),
                .Erreichbar = s IsNot Nothing AndAlso s.Zustand <> DruckerZustand.Offline AndAlso s.Zustand <> DruckerZustand.Unbekannt,
                .Meldung = If(s?.Meldung, "")})
        Next
        z.PushGeraete = _db.PushGeraeteLaden().Count
        z.OffeneDrucke = _db.OffeneDruckEnden().Count
        Return z
    End Function

    Private Shared Function OrdnerGroesse(d As DirectoryInfo) As Long
        Try
            Return d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(Function(f) f.Length)
        Catch ex As Exception
            Return 0
        End Try
    End Function
End Class
