Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Spule schnell finden: ID scannen (QR-Code) oder eintippen. Enter öffnet die Spule zum Wiegen,
''' "Anzeigen" springt nur zu ihr. Ein USB-Scanner tippt die ID samt Enter – das genügt.
''' </summary>
Public Class SchnellsucheDialog
    Inherits Form

    Private ReadOnly _alle As List(Of Filament)
    Private ReadOnly suche As New SuchFeld()
    Private ReadOnly grid As New WebTabelle()
    Private ReadOnly quelle As New BindingSource()
    Private ReadOnly lblHinweis As New Label With {.AutoSize = True, .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein,
                                                   .Margin = New Padding(2, 8, 0, 0)}

    ''' <summary>Gewählte Spule (Nothing = abgebrochen).</summary>
    Public ReadOnly Property Ergebnis As Filament
    ''' <summary>True = Verbrauch-/Wiege-Dialog öffnen, False = nur anzeigen.</summary>
    Public ReadOnly Property ZumWiegen As Boolean
    ''' <summary>Gescannter Verpackungs-Barcode: neue Spule dieser Sorte anlegen (Nothing = nein).</summary>
    Public ReadOnly Property NeuMitBarcode As String

    Public Sub New(spulen As List(Of Filament))
        _alle = spulen.OrderBy(Function(f) f.Code).ToList()
        OberflaecheAufbauen()
        Filtern()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Spule suchen"
        Size = New Size(620, 460)
        MinimizeBox = False
        MaximizeBox = False
        FormBorderStyle = FormBorderStyle.FixedDialog
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        BackColor = Theme.Seite
        Padding = New Padding(20, 16, 20, 16)
        KeyPreview = True

        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 5}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        t.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        t.Controls.Add(New Label With {.Text = "QR-Code oder Verpackungs-Barcode scannen, ID / Name eingeben", .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark,
                                       .AutoSize = True, .Margin = New Padding(0, 0, 0, 8)}, 0, 0)
        suche.Dock = DockStyle.Fill
        suche.Margin = New Padding(0, 0, 0, 10)
        suche.Eingabe.PlaceholderText = "z. B. FIL-0007 oder PETG Orange"
        AddHandler suche.Eingabe.TextChanged, Sub(s, e) Filtern()
        t.Controls.Add(suche, 0, 1)

        grid.DataSource = quelle
        grid.Spalte("Code", "ID", 0)
        grid.Spalte("Hersteller", "Hersteller", 100)
        grid.Spalte("Material", "Material", 80)
        grid.Spalte("Farbe", "Farbe", 90)
        grid.Spalte("Restgewicht", "Rest", 0, "#,##0 g", True)
        grid.Columns("Code").DefaultCellStyle.Font = Theme.SchriftFett
        AddHandler grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 Then Uebernehmen(zumWiegen:=True)
                                          End Sub
        Dim karte As New Karte With {.Dock = DockStyle.Fill, .Padding = New Padding(1, 1, 1, 6)}
        karte.Controls.Add(grid)
        t.Controls.Add(karte, 0, 2)
        t.Controls.Add(lblHinweis, 0, 3)

        Dim knoepfe As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.RightToLeft, .Dock = DockStyle.Fill,
                                                 .Margin = New Padding(0, 12, 0, 0)}
        Dim btnWiegen As New WebButton("Wiegen / Verbrauch", Theme.SymWaage, KnopfStil.Primaer) With {.Margin = New Padding(8, 0, 0, 0)}
        Dim btnAnzeigen As New WebButton("Anzeigen") With {.Margin = New Padding(8, 0, 0, 0)}
        Dim btnAbbrechen As New WebButton("Abbrechen") With {.Margin = New Padding(0), .DialogResult = DialogResult.Cancel}
        AddHandler btnWiegen.Click, Sub(s, e) Uebernehmen(zumWiegen:=True)
        AddHandler btnAnzeigen.Click, Sub(s, e) Uebernehmen(zumWiegen:=False)
        knoepfe.Controls.Add(btnWiegen)
        knoepfe.Controls.Add(btnAnzeigen)
        knoepfe.Controls.Add(btnAbbrechen)
        t.Controls.Add(knoepfe, 0, 4)

        Controls.Add(t)
        CancelButton = btnAbbrechen
        ResumeLayout(False)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        suche.Eingabe.Focus()
    End Sub

    Private Function Treffer() As List(Of Filament)
        Dim begriff = suche.Eingabe.Text.Trim()
        If begriff.Length = 0 Then Return _alle
        ' Mehrere Wörter: alle müssen vorkommen ("PETG Orange")
        Dim woerter = begriff.Split(" "c, StringSplitOptions.RemoveEmptyEntries)
        Return _alle.Where(Function(f) woerter.All(Function(w) f.Passt(w))).
                     OrderByDescending(Function(f) String.Equals(f.Code, begriff, StringComparison.OrdinalIgnoreCase)).ToList()
    End Function

    Private Sub Filtern()
        Dim liste = Treffer()
        quelle.DataSource = liste
        lblHinweis.Text = If(liste.Count = 0, "Keine Spule gefunden.",
                             "Enter = Wiegen / Verbrauch · Pfeiltasten wählen · Esc = Abbrechen")
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        Select Case keyData
            Case Keys.Enter
                Uebernehmen(zumWiegen:=True)
                Return True
            Case Keys.Shift Or Keys.Enter
                Uebernehmen(zumWiegen:=False)
                Return True
            Case Keys.Down, Keys.Up
                ' Pfeiltasten im Suchfeld bewegen die Auswahl in der Liste
                If suche.Eingabe.Focused AndAlso grid.Rows.Count > 0 Then
                    Dim zeile = If(grid.CurrentRow?.Index, -1) + If(keyData = Keys.Down, 1, -1)
                    zeile = Math.Max(0, Math.Min(grid.Rows.Count - 1, zeile))
                    grid.CurrentCell = grid.Rows(zeile).Cells(0)
                    Return True
                End If
        End Select
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    ''' <summary>EAN-8/EAN-13/UPC: nur Ziffern, 8 bis 14 Stellen.</summary>
    Private Shared Function IstBarcode(text As String) As Boolean
        Return text.Length >= 8 AndAlso text.Length <= 14 AndAlso text.All(AddressOf Char.IsDigit)
    End Function

    Private Sub Uebernehmen(zumWiegen As Boolean)
        Dim begriff = suche.Eingabe.Text.Trim()
        ' Genaue ID hat Vorrang (Scan), sonst die markierte Zeile
Dim f = _alle.FirstOrDefault(Function(x) String.Equals(x.Code, begriff, StringComparison.OrdinalIgnoreCase))
        ' Barcode einer Verpackung: neue Spule dieser Sorte (bzw. mit diesem Barcode) anlegen
        If f Is Nothing AndAlso IstBarcode(begriff) Then
            Dim bekannt = _alle.Any(Function(x) x.Barcode = begriff)
            If bekannt OrElse MessageBox.Show(Me, $"Der Barcode {begriff} ist noch keiner Sorte zugeordnet. Neue Spule mit diesem Barcode anlegen?",
                                               "Barcode", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                _NeuMitBarcode = begriff
                DialogResult = DialogResult.OK
            End If
            Return
        End If
        If f Is Nothing Then f = grid.Ausgewaehlt(Of Filament)()
        If f Is Nothing Then
            System.Media.SystemSounds.Beep.Play()
            suche.Eingabe.SelectAll()
            Return
        End If
        _Ergebnis = f
        _ZumWiegen = zumWiegen
        DialogResult = DialogResult.OK
    End Sub

End Class
