Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Net.Http.Json
Imports System.Text.Json

''' <summary>Zugangsdaten für den Server.</summary>
Public Class ServerZugang
    ''' <summary>Adresse im Heimnetz, z. B. http://192.168.1.10:8080 (wird zuerst versucht).</summary>
    Public Property Intern As String = ""
    ''' <summary>Adresse von unterwegs, z. B. https://filament.example.de (Cloudflare-Tunnel).</summary>
    Public Property Extern As String = ""
    Public Property Token As String = ""
    ''' <summary>Cloudflare Access Service-Token (optional).</summary>
    Public Property CloudflareId As String = ""
    Public Property CloudflareGeheimnis As String = ""
End Class

''' <summary>
''' Daten über die REST-API des Servers. Die Aufrufe sind synchron (wie bei der lokalen Datenbank) und nutzen
''' HttpClient.Send – damit gibt es im UI-Thread keine Verklemmung.
''' Dateien werden für Vorschau, Slicer und Senden in einen lokalen Zwischenspeicher geladen.
''' </summary>
Public Class ServerDatenquelle
    Implements IDatenquelle, IDisposable

    Private Shared ReadOnly Json As New JsonSerializerOptions(JsonSerializerDefaults.Web)

    Private ReadOnly _zugang As ServerZugang
    Private ReadOnly _client As HttpClient
    Private ReadOnly _zwischenspeicher As String
    Private _basis As String = ""

    Public Sub New(zugang As ServerZugang, zwischenspeicher As String)
        _zugang = zugang
        _zwischenspeicher = zwischenspeicher
        ' Keine Weiterleitungen folgen: Cloudflare Access leitet ohne gültiges Service-Token auf eine Anmeldeseite um
        _client = New HttpClient(New SocketsHttpHandler With {.AllowAutoRedirect = False, .ConnectTimeout = TimeSpan.FromSeconds(5)}) With {
            .Timeout = TimeSpan.FromMinutes(5)}
        ' Ohne User-Agent blockiert Cloudflare (Browser Integrity Check / Bot-Schutz) die Anfragen mit 403
        Dim version = GetType(ServerDatenquelle).Assembly.GetName().Version
        _client.DefaultRequestHeaders.UserAgent.Add(New ProductInfoHeaderValue("FilamentVerwaltung", If(version?.ToString(3), "1.0")))
        _client.DefaultRequestHeaders.Authorization = New AuthenticationHeaderValue("Bearer", zugang.Token.Trim())
        If zugang.CloudflareId.Trim().Length > 0 Then
            _client.DefaultRequestHeaders.Add("CF-Access-Client-Id", zugang.CloudflareId.Trim())
            _client.DefaultRequestHeaders.Add("CF-Access-Client-Secret", zugang.CloudflareGeheimnis.Trim())
        End If
    End Sub

    ''' <summary>Die gerade benutzte Adresse (intern oder extern).</summary>
    Public ReadOnly Property Basis As String
        Get
            Return _basis
        End Get
    End Property

    Public ReadOnly Property Ort As String Implements IDatenquelle.Ort
        Get
            Return "Server: " & _basis
        End Get
    End Property

    ' ------------------------------------------------------------------ Verbindung

    Private Function Adressen() As List(Of String)
        Return {_zugang.Intern, _zugang.Extern}.Select(AddressOf Normalisieren).Where(Function(a) a.Length > 0).Distinct().ToList()
    End Function

    Private Shared Function Normalisieren(adresse As String) As String
        Dim a = If(adresse, "").Trim().TrimEnd("/"c)
        If a.Length = 0 Then Return ""
        If Not a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) AndAlso
           Not a.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then a = "http://" & a
        Return a
    End Function

    ''' <summary>
    ''' Sucht eine erreichbare Adresse (zuerst intern, dann extern) und prüft das Token.
    ''' Gibt die Server-Version zurück; wirft eine verständliche Meldung, wenn es nicht klappt.
    ''' </summary>
    Public Function Verbinden() As String
        Dim adressen = Me.Adressen()
        If adressen.Count = 0 Then Throw New InvalidOperationException("Es ist keine Server-Adresse eingetragen.")
        Dim fehler As New List(Of String)
        For Each a In adressen
            Try
                Dim info = InfoAbrufen(a)
                _basis = a
                ' Token prüfen (info ist ohne Token erreichbar)
                EinstellungLesen(DesignSchluessel)
                Return info.Version
            Catch ex As Exception When TypeOf ex Is HttpRequestException OrElse TypeOf ex Is TaskCanceledException
                fehler.Add($"{a}: {If(TypeOf ex Is TaskCanceledException, "keine Antwort", KurzeMeldung(ex))}")
            End Try
        Next
        Throw New InvalidOperationException("Der Server ist nicht erreichbar." & Environment.NewLine & String.Join(Environment.NewLine, fehler))
    End Function

    ''' <summary>
    ''' Kurze Prüfung für die Statusanzeige: antwortet der Server unter der aktuellen Adresse?
    ''' Sonst wird über <see cref="Verbinden"/> die andere Adresse versucht. Wirft keine Ausnahme.
    ''' </summary>
    Public Function Erreichbar() As Boolean
        Try
            If _basis.Length > 0 Then
                InfoAbrufen(_basis)
                Return True
            End If
        Catch ex As Exception
            ' aktuelle Adresse antwortet nicht – unten die andere versuchen
        End Try
        Try
            Verbinden()
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

    ''' <summary>Schlüssel der Design-Einstellung (für die Token-Prüfung; der Wert selbst ist egal).</summary>
    Private Const DesignSchluessel As String = "Design"

    Private Function InfoAbrufen(adresse As String) As ServerInfo
        Using anfrage As New HttpRequestMessage(HttpMethod.Get, adresse & "/api/info"),
              zeit As New Threading.CancellationTokenSource(TimeSpan.FromSeconds(If(adresse = Normalisieren(_zugang.Intern), 3, 15)))
            Using antwort = _client.Send(anfrage, zeit.Token)
                Pruefen(antwort)
                Dim info = Lesen(Of ServerInfo)(antwort)
                If info Is Nothing OrElse info.App <> "Filament-Verwaltung" Then
                    Throw New HttpRequestException("Unter dieser Adresse antwortet kein Filament-Verwaltung-Server.")
                End If
                Return info
            End Using
        End Using
    End Function

    Private Shared Function KurzeMeldung(ex As Exception) As String
        Dim innen = ex
        While innen.InnerException IsNot Nothing
            innen = innen.InnerException
        End While
        Return innen.Message
    End Function

    ''' <summary>
    ''' Schickt eine Anfrage. Scheitert schon der Verbindungsaufbau (Anfrage hat den Server nicht erreicht),
    ''' wird einmal die andere Adresse versucht – z. B. nach dem Wechsel vom Heimnetz nach unterwegs.
    ''' </summary>
    Private Function Senden(methode As HttpMethod, pfad As String, Optional inhalt As Func(Of HttpContent) = Nothing) As HttpResponseMessage
        If _basis.Length = 0 Then Verbinden()
        Try
            Return EinmalSenden(_basis, methode, pfad, inhalt)
        Catch ex As HttpRequestException When ex.HttpRequestError = HttpRequestError.ConnectionError OrElse
                                              ex.HttpRequestError = HttpRequestError.NameResolutionError
            Dim andere = Adressen().FirstOrDefault(Function(a) a <> _basis)
            If andere Is Nothing Then Throw New HttpRequestException("Der Server ist nicht erreichbar: " & KurzeMeldung(ex), ex)
            Dim antwort = EinmalSenden(andere, methode, pfad, inhalt)
            _basis = andere
            Return antwort
        End Try
    End Function

    Private Function EinmalSenden(basis As String, methode As HttpMethod, pfad As String, inhalt As Func(Of HttpContent)) As HttpResponseMessage
        Using anfrage As New HttpRequestMessage(methode, basis & "/api/" & pfad)
            If inhalt IsNot Nothing Then anfrage.Content = inhalt()
            Dim antwort = _client.Send(anfrage)
            Try
                Pruefen(antwort)
            Catch
                antwort.Dispose()
                Throw
            End Try
            Return antwort
        End Using
    End Function

    ''' <summary>Fehlerantworten in verständliche Meldungen übersetzen.</summary>
    Private Shared Sub Pruefen(antwort As HttpResponseMessage)
        If antwort.IsSuccessStatusCode Then Return
        Dim code = CInt(antwort.StatusCode)
        If code >= 300 AndAlso code < 400 Then
            Throw New InvalidOperationException("Der Server leitet auf eine Anmeldeseite um – vermutlich Cloudflare Access. " &
                                                "Service-Token (Client-ID und Secret) in der Verbindung prüfen.")
        End If
        If antwort.StatusCode = HttpStatusCode.Unauthorized Then
            Throw New UnauthorizedAccessException("Der Server hat das Zugangs-Token abgelehnt (steht in token.txt im Datenordner des Servers).")
        End If
        Dim text = ""
        Try
            text = antwort.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        Catch
        End Try
        If antwort.StatusCode = HttpStatusCode.Forbidden Then
            ' Cloudflare nennt in der Fehlerseite einen Code, z. B. "error code: 1010" (Bot-Schutz) oder 1020 (Firewall-Regel)
            Dim cf = System.Text.RegularExpressions.Regex.Match(text, "error code:?\s*(\d{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            Throw New UnauthorizedAccessException("Zugriff verweigert (403" & If(cf.Success, ", Cloudflare-Fehler " & cf.Groups(1).Value, "") & "). " &
                                                  "Bei Cloudflare Access Client-ID und Secret prüfen und ob die Policy auf ""Service Auth"" steht" &
                                                  If(cf.Success, "; bei 1010/1020 greift der Bot-Schutz bzw. eine Firewall-Regel von Cloudflare.", "."))
        End If
        Dim istHtml = If(antwort.Content.Headers.ContentType?.MediaType, "").Contains("html")
        If istHtml OrElse text.Trim().Length = 0 Then text = $"Der Server meldet Fehler {code} ({antwort.ReasonPhrase})."
        Throw New InvalidOperationException(text.Trim())
    End Sub

    Private Shared Function Lesen(Of T)(antwort As HttpResponseMessage) As T
        Using s = antwort.Content.ReadAsStream()
            Return JsonSerializer.Deserialize(Of T)(s, Json)
        End Using
    End Function

    Private Function Abrufen(Of T)(pfad As String) As T
        Using antwort = Senden(HttpMethod.Get, pfad)
            Return Lesen(Of T)(antwort)
        End Using
    End Function

    Private Function Schicken(Of T)(methode As HttpMethod, pfad As String, daten As Object) As T
        Using antwort = Senden(methode, pfad, Function() JsonContent.Create(daten, daten.GetType(), options:=Json))
            Return Lesen(Of T)(antwort)
        End Using
    End Function

    Private Sub Schicken(methode As HttpMethod, pfad As String, Optional daten As Object = Nothing)
        Dim inhalt As Func(Of HttpContent) = If(daten Is Nothing, Nothing, Function() JsonContent.Create(daten, daten.GetType(), options:=Json))
        Using antwort = Senden(methode, pfad, inhalt)
        End Using
    End Sub

    Private Shared Function Q(wert As String) As String
        Return Uri.EscapeDataString(If(wert, ""))
    End Function

    ' ------------------------------------------------------------------ Einstellungen

    Public Function EinstellungLesen(schluessel As String) As String Implements IDatenquelle.EinstellungLesen
        Return Abrufen(Of WertAnfrage)("einstellungen/" & Q(schluessel))?.Wert
    End Function

    Public Sub EinstellungSpeichern(schluessel As String, wert As String) Implements IDatenquelle.EinstellungSpeichern
        Schicken(HttpMethod.Put, "einstellungen/" & Q(schluessel), New WertAnfrage With {.Wert = wert})
    End Sub

    Public Function LeerspulenVorlage(hersteller As String, ByRef eigenerWert As Boolean) As Integer? Implements IDatenquelle.LeerspulenVorlage
        eigenerWert = False
        If String.IsNullOrWhiteSpace(hersteller) Then Return Nothing
        Dim a = Abrufen(Of LeerspulenAntwort)("leerspulen?hersteller=" & Q(hersteller.Trim()))
        eigenerWert = a.EigenerWert
        Return a.Gewicht
    End Function

    ' ------------------------------------------------------------------ Filament

    Public Function AlleLaden() As List(Of Filament) Implements IDatenquelle.AlleLaden
        Return Abrufen(Of List(Of Filament))("filament")
    End Function

    Public Sub Speichern(f As Filament) Implements IDatenquelle.Speichern
        f.Id = Schicken(Of Filament)(HttpMethod.Post, "filament", f).Id
    End Sub

    Public Sub Loeschen(id As Long) Implements IDatenquelle.Loeschen
        Schicken(HttpMethod.Delete, $"filament/{id}")
    End Sub

    Public Function CodeExistiert(code As String, ausserId As Long) As Boolean Implements IDatenquelle.CodeExistiert
        Return Abrufen(Of Boolean)($"filament/code-existiert?code={Q(code.Trim())}&ausserId={ausserId}")
    End Function

    Public Function NaechsterCode() As String Implements IDatenquelle.NaechsterCode
        Return Abrufen(Of WertAnfrage)("filament/naechster-code").Wert
    End Function

    ' ------------------------------------------------------------------ Buchungen

    Public Sub RestgewichtBuchen(filamentId As Long, neuesRestgewicht As Integer, art As String, bezeichnung As String) Implements IDatenquelle.RestgewichtBuchen
        Schicken(HttpMethod.Post, $"filament/{filamentId}/restgewicht",
                 New RestgewichtAnfrage With {.NeuesRestgewicht = neuesRestgewicht, .Art = art, .Bezeichnung = If(bezeichnung, "")})
    End Sub

    Public Sub DruckSpeichern(b As Buchung) Implements IDatenquelle.DruckSpeichern
        Dim gespeichert = Schicken(Of Buchung)(HttpMethod.Post, "buchungen/druck", b)
        b.Id = gespeichert.Id
        b.Kosten = gespeichert.Kosten
    End Sub

    Public Sub BuchungLoeschen(id As Long, zurueckbuchenJa As Boolean) Implements IDatenquelle.BuchungLoeschen
        Schicken(HttpMethod.Delete, $"buchungen/{id}?zurueckbuchen={zurueckbuchenJa.ToString().ToLowerInvariant()}")
    End Sub

    Public Function BuchungenLaden() As List(Of Buchung) Implements IDatenquelle.BuchungenLaden
        Return Abrufen(Of List(Of Buchung))("buchungen")
    End Function

    ' ------------------------------------------------------------------ Drucker

    Public Function DruckerLaden() As List(Of Drucker) Implements IDatenquelle.DruckerLaden
        Return Abrufen(Of List(Of Drucker))("drucker")
    End Function

    Public Sub DruckerSpeichern(d As Drucker) Implements IDatenquelle.DruckerSpeichern
        d.Id = Schicken(Of Drucker)(HttpMethod.Post, "drucker", d).Id
    End Sub

    Public Sub DruckerLoeschen(id As Long) Implements IDatenquelle.DruckerLoeschen
        Schicken(HttpMethod.Delete, $"drucker/{id}")
    End Sub

    Public Function SpulenZuordnung(druckerId As Long) As Dictionary(Of Integer, Long) Implements IDatenquelle.SpulenZuordnung
        Return Abrufen(Of Dictionary(Of Integer, Long))($"drucker/{druckerId}/spulen")
    End Function

    Public Sub SpuleZuordnen(druckerId As Long, slot As Integer, filamentId As Long?) Implements IDatenquelle.SpuleZuordnen
        Schicken(HttpMethod.Put, $"drucker/{druckerId}/spulen/{slot}", New SpuleAnfrage With {.FilamentId = filamentId})
    End Sub

    ' ------------------------------------------------------------------ Dateien

    Public Function DateienLaden() As List(Of Datei) Implements IDatenquelle.DateienLaden
        Return Abrufen(Of List(Of Datei))("dateien")
    End Function

    Public Sub DateiNotizSpeichern(id As Long, notiz As String) Implements IDatenquelle.DateiNotizSpeichern
        Schicken(HttpMethod.Put, $"dateien/{id}/notiz", New WertAnfrage With {.Wert = If(notiz, "")})
    End Sub

    Public Sub DateiLoeschen(d As Datei) Implements IDatenquelle.DateiLoeschen
        Schicken(HttpMethod.Delete, $"dateien/{d.Id}")
        Try
            Dim kopie = KopiePfad(d)
            If File.Exists(kopie) Then File.Delete(kopie)
        Catch ex As IOException
            ' Kopie ist gerade geöffnet – bleibt im Zwischenspeicher liegen
        End Try
    End Sub

    ''' <summary>Zwischenspeicher der heruntergeladenen Dateien.</summary>
    Public ReadOnly Property DateienOrdner As String Implements IDatenquelle.DateienOrdner
        Get
            Return _zwischenspeicher
        End Get
    End Property

    Private Function KopiePfad(d As Datei) As String
        Return Path.Combine(_zwischenspeicher, $"{d.Id}_{Path.GetFileName(d.Pfad)}")
    End Function

    ''' <summary>Lokale Kopie der Datei; wird beim ersten Zugriff heruntergeladen (Dateien ändern sich nicht).</summary>
    Public Function DateiPfad(d As Datei) As String Implements IDatenquelle.DateiPfad
        Return Herunterladen(d, Nothing, Threading.CancellationToken.None)
    End Function

    Public Function DateiVorhanden(d As Datei) As Boolean Implements IDatenquelle.DateiVorhanden
        Dim ziel = KopiePfad(d)
        Return File.Exists(ziel) AndAlso New FileInfo(ziel).Length = d.Groesse
    End Function

    Public Function DateiLadenAsync(d As Datei, fortschritt As IProgress(Of Double), abbruch As Threading.CancellationToken) As Task(Of String) Implements IDatenquelle.DateiLadenAsync
        If DateiVorhanden(d) Then Return Task.FromResult(KopiePfad(d))
        Return Task.Run(Function() Herunterladen(d, fortschritt, abbruch), abbruch)
    End Function

    Private Function Herunterladen(d As Datei, fortschritt As IProgress(Of Double), abbruch As Threading.CancellationToken) As String
        Dim ziel = KopiePfad(d)
        If DateiVorhanden(d) Then Return ziel
        Directory.CreateDirectory(_zwischenspeicher)
        Dim temp = ziel & "." & Guid.NewGuid().ToString("N").Substring(0, 6) & ".laden"
        Try
            Using antwort = Senden(HttpMethod.Get, $"dateien/{d.Id}/inhalt"),
                  quelle = antwort.Content.ReadAsStream(),
                  datei As New FileStream(temp, FileMode.Create, FileAccess.Write)
                Dim puffer(81919) As Byte
                Dim gesamt As Long = 0
                Do
                    abbruch.ThrowIfCancellationRequested()
                    Dim n = quelle.Read(puffer, 0, puffer.Length)
                    If n = 0 Then Exit Do
                    datei.Write(puffer, 0, n)
                    gesamt += n
                    If d.Groesse > 0 Then fortschritt?.Report(Math.Min(1.0, gesamt / CDbl(d.Groesse)))
                Loop
            End Using
            File.Move(temp, ziel, overwrite:=True)
        Finally
            If File.Exists(temp) Then File.Delete(temp)
        End Try
        Return ziel
    End Function

    ''' <summary>Lädt die Datei in Stücken hoch (Cloudflare erlaubt höchstens 100 MB pro Anfrage).</summary>
    Public Function DateiImportieren(quelle As String) As Datei Implements IDatenquelle.DateiImportieren
        Dim id = Schicken(Of UploadAntwort)(HttpMethod.Post, "uploads", New Object()).Id
        Using s As New FileStream(quelle, FileMode.Open, FileAccess.Read, FileShare.Read)
            Dim puffer(Api.UploadStueckBytes - 1) As Byte
            Dim offset As Long = 0
            Do
                Dim n = s.ReadAtLeast(puffer, puffer.Length, throwOnEndOfStream:=False)
                If n = 0 AndAlso offset > 0 Then Exit Do
                Dim stueck = puffer
                Dim laenge = n
                Using antwort = Senden(HttpMethod.Put, $"uploads/{id}?offset={offset}",
                                       Function()
                                           Dim inhalt As New ByteArrayContent(stueck, 0, laenge)
                                           inhalt.Headers.ContentType = New MediaTypeHeaderValue("application/octet-stream")
                                           Return inhalt
                                       End Function)
                End Using
                offset += n
                If n < puffer.Length Then Exit Do
            Loop
        End Using
        Dim d = Schicken(Of Datei)(HttpMethod.Post, $"uploads/{id}/fertig", New UploadFertigAnfrage With {.Name = Path.GetFileName(quelle)})
        ' Die eigene Datei gleich in den Zwischenspeicher legen – spart das Herunterladen für die Vorschau
        Try
            Directory.CreateDirectory(_zwischenspeicher)
            File.Copy(quelle, KopiePfad(d), overwrite:=True)
        Catch ex As IOException
        End Try
        Return d
    End Function

    ' ------------------------------------------------------------------ Sicherungen

    Public Function SicherungenLaden() As List(Of SicherungInfo) Implements IDatenquelle.SicherungenLaden
        Return Abrufen(Of List(Of SicherungInfo))("sicherungen")
    End Function

    Public Function SicherungErstellen(art As String) As SicherungInfo Implements IDatenquelle.SicherungErstellen
        Return Schicken(Of SicherungInfo)(HttpMethod.Post, "sicherungen", New SicherungAnfrage With {.Art = art})
    End Function

    Public Sub SicherungWiederherstellen(s As SicherungInfo) Implements IDatenquelle.SicherungWiederherstellen
        Schicken(HttpMethod.Post, "sicherungen/wiederherstellen", New SicherungAnfrage With {.Name = s.Name})
    End Sub

    ' ------------------------------------------------------------------ Drucker live (vom Server überwacht)

    Public Function DruckerLiveLaden() As List(Of DruckerLive)
        Return Abrufen(Of List(Of DruckerLive))("drucker/live")
    End Function

    Public Function DruckerProtokollText(druckerId As Long) As String
        Return Abrufen(Of WertAnfrage)($"drucker/{druckerId}/protokoll")?.Wert
    End Function

    ''' <summary>Startet das Senden auf dem Server; liefert die Nummer des Auftrags für <see cref="SendeAuftrag"/>.</summary>
    Public Function SendenStarten(druckerId As Long, dateiId As Long, optionen As SendeOptionen) As String
        Return Schicken(Of SendeAuftragInfo)(HttpMethod.Post, $"drucker/{druckerId}/senden",
                                             New SendenAnfrage With {.DateiId = dateiId, .Optionen = optionen}).Id
    End Function

    Public Function SendeAuftrag(id As String) As SendeAuftragInfo
        Return Abrufen(Of SendeAuftragInfo)("sendeauftraege/" & Q(id))
    End Function

    Public Sub SendenAbbrechen(id As String)
        Schicken(HttpMethod.Delete, "sendeauftraege/" & Q(id))
    End Sub

    Public Function DruckEndenLaden() As List(Of DruckEnde)
        Return Abrufen(Of List(Of DruckEnde))("druckenden")
    End Function

    Public Sub DruckEndeErledigt(id As Long)
        Schicken(HttpMethod.Delete, $"druckenden/{id}")
    End Sub

    ''' <summary>Beansprucht das Druckende zum Buchen; False, wenn es schon weg ist (anderer PC war schneller).</summary>
    Public Function DruckEndeUebernehmen(id As Long) As Boolean
        Return Schicken(Of WertAnfrage)(HttpMethod.Post, $"druckenden/{id}/uebernehmen", New Object()).Wert = "1"
    End Function

    ''' <summary>Legt ein Druckende wieder offen (Buchen abgebrochen); liefert die neue Nummer.</summary>
    Public Function DruckEndeZuruecklegen(ende As DruckEnde) As Long
        Return Schicken(Of DruckEnde)(HttpMethod.Post, "druckenden", ende).Id
    End Function

    ''' <summary>Verbindungstest vom Server aus (der Server spricht mit dem Drucker).</summary>
    Public Function DruckerTesten(d As Drucker) As DruckerStatus
        Return Schicken(Of DruckerStatus)(HttpMethod.Post, "drucker/testen", d)
    End Function

    ''' <summary>Neuestes Kamerabild; ohne Bild (noch keins da oder Fehler) steht im Status, warum.</summary>
    Public Function KameraBild(druckerId As Long) As (Bild As Byte(), Status As String)
        Using antwort = Senden(HttpMethod.Get, $"drucker/{druckerId}/kamera")
            If antwort.StatusCode = HttpStatusCode.NoContent Then
                Dim status As IEnumerable(Of String) = Nothing
                Dim text = If(antwort.Headers.TryGetValues("X-Kamera-Status", status), Uri.UnescapeDataString(status.First()), "Warte auf Bild …")
                Return (Nothing, text)
            End If
            Using s = antwort.Content.ReadAsStream(), ms As New MemoryStream()
                s.CopyTo(ms)
                Return (ms.ToArray(), "")
            End Using
        End Using
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _client.Dispose()
    End Sub

End Class
