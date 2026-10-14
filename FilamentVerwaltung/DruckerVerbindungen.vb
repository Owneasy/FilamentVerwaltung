Imports System.Globalization
Imports System.Net
Imports System.Net.Http
Imports System.Text.Json
Imports System.Threading
Imports MQTTnet
Imports MQTTnet.Protocol

''' <summary>Die letzten Meldungen einer Druckerverbindung (für die Fehlersuche mit echten Geräten).</summary>
Public Class DruckerProtokoll
    Private Const MaxZeilen As Integer = 300
    Private ReadOnly _zeilen As New LinkedList(Of String)

    Public Sub Schreiben(text As String)
        SyncLock _zeilen
            _zeilen.AddLast($"{Date.Now:HH:mm:ss}  {text}")
            While _zeilen.Count > MaxZeilen
                _zeilen.RemoveFirst()
            End While
        End SyncLock
    End Sub

    Public Function Alles() As String
        SyncLock _zeilen
            Return String.Join(Environment.NewLine, _zeilen)
        End SyncLock
    End Function
End Class

''' <summary>
''' Verbindung zu einem Drucker. Jede Art liefert ihren Zustand als <see cref="DruckerStatus"/>.
''' </summary>
Public MustInherit Class DruckerVerbindung
    Implements IDisposable

    Protected ReadOnly Drucker As Drucker
    Protected ReadOnly Protokoll As DruckerProtokoll

    Protected Sub New(d As Drucker, p As DruckerProtokoll)
        Drucker = d
        Protokoll = p
    End Sub

    ''' <summary>Fragt den aktuellen Zustand ab (wirft bei Verbindungsfehlern).</summary>
    Public MustOverride Function AbfragenAsync(abbruch As CancellationToken) As Task(Of DruckerStatus)

    Public Shared Function Erstellen(d As Drucker, p As DruckerProtokoll) As DruckerVerbindung
        Select Case d.Typ
            Case Drucker.TypBambu : Return New BambuVerbindung(d, p)
            Case Drucker.TypPrusaLink : Return New PrusaLinkVerbindung(d, p)
            Case Drucker.TypOctoPrint : Return New OctoPrintVerbindung(d, p)
            Case Else : Return New MoonrakerVerbindung(d, p)
        End Select
    End Function

    ' ------------------------------------------------------------------ Hilfen

    ''' <summary>GET und JSON lesen. 204 = Nothing; 401/403 = verständliche Fehlermeldung.</summary>
    Protected Async Function JsonHolenAsync(http As HttpClient, pfad As String, abbruch As CancellationToken) As Task(Of JsonElement?)
        Using antwort = Await http.GetAsync(pfad, abbruch)
            Dim text = Await antwort.Content.ReadAsStringAsync(abbruch)
            Protokoll.Schreiben($"GET {pfad} → {CInt(antwort.StatusCode)} {Kuerzen(text)}")
            If antwort.StatusCode = HttpStatusCode.NoContent OrElse String.IsNullOrWhiteSpace(text) Then Return Nothing
            If antwort.StatusCode = HttpStatusCode.Unauthorized OrElse antwort.StatusCode = HttpStatusCode.Forbidden Then
                Throw New UnauthorizedAccessException("Zugriff verweigert – bitte API-Key, Benutzer bzw. Passwort prüfen.")
            End If
            If antwort.StatusCode = HttpStatusCode.NotFound Then Return Nothing
            antwort.EnsureSuccessStatusCode()
            Using doc = JsonDocument.Parse(text)
                Return doc.RootElement.Clone()
            End Using
        End Using
    End Function

    Protected Shared Function Kuerzen(text As String) As String
        Dim t = If(text, "").Replace(vbCr, " ").Replace(vbLf, " ")
        Return If(t.Length > 400, t.Substring(0, 400) & " …", t)
    End Function

    Protected Shared Function Kind(e As JsonElement?, ParamArray pfad As String()) As JsonElement?
        If Not e.HasValue Then Return Nothing
        Dim aktuell = e.Value
        For Each teil In pfad
            Dim naechstes As JsonElement
            If aktuell.ValueKind <> JsonValueKind.Object OrElse Not aktuell.TryGetProperty(teil, naechstes) Then Return Nothing
            aktuell = naechstes
        Next
        Return aktuell
    End Function

    Protected Shared Function Text(e As JsonElement?, ParamArray pfad As String()) As String
        Dim k = Kind(e, pfad)
        If Not k.HasValue Then Return ""
        Select Case k.Value.ValueKind
            Case JsonValueKind.String : Return If(k.Value.GetString(), "")
            Case JsonValueKind.Number : Return k.Value.GetRawText()
            Case Else : Return ""
        End Select
    End Function

    ''' <summary>Zahl lesen – auch wenn sie als Text kommt ("12.5").</summary>
    Protected Shared Function Zahl(e As JsonElement?, ParamArray pfad As String()) As Double?
        Dim k = Kind(e, pfad)
        If Not k.HasValue Then Return Nothing
        Dim d As Double
        If k.Value.ValueKind = JsonValueKind.Number AndAlso k.Value.TryGetDouble(d) Then Return d
        If k.Value.ValueKind = JsonValueKind.String AndAlso
           Double.TryParse(k.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, d) Then Return d
        Return Nothing
    End Function

    Protected Shared Function NeuerHttpClient(basis As String, Optional handler As HttpMessageHandler = Nothing) As HttpClient
        Dim c = If(handler Is Nothing, New HttpClient(), New HttpClient(handler, disposeHandler:=True))
        c.BaseAddress = New Uri(basis.TrimEnd("/"c) & "/")
        c.Timeout = TimeSpan.FromSeconds(8)
        c.DefaultRequestHeaders.UserAgent.ParseAdd("FilamentVerwaltung")
        Return c
    End Function

    Public Overridable Sub Dispose() Implements IDisposable.Dispose
    End Sub

End Class

''' <summary>OctoPrint: GET /api/job mit X-Api-Key.</summary>
Public Class OctoPrintVerbindung
    Inherits DruckerVerbindung

    Private ReadOnly _http As HttpClient

    Public Sub New(d As Drucker, p As DruckerProtokoll)
        MyBase.New(d, p)
        _http = NeuerHttpClient(d.BasisUrl())
        If d.Zugang.Length > 0 Then _http.DefaultRequestHeaders.Add("X-Api-Key", d.Zugang)
    End Sub

    Public Overrides Async Function AbfragenAsync(abbruch As CancellationToken) As Task(Of DruckerStatus)
        Dim job = Await JsonHolenAsync(_http, "api/job", abbruch)
        Dim s As New DruckerStatus()
        Dim zustand = Text(job, "state")
        Dim fortschritt = If(Zahl(job, "progress", "completion"), 0.0) / 100.0
        s.Fortschritt = Math.Max(0, Math.Min(1, fortschritt))
        s.Datei = If(Text(job, "job", "file", "display").Length > 0, Text(job, "job", "file", "display"), Text(job, "job", "file", "name"))
        Dim dauer = Zahl(job, "progress", "printTime"), rest = Zahl(job, "progress", "printTimeLeft")
        If dauer.HasValue Then s.DauerSekunden = CInt(dauer.Value)
        If rest.HasValue Then s.RestSekunden = CInt(rest.Value)

        ' Filament laut G-Code-Analyse (je Extruder)
        Dim filament = Kind(job, "job", "filament")
        If filament.HasValue AndAlso filament.Value.ValueKind = JsonValueKind.Object Then
            Dim laenge = 0.0, volumen = 0.0
            For Each werkzeug In filament.Value.EnumerateObject()
                laenge += If(Zahl(werkzeug.Value, "length"), 0.0)
                volumen += If(Zahl(werkzeug.Value, "volume"), 0.0)
            Next
            If laenge > 0 Then s.FilamentMmGeplant = laenge
            If volumen > 0 Then s.VolumenCm3 = volumen
        End If

        Dim z = zustand.ToLowerInvariant()
        If z.Contains("offline") Then
            s.Zustand = DruckerZustand.Offline
        ElseIf z.Contains("error") Then
            s.Zustand = DruckerZustand.Fehler
        ElseIf z.Contains("cancel") Then
            s.Zustand = DruckerZustand.Abgebrochen
        ElseIf z.Contains("paus") Then
            s.Zustand = DruckerZustand.Pausiert
        ElseIf z.Contains("printing") OrElse z.Contains("starting") Then
            s.Zustand = DruckerZustand.Druckt
        ElseIf z.Length > 0 Then
            s.Zustand = If(fortschritt >= 0.999 AndAlso s.Datei.Length > 0, DruckerZustand.Fertig, DruckerZustand.Bereit)
        End If
        s.Meldung = zustand
        Return s
    End Function

    Public Overrides Sub Dispose()
        _http.Dispose()
    End Sub
