Imports System.Threading

''' <summary>
''' Drucker im Server-Betrieb: Der Server überwacht die Drucker, die App fragt alle paar Sekunden den Stand ab.
''' Die Abfragen laufen im Hintergrund; Ereignisse kommen im Thread, der <see cref="Starten"/> aufgerufen hat (UI-Thread).
''' </summary>
Public Class ServerDruckerDienst
    Implements IDruckerDienst

    Private Const IntervallMs As Integer = 5000

    Private ReadOnly _server As ServerDatenquelle
    Private ReadOnly _abbruch As New CancellationTokenSource()
    Private _live As New Dictionary(Of Long, DruckerLive)
    ''' <summary>Druckenden, die schon gemeldet wurden (damit jedes nur einmal erscheint).</summary>
    Private ReadOnly _gemeldet As New HashSet(Of Long)
    Private _laeuft As Boolean

    Public Event StatusGeaendert(druckerId As Long) Implements IDruckerDienst.StatusGeaendert
    Public Event DruckBeendet(ende As DruckEnde) Implements IDruckerDienst.DruckBeendet
    Public Event DruckEndeEntfernt(endeId As Long) Implements IDruckerDienst.DruckEndeEntfernt

    Public Sub New(server As ServerDatenquelle)
        _server = server
    End Sub

    Public Sub Starten() Implements IDruckerDienst.Starten
        Takt()
    End Sub

    Private Async Sub Takt()
        Await AktualisierenAsync()
        Using t As New PeriodicTimer(TimeSpan.FromMilliseconds(IntervallMs))
            Try
                While Await t.WaitForNextTickAsync(_abbruch.Token)
                    Await AktualisierenAsync()
                End While
            Catch ex As OperationCanceledException
                ' Programm wird beendet
            End Try
        End Using
    End Sub

    ''' <summary>Holt Status und offene Druckenden vom Server (im Hintergrund) und meldet Änderungen.</summary>
    Private Async Function AktualisierenAsync() As Task
        If _laeuft OrElse _abbruch.IsCancellationRequested Then Return
        _laeuft = True
        Try
            Dim live As List(Of DruckerLive)
            Dim enden As List(Of DruckEnde)
            Try
                live = Await Task.Run(Function() _server.DruckerLiveLaden())
                enden = Await Task.Run(Function() _server.DruckEndenLaden())
            Catch ex As Exception
                If _abbruch.IsCancellationRequested Then Return
                ' Server nicht erreichbar: nicht den alten Stand weiter als "druckt" anzeigen
                Dim meldung = "Keine Verbindung zum Server: " & ex.Message
                For Each l In _live.Values
                    l.Status = New DruckerStatus With {.Zustand = DruckerZustand.Offline, .Meldung = meldung}
                Next
                For Each id In _live.Keys.ToList()
                    RaiseEvent StatusGeaendert(id)
                Next
                Return
            End Try
            If _abbruch.IsCancellationRequested Then Return

            Dim alt = _live
            _live = live.ToDictionary(Function(l) l.DruckerId)
            For Each id In _live.Keys.Union(alt.Keys).ToList()
                RaiseEvent StatusGeaendert(id)
            Next

            Dim offen = enden.Select(Function(e) e.Id).ToHashSet()
            For Each id In _gemeldet.Where(Function(x) Not offen.Contains(x)).ToList()
                _gemeldet.Remove(id)
                RaiseEvent DruckEndeEntfernt(id)
            Next
            For Each ende In enden.Where(Function(e) Not _gemeldet.Contains(e.Id))
                _gemeldet.Add(ende.Id)
                RaiseEvent DruckBeendet(ende)
            Next
        Finally
            _laeuft = False
        End Try
    End Function

    ''' <summary>Der Server übernimmt geänderte Drucker selbst – hier nur gleich neu abfragen.</summary>
    Public Async Sub Neuladen() Implements IDruckerDienst.Neuladen
        Await AktualisierenAsync()
    End Sub

    Public Function Status(druckerId As Long) As DruckerStatus Implements IDruckerDienst.Status
        Dim l As DruckerLive = Nothing
        Return If(_live.TryGetValue(druckerId, l), l.Status, Nothing)
    End Function

    Public Function ProtokollText(druckerId As Long) As String Implements IDruckerDienst.ProtokollText
        Try
            Return If(_server.DruckerProtokollText(druckerId), "")
        Catch ex As Exception
            Return "Das Protokoll konnte nicht vom Server geladen werden: " & ex.Message
        End Try
    End Function

    Public Function DruckbareEndungen(druckerId As Long) As String() Implements IDruckerDienst.DruckbareEndungen
        Dim l As DruckerLive = Nothing
        Return If(_live.TryGetValue(druckerId, l), l.Endungen, Array.Empty(Of String)())
    End Function

    Public ReadOnly Property AnzahlDruckend As Integer Implements IDruckerDienst.AnzahlDruckend
        Get
            Return _live.Values.Where(Function(l) l.Status IsNot Nothing AndAlso l.Status.DruckLaeuft).Count()
        End Get
    End Property

    ''' <summary>
    ''' Der Server sendet die Datei aus "Dateien" an den Drucker (dateiPfad wird nicht gebraucht); der Fortschritt
    ''' wird jede Sekunde abgefragt. Abbrechen bricht auch auf dem Server ab.
    ''' </summary>
    Public Async Function SendenAsync(druckerId As Long, dateiPfad As String, dateiId As Long, optionen As SendeOptionen,
                                      fortschritt As IProgress(Of Double), abbruch As CancellationToken) As Task Implements IDruckerDienst.SendenAsync
        Dim auftrag = Await Task.Run(Function() _server.SendenStarten(druckerId, dateiId, optionen))
        Try
            Do
                Await Task.Delay(1000, abbruch)
                Dim info = Await Task.Run(Function() _server.SendeAuftrag(auftrag))
                fortschritt?.Report(info.Fortschritt)
                If info.Fertig Then
                    If info.Fehler.Length > 0 Then Throw New InvalidOperationException(info.Fehler)
                    Exit Do
                End If
            Loop
        Catch ex As OperationCanceledException
            Try
                _server.SendenAbbrechen(auftrag)
            Catch
            End Try
            Throw
        End Try
        Await AktualisierenAsync()
    End Function

    Public Sub DruckEndeErledigt(ende As DruckEnde) Implements IDruckerDienst.DruckEndeErledigt
        If ende.Id <= 0 Then Return
        Dim id = ende.Id
        Task.Run(Sub()
                     Try
                         _server.DruckEndeErledigt(id)
                     Catch
                         ' Schon an einem anderen PC erledigt oder Server gerade weg – verschwindet beim nächsten Abgleich
                     End Try
                 End Sub)
    End Sub

    ''' <summary>
    ''' Beansprucht das Druckende auf dem Server (wird dort entfernt). Es wird hier nicht mehr als "anderswo erledigt"
    ''' gemeldet, damit der Hinweis während des Buchens stehen bleibt.
    ''' </summary>
    Public Function DruckEndeUebernehmen(ende As DruckEnde) As Boolean Implements IDruckerDienst.DruckEndeUebernehmen
        If ende.Id <= 0 Then Return True
        If Not _server.DruckEndeUebernehmen(ende.Id) Then Return False
        _gemeldet.Remove(ende.Id)
        Return True
    End Function

    ''' <summary>Buchen abgebrochen: wieder auf den Server legen (neue Nummer, hier schon als gemeldet vermerkt).</summary>
    Public Sub DruckEndeZuruecklegen(ende As DruckEnde) Implements IDruckerDienst.DruckEndeZuruecklegen
        If ende.Id <= 0 Then Return
        ende.Id = _server.DruckEndeZuruecklegen(ende)
        _gemeldet.Add(ende.Id)
    End Sub

    Public Function TestenAsync(d As Drucker, abbruch As CancellationToken) As Task(Of DruckerStatus) Implements IDruckerDienst.TestenAsync
        Return Task.Run(Function() _server.DruckerTesten(d), abbruch)
    End Function

    Public Function KameraErstellen(d As Drucker) As IKamera Implements IDruckerDienst.KameraErstellen
        Return New ServerKamera(_server, d.Id)
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _abbruch.Cancel()
    End Sub

