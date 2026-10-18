''' <summary>
''' Ein Auftrag in der Druck-Warteschlange: eine Datei, optional mit Wunschdrucker und Platte.
''' Gestartet wird immer von Hand (Druckplatte muss frei sein) – über den Senden-Dialog mit Restmengen-Prüfung.
''' </summary>
Public Class Auftrag
    Public Property Id As Long
    Public Property DateiId As Long
    ''' <summary>Leer = egal, welcher passende Drucker.</summary>
    Public Property DruckerId As Long?
    ''' <summary>Platte der 3MF (Bambu), 1-basiert.</summary>
    Public Property Platte As Integer = 1
    Public Property Notiz As String = ""
    ''' <summary>Reihenfolge (kleiner = früher).</summary>
    Public Property Position As Integer
    Public Property Erstellt As Date = Date.Now

    ' --- Beim Laden ergänzt
    Public Property DateiName As String = ""
    Public Property Gramm As Double?
    Public Property Druckzeit As Integer?
    Public Property DruckerName As String = ""
End Class
