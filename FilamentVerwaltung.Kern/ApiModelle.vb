''' <summary>
''' Anfragen und Antworten der Server-API (JSON), gemeinsam für Server und App.
''' </summary>
Public Module Api
    ''' <summary>Größe eines Upload-Stücks – deutlich unter der 100-MB-Grenze von Cloudflare.</summary>
    Public Const UploadStueckBytes As Integer = 16 * 1024 * 1024
End Module

Public Class ServerInfo
    Public Property App As String = "Filament-Verwaltung"
    Public Property Version As String = ""
End Class

Public Class WertAnfrage
    Public Property Wert As String
End Class

Public Class LeerspulenAntwort
    Public Property Gewicht As Integer?
    Public Property EigenerWert As Boolean
End Class

Public Class RestgewichtAnfrage
    Public Property NeuesRestgewicht As Integer
    Public Property Art As String = ""
    Public Property Bezeichnung As String = ""
End Class

Public Class SpuleAnfrage
    Public Property FilamentId As Long?
End Class

Public Class UploadAntwort
    Public Property Id As String = ""
End Class

Public Class UploadFertigAnfrage
    Public Property Name As String = ""
End Class

Public Class SicherungAnfrage
    ''' <summary>SicherungInfo.ArtManuell usw.</summary>
    Public Property Art As String = ""
    ''' <summary>Dateiname der Sicherung (beim Wiederherstellen).</summary>
    Public Property Name As String = ""
End Class
