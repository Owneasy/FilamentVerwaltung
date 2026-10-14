Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Restgewicht aktualisieren: Verbrauch abziehen, Spule nachwiegen oder Rest direkt eingeben.
''' </summary>
Public Class VerbrauchDialog
    Inherits Form

    Private ReadOnly _f As Filament

    Private ReadOnly rbVerbrauch As New RadioButton With {.Text = "Verbrauch abziehen (g)", .AutoSize = True, .Checked = True}
    Private ReadOnly rbGewogen As New RadioButton With {.Text = "Spule gewogen – Gesamtgewicht inkl. Leerspule (g)", .AutoSize = True}
    Private ReadOnly rbDirekt As New RadioButton With {.Text = "Restgewicht direkt eingeben (g)", .AutoSize = True}
    Private ReadOnly numWert As New NumericUpDown With {.Maximum = 100000, .Width = 120, .ThousandsSeparator = True, .Increment = 10}
    Private ReadOnly lblVorschau As New Label With {.AutoSize = True, .Margin = New Padding(3, 10, 3, 3), .ForeColor = Theme.Akzent, .Font = Theme.SchriftFett}
    Private ReadOnly txtBezeichnung As New TextBox With {.Width = 360, .Margin = New Padding(3, 4, 3, 3), .PlaceholderText = "z. B. Halterung v2, Fehldruck …"}

    Public Sub New(f As Filament)
        _f = f
        OberflaecheAufbauen()
        VorschauAktualisieren()
    End Sub

    ''' <summary>Das neue Restgewicht gemäß Auswahl.</summary>
    Public ReadOnly Property NeuesRestgewicht As Integer
        Get
            Dim wert = CInt(EingabeWert(numWert))
            Dim ergebnis As Integer
            If rbVerbrauch.Checked Then
                ergebnis = _f.Restgewicht - wert
            ElseIf rbGewogen.Checked Then
                ergebnis = wert - _f.Spulengewicht
            Else
                ergebnis = wert
            End If
            Return Math.Max(0, ergebnis)
        End Get
    End Property

    ''' <summary>Art der Buchung im Verlauf: Verbrauch abziehen = Druck, sonst Wiegen.</summary>
    Public ReadOnly Property Art As String
        Get
            Return If(rbVerbrauch.Checked, Buchung.ArtDruck, Buchung.ArtWiegen)
        End Get
    End Property

    Public ReadOnly Property Bezeichnung As String
        Get
            Return txtBezeichnung.Text.Trim()
        End Get
    End Property

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Restgewicht aktualisieren – " & _f.Code
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim p As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False,
            .AutoSize = True,
            .Padding = New Padding(20, 16, 20, 16),
            .Location = New Point(0, 0)
        }

        Dim info As New Label With {
            .AutoSize = True,
            .Text = ($"{_f.Hersteller} {_f.Material} {_f.Farbe}").Trim() & Environment.NewLine &
                    $"Aktuell: {_f.Restgewicht:N0} g von {_f.Gewicht:N0} g   (Leerspule: {_f.Spulengewicht:N0} g)",
            .Margin = New Padding(3, 3, 3, 12), .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark
        }
        p.Controls.Add(info)
        p.Controls.Add(rbVerbrauch)
        p.Controls.Add(rbGewogen)
        p.Controls.Add(rbDirekt)
        numWert.Margin = New Padding(20, 6, 3, 3)
        p.Controls.Add(numWert)
        p.Controls.Add(lblVorschau)
        p.Controls.Add(New Label With {.Text = "Wofür? (optional, erscheint im Verlauf)", .AutoSize = True, .Margin = New Padding(3, 14, 3, 0)})
        p.Controls.Add(txtBezeichnung)

        If _f.Spulengewicht <= 0 Then
            rbGewogen.Text &= "  [Leerspulengewicht im Filament eintragen!]"
        End If

        AddHandler rbVerbrauch.CheckedChanged, AddressOf Modus_CheckedChanged
        AddHandler rbGewogen.CheckedChanged, AddressOf Modus_CheckedChanged
        AddHandler rbDirekt.CheckedChanged, AddressOf Modus_CheckedChanged
        AddHandler numWert.ValueChanged, Sub(s, e) VorschauAktualisieren()
        AddHandler numWert.KeyUp, Sub(s, e) VorschauAktualisieren()

        Dim btnOk As New WebButton("Übernehmen", "", KnopfStil.Primaer) With {.MinimumSize = New Size(100, 0),
                                      .DialogResult = DialogResult.OK}
        Dim btnAbbrechen As New WebButton("Abbrechen") With {.MinimumSize = New Size(100, 0),
                                             .DialogResult = DialogResult.Cancel}
        Dim knoepfe As New FlowLayoutPanel With {.AutoSize = True, .Margin = New Padding(0, 12, 0, 0)}
        knoepfe.Controls.Add(btnOk)
        knoepfe.Controls.Add(btnAbbrechen)
        p.Controls.Add(knoepfe)

        Controls.Add(p)
        AcceptButton = btnOk
        CancelButton = btnAbbrechen

        ResumeLayout(False)
        PerformLayout()
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        numWert.Select(0, numWert.Text.Length)
        numWert.Focus()
    End Sub

    Private Sub Modus_CheckedChanged(sender As Object, e As EventArgs)
        Dim rb = DirectCast(sender, RadioButton)
        If Not rb.Checked Then Return
        If rb Is rbVerbrauch Then
            numWert.Value = 0
        ElseIf rb Is rbGewogen Then
            numWert.Value = Math.Min(numWert.Maximum, _f.Restgewicht + _f.Spulengewicht)
        Else
            numWert.Value = Math.Min(numWert.Maximum, _f.Restgewicht)
        End If
        VorschauAktualisieren()
        numWert.Focus()
    End Sub

    Private Sub VorschauAktualisieren()
        Dim neu = NeuesRestgewicht
        Dim anteil = If(_f.Gewicht > 0, CDbl(neu) / _f.Gewicht, 0.0)
        lblVorschau.Text = $"Neues Restgewicht: {neu:N0} g  ({anteil:P0})"
    End Sub

End Class
