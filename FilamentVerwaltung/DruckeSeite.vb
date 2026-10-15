Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Seite "Drucke": Verlauf aller Buchungen (Drucke, Wiegen, Korrekturen) mit Kosten und Druckzeit.
''' </summary>
Public Class DruckeSeite
    Inherits UserControl
    Implements ISeite

    Private Const AlleSpulen As String = "Alle Spulen"

    Private ReadOnly _db As IDatenquelle
    Private _alle As New List(Of Buchung)
    Private _filamente As New List(Of Filament)
    Private _filterWirdAktualisiert As Boolean
    ''' <summary>Spule, nach der beim nächsten Laden gefiltert werden soll (aus "Verlauf anzeigen").</summary>
    Private _spuleVormerken As Long

    Private ReadOnly grid As New WebTabelle()
    Private ReadOnly quelle As New BindingSource()
    Private ReadOnly suche As New SuchFeld()
    Private ReadOnly cboSpule As New ComboBox()
    Private ReadOnly chkAlleArten As New CheckBox()
    Private ReadOnly btnBearbeiten As New WebButton("Bearbeiten", Theme.SymBearbeiten)
    Private ReadOnly btnLoeschen As New WebButton("Löschen", Theme.SymLoeschen, KnopfStil.Gefahr)
    Private ReadOnly tipps As New ToolTip()
    Private karte As TabellenKarte

    Private ReadOnly kzDrucke As New KennzahlKarte("Drucke diesen Monat", Theme.SymDrucker, Theme.Akzent)
    Private ReadOnly kzVerbrauch As New KennzahlKarte("Verbrauch diesen Monat", Theme.SymSpule, Theme.Gruen)
    Private ReadOnly kzKosten As New KennzahlKarte("Kosten diesen Monat", Theme.SymGeld, Theme.Lila)
    Private ReadOnly kzZeit As New KennzahlKarte("Druckzeit diesen Monat", Theme.SymUhr, Theme.Gelb)

    Public Sub New(db As IDatenquelle)
        _db = db
        OberflaecheAufbauen()
    End Sub

    Public Sub Aktivieren() Implements ISeite.Aktivieren
        grid.Focus()
    End Sub

    Public Sub Neuladen() Implements ISeite.Neuladen
        DatenLaden()
    End Sub

    ''' <summary>Zeigt beim nächsten Laden nur die Buchungen dieser Spule.</summary>
    Public Sub SpuleFiltern(filamentId As Long)
        _spuleVormerken = filamentId
        chkAlleArten.Checked = True
    End Sub

    ' ------------------------------------------------------------------ Oberfläche

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        Dock = DockStyle.Fill
        BackColor = Theme.Seite
        Font = Theme.Schrift
        Padding = New Padding(28, 24, 28, 24)

        Dim seite As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .Margin = New Padding(0)}
        seite.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        seite.RowStyles.Add(New RowStyle(SizeType.Absolute, 134))
        seite.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim kopf As New SeitenKopf("Drucke", "Druckaufträge, Verbrauch und Kosten – jede Änderung am Restgewicht wird hier protokolliert")
        Dim btnNeu As New WebButton("Druck erfassen", Theme.SymNeu, KnopfStil.Primaer) With {.Margin = New Padding(0)}
        AddHandler btnNeu.Click, Sub(s, e) NeuErfassen()
        tipps.SetToolTip(btnNeu, "Druck erfassen und den Verbrauch von der Spule abziehen (Strg+N)")
        kopf.Knoepfe.Controls.Add(btnNeu)
        seite.Controls.Add(kopf, 0, 0)

        seite.Controls.Add(FilamentSeite.KennzahlZeile(kzDrucke, kzVerbrauch, kzKosten, kzZeit), 0, 1)

        TabelleAufbauen()
        karte = New TabellenKarte(grid)
        karte.FussRechts.Text = "Doppelklick zum Bearbeiten"

        suche.Margin = New Padding(0, 0, 8, 4)
        suche.Width = 240
        AddHandler suche.Eingabe.TextChanged, Sub(s, e) AnsichtAktualisieren()
        tipps.SetToolTip(suche.Eingabe, "Sucht in Bezeichnung, Spule, Filament, Datei und Notiz (Strg+F)")
        karte.Werkzeug.Controls.Add(suche)

        FilamentSeite.FilterGestalten(cboSpule, 220)
        AddHandler cboSpule.SelectedIndexChanged, Sub(s, e) AnsichtAktualisieren()
        karte.Werkzeug.Controls.Add(cboSpule)

        chkAlleArten.Text = "Wiegen && Korrekturen"
        chkAlleArten.AutoSize = True
        chkAlleArten.ForeColor = Theme.TextNormal
        chkAlleArten.Margin = New Padding(4, 8, 16, 4)
        AddHandler chkAlleArten.CheckedChanged, Sub(s, e) AnsichtAktualisieren()
        tipps.SetToolTip(chkAlleArten, "Auch Nachwiegen und manuelle Änderungen des Restgewichts anzeigen")
        karte.Werkzeug.Controls.Add(chkAlleArten)

        For Each b In {btnBearbeiten, btnLoeschen}
            b.Margin = New Padding(0, 0, 8, 4)
            karte.Werkzeug.Controls.Add(b)
        Next
        AddHandler btnBearbeiten.Click, Sub(s, e) Bearbeiten()
        AddHandler btnLoeschen.Click, Sub(s, e) Loeschen()
        tipps.SetToolTip(btnBearbeiten, "Ausgewählten Druck bearbeiten – der Verbrauch der Spule wird angepasst")
        tipps.SetToolTip(btnLoeschen, "Eintrag löschen, wahlweise mit Rückbuchung auf die Spule (Entf)")

        seite.Controls.Add(karte, 0, 2)
        Controls.Add(seite)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Private Sub TabelleAufbauen()
        grid.DataSource = quelle
        grid.SortSpalte = "Datum"
        grid.SortAufsteigend = False

        grid.Spalte("Datum", "Datum", 0, "dd.MM.yyyy  HH:mm")
        grid.Spalte("Art", "Art", 0, minBreite:=90).MinimumWidth = Px(Me, 100)
        grid.Spalte("Bezeichnung", "Bezeichnung", 160, minBreite:=120)
        grid.Spalte("FilamentCode", "Spule", 0)
        grid.Spalte("FilamentText", "Filament", 130, minBreite:=110)
        grid.Spalte("Gramm", "Verbrauch", 0, "#,##0 g", True)
        grid.Spalte("Kosten", "Kosten", 0, "C2", True)
        grid.Spalte("Druckzeit", "Druckzeit", 0, rechtsbuendig:=True)
        grid.Spalte("DateiName", "Datei", 110, minBreite:=80)
        grid.Columns("FilamentCode").DefaultCellStyle.Font = Theme.SchriftFett

        Dim menue As New ContextMenuStrip With {.Font = Theme.Schrift, .ShowImageMargin = False}
        menue.Items.Add("Bearbeiten", Nothing, Sub(s, e) Bearbeiten())
        menue.Items.Add(New ToolStripSeparator())
        menue.Items.Add("Löschen", Nothing, Sub(s, e) Loeschen()).ForeColor = Theme.Rot
        AddHandler menue.Opening, Sub(s, e) e.Cancel = Ausgewaehlt() Is Nothing
        grid.ContextMenuStrip = menue

        AddHandler grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 Then Bearbeiten()
                                          End Sub
        AddHandler grid.SelectionChanged, Sub(s, e) KnoepfeAktualisieren()
        AddHandler grid.SortierungGeaendert, Sub(s, e) AnsichtAktualisieren()
        AddHandler grid.CellFormatting, AddressOf Grid_CellFormatting
        AddHandler grid.CellPainting, AddressOf Grid_CellPainting
    End Sub

    Private Sub KnoepfeAktualisieren()
        Dim b = Ausgewaehlt()
        btnBearbeiten.Enabled = b IsNot Nothing AndAlso b.IstDruck
        btnLoeschen.Enabled = b IsNot Nothing
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        Select Case keyData
            Case Keys.Control Or Keys.N
                NeuErfassen()
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
                    Bearbeiten()
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

    ' ------------------------------------------------------------------ Daten & Anzeige

    Private Sub DatenLaden(Optional auswahlId As Long = 0)
        Try
            _alle = _db.BuchungenLaden()
            _filamente = _db.AlleLaden()
        Catch ex As Exception
            Fehler("Die Drucke konnten nicht geladen werden.", ex)
            Return
        End Try
        SpulenFilterAktualisieren()
        AnsichtAktualisieren(auswahlId)
    End Sub

    Private Sub SpulenFilterAktualisieren()
        Dim bisher = If(_spuleVormerken <> 0, Nothing, TryCast(cboSpule.SelectedItem, String))
        _filterWirdAktualisiert = True
        cboSpule.Items.Clear()
        cboSpule.Items.Add(AlleSpulen)
        ' Nur Spulen, zu denen es Einträge gibt
        Dim codes = _alle.Select(Function(b) b.FilamentCode).Distinct().OrderBy(Function(c) c).ToList()
        For Each c In codes
            cboSpule.Items.Add(c)
        Next
        If _spuleVormerken <> 0 Then
            Dim f = _filamente.FirstOrDefault(Function(x) x.Id = _spuleVormerken)
            If f IsNot Nothing AndAlso Not codes.Contains(f.Code) Then cboSpule.Items.Add(f.Code)
            bisher = f?.Code
            _spuleVormerken = 0
        End If
        Dim index = If(bisher Is Nothing, 0, cboSpule.Items.IndexOf(bisher))
        cboSpule.SelectedIndex = If(index < 0, 0, index)
        _filterWirdAktualisiert = False
    End Sub

    Private Sub AnsichtAktualisieren(Optional auswahlId As Long = 0)
        If _filterWirdAktualisiert Then Return
        Dim merkeId = auswahlId
        If merkeId = 0 Then
            Dim aktuell = Ausgewaehlt()
            If aktuell IsNot Nothing Then merkeId = aktuell.Id
        End If

        Dim treffer As IEnumerable(Of Buchung) = _alle
        If Not chkAlleArten.Checked Then treffer = treffer.Where(Function(b) b.IstDruck)
        Dim spule = TryCast(cboSpule.SelectedItem, String)
        If spule IsNot Nothing AndAlso spule <> AlleSpulen Then treffer = treffer.Where(Function(b) b.FilamentCode = spule)
        Dim begriff = suche.Eingabe.Text.Trim()
        If begriff.Length > 0 Then treffer = treffer.Where(Function(b) b.Passt(begriff))

        Dim liste = grid.Sortieren(treffer)
        quelle.DataSource = liste
        If merkeId <> 0 Then grid.Auswaehlen(Of Buchung)(Function(b) b.Id = merkeId)
        KnoepfeAktualisieren()

        ' Kennzahlen: Verbrauch und Kosten zählen Drucke und Nachwiegen (beides ist echter Verbrauch)
        Dim monatsAnfang = New Date(Date.Today.Year, Date.Today.Month, 1)
        Dim verbrauch = _alle.Where(Function(b) b.Art <> Buchung.ArtKorrektur AndAlso b.Gramm > 0).ToList()
        Dim drucke = _alle.Where(Function(b) b.IstDruck).ToList()
        Dim imMonat As Func(Of Buchung, Boolean) = Function(b) b.Datum >= monatsAnfang

        kzDrucke.Setzen(drucke.Where(imMonat).Count().ToString("N0"), $"{drucke.Count:N0} insgesamt")
        kzVerbrauch.Setzen($"{verbrauch.Where(imMonat).Sum(Function(b) b.Gramm):N0} g",
                           $"{verbrauch.Sum(Function(b) CLng(b.Gramm)) / 1000.0:N2} kg insgesamt")
        kzKosten.Setzen(verbrauch.Where(imMonat).Sum(Function(b) b.Kosten).ToString("C2"),
                        $"{verbrauch.Sum(Function(b) b.Kosten):C2} insgesamt")
        Dim zeitMonat = drucke.Where(imMonat).Sum(Function(b) If(b.Druckzeit, 0))
        Dim zeitGesamt = drucke.Sum(Function(b) CLng(If(b.Druckzeit, 0)))
        kzZeit.Setzen(If(zeitMonat > 0, DauerText(zeitMonat), "–"), $"{zeitGesamt / 3600.0:N1} h insgesamt")

        Dim summeG = liste.Where(Function(b) b.Art <> Buchung.ArtKorrektur).Sum(Function(b) CLng(b.Gramm))
        Dim summeK = liste.Where(Function(b) b.Art <> Buchung.ArtKorrektur).Sum(Function(b) b.Kosten)
        karte.FussLinks.Text = $"{liste.Count:N0} Einträge · {summeG:N0} g · {summeK:C2}"
    End Sub

    Private Function Ausgewaehlt() As Buchung
        Return grid.Ausgewaehlt(Of Buchung)()
    End Function

    Private Sub Grid_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs)
        If e.RowIndex < 0 OrElse e.CellStyle Is Nothing Then Return
        Dim b = grid.Eintrag(Of Buchung)(e.RowIndex)
        If b Is Nothing Then Return
        Select Case grid.Columns(e.ColumnIndex).Name
            Case "Druckzeit"
                e.Value = DauerText(b.Druckzeit)
                e.FormattingApplied = True
            Case "Gramm"
                If b.Gramm < 0 Then
                    e.Value = $"+{-b.Gramm:N0} g"
                    e.CellStyle.ForeColor = Theme.Gruen
                    e.FormattingApplied = True
                End If
            Case "Bezeichnung"
                If b.Bezeichnung.Length = 0 Then
                    e.Value = If(b.IstDruck, "(ohne Bezeichnung)", "")
                    e.CellStyle.ForeColor = Theme.Grau
                    e.FormattingApplied = True
                End If
        End Select
        If Not b.IstDruck AndAlso grid.Columns(e.ColumnIndex).Name <> "Gramm" Then e.CellStyle.ForeColor = Theme.TextLeise
    End Sub

    Private Sub Grid_CellPainting(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse e.Graphics Is Nothing OrElse e.CellStyle Is Nothing Then Return
        If grid.Columns(e.ColumnIndex).Name <> "Art" Then Return
        Dim b = grid.Eintrag(Of Buchung)(e.RowIndex)
        If b Is Nothing Then Return
        Dim farbe = If(b.IstDruck, Theme.Akzent, If(b.Art = Buchung.ArtWiegen, Theme.Tuerkis, Theme.Gelb))
        grid.BadgeZeichnen(e, b.Art, farbe, Hell(farbe, 0.12))
    End Sub

    ' ------------------------------------------------------------------ Aktionen

    Private Sub NeuErfassen()
        Dim b As New Buchung With {.Datum = Date.Now}
        ' Gefilterte Spule direkt vorschlagen
        Dim spule = TryCast(cboSpule.SelectedItem, String)
        Dim f = _filamente.FirstOrDefault(Function(x) x.Code = spule)
        If f IsNot Nothing Then b.FilamentId = f.Id
        DruckBearbeiten(b, "Druck erfassen")
    End Sub

    Private Sub Bearbeiten()
        Dim b = Ausgewaehlt()
        If b Is Nothing Then Return
        If Not b.IstDruck Then
            MessageBox.Show(Me, "Nachwiegen und Korrekturen können nicht bearbeitet werden – nur gelöscht.",
                            "Bearbeiten", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        DruckBearbeiten(b.Kopie(), "Druck bearbeiten")
    End Sub

    Private Sub DruckBearbeiten(b As Buchung, titel As String)
        Dim dateien As List(Of Datei)
        Try
            _filamente = _db.AlleLaden()
            dateien = _db.DateienLaden()
        Catch ex As Exception
            Fehler("Die Daten konnten nicht geladen werden.", ex)
            Return
        End Try
        If Not DruckDialogZeigen(Me, _db, b, _filamente, dateien, titel) Then Return
        DatenLaden(b.Id)
    End Sub

    ''' <summary>Zeigt den Druck-Dialog und speichert bei OK (auch von der Dateien-Seite aus genutzt).</summary>
    Friend Shared Function DruckDialogZeigen(besitzer As IWin32Window, db As IDatenquelle, b As Buchung,
                                             filamente As List(Of Filament), dateien As List(Of Datei), titel As String) As Boolean
        If Not filamente.Any(Function(f) f.Restgewicht > 0 OrElse f.Id = b.FilamentId) Then
            MessageBox.Show(besitzer, "Es gibt keine Spule mit Restbestand. Bitte zuerst ein Filament anlegen.",
                            titel, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return False
        End If
        Dim foto As Byte() = Nothing
        If Not String.IsNullOrEmpty(b.Foto) Then
            Try
                foto = db.FotoLaden(b.Foto)
            Catch ex As Exception
                ' ohne Foto weiter
            End Try
        End If
        Using dlg As New DruckDialog(b, filamente, dateien, titel, foto)
            If dlg.ShowDialog(besitzer) <> DialogResult.OK Then Return False
        End Using
        Try
            db.DruckSpeichern(b)
            Return True
        Catch ex As Exception
            MessageBox.Show(besitzer, "Der Druck konnte nicht gespeichert werden." & Environment.NewLine & Environment.NewLine & ex.Message,
                            "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End Try
    End Function

    Private Sub Loeschen()
        Dim b = Ausgewaehlt()
        If b Is Nothing Then Return
        Dim beschreibung = If(b.Bezeichnung.Length > 0, $"""{b.Bezeichnung}""", b.Art)
        Dim menge = If(b.Gramm >= 0, $"{b.Gramm:N0} g wieder auf {b.FilamentCode} gutschreiben", $"{-b.Gramm:N0} g wieder von {b.FilamentCode} abziehen")
        Dim antwort = MessageBox.Show(Me,
            $"{beschreibung} vom {b.Datum:d} löschen." & Environment.NewLine & Environment.NewLine &
            $"Ja = löschen und {menge}" & Environment.NewLine &
            "Nein = nur den Eintrag löschen, Restgewicht unverändert lassen",
            "Eintrag löschen", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
        If antwort = DialogResult.Cancel Then Return
        Try
            _db.BuchungLoeschen(b.Id, zurueckbuchenJa:=antwort = DialogResult.Yes)
        Catch ex As Exception
            Fehler("Der Eintrag konnte nicht gelöscht werden.", ex)
            Return
        End Try
        DatenLaden()
    End Sub

    Private Sub Fehler(text As String, ex As Exception)
        MessageBox.Show(Me, text & Environment.NewLine & Environment.NewLine & ex.Message,
                        "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
    End Sub

End Class
