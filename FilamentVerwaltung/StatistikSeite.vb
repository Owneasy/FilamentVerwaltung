Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

''' <summary>Prognose für eine Spule: wann ist sie beim bisherigen Verbrauch leer?</summary>
Public Class SpulenPrognose
    Public Property Code As String = ""
    Public Property Filament As String = ""
    Public Property Restgewicht As Integer
    ''' <summary>Durchschnittlicher Verbrauch pro Woche (Basis: letzte 90 Tage).</summary>
    Public Property ProWoche As Double
    Public Property TageBisLeer As Double
    Public Property LeerAm As Date
    Public ReadOnly Property ReichweiteText As String
        Get
            Return Statistik.ReichweiteText(TageBisLeer)
        End Get
    End Property
End Class



''' <summary>
''' Seite "Statistik": Verbrauch und Kosten pro Monat, Verbrauch nach Material, Reichweite je Spule.
''' </summary>
Public Class StatistikSeite
    Inherits UserControl
    Implements ISeite

    Private ReadOnly _db As IDatenquelle
    Private ReadOnly kzSchnitt As New KennzahlKarte("Ø Verbrauch pro Monat", Theme.SymSpule, Theme.Akzent)
    Private ReadOnly kzReichweite As New KennzahlKarte("Reichweite des Lagers", Theme.SymUhr, Theme.Gruen)
    Private ReadOnly kzKostenJahr As New KennzahlKarte("Kosten dieses Jahr", Theme.SymGeld, Theme.Lila)
    Private ReadOnly kzDruckeJahr As New KennzahlKarte("Drucke dieses Jahr", Theme.SymDrucker, Theme.Gelb)
    Private ReadOnly kzFehldrucke As New KennzahlKarte("Fehldrucke dieses Jahr", Theme.SymWarnung, Theme.Rot)
    Private ReadOnly monate As New SaeulenDiagramm With {.LeerText = "Noch kein Verbrauch erfasst – buche Drucke oder wiege Spulen nach."}
    Private ReadOnly materialien As New BalkenDiagramm With {.LeerText = "Noch kein Verbrauch erfasst."}
    Private ReadOnly karteMonate As DiagrammKarte
    Private ReadOnly karteMaterial As DiagrammKarte
    Private ReadOnly fehlMonate As New SaeulenDiagramm With {.LeerText = "Keine Fehldrucke in den letzten 12 Monaten."}
    Private ReadOnly fehlDrucker As New BalkenDiagramm With {.LeerText = "Keine Fehldrucke in den letzten 12 Monaten."}
    Private ReadOnly karteFehlMonate As DiagrammKarte
    Private ReadOnly karteFehlDrucker As DiagrammKarte
    Private ReadOnly fehlGruende As New BalkenDiagramm With {.LeerText = "Keine Fehldrucke mit Grund in den letzten 12 Monaten."}
    Private ReadOnly karteFehlGruende As DiagrammKarte
    Private ReadOnly prognose As New WebTabelle()
    Private ReadOnly prognoseQuelle As New BindingSource()
    Private kartePrognose As TabellenKarte

    Public Sub New(db As IDatenquelle)
        _db = db
        karteMonate = New DiagrammKarte("Verbrauch pro Monat", "Letzte 12 Monate · Drucke und Nachwiegen", monate, "Verbrauch")
        karteMaterial = New DiagrammKarte("Verbrauch nach Material", "Letzte 12 Monate · Anteil am Gesamtverbrauch", materialien, "Verbrauch")
        karteFehlMonate = New DiagrammKarte("Fehldrucke pro Monat", "Letzte 12 Monate · verlorenes Filament (abgebrochene und markierte Drucke)", fehlMonate, "Verlust")
        karteFehlGruende = New DiagrammKarte("Fehldrucke nach Grund", "Letzte 12 Monate · was am häufigsten schiefgeht", fehlGruende, "Fehldrucke")
        karteFehlDrucker = New DiagrammKarte("Fehldrucke je Drucker", "Letzte 12 Monate · Anzahl und Anteil an allen Drucken", fehlDrucker, "Fehldrucke")
        OberflaecheAufbauen()
    End Sub

    Public Sub Aktivieren() Implements ISeite.Aktivieren
        prognose.Focus()
    End Sub

    Public Sub Neuladen() Implements ISeite.Neuladen
        DatenLaden()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        Dock = DockStyle.Fill
        BackColor = Theme.Seite
        Font = Theme.Schrift
        Padding = New Padding(28, 24, 28, 24)

        Dim seite As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 5, .Margin = New Padding(0)}
        seite.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        seite.RowStyles.Add(New RowStyle(SizeType.Absolute, 134))
        seite.RowStyles.Add(New RowStyle(SizeType.Percent, 36))
        seite.RowStyles.Add(New RowStyle(SizeType.Percent, 32))
        seite.RowStyles.Add(New RowStyle(SizeType.Percent, 32))

        seite.Controls.Add(New SeitenKopf("Statistik", $"Verbrauch, Kosten und Reichweite – berechnet aus dem Verlauf (Durchschnitt: letzte {TageBasis} Tage)"), 0, 0)
        seite.Controls.Add(FilamentSeite.KennzahlZeile(kzSchnitt, kzReichweite, kzKostenJahr, kzDruckeJahr, kzFehldrucke), 0, 1)

        Dim diagramme As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .Margin = New Padding(0, 0, 0, 16)}
        diagramme.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58))
        diagramme.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42))
        karteMonate.Margin = New Padding(0, 0, 8, 0)
        karteMaterial.Margin = New Padding(8, 0, 0, 0)
        diagramme.Controls.Add(karteMonate, 0, 0)
        diagramme.Controls.Add(karteMaterial, 1, 0)
        seite.Controls.Add(diagramme, 0, 2)

        Dim fehl As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1, .Margin = New Padding(0, 0, 0, 16)}
        fehl.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 40))
        fehl.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 30))
        fehl.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 30))
        karteFehlMonate.Margin = New Padding(0, 0, 8, 0)
        karteFehlDrucker.Margin = New Padding(8, 0, 8, 0)
        karteFehlGruende.Margin = New Padding(8, 0, 0, 0)
        fehl.Controls.Add(karteFehlMonate, 0, 0)
        fehl.Controls.Add(karteFehlDrucker, 1, 0)
        fehl.Controls.Add(karteFehlGruende, 2, 0)
        seite.Controls.Add(fehl, 0, 3)

        prognose.DataSource = prognoseQuelle
        prognose.SortSpalte = "TageBisLeer"
        prognose.Spalte("Code", "Spule", 0)
        prognose.Spalte("Filament", "Filament", 160, minBreite:=120)
        prognose.Spalte("Restgewicht", "Rest", 0, "#,##0 g", True)
        prognose.Spalte("ProWoche", "Ø pro Woche", 0, "#,##0 g", True)
        prognose.Spalte("TageBisLeer", "Leer in", 0, rechtsbuendig:=True)
        prognose.Spalte("LeerAm", "Voraussichtlich leer am", 0, "dd.MM.yyyy", True)
        prognose.Columns("Code").DefaultCellStyle.Font = Theme.SchriftFett
        AddHandler prognose.SortierungGeaendert, Sub(s, e) DatenLaden()
        AddHandler prognose.CellFormatting, AddressOf Prognose_CellFormatting
        kartePrognose = New TabellenKarte(prognose)
        Dim titel As New Label With {.Text = "Reichweite je Spule", .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark,
                                     .AutoSize = True, .Margin = New Padding(0, 4, 16, 0)}
        Dim hinweis As New Label With {.Text = $"Beim Verbrauch der letzten {TageBasis} Tage – nur Spulen, die in dieser Zeit genutzt wurden",
                                       .Font = Theme.SchriftKlein, .ForeColor = Theme.TextLeise, .AutoSize = True, .Margin = New Padding(0, 6, 0, 0)}
        kartePrognose.Werkzeug.Padding = New Padding(18, 12, 18, 10)
        kartePrognose.Werkzeug.Controls.Add(titel)
        kartePrognose.Werkzeug.Controls.Add(hinweis)
        seite.Controls.Add(kartePrognose, 0, 4)

        Controls.Add(seite)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Private Sub DatenLaden()
        Dim buchungen As List(Of Buchung)
        Dim filamente As List(Of Filament)
        Try
            buchungen = _db.BuchungenLaden()
            filamente = _db.AlleLaden()
        Catch ex As Exception
            MessageBox.Show(Me, "Die Statistik konnte nicht geladen werden." & Environment.NewLine & Environment.NewLine & ex.Message,
                            "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        Dim kultur = CultureInfo.CurrentCulture
        Dim heute = Date.Today
        Dim verbrauch = buchungen.Where(AddressOf IstVerbrauch).ToList()
        Dim drucke = buchungen.Where(Function(b) b.IstDruck).ToList()
        Dim basisStart = heute.AddDays(-TageBasis + 1)
        Dim basis = verbrauch.Where(Function(b) b.Datum.Date >= basisStart).ToList()
        Dim proTag = basis.Sum(Function(b) CDbl(b.Gramm)) / TageBasis

        ' --- Kennzahlen
        Dim gesamtRest = filamente.Sum(Function(f) CLng(f.Restgewicht))
        kzSchnitt.Setzen(DiagrammBasis.GrammKompakt(proTag * 30.4), $"Basis: {basis.Count} Einträge der letzten {TageBasis} Tage")
        kzReichweite.Setzen(If(proTag > 0, ReichweiteText(gesamtRest / proTag), "–"),
                            $"{DiagrammBasis.GrammKompakt(gesamtRest)} auf Lager")
        Dim jahresStart = New Date(heute.Year, 1, 1)
        kzKostenJahr.Setzen(verbrauch.Where(Function(b) b.Datum >= jahresStart).Sum(Function(b) b.Kosten).ToString("C2"),
                            $"{DiagrammBasis.GrammKompakt(verbrauch.Where(Function(b) b.Datum >= jahresStart).Sum(Function(b) CDbl(b.Gramm)))} verbraucht")
        Dim druckeJahr = drucke.Where(Function(b) b.Datum >= jahresStart).ToList()
        kzDruckeJahr.Setzen(druckeJahr.Count.ToString("N0"),
                            If(druckeJahr.Any(Function(b) b.Druckzeit.HasValue),
                               $"{druckeJahr.Sum(Function(b) CLng(If(b.Druckzeit, 0))) / 3600.0:N0} h Druckzeit", "ohne erfasste Druckzeit"))

        ' --- Verbrauch pro Monat (letzte 12 Monate, aktueller Monat zuletzt)
        Dim ersterMonat = New Date(heute.Year, heute.Month, 1).AddMonths(-11)
        Dim monatsWerte As New List(Of DiagrammWert)
        For i = 0 To 11
            Dim beginn = ersterMonat.AddMonths(i)
            Dim imMonat = verbrauch.Where(Function(b) b.Datum >= beginn AndAlso b.Datum < beginn.AddMonths(1)).ToList()
            Dim gramm = imMonat.Sum(Function(b) CDbl(b.Gramm))
            Dim anzahlDrucke = imMonat.Where(Function(b) b.IstDruck).Count()
            monatsWerte.Add(New DiagrammWert With {
                .Beschriftung = beginn.ToString("MMM", kultur).TrimEnd("."c),
                .Titel = beginn.ToString("MMMM yyyy", kultur),
                .Wert = gramm,
                .WertText = DiagrammBasis.GrammKompakt(gramm),
                .Zusatz = $"Kosten {imMonat.Sum(Function(b) b.Kosten):C2} · {anzahlDrucke} {If(anzahlDrucke = 1, "Druck", "Drucke")}"
            })
        Next
        ' Sparsam beschriften: Maximum und aktueller Monat
        Dim maximum = monatsWerte.Max(Function(w) w.Wert)
        If maximum > 0 Then monatsWerte.First(Function(w) w.Wert = maximum).Hervorheben = True
        If monatsWerte.Last().Wert > 0 Then monatsWerte.Last().Hervorheben = True
        karteMonate.Setzen(monatsWerte)

        ' --- Verbrauch nach Material (letzte 12 Monate), höchstens 6 Balken + "Andere"
        Dim materialVon = filamente.ToDictionary(Function(f) f.Id, Function(f) If(String.IsNullOrWhiteSpace(f.Material), "Ohne Angabe", f.Material))
        Dim zeitraum = verbrauch.Where(Function(b) b.Datum >= ersterMonat).ToList()
        Dim summe = zeitraum.Sum(Function(b) CDbl(b.Gramm))
        Dim gruppen = zeitraum.GroupBy(Function(b) If(materialVon.ContainsKey(b.FilamentId), materialVon(b.FilamentId), "Gelöschte Spulen"),
                                       StringComparer.OrdinalIgnoreCase).
                               Select(Function(gr) (Name:=gr.Key, Gramm:=gr.Sum(Function(b) CDbl(b.Gramm)), Kosten:=gr.Sum(Function(b) b.Kosten), Anzahl:=gr.Count())).
                               OrderByDescending(Function(x) x.Gramm).ToList()
        If gruppen.Count > 7 Then
            Dim rest = gruppen.Skip(6).ToList()
            gruppen = gruppen.Take(6).ToList()
            gruppen.Add((Name:="Andere", Gramm:=rest.Sum(Function(x) x.Gramm), Kosten:=rest.Sum(Function(x) x.Kosten), Anzahl:=rest.Sum(Function(x) x.Anzahl)))
        End If
        karteMaterial.Setzen(gruppen.Select(Function(x) New DiagrammWert With {
            .Beschriftung = x.Name,
            .Titel = x.Name,
            .Wert = x.Gramm,
            .WertText = $"{DiagrammBasis.GrammKompakt(x.Gramm)} · {If(summe > 0, x.Gramm / summe, 0):P0}",
            .Zusatz = $"Kosten {x.Kosten:C2} · {x.Anzahl} Einträge"
        }).ToList())

        FehldruckeAuswerten(drucke, ersterMonat, jahresStart, kultur)

        ' --- Reichweite je Spule
        Dim liste As New List(Of SpulenPrognose)
        Dim ohneVerbrauch = 0
        For Each f In filamente.Where(Function(x) x.Restgewicht > 0)
            ' Neuere Spulen: nur den Zeitraum seit dem Kauf zählen (mindestens eine Woche)
            Dim tage = Math.Max(7, Math.Min(TageBasis, (heute - f.Kaufdatum.Date).TotalDays + 1))
            Dim gramm = basis.Where(Function(b) b.FilamentId = f.Id).Sum(Function(b) CDbl(b.Gramm))
            If gramm <= 0 Then
                ohneVerbrauch += 1
                Continue For
            End If
            Dim spuleProTag = gramm / tage
            Dim tageBisLeer = f.Restgewicht / spuleProTag
            liste.Add(New SpulenPrognose With {
                .Code = f.Code,
                .Filament = $"{f.Hersteller} {f.Material} {f.Farbe}".Trim(),
                .Restgewicht = f.Restgewicht,
                .ProWoche = Math.Round(spuleProTag * 7),
                .TageBisLeer = tageBisLeer,
                .LeerAm = heute.AddDays(Math.Min(tageBisLeer, 3650))
            })
        Next
        prognoseQuelle.DataSource = prognose.Sortieren(liste)
        kartePrognose.FussLinks.Text = If(liste.Count = 0,
            $"In den letzten {TageBasis} Tagen wurde keine Spule genutzt.",
            $"{liste.Count} Spulen mit Verbrauch · {ohneVerbrauch} ohne Verbrauch in den letzten {TageBasis} Tagen")
    End Sub

    ''' <summary>Fehldrucke: Kennzahl (Jahr), Verlust pro Monat und Anzahl/Quote je Drucker (letzte 12 Monate).</summary>
    Private Sub FehldruckeAuswerten(drucke As List(Of Buchung), ersterMonat As Date, jahresStart As Date, kultur As CultureInfo)
        Dim jahr = drucke.Where(Function(b) b.Datum >= jahresStart).ToList()
        Dim fehlJahr = jahr.Where(Function(b) b.Fehldruck).ToList()
        kzFehldrucke.Setzen(fehlJahr.Count.ToString("N0"),
                            If(jahr.Count = 0, "noch keine Drucke",
                               $"{fehlJahr.Count / CDbl(jahr.Count):P0} der Drucke · {DiagrammBasis.GrammKompakt(fehlJahr.Sum(Function(b) CDbl(b.Gramm)))} · {fehlJahr.Sum(Function(b) b.Kosten):C2}"))

        Dim zeitraum = drucke.Where(Function(b) b.Datum >= ersterMonat).ToList()
        Dim werte As New List(Of DiagrammWert)
        For i = 0 To 11
            Dim beginn = ersterMonat.AddMonths(i)
            Dim imMonat = zeitraum.Where(Function(b) b.Datum >= beginn AndAlso b.Datum < beginn.AddMonths(1)).ToList()
            Dim fehl = imMonat.Where(Function(b) b.Fehldruck).ToList()
            Dim gramm = fehl.Sum(Function(b) CDbl(b.Gramm))
            Dim quote = If(imMonat.Count > 0, $" ({fehl.Count / CDbl(imMonat.Count):P0})", "")
            werte.Add(New DiagrammWert With {
                .Beschriftung = beginn.ToString("MMM", kultur).TrimEnd("."c),
                .Titel = beginn.ToString("MMMM yyyy", kultur),
                .Wert = gramm,
                .WertText = DiagrammBasis.GrammKompakt(gramm),
                .Zusatz = $"{fehl.Count} von {imMonat.Count} Drucken{quote} · Kosten {fehl.Sum(Function(b) b.Kosten):C2}"
            })
        Next
        Dim maximum = werte.Max(Function(w) w.Wert)
        If maximum > 0 Then werte.First(Function(w) w.Wert = maximum).Hervorheben = True
        If werte.Last().Wert > 0 Then werte.Last().Hervorheben = True
        karteFehlMonate.Setzen(werte)

        ' Je Drucker; von Hand erfasste Drucke ohne bekannten Drucker als eigene Gruppe
        Dim gruppen = zeitraum.GroupBy(Function(b) If(String.IsNullOrWhiteSpace(b.Drucker), "Ohne Angabe", b.Drucker), StringComparer.OrdinalIgnoreCase).
                               Select(Function(gr) (Name:=gr.Key, Alle:=gr.Count(), Fehl:=gr.Where(Function(b) b.Fehldruck).ToList())).
                               Where(Function(x) x.Fehl.Count > 0).
                               OrderByDescending(Function(x) x.Fehl.Count).ToList()
        karteFehlDrucker.Setzen(gruppen.Select(Function(x) New DiagrammWert With {
            .Beschriftung = x.Name,
            .Titel = x.Name,
            .Wert = x.Fehl.Count,
            .WertText = $"{x.Fehl.Count} · {x.Fehl.Count / CDbl(x.Alle):P0}",
            .Zusatz = $"{x.Fehl.Count} von {x.Alle} Drucken · {DiagrammBasis.GrammKompakt(x.Fehl.Sum(Function(b) CDbl(b.Gramm)))} · Kosten {x.Fehl.Sum(Function(b) b.Kosten):C2}"
        }).ToList())

        ' Nach Grund: höchstens 6 Balken + "Andere"; ohne Grund als eigene Gruppe
        Dim fehlZeitraum = zeitraum.Where(Function(b) b.Fehldruck).ToList()
        Dim gruende = fehlZeitraum.GroupBy(Function(b) If(String.IsNullOrWhiteSpace(b.FehldruckGrund), "Ohne Angabe", b.FehldruckGrund.Trim()), StringComparer.OrdinalIgnoreCase).
                                 Select(Function(gr) (Name:=gr.Key, Liste:=gr.ToList())).OrderByDescending(Function(x) x.Liste.Count).ToList()
        If gruende.Count > 7 Then
            Dim rest = gruende.Skip(6).SelectMany(Function(x) x.Liste).ToList()
            gruende = gruende.Take(6).ToList()
            gruende.Add((Name:="Andere", Liste:=rest))
        End If
        karteFehlGruende.Setzen(gruende.Select(Function(x) New DiagrammWert With {
            .Beschriftung = x.Name,
            .Titel = x.Name,
            .Wert = x.Liste.Count,
            .WertText = $"{x.Liste.Count} · {x.Liste.Count / CDbl(Math.Max(1, fehlZeitraum.Count)):P0}",
            .Zusatz = $"{DiagrammBasis.GrammKompakt(x.Liste.Sum(Function(b) CDbl(b.Gramm)))} · Kosten {x.Liste.Sum(Function(b) b.Kosten):C2}"
        }).ToList())
    End Sub

    Private Sub Prognose_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs)
        If e.RowIndex < 0 OrElse prognose.Columns(e.ColumnIndex).Name <> "TageBisLeer" Then Return
        Dim p = prognose.Eintrag(Of SpulenPrognose)(e.RowIndex)
        If p Is Nothing Then Return
        e.Value = p.ReichweiteText
        e.FormattingApplied = True
        ' Bald leer: hervorheben (Status-Farbe nur mit Text, nie allein)
        If p.TageBisLeer < 14 Then
            e.CellStyle.ForeColor = Theme.Rot
            e.CellStyle.Font = Theme.SchriftFett
        End If
    End Sub

End Class
