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

    Private ReadOnly _db As IDatenquelle
    Private ReadOnly _ueberwachung As IDruckerDienst
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

    ' --- Ordner (nur zum Sortieren; links als Baum)
    Private Const ZiehFormat As String = "FilamentVerwaltung.DateiIds"
    Private ReadOnly baum As New TreeView With {.Dock = DockStyle.Fill, .BorderStyle = BorderStyle.None, .FullRowSelect = True,
                                                .HideSelection = False, .ShowLines = True, .ShowRootLines = True, .ShowPlusMinus = True, .AllowDrop = True,
                                                .LabelEdit = False}
    Private ordnerKarte As Karte
    Private _ordner As New List(Of String)
    ''' <summary>Gewählter Ordner; Nothing = alle Dateien, "" = oberste Ebene.</summary>
    Private _aktuellerOrdner As String
    Private _baumWirdAufgebaut As Boolean
    Private _ziehStart As Point?
    Private inhaltTabelle As TableLayoutPanel
    Private rechtsKarte As Karte
    ''' <summary>Während die Liste neu gefüllt wird: Auswahlwechsel nicht als Klick werten.</summary>
    Private _bindet As Boolean
    ''' <summary>Klick auf die schon gewählte Datei: beim Loslassen abwählen (außer es wird gezogen).</summary>
    Private _abwaehlen As Boolean
    ''' <summary>Dateitypen, die beim Hinzufügen eines ganzen Ordners übernommen werden.</summary>
    Private Shared ReadOnly OrdnerImportTypen As String() = {".stl", ".3mf", ".obj", ".step", ".stp", ".gcode", ".gco", ".bgcode", ".f3d", ".scad"}

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
    Private ReadOnly btnSlicen As New WebButton("Slicen …", Theme.SymWuerfel)
    Private ReadOnly btnSenden As New WebButton("An Drucker senden …", Theme.SymNetzDrucker)
    Private ReadOnly btnImSlicer As New WebButton("Im Slicer öffnen", Theme.SymOeffnen)
    Private _slicer As List(Of SlicerInstallation)
    Private _bambu As BambuStudio
    Private _bambuGesucht As Boolean
    ''' <summary>Dateitypen, die sich slicen lassen.</summary>
    Private Shared ReadOnly SliceTypen As String() = {"STL", "3MF", "OBJ", "STEP", "STP"}
    Private ReadOnly btnImOrdner As New WebButton("Im Ordner", Theme.SymOrdnerOffen)
    Private ReadOnly btnLoeschen As New WebButton("Löschen", Theme.SymLoeschen, KnopfStil.Gefahr)
    Private details As Panel

    Public Sub New(db As IDatenquelle, ueberwachung As IDruckerDienst)
        _db = db
        _ueberwachung = ueberwachung
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

        Dim inhalt As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1, .Margin = New Padding(0)}
        inhaltTabelle = inhalt
        inhalt.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 230))
        inhalt.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        inhalt.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 0))   ' Vorschau erst, wenn eine Datei angeklickt ist

        ' --- Ordner
        ordnerKarte = OrdnerKarteAufbauen()
        inhalt.Controls.Add(ordnerKarte, 0, 0)

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
        inhalt.Controls.Add(karte, 1, 0)

        ' --- Vorschau und Details
        Dim rechts As New Karte With {.Dock = DockStyle.Fill, .Padding = New Padding(12), .Visible = False}
        rechtsKarte = rechts
        Dim spalte As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .Margin = New Padding(0)}
        spalte.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        spalte.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        spalte.Controls.Add(vorschau, 0, 0)
        details = DetailsAufbauen()
        spalte.Controls.Add(details, 0, 1)
        rechts.Controls.Add(spalte)
        inhalt.Controls.Add(rechts, 2, 0)

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
        For Each b In {btnBuchen, btnSlicen, btnSenden, btnImSlicer, btnOeffnen, btnImOrdner, btnLoeschen}
            b.Margin = New Padding(0, 0, 8, 6)
            knoepfe.Controls.Add(b)
        Next
        AddHandler btnBuchen.Click, Sub(s, e) AlsDruckBuchen()
        AddHandler btnOeffnen.Click, Sub(s, e) Oeffnen()
        AddHandler btnSlicen.Click, Sub(s, e) Slicen()
        AddHandler btnSenden.Click, Sub(s, e) Senden()
        tipps.SetToolTip(btnSenden, "Datei an einen angebundenen Drucker senden und den Druck starten (mit Bestätigung)")
        AddHandler btnImSlicer.Click, Sub(s, e) ImSlicerOeffnen(btnImSlicer)
        tipps.SetToolTip(btnSlicen, "Mit Bambu Studio slicen – das Ergebnis (mit Gewicht und Druckzeit) landet hier in Dateien")
        tipps.SetToolTip(btnImSlicer, "Datei in einem installierten Slicer öffnen (Bambu Studio, OrcaSlicer, PrusaSlicer, Cura)")
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
        menue.Items.Add("Slicen …", Nothing, Sub(s, e) Slicen())
        menue.Items.Add("An Drucker senden …", Nothing, Sub(s, e) Senden())
        menue.Items.Add("Im Slicer öffnen", Nothing, Sub(s, e) ImSlicerOeffnen(Nothing))
        menue.Items.Add("Öffnen", Nothing, Sub(s, e) Oeffnen())
        menue.Items.Add("Im Ordner anzeigen", Nothing, Sub(s, e) OrdnerOeffnen(_angezeigt))
        Dim mnuVerschieben As New ToolStripMenuItem("In Ordner verschieben")
        menue.Items.Add(mnuVerschieben)
        menue.Items.Add(New ToolStripSeparator())
        AddHandler menue.Opening, Sub(s, e)
                                      mnuVerschieben.Visible = OrdnerVerfuegbar()
                                      mnuVerschieben.DropDownItems.Clear()
                                      mnuVerschieben.DropDownItems.Add("(oberste Ebene)", Nothing, Sub(x, y) Verschieben(AusgewaehlteIds(), ""))
                                      For Each o In _ordner
                                          Dim ziel = o
                                          mnuVerschieben.DropDownItems.Add(o.Replace("/", " › "), Nothing, Sub(x, y) Verschieben(AusgewaehlteIds(), ziel))
                                      Next
                                      mnuVerschieben.DropDownItems.Add(New ToolStripSeparator())
                                      mnuVerschieben.DropDownItems.Add("Neuer Ordner …", Nothing, Sub(x, y)
                                                                                                Dim neu = OrdnerNameAbfragen("Neuer Ordner", If(_aktuellerOrdner, ""), "")
                                                                                                If neu IsNot Nothing Then Verschieben(AusgewaehlteIds(), neu)
                                                                                            End Sub)
                                  End Sub
        menue.Items.Add("Löschen", Nothing, Sub(s, e) Loeschen()).ForeColor = Theme.Rot
        AddHandler menue.Opening, Sub(s, e) e.Cancel = Ausgewaehlt() Is Nothing
        grid.ContextMenuStrip = menue

        AddHandler grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 Then Oeffnen()
                                          End Sub
        AddHandler grid.SelectionChanged, Sub(s, e)
                                              If Not _bindet Then DetailsAnzeigen(Ausgewaehlt())
                                          End Sub
        ' Auch wenn die Tabelle die Zeile schon intern markiert hatte (dann kommt kein SelectionChanged)
        AddHandler grid.CurrentCellChanged, Sub(s, e)
                                                If Not _bindet AndAlso grid.CurrentRow IsNot Nothing Then DetailsAnzeigen(Ausgewaehlt())
                                            End Sub
        ' Nochmal auf die gewählte Datei klicken (oder ins Leere / Esc): Auswahl aufheben, Vorschau schließen
        AddHandler grid.CellMouseDown, Sub(s, e)
                                           _abwaehlen = e.Button = MouseButtons.Left AndAlso e.RowIndex >= 0 AndAlso ModifierKeys = Keys.None AndAlso
                                                        _angezeigt IsNot Nothing AndAlso If(grid.Eintrag(Of Datei)(e.RowIndex)?.Id, -1) = _angezeigt.Id
                                       End Sub
        AddHandler grid.CellMouseUp, Sub(s, e)
                                         If _abwaehlen Then AuswahlAufheben()
                                         _abwaehlen = False
                                     End Sub
        AddHandler grid.MouseDown, Sub(s, e)
                                       If e.Button = MouseButtons.Left AndAlso grid.HitTest(e.X, e.Y).Type = DataGridViewHitTestType.None Then AuswahlAufheben()
                                   End Sub
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
            Case Keys.Escape
                If _angezeigt IsNot Nothing Then
                    AuswahlAufheben()
                    Return True
                End If
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
        If e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(ZiehFormat) Then Return   ' Dateien in einen Ordner ziehen: regelt der Baum
        e.Effect = If(e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(DataFormats.FileDrop), DragDropEffects.Copy, DragDropEffects.None)
    End Sub

    Private Sub Ziel_DragDrop(sender As Object, e As DragEventArgs)
        If e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(ZiehFormat) Then Return
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
            _ordner = If(OrdnerVerfuegbar(), _db.OrdnerLaden(), New List(Of String))
        Catch ex As Exception
            Fehler("Die Dateien konnten nicht geladen werden.", ex)
            Return
        End Try
        ordnerKarte.Visible = OrdnerVerfuegbar()
        BaumAufbauen()
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
        ' Ordner: nur die Dateien direkt darin; beim Suchen auch die Unterordner
        If _aktuellerOrdner IsNot Nothing Then
            Dim ordner = _aktuellerOrdner
            treffer = If(begriff.Length > 0,
                         treffer.Where(Function(d) DateiOrdner.IstInnerhalb(d.Ordner, ordner)),
                         treffer.Where(Function(d) String.Equals(DateiOrdner.Normalisieren(d.Ordner), ordner, StringComparison.OrdinalIgnoreCase)))
        End If

        Dim liste = grid.Sortieren(treffer)
        ' Nichts automatisch auswählen: Die Vorschau erscheint erst, wenn eine Datei angeklickt wird
        _bindet = True
        quelle.DataSource = liste
        grid.ClearSelection()
        grid.CurrentCell = Nothing
        If merkeId <> 0 AndAlso liste.Any(Function(d) d.Id = merkeId) Then grid.Auswaehlen(Of Datei)(Function(d) d.Id = merkeId)
        _bindet = False
        DetailsAnzeigen(If(merkeId <> 0, Ausgewaehlt(), Nothing))
        karte.FussLinks.Text = $"{liste.Count:N0} von {_alle.Count:N0} Dateien · {GroesseText(liste.Sum(Function(d) d.Groesse))}"
    End Sub

    ''' <summary>Keine Datei gewählt: Vorschau-Spalte schließen, die Liste nutzt die ganze Breite.</summary>
    Private Sub AuswahlAufheben()
        _bindet = True
        grid.ClearSelection()
        grid.CurrentCell = Nothing
        _bindet = False
        DetailsAnzeigen(Nothing)
    End Sub

    Private Function Ausgewaehlt() As Datei
        Return grid.Ausgewaehlt(Of Datei)()
    End Function

    Private Sub DetailsAnzeigen(d As Datei)
        If d IsNot Nothing AndAlso _angezeigt IsNot Nothing AndAlso d.Id = _angezeigt.Id AndAlso infos.Controls.Count > 0 Then Return
        NotizSpeichern()
        _angezeigt = d
        ' Vorschau-Spalte nur mit gewählter Datei
        If rechtsKarte IsNot Nothing AndAlso rechtsKarte.Visible <> (d IsNot Nothing) Then
            inhaltTabelle.SuspendLayout()
            inhaltTabelle.ColumnStyles(2).Width = If(d IsNot Nothing, Px(Me, 420), 0)
            rechtsKarte.Visible = d IsNot Nothing
            inhaltTabelle.ResumeLayout()
        End If
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
            If OrdnerVerfuegbar() Then InfoZeile("Ordner", If(d.Ordner.Length > 0, d.Ordner.Replace("/", " › "), "oberste Ebene"))
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
        Dim sliceBar = d IsNot Nothing AndAlso SliceTypen.Contains(d.Typ) AndAlso Not d.HatDruckdaten
        btnSlicen.Visible = sliceBar
        ' Der sinnvolle nächste Schritt wird hervorgehoben: erst slicen, dann buchen
        btnSlicen.Stil = If(sliceBar, KnopfStil.Primaer, KnopfStil.Sekundaer)
        btnBuchen.Stil = If(sliceBar, KnopfStil.Sekundaer, KnopfStil.Primaer)
        btnSlicen.Invalidate()
        btnBuchen.Invalidate()
        btnImSlicer.Visible = d IsNot Nothing AndAlso Not Datei.GcodeTypen.Contains(d.Typ)
        ' Senden: geslicte 3MF (Bambu) oder G-Code (Klipper, OctoPrint, PrusaLink)
        btnSenden.Visible = d IsNot Nothing AndAlso (d.HatDruckdaten OrElse Datei.GcodeTypen.Contains(d.Typ) OrElse d.Typ = "BGCODE")
        ' Feste Reihenfolge erzwingen – erst nach dem Ein-/Ausblenden, weil ein erstmals sichtbarer Knopf sonst ans Ende rutscht
        Dim leiste = btnBuchen.Parent
        If leiste IsNot Nothing Then
            Dim reihenfolge = {btnBuchen, btnSlicen, btnSenden, btnImSlicer, btnOeffnen, btnImOrdner, btnLoeschen}
            For i = 0 To reihenfolge.Length - 1
                leiste.Controls.SetChildIndex(reihenfolge(i), i)
            Next
            leiste.PerformLayout()
        End If
        VorschauLaden(d)
    End Sub

    Private _vorschauAbbruch As Threading.CancellationTokenSource

    Private Async Sub VorschauLaden(d As Datei)
        _ladeNummer += 1
        Dim nummer = _ladeNummer
        _vorschauAbbruch?.Cancel()   ' Download für die vorige Auswahl abbrechen
        vorschau.Netz = Nothing
        If d Is Nothing Then
            vorschau.Hinweis = If(_alle.Count = 0, "Hier erscheint die 3D-Vorschau von STL-, 3MF- und OBJ-Dateien.",
                                  "Datei auswählen, um die Vorschau zu sehen.")
            Return
        End If
        ' Dateien ohne Vorschau (G-Code, STEP …) gar nicht erst vom Server laden
        If Not Netz.KannLaden(d.Name) Then
            vorschau.Hinweis = If(Datei.GcodeTypen.Contains(d.Typ), "G-Code hat keine 3D-Vorschau – die Druckdaten stehen unten.",
                                  $"Für {d.Typ}-Dateien gibt es keine Vorschau. Mit ""Öffnen"" im passenden Programm ansehen.")
            Return
        End If
        Dim pfad As String
        If _db.DateiVorhanden(d) Then
            pfad = _db.DateiPfad(d)
        Else
            ' Vom Server im Hintergrund laden; eine neue Auswahl bricht das Laden ab
            _vorschauAbbruch = New Threading.CancellationTokenSource()
            Dim abbruch = _vorschauAbbruch.Token
            vorschau.Hinweis = "Lade Datei vom Server …"
            Dim fortschritt As New Progress(Of Double)(Sub(w)
                                                           If nummer = _ladeNummer Then vorschau.Hinweis = $"Lade Datei vom Server … {w:P0}"
                                                       End Sub)
            Try
                pfad = Await _db.DateiLadenAsync(d, fortschritt, abbruch)
            Catch ex As OperationCanceledException
                Return
            Catch ex As Exception
                If nummer = _ladeNummer Then vorschau.Hinweis = "Die Datei konnte nicht vom Server geladen werden: " & ex.Message
                Return
            End Try
            If nummer <> _ladeNummer Then Return
        End If
        If Not File.Exists(pfad) Then
            vorschau.Hinweis = "Die Datei wurde im Dateien-Ordner nicht gefunden."
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

    ' ------------------------------------------------------------------ Ordner

    Private Function OrdnerVerfuegbar() As Boolean
        Return ServerFunktionen.Verfuegbar(_db, ServerFunktionen.Ordner)
    End Function

    Private Function OrdnerKarteAufbauen() As Karte
        Dim k As New Karte With {.Dock = DockStyle.Fill, .Padding = New Padding(8, 10, 8, 8), .Margin = New Padding(0, 0, 16, 0)}
        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 2, .Margin = New Padding(0)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        t.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        t.Controls.Add(New Label With {.Text = "Ordner", .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark, .AutoSize = True,
                                       .Anchor = AnchorStyles.Left, .Margin = New Padding(6, 0, 0, 6)}, 0, 0)
        Dim btnNeu As New WebButton("", Theme.SymNeu) With {.Margin = New Padding(0, 0, 0, 6)}
        AddHandler btnNeu.Click, Sub(s, e) NeuerOrdner(If(_aktuellerOrdner, ""))
        tipps.SetToolTip(btnNeu, "Neuer Ordner (im gewählten Ordner)")
        t.Controls.Add(btnNeu, 1, 0)

        baum.BackColor = Theme.KartenFlaeche
        baum.ForeColor = Theme.TextNormal
        baum.Font = Theme.Schrift
        baum.ItemHeight = Px(Me, 28)
        baum.Indent = Px(Me, 14)
        baum.DrawMode = TreeViewDrawMode.OwnerDrawText
        ' Im Dunkel-Modus übernimmt die Baumansicht BackColor nicht von selbst – Farben direkt setzen, Pfeile im Explorer-Stil
        AddHandler baum.HandleCreated, Sub(s, e) BaumFarbenSetzen()
        AddHandler baum.DrawNode, AddressOf Baum_DrawNode
        AddHandler baum.AfterSelect, Sub(s, e)
                                         If _baumWirdAufgebaut Then Return
                                         _aktuellerOrdner = TryCast(e.Node?.Tag, String)
                                         _angezeigt = Nothing
                                         AnsichtAktualisieren()
                                     End Sub
        AddHandler baum.NodeMouseClick, Sub(s, e)
                                            If e.Button = MouseButtons.Right Then baum.SelectedNode = e.Node
                                        End Sub
        ' Dateien aus der Liste auf einen Ordner ziehen
        AddHandler baum.DragOver, AddressOf Baum_DragOver
        AddHandler baum.DragDrop, AddressOf Baum_DragDrop
        AddHandler grid.MouseDown, Sub(s, e) _ziehStart = If(e.Button = MouseButtons.Left AndAlso grid.HitTest(e.X, e.Y).RowIndex >= 0, e.Location, CType(Nothing, Point?))
        AddHandler grid.MouseMove, AddressOf Grid_MouseMove

        Dim menue As New ContextMenuStrip With {.Font = Theme.Schrift}
        Dim neu = menue.Items.Add("Neuer Ordner …", Nothing, Sub(s, e) NeuerOrdner(If(_aktuellerOrdner, "")))
        Dim umbenennen = menue.Items.Add("Umbenennen …", Nothing, Sub(s, e) OrdnerUmbenennen())
        Dim loeschen = menue.Items.Add("Ordner auflösen …", Nothing, Sub(s, e) OrdnerAufloesen())
        loeschen.ForeColor = Theme.Rot
        AddHandler menue.Opening, Sub(s, e)
                                      Dim echt = Not String.IsNullOrEmpty(_aktuellerOrdner)
                                      neu.Text = If(echt, "Neuer Unterordner …", "Neuer Ordner …")
                                      umbenennen.Visible = echt
                                      loeschen.Visible = echt
                                  End Sub
        baum.ContextMenuStrip = menue
        t.Controls.Add(baum, 0, 1)
        t.SetColumnSpan(baum, 2)
        k.Controls.Add(t)
        Return k
    End Function

    <Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    <Runtime.InteropServices.DllImport("uxtheme.dll", CharSet:=Runtime.InteropServices.CharSet.Unicode)>
    Private Shared Function SetWindowTheme(hWnd As IntPtr, appName As String, idList As String) As Integer
    End Function

    Private Sub BaumFarbenSetzen()
        Const TVM_SETBKCOLOR = &H1100 + 29, TVM_SETTEXTCOLOR = &H1100 + 30, TVM_SETLINECOLOR = &H1100 + 40
        Try
            ' Klassische Darstellung: nur so zeichnet Windows die Verbindungslinien (das Explorer-Design blendet sie aus)
            SetWindowTheme(baum.Handle, "", "")
        Catch ex As Exception
            ' Ohne Design-Unterstützung bleiben die Standard-Pfeile
        End Try
        SendMessage(baum.Handle, TVM_SETBKCOLOR, IntPtr.Zero, New IntPtr(ColorTranslator.ToWin32(Theme.KartenFlaeche)))
        SendMessage(baum.Handle, TVM_SETTEXTCOLOR, IntPtr.Zero, New IntPtr(ColorTranslator.ToWin32(Theme.TextNormal)))
        SendMessage(baum.Handle, TVM_SETLINECOLOR, IntPtr.Zero, New IntPtr(ColorTranslator.ToWin32(Theme.TextLeise)))
    End Sub

    ''' <summary>"Alle Dateien", "Ohne Ordner" und die Ordner als Baum; aufgeklappte Ordner und Auswahl bleiben erhalten.</summary>
    Private Sub BaumAufbauen()
        Dim offen = AlleKnoten(baum.Nodes).Where(Function(n) n.IsExpanded).Select(Function(n) TryCast(n.Tag, String)).ToHashSet()
        _baumWirdAufgebaut = True
        baum.BeginUpdate()
        baum.Nodes.Clear()
        Dim alle = baum.Nodes.Add("Alle Dateien")
        alle.Tag = Nothing
        Dim ohne = baum.Nodes.Add("Ohne Ordner")
        ohne.Tag = ""
        Dim knoten As New Dictionary(Of String, TreeNode)(StringComparer.OrdinalIgnoreCase)
        For Each pfad In _ordner
            Dim eltern = DateiOrdner.Eltern(pfad)
            Dim liste = If(eltern.Length > 0 AndAlso knoten.ContainsKey(eltern), knoten(eltern).Nodes, baum.Nodes)
            Dim n = liste.Add(DateiOrdner.Name(pfad))
            n.Tag = pfad
            knoten(pfad) = n
        Next
        For Each n In AlleKnoten(baum.Nodes)
            If offen.Contains(TryCast(n.Tag, String)) Then n.Expand()
        Next
        ' Gewählten Ordner wieder auswählen (oder "Alle Dateien", wenn es ihn nicht mehr gibt)
        Dim auswahl = If(_aktuellerOrdner Is Nothing, alle, If(_aktuellerOrdner = "", ohne,
                         If(knoten.ContainsKey(_aktuellerOrdner), knoten(_aktuellerOrdner), Nothing)))
        If auswahl Is Nothing Then
            auswahl = alle
            _aktuellerOrdner = Nothing
        End If
        baum.SelectedNode = auswahl
        auswahl.EnsureVisible()
        baum.EndUpdate()
        _baumWirdAufgebaut = False
    End Sub

    Private Shared Iterator Function AlleKnoten(liste As TreeNodeCollection) As IEnumerable(Of TreeNode)
        For Each n As TreeNode In liste
            Yield n
            For Each kind In AlleKnoten(n.Nodes)
                Yield kind
            Next
        Next
    End Function

    ''' <summary>Anzahl der Dateien direkt im Ordner (bzw. alle / ohne Ordner).</summary>
    Private Function Anzahl(pfad As String) As Integer
        If pfad Is Nothing Then Return _alle.Count
        Return _alle.Where(Function(d) String.Equals(DateiOrdner.Normalisieren(d.Ordner), pfad, StringComparison.OrdinalIgnoreCase)).Count()
    End Function

    Private Sub Baum_DrawNode(sender As Object, e As DrawTreeNodeEventArgs)
        If e.Node Is Nothing OrElse e.Bounds.IsEmpty Then Return
        Dim r As New Rectangle(e.Bounds.X, e.Bounds.Y, baum.ClientSize.Width - e.Bounds.X, e.Bounds.Height)
        Dim gewaehlt = (e.State And TreeNodeStates.Selected) <> 0
        Dim ziel = e.Node Is _ablegeZiel
        Using b As New SolidBrush(If(ziel, Hell(Theme.Akzent, 0.25), If(gewaehlt, Theme.Auswahl, Theme.KartenFlaeche)))
            e.Graphics.FillRectangle(b, r)
        End Using
        Dim pfad = TryCast(e.Node.Tag, String)
        Dim symbol = If(pfad Is Nothing, Theme.SymDatei, Theme.SymOrdner)
        Dim x = r.X + Px(baum, 2)
        TextRenderer.DrawText(e.Graphics, symbol, Theme.SymbolSchrift, New Rectangle(x, r.Y, Px(baum, 20), r.Height),
                              If(pfad Is Nothing OrElse pfad = "", Theme.TextLeise, Theme.Gelb),
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
        x += Px(baum, 24)
        Dim zahl = Anzahl(pfad).ToString()
        Dim zahlBreite = TextRenderer.MeasureText(zahl, Theme.SchriftKlein).Width + Px(baum, 8)
        TextRenderer.DrawText(e.Graphics, e.Node.Text, If(gewaehlt, Theme.SchriftFett, Theme.Schrift),
                              New Rectangle(x, r.Y, Math.Max(0, r.Right - x - zahlBreite), r.Height),
                              If(gewaehlt, Theme.TextStark, Theme.TextNormal),
                              TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPadding)
        TextRenderer.DrawText(e.Graphics, zahl, Theme.SchriftKlein, New Rectangle(r.Right - zahlBreite, r.Y, zahlBreite - Px(baum, 4), r.Height),
                              Theme.TextLeise, TextFormatFlags.Right Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
    End Sub

    ''' <summary>Fragt einen Ordnernamen ab und gibt den vollständigen Pfad zurück (Nothing = abgebrochen).</summary>
    Private Function OrdnerNameAbfragen(titel As String, eltern As String, vorgabe As String) As String
        Dim wo = If(String.IsNullOrEmpty(eltern), "auf oberster Ebene", $"in ""{eltern.Replace("/", " › ")}""")
        Dim name = Eingabe.Abfragen(Me, titel, $"Name des Ordners ({wo}). Mit ""/"" lassen sich auch Unterordner anlegen, z. B. ""Halter/Wand"".", vorgabe)
        If name Is Nothing Then Return Nothing
        Dim pfad = DateiOrdner.Normalisieren(eltern & "/" & name)
        If DateiOrdner.Normalisieren(name).Length = 0 Then Return Nothing
        Return pfad
    End Function

    Private Sub NeuerOrdner(eltern As String)
        Dim pfad = OrdnerNameAbfragen("Neuer Ordner", eltern, "")
        If pfad Is Nothing Then Return
        Try
            _db.OrdnerAnlegen(pfad)
            _aktuellerOrdner = pfad
            DatenLaden()
        Catch ex As Exception
            Fehler("Der Ordner konnte nicht angelegt werden.", ex)
        End Try
    End Sub

    Private Sub OrdnerUmbenennen()
        Dim alt = _aktuellerOrdner
        If String.IsNullOrEmpty(alt) Then Return
        Dim neu = OrdnerNameAbfragen("Ordner umbenennen", DateiOrdner.Eltern(alt), DateiOrdner.Name(alt))
        If neu Is Nothing OrElse String.Equals(neu, alt, StringComparison.Ordinal) Then Return
        Try
            _db.OrdnerUmbenennen(alt, neu)
            _aktuellerOrdner = neu
            DatenLaden()
        Catch ex As Exception
            Fehler("Der Ordner konnte nicht umbenannt werden.", ex)
        End Try
    End Sub

    ''' <summary>Ordner entfernen – Dateien und Unterordner wandern eine Ebene nach oben, gelöscht wird nichts.</summary>
    Private Sub OrdnerAufloesen()
        Dim pfad = _aktuellerOrdner
        If String.IsNullOrEmpty(pfad) Then Return
        Dim eltern = DateiOrdner.Eltern(pfad)
        Dim innen = _alle.Where(Function(d) DateiOrdner.IstInnerhalb(d.Ordner, pfad)).Count()
        If MessageBox.Show(Me, $"Ordner ""{DateiOrdner.Name(pfad)}"" auflösen?" & Environment.NewLine & Environment.NewLine &
                               If(innen > 0, $"Die {innen} Datei(en) und Unterordner darin wandern nach ""{If(eltern.Length > 0, eltern.Replace("/", " › "), "Ohne Ordner")}"" – gelöscht wird nichts.",
                                  "Der Ordner ist leer."),
                           "Ordner auflösen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        Try
            _db.OrdnerLoeschen(pfad)
            _aktuellerOrdner = If(eltern.Length > 0, eltern, Nothing)
            DatenLaden()
        Catch ex As Exception
            Fehler("Der Ordner konnte nicht aufgelöst werden.", ex)
        End Try
    End Sub

    Private Function AusgewaehlteIds() As List(Of Long)
        Dim ids = grid.SelectedRows.Cast(Of DataGridViewRow)().Select(Function(r) TryCast(r.DataBoundItem, Datei)).
                                    Where(Function(d) d IsNot Nothing).Select(Function(d) d.Id).ToList()
        If ids.Count = 0 AndAlso Ausgewaehlt() IsNot Nothing Then ids.Add(Ausgewaehlt().Id)
        Return ids
    End Function

    Private Sub Verschieben(ids As List(Of Long), ziel As String)
        If ids.Count = 0 Then Return
        Try
            For Each id In ids
                _db.DateiOrdnerSetzen(id, ziel)
            Next
            Dim erste = ids(0)
            DatenLaden(If(_aktuellerOrdner Is Nothing, erste, 0))
        Catch ex As Exception
            Fehler("Die Datei konnte nicht verschoben werden.", ex)
        End Try
    End Sub

    Private _ablegeZiel As TreeNode

    Private Sub Grid_MouseMove(sender As Object, e As MouseEventArgs)
        If Not _ziehStart.HasValue OrElse e.Button <> MouseButtons.Left OrElse Not OrdnerVerfuegbar() Then Return
        Dim groesse = SystemInformation.DragSize
        If Math.Abs(e.X - _ziehStart.Value.X) < groesse.Width AndAlso Math.Abs(e.Y - _ziehStart.Value.Y) < groesse.Height Then Return
        _ziehStart = Nothing
        _abwaehlen = False   ' gezogen, nicht geklickt
        Dim ids = AusgewaehlteIds()
        If ids.Count = 0 Then Return
        Dim daten As New DataObject()
        daten.SetData(ZiehFormat, String.Join(",", ids))
        grid.DoDragDrop(daten, DragDropEffects.Move)
        AblegeZielSetzen(Nothing)
    End Sub

    Private Sub AblegeZielSetzen(n As TreeNode)
        If n Is _ablegeZiel Then Return
        _ablegeZiel = n
        baum.Invalidate()
    End Sub

    Private Sub Baum_DragOver(sender As Object, e As DragEventArgs)
        If e.Data Is Nothing OrElse Not e.Data.GetDataPresent(ZiehFormat) Then Return
        Dim n = baum.GetNodeAt(baum.PointToClient(New Point(e.X, e.Y)))
        AblegeZielSetzen(n)
        e.Effect = If(n IsNot Nothing, DragDropEffects.Move, DragDropEffects.None)
    End Sub

    Private Sub Baum_DragDrop(sender As Object, e As DragEventArgs)
        If e.Data Is Nothing OrElse Not e.Data.GetDataPresent(ZiehFormat) Then Return
        Dim n = baum.GetNodeAt(baum.PointToClient(New Point(e.X, e.Y)))
        AblegeZielSetzen(Nothing)
        If n Is Nothing Then Return
        Dim ids = CStr(e.Data.GetData(ZiehFormat)).Split(","c).Select(Function(x) Long.Parse(x)).ToList()
        ' "Alle Dateien" und "Ohne Ordner": auf die oberste Ebene
        Verschieben(ids, If(TryCast(n.Tag, String), ""))
    End Sub

    ' ------------------------------------------------------------------ Aktionen

    Private Sub DateienWaehlen()
        Using dlg As New OpenFileDialog With {.Filter = Datei.DialogFilter, .Multiselect = True, .Title = "Dateien hinzufügen"}
            If dlg.ShowDialog(Me) = DialogResult.OK Then Importieren(dlg.FileNames)
        End Using
    End Sub

    ''' <summary>
    ''' Dateien hinzufügen – in den gerade geöffneten Ordner. Ein ganzer Windows-Ordner wird mit seiner Struktur
    ''' übernommen (Unterordner werden zu Ordnern hier), sofern es die Ordner gibt.
    ''' </summary>
    Private Sub Importieren(pfade As IEnumerable(Of String))
        Dim fehlerListe As New List(Of String)
        Dim letzte As Datei = Nothing
        Dim basis = If(OrdnerVerfuegbar(), If(_aktuellerOrdner, ""), "")
        ' (Quelle, Ziel-Ordner) – Windows-Ordner werden aufgelöst
        Dim liste As New List(Of (Pfad As String, Ordner As String))
        For Each pfad In pfade
            If Directory.Exists(pfad) Then
                If Not OrdnerVerfuegbar() Then
                    fehlerListe.Add($"{Path.GetFileName(pfad)}: Ordner werden von diesem Server noch nicht unterstützt")
                    Continue For
                End If
                Dim wurzel = Path.GetFullPath(pfad).TrimEnd(Path.DirectorySeparatorChar)
                Dim name = Path.GetFileName(wurzel)
                For Each datei In Directory.EnumerateFiles(wurzel, "*", SearchOption.AllDirectories).
                                            Where(Function(f) OrdnerImportTypen.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(Function(f) f)
                    Dim relativ = Path.GetRelativePath(wurzel, Path.GetDirectoryName(datei))
                    liste.Add((datei, DateiOrdner.Normalisieren(basis & "/" & name & "/" & If(relativ = ".", "", relativ))))
                Next
            Else
                liste.Add((pfad, basis))
            End If
        Next
        Cursor = Cursors.WaitCursor
        Try
            For Each eintrag In liste
                Try
                    letzte = _db.DateiImportieren(eintrag.Pfad)
                    If eintrag.Ordner.Length > 0 Then
                        _db.DateiOrdnerSetzen(letzte.Id, eintrag.Ordner)
                        letzte.Ordner = eintrag.Ordner
                    End If
                Catch ex As Exception
                    fehlerListe.Add($"{Path.GetFileName(eintrag.Pfad)}: {ex.Message}")
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

    ''' <summary>Bambu Studio einmal suchen (die Profile werden erst im Slicer-Dialog gelesen).</summary>
    Private Function BambuFinden() As BambuStudio
        If Not _bambuGesucht Then
            _bambu = BambuStudio.Finden()
            _bambuGesucht = True
        End If
        Return _bambu
    End Function

    Private Sub Slicen()
        Dim d = Ausgewaehlt()
        If d Is Nothing Then Return
        Dim bambu = BambuFinden()
        If bambu Is Nothing Then
            MessageBox.Show(Me, "Bambu Studio wurde nicht gefunden. Slicen aus der App heraus funktioniert mit einer installierten Bambu Studio " &
                            "(Standardordner C:\Program Files\Bambu Studio). Andere Slicer kannst du über ""Im Slicer öffnen"" verwenden.",
                            "Slicen", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim spulen As List(Of Filament)
        Try
            spulen = _db.AlleLaden()
        Catch ex As Exception
            Fehler("Die Spulen konnten nicht geladen werden.", ex)
            Return
        End Try
        Dim pfad = LadeDialog.DateiHolen(Me, _db, d)
        If pfad Is Nothing Then Return
        Using dlg As New SlicerDialog(_db, bambu, d, pfad, spulen)
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.Ergebnis IsNot Nothing Then
                _angezeigt = Nothing
                DatenLaden(dlg.Ergebnis.Id)
            End If
        End Using
    End Sub

    Private Sub Senden()
        Dim d = Ausgewaehlt()
        If d Is Nothing Then Return
        Dim pfad = LadeDialog.DateiHolen(Me, _db, d)
        If pfad Is Nothing Then Return
        Using dlg As New SendenDialog(_db, _ueberwachung, d, pfad)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                MessageBox.Show(Me, "Der Auftrag wurde an den Drucker gesendet. Den Fortschritt siehst du auf der Seite ""Drucker""; " &
                                "nach dem Druck wird der Verbrauch mit dieser Datei zum Buchen vorgeschlagen.", "An Drucker senden",
                                MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If
        End Using
    End Sub

    ''' <summary>Datei in einem installierten Slicer öffnen; bei mehreren Slicern Auswahlmenü.</summary>
    Private Sub ImSlicerOeffnen(knopf As Control)
        Dim d = Ausgewaehlt()
        If d Is Nothing Then Return
        If _slicer Is Nothing Then _slicer = SlicerSuche.Installierte()
        If _slicer.Count = 0 Then
            MessageBox.Show(Me, "Es wurde kein Slicer gefunden (Bambu Studio, OrcaSlicer, PrusaSlicer oder Cura).", "Im Slicer öffnen",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim starten = Sub(s As SlicerInstallation)
                          Dim pfad = LadeDialog.DateiHolen(Me, _db, d)
                          If pfad Is Nothing Then Return
                          Try
                              SlicerSuche.Oeffnen(s, pfad)
                          Catch ex As Exception
                              Fehler($"{s.Name} konnte nicht gestartet werden.", ex)
                          End Try
                      End Sub
        If _slicer.Count = 1 Then
            starten(_slicer(0))
            Return
        End If
        Dim menue As New ContextMenuStrip With {.Font = Theme.Schrift, .ShowImageMargin = False}
        For Each s In _slicer
            Dim slicer = s
            menue.Items.Add(slicer.Name, Nothing, Sub(x, y) starten(slicer))
        Next
        AddHandler menue.Closed, Sub(x, y) BeginInvoke(Sub() menue.Dispose())
        If knopf IsNot Nothing Then menue.Show(knopf, New Point(0, knopf.Height)) Else menue.Show(Cursor.Position)
    End Sub

    Private Sub Oeffnen()
        Dim d = Ausgewaehlt()
        If d Is Nothing Then Return
        Dim pfad = LadeDialog.DateiHolen(Me, _db, d)
        If pfad Is Nothing Then Return
        Try
            Process.Start(New ProcessStartInfo(pfad) With {.UseShellExecute = True})
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
                Dim pfad = LadeDialog.DateiHolen(Me, _db, d)
                If pfad Is Nothing Then Return
                Process.Start("explorer.exe", "/select,""" & pfad & """")
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
