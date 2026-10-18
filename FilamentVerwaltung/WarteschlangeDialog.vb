Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Druck-Warteschlange: Aufträge in Reihenfolge. "Drucken" öffnet den Senden-Dialog mit Drucker und Platte
''' aus dem Auftrag (inkl. Restmengen-Prüfung); nach dem Start ist der Auftrag aus der Liste.
''' </summary>
Public Class WarteschlangeDialog
    Inherits Form

    Private ReadOnly _db As IDatenquelle
    Private ReadOnly _ueberwachung As IDruckerDienst
    Private ReadOnly grid As New WebTabelle()
    Private ReadOnly quelle As New BindingSource()
    Private ReadOnly lblInfo As New Label With {.AutoSize = False, .Dock = DockStyle.Fill, .Height = 22, .AutoEllipsis = True,
                                                .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein}
    Private ReadOnly btnDrucken As New WebButton("Drucken …", Theme.SymDrucker, KnopfStil.Primaer)
    Private ReadOnly btnVor As New WebButton("Nach vorn", Theme.SymPfeilHoch)
    Private ReadOnly btnZurueck As New WebButton("Nach hinten", Theme.SymPfeilRunter)
    Private ReadOnly btnEntfernen As New WebButton("Entfernen", Theme.SymLoeschen, KnopfStil.Gefahr)

    ''' <summary>Es wurde etwas geändert (Zahl am Knopf neu berechnen).</summary>
    Public ReadOnly Property Geaendert As Boolean

    Public Sub New(db As IDatenquelle, ueberwachung As IDruckerDienst)
        _db = db
        _ueberwachung = ueberwachung
        OberflaecheAufbauen()
        Laden()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Warteschlange"
        Size = New Size(880, 520)
        MinimumSize = New Size(640, 380)
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
        kopf.Controls.Add(New Label With {.Text = "Warteschlange", .Font = Theme.SchriftLogo, .ForeColor = Theme.TextStark,
                                          .AutoSize = True, .Margin = New Padding(0)})
        kopf.Controls.Add(New Label With {
            .Text = "Was als Nächstes gedruckt werden soll. Ist ein Drucker frei, kommt eine Benachrichtigung. Gestartet wird von Hand " &
                    "(Druckplatte freimachen) – mit ""Drucken …"" hier oder auf dem Handy. Hinzufügen: beim Senden einer Datei ""In Warteschlange"".",
            .ForeColor = Theme.TextLeise, .AutoSize = True, .MaximumSize = New Size(800, 0), .Margin = New Padding(0, 4, 0, 0)})
        t.Controls.Add(kopf, 0, 0)

        grid.DataSource = quelle
        grid.Spalte("Position", "Nr.", 0, rechtsbuendig:=True)
        grid.Spalte("DateiName", "Datei", 220, minBreite:=180)
        grid.Spalte("Gramm", "Filament", 0, "#,##0 g", True)
        grid.Spalte("Druckzeit", "Druckzeit", 0, rechtsbuendig:=True)
        grid.Spalte("DruckerName", "Drucker", 0, minBreite:=110)
        grid.Spalte("Platte", "Platte", 0, rechtsbuendig:=True)
        grid.Columns("DateiName").DefaultCellStyle.Font = Theme.SchriftFett
        AddHandler grid.CellFormatting, Sub(s, e)
                                            If e.RowIndex < 0 Then Return
                                            Dim a = grid.Eintrag(Of Auftrag)(e.RowIndex)
                                            If a Is Nothing Then Return
                                            Select Case grid.Columns(e.ColumnIndex).Name
                                                Case "Position"
                                                    e.Value = (e.RowIndex + 1).ToString()   ' fortlaufend, auch wenn dazwischen etwas entfernt wurde
                                                    e.FormattingApplied = True
                                                Case "Druckzeit"
                                                    e.Value = DauerText(a.Druckzeit)
                                                    e.FormattingApplied = True
                                                Case "DruckerName"
                                                    If a.DruckerName.Length = 0 Then
                                                        e.Value = "egal"
                                                        e.CellStyle.ForeColor = Theme.TextLeise
                                                        e.FormattingApplied = True
                                                    End If
                                            End Select
                                        End Sub
        AddHandler grid.SelectionChanged, Sub(s, e) KnoepfeAktualisieren()
        AddHandler grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 Then Drucken()
                                          End Sub
        Dim karte As New Karte With {.Dock = DockStyle.Fill, .Padding = New Padding(1, 1, 1, 8)}
        karte.Controls.Add(grid)
        t.Controls.Add(karte, 0, 1)

        lblInfo.Margin = New Padding(2, 8, 0, 0)
        t.Controls.Add(lblInfo, 0, 2)

        Dim knoepfe As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .ColumnCount = 2, .Margin = New Padding(0, 14, 0, 0)}
        knoepfe.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        knoepfe.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim links As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        links.Controls.Add(btnDrucken)
        links.Controls.Add(btnVor)
        links.Controls.Add(btnZurueck)
        links.Controls.Add(btnEntfernen)
        Dim btnSchliessen As New WebButton("Schließen") With {.Margin = New Padding(0), .DialogResult = DialogResult.Cancel}
        knoepfe.Controls.Add(links, 0, 0)
        knoepfe.Controls.Add(btnSchliessen, 1, 0)
        t.Controls.Add(knoepfe, 0, 3)

        AddHandler btnDrucken.Click, Sub(s, e) Drucken()
        AddHandler btnVor.Click, Sub(s, e) Verschieben(True)
        AddHandler btnZurueck.Click, Sub(s, e) Verschieben(False)
        AddHandler btnEntfernen.Click, Sub(s, e) Entfernen()

        Controls.Add(t)
        CancelButton = btnSchliessen
        ResumeLayout(False)
    End Sub

    Private Sub Laden(Optional auswahlId As Long = 0)
        Dim liste As List(Of Auftrag)
        Try
            liste = _db.WarteschlangeLaden()
        Catch ex As Exception
            lblInfo.Text = "Die Warteschlange konnte nicht geladen werden: " & ex.Message
            Return
        End Try
        quelle.DataSource = liste
        If auswahlId > 0 Then grid.Auswaehlen(Of Auftrag)(Function(a) a.Id = auswahlId)
        KnoepfeAktualisieren()
        lblInfo.Text = If(liste.Count = 0, "Die Warteschlange ist leer.",
                          $"{liste.Count} Aufträge · {liste.Sum(Function(a) If(a.Gramm, 0)):N0} g Filament · " &
                          DauerText(CInt(liste.Sum(Function(a) CLng(If(a.Druckzeit, 0))))) & " Druckzeit")
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        KnoepfeAktualisieren()   ' erst jetzt steht die aktuelle Zeile fest
    End Sub

    Private Sub KnoepfeAktualisieren()
        Dim a = grid.Ausgewaehlt(Of Auftrag)()
        btnDrucken.Enabled = a IsNot Nothing
        btnEntfernen.Enabled = a IsNot Nothing
        Dim zeile = If(grid.CurrentRow?.Index, -1)
        btnVor.Enabled = a IsNot Nothing AndAlso zeile > 0
        btnZurueck.Enabled = a IsNot Nothing AndAlso zeile >= 0 AndAlso zeile < grid.Rows.Count - 1
    End Sub

    Private Sub Verschieben(nachVorn As Boolean)
        Dim a = grid.Ausgewaehlt(Of Auftrag)()
        If a Is Nothing Then Return
        Try
            _db.WarteschlangeVerschieben(a.Id, nachVorn)
            _Geaendert = True
            Laden(a.Id)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Warteschlange", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub Entfernen()
        Dim a = grid.Ausgewaehlt(Of Auftrag)()
        If a Is Nothing Then Return
        If MessageBox.Show(Me, $"""{a.DateiName}"" aus der Warteschlange entfernen? Die Datei selbst bleibt erhalten.", "Warteschlange",
                           MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        Try
            _db.WarteschlangeLoeschen(a.Id)
            _Geaendert = True
            Laden()
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Warteschlange", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Senden-Dialog mit Drucker und Platte des Auftrags; nach dem Start aus der Warteschlange nehmen.</summary>
    Private Sub Drucken()
        Dim a = grid.Ausgewaehlt(Of Auftrag)()
        If a Is Nothing Then Return
        Dim d As Datei
        Try
            d = _db.DateienLaden().FirstOrDefault(Function(x) x.Id = a.DateiId)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Warteschlange", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        If d Is Nothing Then
            MessageBox.Show(Me, "Die Datei gibt es nicht mehr.", "Warteschlange", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim pfad = LadeDialog.DateiHolen(Me, _db, d)
        If pfad Is Nothing Then Return
        Using dlg As New SendenDialog(_db, _ueberwachung, d, pfad, a.DruckerId, a.Platte)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
        End Using
        Try
            _db.WarteschlangeLoeschen(a.Id)
        Catch ex As Exception
            ' Druck läuft trotzdem – Eintrag ggf. von Hand entfernen
        End Try
        _Geaendert = True
        Laden()
    End Sub

End Class
