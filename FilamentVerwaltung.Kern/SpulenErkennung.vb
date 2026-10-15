''' <summary>
''' Spulen im AMS erkennen: Farben aus Farbnamen, Vorschläge nach Material und Farbe für Fächer ohne Zuordnung.
''' Wird von der Überwachung (App lokal bzw. Server) genutzt; die Oberflächen zeigen nur das Ergebnis.
''' </summary>
Public Module SpulenErkennung

    ''' <summary>Ordnet bekannten Farbnamen eine Anzeigefarbe zu (für den Farbpunkt in der Tabelle).</summary>
    Public Function FarbeZuName(name As String) As Drawing.Color?
        If String.IsNullOrWhiteSpace(name) Then Return Nothing
        Dim n = name.ToLowerInvariant()
        Dim tabelle As (Wort As String, Farbe As String)() = {
            ("schwarz", "#111827"), ("black", "#111827"), ("weiß", "#FFFFFF"), ("weiss", "#FFFFFF"), ("white", "#FFFFFF"),
            ("natur", "#F5F0E1"), ("transparent", "#E0F2FE"), ("klar", "#E0F2FE"),
            ("dunkelgrau", "#4B5563"), ("grau", "#9CA3AF"), ("grey", "#9CA3AF"), ("gray", "#9CA3AF"), ("silber", "#C0C4CC"),
            ("gold", "#D4A72C"), ("dunkelblau", "#1E3A8A"), ("hellblau", "#7DD3FC"), ("blau", "#2563EB"), ("blue", "#2563EB"),
            ("türkis", "#14B8A6"), ("tuerkis", "#14B8A6"), ("dunkelgrün", "#166534"), ("hellgrün", "#86EFAC"),
            ("grün", "#16A34A"), ("gruen", "#16A34A"), ("green", "#16A34A"), ("gelb", "#FACC15"), ("yellow", "#FACC15"),
            ("orange", "#F97316"), ("dunkelrot", "#991B1B"), ("rot", "#DC2626"), ("red", "#DC2626"),
            ("rosa", "#F9A8D4"), ("pink", "#EC4899"), ("magenta", "#D946EF"), ("lila", "#9333EA"), ("violett", "#7C3AED"),
            ("purple", "#9333EA"), ("braun", "#92400E"), ("brown", "#92400E"), ("holz", "#B07A4A"), ("beige", "#E7D8B9")}
        For Each eintrag In tabelle
            If n.Contains(eintrag.Wort) Then Return Drawing.ColorTranslator.FromHtml(eintrag.Farbe)
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' Wie gut passt die Spule zum Fach? -1 = Material passt nicht; 2 = nur Material; 5 = Material und Farbe ähnlich.
    ''' </summary>
    Public Function Passung(fach As DruckerFach, f As Filament) As Integer
        Dim typ = Normalisieren(fach.Typ)
        If typ.Length = 0 Then Return -1
        Dim material = Normalisieren(f.Material)
        If material.Length = 0 OrElse Not (material.Contains(typ) OrElse typ.Contains(material)) Then Return -1
        Dim punkte = 2
        Dim spulenFarbe = FarbeZuName(f.Farbe)
        If fach.Farbe.HasValue AndAlso spulenFarbe.HasValue Then
            Dim a = fach.Farbe.Value, b = spulenFarbe.Value
            Dim abstand = Math.Sqrt((CInt(a.R) - b.R) ^ 2 + (CInt(a.G) - b.G) ^ 2 + (CInt(a.B) - b.B) ^ 2)
            If abstand < 90 Then punkte += 3 Else If abstand > 180 Then punkte -= 2
        End If
        Return punkte
    End Function

    Private Function Normalisieren(text As String) As String
        Return If(text, "").ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace("_", "")
    End Function

    ''' <summary>
    ''' Vorschlag je belegtem Fach ohne Zuordnung: die eine Spule mit Restbestand, die nach Material und Farbe
    ''' eindeutig am besten passt und noch in keinem anderen Fach steckt. Bei Gleichstand gibt es keinen Vorschlag.
    ''' </summary>
    Public Function Vorschlaege(faecher As IEnumerable(Of DruckerFach), filamente As IEnumerable(Of Filament),
                                zuordnung As Dictionary(Of Integer, Long)) As Dictionary(Of Integer, Long)
        Dim ergebnis As New Dictionary(Of Integer, Long)
        Dim vergeben = zuordnung.Values.ToHashSet()
        For Each fach In faecher.Where(Function(x) x.Belegt AndAlso Not zuordnung.ContainsKey(x.Slot))
            Dim kandidaten = filamente.
                Where(Function(f) f.Restgewicht > 0 AndAlso Not vergeben.Contains(f.Id) AndAlso
                                  (String.IsNullOrEmpty(f.RfidKennung) OrElse f.RfidKennung = fach.Kennung)).
                Select(Function(f) (Spule:=f, Punkte:=Passung(fach, f))).
                Where(Function(x) x.Punkte >= 5).
                OrderByDescending(Function(x) x.Punkte).ToList()
            If kandidaten.Count = 1 OrElse (kandidaten.Count > 1 AndAlso kandidaten(0).Punkte > kandidaten(1).Punkte) Then
                ergebnis(fach.Slot) = kandidaten(0).Spule.Id
            End If
        Next
        Return ergebnis
    End Function

End Module