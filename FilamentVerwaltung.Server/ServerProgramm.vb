Imports System.IO
Imports System.Security.Cryptography
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging

''' <summary>Einstellungen des Servers (aus Umgebungsvariablen).</summary>
Public Class ServerOptionen
    ''' <summary>Ordner für Datenbank, Dateien und Sicherungen (FILAMENT_DATEN, Standard /data).</summary>
    Public Property DatenOrdner As String = ""
    ''' <summary>Zugangs-Token, das jeder Client im Header "Authorization: Bearer …" mitschicken muss.</summary>
    Public Property Token As String = ""

    Public ReadOnly Property UploadOrdner As String
        Get
            Return Path.Combine(DatenOrdner, "tmp", "uploads")
        End Get
    End Property
End Class

''' <summary>
''' Einstiegspunkt des Servers.
''' Umgebungsvariablen: FILAMENT_DATEN (Datenordner), FILAMENT_TOKEN (Zugangs-Token; fehlt es, wird eines erzeugt
''' und in token.txt im Datenordner gespeichert), ASPNETCORE_HTTP_PORTS (Standard 8080).
''' </summary>
Public Module ServerProgramm

    Public Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        Dim optionen As New ServerOptionen With {
            .DatenOrdner = Path.GetFullPath(If(NichtLeer(Environment.GetEnvironmentVariable("FILAMENT_DATEN")),
                                               If(OperatingSystem.IsWindows(), Path.Combine(AppContext.BaseDirectory, "daten"), "/data")))
        }
        Directory.CreateDirectory(optionen.DatenOrdner)
        optionen.Token = TokenErmitteln(optionen.DatenOrdner)

        If NichtLeer(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) Is Nothing AndAlso
           NichtLeer(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")) Is Nothing Then
            builder.WebHost.UseUrls("http://0.0.0.0:8080")
        End If

        Dim db As New Datenbank(Path.Combine(optionen.DatenOrdner, "filament.db"))
        builder.Services.AddSingleton(optionen)
        builder.Services.AddSingleton(db)
        builder.Services.AddHostedService(Of AutomatischeSicherung)()
        ' Drucker: Überwachung, Kamera, Sendeaufträge
        Dim ueberwachung As New DruckerUeberwachung(db)
        builder.Services.AddSingleton(ueberwachung)
        builder.Services.AddSingleton(New KameraVerwaltung(ueberwachung))
        builder.Services.AddSingleton(New SendeAuftraege())
        builder.Services.AddHostedService(Of DruckerStart)()
        builder.Services.AddSingleton(Of PushDienst)()
        builder.Services.AddControllers()

        ' Abgebrochene Uploads vom letzten Lauf entfernen
        Try
            If Directory.Exists(optionen.UploadOrdner) Then Directory.Delete(optionen.UploadOrdner, True)
        Catch ex As Exception
        End Try

        Dim app = builder.Build()
        app.UseMiddleware(Of FehlerAlsText)()
        app.UseMiddleware(Of TokenPruefung)()
        ' Webseite fürs Handy (wwwroot) – ohne Daten, daher ohne Token. Die Startseite kommt mit Versionskennung an
        ' app.js/app.css (neue Adresse nach jedem Update, egal was Browser oder Cloudflare zwischenspeichern).
        app.UseMiddleware(Of StartseiteMitVersion)()
        app.UseStaticFiles(New StaticFileOptions With {
            .OnPrepareResponse = Sub(c) c.Context.Response.Headers.CacheControl = "no-cache"})
        app.MapControllers()

        app.Logger.LogInformation("Filament-Verwaltung Server {Version} – Daten in {Ordner}", ServerVersion(), optionen.DatenOrdner)
        app.Run()
    End Sub

    Public Function ServerVersion() As String
        Dim v = GetType(ServerProgramm).Assembly.GetName().Version
        Return If(v Is Nothing, "?", $"{v.Major}.{v.Minor}.{v.Build}")
    End Function

    Private Function NichtLeer(wert As String) As String
        Return If(String.IsNullOrWhiteSpace(wert), Nothing, wert.Trim())
    End Function

    ''' <summary>Token aus FILAMENT_TOKEN, sonst aus token.txt, sonst neu erzeugt und dort gespeichert.</summary>
    Private Function TokenErmitteln(datenOrdner As String) As String
        Dim ausUmgebung = NichtLeer(Environment.GetEnvironmentVariable("FILAMENT_TOKEN"))
        If ausUmgebung IsNot Nothing Then Return ausUmgebung
        Dim datei = Path.Combine(datenOrdner, "token.txt")
        If File.Exists(datei) Then
            Dim gespeichert = NichtLeer(File.ReadAllText(datei))
            If gespeichert IsNot Nothing Then Return gespeichert
        End If
        Dim neu = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace("+", "-").Replace("/", "_").TrimEnd("="c)
        File.WriteAllText(datei, neu)
        System.Console.WriteLine($"Neues Zugangs-Token erzeugt (steht in {datei}): {neu}")
        Return neu
    End Function

End Module

''' <summary>Lässt API-Anfragen nur mit gültigem Token durch ("Authorization: Bearer &lt;Token&gt;"); /api/info und die Webseite sind frei.</summary>
Public Class TokenPruefung

    Private ReadOnly _weiter As RequestDelegate
    Private ReadOnly _erwartet As Byte()

    Public Sub New(weiter As RequestDelegate, optionen As ServerOptionen)
        _weiter = weiter
        _erwartet = Text.Encoding.UTF8.GetBytes(optionen.Token)
    End Sub

    Public Function InvokeAsync(ctx As HttpContext) As Task
        If Not ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) OrElse
           ctx.Request.Path.Equals("/api/info", StringComparison.OrdinalIgnoreCase) Then Return _weiter(ctx)
        Dim kopf = ctx.Request.Headers.Authorization.ToString()
        Dim gesendet = If(kopf.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase), kopf.Substring(7).Trim(), "")
        If Not CryptographicOperations.FixedTimeEquals(Text.Encoding.UTF8.GetBytes(gesendet), _erwartet) Then
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized
            Return ctx.Response.WriteAsync("Zugangs-Token fehlt oder ist falsch.")
        End If
        Return _weiter(ctx)
    End Function

