Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Reflection
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Threading

''' <summary>Ein auf GitHub veröffentlichtes Release mit neuerer Version.</summary>
Public Class UpdateInfo
    Public Property Version As Version
    Public Property Hinweise As String = ""
    Public Property ReleaseSeite As String = ""
    Public Property DownloadUrl As String = ""
    Public Property Groesse As Long
    ''' <summary>SHA-256 der EXE laut GitHub (leer, wenn GitHub keinen Wert liefert).</summary>
    Public Property Sha256 As String = ""
End Class

''' <summary>
''' Sucht auf GitHub Releases nach neuen Versionen und tauscht die laufende EXE aus.
''' Ablauf: neue EXE neben die alte laden und prüfen → laufende EXE in *.alt umbenennen
''' (unter Windows erlaubt, Überschreiben nicht) → neue EXE an ihre Stelle → neu starten.
''' Die *.alt-Datei wird beim nächsten Start entfernt.
''' </summary>
Public Module Updater

    ''' <summary>GitHub-Repository im Format "Besitzer/Name". Releases brauchen ein Tag wie "v1.2.0".</summary>
    Public Const GitHubRepo As String = "Owneasy/FilamentVerwaltung"

    ''' <summary>Name der EXE, die am Release hängen muss (wird von release.ps1 so erzeugt).</summary>
    Public Const AssetName As String = "FilamentVerwaltung.exe"

    ''' <summary>
    ''' Adresse der Release-Abfrage. Die Umgebungsvariable dient nur zum Testen des Update-Ablaufs
    ''' mit einem lokalen Server; ohne sie wird immer GitHub gefragt.
    ''' </summary>
    Private ReadOnly Property ReleaseApi As String
        Get
            Dim test = Environment.GetEnvironmentVariable("FILAMENTVERWALTUNG_UPDATE_API")
            Return If(String.IsNullOrWhiteSpace(test), $"https://api.github.com/repos/{GitHubRepo}/releases/latest", test)
        End Get
    End Property

    Private Const EndungNeu As String = ".neu"
    Private Const EndungAlt As String = ".alt"

    Public ReadOnly Property AktuelleVersion As Version
        Get
            Dim v = If(Assembly.GetEntryAssembly()?.GetName().Version, New Version(0, 0, 0))
            Return New Version(v.Major, v.Minor, Math.Max(0, v.Build))
        End Get
    End Property

    ''' <summary>
    ''' Nur die veröffentlichte Einzel-EXE kann sich selbst ersetzen. Beim Start aus Visual Studio
    ''' (DLL + EXE im bin-Ordner) wird nur geprüft, aber nicht installiert.
    ''' </summary>
    Public ReadOnly Property KannInstallieren As Boolean
        Get
            Return String.IsNullOrEmpty(GetType(UpdateInfo).Assembly.Location) AndAlso
                   Not String.IsNullOrEmpty(Environment.ProcessPath)
        End Get
    End Property

    Private Function NeuerClient(timeout As TimeSpan) As HttpClient
        Dim c As New HttpClient With {.Timeout = timeout}
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"FilamentVerwaltung/{AktuelleVersion}")
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json")
        Return c
    End Function

    ''' <summary>Liefert das neueste Release, wenn es neuer als die laufende Version ist, sonst Nothing.</summary>
    Public Async Function PruefenAsync() As Task(Of UpdateInfo)
        Using client = NeuerClient(TimeSpan.FromSeconds(15))
            Using antwort = Await client.GetAsync(ReleaseApi)
                If antwort.StatusCode = HttpStatusCode.NotFound Then
                    ' Repository existiert nicht, ist privat oder hat noch kein Release
                    Throw New InvalidOperationException(
                        $"Auf GitHub wurde kein Release gefunden ({GitHubRepo}). " &
                        "Entweder wurde noch keine Version veröffentlicht oder das Repository ist privat.")
                End If
                antwort.EnsureSuccessStatusCode()
                Using json = JsonDocument.Parse(Await antwort.Content.ReadAsStringAsync())
                    Return AuswertenWennNeuer(json.RootElement)
                End Using
            End Using
        End Using
    End Function

    Private Function AuswertenWennNeuer(release As JsonElement) As UpdateInfo
        Dim tag = Text(release, "tag_name").TrimStart("v"c, "V"c)
        Dim gelesen As Version = Nothing
        If Not System.Version.TryParse(tag, gelesen) Then Return Nothing
        Dim neu As New Version(gelesen.Major, gelesen.Minor, Math.Max(0, gelesen.Build))
        If neu <= AktuelleVersion Then Return Nothing

        Dim info As New UpdateInfo With {
            .Version = neu,
            .Hinweise = Text(release, "body"),
            .ReleaseSeite = Text(release, "html_url")
        }
        Dim assets As JsonElement
        If release.TryGetProperty("assets", assets) AndAlso assets.ValueKind = JsonValueKind.Array Then
            For Each a In assets.EnumerateArray()
                If Not String.Equals(Text(a, "name"), AssetName, StringComparison.OrdinalIgnoreCase) Then Continue For
                info.DownloadUrl = Text(a, "browser_download_url")
                Dim groesse As JsonElement
                If a.TryGetProperty("size", groesse) AndAlso groesse.ValueKind = JsonValueKind.Number Then info.Groesse = groesse.GetInt64()
                Dim digest = Text(a, "digest")
                If digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) Then info.Sha256 = digest.Substring(7)
                Exit For
            Next
        End If
        Return info
    End Function

    Private Function Text(e As JsonElement, name As String) As String
        Dim wert As JsonElement
        If e.TryGetProperty(name, wert) AndAlso wert.ValueKind = JsonValueKind.String Then Return If(wert.GetString(), "")
        Return ""
    End Function

    ''' <summary>
    ''' Lädt die neue EXE in den Programmordner (gleiches Laufwerk, damit das Umbenennen klappt)
    ''' und prüft Größe und Prüfsumme. Liefert den Pfad der heruntergeladenen Datei.
    ''' </summary>
    Public Async Function HerunterladenAsync(info As UpdateInfo, fortschritt As IProgress(Of Integer),
                                             abbruch As CancellationToken) As Task(Of String)
        If String.IsNullOrEmpty(info.DownloadUrl) Then
            Throw New InvalidOperationException($"Am Release hängt keine Datei ""{AssetName}"".")
        End If
        Dim ziel = Environment.ProcessPath & EndungNeu
        Try
            Using client = NeuerClient(TimeSpan.FromMinutes(30))
                Using antwort = Await client.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, abbruch)
                    antwort.EnsureSuccessStatusCode()
                    Dim gesamt = If(antwort.Content.Headers.ContentLength, info.Groesse)
                    Using quelle = Await antwort.Content.ReadAsStreamAsync(abbruch),
                          datei As New FileStream(ziel, FileMode.Create, FileAccess.Write, FileShare.None)
                        Dim puffer(81919) As Byte
                        Dim gelesen As Long = 0
                        Do
                            Dim n = Await quelle.ReadAsync(puffer.AsMemory(0, puffer.Length), abbruch)
                            If n = 0 Then Exit Do
                            Await datei.WriteAsync(puffer.AsMemory(0, n), abbruch)
                            gelesen += n
                            If gesamt > 0 Then fortschritt?.Report(CInt(gelesen * 100 \ gesamt))
                        Loop
                    End Using
                End Using
            End Using

            Dim laenge = New FileInfo(ziel).Length
            If info.Groesse > 0 AndAlso laenge <> info.Groesse Then
                Throw New InvalidDataException($"Download unvollständig ({laenge:N0} von {info.Groesse:N0} Bytes).")
            End If
            If info.Sha256.Length > 0 Then
                Dim ist As String
                Using s = File.OpenRead(ziel)
                    ist = Convert.ToHexString(Await SHA256.HashDataAsync(s, abbruch))
                End Using
                If Not String.Equals(ist, info.Sha256, StringComparison.OrdinalIgnoreCase) Then
                    Throw New InvalidDataException("Die Prüfsumme der heruntergeladenen Datei stimmt nicht. Das Update wurde verworfen.")
                End If
            End If
            Return ziel
        Catch ex As UnauthorizedAccessException
            LoeschenOhneFehler(ziel)
            Throw New UnauthorizedAccessException(
                "Im Programmordner darf nicht geschrieben werden. Lege die EXE in einen eigenen Ordner " &
                "(z. B. Dokumente oder Nextcloud) statt unter ""Programme"".", ex)
        Catch
            LoeschenOhneFehler(ziel)
            Throw
        End Try
    End Function

    ''' <summary>Ersetzt die laufende EXE durch die heruntergeladene und startet die neue Version.</summary>
    Public Sub InstallierenUndNeustarten(neueDatei As String)
        Dim exe = Environment.ProcessPath
        Dim alt = exe & EndungAlt
        LoeschenOhneFehler(alt)
        File.Move(exe, alt)
        Try
            File.Move(neueDatei, exe)
        Catch
            ' Zurück auf die alte Version, damit das Programm startfähig bleibt
            File.Move(alt, exe)
            Throw
        End Try
        Process.Start(New ProcessStartInfo(exe) With {
            .UseShellExecute = False,
            .Arguments = String.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(Function(a) """" & a & """"))
        })
        System.Windows.Forms.Application.Exit()
    End Sub

    ''' <summary>Entfernt Reste eines vorherigen Updates (beim Programmstart aufrufen).</summary>
    Public Sub Aufraeumen()
        Dim exe = Environment.ProcessPath
        If String.IsNullOrEmpty(exe) Then Return
        ' Die alte EXE kann kurz nach dem Neustart noch gesperrt sein → mehrmals versuchen
        Dim alt = exe & EndungAlt
        If File.Exists(alt) Then
            Task.Run(Sub()
                         For versuch = 1 To 10
                             Try
                                 File.Delete(alt)
                                 Return
                             Catch ex As IOException
                                 Thread.Sleep(500)
                             Catch ex As UnauthorizedAccessException
                                 Thread.Sleep(500)
                             End Try
                         Next
                     End Sub)
        End If
        LoeschenOhneFehler(exe & EndungNeu)
    End Sub

    Private Sub LoeschenOhneFehler(pfad As String)
        Try
            If File.Exists(pfad) Then File.Delete(pfad)
        Catch ex As Exception
            ' Wird beim nächsten Start erneut versucht
        End Try
    End Sub

End Module
