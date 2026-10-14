Imports System.Diagnostics
Imports System.Drawing
Imports System.Text
Imports System.Windows.Forms

''' <summary>Eine Filamentsorte (gleicher Hersteller, Material, Farbe, Durchmesser) mit Bestand und Reichweite.</summary>
Public Class SorteInfo

    Public Const StatusNachkaufen As String = "Nachkaufen"
    Public Const StatusBald As String = "Bald"
    Public Const StatusOk As String = "Genug"

    Public Property Schluessel As String = ""
    Public Property Bezeichnung As String = ""
    Public Property Durchmesser As Decimal
    ''' <summary>Spulen mit Restbestand.</summary>
    Public Property Spulen As Integer
    Public Property Rest As Integer
    Public Property ProMonat As Double
    Public Property ReichweiteTage As Double = Double.PositiveInfinity
    Public Property LetzterPreis As Decimal
    Public Property PreisProKg As Decimal
    Public Property ShopLink As String = ""
    Public Property Status As String = StatusOk
    Public Property Ausgeblendet As Boolean
    ''' <summary>Zuletzt gekaufte Spule dieser Sorte (Vorlage für "Gekauft").</summary>
    Public Property Vorlage As Filament

    ''' <summary>Sortierschlüssel: Nachkaufen zuerst, dann Bald, dann Genug.</summary>
    Public ReadOnly Property Dringlichkeit As Integer
        Get
            Return If(Status = StatusNachkaufen, 0, If(Status = StatusBald, 1, 2))
        End Get
    End Property

    Public ReadOnly Property ReichweiteText As String
        Get
            If Rest <= 0 Then Return "leer"
            If ProMonat <= 0 Then Return "kein Verbrauch"
            Return Statistik.ReichweiteText(ReichweiteTage)
        End Get
    End Property

    Public ReadOnly Property ShopText As String
        Get
            Return If(ShopLink.Length > 0, "Shop öffnen", "")
        End Get
    End Property

End Class

