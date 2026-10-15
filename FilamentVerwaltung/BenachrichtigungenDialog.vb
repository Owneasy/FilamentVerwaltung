Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Wofür der Server Push-Benachrichtigungen schickt (gilt für alle Geräte). Geräte melden sich auf der Handy-Seite
''' über die Glocke an; hier sieht man, welche angemeldet sind, und kann eine Testnachricht schicken.
''' </summary>
Public Class BenachrichtigungenDialog
    Inherits Form

    Private ReadOnly _db As IDatenquelle
    Private ReadOnly _schalter As New List(Of (Schluessel As String, Box As CheckBox))
    Private ReadOnly numGrenze As New NumericUpDown With {.Minimum = 1, .Maximum = 5000, .Increment = 50, .Width = 80, .Margin = New Padding(6, 0, 0, 0)}
    Private ReadOnly lblGeraete As New Label With {.AutoSize = True, .MaximumSize = New Size(460, 0), .ForeColor = Theme.TextLeise, .Margin = New Padding(3, 4, 3, 0)}
    Private ReadOnly btnTest As New WebButton("Test senden", Theme.SymServer)

    Public Sub New(db As IDatenquelle)
        _db = db
        OberflaecheAufbauen()
        WerteLaden()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Benachrichtigungen"
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
            .Text = "Der Server schickt Nachrichten aufs Handy (Sperrbildschirm), auch wenn kein PC läuft. Die Auswahl gilt für alle Geräte.",
            .AutoSize = True, .MaximumSize = New Size(460, 0), .Margin = New Padding(3, 0, 3, 12)})

        For Each eintrag In {(PushEinstellungen.Druckende, "Druck fertig oder abgebrochen"),
                             (PushEinstellungen.SpuleKnapp, "Spule fast leer bzw. leer (nach einer Buchung)"),
                             (PushEinstellungen.DruckerFehler, "Fehler am Drucker"),
                             (PushEinstellungen.DruckerOffline, "Drucker länger als 3 Minuten nicht erreichbar")}
            Dim box As New CheckBox With {.Text = eintrag.Item2, .AutoSize = True, .Margin = New Padding(3, 3, 3, 3)}
            p.Controls.Add(box)
            _schalter.Add((eintrag.Item1, box))
        Next
        Dim grenze As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(22, 2, 0, 8)}
        grenze.Controls.Add(New Label With {.Text = """Fast leer"" unter", .AutoSize = True, .Margin = New Padding(0, 4, 0, 0)})
        grenze.Controls.Add(numGrenze)
        grenze.Controls.Add(New Label With {.Text = "g", .AutoSize = True, .Margin = New Padding(4, 4, 0, 0)})
        p.Controls.Add(grenze)

        p.Controls.Add(New Label With {.Text = "Geräte", .AutoSize = True, .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark, .Margin = New Padding(3, 10, 3, 0)})
        p.Controls.Add(lblGeraete)
        btnTest.Margin = New Padding(3, 8, 3, 0)
        AddHandler btnTest.Click, Sub(s, e) TestSenden()
        p.Controls.Add(btnTest)

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

    Private Sub WerteLaden()
        Try
            For Each s In _schalter
                Dim wert = _db.EinstellungLesen(s.Schluessel)
                s.Box.Checked = If(String.IsNullOrEmpty(wert), PushEinstellungen.Standard(s.Schluessel), wert = "1")
            Next
            Dim g As Integer
            numGrenze.Value = If(Integer.TryParse(_db.EinstellungLesen(PushEinstellungen.SpuleKnappGrenze), g), Math.Max(1, Math.Min(5000, g)), 100)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try

        Dim server = TryCast(_db, ServerDatenquelle)
        If server Is Nothing Then
            lblGeraete.Text = "Benachrichtigungen verschickt nur der Server. Im lokalen Betrieb (Datenbankdatei auf diesem PC) " &
                              "werden die Einstellungen gespeichert, aber es geht keine Nachricht raus."
            btnTest.Visible = False
            Return
        End If
        Try
            Dim geraete = server.PushGeraete()
            lblGeraete.Text = If(geraete.Count = 0,
                "Noch kein Gerät angemeldet. Auf dem Handy die Seite des Servers öffnen (iPhone: als Home-Bildschirm-App) und oben auf die Glocke tippen.",
                String.Join(Environment.NewLine, geraete.Select(Function(g) $"• {If(g.Geraet.Length > 0, g.Geraet, "Gerät")}" &
                                                                             If(g.Erstellt.Length >= 10, $" (seit {g.Erstellt.Substring(8, 2)}.{g.Erstellt.Substring(5, 2)}.{g.Erstellt.Substring(0, 4)})", ""))))
            btnTest.Enabled = geraete.Count > 0
        Catch ex As Exception
            lblGeraete.Text = "Die Geräte konnten nicht geladen werden: " & ex.Message
            btnTest.Enabled = False
        End Try
    End Sub

    Private Async Sub TestSenden()
        Dim server = TryCast(_db, ServerDatenquelle)
        If server Is Nothing Then Return
        btnTest.Enabled = False
        Try
            Dim anzahl = Await Task.Run(Function() server.PushTest())
            MessageBox.Show(Me, If(anzahl > 0, $"Testnachricht an {anzahl} Gerät{If(anzahl = 1, "", "e")} gesendet.",
                                   "Kein Gerät erreicht – auf dem Handy die Benachrichtigungen aus- und wieder einschalten."),
                            "Benachrichtigungen", MessageBoxButtons.OK, If(anzahl > 0, MessageBoxIcon.Information, MessageBoxIcon.Warning))
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        btnTest.Enabled = True
    End Sub

    Private Sub Speichern()
        Try
            For Each s In _schalter
                _db.EinstellungSpeichern(s.Schluessel, If(s.Box.Checked, "1", "0"))
            Next
            _db.EinstellungSpeichern(PushEinstellungen.SpuleKnappGrenze, CInt(EingabeWert(numGrenze)).ToString())
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        DialogResult = DialogResult.OK
    End Sub

End Class
