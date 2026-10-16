Imports System.Threading

''' <summary>
''' Fragt alle aktiven Drucker regelmäßig ab und meldet, wenn ein Druck endet.
''' Läuft in der App (lokaler Betrieb) und auf dem Server. Ereignisse kommen im Thread, der <see cref="Starten"/>
''' aufruft (in der App: UI-Thread; auf dem Server: Thread-Pool) – der Zustand ist deshalb mit einer Sperre geschützt.
''' </summary>
Public Class DruckerUeberwachung
    Implements IDruckerDienst

    Private Const IntervallMs As Integer = 10000

    Private Class Eintrag
        Public Drucker As Drucker
        Public Verbindung As DruckerVerbindung
        Public ReadOnly Protokoll As New DruckerProtokoll()
        Public Status As New DruckerStatus()
        Public LaeuftGerade As Boolean
        ' Laufender Druckauftrag
        Public JobAktiv As Boolean
        Public JobDatei As String = ""
        Public JobStart As Date
        Public MaxFortschritt As Double
        Public LetzterDruckStatus As DruckerStatus
        Public ReadOnly SlotZaehler As New Dictionary(Of Integer, Integer)
        ' Von der App gesendeter Auftrag
        Public GesendetDateiId As Long?
        Public GesendetUm As Date
        Public JobDateiId As Long?
    End Class

    Private ReadOnly _db As IDatenquelle
    Private ReadOnly _eintraege As New Dictionary(Of Long, Eintrag)
    Private ReadOnly _sperre As New Object()
    Private ReadOnly _abbruch As New CancellationTokenSource()

    Public Event StatusGeaendert(druckerId As Long) Implements IDruckerDienst.StatusGeaendert
    Public Event DruckBeendet(ende As DruckEnde) Implements IDruckerDienst.DruckBeendet
    ''' <summary>Lokal nie ausgelöst (es gibt nur diesen einen Empfänger).</summary>
    Public Event DruckEndeEntfernt(endeId As Long) Implements IDruckerDienst.DruckEndeEntfernt

    Public Sub New(db As IDatenquelle)
        _db = db
    End Sub

    Public Sub Starten() Implements IDruckerDienst.Starten
        Neuladen()
        Takt()
    End Sub

    ''' <summary>Fragt alle <see cref="IntervallMs"/> ab; Await kehrt in den Thread des Aufrufers zurück (wie ein Timer).</summary>
    Private Async Sub Takt()
        Using t As New PeriodicTimer(TimeSpan.FromMilliseconds(IntervallMs))
            Try
                While Await t.WaitForNextTickAsync(_abbruch.Token)
                    AlleAbfragen()
                End While
            Catch ex As OperationCanceledException
                ' Programm wird beendet
            End Try
        End Using
    End Sub

    ''' <summary>Drucker aus der Datenbank übernehmen (nach Hinzufügen / Bearbeiten / Löschen).</summary>
    Public Sub Neuladen() Implements IDruckerDienst.Neuladen
        Dim liste As List(Of Drucker)
        Try
            liste = _db.DruckerLaden()
        Catch ex As Exception
            Return
        End Try
        SyncLock _sperre
            ' Entfernte oder deaktivierte Drucker trennen
            For Each id In _eintraege.Keys.ToList()
                If Not liste.Any(Function(d) d.Id = id AndAlso d.Aktiv) Then
                    _eintraege(id).Verbindung?.Dispose()
                    _eintraege.Remove(id)
                End If
            Next
            For Each d In liste.Where(Function(x) x.Aktiv)
                Dim e As Eintrag = Nothing
                If _eintraege.TryGetValue(d.Id, e) Then
                    ' Geänderte Verbindungsdaten: neu verbinden
                    If e.Drucker.Typ <> d.Typ OrElse e.Drucker.Adresse <> d.Adresse OrElse e.Drucker.Zugang <> d.Zugang OrElse
                       e.Drucker.Benutzer <> d.Benutzer OrElse e.Drucker.Seriennummer <> d.Seriennummer Then
                        e.Verbindung?.Dispose()
                        e.Verbindung = DruckerVerbindung.Erstellen(d, e.Protokoll)
                        e.Status = New DruckerStatus()
                    End If
                    e.Drucker = d
                Else
                    e = New Eintrag With {.Drucker = d}
                    e.Verbindung = DruckerVerbindung.Erstellen(d, e.Protokoll)
                    _eintraege(d.Id) = e
                End If
            Next
        End SyncLock
        AlleAbfragen()
    End Sub

    Public Function Status(druckerId As Long) As DruckerStatus Implements IDruckerDienst.Status
        SyncLock _sperre
            Dim e As Eintrag = Nothing
            Return If(_eintraege.TryGetValue(druckerId, e), e.Status, Nothing)
        End SyncLock
    End Function

    ''' <summary>Ids der überwachten Drucker.</summary>
    Public Function DruckerIds() As List(Of Long)
        SyncLock _sperre
            Return _eintraege.Keys.ToList()
        End SyncLock
    End Function

    Public Function Protokoll(druckerId As Long) As DruckerProtokoll
        SyncLock _sperre
            Dim e As Eintrag = Nothing
            Return If(_eintraege.TryGetValue(druckerId, e), e.Protokoll, Nothing)
        End SyncLock
    End Function

    Public Function ProtokollText(druckerId As Long) As String Implements IDruckerDienst.ProtokollText
        Return If(Protokoll(druckerId)?.Alles(), "")
    End Function

    ''' <summary>Dateiendungen, die der Drucker zum Drucken annimmt (leer, wenn nicht überwacht).</summary>
    Public Function DruckbareEndungen(druckerId As Long) As String() Implements IDruckerDienst.DruckbareEndungen
        SyncLock _sperre
            Dim e As Eintrag = Nothing
            Return If(_eintraege.TryGetValue(druckerId, e), e.Verbindung.DruckbareEndungen, Array.Empty(Of String)())
        End SyncLock
    End Function

    ''' <summary>
    ''' Lädt eine Datei hoch und startet den Druck über die bestehende Verbindung. Die Datei wird gemerkt,
    ''' damit beim Druckende genau sie zum Buchen vorgeschlagen wird.
    ''' </summary>
    Public Async Function SendenAsync(druckerId As Long, dateiPfad As String, dateiId As Long, optionen As SendeOptionen,
                                      fortschritt As IProgress(Of Double), abbruch As CancellationToken) As Task Implements IDruckerDienst.SendenAsync
        Dim e As Eintrag = Nothing
        SyncLock _sperre
            If Not _eintraege.TryGetValue(druckerId, e) Then Throw New InvalidOperationException("Der Drucker wird nicht überwacht (in den Druckereinstellungen aktivieren).")
            e.GesendetDateiId = dateiId
            e.GesendetUm = Date.Now
        End SyncLock
        e.Protokoll.Schreiben($"Sende Auftrag: {IO.Path.GetFileName(dateiPfad)}")
        Try
            Await e.Verbindung.SendenAsync(dateiPfad, optionen, fortschritt, abbruch)
        Catch
            SyncLock _sperre
                e.GesendetDateiId = Nothing
            End SyncLock
            Throw
        End Try
        ' Gleich nachsehen, damit die Karte den Start zeigt
        AbfragenWennFrei(e)
    End Function

    ''' <summary>Anzahl Drucker, die gerade drucken.</summary>
    Public ReadOnly Property AnzahlDruckend As Integer Implements IDruckerDienst.AnzahlDruckend
        Get
            SyncLock _sperre
                Return _eintraege.Values.Where(Function(e) e.Status.DruckLaeuft).Count()
            End SyncLock
        End Get
    End Property

    ''' <summary>Lokal ist nichts zu tun – das Druckende wird nirgends gespeichert.</summary>
    Public Sub DruckEndeErledigt(ende As DruckEnde) Implements IDruckerDienst.DruckEndeErledigt
    End Sub

    ''' <summary>Lokal bucht nur dieser PC – das Druckende gehört ihm immer.</summary>
    Public Function DruckEndeUebernehmen(ende As DruckEnde) As Boolean Implements IDruckerDienst.DruckEndeUebernehmen
        Return True
    End Function

    Public Sub DruckEndeZuruecklegen(ende As DruckEnde) Implements IDruckerDienst.DruckEndeZuruecklegen
    End Sub

    ''' <summary>Fragt den Drucker einmal mit eigener Verbindung ab (für "Verbindung testen"); höchstens 20 Sekunden.</summary>
    Public Async Function TestenAsync(d As Drucker, abbruch As CancellationToken) As Task(Of DruckerStatus) Implements IDruckerDienst.TestenAsync
        Return Await DruckerTest.AbfragenAsync(d, abbruch)
    End Function

    Public Function KameraErstellen(d As Drucker) As IKamera Implements IDruckerDienst.KameraErstellen
        Return New BambuKamera(d, Protokoll(d.Id))
    End Function

    Private Sub AlleAbfragen()
        Dim liste As List(Of Eintrag)
        SyncLock _sperre
            liste = _eintraege.Values.ToList()
        End SyncLock
        For Each e In liste
            AbfragenWennFrei(e)
        Next
    End Sub

    Private Sub AbfragenWennFrei(e As Eintrag)
        SyncLock _sperre
            If e.LaeuftGerade Then Return
            e.LaeuftGerade = True
        End SyncLock
        Abfragen(e)
    End Sub

    Private Async Sub Abfragen(e As Eintrag)
        Dim neu As DruckerStatus
        Try
            Using zeitlimit = CancellationTokenSource.CreateLinkedTokenSource(_abbruch.Token)
                zeitlimit.CancelAfter(TimeSpan.FromSeconds(15))
                neu = Await e.Verbindung.AbfragenAsync(zeitlimit.Token)
            End Using
        Catch ex As Exception When Not _abbruch.IsCancellationRequested
            e.Protokoll.Schreiben("Fehler: " & ex.Message)
            neu = New DruckerStatus With {.Zustand = DruckerZustand.Offline, .Meldung = KurzeFehlermeldung(ex)}
        Catch ex As Exception
            Return  ' Programm wird beendet
        Finally
            SyncLock _sperre
                e.LaeuftGerade = False
            End SyncLock
        End Try
        Dim ende As DruckEnde
        SyncLock _sperre
            If Not _eintraege.ContainsValue(e) Then Return  ' inzwischen entfernt
            ende = Auswerten(e, neu)
        End SyncLock
        SpulenAbgleichen(e, neu)
        RaiseEvent StatusGeaendert(e.Drucker.Id)
        If ende IsNot Nothing Then RaiseEvent DruckBeendet(ende)
    End Sub

    ''' <summary>
    ''' Spulen im AMS: bekannte RFID-Spulen automatisch ihrem Fach zuordnen, die Kennung einer zugeordneten Spule lernen,
    ''' Zuordnungen sicher leerer Fächer entfernen und für die übrigen Fächer Vorschläge berechnen.
    ''' Die aktuelle Zuordnung und die Vorschläge kommen in den Status (für App und Handy).
    ''' </summary>
    Private Sub SpulenAbgleichen(e As Eintrag, s As DruckerStatus)
        If s.Zustand = DruckerZustand.Offline OrElse s.Zustand = DruckerZustand.Unbekannt Then Return
        Try
            Dim id = e.Drucker.Id
            Dim zuordnung = _db.SpulenZuordnung(id)
            If s.Faecher.Count > 0 Then
                Dim filamente = _db.AlleLaden()
                For Each fach In s.Faecher.Where(Function(f) f.Belegt AndAlso f.Kennung.Length > 0)
                    Dim aktuell As Long = 0
                    zuordnung.TryGetValue(fach.Slot, aktuell)
                    Dim bekannt = filamente.FirstOrDefault(Function(f) f.RfidKennung = fach.Kennung)
                    If bekannt IsNot Nothing Then
                        If aktuell = bekannt.Id Then Continue For
                        ' Bekannte Spule eingelegt oder umgesteckt: ins Fach eintragen, aus dem alten Fach entfernen
                        For Each alt In zuordnung.Where(Function(p) p.Value = bekannt.Id).Select(Function(p) p.Key).ToList()
                            _db.SpuleZuordnen(id, alt, Nothing)
                            zuordnung.Remove(alt)
                        Next
                        _db.SpuleZuordnen(id, fach.Slot, bekannt.Id)
                        zuordnung(fach.Slot) = bekannt.Id
                        e.Protokoll.Schreiben($"Spule {bekannt.Code} am RFID-Chip erkannt: {fach.Beschreibung}")
                    ElseIf aktuell <> 0 Then
                        ' Zugeordnete Spule hat noch keine Kennung: diese hier merken
                        Dim spule = filamente.FirstOrDefault(Function(f) f.Id = aktuell)
                        If spule IsNot Nothing AndAlso String.IsNullOrEmpty(spule.RfidKennung) Then
                            _db.RfidKennungSetzen(spule.Id, fach.Kennung)
                            spule.RfidKennung = fach.Kennung
                            e.Protokoll.Schreiben($"RFID-Chip von Spule {spule.Code} gelernt ({fach.Beschreibung})")
                        End If
                    End If
                Next
                ' Spule herausgenommen (der Drucker meldet das Fach ausdrücklich als leer)
                For Each fach In s.Faecher.Where(Function(f) f.LeerSicher AndAlso zuordnung.ContainsKey(f.Slot))
                    _db.SpuleZuordnen(id, fach.Slot, Nothing)
                    zuordnung.Remove(fach.Slot)
                Next
                s.Vorschlaege = Vorschlaege(s.Faecher, filamente, zuordnung)
            End If
            s.Zuordnung = zuordnung
        Catch ex As Exception
            e.Protokoll.Schreiben("Spulen-Abgleich: " & ex.Message)
        End Try
    End Sub

    Private Shared Function KurzeFehlermeldung(ex As Exception) As String
        Dim innen = ex
        While innen.InnerException IsNot Nothing AndAlso TypeOf innen Is Net.Http.HttpRequestException
            innen = innen.InnerException
        End While
        If TypeOf ex Is TaskCanceledException OrElse TypeOf ex Is OperationCanceledException Then Return "Keine Antwort (Zeitüberschreitung)"
        Return innen.Message
    End Function

    ''' <summary>Erkennt Beginn und Ende eines Druckauftrags; gibt beim Ende das <see cref="DruckEnde"/> zurück.</summary>
    Private Shared Function Auswerten(e As Eintrag, neu As DruckerStatus) As DruckEnde
        e.Status = neu
        If neu.Zustand = DruckerZustand.Offline OrElse neu.Zustand = DruckerZustand.Unbekannt Then Return Nothing ' kurzer Aussetzer beendet keinen Druck

        If neu.DruckLaeuft Then
            If Not e.JobAktiv OrElse (neu.Datei.Length > 0 AndAlso e.JobDatei.Length > 0 AndAlso neu.Datei <> e.JobDatei) Then
                ' Neuer Druckauftrag (beim Programmstart mitten im Druck: Startzeit aus der Druckdauer schätzen)
                e.JobAktiv = True
                e.JobDatei = neu.Datei
                e.JobStart = Date.Now.AddSeconds(-If(neu.DauerSekunden, 0))
                e.MaxFortschritt = 0
                e.SlotZaehler.Clear()
                ' Gerade von der App gesendet? Dann gehört die Datei zu diesem Druck
                e.JobDateiId = If(e.GesendetDateiId.HasValue AndAlso (Date.Now - e.GesendetUm).TotalMinutes < 30, e.GesendetDateiId, Nothing)
                e.GesendetDateiId = Nothing
                e.Protokoll.Schreiben($"Druck erkannt: {neu.Datei}" & If(e.JobDateiId.HasValue, " (von der App gesendet)", ""))
            End If
            If e.JobDatei.Length = 0 Then e.JobDatei = neu.Datei
            e.MaxFortschritt = Math.Max(e.MaxFortschritt, neu.Fortschritt)
            If neu.Slot.HasValue Then e.SlotZaehler(neu.Slot.Value) = If(e.SlotZaehler.ContainsKey(neu.Slot.Value), e.SlotZaehler(neu.Slot.Value), 0) + 1
            e.LetzterDruckStatus = neu
            Return Nothing
        End If

        If Not e.JobAktiv Then Return Nothing
        e.JobAktiv = False
        Dim letzter = If(e.LetzterDruckStatus, neu)
        Dim fortschritt = Math.Max(e.MaxFortschritt, If(neu.Zustand = DruckerZustand.Fertig, 1.0, neu.Fortschritt))
        Dim abgebrochen = neu.Zustand = DruckerZustand.Abgebrochen OrElse neu.Zustand = DruckerZustand.Fehler OrElse
                          (neu.Zustand = DruckerZustand.Bereit AndAlso fortschritt < 0.995)
        Dim ende As New DruckEnde With {
            .DruckerId = e.Drucker.Id,
            .DruckerName = e.Drucker.Name,
            .Datei = If(e.JobDatei.Length > 0, e.JobDatei, neu.Datei),
            .Abgebrochen = abgebrochen,
            .Fortschritt = If(abgebrochen, fortschritt, 1.0),
            .DauerSekunden = If(If(neu.DauerSekunden, letzter.DauerSekunden), CInt((Date.Now - e.JobStart).TotalSeconds)),
            .GrammGeplant = If(neu.GrammGeplant, letzter.GrammGeplant),
            .FilamentMmGeplant = If(neu.FilamentMmGeplant, letzter.FilamentMmGeplant),
            .FilamentMmVerbraucht = If(neu.FilamentMmVerbraucht, letzter.FilamentMmVerbraucht),
            .VolumenCm3 = If(neu.VolumenCm3, letzter.VolumenCm3),
            .Slot = If(e.SlotZaehler.Count > 0, e.SlotZaehler.OrderByDescending(Function(p) p.Value).First().Key, Drucker.SlotStandard),
            .DateiId = e.JobDateiId
        }
        e.JobDateiId = Nothing
        e.Protokoll.Schreiben($"Druck beendet: {ende.Datei} ({If(abgebrochen, $"abgebrochen bei {ende.Fortschritt:P0}", "fertig")})")
        Return ende
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _abbruch.Cancel()
        SyncLock _sperre
            For Each e In _eintraege.Values
                e.Verbindung?.Dispose()
            Next
            _eintraege.Clear()
        End SyncLock
    End Sub

