''' <summary>Ein angebundener 3D-Drucker.</summary>
Public Class Drucker

    Public Const TypBambu As String = "Bambu"
    Public Const TypPrusaLink As String = "PrusaLink"
    Public Const TypMoonraker As String = "Moonraker"
    Public Const TypOctoPrint As String = "OctoPrint"

    Public Shared ReadOnly Typen As (Wert As String, Name As String)() = {
        (TypBambu, "Bambu Lab (LAN, MQTT)"),
        (TypPrusaLink, "Prusa (PrusaLink)"),
        (TypMoonraker, "Klipper (Moonraker)"),
        (TypOctoPrint, "OctoPrint")}

    ''' <summary>Fach-Nummer für "keine Mehrfarbeinheit" bzw. die einzige Spule.</summary>
    Public Const SlotStandard As Integer = 0
    ''' <summary>Bambu: externe Spulenhalterung.</summary>
    Public Const SlotExtern As Integer = 254

    Public Property Id As Long
    Public Property Name As String = ""
    Public Property Typ As String = TypMoonraker
    ''' <summary>IP-Adresse oder Hostname, optional mit Port oder http(s)://.</summary>
    Public Property Adresse As String = ""
    ''' <summary>API-Key, Passwort oder Bambu-Zugangscode.</summary>
    Public Property Zugang As String = ""
    ''' <summary>Benutzername (PrusaLink, meist "maker").</summary>
    Public Property Benutzer As String = ""
    ''' <summary>Seriennummer (Bambu Lab).</summary>
    Public Property Seriennummer As String = ""
    Public Property Aktiv As Boolean = True

    Public ReadOnly Property TypName As String
        Get
            Return If(Typen.FirstOrDefault(Function(t) t.Wert = Typ).Name, Typ)
        End Get
    End Property

    ''' <summary>Basis-URL für HTTP-Drucker (ergänzt http:// und lässt einen angegebenen Port stehen).</summary>
    Public Function BasisUrl() As String
        Dim a = Adresse.Trim().TrimEnd("/"c)
        If Not a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) AndAlso
           Not a.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then a = "http://" & a
        Return a
    End Function

    Public Function Kopie() As Drucker
        Return DirectCast(Me.MemberwiseClone(), Drucker)
    End Function

End Class

Public Enum DruckerZustand
    Unbekannt
    Offline
    Bereit
    Druckt
    Pausiert
    Fertig
    Abgebrochen
    Fehler
End Enum

''' <summary>Ein Fach einer Mehrfarbeinheit (Bambu AMS) mit dem, was der Drucker darüber meldet.</summary>
Public Class DruckerFach
    Public Property Slot As Integer
    Public Property Beschreibung As String = ""
    ''' <summary>Farbe laut Drucker (z. B. aus dem RFID-Chip), leer wenn unbekannt.</summary>
    Public Property Farbe As Drawing.Color?
End Class

''' <summary>Eine Temperatur des Druckers in °C, Soll = Nothing oder 0 heißt: Heizung aus bzw. nur Sensor.</summary>
Public Class DruckerTemperatur
    Public Property Name As String = ""
    Public Property Ist As Double
    Public Property Soll As Double?

    Public ReadOnly Property Heizt As Boolean
        Get
            Return Soll.HasValue AndAlso Soll.Value > 0
        End Get
    End Property

    ''' <summary>Heizung an, aber noch mehr als 3 °C vom Ziel entfernt.</summary>
    Public ReadOnly Property HeiztAuf As Boolean
        Get
            Return Heizt AndAlso Math.Abs(Ist - Soll.Value) > 3
        End Get
    End Property

    Public ReadOnly Property WertText As String
        Get
            Return If(Heizt, $"{Ist:N0} / {Soll.Value:N0} °C", $"{Ist:N0} °C")
        End Get
    End Property
End Class

