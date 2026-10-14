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

''' <summary>Berechnungen für die Statistik (aus dem Verlauf: Drucke und Nachwiegen, ohne Korrekturen).</summary>
Public Module Statistik

    ''' <summary>Zeitraum für Durchschnitt und Prognose.</summary>
    Public Const TageBasis As Integer = 90

    Public Function IstVerbrauch(b As Buchung) As Boolean
        Return b.Art <> Buchung.ArtKorrektur AndAlso b.Gramm > 0
    End Function

    Public Function ReichweiteText(tage As Double) As String
        If Double.IsInfinity(tage) OrElse Double.IsNaN(tage) Then Return "–"
        If tage < 7 Then Return "unter 1 Woche"
        If tage < 60 Then Return $"ca. {Math.Round(tage / 7):0} Wochen"
        If tage < 730 Then Return $"ca. {Math.Round(tage / 30.4):0} Monate"
        Return "über 2 Jahre"
    End Function

End Module

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
    Private ReadOnly monate As New SaeulenDiagramm With {.LeerText = "Noch kein Verbrauch erfasst – buche Drucke oder wiege Spulen nach."}
    Private ReadOnly materialien As New BalkenDiagramm With {.LeerText = "Noch kein Verbrauch erfasst."}
    Private ReadOnly karteMonate As DiagrammKarte
    Private ReadOnly karteMaterial As DiagrammKarte
    Private ReadOnly prognose As New WebTabelle()
    Private ReadOnly prognoseQuelle As New BindingSource()
    Private kartePrognose As TabellenKarte

    Public Sub New(db As IDatenquelle)
        _db = db
        karteMonate = New DiagrammKarte("Verbrauch pro Monat", "Letzte 12 Monate · Drucke und Nachwiegen", monate, "Verbrauch")
        karteMaterial = New DiagrammKarte("Verbrauch nach Material", "Letzte 12 Monate · Anteil am Gesamtverbrauch", materialien, "Verbrauch")
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

        Dim seite As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4, .Margin = New Padding(0)}
        seite.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        seite.RowStyles.Add(New RowStyle(SizeType.Absolute, 134))
        seite.RowStyles.Add(New RowStyle(SizeType.Percent, 55))
        seite.RowStyles.Add(New RowStyle(SizeType.Percent, 45))

        seite.Controls.Add(New SeitenKopf("Statistik", $"Verbrauch, Kosten und Reichweite – berechnet aus dem Verlauf (Durchschnitt: letzte {TageBasis} Tage)"), 0, 0)
        seite.Controls.Add(FilamentSeite.KennzahlZeile(kzSchnitt, kzReichweite, kzKostenJahr, kzDruckeJahr), 0, 1)

        Dim diagramme As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .Margin = New Padding(0, 0, 0, 16)}
        diagramme.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58))
        diagramme.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42))
        karteMonate.Margin = New Padding(0, 0, 8, 0)
        karteMaterial.Margin = New Padding(8, 0, 0, 0)
        diagramme.Controls.Add(karteMonate, 0, 0)
        diagramme.Controls.Add(karteMaterial, 1, 0)
        seite.Controls.Add(diagramme, 0, 2)

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
        seite.Controls.Add(kartePrognose, 0, 3)

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
