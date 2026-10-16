''' <summary>
''' Anbindung an Home Assistant über MQTT (Einstellungen auf dem Server). Der Server meldet Drucker und Lager
''' per MQTT-Discovery an – die Sensoren erscheinen in Home Assistant von selbst.
''' </summary>
Public NotInheritable Class HomeAssistantEinstellungen
    ''' <summary>"1" = an (Standard: aus).</summary>
    Public Const Aktiv As String = "HA.Aktiv"
    ''' <summary>MQTT-Broker, z. B. die IP von Home Assistant (Add-on "Mosquitto broker").</summary>
    Public Const Host As String = "HA.Host"
    ''' <summary>Standard 1883.</summary>
    Public Const Port As String = "HA.Port"
    Public Const Benutzer As String = "HA.Benutzer"
    Public Const Passwort As String = "HA.Passwort"
    ''' <summary>Discovery-Präfix von Home Assistant (Standard "homeassistant").</summary>
    Public Const Praefix As String = "HA.Praefix"

    Public Const StandardPort As Integer = 1883
    Public Const StandardPraefix As String = "homeassistant"

    ''' <summary>Gespeicherte Verbindung lesen (fehlende Werte: Standard).</summary>
    Public Shared Function Laden(db As IDatenquelle) As HaVerbindung
        Dim p As Integer
        Return New HaVerbindung With {
            .Aktiv = db.EinstellungLesen(Aktiv) = "1",
            .Host = If(db.EinstellungLesen(Host), "").Trim(),
            .Port = If(Integer.TryParse(db.EinstellungLesen(Port), p) AndAlso p > 0 AndAlso p < 65536, p, StandardPort),
            .Benutzer = If(db.EinstellungLesen(Benutzer), ""),
            .Passwort = If(db.EinstellungLesen(Passwort), ""),
            .Praefix = If(String.IsNullOrWhiteSpace(db.EinstellungLesen(Praefix)), StandardPraefix, db.EinstellungLesen(Praefix).Trim().Trim("/"c))
        }
    End Function

    Public Shared Sub Speichern(db As IDatenquelle, v As HaVerbindung)
        db.EinstellungSpeichern(Aktiv, If(v.Aktiv, "1", "0"))
        db.EinstellungSpeichern(Host, v.Host.Trim())
        db.EinstellungSpeichern(Port, v.Port.ToString())
        db.EinstellungSpeichern(Benutzer, v.Benutzer)
        db.EinstellungSpeichern(Passwort, v.Passwort)
        db.EinstellungSpeichern(Praefix, v.Praefix.Trim())
    End Sub
End Class

''' <summary>Verbindung zum MQTT-Broker von Home Assistant.</summary>
Public Class HaVerbindung
    Public Property Aktiv As Boolean
    Public Property Host As String = ""
    Public Property Port As Integer = HomeAssistantEinstellungen.StandardPort
    Public Property Benutzer As String = ""
    Public Property Passwort As String = ""
    Public Property Praefix As String = HomeAssistantEinstellungen.StandardPraefix

    ''' <summary>Ändert sich, sobald eine Einstellung anders ist (dann neu verbinden).</summary>
    Public Function Kennung() As String
        Return String.Join(vbLf, Aktiv, Host, Port, Benutzer, Passwort, Praefix)
    End Function
End Class

''' <summary>Wie es um die Verbindung zu Home Assistant steht (für die Anzeige in der App).</summary>
Public Class HaStatus
    Public Property Verbunden As Boolean
    Public Property Meldung As String = ""
End Class
