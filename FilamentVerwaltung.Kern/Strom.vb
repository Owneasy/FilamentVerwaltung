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

    ''' <summary>
    ''' Stromkosten eines Drucks: einmal Aufheizen (Watt × Minuten) plus Drucken (Ø Watt × Druckzeit).
    ''' Ohne Druckzeit wird nichts berechnet (dann ist unklar, ob überhaupt gedruckt wurde).
    ''' </summary>
    Public Shared Function Kosten(sekunden As Integer?, watt As Integer, preisProKwh As Decimal,
                                  Optional aufheizWatt As Integer = 0, Optional aufheizMinuten As Integer = 0) As Decimal
        If Not sekunden.HasValue OrElse sekunden.Value <= 0 OrElse preisProKwh <= 0D Then Return 0D
        Return Math.Round((Wattstunden(sekunden.Value, watt) + Aufheizen(aufheizWatt, aufheizMinuten)) / 1000D * preisProKwh, 2)
    End Function

    ''' <summary>Energie fürs Drucken in Wh.</summary>
    Public Shared Function Wattstunden(sekunden As Integer, watt As Integer) As Decimal
        Return If(sekunden > 0 AndAlso watt > 0, CDec(sekunden) / 3600D * watt, 0D)
    End Function

    ''' <summary>Energie fürs Aufheizen in Wh (einmal pro Druck).</summary>
    Public Shared Function Aufheizen(watt As Integer, minuten As Integer) As Decimal
        Return If(watt > 0 AndAlso minuten > 0, CDec(watt) * minuten / 60D, 0D)
    End Function

    ''' <summary>
    ''' Grober Durchschnitt beim Aufheizen (vor allem das Heizbett) als Vorschlag.
    ''' Gemessen: Wh vom Einschalten bis zum ersten Layer ÷ Minuten × 60.
    ''' </summary>
    Public Shared Function TypischesAufheizen(name As String, typ As String) As (Watt As Integer, Minuten As Integer)
        Dim n = If(name, "").ToUpperInvariant()
        If n.Contains("A1 MINI") OrElse n.Contains("A1MINI") Then Return (150, 3)
        If n.Contains("A1") Then Return (500, 3)
        If n.Contains("H2") Then Return (900, 5)
        If n.Contains("X1") OrElse n.Contains("P1") OrElse n.Contains("P2") Then Return (600, 4)
        If n.Contains("MINI") Then Return (160, 3)
        If n.Contains("XL") Then Return (500, 6)
        If n.Contains("CORE ONE") OrElse n.Contains("COREONE") Then Return (300, 4)
        If n.Contains("MK4") OrElse n.Contains("MK3") Then Return (240, 4)
        If n.Contains("VORON") Then Return (700, 6)
        Select Case typ
            Case Drucker.TypBambu : Return (600, 4)
            Case Drucker.TypPrusaLink : Return (240, 4)
            Case Else : Return (350, 5)
        End Select
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
