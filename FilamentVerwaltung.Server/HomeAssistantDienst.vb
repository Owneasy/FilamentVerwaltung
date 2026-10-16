Imports System.Collections.Concurrent
Imports System.Text.Json
Imports System.Threading
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports MQTTnet
Imports MQTTnet.Protocol

''' <summary>
''' Meldet Drucker und Lager per MQTT an Home Assistant (MQTT-Discovery): je Drucker ein Gerät mit Status, Fortschritt,
''' Restzeit, Temperaturen und einem Ereignis "Druckende"; dazu ein Gerät "Filament-Verwaltung" mit Lager und Monatswerten.
''' Die Einstellungen (HA.*) werden laufend gelesen – Änderungen in der App greifen ohne Neustart.
''' </summary>
Public Class HomeAssistantDienst
    Inherits BackgroundService

    Private Const Basis As String = "filamentverwaltung"
    Private Const Verfuegbar As String = Basis & "/verfuegbar"
    Private Const LagerGeraet As String = "filamentverwaltung"
    Private Shared ReadOnly Takt As TimeSpan = TimeSpan.FromSeconds(5)
    Private Shared ReadOnly LagerTakt As TimeSpan = TimeSpan.FromSeconds(30)
    Private Shared ReadOnly Json As New JsonSerializerOptions With {.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}
    ''' <summary>Alle Sensoren eines Druckers (zum Entfernen, wenn der Drucker gelöscht wird).</summary>
    Private Shared ReadOnly DruckerSchluessel As String() = {"status", "fortschritt", "restzeit", "fertig_um", "datei", "schicht", "meldung",
                                                             "temp_duese", "temp_bett", "temp_kammer", "druckt", "druckende"}

    Private ReadOnly _db As Datenbank
    Private ReadOnly _ueberwachung As DruckerUeberwachung
    Private ReadOnly _log As ILogger(Of HomeAssistantDienst)
    Private ReadOnly _ereignisse As New ConcurrentQueue(Of DruckEnde)
    Private ReadOnly _version As String = If(GetType(HomeAssistantDienst).Assembly.GetName().Version?.ToString(3), "")

    Private _client As IMqttClient
    Private _kennung As String = ""
    Private _praefix As String = HomeAssistantEinstellungen.StandardPraefix
    ''' <summary>Je Drucker die zuletzt angekündigten Sensoren (neu ankündigen, wenn sich das ändert).</summary>
    Private ReadOnly _angekuendigt As New Dictionary(Of Long, String)
    Private _lagerAngekuendigt As Boolean
    Private ReadOnly _letzte As New Dictionary(Of String, String)
    Private _lagerZeit As Date = Date.MinValue
    Private _neuAnkuendigen As Boolean

    Public Property Status As New HaStatus With {.Meldung = "Aus"}

    Public Sub New(db As Datenbank, ueberwachung As DruckerUeberwachung, log As ILogger(Of HomeAssistantDienst))
        _db = db
        _ueberwachung = ueberwachung
        _log = log
        AddHandler _ueberwachung.DruckBeendet, Sub(ende) If _client IsNot Nothing Then _ereignisse.Enqueue(ende)
    End Sub

    Protected Overrides Async Function ExecuteAsync(abbruch As CancellationToken) As Task
        Dim warten = Takt
        While Not abbruch.IsCancellationRequested
            Dim fehler As Exception = Nothing
            Try
                warten = Await RundeAsync(abbruch)
            Catch ex As OperationCanceledException When abbruch.IsCancellationRequested
                Exit While
            Catch ex As Exception
                fehler = ex
            End Try
            If fehler IsNot Nothing Then
                ' Nur bei einer neuen Meldung ins Protokoll (sonst alle 30 s dieselbe Zeile)
                Dim meldung = "Fehler: " & fehler.Message
                If Status.Meldung <> meldung Then _log.LogWarning("Home Assistant: {Meldung}", fehler.Message)
                Status = New HaStatus With {.Meldung = meldung}
                _kennung = ""
                Await TrennenAsync(CancellationToken.None)
                warten = TimeSpan.FromSeconds(30)
            End If
            Try
                Await Task.Delay(warten, abbruch)
            Catch ex As OperationCanceledException
                Exit While
            End Try
        End While
        Await TrennenAsync(CancellationToken.None)
    End Function

    ''' <summary>Ein Durchlauf: Verbindung prüfen, ankündigen, Zustände senden. Liefert die Wartezeit bis zum nächsten.</summary>
    Private Async Function RundeAsync(abbruch As CancellationToken) As Task(Of TimeSpan)
        Dim v = HomeAssistantEinstellungen.Laden(_db)
        If Not v.Aktiv OrElse v.Host.Length = 0 Then
            Await TrennenAsync(abbruch)
            _kennung = ""
            Status = New HaStatus With {.Meldung = If(v.Aktiv, "Kein MQTT-Broker eingetragen", "Aus")}
            Return Takt
        End If
        If _client Is Nothing OrElse Not _client.IsConnected OrElse v.Kennung() <> _kennung Then
            Await TrennenAsync(abbruch)
            _kennung = v.Kennung()
            _praefix = v.Praefix
            _client = Await VerbindenAsync(v, abbruch, mitWille:=True)
            AddHandler _client.ApplicationMessageReceivedAsync,
                Function(e)
                    ' Home Assistant neu gestartet: alles noch einmal ankündigen
                    If e.ApplicationMessage.Topic = _praefix & "/status" AndAlso e.ApplicationMessage.ConvertPayloadToString() = "online" Then _neuAnkuendigen = True
                    Return Task.CompletedTask
                End Function
            Await _client.SubscribeAsync(New MqttClientSubscribeOptionsBuilder().WithTopicFilter(_praefix & "/status").Build(), abbruch)
            Await SendenAsync(Verfuegbar, "online", True, abbruch)
            _angekuendigt.Clear()
            _letzte.Clear()
            _lagerAngekuendigt = False
            _lagerZeit = Date.MinValue
            Status = New HaStatus With {.Verbunden = True, .Meldung = $"Verbunden mit {v.Host}:{v.Port}"}
            _log.LogInformation("Home Assistant: verbunden mit {Host}:{Port}", v.Host, v.Port)
        End If
        If _neuAnkuendigen Then
            _neuAnkuendigen = False
            _angekuendigt.Clear()
            _lagerAngekuendigt = False
            _letzte.Clear()
            _lagerZeit = Date.MinValue
        End If

        Await DruckerSendenAsync(abbruch)
        If Date.Now - _lagerZeit >= LagerTakt Then
            _lagerZeit = Date.Now
            Await LagerSendenAsync(abbruch)
        End If
        Dim ende As DruckEnde = Nothing
        While _ereignisse.TryDequeue(ende)
            Await SendenAsync($"{Basis}/drucker/{ende.DruckerId}/druckende", JsonSerializer.Serialize(New Dictionary(Of String, Object) From {
                {"event_type", If(ende.Abgebrochen, "abgebrochen", "fertig")},
                {"datei", ende.Datei},
                {"fortschritt", Math.Round(ende.Fortschritt * 100)},
                {"dauer_min", If(ende.DauerSekunden.HasValue, CObj(Math.Round(ende.DauerSekunden.Value / 60.0)), Nothing)}
            }, Json), False, abbruch)
        End While
        Return Takt
    End Function

    ''' <summary>Verbindung aufbauen (mit "Letztem Willen": fällt der Server weg, zeigt HA die Sensoren als nicht verfügbar).</summary>
    Private Shared Async Function VerbindenAsync(v As HaVerbindung, abbruch As CancellationToken, mitWille As Boolean) As Task(Of IMqttClient)
        Dim client = New MqttClientFactory().CreateMqttClient()
        Dim b = New MqttClientOptionsBuilder().
            WithTcpServer(v.Host, v.Port).
            WithClientId($"filamentverwaltung-{Guid.NewGuid():N}".Substring(0, 30)).
            WithCleanSession(True).
            WithTimeout(TimeSpan.FromSeconds(10)).
            WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
        If v.Benutzer.Length > 0 Then b = b.WithCredentials(v.Benutzer, v.Passwort)
        If mitWille Then b = b.WithWillTopic(Verfuegbar).WithWillPayload("offline").WithWillRetain(True).WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
        Try
            Dim ergebnis = Await client.ConnectAsync(b.Build(), abbruch)
            If ergebnis.ResultCode <> MqttClientConnectResultCode.Success Then
                Throw New InvalidOperationException(VerbindungsFehler(ergebnis.ResultCode))
            End If
        Catch ex As MQTTnet.Adapter.MqttConnectingFailedException
            client.Dispose()
            Dim anmeldung = ex.Message.Contains(NameOf(MqttClientConnectResultCode.BadUserNameOrPassword)) OrElse
                            ex.Message.Contains(NameOf(MqttClientConnectResultCode.NotAuthorized))
            Throw New InvalidOperationException(If(anmeldung, VerbindungsFehler(MqttClientConnectResultCode.NotAuthorized), ex.Message), ex)
        Catch
            client.Dispose()
            Throw
        End Try
        Return client
    End Function

    Private Shared Function VerbindungsFehler(code As MqttClientConnectResultCode) As String
        Select Case code
            Case MqttClientConnectResultCode.BadUserNameOrPassword, MqttClientConnectResultCode.NotAuthorized
                Return "Benutzername oder Passwort falsch (MQTT-Broker lehnt die Anmeldung ab)"
            Case Else
                Return "MQTT-Broker lehnt die Verbindung ab: " & code.ToString()
        End Select
    End Function

    ''' <summary>Verbindung mit den angegebenen Werten testen (Knopf in der App), ohne die laufende zu stören.</summary>
    Public Shared Async Function TestenAsync(v As HaVerbindung, abbruch As CancellationToken) As Task(Of HaStatus)
        If String.IsNullOrWhiteSpace(v.Host) Then Return New HaStatus With {.Meldung = "Bitte den MQTT-Broker eintragen."}
        Try
            Using client = Await VerbindenAsync(v, abbruch, mitWille:=False)
                Await client.DisconnectAsync(New MqttClientDisconnectOptionsBuilder().Build(), abbruch)
            End Using
            Return New HaStatus With {.Verbunden = True, .Meldung = $"Verbindung zu {v.Host}:{v.Port} klappt."}
        Catch ex As Exception
            Dim grund = If(TypeOf ex.InnerException Is Net.Sockets.SocketException, ex.InnerException.Message, ex.Message)
            Return New HaStatus With {.Meldung = grund}
        End Try
    End Function

    Private Async Function TrennenAsync(abbruch As CancellationToken) As Task
        Dim c = _client
        _client = Nothing
        If c Is Nothing Then Return
        Try
            If c.IsConnected Then
                Await SendenAsync(c, Verfuegbar, "offline", True, abbruch)
                Await c.DisconnectAsync(New MqttClientDisconnectOptionsBuilder().Build(), abbruch)
            End If
        Catch ex As Exception
            ' Verbindung ist ohnehin weg
        End Try
        c.Dispose()
    End Function

    Private Function SendenAsync(thema As String, inhalt As String, behalten As Boolean, abbruch As CancellationToken) As Task
        Return SendenAsync(_client, thema, inhalt, behalten, abbruch)
    End Function

    Private Shared Async Function SendenAsync(c As IMqttClient, thema As String, inhalt As String, behalten As Boolean, abbruch As CancellationToken) As Task
        Await c.PublishAsync(New MqttApplicationMessageBuilder().WithTopic(thema).WithPayload(inhalt).WithRetainFlag(behalten).
                             WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build(), abbruch)
    End Function

    ''' <summary>Nur senden, wenn sich der Inhalt geändert hat (Zustände werden behalten: HA kennt sie auch nach einem Neustart).</summary>
    Private Async Function WennGeaendertAsync(thema As String, inhalt As String, abbruch As CancellationToken) As Task
        Dim alt As String = Nothing
        If _letzte.TryGetValue(thema, alt) AndAlso alt = inhalt Then Return
        Await SendenAsync(thema, inhalt, True, abbruch)
        _letzte(thema) = inhalt
    End Function

    ' ------------------------------------------------------------------ Drucker

    Private Async Function DruckerSendenAsync(abbruch As CancellationToken) As Task
        Dim alle = _db.DruckerLaden().Where(Function(d) d.Aktiv).ToList()
        For Each d In alle
            Dim s = _ueberwachung.Status(d.Id)
            If s Is Nothing Then Continue For
            Dim temps = s.Temperaturen.ToDictionary(Function(t) t.Name, Function(t) t, StringComparer.OrdinalIgnoreCase)
            Dim mitKammer = temps.ContainsKey("Kammer")
            Dim kennung = $"{d.Name}|{d.TypName}|{mitKammer}"
            Dim alt As String = Nothing
            If Not _angekuendigt.TryGetValue(d.Id, alt) OrElse alt <> kennung Then
                Await DruckerAnkuendigenAsync(d, mitKammer, abbruch)
                _angekuendigt(d.Id) = kennung
            End If

            Dim laeuft = s.DruckLaeuft
            Dim temp = Function(name As String) As Object
                           Dim t As DruckerTemperatur = Nothing
                           Return If(temps.TryGetValue(name, t), CObj(Math.Round(t.Ist, 1)), Nothing)
                       End Function
            Dim fertigUm As Object = Nothing
            If laeuft AndAlso s.RestSekunden.HasValue Then
                Dim z = DateTimeOffset.Now.AddSeconds(s.RestSekunden.Value)
                fertigUm = New DateTimeOffset(z.Year, z.Month, z.Day, z.Hour, z.Minute, 0, z.Offset).ToString("yyyy-MM-ddTHH:mm:sszzz")
            End If
            Dim werte As New Dictionary(Of String, Object) From {
                {"zustand", DruckerStatus.ZustandText(s.Zustand)},
                {"druckt", If(laeuft, "ON", "OFF")},
                {"fortschritt", If(laeuft OrElse s.Zustand = DruckerZustand.Fertig, CObj(Math.Round(s.Fortschritt * 100)), CObj(0))},
                {"restzeit", If(laeuft AndAlso s.RestSekunden.HasValue, CObj(Math.Round(s.RestSekunden.Value / 60.0)), Nothing)},
                {"fertig_um", fertigUm},
                {"datei", If(String.IsNullOrWhiteSpace(s.Datei), Nothing, CObj(Kuerzen(s.Datei)))},
                {"schicht", If(laeuft AndAlso s.Schicht.HasValue, CObj(s.Schicht.Value), Nothing)},
                {"schichten", If(s.SchichtenGesamt.HasValue, CObj(s.SchichtenGesamt.Value), Nothing)},
                {"meldung", If(String.IsNullOrWhiteSpace(s.Meldung), "", Kuerzen(s.Meldung))},
                {"temp_duese", temp("Düse")},
                {"temp_bett", temp("Bett")},
                {"temp_kammer", temp("Kammer")}
            }
            Await WennGeaendertAsync($"{Basis}/drucker/{d.Id}/zustand", JsonSerializer.Serialize(werte, Json), abbruch)
        Next

        ' Gelöschte oder abgeschaltete Drucker: Sensoren in Home Assistant entfernen
        For Each id In _angekuendigt.Keys.Except(alle.Select(Function(d) d.Id)).ToList()
            Await DruckerEntfernenAsync(id, abbruch)
            _angekuendigt.Remove(id)
        Next
        Dim gemerkt = String.Join(",", _angekuendigt.Keys.OrderBy(Function(x) x))
        Dim vorher = If(_db.EinstellungLesen("HA.Drucker"), "")
        If gemerkt <> vorher Then
            ' Auch Drucker entfernen, die gelöscht wurden, während der Server nicht lief
            For Each teil In vorher.Split(","c, StringSplitOptions.RemoveEmptyEntries)
                Dim id As Long
                If Long.TryParse(teil, id) AndAlso Not _angekuendigt.ContainsKey(id) AndAlso Not alle.Any(Function(d) d.Id = id) Then
                    Await DruckerEntfernenAsync(id, abbruch)
                End If
            Next
            _db.EinstellungSpeichern("HA.Drucker", gemerkt)
        End If
    End Function

    Private Shared Function Kuerzen(text As String) As String
        Return If(text.Length > 240, text.Substring(0, 240) & "…", text)
    End Function

    Private Shared Function DruckerGeraet(d As Drucker) As Dictionary(Of String, Object)
        Return New Dictionary(Of String, Object) From {
            {"identifiers", {$"filamentverwaltung_drucker_{d.Id}"}},
            {"name", d.Name},
            {"model", d.TypName},
            {"via_device", LagerGeraet}
        }
    End Function

    Private Async Function DruckerAnkuendigenAsync(d As Drucker, mitKammer As Boolean, abbruch As CancellationToken) As Task
        Dim zustand = $"{Basis}/drucker/{d.Id}/zustand"
        Dim geraet = DruckerGeraet(d)
        Dim sensor = Function(schluessel As String, name As String, vorlage As String, einheit As String,
                              klasse As String, messung As Boolean, symbol As String) As Task
                         Dim k As New Dictionary(Of String, Object) From {
                             {"name", name}, {"unique_id", $"filamentverwaltung_d{d.Id}_{schluessel}"},
                             {"state_topic", zustand}, {"value_template", vorlage}, {"device", geraet}}
                         If einheit IsNot Nothing Then k("unit_of_measurement") = einheit
                         If klasse IsNot Nothing Then k("device_class") = klasse
                         If messung Then k("state_class") = "measurement"
                         If symbol IsNot Nothing Then k("icon") = symbol
                         Return KonfigSendenAsync("sensor", $"d{d.Id}_{schluessel}", k, abbruch)
                     End Function

        Await sensor("status", "Status", "{{ value_json.zustand }}", Nothing, Nothing, False, "mdi:printer-3d")
        Await sensor("fortschritt", "Fortschritt", "{{ value_json.fortschritt }}", "%", Nothing, True, "mdi:progress-clock")
        Await sensor("restzeit", "Restzeit", "{{ value_json.restzeit }}", "min", "duration", False, Nothing)
        Await sensor("fertig_um", "Fertig um", "{{ value_json.fertig_um }}", Nothing, "timestamp", False, Nothing)
        Await sensor("datei", "Datei", "{{ value_json.datei }}", Nothing, Nothing, False, "mdi:file-outline")
        Await sensor("schicht", "Schicht", "{{ value_json.schicht }}", Nothing, Nothing, False, "mdi:layers-triple-outline")
        Await sensor("meldung", "Meldung", "{{ value_json.meldung }}", Nothing, Nothing, False, "mdi:message-alert-outline")
        Await sensor("temp_duese", "Temperatur Düse", "{{ value_json.temp_duese }}", "°C", "temperature", True, Nothing)
        Await sensor("temp_bett", "Temperatur Bett", "{{ value_json.temp_bett }}", "°C", "temperature", True, Nothing)
        If mitKammer Then
            Await sensor("temp_kammer", "Temperatur Kammer", "{{ value_json.temp_kammer }}", "°C", "temperature", True, Nothing)
        Else
            Await KonfigEntfernenAsync("sensor", $"d{d.Id}_temp_kammer", abbruch)
        End If

        Await KonfigSendenAsync("binary_sensor", $"d{d.Id}_druckt", New Dictionary(Of String, Object) From {
            {"name", "Druckt"}, {"unique_id", $"filamentverwaltung_d{d.Id}_druckt"}, {"state_topic", zustand},
            {"value_template", "{{ value_json.druckt }}"}, {"device_class", "running"}, {"device", geraet}}, abbruch)
        Await KonfigSendenAsync("event", $"d{d.Id}_druckende", New Dictionary(Of String, Object) From {
            {"name", "Druckende"}, {"unique_id", $"filamentverwaltung_d{d.Id}_druckende"},
            {"state_topic", $"{Basis}/drucker/{d.Id}/druckende"}, {"event_types", {"fertig", "abgebrochen"}},
            {"icon", "mdi:printer-3d-nozzle-alert-outline"}, {"device", geraet}}, abbruch)
    End Function

    Private Async Function DruckerEntfernenAsync(id As Long, abbruch As CancellationToken) As Task
        For Each schluessel In DruckerSchluessel
            Dim art = If(schluessel = "druckt", "binary_sensor", If(schluessel = "druckende", "event", "sensor"))
            Await KonfigEntfernenAsync(art, $"d{id}_{schluessel}", abbruch)
        Next
        Await SendenAsync($"{Basis}/drucker/{id}/zustand", "", True, abbruch)
    End Function

    Private Function KonfigSendenAsync(art As String, objekt As String, k As Dictionary(Of String, Object), abbruch As CancellationToken) As Task
        k("availability_topic") = Verfuegbar
        k("origin") = New Dictionary(Of String, Object) From {{"name", "Filament-Verwaltung"}, {"sw_version", _version}}
        Return SendenAsync($"{_praefix}/{art}/filamentverwaltung/{objekt}/config", JsonSerializer.Serialize(k, Json), True, abbruch)
    End Function

    ''' <summary>Leere, behaltene Konfiguration: Home Assistant entfernt den Sensor.</summary>
    Private Function KonfigEntfernenAsync(art As String, objekt As String, abbruch As CancellationToken) As Task
        Return SendenAsync($"{_praefix}/{art}/filamentverwaltung/{objekt}/config", "", True, abbruch)
    End Function

    ' ------------------------------------------------------------------ Lager

    Private Async Function LagerSendenAsync(abbruch As CancellationToken) As Task
        Dim zustand = $"{Basis}/lager"
        If Not _lagerAngekuendigt Then
            Dim geraet As New Dictionary(Of String, Object) From {
                {"identifiers", {LagerGeraet}}, {"name", "Filament-Verwaltung"}, {"manufacturer", "Filament-Verwaltung"},
                {"model", "Server"}, {"sw_version", _version}}
            Dim eintraege As (Schluessel As String, Name As String, Einheit As String, Symbol As String)() = {("spulen", "Spulen auf Lager", Nothing, "mdi:printer-3d-nozzle"),
                           ("lager", "Filament auf Lager", "g", "mdi:weight-gram"),
                           ("fast_leer", "Spulen fast leer", Nothing, "mdi:alert-circle-outline"),
                           ("offen", "Drucke zum Buchen", Nothing, "mdi:clipboard-text-clock-outline"),
                           ("drucke_monat", "Drucke diesen Monat", Nothing, "mdi:printer-3d"),
                           ("verbrauch_monat", "Verbrauch diesen Monat", "g", "mdi:chart-bar"),
                           ("kosten_monat", "Kosten diesen Monat", "EUR", Nothing),
                           ("fehldrucke_monat", "Fehldrucke diesen Monat", Nothing, "mdi:alert-octagon-outline")}
            For Each e In eintraege
                Dim k As New Dictionary(Of String, Object) From {
                    {"name", e.Name}, {"unique_id", $"filamentverwaltung_{e.Schluessel}"}, {"state_topic", zustand},
                    {"value_template", $"{{{{ value_json.{e.Schluessel} }}}}"}, {"device", geraet}}
                If e.Einheit IsNot Nothing Then k("unit_of_measurement") = e.Einheit
                If e.Symbol IsNot Nothing Then k("icon") = e.Symbol
                If e.Schluessel = "kosten_monat" Then
                    k("device_class") = "monetary"
                ElseIf e.Schluessel = "lager" Then
                    k("device_class") = "weight"
                    k("state_class") = "measurement"
                Else
                    k("state_class") = "measurement"
                End If
                Await KonfigSendenAsync("sensor", e.Schluessel, k, abbruch)
            Next
            _lagerAngekuendigt = True
        End If

        Dim filamente = _db.AlleLaden()
        Dim buchungen = _db.BuchungenLaden()
        Dim g As Integer
        Dim grenze = If(Integer.TryParse(_db.EinstellungLesen(PushEinstellungen.SpuleKnappGrenze), g) AndAlso g > 0, g, 100)
        Dim monatsStart = New Date(Date.Today.Year, Date.Today.Month, 1)
        Dim monat = buchungen.Where(Function(b) b.Datum >= monatsStart).ToList()
        Dim verbrauch = monat.Where(AddressOf IstVerbrauch).ToList()
        Dim werte As New Dictionary(Of String, Object) From {
            {"spulen", filamente.Where(Function(f) f.Restgewicht > 0).Count()},
            {"lager", filamente.Where(Function(f) f.Restgewicht > 0).Sum(Function(f) CLng(f.Restgewicht))},
            {"fast_leer", filamente.Where(Function(f) f.Restgewicht > 0 AndAlso f.Restgewicht < grenze).Count()},
            {"offen", _db.OffeneDruckEnden().Count},
            {"drucke_monat", monat.Where(Function(b) b.IstDruck).Count()},
            {"verbrauch_monat", verbrauch.Sum(Function(b) CLng(b.Gramm))},
            {"kosten_monat", Math.Round(verbrauch.Sum(Function(b) b.Kosten), 2)},
            {"fehldrucke_monat", monat.Where(Function(b) b.IstDruck AndAlso b.Fehldruck).Count()}
        }
        Await WennGeaendertAsync(zustand, JsonSerializer.Serialize(werte, Json), abbruch)
    End Function

End Class
