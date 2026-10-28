Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Druck erfassen oder bearbeiten: Spule, verbrauchte Gramm, Druckzeit und optional die zugehörige Datei.
''' Bei G-Code / geslicten 3MF werden Gewicht und Druckzeit aus der Datei übernommen.
''' </summary>
Public Class DruckDialog
    Inherits Form

    Private Const KeineDatei As String = "(keine Datei)"

    Private ReadOnly _b As Buchung
    Private ReadOnly _foto As Byte()
    Private ReadOnly _original As Buchung
    Private ReadOnly _filamente As List(Of Filament)

    Private ReadOnly txtBezeichnung As New TextBox With {.Width = 360}
    Private ReadOnly cboFilament As New ComboBox With {.Width = 360, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly cboDatei As New ComboBox With {.Width = 360, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly numGramm As New NumericUpDown With {.Maximum = 100000, .Width = 100, .ThousandsSeparator = True}
    Private ReadOnly numStunden As New NumericUpDown With {.Maximum = 999, .Width = 60}
    Private ReadOnly numMinuten As New NumericUpDown With {.Maximum = 59, .Width = 60}
    Private ReadOnly dtpDatum As New DateTimePicker With {.Format = DateTimePickerFormat.Custom, .CustomFormat = "dd.MM.yyyy  HH:mm", .Width = 160}
    Private ReadOnly txtNotiz As New TextBox With {.Width = 360, .Height = 50, .Multiline = True, .ScrollBars = ScrollBars.Vertical}
    Private ReadOnly chkFehldruck As New CheckBox With {.Text = "Fehldruck", .AutoSize = True, .Margin = New Padding(3, 6, 10, 3)}
    Private ReadOnly cboGrund As New ComboBox With {.Width = 260, .DropDownStyle = ComboBoxStyle.DropDown}
    Private ReadOnly lblVorschau As New Label With {.AutoSize = True, .Font = Theme.SchriftFett, .ForeColor = Theme.Akzent,
                                                    .Margin = New Padding(3, 10, 3, 3)}
    Private ReadOnly lblDateiInfo As New Label With {.AutoSize = True, .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein,
                                                     .Margin = New Padding(3, 0, 3, 3)}

    ''' <param name="b">Neuer (Id = 0) oder zu bearbeitender Druck. Die Werte werden erst bei "Speichern" übernommen.</param>
    ''' <param name="foto">Kamerabild vom Druckende (JPEG) oder Nothing.</param>
    Public Sub New(b As Buchung, filamente As List(Of Filament), dateien As List(Of Datei), titel As String, Optional foto As Byte() = Nothing)
        _b = b
        _foto = foto
        _original = b.Kopie()
        ' Leere Spulen nur anbieten, wenn der Druck bereits zu ihnen gehört
        _filamente = filamente.Where(Function(f) f.Restgewicht > 0 OrElse f.Id = b.FilamentId).
                               OrderBy(Function(f) f.Code).ToList()
        OberflaecheAufbauen(titel)

        For Each f In _filamente
            cboFilament.Items.Add($"{f.Code}  ·  {f.Hersteller} {f.Material} {f.Farbe}  ({f.Restgewicht:N0} g)")
        Next
        cboDatei.Items.Add(KeineDatei)
        For Each d In dateien.OrderByDescending(Function(x) x.Hinzugefuegt)
            cboDatei.Items.Add(d)
        Next

        txtBezeichnung.Text = b.Bezeichnung
        cboFilament.SelectedIndex = _filamente.FindIndex(Function(f) f.Id = b.FilamentId)
        Dim dateiIndex = If(b.DateiId.HasValue, cboDatei.Items.Cast(Of Object)().ToList().FindIndex(
                                Function(x) TypeOf x Is Datei AndAlso DirectCast(x, Datei).Id = b.DateiId.Value), 0)
        numGramm.Value = Math.Min(numGramm.Maximum, Math.Max(0, b.Gramm))
        Dim zeit = If(b.Druckzeit, 0)
        numStunden.Value = Math.Min(numStunden.Maximum, zeit \ 3600)
        numMinuten.Value = (zeit Mod 3600) \ 60
        dtpDatum.Value = b.Datum
        txtNotiz.Text = b.Notiz
        cboGrund.Items.AddRange(FehldruckGruende.Liste)
        cboGrund.Text = b.FehldruckGrund
        chkFehldruck.Checked = b.Fehldruck
        cboGrund.Enabled = b.Fehldruck
        AddHandler chkFehldruck.CheckedChanged, Sub(s, e) cboGrund.Enabled = chkFehldruck.Checked
        ' Datei zuletzt setzen: übernimmt nur Werte, die noch leer sind
        cboDatei.SelectedIndex = Math.Max(0, dateiIndex)

        AddHandler cboDatei.SelectedIndexChanged, Sub(s, e) DateiGewaehlt()
        AddHandler cboFilament.SelectedIndexChanged, Sub(s, e) VorschauAktualisieren()
        AddHandler numGramm.ValueChanged, Sub(s, e) VorschauAktualisieren()
        AddHandler numGramm.KeyUp, Sub(s, e) VorschauAktualisieren()
        DateiInfoAnzeigen()
        VorschauAktualisieren()
    End Sub

    Private Sub OberflaecheAufbauen(titel As String)
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = titel
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim t As New TableLayoutPanel With {
            .ColumnCount = 2,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(20, 16, 20, 16),
            .Location = New Point(0, 0)
        }
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        Dim zeit As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        zeit.Controls.Add(numStunden)
        zeit.Controls.Add(New Label With {.Text = "h", .AutoSize = True, .Margin = New Padding(2, 6, 10, 0)})
        zeit.Controls.Add(numMinuten)
        zeit.Controls.Add(New Label With {.Text = "min", .AutoSize = True, .Margin = New Padding(2, 6, 0, 0)})

        Dim datei As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .FlowDirection = FlowDirection.TopDown, .Margin = New Padding(0)}
        datei.Controls.Add(cboDatei)
        datei.Controls.Add(lblDateiInfo)

        ' Kamerabild vom Druckende (Server, Bambu Lab)
        If _foto IsNot Nothing Then
            Try
                Using ms As New IO.MemoryStream(_foto), roh = Image.FromStream(ms)
                    Dim bild As New PictureBox With {.Image = New Bitmap(roh), .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.Black,
                                                     .Size = New Size(440, 248), .Margin = New Padding(3, 0, 3, 12), .Anchor = AnchorStyles.Left}
                    t.Controls.Add(bild, 0, t.RowCount)
                    t.SetColumnSpan(bild, 2)
                    t.RowCount += 1
                End Using
            Catch ex As Exception
                ' Beschädigtes Bild – Dialog trotzdem zeigen
            End Try
        End If
        Zeile(t, "Bezeichnung:", txtBezeichnung)
        Zeile(t, "Datei:", datei)
        Zeile(t, "Spule:", cboFilament)
        Zeile(t, "Verbrauch (g):", numGramm)
        Zeile(t, "Druckzeit:", zeit)
        Zeile(t, "Datum:", dtpDatum)
        Zeile(t, "Notiz:", txtNotiz)
        Dim fehl As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        fehl.Controls.Add(chkFehldruck)
        fehl.Controls.Add(cboGrund)
        Zeile(t, "", fehl)
        t.Controls.Add(lblVorschau, 1, t.RowCount)
        t.RowCount += 1

        Dim btnOk As New WebButton("Speichern", "", KnopfStil.Primaer) With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0)}
        Dim btnAbbrechen As New WebButton("Abbrechen") With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0),
                                                            .DialogResult = DialogResult.Cancel}
        AddHandler btnOk.Click, AddressOf Ok_Click
        Dim knoepfe As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True,
                                                .Anchor = AnchorStyles.Right, .Margin = New Padding(0, 12, 0, 0)}
        knoepfe.Controls.Add(btnAbbrechen)
        knoepfe.Controls.Add(btnOk)
        t.Controls.Add(knoepfe, 0, t.RowCount)
        t.SetColumnSpan(knoepfe, 2)
        t.RowCount += 1

        Controls.Add(t)
        AcceptButton = btnOk
        CancelButton = btnAbbrechen
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Private Shared Sub Zeile(t As TableLayoutPanel, beschriftung As String, steuerelement As Control)
        Dim l As New Label With {.Text = beschriftung, .AutoSize = True, .Anchor = AnchorStyles.Left Or AnchorStyles.Top,
                                 .Margin = New Padding(3, 7, 12, 3)}
        steuerelement.Anchor = AnchorStyles.Left
        t.Controls.Add(l, 0, t.RowCount)
        t.Controls.Add(steuerelement, 1, t.RowCount)
        t.RowCount += 1
    End Sub

    Private Function GewaehlteDatei() As Datei
        Return TryCast(cboDatei.SelectedItem, Datei)
    End Function

    Private Function GewaehlteSpule() As Filament
        If cboFilament.SelectedIndex < 0 Then Return Nothing
        Return _filamente(cboFilament.SelectedIndex)
    End Function

    ''' <summary>Übernimmt Bezeichnung, Gewicht und Druckzeit aus der gewählten Datei.</summary>
    Private Sub DateiGewaehlt()
        Dim d = GewaehlteDatei()
        DateiInfoAnzeigen()
        If d Is Nothing Then Return
        If txtBezeichnung.Text.Trim().Length = 0 Then txtBezeichnung.Text = Path.GetFileNameWithoutExtension(d.Name)
        If d.HatDruckdaten Then numGramm.Value = Math.Min(numGramm.Maximum, CDec(Math.Ceiling(d.Gramm.Value)))
        If d.Druckzeit.HasValue Then
            numStunden.Value = Math.Min(numStunden.Maximum, d.Druckzeit.Value \ 3600)
            numMinuten.Value = (d.Druckzeit.Value Mod 3600) \ 60
        End If
        ' Noch keine Spule gewählt: eine mit passendem Material vorschlagen
        If cboFilament.SelectedIndex < 0 AndAlso d.Material.Length > 0 Then
            Dim material = d.Material.Split(","c)(0).Trim()
            Dim index = _filamente.FindIndex(Function(f) f.Material.StartsWith(material, StringComparison.OrdinalIgnoreCase))
            If index >= 0 Then cboFilament.SelectedIndex = index
        End If
    End Sub

    Private Sub DateiInfoAnzeigen()
        Dim d = GewaehlteDatei()
        If d Is Nothing Then
            lblDateiInfo.Text = "Tipp: Bei G-Code werden Gewicht und Druckzeit automatisch übernommen."
        ElseIf d.HatDruckdaten OrElse d.Druckzeit.HasValue Then
            Dim teile = {GrammText(d.Gramm), DauerText(d.Druckzeit), d.Material}.Where(Function(x) x.Length > 0)
            lblDateiInfo.Text = "Laut Slicer: " & String.Join(" · ", teile)
        Else
            lblDateiInfo.Text = "Die Datei enthält keine Druckdaten – Gewicht bitte selbst eintragen."
        End If
    End Sub

    Private Sub VorschauAktualisieren()
        Dim f = GewaehlteSpule()
        If f Is Nothing Then
            lblVorschau.Text = "Bitte eine Spule wählen."
            lblVorschau.ForeColor = Theme.TextLeise
            Return
        End If
        Dim gramm = CInt(EingabeWert(numGramm))
        ' Beim Bearbeiten ist der alte Verbrauch schon abgezogen
        Dim basis = f.Restgewicht + If(_original.Id <> 0 AndAlso _original.FilamentId = f.Id, _original.Gramm, 0)
        Dim danach = basis - gramm
        Dim kosten = If(f.Gewicht > 0, f.Preis * gramm / f.Gewicht, 0D)
        If danach < 0 Then
            lblVorschau.Text = $"Material {kosten:C2} · Achtung: nur {basis:N0} g auf der Spule!"
            lblVorschau.ForeColor = Theme.Rot
        Else
            lblVorschau.Text = $"Material {kosten:C2} · Rest danach {danach:N0} g ({If(f.Gewicht > 0, danach / f.Gewicht, 0):P0})"
            lblVorschau.ForeColor = Theme.Akzent
        End If
    End Sub

    Private Sub Ok_Click(sender As Object, e As EventArgs)
        Dim f = GewaehlteSpule()
        If f Is Nothing Then
            Hinweis("Bitte die Spule wählen, mit der gedruckt wurde.", cboFilament)
            Return
        End If
        If numGramm.Value <= 0 Then
            Hinweis("Bitte den Verbrauch in Gramm eintragen.", numGramm)
            Return
        End If

        Dim sekunden = CInt(numStunden.Value) * 3600 + CInt(numMinuten.Value) * 60
        _b.FilamentId = f.Id
        _b.Art = Buchung.ArtDruck
        _b.Bezeichnung = txtBezeichnung.Text.Trim()
        _b.DateiId = GewaehlteDatei()?.Id
        _b.Gramm = CInt(numGramm.Value)
        _b.Druckzeit = If(sekunden > 0, sekunden, CType(Nothing, Integer?))
        _b.Datum = dtpDatum.Value
        _b.Notiz = txtNotiz.Text
        _b.Fehldruck = chkFehldruck.Checked
        _b.FehldruckGrund = If(chkFehldruck.Checked, cboGrund.Text.Trim(), "")
        DialogResult = DialogResult.OK
    End Sub

    Private Sub Hinweis(text As String, feld As Control)
        MessageBox.Show(Me, text, "Eingabe prüfen", MessageBoxButtons.OK, MessageBoxIcon.Information)
        feld.Focus()
    End Sub

End Class
