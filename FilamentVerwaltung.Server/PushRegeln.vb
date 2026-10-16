Imports System.Collections.Concurrent
Imports Microsoft.Extensions.Logging

''' <summary>
''' Wann eine Benachrichtigung verschickt wird. Die Schalter liegen in den Einstellungen (für alle Geräte gleich):
''' Push.Druckende, Push.SpuleKnapp (+ Push.SpuleKnappGrenze in g), Push.DruckerOffline, Push.DruckerFehler.
''' </summary>
Public Class PushRegeln

    Public Const Druckende As String = PushEinstellungen.Druckende
    Public Const SpuleKnapp As String = PushEinstellungen.SpuleKnapp
    Public Const SpuleKnappGrenze As String = PushEinstellungen.SpuleKnappGrenze
    Public Const DruckerOffline As String = PushEinstellungen.DruckerOffline
    Public Const DruckerFehler As String = PushEinstellungen.DruckerFehler

    ''' <summary>So lange muss ein Drucker durchgehend nicht erreichbar sein, bevor eine Nachricht kommt.</summary>
    Private Shared ReadOnly OfflineNach As TimeSpan = TimeSpan.FromMinutes(3)

    Private Class DruckerLage
        Public Zustand As DruckerZustand = DruckerZustand.Unbekannt
        Public OfflineSeit As Date?
        Public OfflineGemeldet As Boolean
    End Class

    Private ReadOnly _db As Datenbank
    Private ReadOnly _push As PushDienst
    Private ReadOnly _log As ILogger(Of PushRegeln)
    Private ReadOnly _drucker As New ConcurrentDictionary(Of Long, DruckerLage)

    Public Sub New(db As Datenbank, push As PushDienst, log As ILogger(Of PushRegeln))
        _db = db
        _push = push
        _log = log
    End Sub

    ''' <summary>Schalter lesen; ohne Eintrag gilt der Standard (Druckende, Spule knapp und Fehler an, offline aus).</summary>
    Public Function Ein(schluessel As String) As Boolean
        Dim wert = _db.EinstellungLesen(schluessel)
        If String.IsNullOrEmpty(wert) Then Return PushEinstellungen.Standard(schluessel)
        Return wert = "1"
    End Function

    Private Function Grenze() As Integer
        Dim g As Integer
        Return If(Integer.TryParse(_db.EinstellungLesen(SpuleKnappGrenze), g) AndAlso g > 0, g, 100)
    End Function

    ''' <summary>Im Hintergrund an alle Geräte senden – wer auslöst, soll nicht warten.</summary>
    Private Sub Senden(n As PushNachricht)
        ' Für die Glocke in der App merken – auch wenn kein Handy angemeldet ist
        Try
            _db.BenachrichtigungSpeichern(New Benachrichtigung With {.Titel = n.Titel, .Text = n.Text, .Ziel = n.Ziel})
        Catch ex As Exception
            _log.LogWarning(ex, "Benachrichtigung konnte nicht gespeichert werden")
        End Try
        Task.Run(Async Function()
                     Try
                         Await _push.AnAlleAsync(n)
                     Catch ex As Exception
                         _log.LogWarning(ex, "Benachrichtigung fehlgeschlagen: {Titel}", n.Titel)
                     End Try
                 End Function)
    End Sub

    Public Sub DruckBeendet(ende As DruckEnde)
        If Not Ein(Druckende) Then Return
        Senden(New PushNachricht With {
            .Titel = If(ende.Abgebrochen, $"Druck abgebrochen – {ende.DruckerName}", $"Druck fertig – {ende.DruckerName}"),
            .Text = String.Join(" · ", {ende.Datei,
                                       If(ende.Abgebrochen, $"bei {ende.Fortschritt:P0}", ""),
                                       If(ende.DauerSekunden.GetValueOrDefault() > 0, DauerText(ende.DauerSekunden), "")}.Where(Function(x) x.Length > 0)) &
                    vbLf & "Antippen zum Buchen",
            .Ziel = "/#buchen",
            .Kennung = $"druckende-{ende.Id}"})
    End Sub

    ''' <summary>Druck wurde automatisch gebucht – statt "zum Buchen" kommt eine Bestätigung (Antippen öffnet den Verlauf).</summary>
    Public Sub AutomatischGebucht(ende As DruckEnde, b As Buchung)
        If Not Ein(Druckende) Then Return
        Dim f = _db.AlleLaden().FirstOrDefault(Function(x) x.Id = b.FilamentId)
        Dim spule = If(f Is Nothing, "", $" von {f.Code} ({String.Join(" ", {f.Hersteller, f.Material, f.Farbe}.Where(Function(x) Not String.IsNullOrWhiteSpace(x)))})")
        Senden(New PushNachricht With {
            .Titel = If(ende.Abgebrochen, $"Abgebrochen und gebucht – {ende.DruckerName}", $"Gebucht – {ende.DruckerName}"),
            .Text = $"{b.Bezeichnung}: {b.Gramm:N0} g{spule}" & vbLf & "Antippen zum Prüfen oder Korrigieren",
            .Ziel = "/#verlauf",
            .Kennung = $"druckende-{ende.Id}"})
    End Sub

    ''' <summary>Restgewicht einer Spule merken, bevor es sich ändert (für <see cref="SpuleGeaendert"/>).</summary>
    Public Function RestVorher(filamentId As Long) As Integer?
        Return _db.AlleLaden().FirstOrDefault(Function(f) f.Id = filamentId)?.Restgewicht
    End Function

    ''' <summary>Nach einer Buchung: Ist die Spule gerade unter die Grenze gefallen bzw. leer geworden?</summary>
    Public Sub SpuleGeaendert(filamentId As Long, vorher As Integer?)
        Try
            If Not vorher.HasValue OrElse Not Ein(SpuleKnapp) Then Return
            Dim f = _db.AlleLaden().FirstOrDefault(Function(x) x.Id = filamentId)
            If f Is Nothing Then Return
            Dim g = Grenze()
            Dim leer = f.Restgewicht <= 0 AndAlso vorher.Value > 0
            If Not leer AndAlso Not (vorher.Value >= g AndAlso f.Restgewicht < g) Then Return
            Dim sorte = String.Join(" ", {f.Hersteller, f.Material, f.Farbe}.Where(Function(x) Not String.IsNullOrWhiteSpace(x)))
            Senden(New PushNachricht With {
                .Titel = If(leer, $"Spule leer – {f.Code}", $"Spule fast leer – {f.Code}"),
                .Text = sorte & If(leer, "", $" · noch {f.Restgewicht:N0} g") & If(f.ShopLink.Length > 0, vbLf & "Nachkaufen über den Shop-Link", ""),
                .Ziel = "/#spule/" & Uri.EscapeDataString(f.Code),
                .Kennung = $"spule-{f.Id}"})
        Catch ex As Exception
            _log.LogWarning(ex, "Prüfung 'Spule fast leer' fehlgeschlagen")
        End Try
    End Sub

    ''' <summary>Bei jeder Abfrage eines Druckers: Fehler sofort melden, "nicht erreichbar" erst nach einigen Minuten.</summary>
    Public Sub StatusGeaendert(d As Drucker, s As DruckerStatus)
        If d Is Nothing OrElse s Is Nothing Then Return
        Dim lage = _drucker.GetOrAdd(d.Id, Function(x) New DruckerLage())
        Dim vorher = lage.Zustand
        lage.Zustand = s.Zustand
        Try
            If s.Zustand = DruckerZustand.Fehler AndAlso vorher <> DruckerZustand.Fehler AndAlso vorher <> DruckerZustand.Unbekannt AndAlso Ein(DruckerFehler) Then
                Senden(New PushNachricht With {
                    .Titel = $"Fehler am Drucker – {d.Name}",
                    .Text = If(String.IsNullOrWhiteSpace(s.Meldung), "Der Drucker meldet einen Fehler.", s.Meldung),
                    .Ziel = "/#drucker", .Kennung = $"drucker-{d.Id}"})
            End If
            If s.Zustand = DruckerZustand.Offline Then
                If Not lage.OfflineSeit.HasValue Then lage.OfflineSeit = Date.Now
                If Not lage.OfflineGemeldet AndAlso Date.Now - lage.OfflineSeit.Value >= OfflineNach AndAlso Ein(DruckerOffline) Then
                    lage.OfflineGemeldet = True
                    Senden(New PushNachricht With {
                        .Titel = $"Drucker nicht erreichbar – {d.Name}",
                        .Text = $"Seit {lage.OfflineSeit.Value:HH:mm} Uhr keine Verbindung." & If(String.IsNullOrWhiteSpace(s.Meldung), "", vbLf & s.Meldung),
                        .Ziel = "/#drucker", .Kennung = $"drucker-{d.Id}"})
                End If
            Else
                If lage.OfflineGemeldet AndAlso s.Zustand <> DruckerZustand.Unbekannt AndAlso Ein(DruckerOffline) Then
                    Senden(New PushNachricht With {.Titel = $"Drucker wieder erreichbar – {d.Name}", .Text = DruckerStatus.ZustandText(s.Zustand),
                                                   .Ziel = "/#drucker", .Kennung = $"drucker-{d.Id}"})
                End If
                lage.OfflineSeit = Nothing
                lage.OfflineGemeldet = False
            End If
        Catch ex As Exception
            _log.LogWarning(ex, "Prüfung des Druckerstatus für Benachrichtigungen fehlgeschlagen")
        End Try
    End Sub

End Class