End Class

''' <summary>Klipper über Moonraker: print_stats/virtual_sdcard und Datei-Metadaten (Slicer-Gewicht).</summary>
Public Class MoonrakerVerbindung
    Inherits DruckerVerbindung

    Private ReadOnly _http As HttpClient
    Private ReadOnly _metadaten As New Dictionary(Of String, (Gramm As Double?, Mm As Double?, Sekunden As Double?))(StringComparer.OrdinalIgnoreCase)

    Public Sub New(d As Drucker, p As DruckerProtokoll)
        MyBase.New(d, p)
        _http = NeuerHttpClient(d.BasisUrl())
        If d.Zugang.Length > 0 Then _http.DefaultRequestHeaders.Add("X-Api-Key", d.Zugang)
    End Sub

    Public Overrides Async Function AbfragenAsync(abbruch As CancellationToken) As Task(Of DruckerStatus)
        Dim antwort = Await JsonHolenAsync(_http, "printer/objects/query?print_stats&virtual_sdcard", abbruch)
        Dim stats = Kind(antwort, "result", "status", "print_stats")
        Dim s As New DruckerStatus With {
            .Datei = Text(stats, "filename"),
            .Fortschritt = Math.Max(0, Math.Min(1, If(Zahl(antwort, "result", "status", "virtual_sdcard", "progress"), 0.0))),
            .Meldung = Text(stats, "message")
        }
        Dim dauer = Zahl(stats, "print_duration")
        If dauer.HasValue Then s.DauerSekunden = CInt(dauer.Value)
        Dim verbraucht = Zahl(stats, "filament_used")
        If verbraucht.HasValue AndAlso verbraucht.Value > 0 Then s.FilamentMmVerbraucht = verbraucht

        Select Case Text(stats, "state").ToLowerInvariant()
            Case "printing" : s.Zustand = DruckerZustand.Druckt
            Case "paused" : s.Zustand = DruckerZustand.Pausiert
            Case "complete" : s.Zustand = DruckerZustand.Fertig
            Case "cancelled" : s.Zustand = DruckerZustand.Abgebrochen
            Case "error" : s.Zustand = DruckerZustand.Fehler
            Case "standby" : s.Zustand = DruckerZustand.Bereit
            Case Else : s.Zustand = If(stats.HasValue, DruckerZustand.Bereit, DruckerZustand.Unbekannt)
        End Select

        If s.Datei.Length > 0 Then
            Dim meta = Await MetadatenAsync(s.Datei, abbruch)
            s.GrammGeplant = meta.Gramm
            s.FilamentMmGeplant = meta.Mm
            If meta.Sekunden.HasValue AndAlso s.DruckLaeuft Then
                s.RestSekunden = CInt(Math.Max(0, meta.Sekunden.Value - If(dauer, 0.0)))
            End If
        End If
        Return s
    End Function

    Private Async Function MetadatenAsync(datei As String, abbruch As CancellationToken) As Task(Of (Gramm As Double?, Mm As Double?, Sekunden As Double?))
        Dim gefunden As (Gramm As Double?, Mm As Double?, Sekunden As Double?)
        If _metadaten.TryGetValue(datei, gefunden) Then Return gefunden
        Dim meta = Await JsonHolenAsync(_http, "server/files/metadata?filename=" & Uri.EscapeDataString(datei), abbruch)
        gefunden = (Zahl(meta, "result", "filament_weight_total"), Zahl(meta, "result", "filament_total"), Zahl(meta, "result", "estimated_time"))
        _metadaten(datei) = gefunden
        Return gefunden
    End Function

    Public Overrides Sub Dispose()
        _http.Dispose()
    End Sub
End Class

''' <summary>Prusa über PrusaLink: /api/v1/status und /api/v1/job (Digest-Anmeldung oder API-Key).</summary>
Public Class PrusaLinkVerbindung
    Inherits DruckerVerbindung

    Private ReadOnly _http As HttpClient
    Private _jobId As String = ""
    Private _datei As String = ""
    Private _gramm As Double?
    Private _mm As Double?

    Public Sub New(d As Drucker, p As DruckerProtokoll)
        MyBase.New(d, p)
        If d.Benutzer.Length > 0 Then
            ' PrusaLink nutzt Digest-Anmeldung (Benutzer "maker", Passwort steht im Druckermenü)
            _http = NeuerHttpClient(d.BasisUrl(), New HttpClientHandler With {.Credentials = New NetworkCredential(d.Benutzer, d.Zugang)})
        Else
            _http = NeuerHttpClient(d.BasisUrl())
            If d.Zugang.Length > 0 Then _http.DefaultRequestHeaders.Add("X-Api-Key", d.Zugang)
        End If
    End Sub

    Public Overrides Async Function AbfragenAsync(abbruch As CancellationToken) As Task(Of DruckerStatus)
        Dim status = Await JsonHolenAsync(_http, "api/v1/status", abbruch)
        Dim s As New DruckerStatus With {
            .Fortschritt = Math.Max(0, Math.Min(1, If(Zahl(status, "job", "progress"), 0.0) / 100.0)),
            .Meldung = Text(status, "printer", "state")
        }
        Dim rest = Zahl(status, "job", "time_remaining"), dauer = Zahl(status, "job", "time_printing")
        If rest.HasValue Then s.RestSekunden = CInt(rest.Value)
        If dauer.HasValue Then s.DauerSekunden = CInt(dauer.Value)

        Select Case Text(status, "printer", "state").ToUpperInvariant()
            Case "PRINTING", "BUSY" : s.Zustand = DruckerZustand.Druckt
            Case "PAUSED", "ATTENTION" : s.Zustand = DruckerZustand.Pausiert
            Case "FINISHED" : s.Zustand = DruckerZustand.Fertig
            Case "STOPPED" : s.Zustand = DruckerZustand.Abgebrochen
            Case "ERROR" : s.Zustand = DruckerZustand.Fehler
            Case "" : s.Zustand = DruckerZustand.Unbekannt
            Case Else : s.Zustand = DruckerZustand.Bereit
        End Select

        ' Dateiname und Slicer-Werte einmal pro Druckauftrag holen
        Dim jobId = Text(status, "job", "id")
        If jobId.Length > 0 AndAlso jobId <> _jobId Then
            Dim job = Await JsonHolenAsync(_http, "api/v1/job", abbruch)
            _jobId = jobId
            _datei = If(Text(job, "file", "display_name").Length > 0, Text(job, "file", "display_name"), Text(job, "file", "name"))
            _gramm = Zahl(job, "file", "meta", "filament used [g]")
            _mm = Zahl(job, "file", "meta", "filament used [mm]")
        End If
        If jobId.Length > 0 Then
            s.Datei = _datei
            s.GrammGeplant = _gramm
            s.FilamentMmGeplant = _mm
        End If
        Return s
    End Function

    Public Overrides Sub Dispose()
        _http.Dispose()
    End Sub
