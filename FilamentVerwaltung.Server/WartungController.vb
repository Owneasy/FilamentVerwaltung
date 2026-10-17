Imports Microsoft.AspNetCore.Mvc

''' <summary>Wartungsaufgaben der Drucker, Protokoll und Druckstunden.</summary>
<ApiController>
<Route("api")>
Public Class WartungController
    Inherits ControllerBase

    Private ReadOnly _db As Datenbank

    Public Sub New(db As Datenbank)
        _db = db
    End Sub

    <HttpGet("wartung")>
    Public Function Laden() As List(Of Wartung)
        Return _db.WartungLaden()
    End Function

    <HttpPost("wartung")>
    Public Function Speichern(<FromBody> w As Wartung) As Wartung
        _db.WartungSpeichern(w)
        Return _db.WartungLaden().FirstOrDefault(Function(x) x.Id = w.Id)
    End Function

    <HttpDelete("wartung/{id:long}")>
    Public Sub Loeschen(id As Long)
        _db.WartungLoeschen(id)
    End Sub

    <HttpPost("wartung/{id:long}/erledigt")>
    Public Sub Erledigt(id As Long, <FromBody> a As WertAnfrage)
        _db.WartungErledigt(id, a?.Wert)
    End Sub

    <HttpGet("wartung/protokoll")>
    Public Function Protokoll() As List(Of WartungEintrag)
        Return _db.WartungProtokollLaden()
    End Function

    ''' <summary>Zählerstand setzen (Sekunden als Text).</summary>
    <HttpPut("drucker/{id:long}/druckstunden")>
    Public Function DruckstundenSetzen(id As Long, <FromBody> a As WertAnfrage) As IActionResult
        Dim sekunden As Long
        If Not Long.TryParse(a?.Wert, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, sekunden) Then Return BadRequest("Ungültige Druckstunden.")
        _db.DrucksekundenSetzen(id, sekunden)
        Return Ok()
    End Function
End Class

''' <summary>Prüft regelmäßig, ob eine Wartung fällig geworden ist, und meldet sie einmal per Push.</summary>
Public Class WartungPruefung
    Inherits Microsoft.Extensions.Hosting.BackgroundService

    Private ReadOnly _db As Datenbank
    Private ReadOnly _regeln As PushRegeln

    Public Sub New(db As Datenbank, regeln As PushRegeln)
        _db = db
        _regeln = regeln
    End Sub

    Protected Overrides Async Function ExecuteAsync(abbruch As Threading.CancellationToken) As Task
        Try
            Await Task.Delay(TimeSpan.FromMinutes(1), abbruch)
            While Not abbruch.IsCancellationRequested
                Try
                    For Each w In _db.WartungLaden().Where(Function(x) x.Faellig AndAlso Not x.Gemeldet)
                        If _regeln.WartungFaellig(w) Then _db.WartungGemeldet(w.Id)
                    Next
                Catch ex As Exception
                    ' Beim nächsten Durchlauf erneut
                End Try
                Await Task.Delay(TimeSpan.FromMinutes(10), abbruch)
            End While
        Catch ex As OperationCanceledException
        End Try
    End Function
End Class
