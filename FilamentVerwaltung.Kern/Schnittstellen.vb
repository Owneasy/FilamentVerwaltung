Imports System.Threading

''' <summary>
''' Woher die App ihre Daten bekommt: lokal aus der SQLite-Datei (<see cref="Datenbank"/>) oder vom Server.
''' Die Oberfläche spricht nur mit dieser Schnittstelle.
''' </summary>
Public Interface IDatenquelle

    ''' <summary>Für die Anzeige: Pfad der Datenbankdatei bzw. Adresse des Servers.</summary>
    ReadOnly Property Ort As String

    ' Einstellungen und Vorlagen
    Function EinstellungLesen(schluessel As String) As String
    Sub EinstellungSpeichern(schluessel As String, wert As String)
    Function LeerspulenVorlage(hersteller As String, ByRef eigenerWert As Boolean) As Integer?

    ' Filament
    Function AlleLaden() As List(Of Filament)
    Sub Speichern(f As Filament)
    Sub Loeschen(id As Long)
    Function CodeExistiert(code As String, ausserId As Long) As Boolean
    Function NaechsterCode() As String

    ' Buchungen
    Sub RestgewichtBuchen(filamentId As Long, neuesRestgewicht As Integer, art As String, bezeichnung As String)
    Sub DruckSpeichern(b As Buchung)
    Sub BuchungLoeschen(id As Long, zurueckbuchenJa As Boolean)
    Function BuchungenLaden() As List(Of Buchung)

    ' Drucker
    Function DruckerLaden() As List(Of Drucker)
    Sub DruckerSpeichern(d As Drucker)
    Sub DruckerLoeschen(id As Long)
    Function SpulenZuordnung(druckerId As Long) As Dictionary(Of Integer, Long)
    Sub SpuleZuordnen(druckerId As Long, slot As Integer, filamentId As Long?)

    ' Dateien
    Function DateienLaden() As List(Of Datei)
    Function DateiImportieren(quelle As String) As Datei
    Sub DateiNotizSpeichern(id As Long, notiz As String)
    Sub DateiLoeschen(d As Datei)
    ''' <summary>Pfad einer lokal lesbaren Kopie der Datei (beim Server wird sie dafür heruntergeladen).</summary>
    Function DateiPfad(d As Datei) As String
    ''' <summary>Lokaler Ordner mit den Dateien (beim Server: Zwischenspeicher der heruntergeladenen Kopien).</summary>
    ReadOnly Property DateienOrdner As String

    ' Sicherungen
    Function SicherungenLaden() As List(Of SicherungInfo)
    Function SicherungErstellen(art As String) As SicherungInfo
    Sub SicherungWiederherstellen(s As SicherungInfo)

End Interface

''' <summary>
''' Live-Anbindung der Drucker: lokal überwacht (<see cref="DruckerUeberwachung"/>) oder vom Server gemeldet.
''' Ereignisse kommen im Thread, der <see cref="Starten"/> aufgerufen hat (in der App: UI-Thread).
''' </summary>
Public Interface IDruckerDienst
    Inherits IDisposable

    ''' <summary>Der Zustand eines Druckers hat sich geändert.</summary>
    Event StatusGeaendert(druckerId As Long)
    ''' <summary>Ein Druck ist fertig oder wurde abgebrochen.</summary>
    Event DruckBeendet(ende As DruckEnde)

    Sub Starten()
    ''' <summary>Drucker neu übernehmen (nach Hinzufügen / Bearbeiten / Löschen).</summary>
    Sub Neuladen()
    Function Status(druckerId As Long) As DruckerStatus
    Function Protokoll(druckerId As Long) As DruckerProtokoll
    ''' <summary>Dateiendungen, die der Drucker zum Drucken annimmt (leer, wenn nicht überwacht).</summary>
    Function DruckbareEndungen(druckerId As Long) As String()
    ReadOnly Property AnzahlDruckend As Integer
    Function SendenAsync(druckerId As Long, dateiPfad As String, dateiId As Long, optionen As SendeOptionen,
                         fortschritt As IProgress(Of Double), abbruch As CancellationToken) As Task

End Interface