End Class

''' <summary>
''' Bambu Lab im LAN: MQTT über TLS (Port 8883), Benutzer "bblp", Passwort = Zugangscode, Thema device/&lt;Seriennummer&gt;/report.
''' Der Drucker schickt laufend Teil-Updates; sie werden zu einem Gesamtzustand zusammengeführt.
''' </summary>
Public Class BambuVerbindung
    Inherits DruckerVerbindung

    Private ReadOnly _zustand As New Nodes.JsonObject()
    Private ReadOnly _sperre As New Object()
    Private _client As IMqttClient
    Private _letzterPushall As Date = Date.MinValue
    Private _datenErhalten As Boolean

    Public Sub New(d As Drucker, p As DruckerProtokoll)
        MyBase.New(d, p)
    End Sub

    Private ReadOnly Property Host As String
        Get
            Dim a = Drucker.Adresse.Trim()
            For Each praefix In {"http://", "https://", "mqtts://", "mqtt://"}
                If a.StartsWith(praefix, StringComparison.OrdinalIgnoreCase) Then a = a.Substring(praefix.Length)
            Next
            a = a.TrimEnd("/"c)
            Dim doppelpunkt = a.LastIndexOf(":"c)
            Return If(doppelpunkt > 0, a.Substring(0, doppelpunkt), a)
        End Get
    End Property

    Private ReadOnly Property Port As Integer
        Get
            Dim a = Drucker.Adresse.Trim().TrimEnd("/"c)
            Dim doppelpunkt = a.LastIndexOf(":"c)
            Dim p As Integer
            If doppelpunkt > 0 AndAlso Integer.TryParse(a.Substring(doppelpunkt + 1), p) Then Return p
            Return 8883
        End Get
    End Property

    Private Async Function VerbindenAsync(abbruch As CancellationToken) As Task
        If _client IsNot Nothing AndAlso _client.IsConnected Then Return
        _client?.Dispose()
        Dim fabrik As New MqttClientFactory()
        _client = fabrik.CreateMqttClient()
        AddHandler _client.ApplicationMessageReceivedAsync, Function(e)
                                                                 NachrichtVerarbeiten(e.ApplicationMessage.ConvertPayloadToString())
                                                                 Return Task.CompletedTask
                                                             End Function
        ' Der Drucker hat ein selbst ausgestelltes Zertifikat – im Heimnetz akzeptieren.
        ' Bambu-Drucker sprechen nur MQTT 3.1.1 (MQTTnet würde sonst MQTT 5 verwenden und abgelehnt werden).
        Dim optionen = New MqttClientOptionsBuilder().
            WithTcpServer(Host, Port).
            WithCredentials("bblp", Drucker.Zugang).
            WithClientId("filamentverwaltung-" & Guid.NewGuid().ToString("N").Substring(0, 8)).
            WithTlsOptions(Sub(o) o.UseTls().WithCertificateValidationHandler(Function(z) True)).
            WithTimeout(TimeSpan.FromSeconds(8)).
            WithKeepAlivePeriod(TimeSpan.FromSeconds(30)).
            WithCleanSession().
            WithProtocolVersion(Formatter.MqttProtocolVersion.V311).
            Build()
        Protokoll.Schreiben($"MQTT verbinde mit {Host}:{Port} …")
        Dim ergebnis = Await _client.ConnectAsync(optionen, abbruch)
        Protokoll.Schreiben($"MQTT Verbindungsergebnis: {ergebnis.ResultCode}")
        Select Case ergebnis.ResultCode
            Case MqttClientConnectResultCode.Success
            Case MqttClientConnectResultCode.BadUserNameOrPassword, MqttClientConnectResultCode.NotAuthorized
                Throw New UnauthorizedAccessException("Der Drucker hat die Anmeldung abgelehnt – Zugangscode prüfen (steht am Drucker unter Netzwerk / LAN-Modus).")
            Case Else
                Throw New InvalidOperationException($"Der Drucker hat die Verbindung abgelehnt ({ergebnis.ResultCode}).")
        End Select
        Dim abo = New MqttClientSubscribeOptionsBuilder().WithTopicFilter($"device/{Drucker.Seriennummer}/report").Build()
        Await _client.SubscribeAsync(abo, abbruch)
        _letzterPushall = Date.MinValue
    End Function

    ''' <summary>Vollständigen Zustand anfordern (nicht zu oft – die P1-Serie verträgt nur wenige Anfragen).</summary>
    Private Async Function PushallAsync(abbruch As CancellationToken) As Task
        If (Date.Now - _letzterPushall).TotalMinutes < 5 Then Return
        _letzterPushall = Date.Now
        Dim nachricht = New MqttApplicationMessageBuilder().
            WithTopic($"device/{Drucker.Seriennummer}/request").
            WithPayload("{""pushing"":{""sequence_id"":""0"",""command"":""pushall""}}").
            Build()
        Await _client.PublishAsync(nachricht, abbruch)
        Protokoll.Schreiben("MQTT pushall gesendet")
    End Function

    Private Sub NachrichtVerarbeiten(text As String)
        Protokoll.Schreiben("MQTT ← " & Kuerzen(text))
        Try
            Using doc = JsonDocument.Parse(text)
                Dim druck As JsonElement
                If Not doc.RootElement.TryGetProperty("print", druck) OrElse druck.ValueKind <> JsonValueKind.Object Then Return
                Dim neu = TryCast(Nodes.JsonNode.Parse(druck.GetRawText()), Nodes.JsonObject)
                If neu Is Nothing Then Return
                SyncLock _sperre
                    Zusammenfuehren(_zustand, neu)
                    _datenErhalten = True
                End SyncLock
            End Using
        Catch ex As JsonException
            ' Unvollständige Nachricht ignorieren
        End Try
    End Sub

    ''' <summary>
    ''' Teil-Updates verschachtelt übernehmen: {"ams":{"tray_now":"1"}} ändert nur tray_now und lässt die Fächerliste stehen.
    ''' Listen (z. B. die Fächer) werden als Ganzes ersetzt.
    ''' </summary>
    Private Shared Sub Zusammenfuehren(ziel As Nodes.JsonObject, quelle As Nodes.JsonObject)
        For Each eintrag In quelle.ToList()
            Dim vorhanden As Nodes.JsonNode = Nothing
            Dim zielObjekt = If(ziel.TryGetPropertyValue(eintrag.Key, vorhanden), TryCast(vorhanden, Nodes.JsonObject), Nothing)
            Dim quellObjekt = TryCast(eintrag.Value, Nodes.JsonObject)
            If zielObjekt IsNot Nothing AndAlso quellObjekt IsNot Nothing Then
                Zusammenfuehren(zielObjekt, quellObjekt)
            Else
                ziel(eintrag.Key) = eintrag.Value?.DeepClone()
            End If
        Next
    End Sub

    Public Overrides Async Function AbfragenAsync(abbruch As CancellationToken) As Task(Of DruckerStatus)
        If Drucker.Seriennummer.Trim().Length = 0 Then Throw New InvalidOperationException("Bitte die Seriennummer des Druckers eintragen.")
        Await VerbindenAsync(abbruch)
        Await PushallAsync(abbruch)
        ' Auf die ersten Daten warten (direkt nach dem Verbinden)
        Dim warten = 0
        While Not _datenErhalten AndAlso warten < 50
            Await Task.Delay(100, abbruch)
            warten += 1
        End While
        If Not _datenErhalten Then Throw New TimeoutException("Verbunden, aber der Drucker sendet keine Daten – Seriennummer prüfen.")

        ' Momentaufnahme des zusammengeführten Zustands
        Dim momentaufnahme As JsonElement
        SyncLock _sperre
            Using doc = JsonDocument.Parse(_zustand.ToJsonString())
                momentaufnahme = doc.RootElement.Clone()
            End Using
        End SyncLock
        Dim feld As Func(Of String, JsonElement?) = Function(name) Kind(momentaufnahme, name)

        Dim s As New DruckerStatus()
        Dim zustand = Text(feld("gcode_state"))
        Select Case zustand.ToUpperInvariant()
            Case "RUNNING", "PREPARE", "SLICING" : s.Zustand = DruckerZustand.Druckt
            Case "PAUSE" : s.Zustand = DruckerZustand.Pausiert
            Case "FINISH" : s.Zustand = DruckerZustand.Fertig
            Case "FAILED" : s.Zustand = DruckerZustand.Abgebrochen
            Case "IDLE" : s.Zustand = DruckerZustand.Bereit
            Case Else : s.Zustand = DruckerZustand.Unbekannt
        End Select
        s.Meldung = zustand
        s.Datei = Text(feld("subtask_name"))
        If s.Datei.Length = 0 Then s.Datei = Text(feld("gcode_file"))
        s.Fortschritt = Math.Max(0, Math.Min(1, If(Zahl(feld("mc_percent")), 0.0) / 100.0))
        Dim rest = Zahl(feld("mc_remaining_time"))
        If rest.HasValue Then s.RestSekunden = CInt(rest.Value * 60)

        ' AMS: Fächer und aktives Fach (Slot = AMS-Nr. × 4 + Fach; 254 = externe Spule)
        Dim ams = feld("ams")
        Dim aktiv As Double? = Zahl(ams, "tray_now")
        If aktiv.HasValue AndAlso aktiv.Value <> 255 Then s.Slot = CInt(aktiv.Value)
        Dim einheiten = Kind(ams, "ams")
        If einheiten.HasValue AndAlso einheiten.Value.ValueKind = JsonValueKind.Array Then
            For Each einheit In einheiten.Value.EnumerateArray()
                Dim amsNr = CInt(If(Zahl(einheit, "id"), 0.0))
                Dim faecher = Kind(einheit, "tray")
                If Not faecher.HasValue OrElse faecher.Value.ValueKind <> JsonValueKind.Array Then Continue For
                For Each fach In faecher.Value.EnumerateArray()
                    Dim nr = CInt(If(Zahl(fach, "id"), 0.0))
                    Dim typ = Text(fach, "tray_type")
                    s.Faecher.Add(New DruckerFach With {
                        .Slot = amsNr * 4 + nr,
                        .Beschreibung = $"AMS {amsNr + 1} · Fach {nr + 1}" & If(typ.Length > 0, ": " & typ, " (leer)"),
                        .Farbe = Farbe(Text(fach, "tray_color"))})
                Next
            Next
        End If
        Dim extern = feld("vt_tray")
        If extern.HasValue Then
            Dim typ = Text(extern, "tray_type")
            s.Faecher.Add(New DruckerFach With {.Slot = Drucker.SlotExtern,
                                               .Beschreibung = "Externe Spule" & If(typ.Length > 0, ": " & typ, ""),
                                               .Farbe = Farbe(Text(extern, "tray_color"))})
        End If
        Return s
    End Function

    ''' <summary>Bambu-Farbe "RRGGBBAA" → Color.</summary>
    Private Shared Function Farbe(hex As String) As Drawing.Color?
        If hex.Length < 6 Then Return Nothing
        Dim r, g, b As Integer
        If Integer.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, Nothing, r) AndAlso
           Integer.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, Nothing, g) AndAlso
           Integer.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, Nothing, b) Then Return Drawing.Color.FromArgb(r, g, b)
        Return Nothing
    End Function

    Public Overrides Sub Dispose()
        Try
            If _client IsNot Nothing AndAlso _client.IsConnected Then _client.DisconnectAsync().Wait(1000)
        Catch ex As Exception
            ' Beim Beenden egal
        End Try
        _client?.Dispose()
    End Sub
End Class
