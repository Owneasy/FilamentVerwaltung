Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports Microsoft.Extensions.Logging

''' <summary>Inhalt einer Benachrichtigung (geht verschlüsselt an das Gerät, der Service Worker zeigt sie an).</summary>
Public Class PushNachricht
    Public Property Titel As String = ""
    Public Property Text As String = ""
    ''' <summary>Seite, die beim Antippen geöffnet wird, z. B. "/#buchen".</summary>
    Public Property Ziel As String = "/"
    ''' <summary>Gleiche Kennung ersetzt eine ältere Benachrichtigung.</summary>
    Public Property Kennung As String = ""
End Class

''' <summary>
''' Web Push (RFC 8030/8291/8292) ohne Fremdbibliothek: Die Nachricht wird für jedes Gerät mit dessen Schlüssel
''' verschlüsselt (aes128gcm) und mit dem VAPID-Schlüssel des Servers signiert an den Push-Dienst geschickt
''' (Apple, Google, Mozilla). Der VAPID-Schlüssel wird beim ersten Start erzeugt und in vapid.json gespeichert.
''' </summary>
Public Class PushDienst

    Private Const Kontakt As String = "https://github.com/Owneasy/FilamentVerwaltung"
    Private Shared ReadOnly Client As New HttpClient With {.Timeout = TimeSpan.FromSeconds(20)}

    Private ReadOnly _db As Datenbank
    Private ReadOnly _log As ILogger(Of PushDienst)
    Private ReadOnly _vapid As ECDsa
    ''' <summary>Öffentlicher VAPID-Schlüssel (Base64url, unkomprimierter Punkt) – braucht der Browser zum Anmelden.</summary>
    Public ReadOnly Property OeffentlicherSchluessel As String

    Private Class VapidDatei
        Public Property X As String = ""
        Public Property Y As String = ""
        Public Property D As String = ""
    End Class

    Public Sub New(db As Datenbank, optionen As ServerOptionen, log As ILogger(Of PushDienst))
        _db = db
        _log = log
        Dim datei = Path.Combine(optionen.DatenOrdner, "vapid.json")
        Dim p As ECParameters
        If File.Exists(datei) Then
            Dim gespeichert = JsonSerializer.Deserialize(Of VapidDatei)(File.ReadAllText(datei))
            p = New ECParameters With {.Curve = ECCurve.NamedCurves.nistP256, .D = B64(gespeichert.D),
                                       .Q = New ECPoint With {.X = B64(gespeichert.X), .Y = B64(gespeichert.Y)}}
        Else
            Using neu = ECDsa.Create(ECCurve.NamedCurves.nistP256)
                p = neu.ExportParameters(True)
            End Using
            File.WriteAllText(datei, JsonSerializer.Serialize(New VapidDatei With {.X = B64(p.Q.X), .Y = B64(p.Q.Y), .D = B64(p.D)}))
        End If
        _vapid = ECDsa.Create(p)
        OeffentlicherSchluessel = B64(Punkt(p.Q))
    End Sub

    ''' <summary>An alle angemeldeten Geräte senden; abgelaufene Anmeldungen werden entfernt. Gibt die Zahl der Empfänger zurück.</summary>
    Public Async Function AnAlleAsync(n As PushNachricht) As Task(Of Integer)
        Dim inhalt = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(New With {.title = n.Titel, .body = n.Text, .url = n.Ziel, .tag = n.Kennung}))
        Dim erreicht = 0
        For Each abo In _db.PushAbosLaden()
            Try
                Dim status = Await SendenAsync(abo, inhalt)
                If status = HttpStatusCode.NotFound OrElse status = HttpStatusCode.Gone Then
                    _db.PushAboLoeschen(abo.Endpunkt)
                    _log.LogInformation("Push-Anmeldung abgelaufen und entfernt ({Geraet})", abo.Geraet)
                ElseIf CInt(status) >= 200 AndAlso CInt(status) < 300 Then
                    erreicht += 1
                Else
                    _log.LogWarning("Push an {Geraet} abgelehnt: {Status}", abo.Geraet, CInt(status))
                End If
            Catch ex As Exception
                _log.LogWarning(ex, "Push an {Geraet} fehlgeschlagen", abo.Geraet)
            End Try
        Next
        Return erreicht
    End Function

    Private Async Function SendenAsync(abo As PushAbo, inhalt As Byte()) As Task(Of HttpStatusCode)
        Dim ziel As New Uri(abo.Endpunkt)
        Using anfrage As New HttpRequestMessage(HttpMethod.Post, ziel)
            anfrage.Content = New ByteArrayContent(Verschluesseln(inhalt, B64(abo.P256dh), B64(abo.Auth)))
            anfrage.Content.Headers.ContentType = New Headers.MediaTypeHeaderValue("application/octet-stream")
            anfrage.Content.Headers.ContentEncoding.Add("aes128gcm")
            anfrage.Headers.Add("TTL", "86400")
            anfrage.Headers.Add("Urgency", "high")
            anfrage.Headers.TryAddWithoutValidation("Authorization", $"vapid t={VapidToken(ziel)}, k={OeffentlicherSchluessel}")
            Using antwort = Await Client.SendAsync(anfrage)
                Return antwort.StatusCode
            End Using
        End Using
    End Function

    ''' <summary>VAPID-Token (JWT, ES256) für den Push-Dienst des Endpunkts, 12 Stunden gültig.</summary>
    Private Function VapidToken(ziel As Uri) As String
        Dim kopf = B64(Encoding.UTF8.GetBytes("{""typ"":""JWT"",""alg"":""ES256""}"))
        Dim ablauf = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds()
        Dim inhalt = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(New With {.aud = ziel.GetLeftPart(UriPartial.Authority), .exp = ablauf, .[sub] = Kontakt})))
        Dim signatur = _vapid.SignData(Encoding.ASCII.GetBytes(kopf & "." & inhalt), HashAlgorithmName.SHA256)   ' IEEE P1363 (r||s), wie JWT es will
        Return kopf & "." & inhalt & "." & B64(signatur)
    End Function

    ''' <summary>
    ''' Verschlüsselung nach RFC 8291 (aes128gcm): einmaliger ECDH-Schlüssel, Schlüsselableitung mit dem
    ''' Geheimnis des Browsers, ein einziger Datensatz. Ergebnis = Kopf (salt, rs, Schlüssel des Servers) + Chiffrat.
    ''' </summary>
    Public Shared Function Verschluesseln(klartext As Byte(), browserSchluessel As Byte(), geheimnis As Byte()) As Byte()
        Using einmalig = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256)
            Return Verschluesseln(klartext, browserSchluessel, geheimnis, RandomNumberGenerator.GetBytes(16), einmalig)
        End Using
    End Function

    ''' <summary>Wie oben, mit vorgegebenem Salz und Einmal-Schlüssel (für die Prüfung mit den Testwerten aus RFC 8291).</summary>
    Public Shared Function Verschluesseln(klartext As Byte(), browserSchluessel As Byte(), geheimnis As Byte(), salz As Byte(), einmalig As ECDiffieHellman) As Byte()
        Using browser = ECDiffieHellman.Create(New ECParameters With {.Curve = ECCurve.NamedCurves.nistP256,
                                                                      .Q = New ECPoint With {.X = browserSchluessel.AsSpan(1, 32).ToArray(),
                                                                                             .Y = browserSchluessel.AsSpan(33, 32).ToArray()}})
            Dim serverSchluessel = Punkt(einmalig.ExportParameters(False).Q)
            Dim ecdh = einmalig.DeriveRawSecretAgreement(browser.PublicKey)

            ' IKM = HKDF(auth, ecdh, "WebPush: info" || 0 || Browser-Schlüssel || Server-Schlüssel)
            Dim info = Verbinden(Encoding.ASCII.GetBytes("WebPush: info"), {0}, browserSchluessel, serverSchluessel)
            Dim ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdh, 32, geheimnis, info)
            Dim cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salz, Verbinden(Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm"), {0}))
            Dim nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salz, Verbinden(Encoding.ASCII.GetBytes("Content-Encoding: nonce"), {0}))

            Dim daten = Verbinden(klartext, {2})          ' Trennzeichen für den letzten (einzigen) Datensatz
            Dim chiffrat(daten.Length - 1) As Byte
            Dim tag(15) As Byte
            Using aes As New AesGcm(cek, 16)
                aes.Encrypt(nonce, daten, chiffrat, tag)
            End Using
            Dim rs = BitConverter.GetBytes(4096UI)
            If BitConverter.IsLittleEndian Then Array.Reverse(rs)
            Return Verbinden(salz, rs, {CByte(serverSchluessel.Length)}, serverSchluessel, chiffrat, tag)
        End Using
    End Function

    Private Shared Function Punkt(q As ECPoint) As Byte()
        Return Verbinden({4}, q.X, q.Y)
    End Function

    Private Shared Function Verbinden(ParamArray teile As Byte()()) As Byte()
        Return teile.SelectMany(Function(t) t).ToArray()
    End Function

    Private Shared Function B64(daten As Byte()) As String
        Return Convert.ToBase64String(daten).TrimEnd("="c).Replace("+"c, "-"c).Replace("/"c, "_"c)
    End Function

    Private Shared Function B64(text As String) As Byte()
        Dim s = text.Replace("-"c, "+"c).Replace("_"c, "/"c)
        Return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length Mod 4) Mod 4, "="c))
    End Function

End Class
