''' <summary>
''' Eine Filament-Spule.
''' </summary>
Public Class Filament

    ''' <summary>Interner Datenbankschlüssel (0 = noch nicht gespeichert).</summary>
    Public Property Id As Long

    ''' <summary>Eindeutige Identifikationsnummer, z. B. "FIL-0001" (z. B. für Etiketten auf der Spule).</summary>
    Public Property Code As String = ""

    Public Property Hersteller As String = ""
    Public Property Material As String = ""
    Public Property Farbe As String = ""

    ''' <summary>Durchmesser in mm (1,75 oder 2,85).</summary>
    Public Property Durchmesser As Decimal = 1.75D

    ''' <summary>Nettogewicht des Filaments bei Kauf in Gramm.</summary>
    Public Property Gewicht As Integer = 1000

    ''' <summary>Noch vorhandenes Filament in Gramm.</summary>
    Public Property Restgewicht As Integer = 1000

    ''' <summary>Gewicht der leeren Spule in Gramm (zum Nachwiegen).</summary>
    Public Property Spulengewicht As Integer = 0

    ''' <summary>Kaufpreis der Spule in Euro.</summary>
    Public Property Preis As Decimal

    Public Property Kaufdatum As Date = Date.Today
    Public Property Notiz As String = ""

    ''' <summary>Wo die Spule liegt, z. B. "Trockenbox 2".</summary>
    Public Property Lagerort As String = ""

    ''' <summary>Düsentemperatur in °C (0 = nicht angegeben).</summary>
    Public Property DuesenTemp As Integer

    ''' <summary>Betttemperatur in °C (0 = nicht angegeben).</summary>
    Public Property BettTemp As Integer

    ''' <summary>Trocknungshinweis, z. B. "4 h bei 55 °C".</summary>
    Public Property Trocknung As String = ""

    ''' <summary>Link zum Shop für den Nachkauf.</summary>
    Public Property ShopLink As String = ""

    ''' <summary>RFID-Kennung der Spule (Bambu-Chip), wird beim Einlegen ins AMS gelernt; leer = keine bekannt.</summary>
    Public Property RfidKennung As String = ""

    ''' <summary>Temperaturen kompakt, z. B. "215 / 60 °C".</summary>
    Public ReadOnly Property Temperaturen As String
        Get
            If DuesenTemp <= 0 AndAlso BettTemp <= 0 Then Return ""
            Return $"{If(DuesenTemp > 0, DuesenTemp.ToString(), "–")} / {If(BettTemp > 0, BettTemp.ToString(), "–")} °C"
        End Get
    End Property

    ''' <summary>Gleiche Sorte = gleicher Hersteller, Material, Farbe und Durchmesser (für den Nachkauf).</summary>
    Public ReadOnly Property Sorte As String
        Get
            Return String.Join("|", {Hersteller.Trim(), Material.Trim(), Farbe.Trim(), Durchmesser.ToString("0.00")}).ToLowerInvariant()
        End Get
    End Property

    ''' <summary>Preis pro Kilogramm.</summary>
    Public ReadOnly Property PreisProKg As Decimal
        Get
            If Gewicht <= 0 Then Return 0D
            Return Math.Round(Preis * 1000D / Gewicht, 2)
        End Get
    End Property

    ''' <summary>Anteil des Restgewichts am Nettogewicht (0..1).</summary>
    Public ReadOnly Property RestAnteil As Double
        Get
            If Gewicht <= 0 Then Return 0.0
            Return CDbl(Restgewicht) / Gewicht
        End Get
    End Property

    ''' <summary>Wert des noch vorhandenen Filaments.</summary>
    Public ReadOnly Property Restwert As Decimal
        Get
            If Gewicht <= 0 Then Return 0D
            Return Math.Round(Preis * Restgewicht / Gewicht, 2)
        End Get
    End Property

    Public Function Kopie() As Filament
        Return DirectCast(Me.MemberwiseClone(), Filament)
    End Function

    ''' <summary>Prüft, ob der Suchbegriff in einem der Textfelder vorkommt.</summary>
    Public Function Passt(suchbegriff As String) As Boolean
        For Each feld In {Code, Hersteller, Material, Farbe, Notiz, Lagerort}
            If feld IsNot Nothing AndAlso feld.IndexOf(suchbegriff, StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return True
            End If
        Next
        Return False
    End Function

End Class
