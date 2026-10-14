Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Dialog zum Anlegen und Bearbeiten eines Filaments.
''' Die Werte werden erst bei "Speichern" in das übergebene Objekt geschrieben.
''' </summary>
Public Class FilamentDialog
    Inherits Form

    Private Shared ReadOnly StandardHersteller As String() = {
        "3DJake", "Anycubic", "Bambu Lab", "Creality", "Das Filament", "Elegoo", "eSun", "Extrudr",
        "Fiberlogy", "Geeetech", "Jayo", "Overture", "Polymaker", "Prusament", "Sunlu"}

    Private Shared ReadOnly StandardMaterialien As String() = {
        "PLA", "PLA+", "PLA Silk", "PLA Matt", "PETG", "ABS", "ASA", "TPU", "PA (Nylon)", "PC", "HIPS", "PVA", "CF-PLA", "CF-PETG"}

    Private ReadOnly _f As Filament
    Private ReadOnly _db As Datenbank
    Private _letztesGewicht As Integer

    Private ReadOnly txtCode As New TextBox With {.Width = 170}
    Private ReadOnly btnCode As New WebButton("Neu vergeben") With {.Margin = New Padding(8, 0, 0, 0)}
    Private ReadOnly cboHersteller As New ComboBox With {.Width = 280}
    Private ReadOnly cboMaterial As New ComboBox With {.Width = 280}
    Private ReadOnly txtFarbe As New TextBox With {.Width = 280}
    Private ReadOnly cboDurchmesser As New ComboBox With {.Width = 100, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly numGewicht As New NumericUpDown With {.Maximum = 100000, .Width = 100, .ThousandsSeparator = True, .Increment = 250}
    Private ReadOnly numRest As New NumericUpDown With {.Maximum = 100000, .Width = 100, .ThousandsSeparator = True, .Increment = 10}
    Private ReadOnly numSpule As New NumericUpDown With {.Maximum = 10000, .Width = 100, .Increment = 10}
    Private ReadOnly numPreis As New NumericUpDown With {.Maximum = 100000, .DecimalPlaces = 2, .Width = 100, .Increment = 0.5D, .ThousandsSeparator = True}
    Private ReadOnly dtpKauf As New DateTimePicker With {.Format = DateTimePickerFormat.Short, .Width = 120}
    Private ReadOnly txtNotiz As New TextBox With {.Width = 280, .Height = 60, .Multiline = True, .AcceptsReturn = True, .ScrollBars = ScrollBars.Vertical}

    Public Sub New(f As Filament, db As Datenbank, titel As String,
                   bekannteHersteller As IEnumerable(Of String), bekannteMaterialien As IEnumerable(Of String))
        _f = f
        _db = db
        OberflaecheAufbauen(titel)
        Auswahllisten(cboHersteller, StandardHersteller.Concat(bekannteHersteller))
        Auswahllisten(cboMaterial, StandardMaterialien.Concat(bekannteMaterialien))
        WerteAnzeigen()
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

        Dim codeZeile As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        codeZeile.Controls.Add(txtCode)
        codeZeile.Controls.Add(btnCode)
        AddHandler btnCode.Click, AddressOf CodeNeuVergeben

        cboDurchmesser.Items.AddRange(New Object() {"1,75 mm", "2,85 mm"})
        For Each cb In {cboHersteller, cboMaterial}
            cb.DropDownStyle = ComboBoxStyle.DropDown
            cb.AutoCompleteMode = AutoCompleteMode.SuggestAppend
            cb.AutoCompleteSource = AutoCompleteSource.ListItems
        Next

        Zeile(t, "ID / Nummer:", codeZeile)
        Zeile(t, "Hersteller:", cboHersteller)
        Zeile(t, "Material:", cboMaterial)
        Zeile(t, "Farbe:", txtFarbe)
        Zeile(t, "Durchmesser:", cboDurchmesser)
        Zeile(t, "Gewicht (netto, g):", numGewicht)
        Zeile(t, "Restgewicht (g):", numRest)
        Zeile(t, "Leerspule (g):", numSpule)
        Zeile(t, "Preis (€):", numPreis)
        Zeile(t, "Kaufdatum:", dtpKauf)
        Zeile(t, "Notiz:", txtNotiz)

        AddHandler numGewicht.ValueChanged, AddressOf Gewicht_ValueChanged

        Dim btnOk As New WebButton("Speichern", "", KnopfStil.Primaer) With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0)}
        Dim btnAbbrechen As New WebButton("Abbrechen") With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0),
                                             .DialogResult = DialogResult.Cancel}
        AddHandler btnOk.Click, AddressOf Ok_Click

        Dim knoepfe As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.RightToLeft,
            .AutoSize = True,
            .Anchor = AnchorStyles.Right,
            .Margin = New Padding(0, 12, 0, 0)
        }
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
        Dim l As New Label With {
            .Text = beschriftung,
            .AutoSize = True,
            .Anchor = AnchorStyles.Left,
            .Margin = New Padding(3, 6, 12, 3)
        }
        steuerelement.Anchor = AnchorStyles.Left
        t.Controls.Add(l, 0, t.RowCount)
        t.Controls.Add(steuerelement, 1, t.RowCount)
        t.RowCount += 1
    End Sub

    Private Shared Sub Auswahllisten(cb As ComboBox, eintraege As IEnumerable(Of String))
        Dim gefiltert = eintraege.Where(Function(x) Not String.IsNullOrWhiteSpace(x)).Select(Function(x) x.Trim())
        Dim eindeutig = gefiltert.Distinct(StringComparer.OrdinalIgnoreCase)
        For Each eintrag In eindeutig.OrderBy(Function(x) x, StringComparer.CurrentCultureIgnoreCase)
            cb.Items.Add(eintrag)
        Next
    End Sub

    Private Sub WerteAnzeigen()
        txtCode.Text = _f.Code
        cboHersteller.Text = _f.Hersteller
        cboMaterial.Text = _f.Material
        txtFarbe.Text = _f.Farbe
        cboDurchmesser.SelectedIndex = If(_f.Durchmesser >= 2.5D, 1, 0)
        numGewicht.Value = Begrenzen(_f.Gewicht, numGewicht)
        numRest.Value = Begrenzen(_f.Restgewicht, numRest)
        numSpule.Value = Begrenzen(_f.Spulengewicht, numSpule)
        numPreis.Value = Begrenzen(_f.Preis, numPreis)
        dtpKauf.Value = If(_f.Kaufdatum < dtpKauf.MinDate OrElse _f.Kaufdatum > dtpKauf.MaxDate, Date.Today, _f.Kaufdatum)
        txtNotiz.Text = _f.Notiz
        _letztesGewicht = CInt(numGewicht.Value)
    End Sub

    Private Shared Function Begrenzen(wert As Decimal, n As NumericUpDown) As Decimal
        Return Math.Min(Math.Max(wert, n.Minimum), n.Maximum)
    End Function

    ''' <summary>War die Spule voll, wandert das Restgewicht beim Ändern des Gewichts mit.</summary>
    Private Sub Gewicht_ValueChanged(sender As Object, e As EventArgs)
        If CInt(numRest.Value) = _letztesGewicht Then numRest.Value = numGewicht.Value
        _letztesGewicht = CInt(numGewicht.Value)
    End Sub

    Private Sub CodeNeuVergeben(sender As Object, e As EventArgs)
        Try
            txtCode.Text = _db.NaechsterCode()
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub Ok_Click(sender As Object, e As EventArgs)
        Dim code = txtCode.Text.Trim()
        If code.Length = 0 Then
            Hinweis("Bitte eine ID / Nummer eingeben.", txtCode)
            Return
        End If
        If cboHersteller.Text.Trim().Length = 0 Then
            Hinweis("Bitte einen Hersteller angeben.", cboHersteller)
            Return
        End If
        If numRest.Value > numGewicht.Value Then
            Hinweis("Das Restgewicht darf nicht größer als das Gewicht sein.", numRest)
            Return
        End If

        Try
            If _db.CodeExistiert(code, _f.Id) Then
                Hinweis($"Die ID ""{code}"" ist bereits vergeben. Bitte eine andere wählen.", txtCode)
                Return
            End If
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        _f.Code = code
        _f.Hersteller = cboHersteller.Text.Trim()
        _f.Material = cboMaterial.Text.Trim()
        _f.Farbe = txtFarbe.Text.Trim()
        _f.Durchmesser = If(cboDurchmesser.SelectedIndex = 1, 2.85D, 1.75D)
        _f.Gewicht = CInt(numGewicht.Value)
        _f.Restgewicht = CInt(numRest.Value)
        _f.Spulengewicht = CInt(numSpule.Value)
        _f.Preis = numPreis.Value
        _f.Kaufdatum = dtpKauf.Value.Date
        _f.Notiz = txtNotiz.Text

        DialogResult = DialogResult.OK
    End Sub

    Private Sub Hinweis(text As String, feld As Control)
        MessageBox.Show(Me, text, "Eingabe prüfen", MessageBoxButtons.OK, MessageBoxIcon.Information)
        feld.Focus()
    End Sub

End Class
