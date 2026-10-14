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

    Private ReadOnly _db As IDatenquelle
    Private ReadOnly inhalt As New Panel With {.Dock = DockStyle.Fill, .BackColor = Theme.Seite}
    Private ReadOnly navigation As New List(Of (Knopf As WebButton, Seite As Control))
    ''' <summary>Untermenü unter "Filament": Alle / Vorhanden / Leer (nur sichtbar, solange die Filament-Seite offen ist).</summary>
    Private ReadOnly filamentUntermenue As New List(Of (Knopf As WebButton, Bestand As FilamentBestand))
    Private ReadOnly uebersichtSeite As UebersichtSeite
    Private ReadOnly filamentSeite As FilamentSeite
    Private ReadOnly druckeSeite As DruckeSeite
    Private ReadOnly dateienSeite As DateienSeite
    Private ReadOnly statistikSeite As StatistikSeite
    Private ReadOnly nachkaufSeite As NachkaufSeite
    Private ReadOnly druckerSeite As DruckerSeite
    Private ReadOnly _ueberwachung As IDruckerDienst

    ' --- Beendete Drucke zum Buchen
    Private ReadOnly _druckEnden As New List(Of DruckEnde)
    Private ReadOnly druckBanner As New TableLayoutPanel With {
        .Dock = DockStyle.Top, .Height = 52, .Visible = False, .ColumnCount = 3, .RowCount = 1,
        .BackColor = Hell(Theme.Gruen, 0.12), .Padding = New Padding(28, 8, 20, 8)
    }
    Private ReadOnly lblDruckBanner As New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Font = Theme.SchriftFett,
                                                       .ForeColor = Theme.TextStark}
    Private ReadOnly tipps As New ToolTip()
    Private ReadOnly btnVerbindung As New WebButton("Verbindung", Theme.SymServer, KnopfStil.Navigation) With {.Dock = DockStyle.Fill, .Height = 42}
    ''' <summary>Prüft beim Server regelmäßig, ob die Verbindung steht (grüner / roter Punkt).</summary>
    Private ReadOnly verbindungsTimer As New Timer With {.Interval = 20000}
    Private _verbindungPruefenLaeuft As Boolean

    ' --- Updates
    Private Const EinstellungUebersprungen As String = "Update.UebersprungeneVersion"
    Private ReadOnly banner As New TableLayoutPanel With {
        .Dock = DockStyle.Top, .Height = 52, .Visible = False, .ColumnCount = 3, .RowCount = 1,
        .BackColor = Hell(Theme.Akzent, 0.1), .Padding = New Padding(28, 8, 20, 8)
    }
    Private ReadOnly lblBanner As New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .ForeColor = Theme.AkzentText,
                                                  .Font = Theme.SchriftFett}
    Private ReadOnly btnUpdate As New WebButton("Nach Updates suchen", Theme.SymExport, KnopfStil.Navigation) With {.Dock = DockStyle.Fill, .Height = 42}
    Private _update As UpdateInfo

    Public Sub New(db As IDatenquelle, ueberwachung As IDruckerDienst)
        _db = db
        filamentSeite = New FilamentSeite(db)
        druckeSeite = New DruckeSeite(db)
        _ueberwachung = ueberwachung
        dateienSeite = New DateienSeite(db, _ueberwachung)
        statistikSeite = New StatistikSeite(db)
        nachkaufSeite = New NachkaufSeite(db)
        druckerSeite = New DruckerSeite(db, _ueberwachung)
        uebersichtSeite = New UebersichtSeite(db, _ueberwachung)
        AddHandler uebersichtSeite.SeiteOeffnen, AddressOf UebersichtZiel
        AddHandler _ueberwachung.DruckBeendet, AddressOf DruckBeendet
        AddHandler _ueberwachung.DruckEndeEntfernt, AddressOf DruckEndeEntfernt
        AddHandler filamentSeite.AnzahlGeaendert, Sub(alle, vorhanden, leer)
                                                      filamentUntermenue(0).Knopf.Zahl = alle.ToString("N0")
                                                      filamentUntermenue(1).Knopf.Zahl = vorhanden.ToString("N0")
                                                      filamentUntermenue(2).Knopf.Zahl = leer.ToString("N0")
                                                  End Sub
        AddHandler filamentSeite.BestandGeaendert, Sub() UntermenueAktualisieren()
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
        ForeColor = Theme.TextNormal
        Text = "Filament-Verwaltung"
        Size = New Size(1440, 860)
        MinimumSize = New Size(1000, 720)
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

        Dim logo As New Panel With {.Height = 44, .Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 0, 16)}
        AddHandler logo.Paint, Sub(s, e) LogoZeichnen(e.Graphics, logo)
        Zeile(leiste, logo)

        Zeile(leiste, New Label With {
            .Text = "VERWALTUNG",
            .Font = Theme.SchriftKopf,
            .ForeColor = Color.FromArgb(100, 116, 139),
            .AutoSize = True,
            .Margin = New Padding(16, 0, 0, 8)
        })
        Zeile(leiste, NavEintrag("Übersicht", Theme.SymStart, uebersichtSeite))
        Dim navFilament = NavEintrag("Filament", Theme.SymSpule, filamentSeite)
        AddHandler navFilament.Click, Sub(s, e) FilamentAnsicht(FilamentBestand.Alle)
        Zeile(leiste, navFilament)
        For Each eintrag In {("Alle", FilamentBestand.Alle), ("Vorhanden", FilamentBestand.Vorhanden), ("Leer", FilamentBestand.Leer)}
            Dim bestand = eintrag.Item2
            Dim knopf As New WebButton(eintrag.Item1, "", KnopfStil.Navigation) With {
                .Unterpunkt = True, .Dock = DockStyle.Fill, .Height = 30, .Margin = New Padding(0), .Visible = False}
            AddHandler knopf.Click, Sub(s, e) FilamentAnsicht(bestand)
            filamentUntermenue.Add((knopf, bestand))
            Zeile(leiste, knopf)
        Next
        filamentUntermenue.Last().Knopf.Margin = New Padding(0, 0, 0, 4)
        tipps.SetToolTip(filamentUntermenue(0).Knopf, "Alle Spulen")
        tipps.SetToolTip(filamentUntermenue(1).Knopf, "Nur Spulen mit Restbestand")
        tipps.SetToolTip(filamentUntermenue(2).Knopf, "Nur leere Spulen")
        Zeile(leiste, NavEintrag("Drucke", Theme.SymDrucker, druckeSeite))
        Zeile(leiste, NavEintrag("Drucker", Theme.SymNetzDrucker, druckerSeite))
        Zeile(leiste, NavEintrag("Nachkauf", Theme.SymGeld, nachkaufSeite))
        Zeile(leiste, NavEintrag("Statistik", Theme.SymDiagramm, statistikSeite))
        Zeile(leiste, NavEintrag("Dateien (CAD / STL)", Theme.SymOrdner, dateienSeite))

        Zeile(leiste, New Label With {
            .Text = "WERKZEUGE",
            .Font = Theme.SchriftKopf,
            .ForeColor = Color.FromArgb(100, 116, 139),
            .AutoSize = True,
            .Margin = New Padding(16, 12, 0, 8)
        })
        Dim btnSuchen As New WebButton("Spule suchen", Theme.SymScanner, KnopfStil.Navigation) With {.Dock = DockStyle.Fill, .Height = 42, .Margin = New Padding(0, 0, 0, 4)}
        AddHandler btnSuchen.Click, Sub(s, e) SchnellsucheZeigen()
        tipps.SetToolTip(btnSuchen, "QR-Code scannen oder ID eingeben und die Spule öffnen (Strg+K)")
        Zeile(leiste, btnSuchen)
        Dim btnEtiketten As New WebButton("Etiketten drucken", Theme.SymDrucker, KnopfStil.Navigation) With {.Dock = DockStyle.Fill, .Height = 42, .Margin = New Padding(0, 0, 0, 4)}
        AddHandler btnEtiketten.Click, Sub(s, e) filamentSeite.EtikettenDrucken()
        tipps.SetToolTip(btnEtiketten, "Etiketten mit QR-Code für die Spulen drucken")
        Zeile(leiste, btnEtiketten)

        ' Platzhalter schiebt den Datenbank-Bereich nach unten
        leiste.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        leiste.Controls.Add(New Panel With {.Margin = New Padding(0)}, 0, leiste.RowCount)
        leiste.RowCount += 1

        AddHandler btnVerbindung.Click, Sub(s, e) VerbindungZeigen()
        VerbindungAnzeigen(True)
        Zeile(leiste, btnVerbindung)
        Dim btnSicherungen As New WebButton("Sicherungen", Theme.SymSicherung, KnopfStil.Navigation) With {.Dock = DockStyle.Fill, .Height = 42}
        AddHandler btnSicherungen.Click, Sub(s, e) SicherungenZeigen()
        tipps.SetToolTip(btnSicherungen, "Sicherungen der Datenbank ansehen, jetzt sichern oder wiederherstellen")
        Zeile(leiste, btnSicherungen)

        Dim btnDesign As New WebButton("Design", If(Theme.Dunkel, Theme.SymMond, Theme.SymSonne), KnopfStil.Navigation) With {.Dock = DockStyle.Fill, .Height = 42}
        AddHandler btnDesign.Click, Sub(s, e) DesignMenueZeigen(btnDesign)
        tipps.SetToolTip(btnDesign, "Helles oder dunkles Design, oder wie in Windows eingestellt")
        Zeile(leiste, btnDesign)

        AddHandler btnUpdate.Click, Sub(s, e) UpdatePruefen(stumm:=False)
        tipps.SetToolTip(btnUpdate, $"Auf GitHub ({GitHubRepo}) nach einer neueren Version suchen")
        Zeile(leiste, btnUpdate)

        Dim lblPfad As New Label With {
            .Text = _db.Ort,
            .Font = New Font("Segoe UI", 8.0F),
            .ForeColor = Color.FromArgb(100, 116, 139),
            .AutoEllipsis = True,
            .Dock = DockStyle.Fill,
            .Height = 20,
            .Margin = New Padding(16, 2, 4, 0)
        }
        If TypeOf _db Is Datenbank Then
            lblPfad.Cursor = Cursors.Hand
            AddHandler lblPfad.Click, Sub(s, e) DbOrdnerOeffnen()
            tipps.SetToolTip(lblPfad, _db.Ort & Environment.NewLine & "Klicken, um den Ordner zu öffnen")
        Else
            tipps.SetToolTip(lblPfad, "Verbunden mit " & _db.Ort)
        End If
        Zeile(leiste, lblPfad)
        Zeile(leiste, New Label With {
            .Text = $"Version {AktuelleVersion}",
            .Font = New Font("Segoe UI", 8.0F),
            .ForeColor = Color.FromArgb(100, 116, 139),
            .AutoSize = True,
            .Margin = New Padding(16, 2, 4, 0)
        })

        BannerAufbauen()
        DruckBannerAufbauen()

        ' Reihenfolge wichtig für das Andocken: zuerst die füllende Fläche, dann der Balken über dem Inhalt, dann die Leiste
        Controls.Add(inhalt)
        Controls.Add(banner)
        Controls.Add(druckBanner)
        Controls.Add(leiste)

        SeiteZeigen(uebersichtSeite)
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

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If keyData = (Keys.Control Or Keys.K) Then
            SchnellsucheZeigen()
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    ''' <summary>Spule per Scan / ID suchen und auf der Filament-Seite öffnen.</summary>
    Private Sub SchnellsucheZeigen()
        Dim spulen As List(Of Filament)
        Try
            spulen = _db.AlleLaden()
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        Using dlg As New SchnellsucheDialog(spulen)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.Ergebnis Is Nothing Then Return
            SeiteZeigen(filamentSeite)
            filamentSeite.SpuleOeffnen(dlg.Ergebnis.Code, dlg.ZumWiegen)
        End Using
    End Sub

    ''' <summary>Menü Hell / Dunkel / Wie Windows; das neue Design gilt nach einem Neustart.</summary>
    Private Sub DesignMenueZeigen(knopf As Control)
        Dim aktuell = DesignEinstellung.Hell
        Try
            aktuell = If(_db.EinstellungLesen(DesignEinstellung.Schluessel), DesignEinstellung.Hell)
        Catch ex As Exception
            ' Standard: hell
        End Try
        Dim menue As New ContextMenuStrip With {.Font = Theme.Schrift}
        For Each eintrag In {(DesignEinstellung.Hell, "Hell"), (DesignEinstellung.Dunkel, "Dunkel"), (DesignEinstellung.WieWindows, "Wie Windows")}
            Dim wert = eintrag.Item1
            Dim punkt As New ToolStripMenuItem(eintrag.Item2) With {.Checked = aktuell = wert}
            AddHandler punkt.Click, Sub(s, e) DesignWaehlen(wert, aktuell)
            menue.Items.Add(punkt)
        Next
        AddHandler menue.Closed, Sub(s, e) BeginInvoke(Sub() menue.Dispose())
        menue.Show(knopf, New Point(knopf.Width, 0), ToolStripDropDownDirection.AboveRight)
    End Sub

    Private Sub DesignWaehlen(wert As String, bisher As String)
        If wert = bisher Then Return
        Try
            _db.EinstellungSpeichern(DesignEinstellung.Schluessel, wert)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        Dim antwort = MessageBox.Show(Me, "Das neue Design wird nach einem Neustart übernommen. Jetzt neu starten?",
                                      "Design", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If antwort = DialogResult.Yes Then Application.Restart()
    End Sub

    ''' <summary>Punkt am Knopf "Verbindung": grün = verbunden bzw. lokale Datenbank offen, rot = Server nicht erreichbar.</summary>
    Private Sub VerbindungAnzeigen(verbunden As Boolean)
        btnVerbindung.Punkt = If(verbunden, Theme.Gruen, Theme.Rot)
        Dim server = TryCast(_db, ServerDatenquelle)
        tipps.SetToolTip(btnVerbindung,
            If(server Is Nothing, "Lokale Datenbank – klicken, um Datenbank oder Server zu wählen",
               If(verbunden, "Verbunden mit " & server.Basis, "Keine Verbindung zum Server – es wird regelmäßig erneut versucht")))
    End Sub

    ''' <summary>Im Hintergrund prüfen, damit die Oberfläche nicht hängt (auch wenn der Server nicht antwortet).</summary>
    Private Async Sub VerbindungPruefen()
        Dim server = TryCast(_db, ServerDatenquelle)
        If server Is Nothing OrElse _verbindungPruefenLaeuft Then Return
        _verbindungPruefenLaeuft = True
        Try
            Dim ok = Await Task.Run(Function() server.Erreichbar())
            If Not IsDisposed Then VerbindungAnzeigen(ok)
        Finally
            _verbindungPruefenLaeuft = False
        End Try
    End Sub

    ''' <summary>Verbindung ändern; danach startet die App neu (alle Seiten hängen an der Datenquelle).</summary>
    Private Sub VerbindungZeigen()
        Using dlg As New VerbindungDialog(Verbindung.Laden())
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
        End Using
        If MessageBox.Show(Me, "Die neue Verbindung wird nach einem Neustart verwendet. Jetzt neu starten?", "Verbindung",
                           MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then Application.Restart()
    End Sub

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
        UntermenueAktualisieren()
    End Sub

    Private Sub FilamentAnsicht(bestand As FilamentBestand)
        If _aktuelleSeite IsNot filamentSeite Then SeiteZeigen(filamentSeite)
        filamentSeite.BestandZeigen(bestand)
        UntermenueAktualisieren()
    End Sub

    Private Sub UntermenueAktualisieren()
        Dim offen = _aktuelleSeite Is filamentSeite
        For Each eintrag In filamentUntermenue
            eintrag.Knopf.Visible = offen
            eintrag.Knopf.Aktiv = offen AndAlso filamentSeite.Bestand = eintrag.Bestand
        Next
    End Sub

    ''' <summary>"Alle anzeigen" auf der Übersicht.</summary>
    Private Sub UebersichtZiel(ziel As String)
        Select Case ziel
            Case UebersichtSeite.ZielFilament : SeiteZeigen(filamentSeite)
            Case UebersichtSeite.ZielDrucke : SeiteZeigen(druckeSeite)
            Case UebersichtSeite.ZielDrucker : SeiteZeigen(druckerSeite)
            Case UebersichtSeite.ZielNachkauf : SeiteZeigen(nachkaufSeite)
            Case UebersichtSeite.ZielStatistik : SeiteZeigen(statistikSeite)
        End Select
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
        TryCast(_aktuelleSeite, ISeite)?.Aktivieren()
        UpdatePruefen(stumm:=True)
        _ueberwachung.Starten()
        If TypeOf _db Is ServerDatenquelle Then
            AddHandler verbindungsTimer.Tick, Sub(s, x) VerbindungPruefen()
            verbindungsTimer.Start()
        End If
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        verbindungsTimer.Dispose()
        _ueberwachung.Dispose()
        MyBase.OnFormClosed(e)
    End Sub

    ' ------------------------------------------------------------------ Drucker: fertige Drucke buchen

    Private Sub DruckBannerAufbauen()
        druckBanner.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        druckBanner.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        druckBanner.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim btnBuchen As New WebButton("Buchen …", Theme.SymDrucker, KnopfStil.Primaer) With {.Anchor = AnchorStyles.Right}
        Dim btnVerwerfen As New WebButton("Verwerfen") With {.Anchor = AnchorStyles.Right, .Margin = New Padding(0)}
        AddHandler btnBuchen.Click, Sub(s, e) DruckBuchen()
        AddHandler btnVerwerfen.Click, Sub(s, e)
                                           If _druckEnden.Count > 0 Then DruckEndeAbschliessen(_druckEnden(0))
                                       End Sub
        druckBanner.Controls.Add(lblDruckBanner, 0, 0)
        druckBanner.Controls.Add(btnBuchen, 1, 0)
        druckBanner.Controls.Add(btnVerwerfen, 2, 0)
        AddHandler druckBanner.Paint, Sub(s, e)
                                          Using p As New Pen(Hell(Theme.Gruen, 0.3))
                                              e.Graphics.DrawLine(p, 0, druckBanner.Height - 1, druckBanner.Width, druckBanner.Height - 1)
                                          End Using
                                      End Sub
    End Sub

    Private Sub DruckBeendet(ende As DruckEnde)
        _druckEnden.Add(ende)
        DruckBannerAktualisieren()
        If Form.ActiveForm IsNot Me Then TaskleisteBlinken()
    End Sub

''' <summary>Gebucht oder verworfen: aus dem Hinweis nehmen und (beim Server) für alle PCs erledigen.</summary>
    Private Sub DruckEndeAbschliessen(ende As DruckEnde)
        _druckEnden.Remove(ende)
        _ueberwachung.DruckEndeErledigt(ende)
        DruckBannerAktualisieren()
    End Sub

    ''' <summary>An einem anderen PC gebucht oder verworfen.</summary>
    Private Sub DruckEndeEntfernt(endeId As Long)
        _druckEnden.RemoveAll(Function(x) x.Id = endeId)
        DruckBannerAktualisieren()
    End Sub

    Private Sub DruckBannerAktualisieren()
        If _druckEnden.Count = 0 Then
            druckBanner.Visible = False
            Return
        End If
        Dim ende = _druckEnden(0)
        Dim weitere = If(_druckEnden.Count > 1, $"  (+{_druckEnden.Count - 1} weitere)", "")
        lblDruckBanner.Text = If(ende.Abgebrochen,
            $"Druck abgebrochen auf {ende.DruckerName}: {ende.Datei} bei {ende.Fortschritt:P0} – Verbrauch buchen?{weitere}",
            $"Druck fertig auf {ende.DruckerName}: {ende.Datei} – Verbrauch buchen?{weitere}")
        druckBanner.Visible = True
    End Sub

    Private Sub DruckBuchen()
        If _druckEnden.Count = 0 Then Return
        Dim ende = _druckEnden(0)
        Dim filamente As List(Of Filament), dateien As List(Of Datei), zuordnung As Dictionary(Of Integer, Long)
        Try
            filamente = _db.AlleLaden()
            dateien = _db.DateienLaden()
            zuordnung = _db.SpulenZuordnung(ende.DruckerId)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        Dim vorschlag = BuchungVorschlagen(ende, filamente, dateien, zuordnung)
        If DruckeSeite.DruckDialogZeigen(Me, _db, vorschlag, filamente, dateien, $"Druck buchen – {ende.DruckerName}") Then
            DruckEndeAbschliessen(ende)
            If _aktuelleSeite IsNot Nothing Then SeiteZeigen(_aktuelleSeite)
        End If
    End Sub

    <Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)>
    Private Structure FLASHWINFO
        Public cbSize As UInteger
        Public hwnd As IntPtr
        Public dwFlags As UInteger
        Public uCount As UInteger
        Public dwTimeout As UInteger
    End Structure

    <Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function FlashWindowEx(ByRef info As FLASHWINFO) As Boolean
    End Function

    ''' <summary>Taskleisten-Eintrag blinken lassen, bis das Fenster aktiviert wird.</summary>
    Private Sub TaskleisteBlinken()
        Dim info As New FLASHWINFO With {.hwnd = Handle, .dwFlags = 3UI Or 12UI, .uCount = 0, .dwTimeout = 0}
        info.cbSize = CUInt(Runtime.InteropServices.Marshal.SizeOf(info))
        FlashWindowEx(info)
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
            Process.Start("explorer.exe", "/select,""" & _db.Ort & """")
        Catch ex As Exception
            MessageBox.Show(Me, "Der Ordner konnte nicht geöffnet werden." & Environment.NewLine & Environment.NewLine & ex.Message,
                            "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

End Class
