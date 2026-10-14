Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' Seite "Filament": Kennzahlen, Suche/Filter und Tabelle aller Spulen mit Bearbeitung.
''' </summary>
Public Class FilamentSeite
    Inherits UserControl
    Implements ISeite

    Private Const AlleMaterialien As String = "Alle Materialien"
    Private Const AlleLagerorte As String = "Alle Lagerorte"
    Private Const EinstellungSpalten As String = "Filament.SichtbareSpalten"

    ''' <summary>Spalten, die ohne gespeicherte Auswahl ausgeblendet sind.</summary>
    Private Shared ReadOnly StandardAusgeblendet As String() = {"Spulengewicht", "Temperaturen", "Trocknung"}

    ''' <summary>Der Verlauf (Drucke/Buchungen) einer Spule soll angezeigt werden.</summary>
    Public Event VerlaufAnzeigen(filamentId As Long)

    Private ReadOnly _db As Datenbank
    Private _alle As New List(Of Filament)
    Private _filterWirdAktualisiert As Boolean

    Private ReadOnly grid As New WebTabelle()
    Private ReadOnly quelle As New BindingSource()
    Private ReadOnly suche As New SuchFeld()
    Private ReadOnly cboMaterial As New ComboBox()
    Private ReadOnly cboLagerort As New ComboBox()
    Private ReadOnly chkLeere As New CheckBox()

    Private ReadOnly kzSpulen As New KennzahlKarte("Spulen", Theme.SymBox, Theme.Akzent)
    Private ReadOnly kzBestand As New KennzahlKarte("Restbestand", Theme.SymDiagramm, Theme.Gruen)
    Private ReadOnly kzWert As New KennzahlKarte("Restwert", Theme.SymGeld, Theme.Lila)
    Private ReadOnly kzLeer As New KennzahlKarte("Fast leer", Theme.SymWarnung, Theme.Rot)

    Private ReadOnly btnBearbeiten As New WebButton("Bearbeiten", Theme.SymBearbeiten)
    Private ReadOnly btnDuplizieren As New WebButton("Duplizieren", Theme.SymKopieren)
    Private ReadOnly btnVerbrauch As New WebButton("Verbrauch", Theme.SymWaage)
    Private ReadOnly btnLoeschen As New WebButton("Löschen", Theme.SymLoeschen, KnopfStil.Gefahr)
    Private ReadOnly tipps As New ToolTip()
    Private ReadOnly spaltenMenue As New ContextMenuStrip With {.Font = Theme.Schrift}
    Private karte As TabellenKarte

    Public Sub New(db As Datenbank)
        _db = db
        OberflaecheAufbauen()
        DatenLaden()
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

        Dim seite As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .Margin = New Padding(0)}
        seite.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        seite.RowStyles.Add(New RowStyle(SizeType.Absolute, 134))
        seite.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        ' --- Kopf
        Dim kopf As New SeitenKopf("Filament", "Spulen, Restbestand und Verbrauch im Überblick")
        Dim btnSpalten As New WebButton("Spalten", Theme.SymSpalten)
        AddHandler btnSpalten.Click, Sub(s, e) spaltenMenue.Show(btnSpalten, New Point(0, btnSpalten.Height + 2))
        tipps.SetToolTip(btnSpalten, "Auswählen, welche Spalten in der Tabelle angezeigt werden (auch per Rechtsklick auf einen Spaltenkopf)")
        Dim btnExport As New WebButton("CSV-Export", Theme.SymExport)
        Dim btnNeu As New WebButton("Neues Filament", Theme.SymNeu, KnopfStil.Primaer) With {.Margin = New Padding(0)}
        AddHandler btnExport.Click, Sub(s, e) CsvExport()
        AddHandler btnNeu.Click, Sub(s, e) NeuAnlegen()
        tipps.SetToolTip(btnExport, "Aktuelle Ansicht als CSV-Datei (z. B. für Excel) speichern")
        tipps.SetToolTip(btnNeu, "Neues Filament anlegen (Strg+N)")
        Dim btnEtiketten As New WebButton("Etiketten", Theme.SymDrucker)
        AddHandler btnEtiketten.Click, Sub(s, e) EtikettenDrucken()
        tipps.SetToolTip(btnEtiketten, "Etiketten mit QR-Code drucken (ausgewählte Spule ist vorausgewählt)")
        kopf.Knoepfe.Controls.Add(btnEtiketten)
        kopf.Knoepfe.Controls.Add(btnSpalten)
        kopf.Knoepfe.Controls.Add(btnExport)
        kopf.Knoepfe.Controls.Add(btnNeu)
        seite.Controls.Add(kopf, 0, 0)

        ' --- Kennzahlen
        seite.Controls.Add(KennzahlZeile(kzSpulen, kzBestand, kzWert, kzLeer), 0, 1)

        ' --- Karte mit Werkzeugleiste, Tabelle und Fußzeile
        TabelleAufbauen()
        karte = New TabellenKarte(grid)
        karte.FussRechts.Text = "Doppelklick zum Bearbeiten · Rechtsklick für weitere Aktionen"
        Dim werkzeug = karte.Werkzeug

        suche.Margin = New Padding(0, 0, 8, 4)
        suche.Width = 210
        AddHandler suche.Eingabe.TextChanged, Sub(s, e) AnsichtAktualisieren()
        AddHandler suche.Eingabe.KeyDown, AddressOf Suche_KeyDown
        tipps.SetToolTip(suche.Eingabe, "Sucht in ID, Hersteller, Material, Farbe, Lagerort und Notiz (Strg+F)." & Environment.NewLine &
                                        "ID scannen oder eintippen + Enter öffnet die Spule zum Wiegen.")
        werkzeug.Controls.Add(suche)

        FilterGestalten(cboMaterial, 140)
        AddHandler cboMaterial.SelectedIndexChanged, Sub(s, e) AnsichtAktualisieren()
        werkzeug.Controls.Add(cboMaterial)

        FilterGestalten(cboLagerort, 140)
        AddHandler cboLagerort.SelectedIndexChanged, Sub(s, e) AnsichtAktualisieren()
        werkzeug.Controls.Add(cboLagerort)

        chkLeere.Text = "Leere anzeigen"
        chkLeere.Checked = True
        chkLeere.AutoSize = True
        chkLeere.ForeColor = Theme.TextNormal
        chkLeere.Margin = New Padding(4, 8, 16, 4)
        AddHandler chkLeere.CheckedChanged, Sub(s, e) AnsichtAktualisieren()
        werkzeug.Controls.Add(chkLeere)

        For Each b In {btnBearbeiten, btnDuplizieren, btnVerbrauch, btnLoeschen}
            b.Margin = New Padding(0, 0, 8, 4)
            werkzeug.Controls.Add(b)
        Next
        AddHandler btnBearbeiten.Click, Sub(s, e) Bearbeiten()
        AddHandler btnDuplizieren.Click, Sub(s, e) Duplizieren()
        AddHandler btnVerbrauch.Click, Sub(s, e) VerbrauchBuchen()
        AddHandler btnLoeschen.Click, Sub(s, e) Loeschen()
        tipps.SetToolTip(btnBearbeiten, "Ausgewähltes Filament bearbeiten (Enter / Doppelklick)")
        tipps.SetToolTip(btnDuplizieren, "Gleiches Filament als neue Spule anlegen (Strg+D)")
        tipps.SetToolTip(btnVerbrauch, "Verbrauch abziehen oder Spule nachwiegen (Strg+G) – wird im Verlauf protokolliert")
        tipps.SetToolTip(btnLoeschen, "Ausgewähltes Filament löschen (Entf)")
        karte.KompaktFaehig(btnBearbeiten, btnDuplizieren, btnVerbrauch, btnLoeschen)

        seite.Controls.Add(karte, 0, 2)
        Controls.Add(seite)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    ''' <summary>Zeile mit gleich breiten Kennzahl-Kacheln.</summary>
    Friend Shared Function KennzahlZeile(ParamArray karten As KennzahlKarte()) As TableLayoutPanel
        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = karten.Length, .RowCount = 1,
                                            .Margin = New Padding(0, 0, 0, 20)}
        For i = 0 To karten.Length - 1
            t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F / karten.Length))
            karten(i).Margin = New Padding(If(i = 0, 0, 8), 0, If(i = karten.Length - 1, 0, 8), 0)
            t.Controls.Add(karten(i), i, 0)
        Next
        Return t
    End Function

    ''' <summary>Auswahlliste im Stil der Werkzeugleiste.</summary>
    Friend Shared Sub FilterGestalten(cb As ComboBox, breite As Integer)
        cb.DropDownStyle = ComboBoxStyle.DropDownList
        cb.FlatStyle = FlatStyle.Flat
        cb.Width = breite
        cb.BackColor = Theme.TabelleKopf
        cb.ForeColor = Theme.TextNormal
        cb.Margin = New Padding(0, 4, 8, 4)
    End Sub

    Private Sub TabelleAufbauen()
        grid.DataSource = quelle
        grid.SortSpalte = "Code"

        ' Zahlenspalten passen sich dem Inhalt an, Textspalten teilen sich den restlichen Platz
        grid.Spalte("Code", "ID", 0, menueName:="ID / Nummer")
        grid.Spalte("Hersteller", "Hersteller", 120, minBreite:=100)
        grid.Spalte("Material", "Material", 90, minBreite:=90)
        grid.Spalte("Farbe", "Farbe", 110, minBreite:=100)
        grid.Spalte("Durchmesser", "Ø", 0, "0.00", True, menueName:="Durchmesser")
        grid.Spalte("Gewicht", "Gewicht", 0, "#,##0 g", True, menueName:="Gewicht (netto)")
        grid.Spalte("Restgewicht", "Rest", 0, "#,##0 g", True, menueName:="Restgewicht")
        grid.Spalte("Spulengewicht", "Leerspule", 0, "#,##0 g", True, menueName:="Leerspulengewicht")
        grid.Spalte("RestAnteil", "Füllstand", 0, menueName:="Füllstand (Balken)")
        grid.Spalte("Preis", "Preis", 0, "C2", True)
        grid.Spalte("PreisProKg", "Preis/kg", 0, "C2", True, menueName:="Preis pro kg")
        grid.Spalte("Restwert", "Restwert", 0, "C2", True)
        grid.Spalte("Kaufdatum", "Gekauft", 0, "d", True, menueName:="Kaufdatum")
        grid.Spalte("Lagerort", "Lagerort", 100, minBreite:=80)
        grid.Spalte("Temperaturen", "Düse / Bett", 0, rechtsbuendig:=True, menueName:="Temperaturen (Düse / Bett)")
        grid.Spalte("Trocknung", "Trocknung", 90, minBreite:=80)
        grid.Spalte("Notiz", "Notiz", 140, minBreite:=60)
        grid.Columns("RestAnteil").AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        grid.Columns("RestAnteil").Width = Px(Me, 120)
        grid.Columns("Code").DefaultCellStyle.Font = Theme.SchriftFett
        SpaltenMenueAufbauen()

        ' Kontextmenü (Rechtsklick)
        Dim menue As New ContextMenuStrip With {.Font = Theme.Schrift, .ShowImageMargin = False}
        menue.Items.Add("Bearbeiten", Nothing, Sub(s, e) Bearbeiten())
        menue.Items.Add("Duplizieren", Nothing, Sub(s, e) Duplizieren())
        menue.Items.Add("Verbrauch / Wiegen", Nothing, Sub(s, e) VerbrauchBuchen())
        menue.Items.Add("Verlauf anzeigen", Nothing, Sub(s, e) VerlaufZeigen())
        menue.Items.Add("Etikett drucken", Nothing, Sub(s, e) EtikettenDrucken())
        menue.Items.Add(New ToolStripSeparator())
        menue.Items.Add("Löschen", Nothing, Sub(s, e) Loeschen()).ForeColor = Theme.Rot
        grid.ContextMenuStrip = menue

        AddHandler menue.Opening, Sub(s, e)
                                      ' Rechtsklick auf einen Spaltenkopf: Spaltenauswahl statt Zeilenaktionen
                                      Dim p = grid.PointToClient(Cursor.Position)
                                      If grid.HitTest(p.X, p.Y).Type = DataGridViewHitTestType.ColumnHeader Then
                                          e.Cancel = True
                                          spaltenMenue.Show(Cursor.Position)
                                      Else
                                          e.Cancel = Ausgewaehlt() Is Nothing
                                      End If
                                  End Sub
        AddHandler grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 Then Bearbeiten()
                                          End Sub
        AddHandler grid.SelectionChanged, Sub(s, e) KnoepfeAktualisieren()
        AddHandler grid.SortierungGeaendert, Sub(s, e) AnsichtAktualisieren()
        AddHandler grid.CellFormatting, AddressOf Grid_CellFormatting
        AddHandler grid.CellPainting, AddressOf Grid_CellPainting
    End Sub

    Private Sub KnoepfeAktualisieren()
        Dim vorhanden = Ausgewaehlt() IsNot Nothing
        For Each b In {btnBearbeiten, btnDuplizieren, btnVerbrauch, btnLoeschen}
            b.Enabled = vorhanden
        Next
    End Sub

    ' ------------------------------------------------------------------ Spaltenauswahl

    Private Sub SpaltenMenueAufbauen()
        spaltenMenue.Items.Add(New ToolStripLabel("Angezeigte Spalten") With {.ForeColor = Theme.TextLeise, .Font = Theme.SchriftKopf})
        For Each sp As DataGridViewColumn In grid.Columns
            Dim spalte = sp
            Dim eintrag As New ToolStripMenuItem(CStr(sp.Tag)) With {.CheckOnClick = False, .Tag = sp}
            AddHandler eintrag.Click, Sub(s, e) SpalteUmschalten(spalte, eintrag)
            spaltenMenue.Items.Add(eintrag)
        Next
        spaltenMenue.Items.Add(New ToolStripSeparator())
        spaltenMenue.Items.Add("Alle anzeigen", Nothing, Sub(s, e) SpaltenSetzen(grid.Columns.Cast(Of DataGridViewColumn)().Select(Function(c) c.Name), speichern:=True))
        spaltenMenue.Items.Add("Standard wiederherstellen", Nothing, Sub(s, e) SpaltenSetzen(StandardSpalten(), speichern:=True))

        ' Beim An-/Abhaken offen bleiben, damit mehrere Spalten nacheinander gewählt werden können
        AddHandler spaltenMenue.Closing, Sub(s, e)
                                             If e.CloseReason = ToolStripDropDownCloseReason.ItemClicked AndAlso
                                                spaltenMenue.Items.OfType(Of ToolStripMenuItem)().Any(Function(i) i.Selected AndAlso TypeOf i.Tag Is DataGridViewColumn) Then
                                                 e.Cancel = True
                                             End If
                                         End Sub
        AddHandler spaltenMenue.Opening, Sub(s, e) SpaltenMenueAktualisieren()

        Dim gespeichert As String = Nothing
        Try
            gespeichert = _db.EinstellungLesen(EinstellungSpalten)
        Catch ex As Exception
            ' Ohne gespeicherte Auswahl mit der Standardansicht starten
        End Try
        If String.IsNullOrWhiteSpace(gespeichert) Then
            SpaltenSetzen(StandardSpalten(), speichern:=False)
        Else
            SpaltenSetzen(gespeichert.Split(","c), speichern:=False)
        End If
    End Sub

    Private Function StandardSpalten() As IEnumerable(Of String)
        Return grid.Columns.Cast(Of DataGridViewColumn)().Select(Function(c) c.Name).Except(StandardAusgeblendet)
    End Function

    Private Sub SpaltenMenueAktualisieren()
        For Each eintrag In spaltenMenue.Items.OfType(Of ToolStripMenuItem)()
            Dim sp = TryCast(eintrag.Tag, DataGridViewColumn)
            If sp IsNot Nothing Then eintrag.Checked = sp.Visible
        Next
    End Sub

    Private Sub SpalteUmschalten(sp As DataGridViewColumn, eintrag As ToolStripMenuItem)
        If sp.Visible AndAlso grid.Columns.GetColumnCount(DataGridViewElementStates.Visible) <= 1 Then
            System.Media.SystemSounds.Beep.Play()   ' mindestens eine Spalte muss sichtbar bleiben
            Return
        End If
        sp.Visible = Not sp.Visible
        eintrag.Checked = sp.Visible
        SpaltenSpeichern()
    End Sub

    Private Sub SpaltenSetzen(sichtbar As IEnumerable(Of String), speichern As Boolean)
        Dim namen As New HashSet(Of String)(sichtbar.Select(Function(n) n.Trim()), StringComparer.OrdinalIgnoreCase)
        ' Unbrauchbare Einstellung (z. B. keine bekannte Spalte): Standard verwenden
        If Not grid.Columns.Cast(Of DataGridViewColumn)().Any(Function(c) namen.Contains(c.Name)) Then
            namen = New HashSet(Of String)(StandardSpalten())
        End If
        For Each sp As DataGridViewColumn In grid.Columns
            sp.Visible = namen.Contains(sp.Name)
        Next
        SpaltenMenueAktualisieren()
        If speichern Then SpaltenSpeichern()
    End Sub

    Private Sub SpaltenSpeichern()
        Dim sichtbar = grid.Columns.Cast(Of DataGridViewColumn)().Where(Function(c) c.Visible).Select(Function(c) c.Name)
        Try
            _db.EinstellungSpeichern(EinstellungSpalten, String.Join(",", sichtbar))
        Catch ex As Exception
            Fehler("Die Spaltenauswahl konnte nicht gespeichert werden.", ex)
        End Try
    End Sub

    ' ------------------------------------------------------------------ Tastatur

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        Select Case keyData
            Case Keys.Control Or Keys.N
                NeuAnlegen()
                Return True
            Case Keys.Control Or Keys.D
                Duplizieren()
                Return True
            Case Keys.Control Or Keys.G
                VerbrauchBuchen()
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
            _alle = _db.AlleLaden()
        Catch ex As Exception
            Fehler("Die Daten konnten nicht geladen werden.", ex)
            Return
        End Try
        MaterialFilterAktualisieren()
        AnsichtAktualisieren(auswahlId)
    End Sub

    Private Sub MaterialFilterAktualisieren()
        Dim bisher = TryCast(cboMaterial.SelectedItem, String)
        _filterWirdAktualisiert = True
        cboMaterial.Items.Clear()
        cboMaterial.Items.Add(AlleMaterialien)
        Dim materialien = _alle.Select(Function(f) f.Material).Where(Function(x) Not String.IsNullOrWhiteSpace(x))
        For Each m In materialien.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Function(x) x)
            cboMaterial.Items.Add(m)
        Next
        Dim index = If(bisher Is Nothing, 0, cboMaterial.Items.IndexOf(bisher))
        cboMaterial.SelectedIndex = If(index < 0, 0, index)

        Dim bisherOrt = TryCast(cboLagerort.SelectedItem, String)
        cboLagerort.Items.Clear()
        cboLagerort.Items.Add(AlleLagerorte)
        For Each ort In BekannteLagerorte().OrderBy(Function(x) x, StringComparer.CurrentCultureIgnoreCase)
            cboLagerort.Items.Add(ort)
        Next
        Dim indexOrt = If(bisherOrt Is Nothing, 0, cboLagerort.Items.IndexOf(bisherOrt))
        cboLagerort.SelectedIndex = If(indexOrt < 0, 0, indexOrt)
        _filterWirdAktualisiert = False
    End Sub

    Private Function BekannteLagerorte() As IEnumerable(Of String)
        Return _alle.Select(Function(f) f.Lagerort).Where(Function(x) Not String.IsNullOrWhiteSpace(x)).
                     Select(Function(x) x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
    End Function

    ''' <summary>Enter im Suchfeld: Passt die Eingabe genau auf eine ID (z. B. vom QR-Scanner), Spule zum Wiegen öffnen.</summary>
    Private Sub Suche_KeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode <> Keys.Enter Then Return
        e.SuppressKeyPress = True
        Dim code = suche.Eingabe.Text.Trim()
        If SpuleOeffnen(code, zumWiegen:=True) Then
            suche.Eingabe.Clear()
        ElseIf grid.Rows.Count > 0 Then
            grid.Focus()
        End If
    End Sub

    ''' <summary>
    ''' Wählt die Spule mit dieser ID aus und öffnet optional den Verbrauch-Dialog.
    ''' Liefert False, wenn es keine Spule mit genau dieser ID gibt.
    ''' </summary>
    Public Function SpuleOeffnen(code As String, zumWiegen As Boolean) As Boolean
        If String.IsNullOrWhiteSpace(code) Then Return False
        Dim f = _alle.FirstOrDefault(Function(x) String.Equals(x.Code, code.Trim(), StringComparison.OrdinalIgnoreCase))
        If f Is Nothing Then Return False
        ' Filter zurücksetzen, damit die Spule sichtbar ist
        _filterWirdAktualisiert = True
        suche.Eingabe.Clear()
        cboMaterial.SelectedIndex = 0
        cboLagerort.SelectedIndex = 0
        chkLeere.Checked = True
        _filterWirdAktualisiert = False
        AnsichtAktualisieren(f.Id)
        grid.Focus()
        If zumWiegen Then VerbrauchBuchen()
        Return True
    End Function

    Private Sub AnsichtAktualisieren(Optional auswahlId As Long = 0)
        If _filterWirdAktualisiert Then Return

        Dim merkeId As Long = auswahlId
        If merkeId = 0 Then
            Dim aktuell = Ausgewaehlt()
            If aktuell IsNot Nothing Then merkeId = aktuell.Id
        End If

        ' Filtern
        Dim treffer As IEnumerable(Of Filament) = _alle
        Dim begriff = suche.Eingabe.Text.Trim()
        If begriff.Length > 0 Then treffer = treffer.Where(Function(f) f.Passt(begriff))
        Dim material = TryCast(cboMaterial.SelectedItem, String)
        If material IsNot Nothing AndAlso material <> AlleMaterialien Then
            treffer = treffer.Where(Function(f) String.Equals(f.Material, material, StringComparison.OrdinalIgnoreCase))
        End If
        Dim ort = TryCast(cboLagerort.SelectedItem, String)
        If ort IsNot Nothing AndAlso ort <> AlleLagerorte Then
            treffer = treffer.Where(Function(f) String.Equals(f.Lagerort.Trim(), ort, StringComparison.OrdinalIgnoreCase))
        End If
        If Not chkLeere.Checked Then treffer = treffer.Where(Function(f) f.Restgewicht > 0)

        Dim liste = grid.Sortieren(treffer)
        quelle.DataSource = liste
        If merkeId <> 0 Then grid.Auswaehlen(Of Filament)(Function(f) f.Id = merkeId)
        KnoepfeAktualisieren()

        ' Kennzahlen
        Dim restKg = liste.Sum(Function(f) CLng(f.Restgewicht)) / 1000.0
        Dim restwert = liste.Sum(Function(f) f.Restwert)
        Dim fastLeer = liste.Where(Function(f) f.Restgewicht > 0 AndAlso f.RestAnteil < 0.15).ToList()
        Dim leer = liste.Where(Function(f) f.Restgewicht <= 0).Count()
        Dim gefiltert = liste.Count <> _alle.Count

        kzSpulen.Setzen(liste.Count.ToString("N0"),
                        If(gefiltert, $"gefiltert · {_alle.Count:N0} insgesamt", $"{liste.Count - leer:N0} mit Restbestand"))
        kzBestand.Setzen(restKg.ToString("N2") & " kg",
                         If(liste.Count > 0, $"Ø {restKg * 1000 / liste.Count:N0} g pro Spule", "keine Spulen"))
        kzWert.Setzen(restwert.ToString("C2"), $"Einkauf gesamt {liste.Sum(Function(f) f.Preis):C2}")
        kzLeer.Setzen(fastLeer.Count.ToString("N0"),
                      If(fastLeer.Count = 0, "alle Spulen über 15 %",
                         String.Join(", ", fastLeer.Take(3).Select(Function(f) f.Code)) & If(fastLeer.Count > 3, " …", "")))
        karte.FussLinks.Text = $"{liste.Count:N0} von {_alle.Count:N0} Spulen angezeigt"
    End Sub

    Private Function Ausgewaehlt() As Filament
        Return grid.Ausgewaehlt(Of Filament)()
    End Function

    Private Sub Grid_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs)
        If e.RowIndex < 0 OrElse e.CellStyle Is Nothing Then Return
        Dim f = grid.Eintrag(Of Filament)(e.RowIndex)
        If f IsNot Nothing AndAlso f.Restgewicht <= 0 Then e.CellStyle.ForeColor = Theme.Grau
    End Sub

    ''' <summary>Zeichnet Material als Badge, Farbe mit Farbpunkt und den Rest als Fortschrittsbalken.</summary>
    Private Sub Grid_CellPainting(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse e.Graphics Is Nothing OrElse e.CellStyle Is Nothing Then Return
        Dim name = grid.Columns(e.ColumnIndex).Name
        If name <> "Material" AndAlso name <> "Farbe" AndAlso name <> "RestAnteil" Then Return
        Dim f = grid.Eintrag(Of Filament)(e.RowIndex)
        If f Is Nothing Then Return
        Dim leer = f.Restgewicht <= 0

        If name = "Material" Then
            grid.BadgeZeichnen(e, f.Material, If(leer, Theme.Grau, Theme.AkzentText), If(leer, Theme.Seite, Hell(Theme.Akzent, If(Theme.Dunkel, 0.22, 0.1))))
            Return
        End If

        e.PaintBackground(e.CellBounds, True)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim b = e.CellBounds
        Dim pad = e.CellStyle.Padding.Left

        If name = "Farbe" Then
            Dim x = b.X + pad
            Dim farbe = FarbeZuName(f.Farbe)
            If farbe.HasValue Then
                Dim d = Px(Me, 12)
                Dim punkt As New Rectangle(x, b.Y + (b.Height - d) \ 2, d, d)
                Using pinsel As New SolidBrush(farbe.Value), stift As New Pen(Color.FromArgb(110, 128, 128, 128))
                    g.FillEllipse(pinsel, punkt)
                    g.DrawEllipse(stift, punkt)
                End Using
                x += d + Px(Me, 8)
            End If
            TextRenderer.DrawText(g, f.Farbe, e.CellStyle.Font, New Rectangle(x, b.Y, Math.Max(0, b.Right - pad - x), b.Height),
                                  e.CellStyle.ForeColor, TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or
                                  TextFormatFlags.NoPadding Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)
        Else
            Dim anteil = Math.Max(0.0, Math.Min(1.0, f.RestAnteil))
            Dim textBreite = Px(Me, 42)
            Dim h = Px(Me, 6)
            Dim balken As New Rectangle(b.X + pad, b.Y + (b.Height - h) \ 2,
                                        Math.Max(10, b.Width - 2 * pad - textBreite - Px(Me, 8)), h)
            Dim farbe = If(leer, Theme.Grau, If(anteil < 0.15, Theme.Rot, If(anteil < 0.5, Theme.Gelb, Theme.Gruen)))
            Using pfad = Abgerundet(balken, h \ 2), pinsel As New SolidBrush(Theme.Rand)
                g.FillPath(pinsel, pfad)
            End Using
            If anteil > 0 Then
                Dim gefuellt As New Rectangle(balken.X, balken.Y, Math.Max(h, CInt(balken.Width * anteil)), h)
                Using pfad = Abgerundet(gefuellt, h \ 2), pinsel As New SolidBrush(farbe)
                    g.FillPath(pinsel, pfad)
                End Using
            End If
            TextRenderer.DrawText(g, anteil.ToString("P0"), Theme.SchriftKlein,
                                  New Rectangle(balken.Right + Px(Me, 8), b.Y, textBreite, b.Height),
                                  If(anteil < 0.15 AndAlso Not leer, Theme.Rot, e.CellStyle.ForeColor),
                                  TextFormatFlags.Right Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
        End If
        grid.ZelleAbschliessen(e)
    End Sub

    ' ------------------------------------------------------------------ Aktionen

    Private Function BekannteHersteller() As IEnumerable(Of String)
        Return _alle.Select(Function(f) f.Hersteller)
    End Function

    Private Function BekannteMaterialien() As IEnumerable(Of String)
        Return _alle.Select(Function(f) f.Material)
    End Function

    Private Sub NeuAnlegen()
        Dim f As New Filament()
        Try
            f.Code = _db.NaechsterCode()
        Catch ex As Exception
            Fehler("Es konnte keine neue ID ermittelt werden.", ex)
            Return
        End Try
        DialogZeigenUndSpeichern(f, "Neues Filament")
    End Sub

    Private Sub Bearbeiten()
        Dim f = Ausgewaehlt()
        If f Is Nothing Then Return
        DialogZeigenUndSpeichern(f.Kopie(), "Filament bearbeiten – " & f.Code)
    End Sub

    Private Sub Duplizieren()
        Dim vorlage = Ausgewaehlt()
        If vorlage Is Nothing Then Return
        Dim f = vorlage.Kopie()
        f.Id = 0
        f.Restgewicht = f.Gewicht
        f.Kaufdatum = Date.Today
        Try
            f.Code = _db.NaechsterCode()
        Catch ex As Exception
            Fehler("Es konnte keine neue ID ermittelt werden.", ex)
            Return
        End Try
        DialogZeigenUndSpeichern(f, "Neue Spule (Kopie von " & vorlage.Code & ")")
    End Sub

    Private Sub DialogZeigenUndSpeichern(f As Filament, titel As String)
        Using dlg As New FilamentDialog(f, _db, titel, BekannteHersteller(), BekannteMaterialien(), BekannteLagerorte())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso Speichern(f) Then
                DatenLaden(f.Id)
            End If
        End Using
    End Sub

    Private Sub VerbrauchBuchen()
        Dim f = Ausgewaehlt()
        If f Is Nothing Then Return
        Using dlg As New VerbrauchDialog(f.Kopie())
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                _db.RestgewichtBuchen(f.Id, dlg.NeuesRestgewicht, dlg.Art, dlg.Bezeichnung)
            Catch ex As Exception
                Fehler("Der Verbrauch konnte nicht gespeichert werden.", ex)
                Return
            End Try
            DatenLaden(f.Id)
        End Using
    End Sub

    ''' <summary>Etiketten-Dialog; die ausgewählte Spule ist vorausgewählt.</summary>
    Public Sub EtikettenDrucken()
        Dim f = Ausgewaehlt()
        Using dlg As New EtikettenDialog(_db, _alle, If(f IsNot Nothing, {f.Id}, Array.Empty(Of Long)()))
            dlg.ShowDialog(Me)
        End Using
    End Sub

    Private Sub VerlaufZeigen()
        Dim f = Ausgewaehlt()
        If f IsNot Nothing Then RaiseEvent VerlaufAnzeigen(f.Id)
    End Sub

    Private Sub Loeschen()
        Dim f = Ausgewaehlt()
        If f Is Nothing Then Return
        Dim antwort = MessageBox.Show(Me,
            $"Soll das Filament {f.Code} ({f.Hersteller} {f.Material} {f.Farbe}) wirklich gelöscht werden?" & Environment.NewLine &
            "Bereits erfasste Drucke bleiben im Verlauf erhalten.",
            "Löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
        If antwort <> DialogResult.Yes Then Return
        Try
            _db.Loeschen(f.Id)
        Catch ex As Exception
            Fehler("Das Filament konnte nicht gelöscht werden.", ex)
            Return
        End Try
        DatenLaden()
    End Sub

    Private Function Speichern(f As Filament) As Boolean
        Try
            _db.Speichern(f)
            Return True
        Catch ex As Exception
            Fehler("Das Filament konnte nicht gespeichert werden.", ex)
            Return False
        End Try
    End Function

    Private Sub CsvExport()
        Dim liste = TryCast(quelle.DataSource, List(Of Filament))
        If liste Is Nothing OrElse liste.Count = 0 Then
            MessageBox.Show(Me, "Es gibt keine Einträge zum Exportieren.", "CSV-Export",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Using dlg As New SaveFileDialog With {
            .Filter = "CSV-Datei (*.csv)|*.csv",
            .FileName = "Filament_" & Date.Today.ToString("yyyy-MM-dd") & ".csv"
        }
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return

            Dim sb As New StringBuilder()
            sb.AppendLine("ID;Hersteller;Material;Farbe;Durchmesser (mm);Gewicht (g);Restgewicht (g);" &
                          "Spulengewicht (g);Preis (EUR);Preis/kg (EUR);Restwert (EUR);Kaufdatum;Notiz")
            For Each f In liste
                sb.AppendLine(String.Join(";", New String() {
                    Csv(f.Code), Csv(f.Hersteller), Csv(f.Material), Csv(f.Farbe),
                    f.Durchmesser.ToString("0.00"), f.Gewicht.ToString(), f.Restgewicht.ToString(),
                    f.Spulengewicht.ToString(), f.Preis.ToString("0.00"), f.PreisProKg.ToString("0.00"),
                    f.Restwert.ToString("0.00"), f.Kaufdatum.ToString("dd.MM.yyyy"), Csv(f.Notiz)
                }))
            Next

            Try
                ' UTF-8 mit BOM, damit Excel die Umlaute korrekt anzeigt
                File.WriteAllText(dlg.FileName, sb.ToString(), New UTF8Encoding(True))
            Catch ex As Exception
                Fehler("Die Datei konnte nicht geschrieben werden.", ex)
            End Try
        End Using
    End Sub

    Private Shared Function Csv(wert As String) As String
        If wert Is Nothing Then Return ""
        If wert.IndexOfAny({";"c, """"c, ControlChars.Cr, ControlChars.Lf}) >= 0 Then
            Return """" & wert.Replace("""", """""") & """"
        End If
        Return wert
    End Function

    Private Sub Fehler(text As String, ex As Exception)
        MessageBox.Show(Me, text & Environment.NewLine & Environment.NewLine & ex.Message,
                        "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
    End Sub

End Class
