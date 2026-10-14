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

    Public Sub New(db As Datenbank, ueberwachung As DruckerUeberwachung, auftraege As SendeAuftraege, kameras As KameraVerwaltung)
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