End Class

''' <summary>Einmal pro Tag eine Sicherung der Datenbank (wie die App beim Start).</summary>
Public Class AutomatischeSicherung
    Inherits BackgroundService

    Private ReadOnly _db As Datenbank
    Private ReadOnly _log As ILogger(Of AutomatischeSicherung)

    Public Sub New(db As Datenbank, log As ILogger(Of AutomatischeSicherung))
        _db = db
        _log = log
    End Sub

    Protected Overrides Async Function ExecuteAsync(abbruch As Threading.CancellationToken) As Task
        While Not abbruch.IsCancellationRequested
            Try
                Dim neu = Sicherung.AutomatischSichern(_db)
                If neu IsNot Nothing Then _log.LogInformation("Sicherung angelegt: {Name}", neu.Name)
            Catch ex As Exception
                _log.LogWarning(ex, "Automatische Sicherung fehlgeschlagen")
            End Try
            Try
                Await Task.Delay(TimeSpan.FromHours(1), abbruch)
            Catch ex As OperationCanceledException
                Return
            End Try
        End While
    End Function

End Class

''' <summary>Fehler aus der Datenbank als Klartext (Status 500) zurückgeben, damit die App die Meldung anzeigen kann.</summary>
Public Class FehlerAlsText

    Private ReadOnly _weiter As RequestDelegate
    Private ReadOnly _log As ILogger(Of FehlerAlsText)

    Public Sub New(weiter As RequestDelegate, log As ILogger(Of FehlerAlsText))
        _weiter = weiter
        _log = log
    End Sub

    Public Async Function InvokeAsync(ctx As HttpContext) As Task
        Dim fehler As Exception = Nothing
        Try
            Await _weiter(ctx)
        Catch ex As Exception When Not ctx.Response.HasStarted
            fehler = ex
        End Try
        If fehler Is Nothing Then Return
        _log.LogWarning(fehler, "Fehler bei {Methode} {Pfad}", ctx.Request.Method, ctx.Request.Path)
        ctx.Response.Clear()
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError
        ctx.Response.ContentType = "text/plain; charset=utf-8"
        Await ctx.Response.WriteAsync(fehler.Message)
    End Function

End Class
''' <summary>
''' Liefert index.html aus und hängt an app.js und app.css eine Kennung aus ihrem Inhalt an (?v=…).
''' So holt jedes Gerät nach einem Update sicher die neuen Dateien; die Startseite selbst wird nie zwischengespeichert.
''' </summary>
Public Class StartseiteMitVersion

    Private ReadOnly _weiter As RequestDelegate
    Private ReadOnly _html As String

    Public Sub New(weiter As RequestDelegate, umgebung As IWebHostEnvironment)
        _weiter = weiter
        Dim wurzel = If(umgebung.WebRootPath, "")
        Dim index = Path.Combine(wurzel, "index.html")
        If Not File.Exists(index) Then Return
        Dim html = File.ReadAllText(index)
        For Each name In {"app.js", "app.css"}
            Dim datei = Path.Combine(wurzel, name)
            If Not File.Exists(datei) Then Continue For
            Dim kennung = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(datei))).Substring(0, 10).ToLowerInvariant()
            html = html.Replace($"""{name}""", $"""{name}?v={kennung}""")
        Next
        _html = html
    End Sub

    Public Function InvokeAsync(ctx As HttpContext) As Task
        Dim pfad = ctx.Request.Path.Value
        If _html Is Nothing OrElse Not HttpMethods.IsGet(ctx.Request.Method) OrElse
           Not (pfad = "/" OrElse String.Equals(pfad, "/index.html", StringComparison.OrdinalIgnoreCase)) Then Return _weiter(ctx)
        ctx.Response.ContentType = "text/html; charset=utf-8"
        ctx.Response.Headers.CacheControl = "no-store"
        Return ctx.Response.WriteAsync(_html)
    End Function

End Class