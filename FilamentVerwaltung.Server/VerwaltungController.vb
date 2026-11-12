Imports Microsoft.AspNetCore.Mvc

''' <summary>
''' Vorlagen aus dem Kern für die Verwaltung im Browser (Wartungspläne der Hersteller, typische Leistung der Drucker),
''' damit die Handy-Seite dieselben Werte nutzt wie die Windows-App.
''' </summary>
<ApiController>
<Route("api")>
Public Class VerwaltungController
    Inherits ControllerBase

    Private ReadOnly _db As Datenbank

    Public Sub New(db As Datenbank)
        _db = db
    End Sub

    ''' <summary>Herstellerpläne (passende zum Druckertyp zuerst, wenn ein Typ angegeben ist) und allgemeine Vorlagen.</summary>
    <HttpGet("wartung/plaene")>
    Public Function Plaene(<FromQuery> typ As String) As WartungsVorlagenAntwort
        Return New WartungsVorlagenAntwort With {
            .Plaene = If(String.IsNullOrEmpty(typ), WartungsPlaene.Liste.ToList(), WartungsPlaene.Fuer(typ)),
            .Vorlagen = WartungVorlagen.Liste.Select(Function(v) New PlanAufgabe With {.Name = v.Name, .Stunden = v.Stunden, .Tage = v.Tage}).ToList()}
    End Function

    ''' <summary>Legt die Aufgaben eines Herstellerplans an (gleichnamige werden übersprungen); Antwort: Zahl der neuen.</summary>
    <HttpPost("wartung/plaene")>
    Public Function PlanAnlegen(<FromBody> a As PlanAnfrage) As IActionResult
        Dim plan = WartungsPlaene.Liste.FirstOrDefault(Function(p) p.Modell = a?.Modell)
        If plan Is Nothing Then Return NotFound("Plan unbekannt.")
        If Not _db.DruckerLaden().Any(Function(d) d.Id = a.DruckerId) Then Return NotFound("Drucker unbekannt.")
        Return Ok(WartungsPlaene.Anlegen(_db, a.DruckerId, plan))
    End Function

    ''' <summary>Typische Leistung je Drucker (Vorschläge für die Stromkosten).</summary>
    <HttpGet("strom/vorschlaege")>
    Public Function StromVorschlaege() As List(Of StromVorschlag)
        Return _db.DruckerLaden().Select(Function(d)
                                             Dim heiz = Strom.TypischesAufheizen(d.Name, d.Typ)
                                             Return New StromVorschlag With {.DruckerId = d.Id, .Watt = Strom.TypischeWatt(d.Name, d.Typ),
                                                                             .AufheizWatt = heiz.Watt, .AufheizMinuten = heiz.Minuten}
                                         End Function).ToList()
    End Function

End Class

Public Class WartungsVorlagenAntwort
    Public Property Plaene As List(Of WartungsPlan)
    Public Property Vorlagen As List(Of PlanAufgabe)
End Class

Public Class PlanAnfrage
    Public Property DruckerId As Long
    Public Property Modell As String = ""
End Class

Public Class StromVorschlag
    Public Property DruckerId As Long
    Public Property Watt As Integer
    Public Property AufheizWatt As Integer
    Public Property AufheizMinuten As Integer
End Class
