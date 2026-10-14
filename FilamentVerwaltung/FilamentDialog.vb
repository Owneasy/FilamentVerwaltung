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
    Private ReadOnly _db As IDatenquelle
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
    Private ReadOnly cboLagerort As New ComboBox With {.Width = 280, .DropDownStyle = ComboBoxStyle.DropDown,
                                                       .AutoCompleteMode = AutoCompleteMode.SuggestAppend, .AutoCompleteSource = AutoCompleteSource.ListItems}
    Private ReadOnly numDuese As New NumericUpDown With {.Maximum = 500, .Width = 70, .Increment = 5}
    Private ReadOnly numBett As New NumericUpDown With {.Maximum = 200, .Width = 70, .Increment = 5}
    Private ReadOnly txtTrocknung As New TextBox With {.Width = 280, .PlaceholderText = "z. B. 4 h bei 55 °C"}
    Private ReadOnly txtShop As New TextBox With {.Width = 280, .PlaceholderText = "https://…"}
    Private ReadOnly lblVorlage As New Label With {.AutoSize = False, .Size = New Size(200, 20), .ForeColor = Theme.TextLeise,
                                                   .Font = Theme.SchriftKlein, .Margin = New Padding(8, 7, 0, 0)}
    ''' <summary>Zuletzt automatisch eingetragenes Leerspulengewicht (wird bei Herstellerwechsel ersetzt, eigene Eingaben nicht).</summary>
    Private _automatischeSpule As Integer = -1

    Public Sub New(f As Filament, db As IDatenquelle, titel As String,
                   bekannteHersteller As IEnumerable(Of String), bekannteMaterialien As IEnumerable(Of String),
                   Optional bekannteLagerorte As IEnumerable(Of String) = Nothing)
        _f = f
        _db = db
        OberflaecheAufbauen(titel)
        Auswahllisten(cboHersteller, StandardHersteller.Concat(bekannteHersteller))
        Auswahllisten(cboMaterial, StandardMaterialien.Concat(bekannteMaterialien))
        Auswahllisten(cboLagerort, If(bekannteLagerorte, Enumerable.Empty(Of String)()))
        WerteAnzeigen()
        ' Erst nach der Eingabe anwenden – während des Tippens würde das die Autovervollständigung stören
        AddHandler cboHersteller.Leave, Sub(s, e) VorlageAnwenden()
        AddHandler cboHersteller.SelectionChangeCommitted, Sub(s, e) BeginInvoke(Sub() VorlageAnwenden())
        VorlageAnwenden()
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
        Dim spuleZeile As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        spuleZeile.Controls.Add(numSpule)
        spuleZeile.Controls.Add(lblVorlage)

        Dim tempZeile As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        tempZeile.Controls.Add(New Label With {.Text = "Düse", .AutoSize = True, .Margin = New Padding(0, 6, 4, 0)})
        tempZeile.Controls.Add(numDuese)
        tempZeile.Controls.Add(New Label With {.Text = "°C    Bett", .AutoSize = True, .Margin = New Padding(4, 6, 4, 0)})
        tempZeile.Controls.Add(numBett)
        tempZeile.Controls.Add(New Label With {.Text = "°C   (0 = keine Angabe)", .AutoSize = True, .ForeColor = Theme.TextLeise,
                                               .Margin = New Padding(4, 6, 0, 0)})

        Zeile(t, "Leerspule (g):", spuleZeile)
        Zeile(t, "Preis (€):", numPreis)
        Zeile(t, "Kaufdatum:", dtpKauf)
        Zeile(t, "Lagerort:", cboLagerort)
        Zeile(t, "Temperaturen:", tempZeile)
        Zeile(t, "Trocknung:", txtTrocknung)
        Zeile(t, "Shop-Link:", txtShop)
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
        cboLagerort.Text = _f.Lagerort
        numDuese.Value = Begrenzen(_f.DuesenTemp, numDuese)
        numBett.Value = Begrenzen(_f.BettTemp, numBett)
        txtTrocknung.Text = _f.Trocknung
        txtShop.Text = _f.ShopLink
        _letztesGewicht = CInt(numGewicht.Value)
    End Sub

    ''' <summary>
    ''' Trägt das Leerspulengewicht des Herstellers ein, solange das Feld leer ist oder noch den
    ''' automatisch eingetragenen Wert enthält (eigene Eingaben werden nicht überschrieben).
    ''' </summary>
    Private Sub VorlageAnwenden()
        Dim eigener As Boolean
        Dim vorlage As Integer? = Nothing
        Try
            vorlage = _db.LeerspulenVorlage(cboHersteller.Text, eigener)
        Catch ex As Exception
            ' Ohne Vorlage weiterarbeiten
        End Try
        If Not vorlage.HasValue Then
            lblVorlage.Text = ""
            ' Automatisch eingetragenen Wert eines vorher erkannten Herstellers wieder entfernen
            If _automatischeSpule > 0 AndAlso CInt(numSpule.Value) = _automatischeSpule Then numSpule.Value = 0
            _automatischeSpule = -1
            Return
        End If
        lblVorlage.Text = If(eigener, $"zuletzt verwendet: {vorlage.Value} g", $"Richtwert: ca. {vorlage.Value} g")
        Dim aktuell = CInt(numSpule.Value)
        If aktuell = 0 OrElse aktuell = _automatischeSpule Then
            numSpule.Value = Begrenzen(vorlage.Value, numSpule)
            _automatischeSpule = vorlage.Value
        End If
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
        Dim link = txtShop.Text.Trim()
        If link.Length > 0 AndAlso Not (link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse
                                        link.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) Then
            Hinweis("Der Shop-Link muss mit http:// oder https:// beginnen.", txtShop)
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
        _f.Lagerort = cboLagerort.Text.Trim()
        _f.DuesenTemp = CInt(numDuese.Value)
        _f.BettTemp = CInt(numBett.Value)
        _f.Trocknung = txtTrocknung.Text.Trim()
        _f.ShopLink = txtShop.Text.Trim()

        DialogResult = DialogResult.OK
    End Sub

    Private Sub Hinweis(text As String, feld As Control)
        MessageBox.Show(Me, text, "Eingabe prüfen", MessageBoxButtons.OK, MessageBoxIcon.Information)
        feld.Focus()
    End Sub

End Class
