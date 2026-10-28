Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Sockets
Imports System.Text
Imports Xunit

''' <summary>
''' Startet den echten Server (FilamentVerwaltung.Server.dll aus dem Build) einmal für alle Server-Tests – mit leerem
''' Datenordner und festem Token auf einem freien Port. So wird die Strecke App → HTTP → Server → Datenbank geprüft.
''' </summary>
Public Class ServerFixture
    Implements IDisposable

    Public Const Token As String = "test-token"
    Public ReadOnly Property Adresse As String
    Private ReadOnly _prozess As Process
    Private ReadOnly _ausgabe As New StringBuilder()

    Public Sub New()
        Dim port = FreierPort()
        Adresse = $"http://127.0.0.1:{port}"
        Dim dll = ServerDll()
        Dim start As New ProcessStartInfo("dotnet", $"""{dll}""") With {
            .WorkingDirectory = Path.GetDirectoryName(dll), .UseShellExecute = False,
            .RedirectStandardOutput = True, .RedirectStandardError = True}
        start.Environment("FILAMENT_DATEN") = NeuerOrdner()
        start.Environment("FILAMENT_TOKEN") = Token
        start.Environment("ASPNETCORE_URLS") = Adresse
        _prozess = Process.Start(start)
        AddHandler _prozess.OutputDataReceived, Sub(s, e) Mitschreiben(e.Data)
        AddHandler _prozess.ErrorDataReceived, Sub(s, e) Mitschreiben(e.Data)
        _prozess.BeginOutputReadLine()
        _prozess.BeginErrorReadLine()

        ' Warten, bis /api/info antwortet (höchstens 30 s)
        Using c As New HttpClient()
            For i = 1 To 60
                Try
                    If c.GetAsync(Adresse & "/api/info").Result.IsSuccessStatusCode Then Return
                Catch ex As Exception
                End Try
                If _prozess.HasExited Then Exit For
                Threading.Thread.Sleep(500)
            Next
        End Using
        Throw New InvalidOperationException("Server startet nicht:" & Environment.NewLine & _ausgabe.ToString())
    End Sub

    Private Sub Mitschreiben(zeile As String)
        SyncLock _ausgabe
            _ausgabe.AppendLine(zeile)
        End SyncLock
    End Sub

    ''' <summary>Server-Build neben dem Test-Build (gleiche Konfiguration), über die Projektmappe gefunden.</summary>
    Private Shared Function ServerDll() As String
        Dim basis = AppContext.BaseDirectory   ' …/FilamentVerwaltung.Tests/bin/<Konfiguration>/net10.0/
        Dim konfiguration = New DirectoryInfo(basis.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Parent.Name
        Dim ordner = New DirectoryInfo(basis)
        While ordner IsNot Nothing AndAlso Not File.Exists(Path.Combine(ordner.FullName, "FilamentVerwaltung.sln"))
            ordner = ordner.Parent
        End While
        If ordner Is Nothing Then Throw New InvalidOperationException("Projektmappe nicht gefunden.")
        Dim dll = Path.Combine(ordner.FullName, "FilamentVerwaltung.Server", "bin", konfiguration, "net10.0", "FilamentVerwaltung.Server.dll")
        If Not File.Exists(dll) Then Throw New FileNotFoundException("Server-Build fehlt: " & dll)
        Return dll
    End Function

    Public Shared Function FreierPort() As Integer
        Dim l As New TcpListener(IPAddress.Loopback, 0)
        l.Start()
        Dim port = CType(l.LocalEndpoint, IPEndPoint).Port
        l.Stop()
        Return port
    End Function

    Public Function Client() As ServerDatenquelle
        Return New ServerDatenquelle(New ServerZugang With {.Intern = Adresse, .Token = Token}, NeuerOrdner())
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Not _prozess.HasExited Then _prozess.Kill(True)
        Catch ex As Exception
        End Try
        _prozess.Dispose()
    End Sub
End Class

Public Class ServerTests
    Implements IClassFixture(Of ServerFixture)

    Private ReadOnly _server As ServerFixture

    Public Sub New(server As ServerFixture)
        _server = server
    End Sub

    <Fact>
    Public Sub Info_ist_ohne_Token_erreichbar_und_nennt_alle_Funktionen()
        Using c As New HttpClient()
            Dim text = c.GetStringAsync(_server.Adresse & "/api/info").Result
            For Each f In ServerFunktionen.Alle
                Assert.Contains($"""{f}""", text)
            Next
        End Using
    End Sub

    <Fact>
    Public Sub Ohne_Token_gibt_es_keine_Daten()
        Using c As New HttpClient()
            Assert.Equal(HttpStatusCode.Unauthorized, c.GetAsync(_server.Adresse & "/api/filament").Result.StatusCode)
        End Using
    End Sub

    <Fact>
    Public Sub Client_kennt_die_Funktionen_des_Servers()
        Dim db = _server.Client()
        Assert.False(String.IsNullOrEmpty(db.Verbinden()))
        For Each f In ServerFunktionen.Alle
            Assert.True(db.Kann(f), f)
        Next
    End Sub

    <Fact>
    Public Sub Buchen_ueber_den_Server_zieht_Restgewicht_ab()
        Dim db = _server.Client()
        db.Verbinden()
        Dim f As New Filament With {.Code = "SRV-" & Guid.NewGuid().ToString("N").Substring(0, 6), .Material = "PLA", .Farbe = "Rot",
                                    .Gewicht = 1000, .Restgewicht = 600, .Preis = 25D}
        db.Speichern(f)
        Dim b As New Buchung With {.FilamentId = f.Id, .Datum = Date.Now, .Art = Buchung.ArtDruck, .Gramm = 45,
                                   .Fehldruck = True, .FehldruckGrund = "Layer-Shift"}
        db.DruckSpeichern(b)
        Assert.Equal(555, db.AlleLaden().First(Function(x) x.Id = f.Id).Restgewicht)
        Dim geladen = db.BuchungenLaden().First(Function(x) x.Id = b.Id)
        Assert.Equal("Layer-Shift", geladen.FehldruckGrund)
    End Sub

    <Fact>
    Public Sub Wartung_ueber_den_Server()
        Dim db = _server.Client()
        db.Verbinden()
        Dim d As New Drucker With {.Name = "Test-" & Guid.NewGuid().ToString("N").Substring(0, 6), .Typ = Drucker.TypMoonraker, .Adresse = "127.0.0.1:1", .Aktiv = False}
        db.DruckerSpeichern(d)
        db.DrucksekundenSetzen(d.Id, 100 * 3600)
        Dim w As New Wartung With {.DruckerId = d.Id, .Name = "Riemen prüfen", .IntervallStunden = 50}
        db.WartungSpeichern(w)
        Assert.True(w.Id > 0)
        db.DrucksekundenSetzen(d.Id, 151 * 3600)
        Assert.True(db.WartungLaden().First(Function(x) x.Id = w.Id).Faellig)
        db.WartungErledigt(w.Id, "")
        Assert.False(db.WartungLaden().First(Function(x) x.Id = w.Id).Faellig)
        Assert.Contains(db.WartungProtokollLaden(), Function(p) p.WartungId = w.Id)
    End Sub

    <Fact>
    Public Sub Vorschaubild_ueber_den_Server()
        Dim db = _server.Client()
        db.Verbinden()
        Dim kennung = Guid.NewGuid().ToString("N").Substring(0, 6)
        Dim mit = db.DateiImportieren(VorschaubildTests.GcodeMitBild($"Bild-{kennung}.gcode"))
        Dim ohne = db.DateiImportieren(GcodeDatei($"OhneBild-{kennung}.gcode"))
        Assert.Equal(VorschaubildTests.Png, db.DateiVorschaubild(mit))
        Assert.Null(db.DateiVorschaubild(ohne))
    End Sub
    <Fact>
    Public Sub Ordner_ueber_den_Server()
        Dim db = _server.Client()
        db.Verbinden()
        Dim kennung = Guid.NewGuid().ToString("N").Substring(0, 6)
        Dim datei = db.DateiImportieren(GcodeDatei($"Ordner-{kennung}.gcode"))
        db.DateiOrdnerSetzen(datei.Id, $"Test {kennung}/Teile")
        db.OrdnerAnlegen($"Test {kennung}/Leer")
        Assert.Contains($"Test {kennung}/Leer", db.OrdnerLaden())
        db.OrdnerUmbenennen($"Test {kennung}", $"Neu {kennung}")
        Assert.Equal($"Neu {kennung}/Teile", db.DateienLaden().First(Function(x) x.Id = datei.Id).Ordner)
        db.OrdnerLoeschen($"Neu {kennung}/Teile")
        Assert.Equal($"Neu {kennung}", db.DateienLaden().First(Function(x) x.Id = datei.Id).Ordner)
    End Sub
    <Fact>
    Public Sub Warteschlange_ueber_den_Server_mit_hochgeladener_Datei()
        Dim db = _server.Client()
        db.Verbinden()
        Dim datei = db.DateiImportieren(GcodeDatei("Schlange-" & Guid.NewGuid().ToString("N").Substring(0, 6) & ".gcode", 12))
        Dim a As New Auftrag With {.DateiId = datei.Id, .Platte = 1}
        db.WarteschlangeSpeichern(a)
        Assert.Contains(db.WarteschlangeLaden(), Function(x) x.Id = a.Id AndAlso x.DateiName = datei.Name)
        db.WarteschlangeLoeschen(a.Id)
        Assert.DoesNotContain(db.WarteschlangeLaden(), Function(x) x.Id = a.Id)
    End Sub

End Class

''' <summary>
''' Ein "alter" Server (0.2.5, ohne Funktionsliste): Die App darf neue Funktionen gar nicht erst abfragen
''' und bei unbekannten Adressen nur einen verständlichen Hinweis zeigen – kein Absturz, kein "Fehler 404".
''' </summary>
Public Class AlterServerTests

    <Fact>
    Public Sub Neue_Funktionen_werden_bei_altem_Server_nicht_abgefragt()
        Dim port = ServerFixture.FreierPort()
        Dim anfragen As New List(Of String)
        Using listener As New HttpListener()
            listener.Prefixes.Add($"http://127.0.0.1:{port}/")
            listener.Start()
            Dim beantworten = Async Function() As Task
                                  While listener.IsListening
                                      Dim ctx As HttpListenerContext
                                      Try
                                          ctx = Await listener.GetContextAsync()
                                      Catch ex As Exception
                                          Return
                                      End Try
                                      Dim pfad = ctx.Request.Url.AbsolutePath
                                      SyncLock anfragen
                                          anfragen.Add(pfad)
                                      End SyncLock
                                      Dim antwort = If(pfad = "/api/info", "{""app"":""Filament-Verwaltung"",""version"":""0.2.5""}",
                                                    If(pfad.StartsWith("/api/einstellungen/"), "{""wert"":""""}", Nothing))
                                      If antwort Is Nothing Then
                                          ctx.Response.StatusCode = 404      ' wie ASP.NET bei unbekannter Adresse: ohne Text
                                      Else
                                          ctx.Response.ContentType = "application/json"
                                          Dim bytes = Encoding.UTF8.GetBytes(antwort)
                                          Await ctx.Response.OutputStream.WriteAsync(bytes)
                                      End If
                                      ctx.Response.Close()
                                  End While
                              End Function
            Dim aufgabe = beantworten()

            Dim db = New ServerDatenquelle(New ServerZugang With {.Intern = $"http://127.0.0.1:{port}", .Token = "x"}, NeuerOrdner())
            Assert.Equal("0.2.5", db.Verbinden())
            Assert.False(db.Kann(ServerFunktionen.Wartung))
            Assert.False(db.Kann(ServerFunktionen.Warteschlange))
            Assert.True(db.Kann(ServerFunktionen.Benachrichtigungen))
            Assert.True(db.Kann(ServerFunktionen.HomeAssistant))
            Assert.False(ServerFunktionen.Verfuegbar(db, ServerFunktionen.Wartung))
            Assert.True(ServerFunktionen.Verfuegbar(NeueDatenbank(), ServerFunktionen.Wartung))   ' lokal: alles da

            ' Wird doch etwas Unbekanntes abgefragt: verständlicher Hinweis statt "Fehler 404"
            Dim fehler = Assert.Throws(Of InvalidOperationException)(Sub() db.WartungLaden())
            Assert.Equal(ServerDatenquelle.ServerZuAlt, fehler.Message)

            listener.Stop()
        End Using
        SyncLock anfragen
            Assert.DoesNotContain("/api/warteschlange", anfragen)
        End SyncLock
    End Sub

End Class
