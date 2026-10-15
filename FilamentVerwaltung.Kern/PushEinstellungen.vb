''' <summary>Schalter für die Push-Benachrichtigungen (Einstellungen auf dem Server, gelten für alle Geräte).</summary>
Public Module PushEinstellungen
    Public Const Druckende As String = "Push.Druckende"
    Public Const SpuleKnapp As String = "Push.SpuleKnapp"
    ''' <summary>Grenze in Gramm für "Spule fast leer" (Standard 100).</summary>
    Public Const SpuleKnappGrenze As String = "Push.SpuleKnappGrenze"
    Public Const DruckerOffline As String = "Push.DruckerOffline"
    Public Const DruckerFehler As String = "Push.DruckerFehler"

    ''' <summary>Ohne Eintrag: alles an außer "Drucker nicht erreichbar".</summary>
    Public Function Standard(schluessel As String) As Boolean
        Return schluessel <> DruckerOffline
    End Function
End Module

''' <summary>Ein angemeldetes Gerät, wie es die App anzeigt (ohne Schlüssel).</summary>
Public Class PushGeraet
    Public Property Geraet As String = ""
    Public Property Erstellt As String = ""
End Class