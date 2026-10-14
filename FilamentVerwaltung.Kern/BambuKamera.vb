Imports System.Net.Security
Imports System.Net.Sockets
Imports System.Threading

''' <summary>
''' Kamerabild eines Bambu-Lab-Druckers im LAN-Modus ("LAN-Liveansicht" muss am Drucker an sein).
''' P1P/P1S/A1/A1 mini: TLS auf Port 6000, Anmeldung mit "bblp" + Zugangscode, danach JPEG-Einzelbilder (ca. 1 pro Sekunde).
''' X1/X1C/X1E/H2D: RTSPS-Stream auf Port 322 – wird mit ffmpeg (im PATH oder neben der EXE) in JPEG-Bilder umgewandelt.
''' Zuerst wird Port 6000 versucht, sonst RTSPS.
''' </summary>
Public Class BambuKamera
    Implements IKamera

    Private ReadOnly _drucker As Drucker
    Private ReadOnly _protokoll As DruckerProtokoll
    Private _abbruch As CancellationTokenSource
    Private _ffmpeg As Process

    ''' <summary>Neues Bild (JPEG-Bytes). Kommt aus einem Hintergrund-Thread.</summary>
    Public Event BildEmpfangen(jpeg As Byte()) Implements IKamera.BildEmpfangen
    ''' <summary>Statusmeldung zum Anzeigen. Kommt aus einem Hintergrund-Thread.</summary>
    Public Event StatusGeaendert(text As String) Implements IKamera.StatusGeaendert

    Public Sub New(d As Drucker, Optional protokoll As DruckerProtokoll = Nothing)
        _drucker = d
        _protokoll = protokoll
    End Sub

    Public ReadOnly Property Laeuft As Boolean
        Get
            Return _abbruch IsNot Nothing
        End Get
    End Property

    Private ReadOnly Property Host As String
        Get
            Dim a = _drucker.Adresse.Trim()
            For Each praefix In {"http://", "https://", "mqtts://", "mqtt://"}
                If a.StartsWith(praefix, StringComparison.OrdinalIgnoreCase) Then a = a.Substring(praefix.Length)
            Next
            a = a.TrimEnd("/"c)
            Dim doppelpunkt = a.LastIndexOf(":"c)
            Return If(doppelpunkt > 0, a.Substring(0, doppelpunkt), a)
        End Get
    End Property

    Public Sub Starten() Implements IKamera.Starten
        If _abbruch IsNot Nothing Then Return
        _abbruch = New CancellationTokenSource()
        Dim token = _abbruch.Token
        Task.Run(Function() SchleifeAsync(token))
    End Sub

    Public Sub Stoppen() Implements IKamera.Stoppen
        _abbruch?.Cancel()
        _abbruch = Nothing
        FfmpegBeenden()
    End Sub

    Private Sub Schreiben(text As String)
        _protokoll?.Schreiben("Kamera: " & text)
    End Sub

    Private Async Function SchleifeAsync(abbruch As CancellationToken) As Task
        Dim rtsp As Boolean = False
        Dim versuche = 0
        While Not abbruch.IsCancellationRequested
            Try
                If Not rtsp Then
                    Try
                        Await Port6000Async(abbruch)
                    Catch ex As SocketException
                        ' Port 6000 nicht offen → vermutlich X1/H2D mit RTSPS
                        Schreiben($"Port 6000 nicht erreichbar ({ex.SocketErrorCode}) – versuche RTSPS auf Port 322")
                        rtsp = True
                        Continue While
                    Catch ex As TimeoutException
                        Schreiben("Port 6000: keine Antwort – versuche RTSPS auf Port 322")
                        rtsp = True
                        Continue While
                    End Try
                Else
                    Await RtspAsync(abbruch)
                End If
            Catch ex As OperationCanceledException When abbruch.IsCancellationRequested
                Return
            Catch ex As Exception
                Schreiben("Fehler: " & ex.Message)
                RaiseEvent StatusGeaendert(ex.Message)
            End Try
            If abbruch.IsCancellationRequested Then Return
            versuche += 1
            Try
                Await Task.Delay(If(versuche < 3, 2000, 5000), abbruch)
            Catch ex As OperationCanceledException
                Return
            End Try
        End While
    End Function

    ' ---------- P1/A1: JPEG über TLS, Port 6000 ----------

    Private Async Function Port6000Async(abbruch As CancellationToken) As Task
        RaiseEvent StatusGeaendert("Verbinde mit Kamera (Port 6000) …")
        Schreiben($"verbinde mit {Host}:6000 …")
        Using tcp As New TcpClient()
            Using zeit = CancellationTokenSource.CreateLinkedTokenSource(abbruch)
                zeit.CancelAfter(TimeSpan.FromSeconds(5))
                Try
                    Await tcp.ConnectAsync(Host, 6000, zeit.Token)
                Catch ex As OperationCanceledException When Not abbruch.IsCancellationRequested
                    Throw New TimeoutException()
                End Try
            End Using
            ' Der Drucker hat ein selbst ausgestelltes Zertifikat – im Heimnetz akzeptieren.
            Using ssl As New SslStream(tcp.GetStream(), False, Function(s, c, k, f) True)
                Await ssl.AuthenticateAsClientAsync(New SslClientAuthenticationOptions With {.TargetHost = Host}, abbruch)
                Await ssl.WriteAsync(Anmeldung(), abbruch)
                Await ssl.FlushAsync(abbruch)
                Schreiben("angemeldet, warte auf Bilder …")
                RaiseEvent StatusGeaendert("Warte auf Bild …")

                Dim kopf(15) As Byte
                While Not abbruch.IsCancellationRequested
                    If Not Await LesenAsync(ssl, kopf, abbruch) Then
                        Throw New IO.IOException("Der Drucker hat die Kamera-Verbindung getrennt – Zugangscode und ""LAN-Liveansicht"" am Drucker prüfen.")
                    End If
                    Dim groesse = kopf(0) Or (CInt(kopf(1)) << 8) Or (CInt(kopf(2)) << 16)
                    If groesse <= 0 OrElse groesse > 8 * 1024 * 1024 Then Throw New IO.IOException($"Ungültige Bildgröße {groesse}")
                    Dim bild(groesse - 1) As Byte
                    If Not Await LesenAsync(ssl, bild, abbruch) Then Throw New IO.IOException("Verbindung während des Bildes getrennt")
                    If bild.Length > 4 AndAlso bild(0) = &HFF AndAlso bild(1) = &HD8 Then RaiseEvent BildEmpfangen(bild)
                End While
            End Using
        End Using
    End Function

    ''' <summary>80 Byte: 0x40, 0x3000, 0, 0 (je uint32 LE), Benutzer (32 Byte), Zugangscode (32 Byte).</summary>
    Private Function Anmeldung() As Byte()
        Dim daten(79) As Byte
        BitConverter.GetBytes(&H40UI).CopyTo(daten, 0)
        BitConverter.GetBytes(&H3000UI).CopyTo(daten, 4)
        Dim benutzer = Text.Encoding.ASCII.GetBytes("bblp")
        Array.Copy(benutzer, 0, daten, 16, benutzer.Length)
        Dim code = Text.Encoding.ASCII.GetBytes(_drucker.Zugang.Trim())
        Array.Copy(code, 0, daten, 48, Math.Min(32, code.Length))
        Return daten
    End Function

    Private Shared Async Function LesenAsync(s As IO.Stream, puffer As Byte(), abbruch As CancellationToken) As Task(Of Boolean)
        Dim gelesen = 0
        While gelesen < puffer.Length
            Dim n = Await s.ReadAsync(puffer.AsMemory(gelesen), abbruch)
            If n = 0 Then Return False
            gelesen += n
        End While
        Return True
    End Function

    ' ---------- X1/H2D: RTSPS über ffmpeg ----------

    Private Shared Function FfmpegPfad() As String
        Dim nebenExe = IO.Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")
        Return If(IO.File.Exists(nebenExe), nebenExe, "ffmpeg")
    End Function

    Private Async Function RtspAsync(abbruch As CancellationToken) As Task
        RaiseEvent StatusGeaendert("Verbinde mit Kamera (RTSPS) …")
        Dim url = $"rtsps://bblp:{Uri.EscapeDataString(_drucker.Zugang.Trim())}@{Host}:322/streaming/live/1"
        Schreiben($"starte ffmpeg für rtsps://bblp:***@{Host}:322/streaming/live/1")
        Dim info As New ProcessStartInfo(FfmpegPfad()) With {
            .UseShellExecute = False, .CreateNoWindow = True,
            .RedirectStandardOutput = True, .RedirectStandardError = True}
        For Each a In {"-hide_banner", "-loglevel", "error", "-rtsp_transport", "tcp", "-i", url,
                       "-an", "-r", "2", "-f", "image2pipe", "-vcodec", "mjpeg", "-q:v", "5", "-"}
            info.ArgumentList.Add(a)
        Next
        Try
            _ffmpeg = Process.Start(info)
        Catch ex As ComponentModel.Win32Exception
            Throw New InvalidOperationException("Port 6000 antwortet nicht (also kein P1/A1) und ffmpeg wurde nicht gefunden. " &
                                                "Für X1/H2D ffmpeg installieren (z. B. ""winget install ffmpeg"") oder ffmpeg.exe neben die EXE legen.")
        End Try
        Dim proc = _ffmpeg
        Dim fehler As New Text.StringBuilder()
        AddHandler proc.ErrorDataReceived, Sub(s, e)
                                               If e.Data Is Nothing Then Return
                                               SyncLock fehler
                                                   If fehler.Length < 2000 Then fehler.AppendLine(e.Data)
                                               End SyncLock
                                               Schreiben("ffmpeg: " & e.Data)
                                           End Sub
        proc.BeginErrorReadLine()
        RaiseEvent StatusGeaendert("Warte auf Bild …")

        Try
            Using reg = abbruch.Register(AddressOf FfmpegBeenden)
                Dim aus = proc.StandardOutput.BaseStream
                Dim lese(65535) As Byte
                Dim puffer As New IO.MemoryStream()
                While True
                    Dim n = Await aus.ReadAsync(lese.AsMemory(), abbruch)
                    If n = 0 Then Exit While
                    puffer.Write(lese, 0, n)
                    BilderHerausloesen(puffer)
                End While
            End Using
            proc.WaitForExit(2000)
        Finally
            FfmpegBeenden()
        End Try
        abbruch.ThrowIfCancellationRequested()
        Dim meldung As String
        SyncLock fehler
            meldung = fehler.ToString().Trim()
        End SyncLock
        Throw New IO.IOException("Kamera-Stream beendet" & If(meldung.Length > 0, ": " & meldung.Split(ChrW(10)).Last().Trim(),
                                 " – ""LAN-Liveansicht"" am Drucker einschalten und Zugangscode prüfen."))
    End Function

    ''' <summary>Schneidet vollständige JPEGs (FFD8 … FFD9) aus dem ffmpeg-Datenstrom.</summary>
    Private Sub BilderHerausloesen(puffer As IO.MemoryStream)
        Dim daten = puffer.GetBuffer()
        Dim laenge = CInt(puffer.Length)
        Dim start = 0
        While True
            Dim soi = Suchen(daten, laenge, start, &HD8)
            If soi < 0 Then
                start = laenge
                Exit While
            End If
            Dim eoi = Suchen(daten, laenge, soi + 2, &HD9)
            If eoi < 0 Then
                start = soi
                Exit While
            End If
            Dim bild(eoi + 2 - soi - 1) As Byte
            Array.Copy(daten, soi, bild, 0, bild.Length)
            RaiseEvent BildEmpfangen(bild)
            start = eoi + 2
        End While
        Dim rest = laenge - start
        If rest > 0 Then Buffer.BlockCopy(daten, start, daten, 0, rest)
        puffer.SetLength(rest)
        puffer.Position = rest
    End Sub

    Private Shared Function Suchen(daten As Byte(), laenge As Integer, ab As Integer, marke As Byte) As Integer
        For i = ab To laenge - 2
            If daten(i) = &HFF AndAlso daten(i + 1) = marke Then Return i
        Next
        Return -1
    End Function

    Private Sub FfmpegBeenden()
        Dim p = _ffmpeg
        _ffmpeg = Nothing
        If p Is Nothing Then Return
        Try
            If Not p.HasExited Then p.Kill()
        Catch
        End Try
        p.Dispose()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Stoppen()
    End Sub

End Class
