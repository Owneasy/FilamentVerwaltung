Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Wartung der Drucker: Aufgaben mit Intervall (Druckstunden und/oder Tage), fällige hervorgehoben,
''' abhaken mit "Erledigt", Protokoll der erledigten Wartungen und die gezählten Druckstunden je Drucker.
''' </summary>
Public Class WartungDialog
    Inherits Form

    Private ReadOnly _db As IDatenquelle
    Private ReadOnly grid As New WebTabelle()
    Private ReadOnly quelle As New BindingSource()
    Private ReadOnly protokoll As New WebTabelle()
    Private ReadOnly protokollQuelle As New BindingSource()
    Private ReadOnly lblInfo As New Label With {.AutoSize = False, .Dock = DockStyle.Fill, .Height = 22, .AutoEllipsis = True,
                                                .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein}
    Private ReadOnly btnErledigt As New WebButton("Erledigt", Theme.SymHaken, KnopfStil.Primaer)
    Private ReadOnly btnBearbeiten As New WebButton("Bearbeiten", Theme.SymBearbeiten)
    Private ReadOnly btnLoeschen As New WebButton("Löschen", Theme.SymLoeschen, KnopfStil.Gefahr)
    Private ReadOnly btnAnsicht As New WebButton("Protokoll", Theme.SymVerlauf)
    Private ReadOnly karteAufgaben As New Karte With {.Dock = DockStyle.Fill, .Padding = New Padding(1, 1, 1, 8)}
    Private ReadOnly karteProtokoll As New Karte With {.Dock = DockStyle.Fill, .Padding = New Padding(1, 1, 1, 8), .Visible = False}
    Private _drucker As New List(Of Drucker)

    ''' <summary>Es wurde etwas geändert (Zahl am Menüpunkt "Drucker" neu berechnen).</summary>
    Public ReadOnly Property Geaendert As Boolean

    Public Sub New(db As IDatenquelle)
        _db = db
        OberflaecheAufbauen()
        Laden()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Wartung"
        Size = New Size(1120, 580)
        MinimumSize = New Size(700, 420)
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        BackColor = Theme.Seite
        Padding = New Padding(20, 16, 20, 16)

        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        t.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        Dim kopf As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .AutoSize = True, .WrapContents = False,
                                              .Margin = New Padding(0, 0, 0, 12)}
        kopf.Controls.Add(New Label With {.Text = "Wartung der Drucker", .Font = Theme.SchriftLogo, .ForeColor = Theme.TextStark,
                                          .AutoSize = True, .Margin = New Padding(0)})
        kopf.Controls.Add(New Label With {
            .Text = "Fällig nach Druckstunden, nach Tagen oder beidem (was zuerst eintritt). Die Druckstunden zählt der Server mit, " &
                    "solange gedruckt wird. Ist eine Wartung fällig, kommt eine Benachrichtigung; nach dem Erledigen hier abhaken.",
            .ForeColor = Theme.TextLeise, .AutoSize = True, .MaximumSize = New Size(820, 0), .Margin = New Padding(0, 4, 0, 0)})
        t.Controls.Add(kopf, 0, 0)

        grid.DataSource = quelle
        grid.Spalte("DruckerName", "Drucker", 0, minBreite:=100)
        grid.Spalte("Name", "Aufgabe", 200, minBreite:=160)
        grid.Spalte("IntervallText", "Intervall", 0)
        grid.Spalte("ErledigtAm", "Zuletzt", 0, "dd.MM.yyyy")
        grid.Spalte("RestText", "Fällig", 0).MinimumWidth = Px(Me, 150)
        grid.Columns("Name").DefaultCellStyle.Font = Theme.SchriftFett
        AddHandler grid.CellPainting, AddressOf Grid_CellPainting
        AddHandler grid.SelectionChanged, Sub(s, e) KnoepfeAktualisieren()
        AddHandler grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 Then Bearbeiten()
                                          End Sub
        karteAufgaben.Controls.Add(grid)

        protokoll.DataSource = protokollQuelle
        protokoll.Spalte("Datum", "Erledigt am", 0, "dd.MM.yyyy  HH:mm")
        protokoll.Spalte("DruckerName", "Drucker", 0)
        protokoll.Spalte("Name", "Aufgabe", 200)
        protokoll.Spalte("Druckstunden", "Bei Druckstunden", 0, "#,##0.0 h", True)
        protokoll.Spalte("Notiz", "Notiz", 150)
        karteProtokoll.Controls.Add(protokoll)

        Dim inhalt As New Panel With {.Dock = DockStyle.Fill, .Margin = New Padding(0)}
        inhalt.Controls.Add(karteAufgaben)
        inhalt.Controls.Add(karteProtokoll)
        t.Controls.Add(inhalt, 0, 1)

        lblInfo.Margin = New Padding(2, 8, 0, 0)
        t.Controls.Add(lblInfo, 0, 2)

        Dim knoepfe As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .ColumnCount = 2, .Margin = New Padding(0, 14, 0, 0)}
        knoepfe.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        knoepfe.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim links As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        Dim btnNeu As New WebButton("Neue Aufgabe", Theme.SymNeu)
        Dim btnStunden As New WebButton("Druckstunden", Theme.SymUhr)
        Dim btnPlan As New WebButton("Herstellerplan", Theme.SymHinzufuegen)
        links.Controls.Add(btnErledigt)
        links.Controls.Add(btnNeu)
        links.Controls.Add(btnBearbeiten)
        links.Controls.Add(btnLoeschen)
        links.Controls.Add(btnPlan)
        links.Controls.Add(btnStunden)
        links.Controls.Add(btnAnsicht)
        Dim btnSchliessen As New WebButton("Schließen") With {.Margin = New Padding(0), .DialogResult = DialogResult.Cancel}
        knoepfe.Controls.Add(links, 0, 0)
        knoepfe.Controls.Add(btnSchliessen, 1, 0)
        t.Controls.Add(knoepfe, 0, 3)

        AddHandler btnErledigt.Click, Sub(s, e) Erledigt()
        AddHandler btnNeu.Click, Sub(s, e) Neu()
        AddHandler btnBearbeiten.Click, Sub(s, e) Bearbeiten()
        AddHandler btnLoeschen.Click, Sub(s, e) Loeschen()
        AddHandler btnStunden.Click, Sub(s, e) DruckstundenSetzen()
        AddHandler btnPlan.Click, Sub(s, e) HerstellerplanAnlegen()
        AddHandler btnAnsicht.Click, Sub(s, e) AnsichtWechseln()

        Controls.Add(t)
        CancelButton = btnSchliessen
        ResumeLayout(False)
    End Sub

    Private Sub Laden(Optional auswahlId As Long = 0)
        Dim liste As List(Of Wartung)
        Try
            _drucker = _db.DruckerLaden()
            liste = _db.WartungLaden()
        Catch ex As Exception
            lblInfo.Text = "Die Wartung konnte nicht geladen werden: " & ex.Message
            Return
        End Try
        ' Fällige zuerst, dann nach Dringlichkeit
        quelle.DataSource = liste.OrderByDescending(Function(w) w.Anteil).ToList()
        If auswahlId > 0 Then grid.Auswaehlen(Of Wartung)(Function(w) w.Id = auswahlId)
        KnoepfeAktualisieren()
        Dim faellig = liste.Where(Function(w) w.Faellig).Count()
        Dim stunden = String.Join(" · ", _drucker.Select(Function(d) $"{d.Name}: {d.Drucksekunden / 3600.0:N0} h"))
        lblInfo.Text = If(liste.Count = 0, "Noch keine Wartungsaufgaben – mit ""Neue Aufgabe"" anlegen (Vorschläge sind dabei).",
                          $"{liste.Count} Aufgaben · {faellig} fällig") & If(stunden.Length > 0, " · Druckstunden: " & stunden, "")
    End Sub

    Private Sub KnoepfeAktualisieren()
        Dim w = grid.Ausgewaehlt(Of Wartung)()
        Dim aufgaben = karteAufgaben.Visible
        btnErledigt.Enabled = aufgaben AndAlso w IsNot Nothing
        btnBearbeiten.Enabled = aufgaben AndAlso w IsNot Nothing
        btnLoeschen.Enabled = aufgaben AndAlso w IsNot Nothing
    End Sub

    Private Sub Grid_CellPainting(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse e.Graphics Is Nothing OrElse e.CellStyle Is Nothing Then Return
        If grid.Columns(e.ColumnIndex).Name <> "RestText" Then Return
        Dim w = grid.Eintrag(Of Wartung)(e.RowIndex)
        If w Is Nothing Then Return
        Dim farbe = If(w.Faellig, Theme.Rot, If(w.Bald, Theme.Gelb, Theme.Gruen))
        grid.BadgeZeichnen(e, w.RestText, farbe, Hell(farbe, 0.12))
    End Sub

    Private Sub AnsichtWechseln()
        Dim protokollZeigen = karteAufgaben.Visible
        karteAufgaben.Visible = Not protokollZeigen
        karteProtokoll.Visible = protokollZeigen
        btnAnsicht.Text = If(protokollZeigen, "Aufgaben", "Protokoll")
        btnAnsicht.Symbol = If(protokollZeigen, Theme.SymSpalten, Theme.SymVerlauf)
        btnAnsicht.Parent?.PerformLayout()
        If protokollZeigen Then
            Try
                protokollQuelle.DataSource = _db.WartungProtokollLaden()
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Wartung", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End If
        KnoepfeAktualisieren()
    End Sub

    Private Sub Erledigt()
        Dim w = grid.Ausgewaehlt(Of Wartung)()
        If w Is Nothing Then Return
        Dim notiz = Eingabe.Abfragen(Me, "Wartung erledigt", $"{w.Name} am {w.DruckerName} als erledigt abhaken." & Environment.NewLine &
                                     "Notiz (optional, z. B. ""Düse 0,4 mm gehärtet eingebaut""):", "")
        If notiz Is Nothing Then Return
        Try
            _db.WartungErledigt(w.Id, notiz)
            _Geaendert = True
            Laden(w.Id)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Wartung", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub Neu()
        If _drucker.Count = 0 Then
            MessageBox.Show(Me, "Bitte zuerst einen Drucker anlegen.", "Wartung", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim vorher = grid.Ausgewaehlt(Of Wartung)()
        Dim w As New Wartung With {.DruckerId = If(vorher?.DruckerId, _drucker(0).Id)}
        Using dlg As New WartungBearbeitenDialog(w, _drucker, "Neue Wartungsaufgabe")
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
        End Using
        Speichern(w)
    End Sub

    Private Sub Bearbeiten()
        Dim w = grid.Ausgewaehlt(Of Wartung)()
        If w Is Nothing Then Return
        Using dlg As New WartungBearbeitenDialog(w, _drucker, "Wartungsaufgabe bearbeiten")
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
        End Using
        Speichern(w)
    End Sub

    Private Sub Speichern(w As Wartung)
        Try
            _db.WartungSpeichern(w)
            _Geaendert = True
            Laden(w.Id)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Wartung", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub Loeschen()
        Dim w = grid.Ausgewaehlt(Of Wartung)()
        If w Is Nothing Then Return
        If MessageBox.Show(Me, $"Die Aufgabe ""{w.Name}"" ({w.DruckerName}) löschen? Das Protokoll bleibt erhalten.", "Wartung",
                           MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Try
            _db.WartungLoeschen(w.Id)
            _Geaendert = True
            Laden()
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Wartung", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Zählerstand eines Druckers übernehmen, z. B. die Gesamtdruckzeit aus den Einstellungen des Druckers.</summary>
    ''' <summary>Wartungsplan des Herstellers für einen Drucker übernehmen.</summary>
    Private Sub HerstellerplanAnlegen()
        If _drucker.Count = 0 Then Return
        Dim vorher = grid.Ausgewaehlt(Of Wartung)()
        Using dlg As New HerstellerPlanDialog(_drucker, If(vorher?.DruckerId, _drucker(0).Id))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Dim anzahl = WartungsPlaene.Anlegen(_db, dlg.DruckerId, dlg.Plan)
                _Geaendert = True
                Laden()
                If anzahl = 0 Then MessageBox.Show(Me, "Alle Aufgaben dieses Plans gibt es für den Drucker schon.", "Wartung", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Wartung", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    Private Sub DruckstundenSetzen()
        If _drucker.Count = 0 Then Return
        Dim vorher = grid.Ausgewaehlt(Of Wartung)()
        Using dlg As New DruckstundenDialog(_drucker, If(vorher?.DruckerId, _drucker(0).Id))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                _db.DrucksekundenSetzen(dlg.DruckerId, CLng(dlg.Stunden * 3600))
                _Geaendert = True
                Laden(If(vorher?.Id, 0))
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Wartung", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

End Class

''' <summary>Aufgabe anlegen oder bearbeiten; Vorschläge füllen Name und Intervall aus.</summary>
Public Class WartungBearbeitenDialog
    Inherits Form

    Private ReadOnly _w As Wartung
    Private ReadOnly _drucker As List(Of Drucker)
    Private ReadOnly cboDrucker As New ComboBox With {.Width = 320, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly cboName As New ComboBox With {.Width = 320, .DropDownStyle = ComboBoxStyle.DropDown}
    Private ReadOnly numStunden As New NumericUpDown With {.Maximum = 100000, .Width = 90, .DecimalPlaces = 0, .Increment = 50}
    Private ReadOnly numTage As New NumericUpDown With {.Maximum = 3650, .Width = 90, .Increment = 7}
    Private ReadOnly txtNotiz As New TextBox With {.Width = 320}

    Public Sub New(w As Wartung, drucker As List(Of Drucker), titel As String)
        _w = w
        _drucker = drucker
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

        For Each d In drucker
            cboDrucker.Items.Add(d.Name)
        Next
        cboDrucker.SelectedIndex = Math.Max(0, drucker.FindIndex(Function(d) d.Id = w.DruckerId))
        For Each v In WartungVorlagen.Liste
            cboName.Items.Add(v.Name)
        Next
        AddHandler cboName.SelectedIndexChanged, Sub(s, e)
                                                     If cboName.SelectedIndex < 0 Then Return
                                                     Dim v = WartungVorlagen.Liste(cboName.SelectedIndex)
                                                     numStunden.Value = CDec(If(v.Stunden, 0))
                                                     numTage.Value = If(v.Tage, 0)
                                                 End Sub
        cboName.Text = w.Name
        numStunden.Value = CDec(Math.Min(numStunden.Maximum, If(w.IntervallStunden, 0)))
        numTage.Value = Math.Min(numTage.Maximum, If(w.IntervallTage, 0))
        txtNotiz.Text = w.Notiz

        Dim t As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                            .Padding = New Padding(20, 16, 20, 16)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Zeile(t, "Drucker:", cboDrucker)
        Zeile(t, "Aufgabe:", cboName)
        Dim hinweis As New Label With {.Text = "Vorschlag aus der Liste wählen oder eigenen Text eingeben – die Intervalle sind übliche Werte, bitte anpassen.",
                                       .AutoSize = True, .MaximumSize = New Size(320, 0), .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein,
                                       .Margin = New Padding(3, 0, 3, 8)}
        t.Controls.Add(hinweis, 1, t.RowCount)
        t.RowCount += 1
        Zeile(t, "Alle … Druckstunden:", MitEinheit(numStunden, "h (0 = nicht nach Stunden)"))
        Zeile(t, "Alle … Tage:", MitEinheit(numTage, "Tage (0 = nicht nach Tagen)"))
        Zeile(t, "Notiz:", txtNotiz)

        Dim knoepfe As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Anchor = AnchorStyles.Right,
                                                .Margin = New Padding(0, 12, 0, 0)}
        Dim btnOk As New WebButton("Speichern", "", KnopfStil.Primaer) With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0)}
        Dim btnAbbrechen As New WebButton("Abbrechen") With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0), .DialogResult = DialogResult.Cancel}
        AddHandler btnOk.Click, Sub(s, e) Ok()
        knoepfe.Controls.Add(btnAbbrechen)
        knoepfe.Controls.Add(btnOk)
        t.Controls.Add(knoepfe, 0, t.RowCount)
        t.SetColumnSpan(knoepfe, 2)
        t.RowCount += 1
        Controls.Add(t)
        AcceptButton = btnOk
        CancelButton = btnAbbrechen
    End Sub

    Private Shared Function MitEinheit(feld As Control, einheit As String) As Control
        Dim p As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        p.Controls.Add(feld)
        p.Controls.Add(New Label With {.Text = einheit, .AutoSize = True, .ForeColor = Theme.TextLeise, .Margin = New Padding(6, 6, 0, 0)})
        Return p
    End Function

    Private Shared Sub Zeile(t As TableLayoutPanel, beschriftung As String, steuerelement As Control)
        Dim l As New Label With {.Text = beschriftung, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 7, 12, 3)}
        steuerelement.Anchor = AnchorStyles.Left
        t.Controls.Add(l, 0, t.RowCount)
        t.Controls.Add(steuerelement, 1, t.RowCount)
        t.RowCount += 1
    End Sub

    Private Sub Ok()
        Dim name = cboName.Text.Trim()
        If name.Length = 0 Then
            MessageBox.Show(Me, "Bitte die Aufgabe eintragen.", "Eingabe prüfen", MessageBoxButtons.OK, MessageBoxIcon.Information)
            cboName.Focus()
            Return
        End If
        Dim stunden = EingabeWert(numStunden)
        Dim tage = EingabeWert(numTage)
        If stunden <= 0 AndAlso tage <= 0 Then
            MessageBox.Show(Me, "Bitte ein Intervall eintragen – nach Druckstunden, nach Tagen oder beides.", "Eingabe prüfen",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            numStunden.Focus()
            Return
        End If
        _w.DruckerId = _drucker(cboDrucker.SelectedIndex).Id
        _w.Name = name
        _w.IntervallStunden = If(stunden > 0, CDbl(stunden), CType(Nothing, Double?))
        _w.IntervallTage = If(tage > 0, CInt(tage), CType(Nothing, Integer?))
        _w.Notiz = txtNotiz.Text.Trim()
        DialogResult = DialogResult.OK
    End Sub
End Class

''' <summary>Druckstunden eines Druckers setzen (Zählerstand übernehmen).</summary>
Public Class DruckstundenDialog
    Inherits Form

    Private ReadOnly _drucker As List(Of Drucker)
    Private ReadOnly cboDrucker As New ComboBox With {.Width = 260, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly numStunden As New NumericUpDown With {.Maximum = 1000000, .Width = 110, .DecimalPlaces = 1, .ThousandsSeparator = True}

    Public ReadOnly Property DruckerId As Long
    Public ReadOnly Property Stunden As Double

    Public Sub New(drucker As List(Of Drucker), auswahl As Long)
        _drucker = drucker
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Druckstunden"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        For Each d In drucker
            cboDrucker.Items.Add(d.Name)
        Next
        AddHandler cboDrucker.SelectedIndexChanged, Sub(s, e) numStunden.Value = CDec(Math.Round(_drucker(cboDrucker.SelectedIndex).Drucksekunden / 3600.0, 1))
        cboDrucker.SelectedIndex = Math.Max(0, drucker.FindIndex(Function(d) d.Id = auswahl))

        Dim p As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoSize = True, .Padding = New Padding(20, 16, 20, 16)}
        p.Controls.Add(New Label With {
            .Text = "Gezählte Druckstunden des Druckers. Hier kannst du den Zählerstand übernehmen, den der Drucker selbst anzeigt " &
                    "(Bambu Lab: Einstellungen → Gerät → Druckdauer). Bestehende Aufgaben zählen ab ihrem letzten Erledigen weiter.",
            .AutoSize = True, .MaximumSize = New Size(380, 0), .Margin = New Padding(3, 0, 3, 12)})
        p.Controls.Add(cboDrucker)
        Dim zeile As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 8, 0, 0)}
        zeile.Controls.Add(numStunden)
        zeile.Controls.Add(New Label With {.Text = "Stunden", .AutoSize = True, .Margin = New Padding(6, 6, 0, 0)})
        p.Controls.Add(zeile)
        Dim knoepfe As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Margin = New Padding(0, 16, 0, 0)}
        Dim btnOk As New WebButton("Übernehmen", "", KnopfStil.Primaer) With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0)}
        Dim btnAbbrechen As New WebButton("Abbrechen") With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0), .DialogResult = DialogResult.Cancel}
        AddHandler btnOk.Click, Sub(s, e)
                                    _DruckerId = _drucker(cboDrucker.SelectedIndex).Id
                                    _Stunden = CDbl(EingabeWert(numStunden))
                                    DialogResult = DialogResult.OK
                                End Sub
        knoepfe.Controls.Add(btnAbbrechen)
        knoepfe.Controls.Add(btnOk)
        p.Controls.Add(knoepfe)
        Controls.Add(p)
        AcceptButton = btnOk
        CancelButton = btnAbbrechen
    End Sub
