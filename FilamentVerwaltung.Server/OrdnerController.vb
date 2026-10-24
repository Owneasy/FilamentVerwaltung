Imports Microsoft.AspNetCore.Mvc

''' <summary>Ordner für Dateien (nur zum Sortieren – auf dem Server wird nichts verschoben).</summary>
<ApiController>
<Route("api")>
Public Class OrdnerController
    Inherits ControllerBase

    Private ReadOnly _db As Datenbank

    Public Sub New(db As Datenbank)
        _db = db
    End Sub

    <HttpGet("ordner")>
    Public Function Laden() As List(Of String)
        Return _db.OrdnerLaden()
    End Function

    <HttpPost("ordner")>
    Public Function Anlegen(<FromBody> a As WertAnfrage) As IActionResult
        If String.IsNullOrWhiteSpace(DateiOrdner.Normalisieren(a?.Wert)) Then Return BadRequest("Bitte einen Ordnernamen eingeben.")
        _db.OrdnerAnlegen(a.Wert)
        Return Ok()
    End Function

    <HttpPut("ordner")>
    Public Function Umbenennen(<FromBody> a As OrdnerAnfrage) As IActionResult
        Try
            _db.OrdnerUmbenennen(a.Alt, a.Neu)
        Catch ex As ArgumentException
            Return BadRequest(ex.Message)
        End Try
        Return Ok()
    End Function

    <HttpDelete("ordner")>
    Public Sub Loeschen(<FromQuery> pfad As String)
        _db.OrdnerLoeschen(pfad)
    End Sub

    <HttpPut("dateien/{id:long}/ordner")>
    Public Sub DateiVerschieben(id As Long, <FromBody> a As WertAnfrage)
        _db.DateiOrdnerSetzen(id, If(a?.Wert, ""))
    End Sub
End Class