''' <summary>
''' Seite "Nachkauf": welche Sorten bald leer sind, mit letztem Preis, Shop-Link und Einkaufsliste.
''' </summary>
Public Class NachkaufSeite
    Inherits UserControl
    Implements ISeite

    Private Const EinstellungMindestbestand As String = "Nachkauf.Mindestbestand"
    Private Const EinstellungAusgeblendet As String = "Nachkauf.Ausgeblendet"
    ''' <summary>Unter dieser Reichweite (Tage) wird nachgekauft bzw. gewarnt.</summary>
    Private Const TageNachkaufen As Integer = 30
    Private Const TageBald As Integer = 60

    Private ReadOnly _db As Datenbank
    Private _sorten As New List(Of SorteInfo)
    Private _filamente As New List(Of Filament)
    Private _ausgeblendet As New HashSet(Of String)
    Private _laedt As Boolean

    Private ReadOnly grid As New WebTabelle()
    Private ReadOnly quelle As New BindingSource()
    Private ReadOnly numMindest As New NumericUpDown With {.Maximum = 10000, .Increment = 50, .Width = 80, .Margin = New Padding(6, 5, 4, 4)}
    Private ReadOnly chkNurNachkaufen As New CheckBox With {.Text = "Nur nachzukaufende", .AutoSize = True, .Margin = New Padding(16, 8, 8, 4)}
    Private ReadOnly chkAusgeblendete As New CheckBox With {.Text = "Ausgeblendete zeigen", .AutoSize = True, .Margin = New Padding(8, 8, 16, 4)}
    Private ReadOnly btnShop As New WebButton("Shop", Theme.SymOeffnen)
    Private ReadOnly btnGekauft As New WebButton("Gekauft", Theme.SymNeu)
    Private ReadOnly btnAusblenden As New WebButton("Ausblenden", Theme.SymLoeschen)
    Private ReadOnly tipps As New ToolTip()
    Private karte As TabellenKarte

    Private ReadOnly kzNachkaufen As New KennzahlKarte("Nachkaufen", Theme.SymWarnung, Theme.Rot)
    Private ReadOnly kzBald As New KennzahlKarte("Bald nachkaufen", Theme.SymUhr, Theme.Gelb)
    Private ReadOnly kzKosten As New KennzahlKarte("Geschätzte Kosten", Theme.SymGeld, Theme.Lila)
    Private ReadOnly kzSorten As New KennzahlKarte("Sorten", Theme.SymSpule, Theme.Akzent)

    ''' <summary>Eine neue Spule wurde angelegt (andere Seiten neu laden).</summary>
    Public Event SpuleAngelegt()

    Public Sub New(db As Datenbank)
        _db = db
        OberflaecheAufbauen()
    End Sub

    Public Sub Aktivieren() Implements ISeite.Aktivieren
        grid.Focus()
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

        Dim seite As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .Margin = New Padding(0)}
        seite.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        seite.RowStyles.Add(New RowStyle(SizeType.Absolute, 134))
        seite.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim kopf As New SeitenKopf("Nachkauf", $"Sorten, die knapp werden – Reichweite aus dem Verbrauch der letzten {TageBasis} Tage")
        Dim btnKopieren As New WebButton("Einkaufsliste kopieren", Theme.SymKopieren, KnopfStil.Primaer) With {.Margin = New Padding(0)}
        AddHandler btnKopieren.Click, Sub(s, e) EinkaufslisteKopieren()
        tipps.SetToolTip(btnKopieren, "Alle nachzukaufenden Sorten als Text in die Zwischenablage kopieren (z. B. für eine Notiz-App)")
        kopf.Knoepfe.Controls.Add(btnKopieren)
        seite.Controls.Add(kopf, 0, 0)
        seite.Controls.Add(FilamentSeite.KennzahlZeile(kzNachkaufen, kzBald, kzKosten, kzSorten), 0, 1)

        grid.DataSource = quelle
        grid.SortSpalte = "Dringlichkeit"
        grid.Spalte("Status", "Status", 0).MinimumWidth = Px(Me, 120)
        grid.Spalte("Bezeichnung", "Sorte", 200, minBreite:=160)
        grid.Spalte("Durchmesser", "Ø", 0, "0.00", True)
        grid.Spalte("Spulen", "Spulen", 0, rechtsbuendig:=True)
        grid.Spalte("Rest", "Bestand", 0, "#,##0 g", True)
        grid.Spalte("ProMonat", "Ø pro Monat", 0, "#,##0 g", True)
        grid.Spalte("ReichweiteTage", "Reicht für", 0, rechtsbuendig:=True)
        grid.Spalte("LetzterPreis", "Letzter Preis", 0, "C2", True)
        grid.Spalte("PreisProKg", "Preis/kg", 0, "C2", True)
        grid.Spalte("ShopText", "Shop", 0)
        grid.Columns("Bezeichnung").DefaultCellStyle.Font = Theme.SchriftFett
        AddHandler grid.SortierungGeaendert, Sub(s, e) AnsichtAktualisieren()
        AddHandler grid.SelectionChanged, Sub(s, e) KnoepfeAktualisieren()
        AddHandler grid.CellFormatting, AddressOf Grid_CellFormatting
        AddHandler grid.CellPainting, AddressOf Grid_CellPainting
        AddHandler grid.CellClick, Sub(s, e)
                                       If e.RowIndex >= 0 AndAlso grid.Columns(e.ColumnIndex).Name = "ShopText" Then ShopOeffnen()
                                   End Sub
        AddHandler grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 AndAlso grid.Columns(e.ColumnIndex).Name <> "ShopText" Then Gekauft()
                                          End Sub

        Dim menue As New ContextMenuStrip With {.Font = Theme.Schrift, .ShowImageMargin = False}
        menue.Items.Add("Gekauft – neue Spule anlegen", Nothing, Sub(s, e) Gekauft())
        menue.Items.Add("Shop öffnen", Nothing, Sub(s, e) ShopOeffnen())
        menue.Items.Add(New ToolStripSeparator())
        menue.Items.Add("Ausblenden / Einblenden", Nothing, Sub(s, e) AusblendenUmschalten())
        AddHandler menue.Opening, Sub(s, e) e.Cancel = Ausgewaehlt() Is Nothing
        grid.ContextMenuStrip = menue

        karte = New TabellenKarte(grid)
        karte.FussRechts.Text = "Doppelklick = gekauft, neue Spule anlegen"
        karte.Werkzeug.Controls.Add(New Label With {.Text = "Mindestbestand:", .AutoSize = True, .Margin = New Padding(0, 8, 0, 4)})
        karte.Werkzeug.Controls.Add(numMindest)
        karte.Werkzeug.Controls.Add(New Label With {.Text = "g je Sorte", .AutoSize = True, .ForeColor = Theme.TextLeise, .Margin = New Padding(0, 8, 0, 4)})
        karte.Werkzeug.Controls.Add(chkNurNachkaufen)
        karte.Werkzeug.Controls.Add(chkAusgeblendete)
        For Each b In {btnGekauft, btnShop, btnAusblenden}
            b.Margin = New Padding(0, 0, 8, 4)
            karte.Werkzeug.Controls.Add(b)
        Next
        tipps.SetToolTip(numMindest, $"Unter diesem Bestand wird eine Sorte zum Nachkaufen vorgeschlagen (außerdem, wenn sie für weniger als {TageNachkaufen} Tage reicht)")
        tipps.SetToolTip(btnGekauft, "Neue Spule dieser Sorte anlegen – Daten der letzten Spule werden übernommen")
        tipps.SetToolTip(btnShop, "Shop-Link der Sorte öffnen (im Filament unter ""Shop-Link"" eintragen)")
        tipps.SetToolTip(btnAusblenden, "Sorte nicht mehr vorschlagen (z. B. wenn du sie nicht mehr kaufen willst)")
        AddHandler btnGekauft.Click, Sub(s, e) Gekauft()
        AddHandler btnShop.Click, Sub(s, e) ShopOeffnen()
        AddHandler btnAusblenden.Click, Sub(s, e) AusblendenUmschalten()
        AddHandler numMindest.ValueChanged, Sub(s, e) MindestbestandGeaendert()
        AddHandler chkNurNachkaufen.CheckedChanged, Sub(s, e) AnsichtAktualisieren()
        AddHandler chkAusgeblendete.CheckedChanged, Sub(s, e) AnsichtAktualisieren()
        karte.KompaktFaehig(btnGekauft, btnShop, btnAusblenden)
        seite.Controls.Add(karte, 0, 2)

        Controls.Add(seite)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Private Sub DatenLaden()
        _laedt = True
        Dim buchungen As List(Of Buchung)
        Try
            _filamente = _db.AlleLaden()
            buchungen = _db.BuchungenLaden()
            Dim mindest As Integer
            numMindest.Value = If(Integer.TryParse(_db.EinstellungLesen(EinstellungMindestbestand), mindest),
                                  Math.Min(numMindest.Maximum, mindest), 250)
            _ausgeblendet = New HashSet(Of String)(If(_db.EinstellungLesen(EinstellungAusgeblendet), "").
                                                   Split({vbLf}, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase)
        Catch ex As Exception
            _laedt = False
            MessageBox.Show(Me, "Die Daten konnten nicht geladen werden." & Environment.NewLine & Environment.NewLine & ex.Message,
                            "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        _laedt = False

        _sorten = SortenBerechnen(_filamente, buchungen, CInt(numMindest.Value), _ausgeblendet)
        AnsichtAktualisieren()
    End Sub

    ''' <summary>Mindestbestand und ausgeblendete Sorten aus den Einstellungen (auch für die Übersicht).</summary>
    Public Shared Function EinstellungenLesen(db As Datenbank) As (Mindestbestand As Integer, Ausgeblendet As HashSet(Of String))
        Dim mindest As Integer
        If Not Integer.TryParse(db.EinstellungLesen(EinstellungMindestbestand), mindest) Then mindest = 250
        Dim aus = New HashSet(Of String)(If(db.EinstellungLesen(EinstellungAusgeblendet), "").
                                         Split({vbLf}, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase)
        Return (mindest, aus)
    End Function

    ''' <summary>Bestand, Verbrauch (letzte 90 Tage) und Nachkauf-Status je Sorte.</summary>
    Public Shared Function SortenBerechnen(filamente As List(Of Filament), buchungen As List(Of Buchung), mindestbestand As Integer,
                                           ausgeblendet As HashSet(Of String)) As List(Of SorteInfo)
        Dim start = Date.Today.AddDays(-TageBasis + 1)
        Dim verbrauchJeSpule = buchungen.Where(Function(b) IstVerbrauch(b) AndAlso b.Datum.Date >= start).
                                         GroupBy(Function(b) b.FilamentId).ToDictionary(Function(gr) gr.Key, Function(gr) gr.Sum(Function(b) CDbl(b.Gramm)))

        Return filamente.GroupBy(Function(f) f.Sorte).Select(Function(gruppe)
            Dim neueste = gruppe.OrderByDescending(Function(f) f.Kaufdatum).ThenByDescending(Function(f) f.Id).First()
            Dim gramm = gruppe.Sum(Function(f) If(verbrauchJeSpule.ContainsKey(f.Id), verbrauchJeSpule(f.Id), 0.0))
            Dim s As New SorteInfo With {
                .Schluessel = gruppe.Key,
                .Bezeichnung = $"{neueste.Hersteller} {neueste.Material} {neueste.Farbe}".Trim(),
                .Durchmesser = neueste.Durchmesser,
                .Spulen = gruppe.Where(Function(f) f.Restgewicht > 0).Count(),
                .Rest = gruppe.Sum(Function(f) f.Restgewicht),
                .ProMonat = Math.Round(gramm / TageBasis * 30.4),
                .LetzterPreis = neueste.Preis,
                .PreisProKg = neueste.PreisProKg,
                .ShopLink = If(gruppe.OrderByDescending(Function(f) f.Kaufdatum).Select(Function(f) f.ShopLink).FirstOrDefault(Function(l) l.Length > 0), ""),
                .Vorlage = neueste,
                .Ausgeblendet = ausgeblendet.Contains(gruppe.Key)
            }
            If gramm > 0 Then s.ReichweiteTage = s.Rest / (gramm / TageBasis)
            s.Status = If(s.Rest < mindestbestand OrElse s.ReichweiteTage < TageNachkaufen, SorteInfo.StatusNachkaufen,
                          If(s.ReichweiteTage < TageBald, SorteInfo.StatusBald, SorteInfo.StatusOk))
            Return s
        End Function).ToList()
    End Function

    Private Sub AnsichtAktualisieren()
        If _laedt Then Return
        Dim merke = Ausgewaehlt()?.Schluessel
        Dim treffer As IEnumerable(Of SorteInfo) = _sorten
        If Not chkAusgeblendete.Checked Then treffer = treffer.Where(Function(s) Not s.Ausgeblendet)
        If chkNurNachkaufen.Checked Then treffer = treffer.Where(Function(s) s.Status = SorteInfo.StatusNachkaufen)
        Dim liste = grid.Sortieren(treffer)
        ' Innerhalb gleicher Dringlichkeit nach Reichweite
        If grid.SortSpalte = "Dringlichkeit" Then
            liste = If(grid.SortAufsteigend,
                       liste.OrderBy(Function(s) s.Dringlichkeit).ThenBy(Function(s) s.ReichweiteTage).ThenBy(Function(s) s.Rest),
                       liste.OrderByDescending(Function(s) s.Dringlichkeit).ThenByDescending(Function(s) s.ReichweiteTage)).ToList()
        End If
        quelle.DataSource = liste
        If merke IsNot Nothing Then grid.Auswaehlen(Of SorteInfo)(Function(s) s.Schluessel = merke)
        KnoepfeAktualisieren()

        Dim sichtbar = _sorten.Where(Function(s) Not s.Ausgeblendet).ToList()
        Dim nachkaufen = sichtbar.Where(Function(s) s.Status = SorteInfo.StatusNachkaufen).ToList()
        Dim bald = sichtbar.Where(Function(s) s.Status = SorteInfo.StatusBald).ToList()
        kzNachkaufen.Setzen(nachkaufen.Count.ToString("N0"),
                            If(nachkaufen.Count = 0, "alles ausreichend vorrätig", String.Join(", ", nachkaufen.Take(2).Select(Function(s) s.Bezeichnung)) & If(nachkaufen.Count > 2, " …", "")))
        kzBald.Setzen(bald.Count.ToString("N0"), $"reicht für weniger als {TageBald} Tage")
        kzKosten.Setzen(nachkaufen.Sum(Function(s) s.LetzterPreis).ToString("C2"), "je eine Spule zum letzten Preis")
        kzSorten.Setzen(sichtbar.Count.ToString("N0"), $"{_sorten.Where(Function(s) s.Ausgeblendet).Count()} ausgeblendet")
        karte.FussLinks.Text = $"{liste.Count:N0} Sorten angezeigt · Mindestbestand {numMindest.Value:N0} g"
    End Sub

    Private Function Ausgewaehlt() As SorteInfo
        Return grid.Ausgewaehlt(Of SorteInfo)()
    End Function

    Private Sub KnoepfeAktualisieren()
        Dim s = Ausgewaehlt()
        btnGekauft.Enabled = s IsNot Nothing
        btnShop.Enabled = s IsNot Nothing AndAlso s.ShopLink.Length > 0
        btnAusblenden.Enabled = s IsNot Nothing
        Dim text = If(s IsNot Nothing AndAlso s.Ausgeblendet, "Einblenden", "Ausblenden")
        If btnAusblenden.Text.Length > 0 AndAlso btnAusblenden.Text <> text Then btnAusblenden.Text = text
    End Sub

    Private Sub Grid_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs)
        If e.RowIndex < 0 Then Return
        Dim s = grid.Eintrag(Of SorteInfo)(e.RowIndex)
        If s Is Nothing Then Return
        Select Case grid.Columns(e.ColumnIndex).Name
            Case "ReichweiteTage"
                e.Value = s.ReichweiteText
                e.FormattingApplied = True
            Case "ProMonat"
                If s.ProMonat <= 0 Then
                    e.Value = "–"
                    e.FormattingApplied = True
                End If
            Case "ShopText"
                e.CellStyle.ForeColor = Theme.Akzent
                e.CellStyle.Font = Theme.SchriftFett
        End Select
        If s.Ausgeblendet AndAlso grid.Columns(e.ColumnIndex).Name <> "Status" Then e.CellStyle.ForeColor = Theme.Grau
    End Sub

    Private Sub Grid_CellPainting(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse e.Graphics Is Nothing OrElse e.CellStyle Is Nothing Then Return
        If grid.Columns(e.ColumnIndex).Name <> "Status" Then Return
        Dim s = grid.Eintrag(Of SorteInfo)(e.RowIndex)
        If s Is Nothing Then Return
        ' Statusfarben nur zusammen mit dem Text
        Dim farbe = If(s.Ausgeblendet, Theme.Grau,
                       If(s.Status = SorteInfo.StatusNachkaufen, Theme.Rot, If(s.Status = SorteInfo.StatusBald, Theme.Gelb, Theme.Gruen)))
        grid.BadgeZeichnen(e, If(s.Ausgeblendet, "Ausgeblendet", s.Status), farbe, Hell(farbe, 0.12))
    End Sub

    Private Sub MindestbestandGeaendert()
        If _laedt Then Return
        Try
            _db.EinstellungSpeichern(EinstellungMindestbestand, CInt(numMindest.Value).ToString())
        Catch ex As Exception
            ' Nicht kritisch
        End Try
        DatenLaden()
    End Sub

    Private Sub ShopOeffnen()
        Dim s = Ausgewaehlt()
        If s Is Nothing OrElse s.ShopLink.Length = 0 Then
            MessageBox.Show(Me, "Für diese Sorte ist kein Shop-Link eingetragen. Du kannst ihn im Filament unter ""Shop-Link"" hinterlegen.",
                            "Nachkauf", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Try
            Process.Start(New ProcessStartInfo(s.ShopLink) With {.UseShellExecute = True})
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Shop öffnen", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Neue Spule dieser Sorte anlegen (Daten der letzten Spule, voll, heutiges Kaufdatum).</summary>
    Private Sub Gekauft()
        Dim s = Ausgewaehlt()
        If s Is Nothing OrElse s.Vorlage Is Nothing Then Return
        Dim f = s.Vorlage.Kopie()
        f.Id = 0
        f.Restgewicht = f.Gewicht
        f.Kaufdatum = Date.Today
        f.Notiz = ""
        Try
            f.Code = _db.NaechsterCode()
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        Using dlg As New FilamentDialog(f, _db, $"Neue Spule: {s.Bezeichnung}",
                                        _filamente.Select(Function(x) x.Hersteller), _filamente.Select(Function(x) x.Material),
                                        _filamente.Select(Function(x) x.Lagerort))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
        End Using
        Try
            _db.Speichern(f)
        Catch ex As Exception
            MessageBox.Show(Me, "Die Spule konnte nicht gespeichert werden." & Environment.NewLine & Environment.NewLine & ex.Message,
                            "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        RaiseEvent SpuleAngelegt()
        DatenLaden()
    End Sub

    Private Sub AusblendenUmschalten()
        Dim s = Ausgewaehlt()
        If s Is Nothing Then Return
        If s.Ausgeblendet Then _ausgeblendet.Remove(s.Schluessel) Else _ausgeblendet.Add(s.Schluessel)
        Try
            _db.EinstellungSpeichern(EinstellungAusgeblendet, String.Join(vbLf, _ausgeblendet))
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        DatenLaden()
    End Sub

    Private Sub EinkaufslisteKopieren()
        Dim liste = _sorten.Where(Function(s) Not s.Ausgeblendet AndAlso s.Status = SorteInfo.StatusNachkaufen).
                            OrderBy(Function(s) s.ReichweiteTage).ToList()
        If liste.Count = 0 Then
            MessageBox.Show(Me, "Zurzeit muss nichts nachgekauft werden.", "Einkaufsliste", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim sb As New StringBuilder()
        sb.AppendLine($"Filament-Einkaufsliste ({Date.Today:dd.MM.yyyy})")
        sb.AppendLine()
        For Each s In liste
            sb.Append($"- {s.Bezeichnung}, {s.Durchmesser:0.00} mm – Bestand {s.Rest:N0} g, ca. {s.LetzterPreis:C2}")
            If s.ShopLink.Length > 0 Then sb.Append($" – {s.ShopLink}")
            sb.AppendLine()
        Next
        sb.AppendLine()
        sb.AppendLine($"Summe ca. {liste.Sum(Function(s) s.LetzterPreis):C2}")
        Try
            Clipboard.SetText(sb.ToString())
            MessageBox.Show(Me, $"{liste.Count} Sorten wurden als Einkaufsliste in die Zwischenablage kopiert.", "Einkaufsliste",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Einkaufsliste", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

End Class
