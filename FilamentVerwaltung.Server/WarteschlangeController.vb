Imports Microsoft.AspNetCore.Mvc

''' <summary>Druck-Warteschlange: Aufträge in Reihenfolge; gestartet wird von Hand über "senden".</summary>
<ApiController>
<Route("api/warteschlange")>
Public Class WarteschlangeController
    Inherits ControllerBase

    Private ReadOnly _db As Datenbank

    Public Sub New(db As Datenbank)
        _db = db
    End Sub

    <HttpGet>
    Public Function Laden() As List(Of Auftrag)
        Return _db.WarteschlangeLaden()
    End Function

    <HttpPost>
    Public Function Speichern(<FromBody> a As Auftrag) As Auftrag
        _db.WarteschlangeSpeichern(a)
        Return _db.WarteschlangeLaden().FirstOrDefault(Function(x) x.Id = a.Id)
    End Function

    <HttpDelete("{id:long}")>
    Public Sub Loeschen(id As Long)
        _db.WarteschlangeLoeschen(id)
    End Sub

    <HttpPost("{id:long}/vor")>
    Public Sub Vor(id As Long)
        _db.WarteschlangeVerschieben(id, True)
    End Sub

    <HttpPost("{id:long}/zurueck")>
    Public Sub Zurueck(id As Long)
        _db.WarteschlangeVerschieben(id, False)
    End Sub
End Class
