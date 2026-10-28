Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Strompreis und Leistung je Drucker. Daraus werden beim Buchen die Stromkosten eines Drucks berechnet
''' (Druckzeit × Watt × €/kWh) und zu den Materialkosten addiert.
''' </summary>
Public Class StromkostenDialog
    Inherits Form

    Private ReadOnly _db As IDatenquelle
    Private ReadOnly _drucker As List(Of Drucker)
    Private ReadOnly _watt As New List(Of (Drucker As Drucker, Feld As NumericUpDown))
    Private ReadOnly numPreis As New NumericUpDown With {.Minimum = 0, .Maximum = 5, .DecimalPlaces = 3, .Increment = 0.01D, .Width = 90, .Margin = New Padding(6, 0, 0, 0)}
    Private ReadOnly lblBeispiel As New Label With {.AutoSize = True, .MaximumSize = New Size(460, 0), .ForeColor = Theme.TextLeise, .Margin = New Padding(3, 8, 3, 0)}
    Private ReadOnly chkNachrechnen As New CheckBox With {.Text = "Bisherige Drucke nachrechnen (mit Druckzeit und Drucker)", .AutoSize = True, .Checked = True, .Margin = New Padding(3, 14, 3, 0)}

    Public Sub New(db As IDatenquelle)
        _db = db
        _drucker = db.DruckerLaden()
        OberflaecheAufbauen()
        numPreis.Value = Math.Min(numPreis.Maximum, Strom.PreisLesen(db.EinstellungLesen(Strom.PreisSchluessel)))
        AddHandler numPreis.ValueChanged, Sub(s, e) BeispielZeigen()
        BeispielZeigen()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Stromkosten"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim p As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoSize = True,
                                           .Padding = New Padding(20, 16, 20, 16)}
        p.Controls.Add(New Label With {
            .Text = "Zu den Materialkosten eines Drucks kommt der Strom: Druckzeit × Leistung des Druckers × Strompreis. " &
                    "Ohne Strompreis oder bei 0 Watt wird kein Strom berechnet.",
            .AutoSize = True, .MaximumSize = New Size(460, 0), .Margin = New Padding(3, 0, 3, 12)})

        Dim preis As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 0, 0, 8)}
        preis.Controls.Add(New Label With {.Text = "Strompreis", .AutoSize = True, .Margin = New Padding(3, 4, 0, 0)})
        preis.Controls.Add(numPreis)
        preis.Controls.Add(New Label With {.Text = "€/kWh", .AutoSize = True, .Margin = New Padding(4, 4, 0, 0)})
        p.Controls.Add(preis)

        p.Controls.Add(New Label With {.Text = "Leistung beim Drucken", .AutoSize = True, .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark, .Margin = New Padding(3, 10, 3, 0)})
        p.Controls.Add(New Label With {
            .Text = "Durchschnitt über den ganzen Druck (Heizbett, Düse, Lüfter). Am genauesten mit einem Steckdosen-Messgerät: kWh ÷ Druckstunden × 1000. " &
                    "Vorgeschlagen sind typische Werte.",
            .AutoSize = True, .MaximumSize = New Size(460, 0), .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein, .Margin = New Padding(3, 2, 3, 6)})
        If _drucker.Count = 0 Then
            p.Controls.Add(New Label With {.Text = "Noch kein Drucker angelegt (Seite ""Drucker"").", .AutoSize = True, .ForeColor = Theme.TextLeise})
        End If
        Dim t As New TableLayoutPanel With {.ColumnCount = 3, .AutoSize = True, .Margin = New Padding(0)}
        For Each d In _drucker
            Dim vorschlag = d.Watt <= 0
            Dim feld As New NumericUpDown With {.Minimum = 0, .Maximum = 3000, .Increment = 10, .Width = 80,
                                                .Value = If(vorschlag, Strom.TypischeWatt(d.Name, d.Typ), d.Watt)}
            t.Controls.Add(New Label With {.Text = d.Name, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 5, 16, 3)}, 0, t.RowCount)
            t.Controls.Add(feld, 1, t.RowCount)
            t.Controls.Add(New Label With {.Text = "W" & If(vorschlag, "  (Vorschlag)", ""), .AutoSize = True, .Anchor = AnchorStyles.Left,
                                           .ForeColor = If(vorschlag, Theme.TextLeise, Theme.TextNormal), .Margin = New Padding(4, 5, 0, 3)}, 2, t.RowCount)
            t.RowCount += 1
            AddHandler feld.ValueChanged, Sub(s, e) BeispielZeigen()
            _watt.Add((d, feld))
        Next
        p.Controls.Add(t)
        p.Controls.Add(lblBeispiel)
        p.Controls.Add(chkNachrechnen)

        Dim knoepfe As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Anchor = AnchorStyles.Right,
                                                .Margin = New Padding(0, 16, 0, 0)}
        Dim btnOk As New WebButton("Speichern", "", KnopfStil.Primaer) With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0)}
        Dim btnAbbrechen As New WebButton("Abbrechen") With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0), .DialogResult = DialogResult.Cancel}
        AddHandler btnOk.Click, Sub(s, e) Speichern()
        knoepfe.Controls.Add(btnAbbrechen)
        knoepfe.Controls.Add(btnOk)
        p.Controls.Add(knoepfe)

        Controls.Add(p)
        AcceptButton = btnOk
        CancelButton = btnAbbrechen
        ResumeLayout(False)
        PerformLayout()
    End Sub

    ''' <summary>Kosten pro Druckstunde je Drucker als Orientierung.</summary>
    Private Sub BeispielZeigen()
        Dim preis = EingabeWert(numPreis)
        If preis <= 0D OrElse _watt.Count = 0 Then
            lblBeispiel.Text = If(preis <= 0D, "Ohne Strompreis werden keine Stromkosten berechnet.", "")
            Return
        End If
        lblBeispiel.Text = "Pro Druckstunde: " & String.Join(" · ", _watt.Select(Function(w) $"{w.Drucker.Name} {Strom.Kosten(3600, CInt(EingabeWert(w.Feld)), preis):C2}"))
    End Sub

    Private Sub Speichern()
        Dim anzahl = 0
        Try
            _db.EinstellungSpeichern(Strom.PreisSchluessel, Strom.PreisText(EingabeWert(numPreis)))
            For Each w In _watt
                Dim watt = CInt(EingabeWert(w.Feld))
                If w.Drucker.Watt = watt Then Continue For
                w.Drucker.Watt = watt
                _db.DruckerSpeichern(w.Drucker)
            Next
            If chkNachrechnen.Checked Then anzahl = _db.StromkostenNachrechnen()
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        If chkNachrechnen.Checked Then
            MessageBox.Show(Me, If(anzahl = 0, "Gespeichert. An den bisherigen Drucken hat sich nichts geändert.",
                                   $"Gespeichert. Die Stromkosten von {anzahl} Druck{If(anzahl = 1, "", "en")} wurden neu berechnet."),
                            "Stromkosten", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
        DialogResult = DialogResult.OK
    End Sub

End Class
