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

    Private ReadOnly _db As IDatenquelle
    Public ReadOnly Property Drucker As Drucker

    Private ReadOnly lblName As New Label With {.AutoSize = True, .Font = Theme.SchriftLogo, .ForeColor = Theme.TextStark, .Margin = New Padding(0)}
    Private ReadOnly lblTyp As New Label With {.AutoSize = True, .Font = Theme.SchriftKlein, .ForeColor = Theme.TextLeise, .Margin = New Padding(0, 2, 0, 0)}
    Private ReadOnly badge As New StatusBadge With {.Anchor = AnchorStyles.Top Or AnchorStyles.Right, .Margin = New Padding(0, 2, 0, 0)}
    Private ReadOnly lblDatei As New Label With {.AutoSize = False, .AutoEllipsis = True, .Dock = DockStyle.Fill, .Height = 22,
                                                 .Font = Theme.SchriftFett, .ForeColor = Theme.TextNormal, .Margin = New Padding(0, 12, 0, 4)}
    Private ReadOnly balken As New Fortschrittsbalken With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 4)}
    Private ReadOnly lblFortschritt As New Label With {.AutoSize = True, .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein, .Margin = New Padding(0, 0, 0, 8)}
    Private ReadOnly lblPhase As New Label With {.AutoSize = True, .ForeColor = Theme.AkzentText, .Font = Theme.SchriftFett,
                                                 .Margin = New Padding(0, 0, 0, 6), .Visible = False}
    Private ReadOnly lblMeldung As New Label With {.AutoSize = True, .MaximumSize = New Size(400, 0), .ForeColor = Theme.Rot,
                                                   .Font = Theme.SchriftKlein, .Margin = New Padding(0, 0, 0, 6), .Visible = False}
    Private ReadOnly temperaturen As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True, .Margin = New Padding(0, 0, 0, 6), .Visible = False}
    Private ReadOnly _temperaturWerte As New List(Of Label)
    Private _temperaturSignatur As String = ""
    Private ReadOnly spulen As New TableLayoutPanel With {.AutoSize = True, .ColumnCount = 2, .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 4)}
    Private _fachSignatur As String = Nothing
    Private _filamente As New List(Of Filament)
    Private _aktualisiert As Boolean
    Private ReadOnly _dienst As IDruckerDienst
    Private _kamera As IKamera
    Private _kameraAn As Boolean
    Private ReadOnly kameraBild As New PictureBox With {.Dock = DockStyle.Fill, .Height = 227, .SizeMode = PictureBoxSizeMode.Zoom,
                                                         .BackColor = Color.Black, .Margin = New Padding(0, 8, 0, 2), .Visible = False}
    Private ReadOnly lblKamera As New Label With {.AutoSize = True, .MaximumSize = New Size(400, 0), .ForeColor = Theme.TextLeise,
                                                  .Font = Theme.SchriftKlein, .Margin = New Padding(0, 0, 0, 6), .Visible = False}
    Private btnKamera As WebButton

    Public Event BearbeitenGeklickt(d As Drucker)
    Public Event ProtokollGeklickt(d As Drucker)

    Public Sub New(d As Drucker, db As IDatenquelle, dienst As IDruckerDienst)
        _Drucker = d
        _db = db
        _dienst = dienst
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
        t.Controls.Add(lblPhase)
        t.Controls.Add(temperaturen)
        t.Controls.Add(lblMeldung)
        spulen.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        spulen.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        t.Controls.Add(spulen)
        t.Controls.Add(kameraBild)
        t.Controls.Add(lblKamera)

        Dim knoepfe As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 10, 0, 0)}
        Dim btnBearbeiten As New WebButton("Bearbeiten", Theme.SymBearbeiten)
        Dim btnProtokoll As New WebButton("Protokoll", Theme.SymSpalten)
        AddHandler btnBearbeiten.Click, Sub(s, e) RaiseEvent BearbeitenGeklickt(Drucker)
        AddHandler btnProtokoll.Click, Sub(s, e) RaiseEvent ProtokollGeklickt(Drucker)
        knoepfe.Controls.Add(btnBearbeiten)
        knoepfe.Controls.Add(btnProtokoll)
        If d.Typ = Drucker.TypBambu Then
            btnKamera = New WebButton("Kamera", Theme.SymKamera)
            AddHandler btnKamera.Click, Sub(s, e) KameraUmschalten()
            knoepfe.Controls.Add(btnKamera)
        End If
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
            SpulenAufbauen(Nothing)
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
        Dim phase = s.PhaseText()
        lblPhase.Text = phase
        lblPhase.Visible = phase.Length > 0
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
        WerteAnzeigen(s)
        lblMeldung.Text = s.Meldung
        lblMeldung.Visible = s.Zustand = DruckerZustand.Offline AndAlso s.Meldung.Length > 0
        SpulenAufbauen(s)
    End Sub

    ''' <summary>
    ''' Zeile mit Düse / Bett / Kammer und der aktuellen Schicht: Beschriftung leise, Wert fett;
    ''' Temperaturen beim Aufheizen in Gelb mit Pfeil.
    ''' </summary>
    Private Sub WerteAnzeigen(s As DruckerStatus)
        Dim werte As New List(Of (Name As String, Text As String, Farbe As Color))
        If s.Zustand <> DruckerZustand.Offline Then
            For Each x In s.Temperaturen
                werte.Add((x.Name, If(x.HeiztAuf, If(x.Ist < x.Soll.Value, "↑ ", "↓ "), "") & x.WertText,
                           If(x.HeiztAuf, Theme.Gelb, If(x.Heizt, Theme.TextStark, Theme.TextLeise))))
            Next
            If s.DruckLaeuft AndAlso s.Schicht.HasValue AndAlso s.Schicht.Value > 0 Then
                werte.Add(("Schicht", If(s.SchichtenGesamt.GetValueOrDefault() > 0, $"{s.Schicht.Value} / {s.SchichtenGesamt.Value}", CStr(s.Schicht.Value)), Theme.TextStark))
            End If
        End If

        Dim signatur = String.Join("|", werte.Select(Function(x) x.Name))
        If signatur <> _temperaturSignatur Then
            _temperaturSignatur = signatur
            temperaturen.SuspendLayout()
            For Each c As Control In temperaturen.Controls.Cast(Of Control)().ToList()
                c.Dispose()
            Next
            temperaturen.Controls.Clear()
            _temperaturWerte.Clear()
            For Each x In werte
                temperaturen.Controls.Add(New Label With {.Text = x.Name, .AutoSize = True, .Font = Theme.SchriftKlein, .ForeColor = Theme.TextLeise,
                                                          .Margin = New Padding(0, 2, 4, 0)})
                Dim wert As New Label With {.AutoSize = True, .Font = Theme.SchriftFett, .Margin = New Padding(0, 0, 18, 0)}
                temperaturen.Controls.Add(wert)
                _temperaturWerte.Add(wert)
            Next
            temperaturen.ResumeLayout()
        End If
        For i = 0 To werte.Count - 1
            _temperaturWerte(i).Text = werte(i).Text
            _temperaturWerte(i).ForeColor = werte(i).Farbe
        Next
        temperaturen.Visible = werte.Count > 0
    End Sub

    ''' <summary>
    ''' Zuordnung Fach → Spule, mit Vorschlag nach Material und Farbe für Fächer ohne Zuordnung.
    ''' Zuordnung und Vorschläge liefert die Überwachung im Status; neu aufgebaut wird nur bei Änderungen.
    ''' </summary>
    Private Sub SpulenAufbauen(s As DruckerStatus)
        Dim faecher = If(s?.Faecher, New List(Of DruckerFach))
        Dim aktivesFach = s?.Slot
        Dim liste = If(faecher.Count > 0, faecher, New List(Of DruckerFach) From {New DruckerFach With {.Slot = Drucker.SlotStandard, .Beschreibung = "Spule"}})
        Dim zuordnung As Dictionary(Of Integer, Long) = s?.Zuordnung
        If zuordnung Is Nothing Then
            Try
                zuordnung = _db.SpulenZuordnung(Drucker.Id)
            Catch ex As Exception
                zuordnung = New Dictionary(Of Integer, Long)
            End Try
        End If
        Dim vorschlaege = If(s?.Vorschlaege, New Dictionary(Of Integer, Long))
        Dim signatur = String.Join("|", liste.Select(Function(f) $"{f.Slot}:{f.Beschreibung}:{f.Rest}")) & "#" & _filamente.Count & "#" & aktivesFach &
                       "#" & String.Join(",", zuordnung.OrderBy(Function(p) p.Key).Select(Function(p) $"{p.Key}={p.Value}")) &
                       "#" & String.Join(",", vorschlaege.OrderBy(Function(p) p.Key).Select(Function(p) $"{p.Key}={p.Value}"))
        If signatur = _fachSignatur Then Return
        _fachSignatur = signatur

        spulen.SuspendLayout()
        For Each c As Control In spulen.Controls.Cast(Of Control)().ToList()
            c.Dispose()
        Next
        spulen.Controls.Clear()
        spulen.RowCount = 0
        For Each fach In liste
            Dim slot = fach.Slot
            Dim aktiv = aktivesFach.HasValue AndAlso aktivesFach.Value = slot
            Dim beschriftung As New Label With {.Text = fach.Beschreibung & If(fach.Rest.HasValue, $" · {fach.Rest.Value} %", "") & If(aktiv, "  ●", ""),
                                                .AutoSize = True, .ForeColor = If(aktiv, Theme.AkzentText, Theme.TextLeise),
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
            Dim zuordnen = Sub(filamentId As Long?)
                               Try
                                   _db.SpuleZuordnen(Drucker.Id, slot, filamentId)
                                   ' Bis die Überwachung neu abfragt, die eigene Änderung im Status vormerken
                                   If s IsNot Nothing Then
                                       If filamentId.HasValue Then s.Zuordnung(slot) = filamentId.Value Else s.Zuordnung.Remove(slot)
                                       s.Vorschlaege.Remove(slot)
                                   End If
                               Catch ex As Exception
                                   MessageBox.Show(FindForm(), ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
                               End Try
                           End Sub
            AddHandler auswahl.SelectionChangeCommitted, Sub(x, y)
                                                             Dim index = auswahl.SelectedIndex
                                                             zuordnen(If(index <= 0, CType(Nothing, Long?), angeboten(index - 1).Id))
                                                         End Sub
            spulen.Controls.Add(beschriftung, 0, spulen.RowCount)
            spulen.Controls.Add(auswahl, 1, spulen.RowCount)
            spulen.RowCount += 1

            ' Vorschlag nach Material und Farbe (nur für Fächer ohne Zuordnung)
            Dim vorschlagId As Long = 0
            Dim vorschlag = If(zugeordnet = 0 AndAlso vorschlaege.TryGetValue(slot, vorschlagId), _filamente.FirstOrDefault(Function(f) f.Id = vorschlagId), Nothing)
            If vorschlag IsNot Nothing Then
                Dim zeile As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 0, 0, 4)}
                zeile.Controls.Add(New Label With {.Text = $"Vorschlag: {vorschlag.Code} · {vorschlag.Hersteller} {vorschlag.Material} {vorschlag.Farbe}",
                                                   .AutoSize = True, .Font = Theme.SchriftKlein, .ForeColor = Theme.AkzentText, .Margin = New Padding(0, 6, 8, 0)})
                Dim btn As New WebButton("Übernehmen") With {.Margin = New Padding(0)}
                AddHandler btn.Click, Sub(x, y)
                                          zuordnen(vorschlag.Id)
                                          auswahl.SelectedIndex = angeboten.FindIndex(Function(f) f.Id = vorschlag.Id) + 1
                                          zeile.Visible = False
                                      End Sub
                zeile.Controls.Add(btn)
                spulen.Controls.Add(zeile, 1, spulen.RowCount)
                spulen.RowCount += 1
            End If
        Next
        spulen.ResumeLayout()
    End Sub

    ' ---------- Kamera (Bambu) ----------

    Private Sub KameraUmschalten()
        _kameraAn = Not _kameraAn
        btnKamera.Text = If(_kameraAn, "Kamera aus", "Kamera")
        kameraBild.Visible = _kameraAn
        lblKamera.Visible = _kameraAn
        If _kameraAn Then
            lblKamera.Text = "Verbinde …"
            KameraStarten()
        Else
            KameraStoppen()
            Dim alt = kameraBild.Image
            kameraBild.Image = Nothing
            alt?.Dispose()
        End If
    End Sub

    Private Sub KameraStarten()
        If _kamera Is Nothing Then
            _kamera = _dienst.KameraErstellen(Drucker)
            AddHandler _kamera.BildEmpfangen, AddressOf KameraBildEmpfangen
            AddHandler _kamera.StatusGeaendert, AddressOf KameraStatusGeaendert
        End If
        _kamera.Starten()
    End Sub

    Private Sub KameraStoppen()
        _kamera?.Stoppen()
    End Sub

    ''' <summary>Aus dem Hintergrund-Thread: Bild dort dekodieren, dann im UI-Thread anzeigen.</summary>
    Private Sub KameraBildEmpfangen(jpeg As Byte())
        Dim bild As Bitmap
        Try
            Using ms As New IO.MemoryStream(jpeg), roh = Image.FromStream(ms)
                bild = New Bitmap(roh)
            End Using
        Catch ex As Exception
            Return   ' unvollständiges Bild – überspringen
        End Try
        If IsDisposed OrElse Not IsHandleCreated Then
            bild.Dispose()
            Return
        End If
        UiAusfuehren(Sub()
                        If IsDisposed OrElse Not _kameraAn Then
                            bild.Dispose()
                            Return
                        End If
                        Dim alt = kameraBild.Image
                        kameraBild.Image = bild
                        alt?.Dispose()
                        lblKamera.Text = $"Live · {bild.Width} × {bild.Height} · {Date.Now:HH:mm:ss}"
                    End Sub)
    End Sub

    Private Sub KameraStatusGeaendert(text As String)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        UiAusfuehren(Sub()
                        If Not IsDisposed AndAlso _kameraAn Then lblKamera.Text = text
                    End Sub)
    End Sub

    Private Sub UiAusfuehren(aktion As Action)
        Try
            BeginInvoke(aktion)
        Catch ex As InvalidOperationException
            ' Karte wurde inzwischen geschlossen
        End Try
    End Sub

    ''' <summary>Kamera nur laufen lassen, solange die Karte sichtbar ist (z. B. nicht auf einer anderen Seite).</summary>
    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        MyBase.OnVisibleChanged(e)
        If Not _kameraAn Then Return
        If Visible Then KameraStarten() Else KameraStoppen()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _kamera?.Dispose()
            _kamera = Nothing
            kameraBild.Image?.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class

