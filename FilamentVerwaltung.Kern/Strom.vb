Imports System.Globalization

''' <summary>
''' Stromkosten eines Drucks: Druckzeit × durchschnittliche Leistung des Druckers × Strompreis.
''' Die Leistung steht am Drucker (Watt), der Strompreis in den Einstellungen (€/kWh).
''' </summary>
Public NotInheritable Class Strom

    ''' <summary>Einstellung: Strompreis in €/kWh (invariant gespeichert, z. B. "0.32").</summary>
    Public Const PreisSchluessel As String = "Strompreis"

    Private Sub New()
    End Sub

    Public Shared Function Kosten(sekunden As Integer?, watt As Integer, preisProKwh As Decimal) As Decimal
        If Not sekunden.HasValue OrElse sekunden.Value <= 0 OrElse watt <= 0 OrElse preisProKwh <= 0D Then Return 0D
        Return Math.Round(CDec(sekunden.Value) / 3600D * watt / 1000D * preisProKwh, 2)
    End Function

    Public Shared Function PreisLesen(wert As String) As Decimal
        Dim p As Decimal
        If Decimal.TryParse(If(wert, "").Trim().Replace(","c, "."c), NumberStyles.Number, CultureInfo.InvariantCulture, p) AndAlso p > 0D Then Return p
        Return 0D
    End Function

    Public Shared Function PreisText(preis As Decimal) As String
        Return preis.ToString(CultureInfo.InvariantCulture)
    End Function

    ''' <summary>
    ''' Grober Durchschnitt beim Drucken (Heizbett, Hotend, Lüfter, Elektronik) als Vorschlag für neue Drucker.
    ''' Wer ein Messgerät hat, trägt besser den gemessenen Wert ein.
    ''' </summary>
    Public Shared Function TypischeWatt(name As String, typ As String) As Integer
        Dim n = If(name, "").ToUpperInvariant()
        If n.Contains("A1 MINI") OrElse n.Contains("A1MINI") Then Return 80
        If n.Contains("A1") Then Return 95
        If n.Contains("H2") Then Return 200
        If n.Contains("X1") Then Return 120
        If n.Contains("P1") OrElse n.Contains("P2") Then Return 110
        If n.Contains("MINI") Then Return 60
        If n.Contains("XL") Then Return 150
        If n.Contains("CORE ONE") OrElse n.Contains("COREONE") Then Return 100
        If n.Contains("MK4") OrElse n.Contains("MK3") Then Return 80
        If n.Contains("VORON") Then Return 200
        Select Case typ
            Case Drucker.TypBambu : Return 110
            Case Drucker.TypPrusaLink : Return 80
            Case Else : Return 120
        End Select
    End Function

End Class
