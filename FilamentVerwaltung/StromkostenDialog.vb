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
    Private ReadOnly _watt As New List(Of (Drucker As Drucker, Feld As NumericUpDown, HeizWatt As NumericUpDown, HeizMinuten As NumericUpDown))
    Private ReadOnly _mitAufheizen As Boolean
    Private ReadOnly _schnitt As (Anzahl As Integer, ProDruck As Decimal, ProStunde As Decimal)
    Private ReadOnly numPreis As New NumericUpDown With {.Minimum = 0, .Maximum = 5, .DecimalPlaces = 3, .Increment = 0.01D, .Width = 90, .Margin = New Padding(6, 0, 0, 0)}
    Private ReadOnly lblBeispiel As New Label With {.AutoSize = True, .MaximumSize = New Size(460, 0), .ForeColor = Theme.TextLeise, .Margin = New Padding(3, 8, 3, 0)}
    Private ReadOnly chkNachrechnen As New CheckBox With {.Text = "Bisherige Drucke nachrechnen (mit Druckzeit und Drucker)", .AutoSize = True, .Checked = True, .Margin = New Padding(3, 14, 3, 0)}

    Public Sub New(db As IDatenquelle)
        _db = db
        _drucker = db.DruckerLaden()
        _mitAufheizen = ServerFunktionen.Verfuegbar(db, ServerFunktionen.StromAufheizen)
        _schnitt = SchnittBerechnen()
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
            .Text = "Zu den Materialkosten eines Drucks kommt der Strom: einmal Aufheizen plus Drucken (Druckzeit × Ø Leistung), " &
                    "mal Strompreis. Ohne Strompreis oder bei 0 Watt wird kein Strom berechnet.",
            .AutoSize = True, .MaximumSize = New Size(460, 0), .Margin = New Padding(3, 0, 3, 12)})

        Dim preis As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 0, 0, 8)}
        preis.Controls.Add(New Label With {.Text = "Strompreis", .AutoSize = True, .Margin = New Padding(3, 4, 0, 0)})
        preis.Controls.Add(numPreis)
        preis.Controls.Add(New Label With {.Text = "€/kWh", .AutoSize = True, .Margin = New Padding(4, 4, 0, 0)})
        p.Controls.Add(preis)

        p.Controls.Add(New Label With {.Text = "Leistung der Drucker", .AutoSize = True, .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark, .Margin = New Padding(3, 10, 3, 0)})
        p.Controls.Add(New Label With {
            .Text = "Aufheizen: vor allem das Heizbett, kurz mit hoher Leistung. Drucken: Durchschnitt über den Druck (Bett halten, Düse, Lüfter). " &
                    "Am genauesten mit einem Steckdosen-Messgerät: Wh bis zum ersten Layer bzw. kWh ÷ Druckstunden × 1000. " &
                    "Grau = typischer Wert als Vorschlag.",
            .AutoSize = True, .MaximumSize = New Size(520, 0), .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein, .Margin = New Padding(3, 2, 3, 6)})
        If _drucker.Count = 0 Then
            p.Controls.Add(New Label With {.Text = "Noch kein Drucker angelegt (Seite ""Drucker"").", .AutoSize = True, .ForeColor = Theme.TextLeise})
        End If
        Dim t As New TableLayoutPanel With {.ColumnCount = 3, .AutoSize = True, .Margin = New Padding(0)}
        If _drucker.Count > 0 Then
            t.Controls.Add(Kopf("Aufheizen (einmal pro Druck)"), 1, 0)
            t.Controls.Add(Kopf("Drucken"), 2, 0)
            t.RowCount = 1
        End If
        For Each d In _drucker
            Dim heiz = Strom.TypischesAufheizen(d.Name, d.Typ)
            Dim heizVorschlag = d.AufheizWatt <= 0 AndAlso d.AufheizMinuten <= 0
            Dim vorschlag = d.Watt <= 0
            Dim heizWatt = Zahlfeld(If(heizVorschlag, heiz.Watt, d.AufheizWatt), 3000, 50, heizVorschlag)
            Dim heizMinuten = Zahlfeld(If(heizVorschlag, heiz.Minuten, d.AufheizMinuten), 60, 1, heizVorschlag)
            Dim druckWatt = Zahlfeld(If(vorschlag, Strom.TypischeWatt(d.Name, d.Typ), d.Watt), 3000, 10, vorschlag)
            heizWatt.Enabled = _mitAufheizen : heizMinuten.Enabled = _mitAufheizen

            t.Controls.Add(New Label With {.Text = d.Name, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 5, 16, 3)}, 0, t.RowCount)
            t.Controls.Add(Gruppe(heizWatt, "W  für", heizMinuten, "min"), 1, t.RowCount)
            t.Controls.Add(Gruppe(druckWatt, "W"), 2, t.RowCount)
            t.RowCount += 1
            _watt.Add((d, druckWatt, heizWatt, heizMinuten))
        Next
        p.Controls.Add(t)
        If Not _mitAufheizen Then
            p.Controls.Add(New Label With {.Text = "Das Aufheizen kann erst berechnet werden, wenn der Server aktualisiert ist.", .AutoSize = True,
                                           .ForeColor = Theme.Gelb, .Margin = New Padding(3, 6, 3, 0)})
        End If
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

    Private Shared Function Kopf(text As String) As Label
        Return New Label With {.Text = text, .AutoSize = True, .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein, .Margin = New Padding(3, 0, 16, 2)}
    End Function

    Private Function Zahlfeld(wert As Integer, maximum As Integer, schritt As Integer, vorschlag As Boolean) As NumericUpDown
        Dim n As New NumericUpDown With {.Minimum = 0, .Maximum = maximum, .Increment = schritt, .Width = If(maximum > 100, 70, 50),
                                         .Value = Math.Min(maximum, Math.Max(0, wert)), .ForeColor = If(vorschlag, Theme.TextLeise, Theme.TextNormal)}
        AddHandler n.ValueChanged, Sub(s, e)
                                       n.ForeColor = Theme.TextNormal
                                       BeispielZeigen()
                                   End Sub
        Return n
    End Function

    ''' <summary>Zahlenfelder mit Einheit dahinter in einer Zeile (z. B. "600 W für 4 min").</summary>
    Private Shared Function Gruppe(ParamArray teile As Object()) As FlowLayoutPanel
        Dim g As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 0, 12, 0), .Anchor = AnchorStyles.Left}
        For Each teil In teile
            If TypeOf teil Is Control Then
                g.Controls.Add(DirectCast(teil, Control))
            Else
                g.Controls.Add(New Label With {.Text = CStr(teil), .AutoSize = True, .Margin = New Padding(4, 5, 4, 0)})
            End If
        Next
        Return g
    End Function

    ''' <summary>Was ein Druck je Drucker kostet (Aufheizen + pro Stunde) und der bisherige Durchschnitt.</summary>
    Private Sub BeispielZeigen()
        Dim preis = EingabeWert(numPreis)
        If preis <= 0D OrElse _watt.Count = 0 Then
            lblBeispiel.Text = If(preis <= 0D, "Ohne Strompreis werden keine Stromkosten berechnet.", "")
        Else
            lblBeispiel.Text = String.Join(Environment.NewLine, _watt.Select(
                Function(w)
                    Dim heizen = If(_mitAufheizen, Strom.Aufheizen(CInt(EingabeWert(w.HeizWatt)), CInt(EingabeWert(w.HeizMinuten))) / 1000D * preis, 0D)
                    Dim stunde = Strom.Wattstunden(3600, CInt(EingabeWert(w.Feld))) / 1000D * preis
                    Return $"{w.Drucker.Name}: Aufheizen {heizen:C2} + {stunde:C2} pro Druckstunde"
                End Function))
        End If
        If _schnitt.Anzahl > 0 Then
            lblBeispiel.Text &= Environment.NewLine & $"Bisher im Schnitt {_schnitt.ProDruck:C2} Strom pro Druck" &
                                If(_schnitt.ProStunde > 0D, $" bzw. {_schnitt.ProStunde:C2} pro Druckstunde", "") & $" (aus {_schnitt.Anzahl} Drucken)"
        End If
    End Sub

    ''' <summary>Durchschnitt der bereits gebuchten Stromkosten (vor dem Speichern).</summary>
    Private Function SchnittBerechnen() As (Anzahl As Integer, ProDruck As Decimal, ProStunde As Decimal)
        Try
            Dim mit = _db.BuchungenLaden().Where(Function(b) b.IstDruck AndAlso b.Stromkosten > 0D).ToList()
            If mit.Count = 0 Then Return (0, 0D, 0D)
            Dim stunden = mit.Sum(Function(b) CDec(If(b.Druckzeit, 0))) / 3600D
            Return (mit.Count, mit.Average(Function(b) b.Stromkosten), If(stunden > 0D, mit.Sum(Function(b) b.Stromkosten) / stunden, 0D))
        Catch
            Return (0, 0D, 0D)
        End Try
    End Function

    Private Sub Speichern()
        Dim anzahl = 0
        Try
            _db.EinstellungSpeichern(Strom.PreisSchluessel, Strom.PreisText(EingabeWert(numPreis)))
            For Each w In _watt
                Dim watt = CInt(EingabeWert(w.Feld)), heizWatt = CInt(EingabeWert(w.HeizWatt)), heizMinuten = CInt(EingabeWert(w.HeizMinuten))
                If Not _mitAufheizen Then heizWatt = w.Drucker.AufheizWatt : heizMinuten = w.Drucker.AufheizMinuten
                If w.Drucker.Watt = watt AndAlso w.Drucker.AufheizWatt = heizWatt AndAlso w.Drucker.AufheizMinuten = heizMinuten Then Continue For
                w.Drucker.Watt = watt : w.Drucker.AufheizWatt = heizWatt : w.Drucker.AufheizMinuten = heizMinuten
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
