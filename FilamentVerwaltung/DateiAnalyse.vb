Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Xml

''' <summary>Vom Slicer berechnete Druckdaten.</summary>
Public Class DruckInfo
    Public Property Gramm As Double?
    Public Property Druckzeit As Integer?
    Public Property Material As String = ""
End Class

''' <summary>
''' Liest Filamentverbrauch, Druckzeit und Material aus G-Code (PrusaSlicer, OrcaSlicer, Bambu Studio, Cura)
''' und aus geslicten 3MF-Projekten (Bambu Studio / OrcaSlicer).
''' </summary>
Public Module DateiAnalyse

    ''' <summary>Wie viel vom Anfang und Ende einer G-Code-Datei gelesen wird (die Angaben stehen im Kopf oder am Ende).</summary>
    Private Const Leselaenge As Integer = 512 * 1024

    Private ReadOnly ReGrammGesamt As New Regex("^;\s*total filament (?:used|weight) \[g\]\s*[=:]\s*([\d.]+)", RegexOptions.IgnoreCase)
    Private ReadOnly ReGramm As New Regex("^;\s*filament used \[g\]\s*=\s*([\d.,\s]+)$", RegexOptions.IgnoreCase)
    Private ReadOnly ReCuraMeter As New Regex("^;\s*Filament used:\s*([\d.]+)\s*m", RegexOptions.IgnoreCase)
    Private ReadOnly ReZeitPrusa As New Regex("^;\s*estimated printing time \(normal mode\)\s*=\s*(.+)$", RegexOptions.IgnoreCase)
    Private ReadOnly ReZeitBambu As New Regex("total estimated time:\s*([^;]+)", RegexOptions.IgnoreCase)
    Private ReadOnly ReZeitCura As New Regex("^;TIME:(\d+)", RegexOptions.IgnoreCase)
    Private ReadOnly ReMaterial As New Regex("^;\s*filament_type\s*=\s*(.+)$", RegexOptions.IgnoreCase)
    Private ReadOnly ReDauerTeil As New Regex("(\d+)\s*([dhms])", RegexOptions.IgnoreCase)

    Public Function Analysieren(pfad As String) As DruckInfo
        Try
            Select Case Path.GetExtension(pfad).ToLowerInvariant()
                Case ".gcode", ".gco", ".g"
                    Return GcodeLesen(pfad)
                Case ".3mf"
                    Return DreiMfLesen(pfad)
            End Select
        Catch ex As Exception
            ' Unlesbare Datei: einfach ohne Druckdaten ablegen
        End Try
        Return New DruckInfo()
    End Function

    Private Function GcodeLesen(pfad As String) As DruckInfo
        Dim info As New DruckInfo()
        Dim grammGesamt As Double? = Nothing
        Dim grammSumme As Double? = Nothing
        Dim curaMeter As Double? = Nothing

        For Each zeile In KopfUndEnde(pfad)
            If zeile.Length = 0 OrElse zeile(0) <> ";"c Then Continue For
            Dim m As Match

            m = ReGrammGesamt.Match(zeile)
            If m.Success Then
                grammGesamt = Zahl(m.Groups(1).Value)
                Continue For
            End If
            m = ReGramm.Match(zeile)
            If m.Success Then
                ' Mehrere Extruder: "12.34, 0.00"
                grammSumme = m.Groups(1).Value.Split(","c).Select(Function(x) If(Zahl(x), 0.0)).Sum()
                Continue For
            End If
            m = ReCuraMeter.Match(zeile)
            If m.Success Then
                curaMeter = Zahl(m.Groups(1).Value)
                Continue For
            End If

            m = ReZeitPrusa.Match(zeile)
            If m.Success Then
                info.Druckzeit = DauerLesen(m.Groups(1).Value)
                Continue For
            End If
            m = ReZeitBambu.Match(zeile)
            If m.Success Then
                info.Druckzeit = DauerLesen(m.Groups(1).Value)
                Continue For
            End If
            m = ReZeitCura.Match(zeile)
            If m.Success AndAlso Not info.Druckzeit.HasValue Then
                info.Druckzeit = Integer.Parse(m.Groups(1).Value, CultureInfo.InvariantCulture)
                Continue For
            End If

            m = ReMaterial.Match(zeile)
            If m.Success Then
                info.Material = MaterialBereinigen(m.Groups(1).Value)
            End If
        Next

        If grammGesamt.HasValue Then
            info.Gramm = grammGesamt
        ElseIf grammSumme.HasValue Then
            info.Gramm = grammSumme
        ElseIf curaMeter.HasValue Then
            ' Cura gibt nur die Länge an: auf Gramm umrechnen (1,75 mm, Dichte 1,24 g/cm³ wie PLA)
            Dim querschnittMm2 = Math.PI * 0.875 * 0.875
            info.Gramm = curaMeter.Value * 1000.0 * querschnittMm2 * 1.24 / 1000.0
        End If
        If info.Gramm.HasValue Then info.Gramm = Math.Round(info.Gramm.Value, 2)
        Return info
    End Function

    ''' <summary>Liest Anfang und Ende der Datei zeilenweise (G-Code-Dateien können sehr groß sein).</summary>
    Private Iterator Function KopfUndEnde(pfad As String) As IEnumerable(Of String)
        Using fs As New FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.Read)
            Dim teile As New List(Of String)
            If fs.Length <= 2L * Leselaenge Then
                teile.Add(Lesen(fs, 0, CInt(fs.Length)))
            Else
                teile.Add(Lesen(fs, 0, Leselaenge))
                teile.Add(Lesen(fs, fs.Length - Leselaenge, Leselaenge))
            End If
            For Each teil In teile
                For Each zeile In teil.Split(ControlChars.Lf)
                    Yield zeile.TrimEnd(ControlChars.Cr)
                Next
            Next
        End Using
    End Function

    Private Function Lesen(fs As FileStream, start As Long, laenge As Integer) As String
        Dim puffer(laenge - 1) As Byte
        fs.Position = start
        Dim gelesen = 0
        While gelesen < laenge
            Dim n = fs.Read(puffer, gelesen, laenge - gelesen)
            If n <= 0 Then Exit While
            gelesen += n
        End While
        Return Encoding.UTF8.GetString(puffer, 0, gelesen)
    End Function

    ''' <summary>Bambu Studio / OrcaSlicer legen in geslicten 3MF-Dateien Metadata/slice_info.config ab.</summary>
    Private Function DreiMfLesen(pfad As String) As DruckInfo
        Dim info As New DruckInfo()
        Using zip = ZipFile.OpenRead(pfad)
            Dim eintrag = zip.GetEntry("Metadata/slice_info.config")
            If eintrag Is Nothing Then Return info
            Dim gramm = 0.0, sekunden = 0
            Dim materialien As New List(Of String)
            Using s = eintrag.Open(), r = XmlReader.Create(s)
                While r.Read()
                    If r.NodeType <> XmlNodeType.Element Then Continue While
                    If r.LocalName = "metadata" Then
                        Dim wert = r.GetAttribute("value")
                        Select Case r.GetAttribute("key")
                            Case "weight" : gramm += If(Zahl(wert), 0.0)
                            Case "prediction" : sekunden += CInt(If(Zahl(wert), 0.0))
                        End Select
                    ElseIf r.LocalName = "filament" Then
                        Dim typ = r.GetAttribute("type")
                        If Not String.IsNullOrWhiteSpace(typ) AndAlso Not materialien.Contains(typ) Then materialien.Add(typ)
                    End If
                End While
            End Using
            If gramm > 0 Then info.Gramm = Math.Round(gramm, 2)
            If sekunden > 0 Then info.Druckzeit = sekunden
            info.Material = String.Join(", ", materialien)
        End Using
        Return info
    End Function

    ''' <summary>"1d 2h 3m 4s" → Sekunden.</summary>
    Public Function DauerLesen(text As String) As Integer?
        Dim summe = 0
        Dim gefunden = False
        For Each m As Match In ReDauerTeil.Matches(text)
            gefunden = True
            Dim wert = Integer.Parse(m.Groups(1).Value, CultureInfo.InvariantCulture)
            Select Case Char.ToLowerInvariant(m.Groups(2).Value(0))
                Case "d"c : summe += wert * 86400
                Case "h"c : summe += wert * 3600
                Case "m"c : summe += wert * 60
                Case Else : summe += wert
            End Select
        Next
        Return If(gefunden, summe, CType(Nothing, Integer?))
    End Function

    Private Function MaterialBereinigen(wert As String) As String
        ' "PLA;PLA;PETG" (mehrere Extruder) → "PLA, PETG"
        Dim teile = wert.Split({";"c, ","c}, StringSplitOptions.RemoveEmptyEntries).Select(Function(x) x.Trim().Trim(""""c)).
                         Where(Function(x) x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
        Return String.Join(", ", teile)
    End Function

    Private Function Zahl(text As String) As Double?
        Dim d As Double
        If Double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, d) Then Return d
        Return Nothing
    End Function

End Module