''' <summary>Momentaufnahme eines Druckers.</summary>
Public Class DruckerStatus
    Public Property Zustand As DruckerZustand = DruckerZustand.Unbekannt
    Public Property Datei As String = ""
    ''' <summary>Fortschritt 0..1.</summary>
    Public Property Fortschritt As Double
    Public Property RestSekunden As Integer?
    Public Property DauerSekunden As Integer?
    ''' <summary>Gesamtgewicht laut Slicer (Gramm), falls bekannt.</summary>
    Public Property GrammGeplant As Double?
    ''' <summary>Gesamte Filamentlänge laut Datei (mm), falls bekannt.</summary>
    Public Property FilamentMmGeplant As Double?
    ''' <summary>Bisher tatsächlich verbrauchte Länge (mm), falls der Drucker sie meldet.</summary>
    Public Property FilamentMmVerbraucht As Double?
    ''' <summary>Filamentvolumen laut Datei (cm³), falls bekannt (OctoPrint).</summary>
    Public Property VolumenCm3 As Double?
    ''' <summary>Aktives Fach der Mehrfarbeinheit (Bambu), sonst Nothing.</summary>
    Public Property Slot As Integer?
    Public Property Faecher As New List(Of DruckerFach)
    ''' <summary>Temperaturen (Düse, Bett, ggf. Kammer) – leer, wenn der Drucker keine meldet.</summary>
    Public Property Temperaturen As New List(Of DruckerTemperatur)
    ''' <summary>Aktuelle Schicht und Gesamtzahl laut Drucker (Bambu, Klipper mit SET_PRINT_STATS_INFO), sonst Nothing.</summary>
    Public Property Schicht As Integer?
    Public Property SchichtenGesamt As Integer?
    ''' <summary>Was der Drucker gerade tut (z. B. "Bett wird nivelliert") – leer, wenn er es nicht meldet.</summary>
    Public Property Phase As String = ""
    Public Property Meldung As String = ""

    ''' <summary>
    ''' Phase zum Anzeigen: die gemeldete, sonst während der Vorbereitung (0 %) aus den Temperaturen abgeleitet.
    ''' </summary>
    Public Function PhaseText() As String
        If Phase.Length > 0 Then Return Phase
        If Not DruckLaeuft OrElse Fortschritt > 0.005 Then Return ""
        Dim heizt = Temperaturen.Where(Function(t) t.HeiztAuf AndAlso t.Ist < t.Soll.Value).Select(Function(t) t.Name).ToList()
        If heizt.Contains("Düse") Then Return "Düse heizt auf"
        If heizt.Contains("Bett") Then Return "Heizbett heizt auf"
        If heizt.Count > 0 Then Return heizt(0) & " heizt auf"
        Return ""
    End Function

    Public ReadOnly Property DruckLaeuft As Boolean
        Get
            Return Zustand = DruckerZustand.Druckt OrElse Zustand = DruckerZustand.Pausiert
        End Get
    End Property

    Public Shared Function ZustandText(z As DruckerZustand) As String
        Select Case z
            Case DruckerZustand.Offline : Return "Offline"
            Case DruckerZustand.Bereit : Return "Bereit"
            Case DruckerZustand.Druckt : Return "Druckt"
            Case DruckerZustand.Pausiert : Return "Pausiert"
            Case DruckerZustand.Fertig : Return "Fertig"
            Case DruckerZustand.Abgebrochen : Return "Abgebrochen"
            Case DruckerZustand.Fehler : Return "Fehler"
            Case Else : Return "Verbinde …"
        End Select
    End Function
End Class

''' <summary>Ein beendeter Druck, der gebucht werden kann.</summary>
Public Class DruckEnde
    Public Property DruckerId As Long
    Public Property DruckerName As String = ""
    Public Property Datei As String = ""
    Public Property Abgebrochen As Boolean
    Public Property Fortschritt As Double
    Public Property DauerSekunden As Integer?
    Public Property GrammGeplant As Double?
    Public Property FilamentMmGeplant As Double?
    Public Property FilamentMmVerbraucht As Double?
    Public Property VolumenCm3 As Double?
    ''' <summary>Fach, aus dem überwiegend gedruckt wurde.</summary>
    Public Property Slot As Integer = Drucker.SlotStandard
    ''' <summary>Datei aus "Dateien", die von der App an den Drucker gesendet wurde (dann sind Gewicht und Datei sicher).</summary>
    Public Property DateiId As Long?
    Public Property Zeitpunkt As Date = Date.Now
End Class

''' <summary>Umrechnung Filamentlänge / Volumen → Gramm.</summary>
Public Module FilamentPhysik

    ''' <summary>Typische Dichten in g/cm³.</summary>
    Private ReadOnly Dichten As (Material As String, Dichte As Double)() = {
        ("PETG", 1.27), ("PET", 1.27), ("ABS", 1.04), ("ASA", 1.07), ("TPU", 1.21), ("TPE", 1.2), ("PA", 1.14), ("NYLON", 1.14),
        ("PC", 1.2), ("HIPS", 1.04), ("PVA", 1.23), ("PP", 0.9), ("CF", 1.3), ("PLA", 1.24)}

    Public Function Dichte(material As String) As Double
        If String.IsNullOrWhiteSpace(material) Then Return 1.24
        Dim m = material.ToUpperInvariant()
        For Each d In Dichten
            If m.Contains(d.Material) Then Return d.Dichte
        Next
        Return 1.24
    End Function

    Public Function GrammAusLaenge(mm As Double, durchmesserMm As Double, material As String) As Double
        Dim r = If(durchmesserMm > 0, durchmesserMm, 1.75) / 2
        Dim volumenCm3 = mm * Math.PI * r * r / 1000.0
        Return volumenCm3 * Dichte(material)
    End Function

    Public Function GrammAusVolumen(cm3 As Double, material As String) As Double
        Return cm3 * Dichte(material)
    End Function

End Module
