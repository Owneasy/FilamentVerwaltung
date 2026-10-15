Imports Microsoft.AspNetCore.Mvc

''' <summary>Anmelden eines Geräts für Push-Benachrichtigungen (Handy-Seite) und Testnachricht.</summary>
<ApiController>
<Route("api/push")>
Public Class PushController
    Inherits ControllerBase

    Private ReadOnly _db As Datenbank
    Private ReadOnly _push As PushDienst

    Public Sub New(db As Datenbank, push As PushDienst)
        _db = db
        _push = push
    End Sub

    ''' <summary>Öffentlicher VAPID-Schlüssel – den braucht der Browser für die Anmeldung beim Push-Dienst.</summary>
    <HttpGet("schluessel")>
    Public Function Schluessel() As WertAnfrage
        Return New WertAnfrage With {.Wert = _push.OeffentlicherSchluessel}
    End Function

    ''' <summary>Angemeldete Geräte (nur Name und Datum) – für die Anzeige in der App.</summary>
    <HttpGet("geraete")>
    Public Function Geraete() As List(Of PushGeraet)
        Return _db.PushGeraeteLaden()
    End Function

    <HttpPost("abo")>
    Public Function Anmelden(<FromBody> a As PushAbo) As IActionResult
        If a Is Nothing OrElse Not Uri.IsWellFormedUriString(a.Endpunkt, UriKind.Absolute) OrElse
           Not a.Endpunkt.StartsWith("https://", StringComparison.OrdinalIgnoreCase) OrElse
           String.IsNullOrEmpty(a.P256dh) OrElse String.IsNullOrEmpty(a.Auth) Then Return BadRequest("Ungültige Anmeldung.")
        _db.PushAboSpeichern(a)
        Return Ok()
    End Function

    <HttpPost("abmelden")>
    Public Function Abmelden(<FromBody> a As PushAbo) As IActionResult
        _db.PushAboLoeschen(If(a?.Endpunkt, ""))
        Return Ok()
    End Function

    <HttpPost("test")>
    Public Async Function Test() As Task(Of WertAnfrage)
        Dim anzahl = Await _push.AnAlleAsync(New PushNachricht With {
            .Titel = "Filament-Verwaltung", .Text = "Test: Benachrichtigungen funktionieren.", .Ziel = "/#drucker", .Kennung = "test"})
        Return New WertAnfrage With {.Wert = anzahl.ToString()}
    End Function

End Class
