Imports System.IO
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Text.Json.Serialization

''' <summary>
''' Womit sich die App verbindet: lokale Datenbankdatei oder Server.
''' Liegt auf diesem PC unter %AppData%\FilamentVerwaltung\verbindung.json (alles andere, auch die Einstellungen
''' der Oberfläche, liegt in der Datenbank bzw. auf dem Server). Token und Secret sind mit Windows (DPAPI) verschlüsselt.
''' </summary>
Public Class Verbindung

    Public Const ModusLokal As String = "Lokal"
    Public Const ModusServer As String = "Server"

    Public Property Modus As String = ModusLokal
    ''' <summary>Leer = Standardpfad in "Dokumente".</summary>
    Public Property DatenbankPfad As String = ""
    Public Property Intern As String = ""
    Public Property Extern As String = ""
    Public Property TokenVerschluesselt As String = ""
    Public Property CloudflareId As String = ""
    Public Property CloudflareGeheimnisVerschluesselt As String = ""

    Private Shared ReadOnly Property Datei As String
        Get
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FilamentVerwaltung", "verbindung.json")
        End Get
    End Property

    Public Shared ReadOnly Property StandardDatenbank As String
        Get
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FilamentVerwaltung", "filament.db")
        End Get
    End Property

    ''' <summary>Zwischenspeicher für Dateien vom Server.</summary>
    Public Shared ReadOnly Property Zwischenspeicher As String
        Get
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilamentVerwaltung", "Dateien")
        End Get
    End Property

    <JsonIgnore>
    Public ReadOnly Property IstServer As Boolean
        Get
            Return Modus = ModusServer
        End Get
    End Property

    <JsonIgnore>
    Public Property Token As String
        Get
            Return Entschluesseln(TokenVerschluesselt)
        End Get
        Set(value As String)
            TokenVerschluesselt = Verschluesseln(value)
        End Set
    End Property

    <JsonIgnore>
    Public Property CloudflareGeheimnis As String
        Get
            Return Entschluesseln(CloudflareGeheimnisVerschluesselt)
        End Get
        Set(value As String)
            CloudflareGeheimnisVerschluesselt = Verschluesseln(value)
        End Set
    End Property

    Public Function Zugang() As ServerZugang
        Return New ServerZugang With {.Intern = Intern, .Extern = Extern, .Token = Token,
                                      .CloudflareId = CloudflareId, .CloudflareGeheimnis = CloudflareGeheimnis}
    End Function

    ''' <summary>Öffnet die Datenquelle; beim Server wird die Verbindung geprüft (wirft bei Fehlern).</summary>
    Public Function Oeffnen() As IDatenquelle
        If Not IstServer Then
            Return New Datenbank(If(String.IsNullOrWhiteSpace(DatenbankPfad), StandardDatenbank, DatenbankPfad))
        End If
        Dim server As New ServerDatenquelle(Zugang(), Zwischenspeicher)
        Try
            server.Verbinden()
        Catch
            server.Dispose()
            Throw
        End Try
        Return server
    End Function

    Public Shared Function Laden() As Verbindung
        Try
            If File.Exists(Datei) Then Return If(JsonSerializer.Deserialize(Of Verbindung)(File.ReadAllText(Datei)), New Verbindung())
        Catch ex As Exception
            ' Beschädigte Datei: mit Standard (lokal) weitermachen
        End Try
        Return New Verbindung()
    End Function

    Public Sub Speichern()
        Directory.CreateDirectory(Path.GetDirectoryName(Datei))
        File.WriteAllText(Datei, JsonSerializer.Serialize(Me, New JsonSerializerOptions With {.WriteIndented = True}))
    End Sub

    Public Function Kopie() As Verbindung
        Return DirectCast(MemberwiseClone(), Verbindung)
    End Function

    Private Shared Function Verschluesseln(klartext As String) As String
        If String.IsNullOrEmpty(klartext) Then Return ""
        Return Convert.ToBase64String(ProtectedData.Protect(Text.Encoding.UTF8.GetBytes(klartext), Nothing, DataProtectionScope.CurrentUser))
    End Function

    Private Shared Function Entschluesseln(wert As String) As String
        If String.IsNullOrEmpty(wert) Then Return ""
        Try
            Return Text.Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(wert), Nothing, DataProtectionScope.CurrentUser))
        Catch ex As Exception
            Return ""   ' von einem anderen Windows-Benutzer verschlüsselt – neu eingeben
        End Try
    End Function

End Class
