Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' Hauptfenster im Web-Dashboard-Stil: Navigation links, Inhalt der gewählten Seite rechts.
''' Neue Bereiche: Seite als UserControl anlegen und in OberflaecheAufbauen() mit NavEintrag() registrieren.
''' (Die Oberfläche wird im Code aufgebaut – zum Bearbeiten "Code anzeigen" wählen.)
''' </summary>
Public Class MainForm
    Inherits Form

    Private ReadOnly _db As Datenbank
    Private ReadOnly inhalt As New Panel With {.Dock = DockStyle.Fill, .BackColor = Theme.Seite}
    Private ReadOnly navigation As New List(Of (Knopf As WebButton, Seite As Control))
    Private ReadOnly filamentSeite As FilamentSeite
    Private ReadOnly druckeSeite As DruckeSeite
    Private ReadOnly dateienSeite As DateienSeite
    Private ReadOnly statistikSeite As StatistikSeite
    Private ReadOnly tipps As New ToolTip()

    ' --- Updates
    Private Const EinstellungUebersprungen As String = "Update.UebersprungeneVersion"
    Private ReadOnly banner As New TableLayoutPanel With {
        .Dock = DockStyle.Top, .Height = 52, .Visible = False, .ColumnCount = 3, .RowCount = 1,
        .BackColor = Hell(Theme.Akzent, 0.1), .Padding = New Padding(28, 8, 20, 8)
    }
    Private ReadOnly lblBanner As New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .ForeColor = Theme.AkzentGedrueckt,
                                                  .Font = Theme.SchriftFett}
    Private ReadOnly btnUpdate As New WebButton("Nach Updates suchen", Theme.SymExport, KnopfStil.Navigation) With {.Dock = DockStyle.Fill, .Height = 42}
    Private _update As UpdateInfo

    Public Sub New(db As Datenbank)
        _db = db
        filamentSeite = New FilamentSeite(db)
        druckeSeite = New DruckeSeite(db)
        dateienSeite = New DateienSeite(db)
        statistikSeite = New StatistikSeite(db)
        AddHandler filamentSeite.VerlaufAnzeigen, Sub(id)
                                                      druckeSeite.SpuleFiltern(id)
                                                      SeiteZeigen(druckeSeite)
                                                  End Sub
        OberflaecheAufbauen()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        Font = Theme.Schrift
        BackColor = Theme.Seite
        Text = "Filament-Verwaltung"
        Size = New Size(1440, 860)
        MinimumSize = New Size(1000, 600)
        StartPosition = FormStartPosition.CenterScreen

        ' --- Seitenleiste
        Dim leiste As New TableLayoutPanel With {
            .Dock = DockStyle.Left,
            .Width = 240,
            .BackColor = Theme.Leiste,
            .ColumnCount = 1,
            .Padding = New Padding(12, 18, 12, 14)
        }
        leiste.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        Dim logo As New Panel With {.Height = 44, .Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 0, 22)}
        AddHandler logo.Paint, Sub(s, e) LogoZeichnen(e.Graphics, logo)
        Zeile(leiste, logo)

        Zeile(leiste, New Label With {
            .Text = "VERWALTUNG",
            .Font = Theme.SchriftKopf,
            .ForeColor = Color.FromArgb(100, 116, 139),
            .AutoSize = True,
            .Margin = New Padding(16, 0, 0, 8)
        })
        Zeile(leiste, NavEintrag("Filament", Theme.SymSpule, filamentSeite))
        Zeile(leiste, NavEintrag("Drucke", Theme.SymDrucker, druckeSeite))
        Zeile(leiste, NavEintrag("Statistik", Theme.SymDiagramm, statistikSeite))
        Zeile(leiste, NavEintrag("Dateien (CAD / STL)", Theme.SymOrdner, dateienSeite))

        ' Platzhalter schiebt den Datenbank-Bereich nach unten
        leiste.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        leiste.Controls.Add(New Panel With {.Margin = New Padding(0)}, 0, leiste.RowCount)
        leiste.RowCount += 1

        Dim btnSicherungen As New WebButton("Sicherungen", Theme.SymSicherung, KnopfStil.Navigation) With {.Dock = DockStyle.Fill, .Height = 42}
        AddHandler btnSicherungen.Click, Sub(s, e) SicherungenZeigen()
        tipps.SetToolTip(btnSicherungen, "Sicherungen der Datenbank ansehen, jetzt sichern oder wiederherstellen")
        Zeile(leiste, btnSicherungen)

        AddHandler btnUpdate.Click, Sub(s, e) UpdatePruefen(stumm:=False)
        tipps.SetToolTip(btnUpdate, $"Auf GitHub ({GitHubRepo}) nach einer neueren Version suchen")
        Zeile(leiste, btnUpdate)

        Dim lblPfad As New Label With {
            .Text = _db.Pfad,
            .Font = New Font("Segoe UI", 8.0F),
            .ForeColor = Color.FromArgb(100, 116, 139),
            .AutoEllipsis = True,
            .Dock = DockStyle.Fill,
            .Height = 20,
            .Margin = New Padding(16, 2, 4, 0)
        }
        lblPfad.Cursor = Cursors.Hand
        AddHandler lblPfad.Click, Sub(s, e) DbOrdnerOeffnen()
        tipps.SetToolTip(lblPfad, _db.Pfad & Environment.NewLine & "Klicken, um den Ordner zu öffnen")
        Zeile(leiste, lblPfad)
        Zeile(leiste, New Label With {
            .Text = $"Version {AktuelleVersion}",
            .Font = New Font("Segoe UI", 8.0F),
            .ForeColor = Color.FromArgb(100, 116, 139),
            .AutoSize = True,
            .Margin = New Padding(16, 2, 4, 0)
        })

        BannerAufbauen()

        ' Reihenfolge wichtig für das Andocken: zuerst die füllende Fläche, dann der Balken über dem Inhalt, dann die Leiste
        Controls.Add(inhalt)
        Controls.Add(banner)
        Controls.Add(leiste)

        SeiteZeigen(filamentSeite)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Private Shared Sub Zeile(t As TableLayoutPanel, c As Control)
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        t.Controls.Add(c, 0, t.RowCount)
        t.RowCount += 1
    End Sub

    Private Function NavEintrag(titel As String, symbol As String, seite As Control) As WebButton
        Dim b As New WebButton(titel, symbol, KnopfStil.Navigation) With {
            .Dock = DockStyle.Fill,
            .Height = 42,
            .Margin = New Padding(0, 0, 0, 4)
        }
        AddHandler b.Click, Sub(s, e) SeiteZeigen(seite)
        navigation.Add((b, seite))
        Return b
    End Function

    Private _aktuelleSeite As Control

    Private Sub SicherungenZeigen()
        Using dlg As New SicherungenDialog(_db)
            dlg.ShowDialog(Me)
            ' Nach einer Wiederherstellung die angezeigte Seite neu laden
            If dlg.Wiederhergestellt AndAlso _aktuelleSeite IsNot Nothing Then SeiteZeigen(_aktuelleSeite)
        End Using
    End Sub

    Private Sub SeiteZeigen(seite As Control)
        _aktuelleSeite = seite
        For Each eintrag In navigation
            eintrag.Knopf.Aktiv = eintrag.Seite Is seite
        Next
        ' Beim Anzeigen neu laden, damit Änderungen anderer Seiten sichtbar sind (z. B. Verbrauch durch einen Druck)
        Dim s = TryCast(seite, ISeite)
        s?.Neuladen()
        If Not inhalt.Controls.Contains(seite) Then
            inhalt.SuspendLayout()
            inhalt.Controls.Clear()
            inhalt.Controls.Add(seite)
            inhalt.ResumeLayout()
        End If
        s?.Aktivieren()
    End Sub

    Private Sub LogoZeichnen(g As Graphics, p As Control)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim groesse = Px(p, 36)
        Dim r As New Rectangle(Px(p, 8), (p.Height - groesse) \ 2, groesse, groesse)
        Using pfad = Abgerundet(r, Px(p, 9)), b As New LinearGradientBrush(r, Color.FromArgb(59, 130, 246), Color.FromArgb(124, 58, 237), 45.0F)
            g.FillPath(b, pfad)
        End Using
        TextRenderer.DrawText(g, Theme.SymWuerfel, Theme.SymbolSchriftGross, r, Color.White,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
        Dim x = r.Right + Px(p, 12)
        Dim flags = TextFormatFlags.Left Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
        TextRenderer.DrawText(g, "Filament", Theme.SchriftLogo, New Point(x, r.Y - Px(p, 2)), Color.White, flags)
        TextRenderer.DrawText(g, "Verwaltung", Theme.SchriftKlein, New Point(x, r.Y + Px(p, 20)), Theme.LeisteText, flags)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        TitelleisteFaerben(Me, Theme.Leiste, Color.White)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        filamentSeite.Aktivieren()
        UpdatePruefen(stumm:=True)
    End Sub

    ' ------------------------------------------------------------------ Updates

    Private Sub BannerAufbauen()
        banner.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        banner.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        banner.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim btnDetails As New WebButton("Details & aktualisieren", Theme.SymExport, KnopfStil.Primaer) With {.Anchor = AnchorStyles.Right}
        Dim btnSchliessen As New WebButton("Später") With {.Anchor = AnchorStyles.Right, .Margin = New Padding(0)}
        btnDetails.UseMnemonic = False
        AddHandler btnDetails.Click, Sub(s, e) UpdateDialogZeigen()
        AddHandler btnSchliessen.Click, Sub(s, e) banner.Visible = False
        banner.Controls.Add(lblBanner, 0, 0)
        banner.Controls.Add(btnDetails, 1, 0)
        banner.Controls.Add(btnSchliessen, 2, 0)
        AddHandler banner.Paint, Sub(s, e)
                                     Using p As New Pen(Hell(Theme.Akzent, 0.25))
                                         e.Graphics.DrawLine(p, 0, banner.Height - 1, banner.Width, banner.Height - 1)
                                     End Using
                                 End Sub
    End Sub

    ''' <summary>Sucht nach einer neuen Version. stumm = beim Start: kein Hinweis bei Fehlern oder aktueller Version.</summary>
    Private Async Sub UpdatePruefen(stumm As Boolean)
        btnUpdate.Enabled = False
        btnUpdate.Text = "Suche nach Updates …"
        Try
            Dim info = Await PruefenAsync()
            If info Is Nothing Then
                If Not stumm Then
                    MessageBox.Show(Me, $"Du verwendest die neueste Version ({AktuelleVersion}).", "Updates",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
                Return
            End If
            _update = info
            If stumm Then
                If info.Version.ToString() = UebersprungeneVersion() Then Return
                lblBanner.Text = $"Version {info.Version} ist verfügbar (installiert: {AktuelleVersion})."
                banner.Visible = True
            Else
                UpdateDialogZeigen()
            End If
        Catch ex As Exception
            If Not stumm Then
                MessageBox.Show(Me, "Die Suche nach Updates ist fehlgeschlagen." & Environment.NewLine & Environment.NewLine & ex.Message,
                                "Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If
        Finally
            btnUpdate.Enabled = True
            btnUpdate.Text = "Nach Updates suchen"
        End Try
    End Sub

    Private Sub UpdateDialogZeigen()
        If _update Is Nothing Then Return
        Using dlg As New UpdateDialog(_update)
            dlg.ShowDialog(Me)
            If dlg.Uebersprungen Then
                banner.Visible = False
                Try
                    _db.EinstellungSpeichern(EinstellungUebersprungen, _update.Version.ToString())
                Catch ex As Exception
                    ' Nicht kritisch: der Hinweis erscheint dann beim nächsten Start erneut
                End Try
            End If
        End Using
    End Sub

    Private Function UebersprungeneVersion() As String
        Try
            Return If(_db.EinstellungLesen(EinstellungUebersprungen), "")
        Catch ex As Exception
            Return ""
        End Try
    End Function

    Private Sub DbOrdnerOeffnen()
        Try
            Process.Start("explorer.exe", "/select,""" & _db.Pfad & """")
        Catch ex As Exception
            MessageBox.Show(Me, "Der Ordner konnte nicht geöffnet werden." & Environment.NewLine & Environment.NewLine & ex.Message,
                            "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

End Class
