''' <summary>
''' Eine Änderung des Restgewichts einer Spule: ein Druck, ein Nachwiegen oder eine Korrektur.
''' </summary>
Public Class Buchung

    Public Const ArtDruck As String = "Druck"
    Public Const ArtWiegen As String = "Wiegen"
    Public Const ArtKorrektur As String = "Korrektur"

    Public Property Id As Long
    Public Property FilamentId As Long
    Public Property Datum As Date = Date.Now
    Public Property Art As String = ArtDruck
    ''' <summary>Verbrauchte Gramm (negativ = Gewicht wurde erhöht, z. B. Korrektur nach oben).</summary>
    Public Property Gramm As Integer
    Public Property Bezeichnung As String = ""
    Public Property DateiId As Long?
    ''' <summary>Druckzeit in Sekunden.</summary>
    Public Property Druckzeit As Integer?
    ''' <summary>Kosten zum Zeitpunkt der Buchung (Gramm × Preis pro Gramm der Spule).</summary>
    Public Property Kosten As Decimal
    Public Property Notiz As String = ""
    ''' <summary>Dateiname des Kamerabilds vom Druckende (Ordner "Fotos"), leer = keins.</summary>
    Public Property Foto As String = ""
    ''' <summary>Drucker, auf dem gedruckt wurde (Name; leer bei Wiegen/Korrektur oder unbekannt).</summary>
    Public Property Drucker As String = ""
    ''' <summary>Fehldruck: abgebrochen oder misslungen (für die Fehldruck-Statistik).</summary>
    Public Property Fehldruck As Boolean

    ' --- Anzeige (aus den verknüpften Tabellen geladen)
    Public Property FilamentCode As String = ""
    Public Property FilamentText As String = ""
    Public Property DateiName As String = ""

    Public ReadOnly Property IstDruck As Boolean
        Get
            Return Art = ArtDruck
        End Get
    End Property

    Public Function Passt(suchbegriff As String) As Boolean
        For Each feld In {Bezeichnung, FilamentCode, FilamentText, DateiName, Notiz, Art}
            If feld IsNot Nothing AndAlso feld.IndexOf(suchbegriff, StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        Next
        Return False
    End Function

    Public Function Kopie() As Buchung
        Return DirectCast(Me.MemberwiseClone(), Buchung)
    End Function

End Class
