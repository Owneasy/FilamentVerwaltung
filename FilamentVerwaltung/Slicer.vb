Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Text.Json.Nodes
Imports System.Threading

''' <summary>Ein installierter Slicer (zum Öffnen von Dateien).</summary>
Public Class SlicerInstallation
    Public Property Name As String = ""
    Public Property Exe As String = ""
End Class

''' <summary>Sucht installierte Slicer an den üblichen Orten.</summary>
Public Module SlicerSuche

    Public Function Installierte() As List(Of SlicerInstallation)
        Dim pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        Dim lokal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        Dim kandidaten As New List(Of (Name As String, Pfade As String()))() From {
            ("Bambu Studio", {Path.Combine(pf, "Bambu Studio", "bambu-studio.exe")}),
            ("OrcaSlicer", {Path.Combine(pf, "OrcaSlicer", "orca-slicer.exe"), Path.Combine(lokal, "OrcaSlicer", "orca-slicer.exe")}),
            ("PrusaSlicer", {Path.Combine(pf, "Prusa3D", "PrusaSlicer", "prusa-slicer.exe")})
        }
        Dim ergebnis As New List(Of SlicerInstallation)
        For Each k In kandidaten
            Dim exe = k.Pfade.FirstOrDefault(AddressOf File.Exists)
            If exe IsNot Nothing Then ergebnis.Add(New SlicerInstallation With {.Name = k.Name, .Exe = exe})
        Next
        ' Cura liegt in einem Ordner mit Versionsnummer
        Try
            For Each verz In Directory.GetDirectories(pf, "UltiMaker Cura*").Concat(Directory.GetDirectories(pf, "Ultimaker Cura*")).OrderByDescending(Function(o) o)
                Dim exe = Path.Combine(verz, "UltiMaker-Cura.exe")
                If Not File.Exists(exe) Then exe = Path.Combine(verz, "Ultimaker-Cura.exe")
                If File.Exists(exe) Then
                    ergebnis.Add(New SlicerInstallation With {.Name = Path.GetFileName(verz), .Exe = exe})
                    Exit For
                End If
            Next
        Catch ex As Exception
            ' Kein Zugriff auf den Programme-Ordner: Cura überspringen
        End Try
        Return ergebnis
    End Function

    Public Sub Oeffnen(slicer As SlicerInstallation, datei As String)
        Dim psi As New ProcessStartInfo(slicer.Exe) With {.UseShellExecute = False}
        psi.ArgumentList.Add(datei)
        Process.Start(psi)
    End Sub

End Module

''' <summary>Ein Profil aus Bambu Studio (Drucker, Prozess oder Filament).</summary>
Public Class SlicerProfil
    Public Property Art As String = ""
    Public Property Name As String = ""
    Public Property Pfad As String = ""
    Public Property IstBenutzerprofil As Boolean
    ''' <summary>Drucker, für die das Profil gedacht ist (leer = alle).</summary>
    Public Property Drucker As New List(Of String)

    ''' <summary>Name ohne das Anhängsel "@BBL A1" (für Anzeige und Dateinamen).</summary>
    Public ReadOnly Property KurzName As String
        Get
            Dim i = Name.IndexOf(" @", StringComparison.Ordinal)
            Return If(i > 0, Name.Substring(0, i), Name)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return Name & If(IstBenutzerprofil, "  (eigenes Profil)", "")
    End Function
End Class

''' <summary>Ergebnis eines Slice-Vorgangs.</summary>
Public Class SliceErgebnis
    Public Property Datei As String = ""
    Public Property Fehler As String = ""
    Public Property Protokoll As String = ""
    Public ReadOnly Property Erfolgreich As Boolean
        Get
            Return Fehler.Length = 0 AndAlso File.Exists(Datei)
        End Get
    End Property
End Class

