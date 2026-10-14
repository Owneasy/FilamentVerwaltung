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

''' <summary>Optionen beim Senden eines Druckauftrags.</summary>
Public Class SendeOptionen
    ''' <summary>Platte der 3MF (Bambu), 1-basiert.</summary>
    Public Property Platte As Integer = 1
    ''' <summary>Je Filament der Datei das AMS-Fach (0–15) bzw. 254 für die externe Spule (Bambu).</summary>
    Public Property AmsZuordnung As New List(Of Integer)
    Public Property Nivellieren As Boolean = True
    Public Property Zeitraffer As Boolean
    Public Property FlussKalibrierung As Boolean
End Class

''' <summary>Meldet beim Lesen den Fortschritt (für Uploads).</summary>
Public Class FortschrittStream
    Inherits IO.Stream

    Private ReadOnly _innen As IO.Stream
    Private ReadOnly _fortschritt As IProgress(Of Double)
    Private _gelesen As Long

    Public Sub New(innen As IO.Stream, fortschritt As IProgress(Of Double))
        _innen = innen
        _fortschritt = fortschritt
    End Sub

    Public Overrides Function Read(buffer() As Byte, offset As Integer, count As Integer) As Integer
        Dim n = _innen.Read(buffer, offset, count)
        Melden(n)
        Return n
    End Function

    Public Overrides Function ReadAsync(buffer As Memory(Of Byte), Optional cancellationToken As CancellationToken = Nothing) As ValueTask(Of Integer)
        Return New ValueTask(Of Integer)(LesenAsync(buffer, cancellationToken))
    End Function

    Private Async Function LesenAsync(buffer As Memory(Of Byte), cancellationToken As CancellationToken) As Task(Of Integer)
        Dim n = Await _innen.ReadAsync(buffer, cancellationToken)
        Melden(n)
        Return n
    End Function

    Private Sub Melden(n As Integer)
        _gelesen += n
        If _innen.Length > 0 Then _fortschritt?.Report(CDbl(_gelesen) / _innen.Length)
    End Sub

    Public Overrides ReadOnly Property CanRead As Boolean = True
    Public Overrides ReadOnly Property CanSeek As Boolean = False
    Public Overrides ReadOnly Property CanWrite As Boolean = False
    Public Overrides ReadOnly Property Length As Long
        Get
            Return _innen.Length
        End Get
    End Property
    Public Overrides Property Position As Long
        Get
            Return _gelesen
        End Get
        Set(value As Long)
            Throw New NotSupportedException()
        End Set
    End Property
    Public Overrides Sub Flush()
    End Sub
    Public Overrides Function Seek(offset As Long, origin As IO.SeekOrigin) As Long
        Throw New NotSupportedException()
    End Function
    Public Overrides Sub SetLength(value As Long)
        Throw New NotSupportedException()
    End Sub
    Public Overrides Sub Write(buffer() As Byte, offset As Integer, count As Integer)
        Throw New NotSupportedException()
    End Sub
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _innen.Dispose()
        MyBase.Dispose(disposing)
    End Sub
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

    ''' <summary>Dateiendungen, die dieser Drucker zum Drucken annimmt.</summary>
    Public MustOverride ReadOnly Property DruckbareEndungen As String()

    ''' <summary>Lädt die Datei hoch und startet den Druck (wirft bei Fehlern).</summary>
    Public MustOverride Function SendenAsync(datei As String, optionen As SendeOptionen, fortschritt As IProgress(Of Double),
                                             abbruch As CancellationToken) As Task

    ''' <summary>Dateiname für den Drucker: nur unkritische Zeichen.</summary>
    Protected Shared Function DruckerDateiname(datei As String) As String
        Dim name = IO.Path.GetFileName(datei)
        Dim erlaubt = name.Select(Function(c) If(Char.IsLetterOrDigit(c) AndAlso c < ChrW(128) OrElse c = "."c OrElse c = "-"c OrElse c = "_"c, c, "_"c)).ToArray()
        Return New String(erlaubt)
    End Function

    ''' <summary>HTTP-Client für Uploads (lange Zeitüberschreitung).</summary>
    Protected Shared Function UploadClient(basis As String, Optional handler As HttpMessageHandler = Nothing) As HttpClient
        Dim c = NeuerHttpClient(basis, handler)
        c.Timeout = TimeSpan.FromMinutes(15)
        Return c
    End Function

    Protected Async Function AntwortPruefenAsync(antwort As HttpResponseMessage, was As String, abbruch As CancellationToken) As Task
        Dim text = Await antwort.Content.ReadAsStringAsync(abbruch)
        Protokoll.Schreiben($"{was} → {CInt(antwort.StatusCode)} {Kuerzen(text)}")
        If antwort.StatusCode = HttpStatusCode.Unauthorized OrElse antwort.StatusCode = HttpStatusCode.Forbidden Then
            Throw New UnauthorizedAccessException("Zugriff verweigert – bitte API-Key, Benutzer bzw. Passwort prüfen.")
        End If
        If antwort.StatusCode = HttpStatusCode.Conflict Then Throw New InvalidOperationException("Der Drucker ist beschäftigt (druckt gerade?).")
        If Not antwort.IsSuccessStatusCode Then Throw New InvalidOperationException($"Der Drucker hat den Auftrag abgelehnt ({CInt(antwort.StatusCode)}): {Kuerzen(text)}")
    End Function

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

    ''' <summary>Temperatur übernehmen, wenn der Drucker einen Ist-Wert meldet.</summary>
    Protected Shared Sub Temperatur(s As DruckerStatus, name As String, ist As Double?, soll As Double?)
        If Not ist.HasValue Then Return
        s.Temperaturen.Add(New DruckerTemperatur With {.Name = name, .Ist = ist.Value, .Soll = soll})
    End Sub

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

        ' Temperaturen (antwortet mit 409, wenn der Drucker nicht verbunden ist – dann eben ohne)
        If s.Zustand <> DruckerZustand.Offline Then
            Try
                Dim drucker = Await JsonHolenAsync(_http, "api/printer?history=false&exclude=sd,state", abbruch)
                Temperatur(s, "Düse", Zahl(drucker, "temperature", "tool0", "actual"), Zahl(drucker, "temperature", "tool0", "target"))
                Temperatur(s, "Bett", Zahl(drucker, "temperature", "bed", "actual"), Zahl(drucker, "temperature", "bed", "target"))
                Temperatur(s, "Kammer", Zahl(drucker, "temperature", "chamber", "actual"), Zahl(drucker, "temperature", "chamber", "target"))
            Catch ex As Exception When TypeOf ex IsNot OperationCanceledException
            End Try
        End If
        Return s
    End Function

    Public Overrides ReadOnly Property DruckbareEndungen As String() = {".gcode", ".gco", ".g"}

    Public Overrides Async Function SendenAsync(datei As String, optionen As SendeOptionen, fortschritt As IProgress(Of Double),
                                                abbruch As CancellationToken) As Task
        Using http = UploadClient(Drucker.BasisUrl()), inhalt As New MultipartFormDataContent(),
              strom As New FortschrittStream(IO.File.OpenRead(datei), fortschritt)
            If Drucker.Zugang.Length > 0 Then http.DefaultRequestHeaders.Add("X-Api-Key", Drucker.Zugang)
            inhalt.Add(New StreamContent(strom), "file", DruckerDateiname(datei))
            inhalt.Add(New StringContent("true"), "select")
            inhalt.Add(New StringContent("true"), "print")
            Using antwort = Await http.PostAsync("api/files/local", inhalt, abbruch)
                Await AntwortPruefenAsync(antwort, "POST api/files/local (drucken)", abbruch)
            End Using
        End Using
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
        ' Nicht vorhandene Objekte (z. B. ohne Heizbett) lässt Moonraker in der Antwort einfach weg
        Dim antwort = Await JsonHolenAsync(_http, "printer/objects/query?print_stats&virtual_sdcard&extruder=temperature,target&heater_bed=temperature,target", abbruch)
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
        Dim status = Kind(antwort, "result", "status")
        Dim schicht = Zahl(stats, "info", "current_layer"), gesamt = Zahl(stats, "info", "total_layer")
        If schicht.HasValue Then s.Schicht = CInt(schicht.Value)
        If gesamt.HasValue Then s.SchichtenGesamt = CInt(gesamt.Value)
        Temperatur(s, "Düse", Zahl(status, "extruder", "temperature"), Zahl(status, "extruder", "target"))
        Temperatur(s, "Bett", Zahl(status, "heater_bed", "temperature"), Zahl(status, "heater_bed", "target"))

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

    Public Overrides ReadOnly Property DruckbareEndungen As String() = {".gcode", ".gco", ".g"}

    Public Overrides Async Function SendenAsync(datei As String, optionen As SendeOptionen, fortschritt As IProgress(Of Double),
                                                abbruch As CancellationToken) As Task
        Using http = UploadClient(Drucker.BasisUrl()), inhalt As New MultipartFormDataContent(),
              strom As New FortschrittStream(IO.File.OpenRead(datei), fortschritt)
            If Drucker.Zugang.Length > 0 Then http.DefaultRequestHeaders.Add("X-Api-Key", Drucker.Zugang)
            inhalt.Add(New StreamContent(strom), "file", DruckerDateiname(datei))
            inhalt.Add(New StringContent("gcodes"), "root")
            inhalt.Add(New StringContent("true"), "print")
            Using antwort = Await http.PostAsync("server/files/upload", inhalt, abbruch)
                Await AntwortPruefenAsync(antwort, "POST server/files/upload (drucken)", abbruch)
            End Using
        End Using
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
        Temperatur(s, "Düse", Zahl(status, "printer", "temp_nozzle"), Zahl(status, "printer", "target_nozzle"))
        Temperatur(s, "Bett", Zahl(status, "printer", "temp_bed"), Zahl(status, "printer", "target_bed"))

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

    Public Overrides ReadOnly Property DruckbareEndungen As String() = {".bgcode", ".gcode"}

    Public Overrides Async Function SendenAsync(datei As String, optionen As SendeOptionen, fortschritt As IProgress(Of Double),
                                                abbruch As CancellationToken) As Task
        ' Speicher ermitteln (MK4/XL: /usb, Mini: /local)
        Dim speicher = Await JsonHolenAsync(_http, "api/v1/storage", abbruch)
        Dim pfad = "/usb"
        Dim liste = Kind(speicher, "storage_list")
        If liste.HasValue AndAlso liste.Value.ValueKind = JsonValueKind.Array Then
            For Each s In liste.Value.EnumerateArray()
                Dim verfuegbar As JsonElement
                If Text(s, "path").Length > 0 AndAlso (Not s.TryGetProperty("available", verfuegbar) OrElse verfuegbar.ValueKind = JsonValueKind.True) Then
                    pfad = Text(s, "path")
                    Exit For
                End If
            Next
        End If
        Dim handler As HttpMessageHandler = If(Drucker.Benutzer.Length > 0,
            New HttpClientHandler With {.Credentials = New NetworkCredential(Drucker.Benutzer, Drucker.Zugang)}, Nothing)
        Using http = UploadClient(Drucker.BasisUrl(), handler), strom As New FortschrittStream(IO.File.OpenRead(datei), fortschritt)
            If Drucker.Benutzer.Length = 0 AndAlso Drucker.Zugang.Length > 0 Then http.DefaultRequestHeaders.Add("X-Api-Key", Drucker.Zugang)
            Dim inhalt As New StreamContent(strom)
            inhalt.Headers.ContentType = New Headers.MediaTypeHeaderValue("application/octet-stream")
            Dim anfrage As New HttpRequestMessage(HttpMethod.Put, $"api/v1/files{pfad}/{Uri.EscapeDataString(DruckerDateiname(datei))}") With {.Content = inhalt}
            anfrage.Headers.Add("Print-After-Upload", "?1")
            anfrage.Headers.Add("Overwrite", "?1")
            Using antwort = Await http.SendAsync(anfrage, abbruch)
                Await AntwortPruefenAsync(antwort, $"PUT api/v1/files{pfad} (drucken)", abbruch)
            End Using
        End Using
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
    ''' <summary>Wartet auf die Antwort des Druckers auf den Startbefehl.</summary>
    Private _startAntwort As TaskCompletionSource(Of (Ok As Boolean, Grund As String))

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

    Private Sub NachrichtVerarbeiten(inhalt As String)
        Protokoll.Schreiben("MQTT ← " & Kuerzen(inhalt))
        Try
            Using doc = JsonDocument.Parse(inhalt)
                Dim druck As JsonElement
                If Not doc.RootElement.TryGetProperty("print", druck) OrElse druck.ValueKind <> JsonValueKind.Object Then Return
                Dim neu = TryCast(Nodes.JsonNode.Parse(druck.GetRawText()), Nodes.JsonObject)
                If neu Is Nothing Then Return
                ' Antwort auf den Startbefehl (project_file) weiterreichen
                If Text(druck, "command") = "project_file" AndAlso Text(druck, "result").Length > 0 Then
                    Dim ergebnis = Text(druck, "result")
                    Dim grund = If(Text(druck, "reason").Length > 0, Text(druck, "reason"), Text(druck, "msg"))
                    _startAntwort?.TrySetResult((ergebnis.Equals("success", StringComparison.OrdinalIgnoreCase), grund))
                End If
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
        Dim schicht = Zahl(feld("layer_num")), gesamt = Zahl(feld("total_layer_num"))
        If schicht.HasValue Then s.Schicht = CInt(schicht.Value)
        If gesamt.HasValue Then s.SchichtenGesamt = CInt(gesamt.Value)
        ' Phase nur während eines Auftrags – nach dem Druck bleibt stg_cur oft auf dem letzten Wert stehen
        Dim phase = Zahl(feld("stg_cur"))
        If phase.HasValue AndAlso s.DruckLaeuft Then s.Phase = PhaseText(CInt(phase.Value))
        If s.Phase.Length = 0 AndAlso zustand.Equals("PREPARE", StringComparison.OrdinalIgnoreCase) Then s.Phase = "Druck wird vorbereitet"
        If s.Phase.Length = 0 AndAlso zustand.Equals("SLICING", StringComparison.OrdinalIgnoreCase) Then s.Phase = "Wird geslict"
        Temperatur(s, "Düse", Zahl(feld("nozzle_temper")), Zahl(feld("nozzle_target_temper")))
        Temperatur(s, "Bett", Zahl(feld("bed_temper")), Zahl(feld("bed_target_temper")))
        ' Einen Kammersensor haben nur X1/H2. A1 (039…), A1 mini (030…), P1P (01S…) und P1S (01P…) melden trotzdem einen Fantasiewert (z. B. 5 °C)
        Dim ohneKammer = {"039", "030", "01S", "01P"}.Any(Function(p) Drucker.Seriennummer.Trim().StartsWith(p, StringComparison.OrdinalIgnoreCase))
        Dim kammer = Zahl(feld("chamber_temper"))
        If Not ohneKammer AndAlso kammer.HasValue AndAlso kammer.Value > 0 Then Temperatur(s, "Kammer", kammer, Nothing)

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

    Public Overrides ReadOnly Property DruckbareEndungen As String() = {".3mf"}

    ''' <summary>
    ''' 3MF per FTPS (implizit, Port 990) auf die SD-Karte laden und per MQTT den Druck starten.
    ''' Der Drucker muss im LAN-Modus mit aktiviertem Entwicklermodus sein, sonst ignoriert er den Startbefehl.
    ''' </summary>
    Public Overrides Async Function SendenAsync(datei As String, optionen As SendeOptionen, fortschritt As IProgress(Of Double),
                                                abbruch As CancellationToken) As Task
        Dim name = DruckerDateiname(datei)
        ' 1) Hochladen
        Protokoll.Schreiben($"FTPS verbinde mit {Host}:990 …")
        Using ftp As New FluentFTP.AsyncFtpClient(Host, "bblp", Drucker.Zugang, 990)
            ftp.Config.EncryptionMode = FluentFTP.FtpEncryptionMode.Implicit
            ftp.Config.ValidateAnyCertificate = True   ' selbst ausgestelltes Zertifikat des Druckers
            ftp.Config.DataConnectionType = FluentFTP.FtpDataConnectionType.PASV
            ftp.Config.ConnectTimeout = 15000
            ftp.Config.DataConnectionConnectTimeout = 15000
            ftp.Config.ReadTimeout = 30000
            Try
                Await ftp.Connect(abbruch)
            Catch ex As FluentFTP.Exceptions.FtpAuthenticationException
                Throw New UnauthorizedAccessException("Der Drucker hat die FTP-Anmeldung abgelehnt – Zugangscode prüfen.", ex)
            Catch ex As Exception When TypeOf ex IsNot OperationCanceledException
                Throw New IO.IOException($"Keine FTPS-Verbindung zum Drucker (Port 990): {ex.Message} Ist der LAN-Modus am Drucker aktiv?", ex)
            End Try
            Protokoll.Schreiben($"FTPS lade hoch: /{name}")
            Dim ftpFortschritt As New Progress(Of FluentFTP.FtpProgress)(Sub(p) fortschritt?.Report(p.Progress / 100.0))
            Dim status = Await ftp.UploadFile(datei, "/" & name, FluentFTP.FtpRemoteExists.Overwrite, False, FluentFTP.FtpVerify.None, ftpFortschritt, abbruch)
            Protokoll.Schreiben($"FTPS Upload: {status}")
            If status <> FluentFTP.FtpStatus.Success Then Throw New IO.IOException("Das Hochladen auf den Drucker ist fehlgeschlagen.")
            Await ftp.Disconnect(abbruch)
        End Using

        ' 2) Startbefehl
        Await VerbindenAsync(abbruch)
        Dim zuordnung As New Nodes.JsonArray()
        For Each slot In optionen.AmsZuordnung
            zuordnung.Add(slot)
        Next
        Dim nurExtern = optionen.AmsZuordnung.Count > 0 AndAlso optionen.AmsZuordnung.All(Function(s) s = Drucker.SlotExtern)
        Dim befehl As New Nodes.JsonObject From {
            {"sequence_id", "0"},
            {"command", "project_file"},
            {"param", $"Metadata/plate_{optionen.Platte}.gcode"},
            {"url", "file:///sdcard/" & name},
            {"subtask_name", IO.Path.GetFileNameWithoutExtension(IO.Path.GetFileNameWithoutExtension(name))},
            {"project_id", "0"}, {"profile_id", "0"}, {"task_id", "0"}, {"subtask_id", "0"},
            {"bed_type", "auto"},
            {"timelapse", optionen.Zeitraffer},
            {"bed_leveling", optionen.Nivellieren},
            {"bed_levelling", optionen.Nivellieren},
            {"flow_cali", optionen.FlussKalibrierung},
            {"vibration_cali", False},
            {"layer_inspect", False},
            {"use_ams", Not nurExtern AndAlso optionen.AmsZuordnung.Count > 0},
            {"ams_mapping", If(nurExtern, New Nodes.JsonArray(), zuordnung)}
        }
        Dim nachricht = New Nodes.JsonObject From {{"print", befehl}}.ToJsonString()
        _startAntwort = New TaskCompletionSource(Of (Ok As Boolean, Grund As String))(TaskCreationOptions.RunContinuationsAsynchronously)
        Protokoll.Schreiben("MQTT → " & nachricht)
        Await _client.PublishAsync(New MqttApplicationMessageBuilder().WithTopic($"device/{Drucker.Seriennummer}/request").WithPayload(nachricht).Build(), abbruch)

        ' 3) Auf die Antwort des Druckers warten (manche Firmware antwortet nicht – dann am Status erkennen)
        Dim fertig = Await Task.WhenAny(_startAntwort.Task, Task.Delay(TimeSpan.FromSeconds(20), abbruch))
        If fertig Is _startAntwort.Task Then
            Dim antwort = _startAntwort.Task.Result
            Protokoll.Schreiben($"Startbefehl: {If(antwort.Ok, "angenommen", "abgelehnt")} {antwort.Grund}")
            If Not antwort.Ok Then
                Throw New InvalidOperationException("Der Drucker hat den Druckauftrag abgelehnt" & If(antwort.Grund.Length > 0, $" ({antwort.Grund})", "") &
                                                    ". Ist der Entwicklermodus (LAN-Modus) am Drucker aktiviert?")
            End If
        Else
            Protokoll.Schreiben("Startbefehl: keine ausdrückliche Antwort – Status wird beobachtet")
        End If
    End Function

    ''' <summary>Bambu "stg_cur" → Text (0 = druckt, -1/255 = nichts → leer).</summary>
    Private Shared Function PhaseText(stufe As Integer) As String
        Select Case stufe
            Case 1 : Return "Bett wird nivelliert"
            Case 2 : Return "Heizbett heizt auf"
            Case 3 : Return "Vibrationskompensation"
            Case 4 : Return "Filamentwechsel"
            Case 5 : Return "Pause (M400)"
            Case 6 : Return "Pausiert: Filament leer"
            Case 7 : Return "Düse heizt auf"
            Case 8 : Return "Extrusion wird kalibriert"
            Case 9 : Return "Bettoberfläche wird gescannt"
            Case 10 : Return "Erste Schicht wird geprüft"
            Case 11 : Return "Druckplatte wird erkannt"
            Case 12, 18 : Return "Micro-Lidar wird kalibriert"
            Case 13 : Return "Druckkopf fährt in Ausgangsposition"
            Case 14 : Return "Düsenspitze wird gereinigt"
            Case 15 : Return "Düsentemperatur wird geprüft"
            Case 16 : Return "Vom Benutzer pausiert"
            Case 17 : Return "Pausiert: Frontabdeckung abgefallen"
            Case 19 : Return "Durchfluss wird kalibriert"
            Case 20 : Return "Pausiert: Fehler Düsentemperatur"
            Case 21 : Return "Pausiert: Fehler Heizbett-Temperatur"
            Case 22 : Return "Filament wird entladen"
            Case 23 : Return "Pausiert: Schrittverlust"
            Case 24 : Return "Filament wird geladen"
            Case 25 : Return "Motorgeräusch wird kalibriert"
            Case 26 : Return "Pausiert: AMS getrennt"
            Case 27 : Return "Pausiert: Hotend-Lüfter zu langsam"
            Case 28 : Return "Pausiert: Fehler Kammertemperatur"
            Case 29 : Return "Kammer kühlt ab"
            Case 30 : Return "Pausiert durch G-Code"
            Case 31 : Return "Motorgeräusch-Test"
            Case 32 : Return "Pausiert: Filament an der Düse erkannt"
            Case 33 : Return "Pausiert: Fehler Filamentschneider"
            Case 34 : Return "Pausiert: Fehler in der ersten Schicht"
            Case 35 : Return "Pausiert: Düse verstopft"
            Case Else : Return ""
        End Select
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
