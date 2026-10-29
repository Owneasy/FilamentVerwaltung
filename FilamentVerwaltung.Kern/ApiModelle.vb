''' <summary>
''' Anfragen und Antworten der Server-API (JSON), gemeinsam für Server und App.
''' </summary>
Public Module Api
    ''' <summary>Größe eines Upload-Stücks – deutlich unter der 100-MB-Grenze von Cloudflare.</summary>
    Public Const UploadStueckBytes As Integer = 16 * 1024 * 1024
End Module

''' <summary>Live-Zustand eines überwachten Druckers.</summary>
Public Class DruckerLive
    Public Property DruckerId As Long
    Public Property Status As DruckerStatus
    Public Property Endungen As String() = Array.Empty(Of String)()
End Class

Public Class SendenAnfrage
    Public Property DateiId As Long
    Public Property Optionen As New SendeOptionen()
End Class

''' <summary>Fortschritt eines Druckauftrags, den der Server an den Drucker sendet.</summary>
Public Class SendeAuftragInfo
    Public Property Id As String = ""
    ''' <summary>0..1</summary>
    Public Property Fortschritt As Double
    Public Property Fertig As Boolean
    ''' <summary>Fehlermeldung, leer = kein Fehler.</summary>
    Public Property Fehler As String = ""
End Class

Public Class ServerInfo
    Public Property App As String = "Filament-Verwaltung"
    Public Property Version As String = ""
    ''' <summary>
    ''' Funktionen, die der Server kann (seit 0.2.6; ältere Server liefern keine Liste). Die App fragt neue Funktionen nur ab,
    ''' wenn sie hier stehen – so gibt es mit einem älteren Server keine Fehler.
    ''' </summary>
    Public Property Funktionen As New List(Of String)
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

''' <summary>Ein Gerät, das Push-Benachrichtigungen bekommt (Web Push: Endpunkt beim Push-Dienst und Schlüssel des Browsers).</summary>
Public Class PushAbo
    Public Property Endpunkt As String = ""
    ''' <summary>Öffentlicher Schlüssel des Browsers (Base64url, 65 Bytes).</summary>
    Public Property P256dh As String = ""
    ''' <summary>Gemeinsames Geheimnis (Base64url, 16 Bytes).</summary>
    Public Property Auth As String = ""
    Public Property Geraet As String = ""
End Class

''' <summary>Namen in <see cref="ServerInfo.Funktionen"/>.</summary>
Public Module ServerFunktionen
    Public Const Wartung As String = "wartung"
    Public Const Benachrichtigungen As String = "benachrichtigungen"
    Public Const HomeAssistant As String = "homeassistant"
    Public Const Warteschlange As String = "warteschlange"
    Public Const Ordner As String = "ordner"
    Public Const Vorschaubilder As String = "vorschaubilder"
    Public Const Stromkosten As String = "stromkosten"
    Public Const StromAufheizen As String = "strom-aufheizen"

    ''' <summary>Alles, was dieser Server kann (nur beim Server selbst verwendet).</summary>
    Public ReadOnly Alle As String() = {Wartung, Benachrichtigungen, HomeAssistant, Warteschlange, Ordner, Vorschaubilder, Stromkosten, StromAufheizen}

    ''' <summary>Gibt es die Funktion bei dieser Datenquelle? Lokale Datenbank: immer; Server: siehe ServerDatenquelle.Kann.</summary>
    Public Function Verfuegbar(db As IDatenquelle, funktion As String) As Boolean
        Dim server = TryCast(db, ServerDatenquelle)
        Return server Is Nothing OrElse server.Kann(funktion)
    End Function
End Module

''' <summary>Ordner umbenennen oder verschieben.</summary>
Public Class OrdnerAnfrage
    Public Property Alt As String = ""
    Public Property Neu As String = ""
End Class
