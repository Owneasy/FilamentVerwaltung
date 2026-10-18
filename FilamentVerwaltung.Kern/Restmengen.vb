''' <summary>Ergebnis der Restmengen-Prüfung für ein Fach bzw. eine Spule.</summary>
Public Class RestmengenHinweis
    Public Enum Stufe
        ''' <summary>Keine Spule zugeordnet – Restmenge unbekannt.</summary>
        Unbekannt
        Reicht
        ''' <summary>Reicht, aber mit weniger als 10 % Reserve.</summary>
        Knapp
        ReichtNicht
    End Enum

    Public Property Ergebnis As Stufe
    Public Property Text As String = ""
    ''' <summary>Andere Spule derselben Sorte mit genug Rest (Vorschlag), sonst Nothing.</summary>
    Public Property Alternative As Filament
End Class

''' <summary>Bedarf eines Filaments der Datei in einem Fach.</summary>
Public Class FilamentBedarf
    Public Property Bezeichnung As String = ""
    Public Property Gramm As Double
    ''' <summary>Fach am Drucker (Bambu AMS 0–15, extern 254; sonst <see cref="Drucker.SlotStandard"/>).</summary>
    Public Property Slot As Integer
End Class

''' <summary>
''' Reicht das Filament? Vergleicht den Bedarf laut Slicer (Bambu Studio: je Filament) mit dem Restgewicht der Spule,
''' die dem Fach zugeordnet ist. Nutzen mehrere Filamente dasselbe Fach, zählt die Summe.
''' </summary>
Public Module Restmengen
    ''' <summary>Reserve für Messungenauigkeit von Waage und Slicer (10 % bzw. mindestens 5 g).</summary>
    Private Function Reserve(gramm As Double) As Double
        Return Math.Max(5, gramm * 0.1)
    End Function

    Public Function Pruefen(bedarf As IEnumerable(Of FilamentBedarf), zuordnung As IDictionary(Of Integer, Long),
                            filamente As List(Of Filament)) As List(Of RestmengenHinweis)
        Dim ergebnis As New List(Of RestmengenHinweis)
        For Each fach In bedarf.Where(Function(b) b.Gramm > 0).GroupBy(Function(b) b.Slot)
            Dim gramm = fach.Sum(Function(b) b.Gramm)
            Dim name = String.Join(" + ", fach.Select(Function(b) b.Bezeichnung))
            Dim spuleId As Long = 0
            Dim spule = If(zuordnung IsNot Nothing AndAlso zuordnung.TryGetValue(fach.Key, spuleId),
                           filamente.FirstOrDefault(Function(f) f.Id = spuleId), Nothing)
            If spule Is Nothing Then
                ergebnis.Add(New RestmengenHinweis With {
                    .Ergebnis = RestmengenHinweis.Stufe.Unbekannt,
                    .Text = $"{name}: {gramm:N0} g – keine Spule zugeordnet, Restmenge unbekannt"})
                Continue For
            End If
            Dim sorte = $"{spule.Code} ({String.Join(" ", {spule.Hersteller, spule.Material, spule.Farbe}.Where(Function(x) Not String.IsNullOrWhiteSpace(x)))})"
            If spule.Restgewicht < gramm Then
                Dim alternative = filamente.Where(Function(f) f.Id <> spule.Id AndAlso f.Restgewicht >= gramm + Reserve(gramm) AndAlso
                                                       GleicheSorte(f, spule)).
                                            OrderBy(Function(f) f.Restgewicht).FirstOrDefault()   ' die kleinste, die reicht – angebrochene zuerst aufbrauchen
                ergebnis.Add(New RestmengenHinweis With {
                    .Ergebnis = RestmengenHinweis.Stufe.ReichtNicht,
                    .Alternative = alternative,
                    .Text = $"{name}: {sorte} hat noch {spule.Restgewicht:N0} g, gebraucht werden {gramm:N0} g" &
                            If(alternative IsNot Nothing, $" – gleiche Sorte mit genug Rest: {alternative.Code} ({alternative.Restgewicht:N0} g)", "")})
            ElseIf spule.Restgewicht < gramm + Reserve(gramm) Then
                ergebnis.Add(New RestmengenHinweis With {
                    .Ergebnis = RestmengenHinweis.Stufe.Knapp,
                    .Text = $"{name}: knapp – {sorte} hat noch {spule.Restgewicht:N0} g für {gramm:N0} g"})
            Else
                ergebnis.Add(New RestmengenHinweis With {
                    .Ergebnis = RestmengenHinweis.Stufe.Reicht,
                    .Text = $"{name}: reicht – {sorte}, noch {spule.Restgewicht:N0} g für {gramm:N0} g"})
            End If
        Next
        Return ergebnis
    End Function

    Private Function GleicheSorte(a As Filament, b As Filament) As Boolean
        Return String.Equals(a.Material.Trim(), b.Material.Trim(), StringComparison.OrdinalIgnoreCase) AndAlso
               String.Equals(a.Farbe.Trim(), b.Farbe.Trim(), StringComparison.OrdinalIgnoreCase) AndAlso
               String.Equals(a.Hersteller.Trim(), b.Hersteller.Trim(), StringComparison.OrdinalIgnoreCase)
    End Function
End Module
