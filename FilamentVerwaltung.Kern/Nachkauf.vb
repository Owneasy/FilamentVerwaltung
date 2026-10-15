''' <summary>Berechnungen für die Statistik (aus dem Verlauf: Drucke und Nachwiegen, ohne Korrekturen).</summary>
Public Module Statistik

    ''' <summary>Zeitraum für Durchschnitt und Prognose.</summary>
    Public Const TageBasis As Integer = 90

    Public Function IstVerbrauch(b As Buchung) As Boolean
        Return b.Art <> Buchung.ArtKorrektur AndAlso b.Gramm > 0
    End Function

    Public Function ReichweiteText(tage As Double) As String
        If Double.IsInfinity(tage) OrElse Double.IsNaN(tage) Then Return "–"
        If tage < 7 Then Return "unter 1 Woche"
        If tage < 60 Then Return $"ca. {Math.Round(tage / 7):0} Wochen"
        If tage < 730 Then Return $"ca. {Math.Round(tage / 30.4):0} Monate"
        Return "über 2 Jahre"
    End Function

End Module

''' <summary>Eine Filamentsorte (gleicher Hersteller, Material, Farbe, Durchmesser) mit Bestand und Reichweite.</summary>
Public Class SorteInfo

    Public Const StatusNachkaufen As String = "Nachkaufen"
    Public Const StatusBald As String = "Bald"
    Public Const StatusOk As String = "Genug"

    Public Property Schluessel As String = ""
    Public Property Bezeichnung As String = ""
    Public Property Durchmesser As Decimal
    ''' <summary>Spulen mit Restbestand.</summary>
    Public Property Spulen As Integer
    Public Property Rest As Integer
    Public Property ProMonat As Double
    <Text.Json.Serialization.JsonIgnore>
    Public Property ReichweiteTage As Double = Double.PositiveInfinity

    ''' <summary>Reichweite in Tagen für die API (JSON kennt kein Unendlich); Nothing = kein Verbrauch.</summary>
    Public ReadOnly Property Reichweite As Double?
        Get
            Return If(Double.IsInfinity(ReichweiteTage) OrElse Double.IsNaN(ReichweiteTage), CType(Nothing, Double?), Math.Round(ReichweiteTage, 1))
        End Get
    End Property
    Public Property LetzterPreis As Decimal
    Public Property PreisProKg As Decimal
    Public Property ShopLink As String = ""
    Public Property Status As String = StatusOk
    Public Property Ausgeblendet As Boolean
    ''' <summary>Zuletzt gekaufte Spule dieser Sorte (Vorlage für "Gekauft").</summary>
    Public Property Vorlage As Filament

    ''' <summary>Sortierschlüssel: Nachkaufen zuerst, dann Bald, dann Genug.</summary>
    Public ReadOnly Property Dringlichkeit As Integer
        Get
            Return If(Status = StatusNachkaufen, 0, If(Status = StatusBald, 1, 2))
        End Get
    End Property

    Public ReadOnly Property ReichweiteText As String
        Get
            If Rest <= 0 Then Return "leer"
            If ProMonat <= 0 Then Return "kein Verbrauch"
            Return Statistik.ReichweiteText(ReichweiteTage)
        End Get
    End Property

    Public ReadOnly Property ShopText As String
        Get
            Return If(ShopLink.Length > 0, "Shop öffnen", "")
        End Get
    End Property

End Class

''' <summary>Nachkauf: Bestand und Reichweite je Sorte (App-Seite "Nachkauf", Übersicht und Handy-Seite).</summary>
Public Module Nachkauf

    Public Const EinstellungMindestbestand As String = "Nachkauf.Mindestbestand"
    Public Const EinstellungAusgeblendet As String = "Nachkauf.Ausgeblendet"
    ''' <summary>Unter dieser Reichweite (Tage) wird nachgekauft bzw. gewarnt.</summary>
    Public Const TageNachkaufen As Integer = 30
    Public Const TageBald As Integer = 60

    ''' <summary>Mindestbestand und ausgeblendete Sorten aus den Einstellungen (auch für die Übersicht).</summary>
    Public Function EinstellungenLesen(db As IDatenquelle) As (Mindestbestand As Integer, Ausgeblendet As HashSet(Of String))
        Dim mindest As Integer
        If Not Integer.TryParse(db.EinstellungLesen(EinstellungMindestbestand), mindest) Then mindest = 250
        Dim aus = New HashSet(Of String)(If(db.EinstellungLesen(EinstellungAusgeblendet), "").
                                         Split({vbLf}, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase)
        Return (mindest, aus)
    End Function

    ''' <summary>Bestand, Verbrauch (letzte 90 Tage) und Nachkauf-Status je Sorte.</summary>
    Public Function SortenBerechnen(filamente As List(Of Filament), buchungen As List(Of Buchung), mindestbestand As Integer,
                                           ausgeblendet As HashSet(Of String)) As List(Of SorteInfo)
        Dim start = Date.Today.AddDays(-TageBasis + 1)
        Dim verbrauchJeSpule = buchungen.Where(Function(b) IstVerbrauch(b) AndAlso b.Datum.Date >= start).
                                         GroupBy(Function(b) b.FilamentId).ToDictionary(Function(gr) gr.Key, Function(gr) gr.Sum(Function(b) CDbl(b.Gramm)))

        Return filamente.GroupBy(Function(f) f.Sorte).Select(Function(gruppe)
            Dim neueste = gruppe.OrderByDescending(Function(f) f.Kaufdatum).ThenByDescending(Function(f) f.Id).First()
            Dim gramm = gruppe.Sum(Function(f) If(verbrauchJeSpule.ContainsKey(f.Id), verbrauchJeSpule(f.Id), 0.0))
            Dim s As New SorteInfo With {
                .Schluessel = gruppe.Key,
                .Bezeichnung = $"{neueste.Hersteller} {neueste.Material} {neueste.Farbe}".Trim(),
                .Durchmesser = neueste.Durchmesser,
                .Spulen = gruppe.Where(Function(f) f.Restgewicht > 0).Count(),
                .Rest = gruppe.Sum(Function(f) f.Restgewicht),
                .ProMonat = Math.Round(gramm / TageBasis * 30.4),
                .LetzterPreis = neueste.Preis,
                .PreisProKg = neueste.PreisProKg,
                .ShopLink = If(gruppe.OrderByDescending(Function(f) f.Kaufdatum).Select(Function(f) f.ShopLink).FirstOrDefault(Function(l) l.Length > 0), ""),
                .Vorlage = neueste,
                .Ausgeblendet = ausgeblendet.Contains(gruppe.Key)
            }
            If gramm > 0 Then s.ReichweiteTage = s.Rest / (gramm / TageBasis)
            s.Status = If(s.Rest < mindestbestand OrElse s.ReichweiteTage < TageNachkaufen, SorteInfo.StatusNachkaufen,
                          If(s.ReichweiteTage < TageBald, SorteInfo.StatusBald, SorteInfo.StatusOk))
            Return s
        End Function).ToList()
    End Function

    ''' <summary>Fertig sortiert für die Anzeige: Nachkaufen zuerst, darin nach Reichweite; ausgeblendete Sorten weg.</summary>
    Public Function Liste(db As IDatenquelle) As List(Of SorteInfo)
        Dim e = EinstellungenLesen(db)
        Return SortenBerechnen(db.AlleLaden(), db.BuchungenLaden(), e.Mindestbestand, e.Ausgeblendet).
            Where(Function(s) Not s.Ausgeblendet).
            OrderBy(Function(s) s.Dringlichkeit).ThenBy(Function(s) s.ReichweiteTage).ThenBy(Function(s) s.Rest).ToList()
    End Function

End Module