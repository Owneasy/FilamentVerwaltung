Imports System.Collections.Concurrent
Imports System.Threading
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging

''' <summary>
''' Startet die Drucker-Überwachung mit dem Server und merkt erkannte Druckenden in der Datenbank,
''' bis sie an einem PC gebucht oder verworfen werden.
''' </summary>
Public Class DruckerStart
    Implements IHostedService

    Private ReadOnly _ueberwachung As DruckerUeberwachung
    Private ReadOnly _kameras As KameraVerwaltung
    Private ReadOnly _db As Datenbank
    Private ReadOnly _regeln As PushRegeln
    Private ReadOnly _log As ILogger(Of DruckerStart)

    Public Sub New(ueberwachung As DruckerUeberwachung, kameras As KameraVerwaltung, db As Datenbank, regeln As PushRegeln, log As ILogger(Of DruckerStart))
        _ueberwachung = ueberwachung
        _kameras = kameras
        _db = db
        _regeln = regeln
        _log = log
    End Sub

    Public Function StartAsync(abbruch As CancellationToken) As Task Implements IHostedService.StartAsync
        AddHandler _ueberwachung.DruckBeendet, AddressOf DruckBeendet
        AddHandler _ueberwachung.StatusGeaendert, AddressOf StatusGeaendert
        _ueberwachung.Starten()
        _log.LogInformation("Drucker-Überwachung gestartet ({Anzahl} Drucker)", _ueberwachung.DruckerIds().Count)
        Return Task.CompletedTask
    End Function

    Private Sub DruckBeendet(ende As DruckEnde)
        Dim gebucht As Buchung = Nothing
        Try
            _db.DruckEndeSpeichern(ende)
            _log.LogInformation("Druck beendet auf {Drucker}: {Datei} ({Ergebnis})", ende.DruckerName, ende.Datei,
                                If(ende.Abgebrochen, "abgebrochen", "fertig"))
            gebucht = AutomatischBuchen(ende)
        Catch ex As Exception
            _log.LogWarning(ex, "Druckende konnte nicht gespeichert bzw. gebucht werden")
        End Try
        If gebucht IsNot Nothing Then _regeln.AutomatischGebucht(ende, gebucht) Else _regeln.DruckBeendet(ende)
        If ende.Id > 0 Then FotoAufnehmen(ende, gebucht?.Id)
    End Sub

    ''' <summary>
    ''' Einstellung "automatisch buchen": bucht den Druck sofort, wenn die Spule (Zuordnung zum Drucker bzw. AMS-Fach)
    ''' und das Gewicht feststehen. Sonst bleibt er offen zum Buchen. Gibt die Buchung zurück oder Nothing.
    ''' </summary>
    Private Function AutomatischBuchen(ende As DruckEnde) As Buchung
        If ende.Id <= 0 OrElse _db.EinstellungLesen(BuchenEinstellungen.Automatisch) <> "1" Then Return Nothing
        If ende.Abgebrochen AndAlso _db.EinstellungLesen(BuchenEinstellungen.AuchAbgebrochene) <> "1" Then Return Nothing
        Dim vorschlag = BuchungVorschlagen(ende, _db.AlleLaden(), _db.DateienLaden(), _db.SpulenZuordnung(ende.DruckerId))
        If vorschlag.FilamentId <= 0 OrElse vorschlag.Gramm <= 0 Then
            _log.LogInformation("Nicht automatisch gebucht ({Datei}): Spule oder Gewicht unbekannt", ende.Datei)
            Return Nothing
        End If
        ' Beanspruchen wie ein PC – wurde es inzwischen anders erledigt, nicht buchen
        If Not _db.DruckEndeErledigt(ende.Id) Then Return Nothing
        Dim vorher = _regeln.RestVorher(vorschlag.FilamentId)
        vorschlag.Notiz = (vorschlag.Notiz & " Automatisch gebucht.").Trim()
        Try
            _db.DruckSpeichern(vorschlag)
        Catch
            _db.DruckEndeSpeichern(ende)   ' Buchen fehlgeschlagen: wieder offen legen
            Throw
        End Try
        _regeln.SpuleGeaendert(vorschlag.FilamentId, vorher)
        _log.LogInformation("Automatisch gebucht: {Gramm} g ({Datei})", vorschlag.Gramm, ende.Datei)
        Return vorschlag
    End Function

    ''' <summary>
    ''' Kamerabild vom Druckende (Bambu) im Hintergrund holen und unter Fotos ablegen: an der automatischen Buchung
    ''' bzw. am offenen Druck (beim Buchen wandert es in die Buchung). Ohne Kamera oder Bild bleibt es einfach weg.
    ''' </summary>
    Private Sub FotoAufnehmen(ende As DruckEnde, buchungId As Long?)
        Dim d = _db.DruckerLaden().FirstOrDefault(Function(x) x.Id = ende.DruckerId)
        If d Is Nothing OrElse d.Typ <> Drucker.TypBambu Then Return
        Task.Run(Async Function()
                     Try
                         Dim bild = Await _kameras.FotoAsync(d, TimeSpan.FromSeconds(40))
                         If bild Is Nothing Then
                             _log.LogInformation("Kein Kamerabild vom Druckende ({Drucker})", d.Name)
                             Return
                         End If
                         IO.Directory.CreateDirectory(_db.FotosOrdner)
                         Dim name = $"druck-{Date.Now:yyyyMMdd-HHmmss}-{d.Id}.jpg"
                         Await IO.File.WriteAllBytesAsync(IO.Path.Combine(_db.FotosOrdner, name), bild)
                         If buchungId.HasValue Then
                             _db.BuchungFotoSetzen(buchungId.Value, name)
                         Else
                             ' Nur wenn der Druck noch offen ist – sonst wurde er inzwischen gebucht oder verworfen
                             Dim offen = _db.OffeneDruckEnden().FirstOrDefault(Function(x) x.Id = ende.Id)
                             If offen Is Nothing Then
                                 _db.FotoLoeschenWennUnbenutzt(name)
                                 Return
                             End If
                             offen.Foto = name
                             _db.DruckEndeAktualisieren(offen)
                         End If
                         _log.LogInformation("Foto vom Druckende gespeichert: {Name}", name)
                     Catch ex As Exception
                         _log.LogWarning(ex, "Foto vom Druckende fehlgeschlagen")
                     End Try
                 End Function)
    End Sub

    ''' <summary>Für die Benachrichtigungen "Fehler am Drucker" und "nicht erreichbar".</summary>
    Private Sub StatusGeaendert(druckerId As Long)
        Try
            _regeln.StatusGeaendert(_db.DruckerLaden().FirstOrDefault(Function(d) d.Id = druckerId), _ueberwachung.Status(druckerId))
        Catch ex As Exception
            _log.LogWarning(ex, "Statusprüfung für Benachrichtigungen fehlgeschlagen")
        End Try
    End Sub

    Public Function StopAsync(abbruch As CancellationToken) As Task Implements IHostedService.StopAsync
        _kameras.Dispose()
        _ueberwachung.Dispose()
        Return Task.CompletedTask
    End Function

End Class

''' <summary>
''' Kamerabilder der Bambu-Drucker: Eine Kamera läuft nur, solange jemand Bilder abruft
''' (nach 30 Sekunden ohne Abruf wird sie beendet).
''' </summary>
Public Class KameraVerwaltung
    Implements IDisposable

    Private Class Eintrag
        Public Kamera As BambuKamera
        Public Bild As Byte()
        Public Status As String = "Verbinde mit Kamera …"
        Public LetzteAnfrage As Date
    End Class

    Private ReadOnly _ueberwachung As DruckerUeberwachung
    Private ReadOnly _eintraege As New Dictionary(Of Long, Eintrag)
    Private ReadOnly _sperre As New Object()
    Private ReadOnly _aufraeumen As Timer

    Public Sub New(ueberwachung As DruckerUeberwachung)
        _ueberwachung = ueberwachung
        _aufraeumen = New Timer(Sub(x) Aufraeumen(), Nothing, 10000, 10000)
    End Sub

    ''' <summary>Neuestes Bild (oder Nothing) und Status; startet die Kamera bei Bedarf.</summary>
    Public Function Abrufen(d As Drucker) As (Bild As Byte(), Status As String)
        SyncLock _sperre
            Dim e As Eintrag = Nothing
            If Not _eintraege.TryGetValue(d.Id, e) Then
                e = New Eintrag()
                Dim eintrag = e
                e.Kamera = New BambuKamera(d, _ueberwachung.Protokoll(d.Id))
                AddHandler e.Kamera.BildEmpfangen, Sub(jpeg)
                                                       SyncLock _sperre
                                                           eintrag.Bild = jpeg
                                                           eintrag.Status = ""
                                                       End SyncLock
                                                   End Sub
                AddHandler e.Kamera.StatusGeaendert, Sub(text)
                                                         SyncLock _sperre
                                                             eintrag.Bild = Nothing
                                                             eintrag.Status = text
                                                         End SyncLock
                                                     End Sub
                _eintraege(d.Id) = e
                e.Kamera.Starten()
            End If
            e.LetzteAnfrage = Date.Now
            Return (e.Bild, e.Status)
        End SyncLock
    End Function

    ''' <summary>Ein aktuelles Bild holen (startet die Kamera bei Bedarf); Nothing, wenn innerhalb der Zeit keins kommt.</summary>
    Public Async Function FotoAsync(d As Drucker, zeitlimit As TimeSpan) As Task(Of Byte())
        Dim ende = Date.Now + zeitlimit
        ' Das erste Bild nach dem Start kann noch vom Aufbau der Verbindung stammen – ein, zwei Sekunden warten lohnt
        Dim ersteGesehen As Date? = Nothing
        While Date.Now < ende
            Dim ergebnis = Abrufen(d)
            If ergebnis.Bild IsNot Nothing Then
                If Not ersteGesehen.HasValue Then ersteGesehen = Date.Now
                If Date.Now - ersteGesehen.Value >= TimeSpan.FromSeconds(2) Then Return Abrufen(d).Bild
            End If
            Await Task.Delay(500)
        End While
        Return Nothing
    End Function
    Private Sub Aufraeumen()
        SyncLock _sperre
            For Each id In _eintraege.Keys.ToList()
                If (Date.Now - _eintraege(id).LetzteAnfrage).TotalSeconds > 30 Then
                    _eintraege(id).Kamera.Dispose()
                    _eintraege.Remove(id)
                End If
            Next
        End SyncLock
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _aufraeumen.Dispose()
        SyncLock _sperre
            For Each e In _eintraege.Values
                e.Kamera.Dispose()
            Next
            _eintraege.Clear()
        End SyncLock
    End Sub

End Class

''' <summary>Laufende Sendeaufträge (Upload zum Drucker), deren Fortschritt die App abfragt.</summary>
Public Class SendeAuftraege

    Private Class Auftrag
        Public ReadOnly Info As New SendeAuftragInfo()
        Public ReadOnly Abbruch As New CancellationTokenSource()
        Public Ende As Date?
    End Class

    Private ReadOnly _auftraege As New ConcurrentDictionary(Of String, Auftrag)

    Public Function Starten(ueberwachung As DruckerUeberwachung, druckerId As Long, pfad As String, dateiId As Long,
                            optionen As SendeOptionen) As SendeAuftragInfo
        ' Erledigte Aufträge nach 10 Minuten vergessen
        For Each alt In _auftraege.Where(Function(p) p.Value.Ende.HasValue AndAlso (Date.Now - p.Value.Ende.Value).TotalMinutes > 10).ToList()
            Dim weg As Auftrag = Nothing
            _auftraege.TryRemove(alt.Key, weg)
        Next

        Dim a As New Auftrag()
        a.Info.Id = Guid.NewGuid().ToString("N")
        _auftraege(a.Info.Id) = a
        Task.Run(Async Function()
                     Try
                         Await ueberwachung.SendenAsync(druckerId, pfad, dateiId, optionen,
                                                        New Progress(Of Double)(Sub(w) a.Info.Fortschritt = w), a.Abbruch.Token)
                         a.Info.Fortschritt = 1
                     Catch ex As OperationCanceledException
                         a.Info.Fehler = "Abgebrochen."
                     Catch ex As Exception
                         a.Info.Fehler = ex.Message
                     Finally
                         a.Ende = Date.Now
                         a.Info.Fertig = True
                     End Try
                 End Function)
        Return Kopie(a)
    End Function

    Public Function Abrufen(id As String) As SendeAuftragInfo
        Dim a As Auftrag = Nothing
        Return If(_auftraege.TryGetValue(id, a), Kopie(a), Nothing)
    End Function

    Public Function Abbrechen(id As String) As Boolean
        Dim a As Auftrag = Nothing
        If Not _auftraege.TryGetValue(id, a) Then Return False
        a.Abbruch.Cancel()
        Return True
    End Function

    Private Shared Function Kopie(a As Auftrag) As SendeAuftragInfo
        Return New SendeAuftragInfo With {.Id = a.Info.Id, .Fortschritt = a.Info.Fortschritt, .Fertig = a.Info.Fertig, .Fehler = a.Info.Fehler}
    End Function

End Class
