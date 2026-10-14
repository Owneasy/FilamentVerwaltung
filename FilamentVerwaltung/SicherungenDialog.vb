Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Übersicht der Sicherungen: jetzt sichern, wiederherstellen, Ordner öffnen.
''' </summary>
Public Class SicherungenDialog
    Inherits Form

    Private ReadOnly _db As Datenbank
    Private ReadOnly grid As New WebTabelle()
    Private ReadOnly quelle As New BindingSource()
    Private ReadOnly lblInfo As New Label With {.AutoSize = False, .Dock = DockStyle.Fill, .Height = 22, .AutoEllipsis = True, .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein}
    Private ReadOnly btnWiederherstellen As New WebButton("Wiederherstellen", Theme.SymVerlauf, KnopfStil.Gefahr)

    ''' <summary>True, wenn eine Sicherung zurückgespielt wurde (die Seiten müssen neu laden).</summary>
    Public ReadOnly Property Wiederhergestellt As Boolean

    Public Sub New(db As Datenbank)
        _db = db
        OberflaecheAufbauen()
        Laden()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Sicherungen"
        Size = New Size(680, 540)
        MinimumSize = New Size(560, 400)
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
        kopf.Controls.Add(New Label With {.Text = "Sicherungen der Datenbank", .Font = Theme.SchriftLogo, .ForeColor = Theme.TextStark,
                                          .AutoSize = True, .Margin = New Padding(0)})
        kopf.Controls.Add(New Label With {
            .Text = $"Automatisch einmal täglich beim Start, die letzten {AnzahlAutomatisch} automatischen bleiben erhalten. " &
                    "Manuelle Sicherungen werden nie automatisch gelöscht. Der Ordner ""Dateien"" (STL, G-Code …) wird nicht mitgesichert.",
            .ForeColor = Theme.TextLeise, .AutoSize = True, .MaximumSize = New Size(600, 0), .Margin = New Padding(0, 4, 0, 0)})
        t.Controls.Add(kopf, 0, 0)

        grid.DataSource = quelle
        grid.Spalte("Zeitpunkt", "Zeitpunkt", 0, "dd.MM.yyyy  HH:mm:ss")
        grid.Spalte("Art", "Art", 0).MinimumWidth = Px(Me, 150)
        grid.Spalte("Groesse", "Größe", 0, rechtsbuendig:=True)
        grid.Spalte("Name", "Datei", 100)
        AddHandler grid.CellFormatting, Sub(s, e)
                                            If e.RowIndex >= 0 AndAlso grid.Columns(e.ColumnIndex).Name = "Groesse" Then
                                                e.Value = GroesseText(grid.Eintrag(Of SicherungInfo)(e.RowIndex).Groesse)
                                                e.FormattingApplied = True
                                            End If
                                        End Sub
        AddHandler grid.CellPainting, AddressOf Grid_CellPainting
        AddHandler grid.SelectionChanged, Sub(s, e) btnWiederherstellen.Enabled = grid.Ausgewaehlt(Of SicherungInfo)() IsNot Nothing
        AddHandler grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 Then Wiederherstellen()
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
        Dim btnJetzt As New WebButton("Jetzt sichern", Theme.SymNeu, KnopfStil.Primaer)
        Dim btnOrdner As New WebButton("Ordner öffnen", Theme.SymOrdnerOffen)
        links.Controls.Add(btnJetzt)
        links.Controls.Add(btnWiederherstellen)
        links.Controls.Add(btnOrdner)
        Dim btnSchliessen As New WebButton("Schließen") With {.Margin = New Padding(0), .DialogResult = DialogResult.Cancel}
        knoepfe.Controls.Add(links, 0, 0)
        knoepfe.Controls.Add(btnSchliessen, 1, 0)
        t.Controls.Add(knoepfe, 0, 3)

        AddHandler btnJetzt.Click, Sub(s, e) JetztSichern()
        AddHandler btnWiederherstellen.Click, Sub(s, e) Wiederherstellen()
        AddHandler btnOrdner.Click, Sub(s, e) OrdnerOeffnen()

        Controls.Add(t)
        CancelButton = btnSchliessen
        ResumeLayout(False)
    End Sub

    Private Sub Laden(Optional auswahl As String = Nothing)
        Dim liste As List(Of SicherungInfo)
        Try
            liste = Sicherung.Liste(_db)
        Catch ex As Exception
            liste = New List(Of SicherungInfo)
            lblInfo.Text = "Die Sicherungen konnten nicht gelesen werden: " & ex.Message
        End Try
        quelle.DataSource = liste
        If auswahl IsNot Nothing Then grid.Auswaehlen(Of SicherungInfo)(Function(s) s.Pfad = auswahl)
        btnWiederherstellen.Enabled = grid.Ausgewaehlt(Of SicherungInfo)() IsNot Nothing
        lblInfo.Text = If(liste.Count = 0, "Noch keine Sicherungen vorhanden.",
                          $"{liste.Count} Sicherungen · {GroesseText(liste.Sum(Function(s) s.Groesse))} · Ordner: {Sicherung.Ordner(_db)}")
    End Sub

    Private Sub Grid_CellPainting(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse e.Graphics Is Nothing OrElse e.CellStyle Is Nothing Then Return
        If grid.Columns(e.ColumnIndex).Name <> "Art" Then Return
        Dim s = grid.Eintrag(Of SicherungInfo)(e.RowIndex)
        If s Is Nothing Then Return
        Dim farbe = If(s.Art = SicherungInfo.ArtManuell, Theme.Akzent,
                       If(s.Art = SicherungInfo.ArtVorWiederherstellung, Theme.Gelb, Theme.Tuerkis))
        grid.BadgeZeichnen(e, s.Art, farbe, Hell(farbe, 0.12))
    End Sub

    Private Sub JetztSichern()
        Try
            Cursor = Cursors.WaitCursor
            Dim neu = Erstellen(_db, SicherungInfo.ArtManuell)
            Laden(neu.Pfad)
        Catch ex As Exception
            MessageBox.Show(Me, "Die Sicherung ist fehlgeschlagen." & Environment.NewLine & Environment.NewLine & ex.Message,
                            "Sicherungen", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Sub Wiederherstellen()
        Dim s = grid.Ausgewaehlt(Of SicherungInfo)()
        If s Is Nothing Then Return
        Dim antwort = MessageBox.Show(Me,
            $"Die aktuelle Datenbank wird durch die Sicherung vom {s.Zeitpunkt:dd.MM.yyyy HH:mm} ersetzt." & Environment.NewLine &
            "Alle Änderungen seitdem gehen verloren." & Environment.NewLine & Environment.NewLine &
            "Vorher wird der aktuelle Stand als Sicherung ""Vor Wiederherstellung"" abgelegt, damit du den Schritt rückgängig machen kannst." &
            Environment.NewLine & Environment.NewLine & "Wiederherstellen?",
            "Sicherung wiederherstellen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
        If antwort <> DialogResult.Yes Then Return
        Try
            Cursor = Cursors.WaitCursor
            Sicherung.Wiederherstellen(_db, s)
            _Wiederhergestellt = True
            Laden(s.Pfad)
            MessageBox.Show(Me, $"Die Sicherung vom {s.Zeitpunkt:dd.MM.yyyy HH:mm} wurde wiederhergestellt.", "Sicherungen",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As Exception
            MessageBox.Show(Me, "Die Wiederherstellung ist fehlgeschlagen." & Environment.NewLine & Environment.NewLine & ex.Message,
                            "Sicherungen", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Sub OrdnerOeffnen()
        Try
            Directory.CreateDirectory(Sicherung.Ordner(_db))
            Process.Start("explorer.exe", """" & Sicherung.Ordner(_db) & """")
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Sicherungen", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

End Class
