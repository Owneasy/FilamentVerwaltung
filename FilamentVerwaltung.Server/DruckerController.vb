Imports Microsoft.AspNetCore.Mvc

''' <summary>
''' REST-API für die Drucker-Überwachung auf dem Server: Live-Status, Protokoll, Senden, offene Druckenden, Kamera.
''' </summary>
<ApiController>
<Route("api")>
Public Class DruckerController
    Inherits ControllerBase

    Private ReadOnly _db As Datenbank
    Private ReadOnly _ueberwachung As DruckerUeberwachung
    Private ReadOnly _auftraege As SendeAuftraege
    Private ReadOnly _kameras As KameraVerwaltung
    Private ReadOnly _regeln As PushRegeln

    Public Sub New(db As Datenbank, ueberwachung As DruckerUeberwachung, auftraege As SendeAuftraege, kameras As KameraVerwaltung, regeln As PushRegeln)
        _regeln = regeln
        _db = db
        _ueberwachung = ueberwachung
        _auftraege = auftraege
        _kameras = kameras
    End Sub

    <HttpGet("drucker/live")>
    Public Function Live() As List(Of DruckerLive)
        Return _ueberwachung.DruckerIds().Select(Function(id) New DruckerLive With {
            .DruckerId = id, .Status = _ueberwachung.Status(id), .Endungen = _ueberwachung.DruckbareEndungen(id)}).ToList()
    End Function

    <HttpGet("drucker/{id:long}/protokoll")>
    Public Function Protokoll(id As Long) As WertAnfrage
        Return New WertAnfrage With {.Wert = _ueberwachung.ProtokollText(id)}
    End Function

    ' ---------- Senden

    <HttpPost("drucker/{id:long}/senden")>
    Public Function Senden(id As Long, <FromBody> a As SendenAnfrage) As ActionResult(Of SendeAuftragInfo)
        Dim d = _db.DateienLaden().FirstOrDefault(Function(x) x.Id = a.DateiId)
        If d Is Nothing Then Return NotFound("Die Datei gibt es auf dem Server nicht (mehr).")
        Dim pfad = _db.DateiPfad(d)
        If Not System.IO.File.Exists(pfad) Then Return NotFound("Die Datei fehlt im Dateien-Ordner des Servers.")
        Return _auftraege.Starten(_ueberwachung, id, pfad, d.Id, If(a.Optionen, New SendeOptionen()))
    End Function

    ''' <summary>Platten einer geslicten 3MF (für die Auswahl von Platte und AMS-Fächern beim Senden); sonst leer.</summary>
    <HttpGet("dateien/{id:long}/platten")>
    Public Function Platten(id As Long) As ActionResult(Of List(Of PlattenInfo))
        Dim d = _db.DateienLaden().FirstOrDefault(Function(x) x.Id = id)
        If d Is Nothing Then Return NotFound("Die Datei gibt es auf dem Server nicht (mehr).")
        Dim pfad = _db.DateiPfad(d)
        If d.Typ <> "3MF" OrElse Not System.IO.File.Exists(pfad) Then Return New List(Of PlattenInfo)
        Dim liste = DreiMfPlatten(pfad)
        For Each p In liste
            p.OhneDruckerstart = DreiMfOhneDruckerstart(pfad, p.Nummer)
        Next
        Return liste
    End Function

    <HttpGet("sendeauftraege/{id}")>
    Public Function SendeAuftrag(id As String) As ActionResult(Of SendeAuftragInfo)
        Dim info = _auftraege.Abrufen(id)
        If info Is Nothing Then Return NotFound("Sendeauftrag unbekannt.")
        Return info
    End Function

    <HttpDelete("sendeauftraege/{id}")>
    Public Function SendenAbbrechen(id As String) As IActionResult
        Return If(_auftraege.Abbrechen(id), CType(Ok(), IActionResult), NotFound())
    End Function

    ' ---------- Offene Druckenden

    <HttpGet("druckenden")>
    Public Function DruckEnden() As List(Of DruckEnde)
        Return _db.OffeneDruckEnden()
    End Function

    <HttpDelete("druckenden/{id:long}")>
    Public Function DruckEndeErledigt(id As Long) As IActionResult
        Return If(_db.DruckEndeErledigt(id), CType(Ok(), IActionResult), NotFound())
    End Function

    ''' <summary>
    ''' Ein PC will buchen: entfernt das Druckende, falls es noch offen ist ("1"), sonst "0" – so kann nur ein PC buchen.
    ''' SQLite führt das DELETE atomar aus.
    ''' </summary>
    <HttpPost("druckenden/{id:long}/uebernehmen")>
    Public Function DruckEndeUebernehmen(id As Long) As WertAnfrage
        Return New WertAnfrage With {.Wert = If(_db.DruckEndeErledigt(id), "1", "0")}
    End Function

    ''' <summary>Buchungsvorschlag wie in der App (Spule aus der Fach-Zuordnung, Gewicht, Datei, Druckzeit).</summary>
    <HttpGet("druckenden/{id:long}/vorschlag")>
    Public Function Vorschlag(id As Long) As ActionResult(Of Buchung)
        Dim ende = _db.OffeneDruckEnden().FirstOrDefault(Function(x) x.Id = id)
        If ende Is Nothing Then Return NotFound("Dieser Druck wurde schon gebucht oder verworfen.")
        Return BuchungVorschlagen(ende, _db.AlleLaden(), _db.DateienLaden(), _db.SpulenZuordnung(ende.DruckerId))
    End Function

    ''' <summary>
    ''' Bucht den Druck in einem Schritt (für die Webseite): beansprucht das Druckende und speichert die Buchung.
    ''' 409, wenn es schon jemand anderes gebucht oder verworfen hat; scheitert das Speichern, bleibt es offen.
    ''' </summary>
    <HttpPost("druckenden/{id:long}/buchen")>
    Public Function Buchen(id As Long, <FromBody> b As Buchung) As ActionResult(Of Buchung)
        Dim ende = _db.OffeneDruckEnden().FirstOrDefault(Function(x) x.Id = id)
        If ende Is Nothing OrElse Not _db.DruckEndeErledigt(id) Then
            Return Conflict("Dieser Druck wurde schon an einem anderen Gerät gebucht oder verworfen.")
        End If
        Try
            b.Id = 0
            b.Art = Buchung.ArtDruck
            Dim vorher = _regeln.RestVorher(b.FilamentId)
            _db.DruckSpeichern(b)
            _regeln.SpuleGeaendert(b.FilamentId, vorher)
            Return b
        Catch
            _db.DruckEndeSpeichern(ende)
            Throw
        End Try
    End Function

    ''' <summary>Buchen abgebrochen: Druckende wieder offen legen (bekommt eine neue Nummer).</summary>
    <HttpPost("druckenden")>
    Public Function DruckEndeZuruecklegen(<FromBody> ende As DruckEnde) As DruckEnde
        _db.DruckEndeSpeichern(ende)
        Return ende
    End Function

    ' ---------- Verbindungstest

    ''' <summary>Fragt einen (noch nicht gespeicherten) Drucker einmal vom Server aus ab.</summary>
    <HttpPost("drucker/testen")>
    Public Async Function Testen(<FromBody> d As Drucker) As Task(Of DruckerStatus)
        Return Await DruckerTest.AbfragenAsync(d, HttpContext.RequestAborted)
    End Function

    ' ---------- Kamera

    ''' <summary>Neuestes JPEG; 204 mit Kopfzeile X-Kamera-Status, solange (noch) kein Bild da ist.</summary>
    <HttpGet("drucker/{id:long}/kamera")>
    Public Function Kamera(id As Long) As IActionResult
        Dim d = _db.DruckerLaden().FirstOrDefault(Function(x) x.Id = id)
        If d Is Nothing Then Return NotFound("Drucker unbekannt.")
        If d.Typ <> Drucker.TypBambu Then Return NotFound("Ein Kamerabild gibt es nur bei Bambu Lab.")
        Dim ergebnis = _kameras.Abrufen(d)
        Response.Headers.CacheControl = "no-store"
        If ergebnis.Bild Is Nothing Then
            Response.Headers("X-Kamera-Status") = Uri.EscapeDataString(If(ergebnis.Status, ""))
            Return NoContent()
        End If
        Return File(ergebnis.Bild, "image/jpeg")
    End Function

End Class
