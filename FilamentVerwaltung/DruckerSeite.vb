Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>Kleines Status-Badge (Text in einer abgerundeten, getönten Fläche).</summary>
Public Class StatusBadge
    Inherits Control

    Private _farbe As Color = Theme.Grau

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw Or ControlStyles.SupportsTransparentBackColor, True)
        Font = Theme.SchriftKopf
        Height = Px(Me, 24)
    End Sub

    Public Sub Setzen(text As String, farbe As Color)
        If Me.Text = text AndAlso _farbe = farbe Then Return
        Me.Text = text
        _farbe = farbe
        Width = TextRenderer.MeasureText(text, Font).Width + Px(Me, 14)
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(If(Parent IsNot Nothing, Parent.BackColor, Theme.KartenFlaeche))
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using pfad = Abgerundet(New Rectangle(0, 0, Width - 1, Height - 1), Height \ 2), b As New SolidBrush(Hell(_farbe, If(Theme.Dunkel, 0.25, 0.14)))
            g.FillPath(b, pfad)
        End Using
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, If(Theme.Dunkel, Hell(_farbe, 1.0), _farbe),
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
    End Sub
End Class

''' <summary>Schlanker Fortschrittsbalken.</summary>
Public Class Fortschrittsbalken
    Inherits Control

    Private _wert As Double

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        Height = Px(Me, 8)
    End Sub

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property Wert As Double
        Get
            Return _wert
        End Get
        Set(value As Double)
            _wert = Math.Max(0, Math.Min(1, value))
            Invalidate()
        End Set
    End Property

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(If(Parent IsNot Nothing, Parent.BackColor, Theme.KartenFlaeche))
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim r As New Rectangle(0, 0, Width - 1, Height - 1)
        Using pfad = Abgerundet(r, Height \ 2), b As New SolidBrush(Theme.Rand)
            g.FillPath(b, pfad)
        End Using
        If _wert > 0 Then
            Using pfad = Abgerundet(New Rectangle(0, 0, Math.Max(Height, CInt((Width - 1) * _wert)), Height - 1), Height \ 2), b As New SolidBrush(Theme.Akzent)
                g.FillPath(b, pfad)
            End Using
        End If
    End Sub
End Class

''' <summary>Live-Karte eines Druckers.</summary>
Public Class DruckerKarte
    Inherits Karte

    Private ReadOnly _db As Datenbank
    Public ReadOnly Property Drucker As Drucker

    Private ReadOnly lblName As New Label With {.AutoSize = True, .Font = Theme.SchriftLogo, .ForeColor = Theme.TextStark, .Margin = New Padding(0)}
    Private ReadOnly lblTyp As New Label With {.AutoSize = True, .Font = Theme.SchriftKlein, .ForeColor = Theme.TextLeise, .Margin = New Padding(0, 2, 0, 0)}
    Private ReadOnly badge As New StatusBadge With {.Anchor = AnchorStyles.Top Or AnchorStyles.Right, .Margin = New Padding(0, 2, 0, 0)}
    Private ReadOnly lblDatei As New Label With {.AutoSize = False, .AutoEllipsis = True, .Dock = DockStyle.Fill, .Height = 22,
                                                 .Font = Theme.SchriftFett, .ForeColor = Theme.TextNormal, .Margin = New Padding(0, 12, 0, 4)}
    Private ReadOnly balken As New Fortschrittsbalken With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 4)}
    Private ReadOnly lblFortschritt As New Label With {.AutoSize = True, .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein, .Margin = New Padding(0, 0, 0, 8)}
    Private ReadOnly lblMeldung As New Label With {.AutoSize = True, .MaximumSize = New Size(400, 0), .ForeColor = Theme.Rot,
                                                   .Font = Theme.SchriftKlein, .Margin = New Padding(0, 0, 0, 6), .Visible = False}
    Private ReadOnly spulen As New TableLayoutPanel With {.AutoSize = True, .ColumnCount = 2, .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 4)}
    Private _fachSignatur As String = Nothing
    Private _filamente As New List(Of Filament)
    Private _aktualisiert As Boolean

    Public Event BearbeitenGeklickt(d As Drucker)
    Public Event ProtokollGeklickt(d As Drucker)

    Public Sub New(d As Drucker, db As Datenbank)
        _Drucker = d
        _db = db
        Width = 440
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        MinimumSize = New Size(440, 0)
        MaximumSize = New Size(440, 0)
        Padding = New Padding(18, 16, 18, 14)
        Margin = New Padding(0, 0, 16, 16)

        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .ColumnCount = 1, .Margin = New Padding(0)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        Dim kopf As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .ColumnCount = 2, .Margin = New Padding(0)}
        kopf.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        kopf.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim namen As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        namen.Controls.Add(lblName)
        namen.Controls.Add(lblTyp)
        kopf.Controls.Add(namen, 0, 0)
        kopf.Controls.Add(badge, 1, 0)
        t.Controls.Add(kopf)
        t.Controls.Add(lblDatei)
        t.Controls.Add(balken)
        t.Controls.Add(lblFortschritt)
        t.Controls.Add(lblMeldung)
        spulen.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        spulen.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        t.Controls.Add(spulen)

        Dim knoepfe As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 10, 0, 0)}
        Dim btnBearbeiten As New WebButton("Bearbeiten", Theme.SymBearbeiten)
        Dim btnProtokoll As New WebButton("Protokoll", Theme.SymSpalten)
        AddHandler btnBearbeiten.Click, Sub(s, e) RaiseEvent BearbeitenGeklickt(Drucker)
        AddHandler btnProtokoll.Click, Sub(s, e) RaiseEvent ProtokollGeklickt(Drucker)
        knoepfe.Controls.Add(btnBearbeiten)
        knoepfe.Controls.Add(btnProtokoll)
        t.Controls.Add(knoepfe)
        Controls.Add(t)

        lblName.Text = d.Name
        lblTyp.Text = d.TypName & " · " & d.Adresse
        If Not d.Aktiv Then
            badge.Setzen("Nicht überwacht", Theme.Grau)
            lblDatei.Text = "Überwachung ausgeschaltet"
        Else
            badge.Setzen("Verbinde …", Theme.Grau)
            lblDatei.Text = "–"
        End If
    End Sub

    Public Sub Aktualisieren(s As DruckerStatus, filamente As List(Of Filament))
        _filamente = filamente
        If s Is Nothing Then
            SpulenAufbauen(New List(Of DruckerFach))
            Return
        End If
        Dim farbe = If(s.Zustand = DruckerZustand.Druckt, Theme.Akzent,
                    If(s.Zustand = DruckerZustand.Pausiert, Theme.Gelb,
                    If(s.Zustand = DruckerZustand.Fertig OrElse s.Zustand = DruckerZustand.Bereit, Theme.Gruen,
                    If(s.Zustand = DruckerZustand.Abgebrochen OrElse s.Zustand = DruckerZustand.Fehler OrElse s.Zustand = DruckerZustand.Offline, Theme.Rot, Theme.Grau))))
        badge.Setzen(DruckerStatus.ZustandText(s.Zustand), farbe)

        lblDatei.Text = If(s.Datei.Length > 0, s.Datei, If(s.Zustand = DruckerZustand.Offline, "Nicht erreichbar", "Kein Druckauftrag"))
        balken.Visible = s.DruckLaeuft OrElse s.Zustand = DruckerZustand.Fertig
        balken.Wert = If(s.Zustand = DruckerZustand.Fertig, 1.0, s.Fortschritt)
        Dim teile As New List(Of String)
        If s.DruckLaeuft Then
            teile.Add(s.Fortschritt.ToString("P0"))
            If s.RestSekunden.HasValue Then teile.Add("noch " & DauerText(s.RestSekunden))
            If s.DauerSekunden.HasValue Then teile.Add("läuft seit " & DauerText(s.DauerSekunden))
        ElseIf s.Zustand = DruckerZustand.Fertig Then
            teile.Add("Fertig" & If(s.DauerSekunden.HasValue, " nach " & DauerText(s.DauerSekunden), ""))
        End If
        If s.GrammGeplant.HasValue Then teile.Add($"{s.GrammGeplant.Value:N0} g laut Slicer")
        lblFortschritt.Text = String.Join(" · ", teile)
        lblFortschritt.Visible = teile.Count > 0
        lblMeldung.Text = s.Meldung
        lblMeldung.Visible = s.Zustand = DruckerZustand.Offline AndAlso s.Meldung.Length > 0
        SpulenAufbauen(s.Faecher, s.Slot)
    End Sub

    ''' <summary>Zuordnung Fach → Spule. Wird nur neu aufgebaut, wenn sich die Fächer ändern.</summary>
    Private Sub SpulenAufbauen(faecher As List(Of DruckerFach), Optional aktivesFach As Integer? = Nothing)
        Dim liste = If(faecher.Count > 0, faecher, New List(Of DruckerFach) From {New DruckerFach With {.Slot = Drucker.SlotStandard, .Beschreibung = "Spule"}})
        Dim signatur = String.Join("|", liste.Select(Function(f) $"{f.Slot}:{f.Beschreibung}")) & "#" & _filamente.Count & "#" & aktivesFach
        If signatur = _fachSignatur Then Return
        _fachSignatur = signatur

        Dim zuordnung As Dictionary(Of Integer, Long)
        Try
            zuordnung = _db.SpulenZuordnung(Drucker.Id)
        Catch ex As Exception
            zuordnung = New Dictionary(Of Integer, Long)
        End Try

        spulen.SuspendLayout()
        For Each c As Control In spulen.Controls.Cast(Of Control)().ToList()
            c.Dispose()
        Next
        spulen.Controls.Clear()
        spulen.RowCount = 0
        For Each fach In liste
            Dim slot = fach.Slot
            Dim beschriftung As New Label With {.Text = fach.Beschreibung & If(aktivesFach.HasValue AndAlso aktivesFach.Value = slot, "  ●", ""),
                                                .AutoSize = True, .ForeColor = If(aktivesFach.HasValue AndAlso aktivesFach.Value = slot, Theme.AkzentText, Theme.TextLeise),
                                                .Font = Theme.SchriftKlein, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 6, 10, 2)}
            Dim auswahl As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill, .Margin = New Padding(0, 2, 0, 2)}
            FilamentSeite.FilterGestalten(auswahl, 260)
            auswahl.Dock = DockStyle.Fill
            auswahl.Margin = New Padding(0, 2, 0, 2)
            auswahl.Items.Add("(keine Spule zugeordnet)")
            Dim zugeordnet As Long = 0
            zuordnung.TryGetValue(slot, zugeordnet)
            Dim angeboten = _filamente.Where(Function(f) f.Restgewicht > 0 OrElse f.Id = zugeordnet).OrderBy(Function(f) f.Code).ToList()
            For Each f In angeboten
                auswahl.Items.Add($"{f.Code} · {f.Hersteller} {f.Material} {f.Farbe} ({f.Restgewicht:N0} g)")
            Next
            auswahl.SelectedIndex = Math.Max(0, angeboten.FindIndex(Function(f) f.Id = zugeordnet) + 1)
            AddHandler auswahl.SelectionChangeCommitted, Sub(s, e)
                                                             Dim index = auswahl.SelectedIndex
                                                             Try
                                                                 _db.SpuleZuordnen(Drucker.Id, slot, If(index <= 0, CType(Nothing, Long?), angeboten(index - 1).Id))
                                                             Catch ex As Exception
                                                                 MessageBox.Show(FindForm(), ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
                                                             End Try
                                                         End Sub
            spulen.Controls.Add(beschriftung, 0, spulen.RowCount)
            spulen.Controls.Add(auswahl, 1, spulen.RowCount)
            spulen.RowCount += 1
        Next
        spulen.ResumeLayout()
    End Sub

End Class

''' <summary>
''' Seite "Drucker": angebundene Drucker mit Live-Status und Spulen-Zuordnung.
''' </summary>
Public Class DruckerSeite
    Inherits UserControl
    Implements ISeite

    Private ReadOnly _db As Datenbank
    Private ReadOnly _ueberwachung As DruckerUeberwachung
    Private ReadOnly karten As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Margin = New Padding(0), .Padding = New Padding(0, 0, 0, 8)}
    Private ReadOnly lblLeer As New Label With {.AutoSize = True, .ForeColor = Theme.TextLeise, .MaximumSize = New Size(640, 0), .Margin = New Padding(0, 8, 0, 0),
        .Text = "Noch kein Drucker angebunden. Mit ""Drucker hinzufügen"" verbindest du Bambu Lab, Prusa (PrusaLink), Klipper (Moonraker) " &
                "oder OctoPrint. Ist ein Druck fertig, schlägt die App vor, den Verbrauch zu buchen."}
    Private _filamente As New List(Of Filament)

    Public Sub New(db As Datenbank, ueberwachung As DruckerUeberwachung)
        _db = db
        _ueberwachung = ueberwachung
        SuspendLayout()
        Dock = DockStyle.Fill
        BackColor = Theme.Seite
        Font = Theme.Schrift
        Padding = New Padding(28, 24, 28, 24)

        Dim seite As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .Margin = New Padding(0)}
        seite.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        seite.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        Dim kopf As New SeitenKopf("Drucker", "Live-Status deiner Drucker – fertige Drucke werden zum Buchen vorgeschlagen")
        Dim btnNeu As New WebButton("Drucker hinzufügen", Theme.SymNeu, KnopfStil.Primaer) With {.Margin = New Padding(0)}
        AddHandler btnNeu.Click, Sub(s, e) Bearbeiten(New Drucker(), "Drucker hinzufügen")
        kopf.Knoepfe.Controls.Add(btnNeu)
        seite.Controls.Add(kopf, 0, 0)
        karten.Controls.Add(lblLeer)
        seite.Controls.Add(karten, 0, 1)
        Controls.Add(seite)
        ResumeLayout(False)

        AddHandler _ueberwachung.StatusGeaendert, AddressOf StatusGeaendert
    End Sub

    Public Sub Aktivieren() Implements ISeite.Aktivieren
        karten.Focus()
    End Sub

    Public Sub Neuladen() Implements ISeite.Neuladen
        Dim drucker As List(Of Drucker)
        Try
            drucker = _db.DruckerLaden()
            _filamente = _db.AlleLaden()
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        karten.SuspendLayout()
        For Each k In karten.Controls.OfType(Of DruckerKarte)().ToList()
            k.Dispose()
        Next
        For Each d In drucker
            Dim karte As New DruckerKarte(d, _db)
            AddHandler karte.BearbeitenGeklickt, Sub(x) Bearbeiten(x.Kopie(), "Drucker bearbeiten")
            AddHandler karte.ProtokollGeklickt, AddressOf ProtokollZeigen
            karte.Aktualisieren(If(d.Aktiv, _ueberwachung.Status(d.Id), Nothing), _filamente)
            karten.Controls.Add(karte)
        Next
        lblLeer.Visible = drucker.Count = 0
        karten.ResumeLayout()
    End Sub

    Private Sub StatusGeaendert(druckerId As Long)
        If Not Visible Then Return
        Dim karte = karten.Controls.OfType(Of DruckerKarte)().FirstOrDefault(Function(k) k.Drucker.Id = druckerId)
        karte?.Aktualisieren(_ueberwachung.Status(druckerId), _filamente)
    End Sub

    Private Sub Bearbeiten(d As Drucker, titel As String)
        Using dlg As New DruckerDialog(d, titel)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                If dlg.Loeschen Then _db.DruckerLoeschen(d.Id) Else _db.DruckerSpeichern(d)
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try
        End Using
        _ueberwachung.Neuladen()
        Neuladen()
    End Sub

    Private Sub ProtokollZeigen(d As Drucker)
        Dim text = If(_ueberwachung.Protokoll(d.Id)?.Alles(), "")
        Using f As New Form With {.Text = $"Protokoll – {d.Name}", .Size = New Size(900, 560), .StartPosition = FormStartPosition.CenterParent,
                                  .ShowInTaskbar = False, .MinimizeBox = False}
            DialogGestalten(f)
            Dim box As New TextBox With {.Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Both, .WordWrap = False, .Dock = DockStyle.Fill,
                                         .Font = New Font("Consolas", 9.0F), .BackColor = Theme.TabelleKopf, .ForeColor = Theme.TextNormal,
                                         .Text = If(text.Length > 0, text, "(noch keine Einträge)")}
            Dim unten As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .FlowDirection = FlowDirection.RightToLeft, .Padding = New Padding(8)}
            Dim btnKopieren As New WebButton("In Zwischenablage", Theme.SymKopieren)
            AddHandler btnKopieren.Click, Sub(s, e) Clipboard.SetText(box.Text)
            unten.Controls.Add(btnKopieren)
            f.Controls.Add(box)
            f.Controls.Add(unten)
            box.SelectionStart = box.TextLength
            box.ScrollToCaret()
            f.ShowDialog(Me)
        End Using
    End Sub

End Class