End Class

''' <summary>Einmaliger Verbindungstest mit einem Drucker (App lokal und Server nutzen ihn).</summary>
Public Module DruckerTest

    Public Async Function AbfragenAsync(d As Drucker, abbruch As CancellationToken) As Task(Of DruckerStatus)
        Using verbindung = DruckerVerbindung.Erstellen(d, New DruckerProtokoll()),
              zeitlimit = CancellationTokenSource.CreateLinkedTokenSource(abbruch)
            zeitlimit.CancelAfter(TimeSpan.FromSeconds(20))
            Try
                Return Await verbindung.AbfragenAsync(zeitlimit.Token)
            Catch ex As OperationCanceledException When Not abbruch.IsCancellationRequested
                Throw New TimeoutException("Keine Antwort (Zeitüberschreitung). Adresse und Netzwerk prüfen.")
            End Try
        End Using
    End Function

End Module

''' <summary>Macht aus einem beendeten Druck einen Buchungsvorschlag für den Druck-Dialog.</summary>
Public Module DruckErkennung

    Public Function BuchungVorschlagen(ende As DruckEnde, filamente As List(Of Filament), dateien As List(Of Datei),
                                       zuordnung As Dictionary(Of Integer, Long)) As Buchung
        ' Spule: Fach des Drucks, sonst Standardfach, sonst die einzige Zuordnung
        Dim spuleId As Long = 0
        If Not zuordnung.TryGetValue(ende.Slot, spuleId) AndAlso Not zuordnung.TryGetValue(Drucker.SlotStandard, spuleId) AndAlso zuordnung.Count = 1 Then
            spuleId = zuordnung.Values.First()
        End If
        Dim spule = filamente.FirstOrDefault(Function(f) f.Id = spuleId)

        ' Datei: von der App gesendet (sicher), sonst gleicher Name ohne Endung
        Dim name = IO.Path.GetFileNameWithoutExtension(ende.Datei)
        If name.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase) Then name = IO.Path.GetFileNameWithoutExtension(name)
        Dim datei = If(ende.DateiId.HasValue, dateien.FirstOrDefault(Function(d) d.Id = ende.DateiId.Value), Nothing)
        If datei Is Nothing Then
            datei = dateien.Where(Function(d) String.Equals(IO.Path.GetFileNameWithoutExtension(d.Name).Replace(".gcode", ""), name, StringComparison.OrdinalIgnoreCase)).
                            OrderByDescending(Function(d) d.HatDruckdaten).ThenByDescending(Function(d) d.Hinzugefuegt).FirstOrDefault()
        ElseIf name.Length = 0 OrElse ende.Datei.Length = 0 Then
            name = IO.Path.GetFileNameWithoutExtension(datei.Name).Replace(".gcode", "")
        End If

        Dim anteil = If(ende.Abgebrochen, ende.Fortschritt, 1.0)
        Dim durchmesser = If(spule IsNot Nothing, CDbl(spule.Durchmesser), 1.75)
        Dim material = If(spule?.Material, "")
        Dim gramm As Double = 0
        Dim quelle As String
        If ende.FilamentMmVerbraucht.HasValue AndAlso ende.FilamentMmVerbraucht.Value > 0 Then
            ' Tatsächlich extrudiert (Klipper) – mit Slicer-Gewicht skalieren, sonst über Dichte umrechnen
            If ende.GrammGeplant.HasValue AndAlso ende.FilamentMmGeplant.HasValue AndAlso ende.FilamentMmGeplant.Value > 0 Then
                gramm = ende.GrammGeplant.Value * ende.FilamentMmVerbraucht.Value / ende.FilamentMmGeplant.Value
            Else
                gramm = GrammAusLaenge(ende.FilamentMmVerbraucht.Value, durchmesser, material)
            End If
            quelle = "tatsächlich extrudierte Länge"
        ElseIf ende.GrammGeplant.HasValue AndAlso ende.GrammGeplant.Value > 0 Then
            gramm = ende.GrammGeplant.Value * anteil
            quelle = "Slicer-Gewicht vom Drucker"
        ElseIf datei IsNot Nothing AndAlso datei.HatDruckdaten Then
            gramm = datei.Gramm.Value * anteil
            quelle = "Datei """ & datei.Name & """"
        ElseIf ende.VolumenCm3.HasValue AndAlso ende.VolumenCm3.Value > 0 Then
            gramm = GrammAusVolumen(ende.VolumenCm3.Value, material) * anteil
            quelle = "Filamentvolumen laut Drucker"
        ElseIf ende.FilamentMmGeplant.HasValue AndAlso ende.FilamentMmGeplant.Value > 0 Then
            gramm = GrammAusLaenge(ende.FilamentMmGeplant.Value, durchmesser, material) * anteil
            quelle = "Filamentlänge laut Drucker"
        Else
            quelle = "unbekannt – bitte Gewicht eintragen"
        End If

        Dim notiz = $"Erkannt am Drucker {ende.DruckerName}" &
                    If(ende.Abgebrochen, $", abgebrochen bei {ende.Fortschritt:P0}", "") &
                    $". Gewicht: {quelle}."
        Return New Buchung With {
            .FilamentId = If(spule?.Id, 0),
            .Datum = ende.Zeitpunkt,
            .Art = Buchung.ArtDruck,
            .Gramm = CInt(Math.Ceiling(gramm)),
            .Bezeichnung = If(name.Length > 0, name, "Druck"),
            .DateiId = datei?.Id,
            .Druckzeit = If(ende.DauerSekunden.HasValue AndAlso ende.DauerSekunden.Value > 0, ende.DauerSekunden, CType(Nothing, Integer?)),
            .Notiz = notiz,
            .Foto = If(ende.Foto, ""),
            .Drucker = If(ende.DruckerName, ""),
            .Fehldruck = ende.Abgebrochen
        }
    End Function

End Module