End Class

''' <summary>
''' Wartungsplan des Herstellers für einen Drucker übernehmen: Modell wählen, Aufgaben ansehen, anlegen.
''' Aufgaben, die es für den Drucker schon gibt (gleicher Name), werden übersprungen.
''' </summary>
Public Class HerstellerPlanDialog
    Inherits Form

    Private ReadOnly _drucker As List(Of Drucker)
    Private ReadOnly cboDrucker As New ComboBox With {.Width = 380, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly cboPlan As New ComboBox With {.Width = 380, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly lblAufgaben As New Label With {.AutoSize = True, .MaximumSize = New Size(440, 0), .ForeColor = Theme.TextNormal, .Margin = New Padding(3, 10, 3, 0)}
    Private ReadOnly lnkQuelle As New LinkLabel With {.AutoSize = True, .Margin = New Padding(3, 8, 3, 0), .LinkColor = Theme.Akzent,
                                                      .ActiveLinkColor = Theme.AkzentHover, .LinkBehavior = LinkBehavior.HoverUnderline}

    Public ReadOnly Property DruckerId As Long
    Public ReadOnly Property Plan As WartungsPlan

    ''' <param name="neuAngelegt">Gerade angelegter Drucker: Text und Knopf "Nicht jetzt" passen dazu.</param>
    Public Sub New(drucker As List(Of Drucker), auswahl As Long, Optional neuAngelegt As Boolean = False)
        _drucker = drucker
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Wartungsplan des Herstellers"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        For Each d In drucker
            cboDrucker.Items.Add(d.Name)
        Next
        AddHandler cboDrucker.SelectedIndexChanged, Sub(s, e) PlaeneFuellen()
        AddHandler cboPlan.SelectedIndexChanged, Sub(s, e) PlanZeigen()
        AddHandler lnkQuelle.LinkClicked, Sub(s, e)
                                              Try
                                                  Process.Start(New ProcessStartInfo(lnkQuelle.Tag.ToString()) With {.UseShellExecute = True})
                                              Catch ex As Exception
                                              End Try
                                          End Sub

        Dim p As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoSize = True, .Padding = New Padding(20, 16, 20, 16)}
        p.Controls.Add(New Label With {
            .Text = If(neuAngelegt,
                       "Soll der Wartungsplan des Herstellers für den neuen Drucker angelegt werden? Bitte das Modell wählen.",
                       "Legt die Wartungsaufgaben nach Angaben des Herstellers an. Aufgaben, die es schon gibt, werden übersprungen."),
            .AutoSize = True, .MaximumSize = New Size(440, 0), .Margin = New Padding(3, 0, 3, 12)})
        p.Controls.Add(New Label With {.Text = "Drucker", .AutoSize = True, .ForeColor = Theme.TextLeise, .Margin = New Padding(3, 0, 3, 2)})
        p.Controls.Add(cboDrucker)
        p.Controls.Add(New Label With {.Text = "Modell", .AutoSize = True, .ForeColor = Theme.TextLeise, .Margin = New Padding(3, 10, 3, 2)})
        p.Controls.Add(cboPlan)
        p.Controls.Add(lblAufgaben)
        p.Controls.Add(lnkQuelle)

        Dim knoepfe As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Margin = New Padding(0, 16, 0, 0)}
        Dim btnOk As New WebButton("Anlegen", "", KnopfStil.Primaer) With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0)}
        Dim btnAbbrechen As New WebButton(If(neuAngelegt, "Nicht jetzt", "Abbrechen")) With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0),
                                                                                         .DialogResult = DialogResult.Cancel}
        AddHandler btnOk.Click, Sub(s, e)
                                    _DruckerId = _drucker(cboDrucker.SelectedIndex).Id
                                    _Plan = TryCast(cboPlan.SelectedItem, WartungsPlan)
                                    If _Plan IsNot Nothing Then DialogResult = DialogResult.OK
                                End Sub
        knoepfe.Controls.Add(btnAbbrechen)
        knoepfe.Controls.Add(btnOk)
        p.Controls.Add(knoepfe)
        Controls.Add(p)
        AcceptButton = btnOk
        CancelButton = btnAbbrechen

        cboDrucker.SelectedIndex = Math.Max(0, drucker.FindIndex(Function(d) d.Id = auswahl))
        ' Nur ein Drucker zur Auswahl (z. B. gerade angelegt): Feld nicht änderbar
        cboDrucker.Enabled = Not neuAngelegt
    End Sub

    ''' <summary>Pläne zum Druckertyp zuerst (Bambu Lab, Prusa …), der erste passende ist vorausgewählt.</summary>
    Private Sub PlaeneFuellen()
        Dim d = _drucker(cboDrucker.SelectedIndex)
        cboPlan.Items.Clear()
        For Each vorlage In WartungsPlaene.Fuer(d.Typ)
            cboPlan.Items.Add(vorlage)
        Next
        cboPlan.SelectedIndex = 0
    End Sub

    Private Sub PlanZeigen()
        Dim gewaehlt = TryCast(cboPlan.SelectedItem, WartungsPlan)
        If gewaehlt Is Nothing Then Return
        lblAufgaben.Text = String.Join(Environment.NewLine, gewaehlt.Aufgaben.Select(Function(a)
                                                                                   Dim w As New Wartung With {.IntervallStunden = a.Stunden, .IntervallTage = a.Tage}
                                                                                   Return $"• {a.Name} – {w.IntervallText}"
                                                                               End Function))
        lnkQuelle.Text = "Quelle: " & New Uri(gewaehlt.Quelle).Host
        lnkQuelle.Tag = gewaehlt.Quelle
    End Sub
End Class
