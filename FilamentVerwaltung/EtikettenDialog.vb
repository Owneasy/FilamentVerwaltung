Imports System.Drawing
Imports System.Drawing.Printing
Imports System.Windows.Forms

''' <summary>
''' Spulen-Etiketten mit QR-Code auswählen, in der Vorschau ansehen und drucken.
''' </summary>
Public Class EtikettenDialog
    Inherits Form

    Private Const EinstellungFormat As String = "Etiketten.Format"

    Private ReadOnly _db As Datenbank
    Private ReadOnly _alle As List(Of Filament)
    Private ReadOnly _ausgewaehlt As New HashSet(Of Long)
    Private _listeWirdAktualisiert As Boolean

    Private ReadOnly suche As New SuchFeld()
    Private ReadOnly liste As New CheckedListBox With {.Dock = DockStyle.Fill, .CheckOnClick = True, .BorderStyle = BorderStyle.None,
                                                       .IntegralHeight = False, .Font = Theme.Schrift, .FormattingEnabled = True}
    Private ReadOnly etikettVorschau As New Panel With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 0, 10)}
    Private ReadOnly cboFormat As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 400}
    Private ReadOnly numStart As New NumericUpDown With {.Minimum = 1, .Maximum = 24, .Width = 60}
    Private ReadOnly lblStart As New Label With {.Text = "Erstes freies Etikett auf dem Bogen:", .AutoSize = True, .Margin = New Padding(16, 7, 6, 0)}
    Private ReadOnly chkRahmen As New CheckBox With {.Text = "Umrisse drucken", .AutoSize = True, .Margin = New Padding(16, 6, 0, 0)}
    Private ReadOnly vorschau As New PrintPreviewControl With {.Dock = DockStyle.Fill, .AutoZoom = True, .UseAntiAlias = True}
    Private ReadOnly lblSeite As New Label With {.AutoSize = True, .ForeColor = Theme.TextLeise, .Margin = New Padding(8, 9, 8, 0)}
    Private ReadOnly btnZurueck As New WebButton("", "") With {.Text = "‹"}
    Private ReadOnly btnWeiter As New WebButton("", "") With {.Text = "›"}
    Private ReadOnly lblInfo As New Label With {.AutoSize = True, .ForeColor = Theme.TextLeise, .Anchor = AnchorStyles.Left}
    Private ReadOnly btnDrucken As New WebButton("Drucken …", Theme.SymDrucker, KnopfStil.Primaer)
    Private ReadOnly _verzoegerung As New Timer With {.Interval = 250}
    Private _dokument As EtikettenDruck

    ''' <param name="vorauswahl">IDs der Spulen, die schon angehakt sein sollen.</param>
    Public Sub New(db As Datenbank, spulen As List(Of Filament), vorauswahl As IEnumerable(Of Long))
        _db = db
        _alle = spulen.OrderBy(Function(f) f.Code).ToList()
        For Each id In vorauswahl
            _ausgewaehlt.Add(id)
        Next
        OberflaecheAufbauen()

        For Each fmt In EtikettFormat.Formate
            cboFormat.Items.Add(fmt)
        Next
        Dim gespeichert = 0
        Try
            Integer.TryParse(_db.EinstellungLesen(EinstellungFormat), gespeichert)
        Catch ex As Exception
            ' Standardformat verwenden
        End Try
        cboFormat.SelectedIndex = Math.Max(0, Math.Min(gespeichert, cboFormat.Items.Count - 1))

        ListeFuellen()
        AddHandler _verzoegerung.Tick, Sub(s, e)
                                           _verzoegerung.Stop()
                                           VorschauAktualisieren()
                                       End Sub
        VorschauAktualisieren()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Etiketten drucken"
        Size = New Size(1100, 720)
        MinimumSize = New Size(860, 520)
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        BackColor = Theme.Seite
        Padding = New Padding(20, 16, 20, 16)

        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 2}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 330))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        t.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        ' --- Links: Spulen auswählen
        Dim links As New Karte With {.Dock = DockStyle.Fill, .Padding = New Padding(14, 12, 6, 12), .Margin = New Padding(0, 0, 16, 0)}
        Dim lt As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4}
        lt.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        lt.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        lt.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        lt.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        lt.Controls.Add(New Label With {.Text = "Spulen", .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark, .AutoSize = True,
                                        .Margin = New Padding(0, 0, 0, 8)}, 0, 0)
        suche.Width = 290
        suche.Margin = New Padding(0, 0, 0, 8)
        AddHandler suche.Eingabe.TextChanged, Sub(s, e) ListeFuellen()
        lt.Controls.Add(suche, 0, 1)
        AddHandler liste.ItemCheck, AddressOf Liste_ItemCheck
        AddHandler liste.Format, Sub(s, e)
                                     Dim f = TryCast(e.ListItem, Filament)
                                     If f IsNot Nothing Then e.Value = $"{f.Code}  ·  {f.Hersteller} {f.Material} {f.Farbe}".Trim()
                                 End Sub
        lt.Controls.Add(liste, 0, 2)
        Dim auswahl As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 8, 0, 0)}
        Dim btnAlle As New WebButton("Alle")
        Dim btnKeine As New WebButton("Keine")
        Dim btnMitRest As New WebButton("Nur mit Rest")
        AddHandler btnAlle.Click, Sub(s, e) AlleSetzen(Function(f) True)
        AddHandler btnKeine.Click, Sub(s, e) AlleSetzen(Function(f) False)
        AddHandler btnMitRest.Click, Sub(s, e) AlleSetzen(Function(f) f.Restgewicht > 0)
        auswahl.Controls.Add(btnAlle)
        auswahl.Controls.Add(btnKeine)
        auswahl.Controls.Add(btnMitRest)
        lt.Controls.Add(auswahl, 0, 3)
        links.Controls.Add(lt)
        t.Controls.Add(links, 0, 0)

        ' --- Rechts: Format und Vorschau
        Dim rechts As New Karte With {.Dock = DockStyle.Fill, .Padding = New Padding(14, 12, 14, 12)}
        Dim rt As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4}
        rt.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        rt.RowStyles.Add(New RowStyle(SizeType.Percent, 42))
        rt.RowStyles.Add(New RowStyle(SizeType.Percent, 58))
        rt.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Dim optionen As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True, .Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 0, 10)}
        optionen.Controls.Add(cboFormat)
        optionen.Controls.Add(lblStart)
        optionen.Controls.Add(numStart)
        optionen.Controls.Add(chkRahmen)
        AddHandler cboFormat.SelectedIndexChanged, Sub(s, e) FormatGewechselt()
        AddHandler numStart.ValueChanged, Sub(s, e) VorschauSpaeter()
        AddHandler chkRahmen.CheckedChanged, Sub(s, e) VorschauSpaeter()
        rt.Controls.Add(optionen, 0, 0)
        etikettVorschau.BackColor = Theme.Seite
        AddHandler etikettVorschau.Paint, AddressOf EtikettVorschau_Paint
        AddHandler etikettVorschau.Resize, Sub(s, e) etikettVorschau.Invalidate()
        rt.Controls.Add(etikettVorschau, 0, 1)
        vorschau.BackColor = Theme.Seite
        rt.Controls.Add(vorschau, 0, 2)
        Dim seiten As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Anchor = AnchorStyles.None, .Margin = New Padding(0, 8, 0, 0)}
        seiten.Controls.Add(btnZurueck)
        seiten.Controls.Add(lblSeite)
        seiten.Controls.Add(btnWeiter)
        AddHandler btnZurueck.Click, Sub(s, e) SeiteWechseln(-1)
        AddHandler btnWeiter.Click, Sub(s, e) SeiteWechseln(1)
        rt.Controls.Add(seiten, 0, 3)
        rechts.Controls.Add(rt)
        t.Controls.Add(rechts, 1, 0)

        ' --- Unten
        Dim unten As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .ColumnCount = 3, .Margin = New Padding(0, 14, 0, 0)}
        unten.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        unten.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        unten.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim btnSchliessen As New WebButton("Schließen") With {.Margin = New Padding(0), .DialogResult = DialogResult.Cancel}
        AddHandler btnDrucken.Click, Sub(s, e) Drucken()
        unten.Controls.Add(lblInfo, 0, 0)
        unten.Controls.Add(btnDrucken, 1, 0)
        unten.Controls.Add(btnSchliessen, 2, 0)
        t.Controls.Add(unten, 0, 1)
        t.SetColumnSpan(unten, 2)

        Controls.Add(t)
        CancelButton = btnSchliessen
        ResumeLayout(False)
    End Sub

    Private Function Format() As EtikettFormat
        Return If(TryCast(cboFormat.SelectedItem, EtikettFormat), EtikettFormat.Formate(0))
    End Function

    Private Sub ListeFuellen()
        _listeWirdAktualisiert = True
        liste.BeginUpdate()
        liste.Items.Clear()
        Dim begriff = suche.Eingabe.Text.Trim()
        For Each f In _alle.Where(Function(x) begriff.Length = 0 OrElse x.Passt(begriff))
            liste.Items.Add(f, _ausgewaehlt.Contains(f.Id))
        Next
        liste.EndUpdate()
        _listeWirdAktualisiert = False
    End Sub

    Private Sub Liste_ItemCheck(sender As Object, e As ItemCheckEventArgs)
        If _listeWirdAktualisiert Then Return
        Dim f = DirectCast(liste.Items(e.Index), Filament)
        If e.NewValue = CheckState.Checked Then _ausgewaehlt.Add(f.Id) Else _ausgewaehlt.Remove(f.Id)
        VorschauSpaeter()
    End Sub

    ''' <summary>Setzt die Auswahl für alle (gefilterten) Spulen in der Liste.</summary>
    Private Sub AlleSetzen(bedingung As Func(Of Filament, Boolean))
        For Each f In liste.Items.Cast(Of Filament)()
            If bedingung(f) Then _ausgewaehlt.Add(f.Id) Else _ausgewaehlt.Remove(f.Id)
        Next
        ListeFuellen()
        VorschauSpaeter()
    End Sub

    Private Sub FormatGewechselt()
        Dim fmt = Format()
        lblStart.Visible = Not fmt.Einzeln
        numStart.Visible = Not fmt.Einzeln
        numStart.Maximum = Math.Max(1, fmt.ProBogen)
        Try
            _db.EinstellungSpeichern(EinstellungFormat, cboFormat.SelectedIndex.ToString())
        Catch ex As Exception
            ' Nicht kritisch
        End Try
        VorschauSpaeter()
    End Sub

    Private Sub VorschauSpaeter()
        _verzoegerung.Stop()
        _verzoegerung.Start()
    End Sub

    Private Function GewaehlteSpulen() As List(Of Filament)
        Return _alle.Where(Function(f) _ausgewaehlt.Contains(f.Id)).ToList()
    End Function

    Private Sub VorschauAktualisieren()
        Dim spulen = GewaehlteSpulen()
        _dokument?.Dispose()
        _dokument = New EtikettenDruck(spulen, Format(), CInt(numStart.Value), chkRahmen.Checked)
        vorschau.Document = If(spulen.Count > 0, _dokument, Nothing)
        vorschau.StartPage = 0
        etikettVorschau.Invalidate()
        btnDrucken.Enabled = spulen.Count > 0
        Dim seiten = _dokument.Seiten
        lblInfo.Text = If(spulen.Count = 0, "Keine Spule ausgewählt.",
                          $"{spulen.Count} Etiketten · {seiten} {If(Format().Einzeln, "Etiketten", If(seiten = 1, "Bogen", "Bögen"))} zum Drucken")
        SeitenAnzeigeAktualisieren()
    End Sub

    ''' <summary>Großes, maßstabsgetreues Bild des ersten ausgewählten Etiketts.</summary>
    Private Sub EtikettVorschau_Paint(sender As Object, e As PaintEventArgs)
        Dim g = e.Graphics
        Dim spule = GewaehlteSpulen().FirstOrDefault()
        If spule Is Nothing Then
            TextRenderer.DrawText(g, "Links Spulen anhaken, um die Etiketten zu sehen.", Theme.Schrift, etikettVorschau.ClientRectangle,
                                  Theme.TextLeise, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            Return
        End If
        Dim fmt = Format()
        Dim dpi = g.DpiX
        Dim pxProMm = dpi / 25.4F
        ' Etikett so groß wie möglich, höchstens 3-fache Originalgröße
        Dim zoom = Math.Min(3.0F, Math.Min((etikettVorschau.Width - 40) / (fmt.Breite * pxProMm), (etikettVorschau.Height - 56) / (fmt.Hoehe * pxProMm)))
        If zoom <= 0 Then Return
        Dim breitePx = fmt.Breite * pxProMm * zoom, hoehePx = fmt.Hoehe * pxProMm * zoom
        Dim x = (etikettVorschau.Width - breitePx) / 2, y = (etikettVorschau.Height - hoehePx - 22) / 2
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        Using schatten As New SolidBrush(Color.FromArgb(30, 0, 0, 0))
            g.FillRectangle(schatten, x + 3, y + 4, breitePx, hoehePx)
        End Using
        g.FillRectangle(Brushes.White, x, y, breitePx, hoehePx)
        ' In Millimetern zeichnen wie beim Druck
        Dim zustand = g.Save()
        g.PageUnit = GraphicsUnit.Millimeter
        g.PageScale = zoom
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit
        EtikettenDruck.Zeichnen(g, spule, New RectangleF(x / pxProMm / zoom, y / pxProMm / zoom, fmt.Breite, fmt.Hoehe), rahmen:=False)
        g.Restore(zustand)
        TextRenderer.DrawText(g, $"{fmt.Breite:0.#} × {fmt.Hoehe:0.#} mm · Vorschau {spule.Code}", Theme.SchriftKlein,
                              New Rectangle(0, CInt(y + hoehePx + 6), etikettVorschau.Width, 18), Theme.TextLeise, TextFormatFlags.HorizontalCenter)
    End Sub

    Private Sub SeiteWechseln(richtung As Integer)
        If _dokument Is Nothing Then Return
        vorschau.StartPage = Math.Max(0, Math.Min(_dokument.Seiten - 1, vorschau.StartPage + richtung))
        SeitenAnzeigeAktualisieren()
    End Sub

    Private Sub SeitenAnzeigeAktualisieren()
        Dim seiten = If(_dokument?.Seiten, 0)
        lblSeite.Text = If(seiten = 0, "–", $"Seite {vorschau.StartPage + 1} von {seiten}")
        btnZurueck.Enabled = vorschau.StartPage > 0
        btnWeiter.Enabled = vorschau.StartPage < seiten - 1
    End Sub

    Private Sub Drucken()
        If _dokument Is Nothing OrElse GewaehlteSpulen().Count = 0 Then Return
        Dim dokument As New EtikettenDruck(GewaehlteSpulen(), Format(), CInt(numStart.Value), chkRahmen.Checked)
        Using dlg As New PrintDialog With {.Document = dokument, .UseEXDialog = True, .AllowSomePages = False}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then
                dokument.Dispose()
                Return
            End If
        End Using
        Try
            dokument.Print()
        Catch ex As Exception
            MessageBox.Show(Me, "Der Druck ist fehlgeschlagen." & Environment.NewLine & Environment.NewLine & ex.Message,
                            "Etiketten", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            dokument.Dispose()
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _verzoegerung.Dispose()
            _dokument?.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class
