Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Seite "Dateien": CAD-Modelle, STL/3MF und G-Code ablegen, in 3D ansehen und als Druck buchen.
''' Die Dateien werden in den Ordner "Dateien" neben der Datenbank kopiert.
''' </summary>
Public Class DateienSeite
    Inherits UserControl
    Implements ISeite

    Private Const AlleTypen As String = "Alle Dateitypen"

    Private ReadOnly _db As Datenbank
    Private _alle As New List(Of Datei)
    Private _buchungen As New List(Of Buchung)
    Private _filterWirdAktualisiert As Boolean
    Private _angezeigt As Datei
    Private _ladeNummer As Integer

    Private ReadOnly grid As New WebTabelle()
    Private ReadOnly quelle As New BindingSource()
    Private ReadOnly suche As New SuchFeld()
    Private ReadOnly cboTyp As New ComboBox()
    Private ReadOnly vorschau As New ModellAnsicht With {.Dock = DockStyle.Fill, .Margin = New Padding(0)}
    Private ReadOnly tipps As New ToolTip()
    Private karte As TabellenKarte

    ' --- Detailbereich
    Private ReadOnly lblName As New Label With {.AutoSize = True, .Font = Theme.SchriftLogo, .ForeColor = Theme.TextStark,
                                                .Margin = New Padding(0, 0, 0, 2)}
    Private ReadOnly lblUnterzeile As New Label With {.AutoSize = True, .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein,
                                                      .Margin = New Padding(0, 0, 0, 10)}
    Private ReadOnly infos As New TableLayoutPanel With {.AutoSize = True, .ColumnCount = 2, .Dock = DockStyle.Top, .Margin = New Padding(0)}
    Private ReadOnly txtNotiz As New TextBox With {.Multiline = True, .Height = 54, .Dock = DockStyle.Top, .ScrollBars = ScrollBars.Vertical,
                                                   .PlaceholderText = "Notiz zur Datei (wird beim Verlassen gespeichert)"}
    Private ReadOnly btnBuchen As New WebButton("Als Druck buchen", Theme.SymDrucker, KnopfStil.Primaer)
    Private ReadOnly btnOeffnen As New WebButton("Öffnen", Theme.SymOeffnen)
    Private ReadOnly btnImOrdner As New WebButton("Im Ordner", Theme.SymOrdnerOffen)
    Private ReadOnly btnLoeschen As New WebButton("Löschen", Theme.SymLoeschen, KnopfStil.Gefahr)
    Private details As Panel

    Public Sub New(db As Datenbank)
        _db = db
        OberflaecheAufbauen()
    End Sub

    Public Sub Aktivieren() Implements ISeite.Aktivieren
        grid.Focus()
    End Sub

    Public Sub Neuladen() Implements ISeite.Neuladen
        DatenLaden()
    End Sub

    ' ------------------------------------------------------------------ Oberfläche

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        Dock = DockStyle.Fill
        BackColor = Theme.Seite
        Font = Theme.Schrift
        Padding = New Padding(28, 24, 28, 24)

        Dim seite As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .Margin = New Padding(0)}
        seite.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        seite.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim kopf As New SeitenKopf("Dateien", "CAD-Modelle, STL/3MF und G-Code – einfach hierher ziehen")
        Dim btnOrdner As New WebButton("Ordner öffnen", Theme.SymOrdnerOffen)
        Dim btnHinzu As New WebButton("Dateien hinzufügen", Theme.SymHinzufuegen, KnopfStil.Primaer) With {.Margin = New Padding(0)}
        AddHandler btnOrdner.Click, Sub(s, e) OrdnerOeffnen(Nothing)
        AddHandler btnHinzu.Click, Sub(s, e) DateienWaehlen()
        tipps.SetToolTip(btnOrdner, "Ordner öffnen, in dem die Dateien gespeichert werden")
        tipps.SetToolTip(btnHinzu, "Dateien auswählen und hinzufügen (oder per Drag & Drop hierher ziehen)")
        kopf.Knoepfe.Controls.Add(btnOrdner)
        kopf.Knoepfe.Controls.Add(btnHinzu)
        seite.Controls.Add(kopf, 0, 0)

        Dim inhalt As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .Margin = New Padding(0)}
        inhalt.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        inhalt.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 420))

        ' --- Liste
        TabelleAufbauen()
        karte = New TabellenKarte(grid) With {.Margin = New Padding(0, 0, 16, 0)}
        suche.Margin = New Padding(0, 0, 8, 4)
        suche.Width = 240
        AddHandler suche.Eingabe.TextChanged, Sub(s, e) AnsichtAktualisieren()
        karte.Werkzeug.Controls.Add(suche)
        FilamentSeite.FilterGestalten(cboTyp, 150)
        AddHandler cboTyp.SelectedIndexChanged, Sub(s, e) AnsichtAktualisieren()
        karte.Werkzeug.Controls.Add(cboTyp)
        karte.FussRechts.Text = "Dateien hierher ziehen zum Hinzufügen"
        inhalt.Controls.Add(karte, 0, 0)

        ' --- Vorschau und Details
        Dim rechts As New Karte With {.Dock = DockStyle.Fill, .Padding = New Padding(12)}
        Dim spalte As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .Margin = New Padding(0)}
        spalte.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        spalte.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        spalte.Controls.Add(vorschau, 0, 0)
        details = DetailsAufbauen()
        spalte.Controls.Add(details, 0, 1)
        rechts.Controls.Add(spalte)
        inhalt.Controls.Add(rechts, 1, 0)

        seite.Controls.Add(inhalt, 0, 1)
        Controls.Add(seite)
        DragDropAnmelden(Me)
        DetailsAnzeigen(Nothing)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Private Function DetailsAufbauen() As Panel
        Dim p As New Panel With {.Dock = DockStyle.Fill, .AutoSize = True, .Margin = New Padding(4, 14, 4, 2)}
        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 1, .Margin = New Padding(0)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        infos.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        infos.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        Dim knoepfe As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top, .WrapContents = True, .Margin = New Padding(0, 10, 0, 0)}
        For Each b In {btnBuchen, btnOeffnen, btnImOrdner, btnLoeschen}
            b.Margin = New Padding(0, 0, 8, 6)
            knoepfe.Controls.Add(b)
        Next
        AddHandler btnBuchen.Click, Sub(s, e) AlsDruckBuchen()
        AddHandler btnOeffnen.Click, Sub(s, e) Oeffnen()
        AddHandler btnImOrdner.Click, Sub(s, e) OrdnerOeffnen(_angezeigt)
        AddHandler btnLoeschen.Click, Sub(s, e) Loeschen()
        tipps.SetToolTip(btnBuchen, "Druck mit dieser Datei erfassen – Gewicht und Druckzeit werden aus der Datei übernommen")
        tipps.SetToolTip(btnOeffnen, "Datei mit dem zugeordneten Programm öffnen (z. B. Slicer)")
        tipps.SetToolTip(btnImOrdner, "Datei im Explorer anzeigen")
        AddHandler txtNotiz.Leave, Sub(s, e) NotizSpeichern()

        lblName.MaximumSize = New Size(380, 0)
        For Each c As Control In New Control() {lblName, lblUnterzeile, infos, txtNotiz, knoepfe}
            t.Controls.Add(c)
        Next
        p.Controls.Add(t)
        Return p
    End Function

    Private Sub InfoZeile(beschriftung As String, wert As String)
        If String.IsNullOrEmpty(wert) Then Return
        infos.Controls.Add(New Label With {.Text = beschriftung, .AutoSize = True, .ForeColor = Theme.TextLeise,
                                           .Margin = New Padding(0, 2, 16, 4)}, 0, infos.RowCount)
        infos.Controls.Add(New Label With {.Text = wert, .AutoSize = True, .ForeColor = Theme.TextStark, .Font = Theme.SchriftFett,
                                           .Margin = New Padding(0, 2, 0, 4)}, 1, infos.RowCount)
        infos.RowCount += 1
    End Sub

    Private Sub TabelleAufbauen()
        grid.DataSource = quelle
        grid.SortSpalte = "Hinzugefuegt"
        grid.SortAufsteigend = False
        grid.Spalte("Name", "Name", 220, minBreite:=160)
        grid.Spalte("Typ", "Typ", 0).MinimumWidth = Px(Me, 80)
        grid.Spalte("Groesse", "Größe", 0, rechtsbuendig:=True)
        grid.Spalte("Gramm", "Verbrauch", 0, rechtsbuendig:=True)
        grid.Spalte("Druckzeit", "Druckzeit", 0, rechtsbuendig:=True)
        grid.Spalte("Hinzugefuegt", "Hinzugefügt", 0, "dd.MM.yyyy", True)
        grid.Columns("Name").DefaultCellStyle.Font = Theme.SchriftFett

        Dim menue As New ContextMenuStrip With {.Font = Theme.Schrift, .ShowImageMargin = False}
        menue.Items.Add("Als Druck buchen", Nothing, Sub(s, e) AlsDruckBuchen())
        menue.Items.Add("Öffnen", Nothing, Sub(s, e) Oeffnen())
        menue.Items.Add("Im Ordner anzeigen", Nothing, Sub(s, e) OrdnerOeffnen(_angezeigt))
        menue.Items.Add(New ToolStripSeparator())
        menue.Items.Add("Löschen", Nothing, Sub(s, e) Loeschen()).ForeColor = Theme.Rot
        AddHandler menue.Opening, Sub(s, e) e.Cancel = Ausgewaehlt() Is Nothing
        grid.ContextMenuStrip = menue

        AddHandler grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 Then Oeffnen()
                                          End Sub
        AddHandler grid.SelectionChanged, Sub(s, e) DetailsAnzeigen(Ausgewaehlt())
        AddHandler grid.SortierungGeaendert, Sub(s, e) AnsichtAktualisieren()
        AddHandler grid.CellFormatting, AddressOf Grid_CellFormatting
        AddHandler grid.CellPainting, AddressOf Grid_CellPainting
        AddHandler grid.Paint, AddressOf Grid_Paint
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        Select Case keyData
            Case Keys.Control Or Keys.N, Keys.Control Or Keys.O
                DateienWaehlen()
                Return True
            Case Keys.Control Or Keys.F
                suche.Eingabe.Focus()
                suche.Eingabe.SelectAll()
                Return True
            Case Keys.F5
                DatenLaden()
                Return True
            Case Keys.Enter
                If grid.Focused Then
                    Oeffnen()
                    Return True
                End If
            Case Keys.Delete
                If grid.Focused Then
                    Loeschen()
                    Return True
                End If
        End Select
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    ' ------------------------------------------------------------------ Drag & Drop

    Private Sub DragDropAnmelden(c As Control)
        c.AllowDrop = True
        AddHandler c.DragEnter, AddressOf Ziel_DragEnter
        AddHandler c.DragDrop, AddressOf Ziel_DragDrop
        For Each kind As Control In c.Controls
            DragDropAnmelden(kind)
        Next
    End Sub

    Private Sub Ziel_DragEnter(sender As Object, e As DragEventArgs)
        e.Effect = If(e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(DataFormats.FileDrop), DragDropEffects.Copy, DragDropEffects.None)
    End Sub

    Private Sub Ziel_DragDrop(sender As Object, e As DragEventArgs)
        Dim pfade = TryCast(e.Data?.GetData(DataFormats.FileDrop), String())
        If pfade IsNot Nothing Then
            ' Erst nach dem Drop verarbeiten, damit der Explorer nicht blockiert wird
            BeginInvoke(Sub() Importieren(pfade))
        End If
    End Sub

    ' ------------------------------------------------------------------ Daten & Anzeige

    Private Sub DatenLaden(Optional auswahlId As Long = 0)
        Try
            _alle = _db.DateienLaden()
            _buchungen = _db.BuchungenLaden()
        Catch ex As Exception
            Fehler("Die Dateien konnten nicht geladen werden.", ex)
            Return
        End Try
        TypFilterAktualisieren()
        AnsichtAktualisieren(auswahlId)
    End Sub

    Private Sub TypFilterAktualisieren()
        Dim bisher = TryCast(cboTyp.SelectedItem, String)
        _filterWirdAktualisiert = True
        cboTyp.Items.Clear()
        cboTyp.Items.Add(AlleTypen)
        For Each typ In _alle.Select(Function(d) d.Typ).Where(Function(x) x.Length > 0).Distinct().OrderBy(Function(x) x)
            cboTyp.Items.Add(typ)
        Next
        Dim index = If(bisher Is Nothing, 0, cboTyp.Items.IndexOf(bisher))
        cboTyp.SelectedIndex = If(index < 0, 0, index)
        _filterWirdAktualisiert = False
    End Sub

    Private Sub AnsichtAktualisieren(Optional auswahlId As Long = 0)
        If _filterWirdAktualisiert Then Return
        Dim merkeId = auswahlId
        If merkeId = 0 AndAlso _angezeigt IsNot Nothing Then merkeId = _angezeigt.Id

        Dim treffer As IEnumerable(Of Datei) = _alle
        Dim typ = TryCast(cboTyp.SelectedItem, String)
        If typ IsNot Nothing AndAlso typ <> AlleTypen Then treffer = treffer.Where(Function(d) d.Typ = typ)
        Dim begriff = suche.Eingabe.Text.Trim()
        If begriff.Length > 0 Then treffer = treffer.Where(Function(d) d.Passt(begriff))

        Dim liste = grid.Sortieren(treffer)
        quelle.DataSource = liste
        If merkeId <> 0 Then grid.Auswaehlen(Of Datei)(Function(d) d.Id = merkeId)
        DetailsAnzeigen(Ausgewaehlt())
        karte.FussLinks.Text = $"{liste.Count:N0} von {_alle.Count:N0} Dateien · {GroesseText(liste.Sum(Function(d) d.Groesse))}"
    End Sub

    Private Function Ausgewaehlt() As Datei
        Return grid.Ausgewaehlt(Of Datei)()
    End Function

    Private Sub DetailsAnzeigen(d As Datei)
        If d IsNot Nothing AndAlso _angezeigt IsNot Nothing AndAlso d.Id = _angezeigt.Id AndAlso infos.Controls.Count > 0 Then Return
        NotizSpeichern()
        _angezeigt = d

        infos.SuspendLayout()
        infos.Controls.Clear()
        infos.RowCount = 0
        If d Is Nothing Then
            lblName.Text = If(_alle.Count = 0, "Noch keine Dateien", "Keine Datei ausgewählt")
            lblUnterzeile.Text = If(_alle.Count = 0, "Ziehe STL-, 3MF- oder G-Code-Dateien hierher.", "Wähle links eine Datei aus.")
            txtNotiz.Text = ""
        Else
            lblName.Text = d.Name
            lblUnterzeile.Text = $"{d.Typ} · {GroesseText(d.Groesse)} · hinzugefügt am {d.Hinzugefuegt:dd.MM.yyyy HH:mm}"
            InfoZeile("Verbrauch laut Slicer", GrammText(d.Gramm))
            InfoZeile("Druckzeit laut Slicer", DauerText(d.Druckzeit))
            InfoZeile("Material", d.Material)
            Dim drucke = _buchungen.Where(Function(b) b.DateiId.HasValue AndAlso b.DateiId.Value = d.Id).ToList()
            InfoZeile("Gedruckt", If(drucke.Count = 0, "noch nie",
                                     $"{drucke.Count}× · zuletzt am {drucke.Max(Function(b) b.Datum):dd.MM.yyyy}"))
            txtNotiz.Text = d.Notiz
        End If
        infos.ResumeLayout()
        txtNotiz.Visible = d IsNot Nothing
        For Each b In {btnBuchen, btnOeffnen, btnImOrdner, btnLoeschen}
            b.Visible = d IsNot Nothing
        Next
        VorschauLaden(d)
    End Sub

    Private Async Sub VorschauLaden(d As Datei)
        _ladeNummer += 1
        Dim nummer = _ladeNummer
        vorschau.Netz = Nothing
        If d Is Nothing Then
            vorschau.Hinweis = If(_alle.Count = 0, "Hier erscheint die 3D-Vorschau von STL-, 3MF- und OBJ-Dateien.",
                                  "Datei auswählen, um die Vorschau zu sehen.")
            Return
        End If
        Dim pfad = _db.DateiPfad(d)
        If Not File.Exists(pfad) Then
            vorschau.Hinweis = "Die Datei wurde im Dateien-Ordner nicht gefunden."
            Return
        End If
        If Not Netz.KannLaden(pfad) Then
            vorschau.Hinweis = If(Datei.GcodeTypen.Contains(d.Typ), "G-Code hat keine 3D-Vorschau – die Druckdaten stehen unten.",
                                  $"Für {d.Typ}-Dateien gibt es keine Vorschau. Mit ""Öffnen"" im passenden Programm ansehen.")
            Return
        End If
        vorschau.Hinweis = "Lade Vorschau …"
        Try
            Dim modell = Await Task.Run(Function() Netz.Laden(pfad))
            If nummer = _ladeNummer Then vorschau.Netz = modell
        Catch ex As Exception
            If nummer = _ladeNummer Then vorschau.Hinweis = "Keine Vorschau möglich: " & ex.Message
        End Try
    End Sub

    Private Sub Grid_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs)
        If e.RowIndex < 0 Then Return
        Dim d = grid.Eintrag(Of Datei)(e.RowIndex)
        If d Is Nothing Then Return
        Select Case grid.Columns(e.ColumnIndex).Name
            Case "Groesse"
                e.Value = GroesseText(d.Groesse)
                e.FormattingApplied = True
            Case "Gramm"
                e.Value = GrammText(d.Gramm)
                e.FormattingApplied = True
            Case "Druckzeit"
                e.Value = DauerText(d.Druckzeit)
                e.FormattingApplied = True
        End Select
    End Sub

    Private Sub Grid_CellPainting(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse e.Graphics Is Nothing OrElse e.CellStyle Is Nothing Then Return
        If grid.Columns(e.ColumnIndex).Name <> "Typ" Then Return
        Dim d = grid.Eintrag(Of Datei)(e.RowIndex)
        If d Is Nothing Then Return
        Dim farbe = If(d.IstModell, Theme.Akzent,
                       If(Datei.GcodeTypen.Contains(d.Typ), Theme.Gruen,
                          If({"STEP", "STP", "F3D", "SCAD"}.Contains(d.Typ), Theme.Lila, Theme.TextLeise)))
        grid.BadgeZeichnen(e, d.Typ, farbe, Hell(farbe, 0.12))
    End Sub

    ''' <summary>Hinweis in der leeren Tabelle.</summary>
    Private Sub Grid_Paint(sender As Object, e As PaintEventArgs)
        If grid.Rows.Count > 0 Then Return
        Dim oben = grid.ColumnHeadersHeight
        Dim r As New Rectangle(Px(Me, 24), oben + Px(Me, 40), grid.Width - Px(Me, 48), Px(Me, 80))
        Dim text = If(_alle.Count = 0,
                      "Noch keine Dateien." & Environment.NewLine & "Ziehe STL, 3MF, STEP oder G-Code hierher oder klicke auf ""Dateien hinzufügen"".",
                      "Keine Datei passt zur Suche.")
        TextRenderer.DrawText(e.Graphics, text, Theme.Schrift, r, Theme.TextLeise,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.WordBreak Or TextFormatFlags.NoPadding)
    End Sub

    ' ------------------------------------------------------------------ Aktionen

    Private Sub DateienWaehlen()
        Using dlg As New OpenFileDialog With {.Filter = Datei.DialogFilter, .Multiselect = True, .Title = "Dateien hinzufügen"}
            If dlg.ShowDialog(Me) = DialogResult.OK Then Importieren(dlg.FileNames)
        End Using
    End Sub

    Private Sub Importieren(pfade As IEnumerable(Of String))
        Dim fehlerListe As New List(Of String)
        Dim letzte As Datei = Nothing
        Cursor = Cursors.WaitCursor
        Try
            For Each pfad In pfade
                If Directory.Exists(pfad) Then
                    fehlerListe.Add($"{Path.GetFileName(pfad)}: Ordner werden nicht unterstützt")
                    Continue For
                End If
                Try
                    letzte = _db.DateiImportieren(pfad)
                Catch ex As Exception
                    fehlerListe.Add($"{Path.GetFileName(pfad)}: {ex.Message}")
                End Try
            Next
        Finally
            Cursor = Cursors.Default
        End Try
        _angezeigt = Nothing
        DatenLaden(If(letzte?.Id, 0))
        If fehlerListe.Count > 0 Then
            MessageBox.Show(Me, "Einige Dateien konnten nicht hinzugefügt werden:" & Environment.NewLine & Environment.NewLine &
                            String.Join(Environment.NewLine, fehlerListe), "Dateien hinzufügen", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

    Private Sub AlsDruckBuchen()
        Dim d = Ausgewaehlt()
        If d Is Nothing Then Return
        Dim filamente As List(Of Filament)
        Try
            filamente = _db.AlleLaden()
        Catch ex As Exception
            Fehler("Die Spulen konnten nicht geladen werden.", ex)
            Return
        End Try

        Dim b As New Buchung With {
            .Datum = Date.Now,
            .DateiId = d.Id,
            .Bezeichnung = Path.GetFileNameWithoutExtension(d.Name),
            .Gramm = If(d.HatDruckdaten, CInt(Math.Ceiling(d.Gramm.Value)), 0),
            .Druckzeit = d.Druckzeit
        }
        ' Spule mit passendem Material vorschlagen
        If d.Material.Length > 0 Then
            Dim material = d.Material.Split(","c)(0).Trim()
            Dim passend = filamente.FirstOrDefault(Function(f) f.Restgewicht > 0 AndAlso f.Material.StartsWith(material, StringComparison.OrdinalIgnoreCase))
            If passend IsNot Nothing Then b.FilamentId = passend.Id
        End If

        If DruckeSeite.DruckDialogZeigen(Me, _db, b, filamente, _alle, "Druck erfassen") Then
            _angezeigt = Nothing
            DatenLaden(d.Id)
        End If
    End Sub

    Private Sub Oeffnen()
        Dim d = Ausgewaehlt()
        If d Is Nothing Then Return
        Try
            Process.Start(New ProcessStartInfo(_db.DateiPfad(d)) With {.UseShellExecute = True})
        Catch ex As Exception
            Fehler("Die Datei konnte nicht geöffnet werden (evtl. ist kein Programm für diesen Dateityp eingerichtet).", ex)
        End Try
    End Sub

    Private Sub OrdnerOeffnen(d As Datei)
        Try
            Directory.CreateDirectory(_db.DateienOrdner)
            If d Is Nothing Then
                Process.Start("explorer.exe", """" & _db.DateienOrdner & """")
            Else
                Process.Start("explorer.exe", "/select,""" & _db.DateiPfad(d) & """")
            End If
        Catch ex As Exception
            Fehler("Der Ordner konnte nicht geöffnet werden.", ex)
        End Try
    End Sub

    Private Sub Loeschen()
        Dim d = Ausgewaehlt()
        If d Is Nothing Then Return
        Dim antwort = MessageBox.Show(Me,
            $"Soll die Datei ""{d.Name}"" gelöscht werden?" & Environment.NewLine &
            "Sie wird auch aus dem Dateien-Ordner entfernt. Erfasste Drucke bleiben erhalten.",
            "Datei löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
        If antwort <> DialogResult.Yes Then Return
        Try
            _db.DateiLoeschen(d)
        Catch ex As Exception
            Fehler("Die Datei konnte nicht gelöscht werden.", ex)
        End Try
        _angezeigt = Nothing
        DatenLaden()
    End Sub

    Private Sub NotizSpeichern()
        Dim d = _angezeigt
        If d Is Nothing OrElse txtNotiz.Text = d.Notiz Then Return
        Try
            _db.DateiNotizSpeichern(d.Id, txtNotiz.Text)
            d.Notiz = txtNotiz.Text
        Catch ex As Exception
            Fehler("Die Notiz konnte nicht gespeichert werden.", ex)
        End Try
    End Sub

    Private Sub Fehler(text As String, ex As Exception)
        MessageBox.Show(Me, text & Environment.NewLine & Environment.NewLine & ex.Message,
                        "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
    End Sub

End Class