''' <summary>
''' Seite "Drucker": angebundene Drucker mit Live-Status und Spulen-Zuordnung.
''' </summary>
Public Class DruckerSeite
    Inherits UserControl
    Implements ISeite

    Private ReadOnly _db As IDatenquelle
    Private ReadOnly _ueberwachung As IDruckerDienst
    Private ReadOnly karten As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Margin = New Padding(0), .Padding = New Padding(0, 0, 0, 8)}
    Private ReadOnly lblLeer As New Label With {.AutoSize = True, .ForeColor = Theme.TextLeise, .MaximumSize = New Size(640, 0), .Margin = New Padding(0, 8, 0, 0),
        .Text = "Noch kein Drucker angebunden. Mit ""Drucker hinzufügen"" verbindest du Bambu Lab, Prusa (PrusaLink), Klipper (Moonraker) " &
                "oder OctoPrint. Ist ein Druck fertig, schlägt die App vor, den Verbrauch zu buchen."}
    Private _filamente As New List(Of Filament)
    Private ReadOnly btnWartung As New WebButton("Wartung", Theme.SymWartung) With {.Margin = New Padding(0, 0, 8, 0)}

    ''' <summary>Im Wartungsfenster wurde etwas geändert (Zahl am Menüpunkt neu berechnen).</summary>
    Public Event WartungGeaendert()

    Public Sub New(db As IDatenquelle, ueberwachung As IDruckerDienst)
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
        AddHandler btnWartung.Click, Sub(s, e) WartungZeigen()
        kopf.Knoepfe.Controls.Add(btnWartung)
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
            Dim karte As New DruckerKarte(d, _db, _ueberwachung)
            AddHandler karte.BearbeitenGeklickt, Sub(x) Bearbeiten(x.Kopie(), "Drucker bearbeiten")
            AddHandler karte.ProtokollGeklickt, AddressOf ProtokollZeigen
            karte.Aktualisieren(If(d.Aktiv, _ueberwachung.Status(d.Id), Nothing), _filamente)
            karten.Controls.Add(karte)
        Next
        lblLeer.Visible = drucker.Count = 0
        karten.ResumeLayout()
        WartungKnopfAktualisieren()
    End Sub

    ''' <summary>"Wartung (2 fällig)"; bei einem älteren Server ohne Wartung ausgeblendet.</summary>
    Private Sub WartungKnopfAktualisieren()
        Try
            Dim faellig = _db.WartungLaden().Where(Function(w) w.Faellig).Count()
            btnWartung.Text = If(faellig > 0, $"Wartung ({faellig} fällig)", "Wartung")
            btnWartung.Stil = If(faellig > 0, KnopfStil.Gefahr, KnopfStil.Sekundaer)
            btnWartung.Visible = True
        Catch ex As Exception When ex.Message = ServerDatenquelle.ServerZuAlt
            btnWartung.Visible = False
        Catch ex As Exception
            btnWartung.Text = "Wartung"
        End Try
        btnWartung.Parent?.PerformLayout()
    End Sub

    Private Sub WartungZeigen()
        Using dlg As New WartungDialog(_db)
            dlg.ShowDialog(Me)
            If dlg.Geaendert Then
                Neuladen()
                RaiseEvent WartungGeaendert()
            End If
        End Using
    End Sub
    Private Sub StatusGeaendert(druckerId As Long)
        If Not Visible Then Return
        Dim karte = karten.Controls.OfType(Of DruckerKarte)().FirstOrDefault(Function(k) k.Drucker.Id = druckerId)
        karte?.Aktualisieren(_ueberwachung.Status(druckerId), _filamente)
    End Sub

    Private Sub Bearbeiten(d As Drucker, titel As String)
        Using dlg As New DruckerDialog(d, titel, _ueberwachung)
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
        Dim text = _ueberwachung.ProtokollText(d.Id)
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
