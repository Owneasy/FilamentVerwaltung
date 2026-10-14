Imports System.Globalization

''' <summary>
''' Eine abgelegte Datei (CAD-Modell, STL, 3MF, G-Code …). Die Datei selbst liegt im Ordner "Dateien" neben der Datenbank.
''' </summary>
Public Class Datei

    Public Shared ReadOnly ModellTypen As String() = {"STL", "3MF", "OBJ"}
    Public Shared ReadOnly GcodeTypen As String() = {"GCODE", "GCO", "G"}

    ''' <summary>Dateitypen für den Öffnen-Dialog.</summary>
    Public Const DialogFilter As String =
        "3D-Dateien (*.stl;*.3mf;*.obj;*.step;*.stp;*.gcode;*.bgcode;*.f3d;*.scad)|*.stl;*.3mf;*.obj;*.step;*.stp;*.gcode;*.gco;*.bgcode;*.f3d;*.scad|" &
        "Alle Dateien (*.*)|*.*"

    Public Property Id As Long
    ''' <summary>Ursprünglicher Dateiname (Anzeige).</summary>
    Public Property Name As String = ""
    ''' <summary>Dateiname im Dateien-Ordner (kann bei gleichen Namen abweichen).</summary>
    Public Property Pfad As String = ""
    ''' <summary>Endung ohne Punkt in Großbuchstaben, z. B. "STL".</summary>
    Public Property Typ As String = ""
    Public Property Groesse As Long
    Public Property Hinzugefuegt As Date = Date.Now
    ''' <summary>Filamentverbrauch laut Slicer (nur G-Code / geslicte 3MF).</summary>
    Public Property Gramm As Double?
    ''' <summary>Druckzeit laut Slicer in Sekunden.</summary>
    Public Property Druckzeit As Integer?
    Public Property Material As String = ""
    Public Property Notiz As String = ""

    Public ReadOnly Property IstModell As Boolean
        Get
            Return ModellTypen.Contains(Typ)
        End Get
    End Property

    Public ReadOnly Property HatDruckdaten As Boolean
        Get
            Return Gramm.HasValue AndAlso Gramm.Value > 0
        End Get
    End Property

    Public Function Passt(suchbegriff As String) As Boolean
        For Each feld In {Name, Typ, Material, Notiz}
            If feld IsNot Nothing AndAlso feld.IndexOf(suchbegriff, StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        Next
        Return False
    End Function

    Public Overrides Function ToString() As String
        Return Name
    End Function

End Class

''' <summary>
''' Formatierungen für die Anzeige.
''' </summary>
Public Module Anzeige

    Public Function DauerText(sekunden As Integer?) As String
        If Not sekunden.HasValue OrElse sekunden.Value <= 0 Then Return ""
        Dim t = TimeSpan.FromSeconds(sekunden.Value)
        Dim stunden = CInt(Math.Floor(t.TotalHours))
        If stunden > 0 Then Return $"{stunden} h {t.Minutes:00} min"
        If t.Minutes > 0 Then Return $"{t.Minutes} min"
        Return $"{t.Seconds} s"
    End Function

    Public Function GroesseText(bytes As Long) As String
        If bytes < 1024 Then Return $"{bytes} B"
        If bytes < 1024L * 1024 Then Return (bytes / 1024.0).ToString("N0") & " KB"
        If bytes < 1024L * 1024 * 1024 Then Return (bytes / 1024.0 / 1024.0).ToString("N1") & " MB"
        Return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("N2") & " GB"
    End Function

    Public Function GrammText(gramm As Double?) As String
        If Not gramm.HasValue OrElse gramm.Value <= 0 Then Return ""
        Return gramm.Value.ToString(If(gramm.Value < 10, "N1", "N0"), CultureInfo.CurrentCulture) & " g"
    End Function

End Module