''' <summary>
''' Steuert die installierte Bambu Studio über die Kommandozeile.
''' Wichtig: Die Kommandozeile löst die Profil-Vererbung ("inherits") nicht vollständig auf – dann fehlen z. B. Dichte
''' und Geschwindigkeitsgrenzen (Gewicht 0 g, falsche Zeit). Deshalb werden Prozess- und Filamentprofil hier selbst
''' aufgelöst und vollständig übergeben; das Druckerprofil bleibt das Original (sonst schlägt die Kompatibilitätsprüfung fehl).
''' </summary>
Public Class BambuStudio

    Public Shared ReadOnly Druckplatten As String() = {"Textured PEI Plate", "Cool Plate", "Engineering Plate", "High Temp Plate"}

    Public ReadOnly Property Exe As String
    Private ReadOnly _systemOrdner As String
    Private ReadOnly _benutzerOrdner As List(Of String)

    ' Index: Art → (Name → Profil-JSON + Pfad)
    Private _index As Dictionary(Of String, Dictionary(Of String, (Json As JsonObject, Pfad As String, Benutzer As Boolean)))
    Private ReadOnly _sperre As New Object()

    Private Sub New(exe As String, systemOrdner As String, benutzerOrdner As List(Of String))
        Me.Exe = exe
        _systemOrdner = systemOrdner
        _benutzerOrdner = benutzerOrdner
    End Sub

    ''' <summary>Findet Bambu Studio; Nothing, wenn nicht installiert.</summary>
    Public Shared Function Finden() As BambuStudio
        Dim slicer = SlicerSuche.Installierte().FirstOrDefault(Function(s) s.Name = "Bambu Studio")
        If slicer Is Nothing Then Return Nothing
        Dim system = Path.Combine(Path.GetDirectoryName(slicer.Exe), "resources", "profiles", "BBL")
        If Not Directory.Exists(system) Then Return Nothing
        Dim benutzerBasis = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BambuStudio", "user")
        Dim benutzer = If(Directory.Exists(benutzerBasis), Directory.GetDirectories(benutzerBasis).ToList(), New List(Of String))
        Return New BambuStudio(slicer.Exe, system, benutzer)
    End Function

    ' ------------------------------------------------------------------ Profile

    ''' <summary>Liest alle Profile einmal ein (dauert beim ersten Mal etwas – im Hintergrund aufrufen).</summary>
    Public Sub ProfileEinlesen()
        SyncLock _sperre
            If _index IsNot Nothing Then Return
            Dim index As New Dictionary(Of String, Dictionary(Of String, (Json As JsonObject, Pfad As String, Benutzer As Boolean)))
            For Each art In {"machine", "process", "filament"}
                Dim liste As New Dictionary(Of String, (Json As JsonObject, Pfad As String, Benutzer As Boolean))(StringComparer.OrdinalIgnoreCase)
                Einlesen(liste, Path.Combine(_systemOrdner, art), benutzer:=False)
                ' Benutzerprofile überschreiben gleichnamige Systemprofile
                For Each verz In _benutzerOrdner
                    Einlesen(liste, Path.Combine(verz, art), benutzer:=True)
                Next
                index(art) = liste
            Next
            _index = index
        End SyncLock
    End Sub

    Private Shared Sub Einlesen(liste As Dictionary(Of String, (Json As JsonObject, Pfad As String, Benutzer As Boolean)), verz As String, benutzer As Boolean)
        If Not Directory.Exists(verz) Then Return
        For Each datei In Directory.EnumerateFiles(verz, "*.json", SearchOption.AllDirectories)
            Try
                Dim json = TryCast(JsonNode.Parse(File.ReadAllText(datei, Encoding.UTF8)), JsonObject)
                If json Is Nothing Then Continue For
                Dim name = Wert(json, "name")
                If name.Length = 0 Then name = Path.GetFileNameWithoutExtension(datei)
                liste(name) = (json, datei, benutzer)
            Catch ex As Exception
                ' Defekte Profildatei überspringen
            End Try
        Next
    End Sub

    Private Shared Function Wert(json As JsonObject, schluessel As String) As String
        Dim knoten As JsonNode = Nothing
        If Not json.TryGetPropertyValue(schluessel, knoten) OrElse knoten Is Nothing Then Return ""
        If TypeOf knoten Is JsonValue Then Return knoten.ToString()
        Return ""
    End Function

    ''' <summary>Erstes Vorkommen eines Schlüssels entlang der Vererbungskette.</summary>
    Private Function GeerbterWert(art As String, name As String, schluessel As String) As JsonNode
        Dim tiefe = 0
        While name.Length > 0 AndAlso tiefe < 20
            Dim eintrag As (Json As JsonObject, Pfad As String, Benutzer As Boolean) = Nothing
            If Not _index(art).TryGetValue(name, eintrag) Then Return Nothing
            Dim knoten As JsonNode = Nothing
            If eintrag.Json.TryGetPropertyValue(schluessel, knoten) AndAlso knoten IsNot Nothing Then
                ' Leere Liste bei Benutzerprofilen heißt "wie das Basisprofil"
                If Not (eintrag.Benutzer AndAlso TypeOf knoten Is JsonArray AndAlso DirectCast(knoten, JsonArray).Count = 0) Then Return knoten
            End If
            name = Wert(eintrag.Json, "inherits")
            tiefe += 1
        End While
        Return Nothing
    End Function

    Private Function Profil(art As String, name As String) As SlicerProfil
        Dim eintrag = _index(art)(name)
        Dim p As New SlicerProfil With {.Art = art, .Name = name, .Pfad = eintrag.Pfad, .IstBenutzerprofil = eintrag.Benutzer}
        Dim drucker = TryCast(GeerbterWert(art, name, "compatible_printers"), JsonArray)
        If drucker IsNot Nothing Then p.Drucker.AddRange(drucker.Where(Function(d) d IsNot Nothing).Select(Function(d) d.ToString()))
        Return p
    End Function

    ''' <summary>Auswählbare Druckerprofile (nur Systemprofile – siehe Klassenbeschreibung).</summary>
    Public Function DruckerProfile() As List(Of SlicerProfil)
        ProfileEinlesen()
        Return _index("machine").Where(Function(e) Not e.Value.Benutzer AndAlso Wert(e.Value.Json, "instantiation") = "true").
                                 Select(Function(e) Profil("machine", e.Key)).OrderBy(Function(p) p.Name).ToList()
    End Function

    Public Function ProzessProfile(drucker As String) As List(Of SlicerProfil)
        Return Passende("process", drucker)
    End Function

    Public Function FilamentProfile(drucker As String) As List(Of SlicerProfil)
        Return Passende("filament", drucker)
    End Function

    Private Function Passende(art As String, drucker As String) As List(Of SlicerProfil)
        ProfileEinlesen()
        Return _index(art).Where(Function(e) e.Value.Benutzer OrElse Wert(e.Value.Json, "instantiation") = "true").
                           Select(Function(e) Profil(art, e.Key)).
                           Where(Function(p) p.Drucker.Count = 0 OrElse p.Drucker.Contains(drucker, StringComparer.OrdinalIgnoreCase)).
                           OrderByDescending(Function(p) p.IstBenutzerprofil).ThenBy(Function(p) p.Name).ToList()
    End Function

    ''' <summary>Löst die Vererbungskette auf und liefert ein vollständiges Profil (ohne "inherits").</summary>
    Public Function Aufloesen(art As String, name As String) As JsonObject
        ProfileEinlesen()
        Dim kette As New List(Of JsonObject)
        Dim aktuell = name
        While aktuell.Length > 0
            Dim eintrag As (Json As JsonObject, Pfad As String, Benutzer As Boolean) = Nothing
            If Not _index(art).TryGetValue(aktuell, eintrag) Then Throw New InvalidOperationException($"Profil ""{aktuell}"" wurde in Bambu Studio nicht gefunden.")
            If kette.Count > 20 Then Throw New InvalidOperationException($"Profil ""{name}"": Vererbung zu tief oder zirkulär.")
            kette.Insert(0, eintrag.Json)
            aktuell = Wert(eintrag.Json, "inherits")
        End While
        Dim ergebnis As New JsonObject()
        For Each teil In kette
            For Each feld In teil
                ergebnis(feld.Key) = feld.Value?.DeepClone()
            Next
        Next
        ergebnis.Remove("inherits")
        Return ergebnis
    End Function

    ''' <summary>Schlägt ein Filamentprofil für eine Spule vor (Hersteller und Material im Namen, eigene Profile bevorzugt).</summary>
    Public Shared Function FilamentVorschlagen(profile As List(Of SlicerProfil), spule As Filament) As SlicerProfil
        If spule Is Nothing OrElse profile.Count = 0 Then Return Nothing
        Dim woerter = (spule.Hersteller & " " & spule.Material).Replace("Bambu Lab", "Bambu").Replace("+", " Plus").
                      Split({" "c, "-"c, "("c, ")"c}, StringSplitOptions.RemoveEmptyEntries).
                      Select(Function(w) w.ToLowerInvariant()).Where(Function(w) w.Length > 1).Distinct().ToList()
        Dim typ = {"PETG", "PLA", "ABS", "ASA", "TPU", "PA", "PC", "PVA", "HIPS"}.FirstOrDefault(Function(t) spule.Material.ToUpperInvariant().Contains(t))
        Dim bewertet = profile.Select(Function(p)
                                          Dim n = p.KurzName.ToLowerInvariant()
                                          Dim punkte = woerter.Where(Function(w) n.Contains(w)).Count() * 10
                                          If typ IsNot Nothing AndAlso n.Contains(typ.ToLowerInvariant()) Then punkte += 5
                                          If p.IstBenutzerprofil Then punkte += 3
                                          If n.StartsWith("generic") Then punkte += 1
                                          Return (Profil:=p, Punkte:=punkte)
                                      End Function).OrderByDescending(Function(x) x.Punkte).First()
        Return If(bewertet.Punkte >= 5, bewertet.Profil, Nothing)
    End Function

    ' ------------------------------------------------------------------ Slicen

    ''' <summary>
    ''' Slict ein Modell zu einer druckfertigen 3MF (mit G-Code und Druckdaten). Die Datei liegt danach in ausgabeOrdner.
    ''' </summary>
    Public Async Function SlicenAsync(modell As String, drucker As SlicerProfil, prozess As SlicerProfil, filament As SlicerProfil,
                                      druckplatte As String, ausrichten As Boolean, farbe As Drawing.Color?, ausgabeName As String,
                                      abbruch As CancellationToken) As Task(Of SliceErgebnis)
        Dim arbeitsordner = Path.Combine(Path.GetTempPath(), "FilamentVerwaltung", "slice_" & Guid.NewGuid().ToString("N").Substring(0, 8))
        Directory.CreateDirectory(arbeitsordner)
        Dim ergebnis As New SliceErgebnis()
        Dim protokoll As New StringBuilder()
        Try
            Dim prozessJson = Aufloesen("process", prozess.Name)
            prozessJson("curr_bed_type") = druckplatte
            Dim filamentJson = Aufloesen("filament", filament.Name)
            If farbe.HasValue Then filamentJson("filament_colour") = New JsonArray(Drawing.ColorTranslator.ToHtml(Drawing.Color.FromArgb(farbe.Value.R, farbe.Value.G, farbe.Value.B)))
            Dim prozessDatei = Path.Combine(arbeitsordner, "process.json")
            Dim filamentDatei = Path.Combine(arbeitsordner, "filament.json")
            Dim ohneBom As New UTF8Encoding(False)
            File.WriteAllText(prozessDatei, prozessJson.ToJsonString(), ohneBom)
            File.WriteAllText(filamentDatei, filamentJson.ToJsonString(), ohneBom)

            Dim zielName = DateinameBereinigen(ausgabeName) & ".gcode.3mf"
            Dim psi As New ProcessStartInfo(Exe) With {
                .UseShellExecute = False, .CreateNoWindow = True, .WorkingDirectory = arbeitsordner,
                .RedirectStandardOutput = True, .RedirectStandardError = True,
                .StandardOutputEncoding = Encoding.UTF8, .StandardErrorEncoding = Encoding.UTF8}
            For Each a In {"--debug", "2", "--slice", "0", "--arrange", "1", "--orient", If(ausrichten, "1", "0"),
                           "--load-settings", drucker.Pfad & ";" & prozessDatei,
                           "--load-filaments", filamentDatei,
                           "--outputdir", arbeitsordner, "--export-3mf", zielName, modell}
                psi.ArgumentList.Add(a)
            Next
            protokoll.AppendLine($"Bambu Studio: {Exe}")
            protokoll.AppendLine($"Drucker: {drucker.Name} · Prozess: {prozess.Name} · Filament: {filament.Name} · Platte: {druckplatte}")

            Using p As New Process With {.StartInfo = psi}
                AddHandler p.OutputDataReceived, Sub(s, e)
                                                     If e.Data IsNot Nothing Then SyncLock protokoll : protokoll.AppendLine(e.Data) : End SyncLock
                                                 End Sub
                AddHandler p.ErrorDataReceived, Sub(s, e)
                                                    If e.Data IsNot Nothing Then SyncLock protokoll : protokoll.AppendLine(e.Data) : End SyncLock
                                                End Sub
                p.Start()
                p.BeginOutputReadLine()
                p.BeginErrorReadLine()
                Using zeitlimit = CancellationTokenSource.CreateLinkedTokenSource(abbruch)
                    zeitlimit.CancelAfter(TimeSpan.FromMinutes(15))
                    Try
                        Await p.WaitForExitAsync(zeitlimit.Token)
                    Catch ex As OperationCanceledException
                        Try
                            p.Kill(entireProcessTree:=True)
                        Catch
                        End Try
                        Throw New OperationCanceledException(If(abbruch.IsCancellationRequested, "Slicen abgebrochen.", "Slicen nach 15 Minuten abgebrochen."))
                    End Try
                End Using
            End Using

            Dim erzeugt = Path.Combine(arbeitsordner, zielName)
            If File.Exists(erzeugt) Then
                ergebnis.Datei = erzeugt
            Else
                ergebnis.Fehler = FehlerAusProtokoll(protokoll.ToString())
            End If
        Catch ex As OperationCanceledException
            ergebnis.Fehler = ex.Message
        Catch ex As Exception
            ergebnis.Fehler = ex.Message
        End Try
        ergebnis.Protokoll = protokoll.ToString()
        Try
            Dim logOrdner = Path.Combine(Path.GetTempPath(), "FilamentVerwaltung")
            File.WriteAllText(Path.Combine(logOrdner, "slicer.log"), ergebnis.Protokoll, Encoding.UTF8)
        Catch
        End Try
        Return ergebnis
    End Function

    ''' <summary>Übersetzt typische Fehlermeldungen von Bambu Studio.</summary>
    Private Shared Function FehlerAusProtokoll(protokoll As String) As String
        Dim zeilen = protokoll.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
        Dim fehler = zeilen.Where(Function(z) z.Contains("[error]") OrElse z.StartsWith("Plate ", StringComparison.Ordinal)).
                            Select(Function(z) If(z.Contains("]   "), z.Substring(z.LastIndexOf("]   ", StringComparison.Ordinal) + 4), z).Trim()).
                            Where(Function(z) z.Length > 0 AndAlso Not z.StartsWith("Slic3r::CLI::run found error", StringComparison.Ordinal)).ToList()
        Dim text = If(fehler.LastOrDefault(), "Bambu Studio hat keine Datei erzeugt.")
        If text.Contains("does not support filament") Then
            Return "Die gewählte Druckplatte passt nicht zum Filament (" & text & "). Bitte eine andere Druckplatte wählen, z. B. Textured PEI Plate."
        ElseIf text.Contains("not compatible") Then
            Return "Die Profile passen nicht zusammen (" & text & "). Bitte Prozess- und Filamentprofil für den gewählten Drucker wählen."
        ElseIf text.Contains("outside") OrElse text.Contains("Nothing to be sliced") Then
            Return "Das Modell passt nicht auf die Druckplatte oder ist leer (" & text & ")."
        End If
        Return text
    End Function

    Private Shared Function DateinameBereinigen(name As String) As String
        Dim ungueltig = Path.GetInvalidFileNameChars()
        Dim sauber = New String(name.Select(Function(c) If(ungueltig.Contains(c), "_"c, c)).ToArray()).Trim()
        Return If(sauber.Length > 120, sauber.Substring(0, 120), If(sauber.Length = 0, "Modell", sauber))
    End Function

    ''' <summary>Arbeitsordner eines Slice-Vorgangs entfernen (nachdem die Datei übernommen wurde).</summary>
    Public Shared Sub Aufraeumen(ergebnis As SliceErgebnis)
        Try
            Dim verz = Path.GetDirectoryName(ergebnis.Datei)
            If verz IsNot Nothing AndAlso Path.GetFileName(verz).StartsWith("slice_", StringComparison.Ordinal) Then Directory.Delete(verz, recursive:=True)
        Catch
            ' Temp-Ordner wird von Windows ohnehin aufgeräumt
        End Try
    End Sub

End Class
