Imports System.IO
Imports Microsoft.AspNetCore.Http
Imports Microsoft.AspNetCore.Mvc

''' <summary>
''' REST-API über der Datenbank – bildet <see cref="IDatenquelle"/> ab, damit die App dasselbe wie lokal tun kann.
''' </summary>
<ApiController>
<Route("api")>
Public Class DatenController
    Inherits ControllerBase

    Private ReadOnly _db As Datenbank
    Private ReadOnly _optionen As ServerOptionen
    Private ReadOnly _ueberwachung As DruckerUeberwachung

    Public Sub New(db As Datenbank, optionen As ServerOptionen, ueberwachung As DruckerUeberwachung)
        _db = db
        _optionen = optionen
        _ueberwachung = ueberwachung
    End Sub

    <HttpGet("info")>
    Public Function Info() As ServerInfo
        Return New ServerInfo With {.Version = ServerVersion()}
    End Function

    ' ---------- Einstellungen

    <HttpGet("einstellungen/{schluessel}")>
    Public Function EinstellungLesen(schluessel As String) As WertAnfrage
        Return New WertAnfrage With {.Wert = _db.EinstellungLesen(schluessel)}
    End Function

    <HttpPut("einstellungen/{schluessel}")>
    Public Sub EinstellungSpeichern(schluessel As String, <FromBody> a As WertAnfrage)
        _db.EinstellungSpeichern(schluessel, a.Wert)
    End Sub

    <HttpGet("leerspulen")>
    Public Function LeerspulenVorlage(<FromQuery> hersteller As String) As LeerspulenAntwort
        Dim eigener As Boolean
        Dim gewicht = _db.LeerspulenVorlage(hersteller, eigener)
        Return New LeerspulenAntwort With {.Gewicht = gewicht, .EigenerWert = eigener}
    End Function

    ' ---------- Filament

    <HttpGet("filament")>
    Public Function AlleLaden() As List(Of Filament)
        Return _db.AlleLaden()
    End Function

    <HttpPost("filament")>
    Public Function Speichern(<FromBody> f As Filament) As Filament
        _db.Speichern(f)
        Return f
    End Function

    <HttpDelete("filament/{id:long}")>
    Public Sub Loeschen(id As Long)
        _db.Loeschen(id)
    End Sub

    <HttpGet("filament/code-existiert")>
    Public Function CodeExistiert(<FromQuery> code As String, <FromQuery> ausserId As Long) As Boolean
        Return _db.CodeExistiert(code, ausserId)
    End Function

    <HttpGet("filament/naechster-code")>
    Public Function NaechsterCode() As WertAnfrage
        Return New WertAnfrage With {.Wert = _db.NaechsterCode()}
    End Function

    <HttpPut("filament/{id:long}/rfid")>
    Public Sub RfidKennungSetzen(id As Long, <FromBody> a As WertAnfrage)
        _db.RfidKennungSetzen(id, a.Wert)
    End Sub

    <HttpPost("filament/{id:long}/restgewicht")>
    Public Sub RestgewichtBuchen(id As Long, <FromBody> a As RestgewichtAnfrage)
        _db.RestgewichtBuchen(id, a.NeuesRestgewicht, a.Art, a.Bezeichnung)
    End Sub

    ' ---------- Buchungen

    <HttpGet("buchungen")>
    Public Function BuchungenLaden() As List(Of Buchung)
        Return _db.BuchungenLaden()
    End Function

    <HttpPost("buchungen/druck")>
    Public Function DruckSpeichern(<FromBody> b As Buchung) As Buchung
        _db.DruckSpeichern(b)
        Return b
    End Function

    <HttpDelete("buchungen/{id:long}")>
    Public Sub BuchungLoeschen(id As Long, <FromQuery> zurueckbuchen As Boolean)
        _db.BuchungLoeschen(id, zurueckbuchen)
    End Sub

    ' ---------- Drucker

    <HttpGet("drucker")>
    Public Function DruckerLaden() As List(Of Drucker)
        Return _db.DruckerLaden()
    End Function

    <HttpPost("drucker")>
    Public Function DruckerSpeichern(<FromBody> d As Drucker) As Drucker
        _db.DruckerSpeichern(d)
        _ueberwachung.Neuladen()   ' neue Verbindungsdaten gleich übernehmen
        Return d
    End Function

    <HttpDelete("drucker/{id:long}")>
    Public Sub DruckerLoeschen(id As Long)
        _db.DruckerLoeschen(id)
        _ueberwachung.Neuladen()
    End Sub

    <HttpGet("drucker/{id:long}/spulen")>
    Public Function SpulenZuordnung(id As Long) As Dictionary(Of Integer, Long)
        Return _db.SpulenZuordnung(id)
    End Function

    <HttpPut("drucker/{id:long}/spulen/{slot:int}")>
    Public Sub SpuleZuordnen(id As Long, slot As Integer, <FromBody> a As SpuleAnfrage)
        _db.SpuleZuordnen(id, slot, a.FilamentId)
    End Sub

    ' ---------- Dateien

    <HttpGet("dateien")>
    Public Function DateienLaden() As List(Of Datei)
        Return _db.DateienLaden()
    End Function

    <HttpPut("dateien/{id:long}/notiz")>
    Public Sub DateiNotizSpeichern(id As Long, <FromBody> a As WertAnfrage)
        _db.DateiNotizSpeichern(id, a.Wert)
    End Sub

    <HttpDelete("dateien/{id:long}")>
    Public Function DateiLoeschen(id As Long) As IActionResult
        Dim d = _db.DateienLaden().FirstOrDefault(Function(x) x.Id = id)
        If d Is Nothing Then Return NotFound()
        _db.DateiLoeschen(d)
        Return Ok()
    End Function

    <HttpGet("dateien/{id:long}/inhalt")>
    Public Function DateiHerunterladen(id As Long) As IActionResult
        Dim d = _db.DateienLaden().FirstOrDefault(Function(x) x.Id = id)
        If d Is Nothing Then Return NotFound()
        Dim pfad = _db.DateiPfad(d)
        If Not System.IO.File.Exists(pfad) Then Return NotFound("Die Datei fehlt im Dateien-Ordner des Servers.")
        Return PhysicalFile(pfad, "application/octet-stream", d.Name, enableRangeProcessing:=True)
    End Function

    ' ---------- Upload in Stücken (wegen der Größenbeschränkung von Cloudflare)

    <HttpPost("uploads")>
    Public Function UploadBeginnen() As UploadAntwort
        Directory.CreateDirectory(_optionen.UploadOrdner)
        Dim id = Guid.NewGuid().ToString("N")
        System.IO.File.WriteAllBytes(UploadPfad(id), Array.Empty(Of Byte)())
        Return New UploadAntwort With {.Id = id}
    End Function

    ''' <summary>Hängt ein Stück an. offset muss der bisherigen Länge entsprechen (sonst 409, z. B. nach doppeltem Senden).</summary>
    <HttpPut("uploads/{id}")>
    <RequestSizeLimit(Api.UploadStueckBytes + 1024 * 1024)>
    Public Async Function UploadStueck(id As String, <FromQuery> offset As Long) As Task(Of IActionResult)
        If Not IstUploadId(id) OrElse Not System.IO.File.Exists(UploadPfad(id)) Then Return NotFound()
        Using ziel As New FileStream(UploadPfad(id), FileMode.Open, FileAccess.Write)
            If ziel.Length <> offset Then Return Conflict($"Erwartet wurde offset={ziel.Length}.")
            ziel.Seek(0, SeekOrigin.End)
            Await Request.Body.CopyToAsync(ziel)
        End Using
        Return Ok()
    End Function

    <HttpPost("uploads/{id}/fertig")>
    Public Function UploadAbschliessen(id As String, <FromBody> a As UploadFertigAnfrage) As ActionResult(Of Datei)
        If Not IstUploadId(id) OrElse Not System.IO.File.Exists(UploadPfad(id)) Then Return NotFound()
        Dim name = Path.GetFileName(If(a.Name, "").Trim())
        For Each c In Path.GetInvalidFileNameChars()
            name = name.Replace(c, "_"c)
        Next
        If name.Length = 0 Then Return BadRequest("Dateiname fehlt.")
        ' Unter dem echten Namen ablegen, damit DateiImportieren ihn übernimmt
        Dim ordner = Path.Combine(_optionen.UploadOrdner, id & "_fertig")
        Directory.CreateDirectory(ordner)
        Dim pfad = Path.Combine(ordner, name)
        Try
            System.IO.File.Move(UploadPfad(id), pfad)
            Return _db.DateiImportieren(pfad)
        Finally
            Directory.Delete(ordner, True)
        End Try
    End Function

    Private Function UploadPfad(id As String) As String
        Return Path.Combine(_optionen.UploadOrdner, id & ".part")
    End Function

    Private Shared Function IstUploadId(id As String) As Boolean
        Dim g As Guid
        Return Guid.TryParseExact(id, "N", g)
    End Function

    ' ---------- QR-Codes für Etiketten (Handy-Seite)

    ''' <summary>
    ''' QR-Codes wie auf den Etiketten der App (QRCoder, Fehlerkorrektur M, mit Ruhezone): je Text die Modulmatrix
    ''' als Zeilen aus "0"/"1". Die Seite zeichnet daraus scharfe Rechtecke in Millimetern.
    ''' </summary>
    <HttpPost("qr")>
    Public Function QrCodes(<FromBody> texte As List(Of String)) As ActionResult(Of Dictionary(Of String, String()))
        If texte Is Nothing OrElse texte.Count > 500 Then Return BadRequest("Bitte höchstens 500 Texte auf einmal.")
        Dim ergebnis As New Dictionary(Of String, String())
        Using gen As New QRCoder.QRCodeGenerator()
            For Each inhalt In texte.Distinct()
                Using daten = gen.CreateQrCode(If(String.IsNullOrEmpty(inhalt), "-", inhalt), QRCoder.QRCodeGenerator.ECCLevel.M)
                    ergebnis(If(inhalt, "")) = daten.ModuleMatrix.Select(
                        Function(zeile) String.Concat(zeile.Cast(Of Boolean)().Select(Function(an) If(an, "1"c, "0"c)))).ToArray()
                End Using
            Next
        End Using
        Return ergebnis
    End Function

    ' ---------- Sicherungen

    <HttpGet("sicherungen")>
    Public Function SicherungenLaden() As List(Of SicherungInfo)
        Return _db.SicherungenLaden().Select(AddressOf OhnePfad).ToList()
    End Function

    <HttpPost("sicherungen")>
    Public Function SicherungErstellen(<FromBody> a As SicherungAnfrage) As SicherungInfo
        Return OhnePfad(_db.SicherungErstellen(a.Art))
    End Function

    <HttpPost("sicherungen/wiederherstellen")>
    Public Function SicherungWiederherstellen(<FromBody> a As SicherungAnfrage) As IActionResult
        Dim s = _db.SicherungenLaden().FirstOrDefault(Function(x) String.Equals(x.Name, a.Name, StringComparison.OrdinalIgnoreCase))
        If s Is Nothing Then Return NotFound("Sicherung nicht gefunden.")
        _db.SicherungWiederherstellen(s)
        _ueberwachung.Neuladen()
        Return Ok()
    End Function

    ''' <summary>Den Ordner des Servers nicht nach außen geben – der Client braucht nur den Namen.</summary>
    Private Shared Function OhnePfad(s As SicherungInfo) As SicherungInfo
        Return New SicherungInfo With {.Pfad = s.Name, .Zeitpunkt = s.Zeitpunkt, .Groesse = s.Groesse, .Art = s.Art}
    End Function

End Class