End Class

''' <summary>Kamerabild über den Server: ruft etwa einmal pro Sekunde das neueste Bild ab.</summary>
Public Class ServerKamera
    Implements IKamera

    Private ReadOnly _server As ServerDatenquelle
    Private ReadOnly _druckerId As Long
    Private _abbruch As CancellationTokenSource

    Public Event BildEmpfangen(jpeg As Byte()) Implements IKamera.BildEmpfangen
    Public Event StatusGeaendert(text As String) Implements IKamera.StatusGeaendert

    Public Sub New(server As ServerDatenquelle, druckerId As Long)
        _server = server
        _druckerId = druckerId
    End Sub

    Public Sub Starten() Implements IKamera.Starten
        If _abbruch IsNot Nothing Then Return
        _abbruch = New CancellationTokenSource()
        Dim token = _abbruch.Token
        Task.Run(Function() SchleifeAsync(token))
    End Sub

    Public Sub Stoppen() Implements IKamera.Stoppen
        _abbruch?.Cancel()
        _abbruch = Nothing
    End Sub

    Private Async Function SchleifeAsync(abbruch As CancellationToken) As Task
        RaiseEvent StatusGeaendert("Verbinde mit Kamera über den Server …")
        Dim letzteLaenge = -1
        While Not abbruch.IsCancellationRequested
            Dim pause = 1000
            Try
                Dim ergebnis = _server.KameraBild(_druckerId)
                If ergebnis.Bild IsNot Nothing Then
                    ' Gleiches Bild (Server hat noch kein neues) nicht nochmal anzeigen
                    If ergebnis.Bild.Length <> letzteLaenge Then RaiseEvent BildEmpfangen(ergebnis.Bild)
                    letzteLaenge = ergebnis.Bild.Length
                Else
                    RaiseEvent StatusGeaendert(ergebnis.Status)
                End If
            Catch ex As Exception
                If abbruch.IsCancellationRequested Then Return
                RaiseEvent StatusGeaendert(ex.Message)
                pause = 3000
            End Try
            Try
                Await Task.Delay(pause, abbruch)
            Catch ex As OperationCanceledException
                Return
            End Try
        End While
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Stoppen()
    End Sub

End Class
