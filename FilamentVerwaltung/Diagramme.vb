Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>Ein Datenpunkt für Säulen- und Balkendiagramme.</summary>
Public Class DiagrammWert
    ''' <summary>Kurze Beschriftung an der Achse (z. B. "Okt").</summary>
    Public Property Beschriftung As String = ""
    ''' <summary>Ausführlicher Titel für Tooltip und Tabelle (z. B. "Oktober 2026").</summary>
    Public Property Titel As String = ""
    Public Property Wert As Double
    ''' <summary>Formatierter Wert (z. B. "1,2 kg").</summary>
    Public Property WertText As String = ""
    ''' <summary>Weitere Angaben für Tooltip und Tabelle (z. B. Kosten, Anzahl Drucke).</summary>
    Public Property Zusatz As String = ""
    ''' <summary>Wert direkt am Balken beschriften (sparsam: Maximum, aktueller Monat).</summary>
    Public Property Hervorheben As Boolean
End Class

''' <summary>
''' Gemeinsame Basis: einfarbige Datenreihe, Hover-Tooltip, Leerzustand.
''' Regeln: dünne Balken (≤ 24 px) mit 4 px gerundetem Ende, feine Hilfslinien, Text nie in der Datenfarbe.
''' </summary>
Public MustInherit Class DiagrammBasis
    Inherits Control

    Protected _werte As New List(Of DiagrammWert)
    Protected _hover As Integer = -1
    ''' <summary>Farbe der Datenreihe (validiert: Kontrast und Helligkeit auf Weiß).</summary>
    Protected ReadOnly Farbe As Color = Theme.Akzent
    Protected ReadOnly FarbeHover As Color = Theme.AkzentGedrueckt

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw, True)
        BackColor = Color.White
        Font = Theme.SchriftKlein
    End Sub

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property LeerText As String = "Noch keine Daten"

    Public Sub Setzen(werte As IEnumerable(Of DiagrammWert))
        _werte = werte.ToList()
        _hover = -1
        Invalidate()
    End Sub

    Protected ReadOnly Property HatDaten As Boolean
        Get
            Return _werte.Count > 0 AndAlso _werte.Any(Function(w) w.Wert > 0)
        End Get
    End Property

    ''' <summary>Index des Datenpunkts unter der Maus (-1 = keiner).</summary>
    Protected MustOverride Function TrefferIndex(p As Point) As Integer

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim neu = If(HatDaten, TrefferIndex(e.Location), -1)
        If neu <> _hover Then
            _hover = neu
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        If _hover <> -1 Then
            _hover = -1
            Invalidate()
        End If
    End Sub

    Protected Sub LeerZeichnen(g As Graphics)
        TextRenderer.DrawText(g, LeerText, Theme.Schrift, ClientRectangle, Theme.TextLeise,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
    End Sub

    ''' <summary>Tooltip-Karte am Ankerpunkt (bleibt innerhalb des Steuerelements).</summary>
    Protected Sub TooltipZeichnen(g As Graphics, anker As Point, w As DiagrammWert)
        Dim zeilen As New List(Of (Text As String, Schrift As Font, Farbe As Color)) From {
            (w.Titel, Theme.SchriftFett, Theme.TextStark),
            (w.WertText, Theme.Schrift, Theme.TextNormal)
        }
        For Each z In w.Zusatz.Split({" · "}, StringSplitOptions.RemoveEmptyEntries)
            zeilen.Add((z, Theme.SchriftKlein, Theme.TextLeise))
        Next
        Dim pad = Px(Me, 10)
        Dim breite = 0, hoehe = 0
        For Each z In zeilen
            Dim s = TextRenderer.MeasureText(g, z.Text, z.Schrift, Size.Empty, TextFormatFlags.NoPadding)
            breite = Math.Max(breite, s.Width)
            hoehe += s.Height + Px(Me, 2)
        Next
        Dim r As New Rectangle(anker.X + Px(Me, 12), anker.Y - hoehe \ 2 - pad, breite + 2 * pad, hoehe + 2 * pad)
        If r.Right > Width - 2 Then r.X = anker.X - Px(Me, 12) - r.Width
        r.X = Math.Max(2, r.X)
        r.Y = Math.Max(2, Math.Min(Height - r.Height - 2, r.Y))

        g.SmoothingMode = SmoothingMode.AntiAlias
        Using schatten = Abgerundet(New Rectangle(r.X, r.Y + 2, r.Width, r.Height), Px(Me, 8)), b As New SolidBrush(Color.FromArgb(25, 15, 23, 42))
            g.FillPath(b, schatten)
        End Using
        Using pfad = Abgerundet(r, Px(Me, 8)), stift As New Pen(Theme.Rand)
            g.FillPath(Brushes.White, pfad)
            g.DrawPath(stift, pfad)
        End Using
        Dim y = r.Y + pad
        For Each z In zeilen
            TextRenderer.DrawText(g, z.Text, z.Schrift, New Point(r.X + pad, y), z.Farbe, TextFormatFlags.NoPadding)
            y += TextRenderer.MeasureText(g, z.Text, z.Schrift, Size.Empty, TextFormatFlags.NoPadding).Height + Px(Me, 2)
        Next
    End Sub

    ''' <summary>Rechteck mit 4 px gerundetem Datenende (oben bzw. rechts), eckig an der Grundlinie.</summary>
    Protected Function BalkenPfad(r As RectangleF, endeOben As Boolean) As GraphicsPath
        Dim p As New GraphicsPath()
        Dim radius = Math.Min(Px(Me, 4), If(endeOben, Math.Min(r.Width / 2, r.Height), Math.Min(r.Height / 2, r.Width)))
        If radius < 1 Then
            p.AddRectangle(r)
            Return p
        End If
        Dim d = radius * 2
        If endeOben Then
            p.AddLine(r.Left, r.Bottom, r.Left, r.Top + radius)
            p.AddArc(r.Left, r.Top, d, d, 180, 90)
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90)
            p.AddLine(r.Right, r.Top + radius, r.Right, r.Bottom)
        Else
            p.AddLine(r.Left, r.Top, r.Right - radius, r.Top)
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90)
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
            p.AddLine(r.Right - radius, r.Bottom, r.Left, r.Bottom)
        End If
        p.CloseFigure()
        Return p
    End Function

    ''' <summary>Runde Achsenschritte (1/2/5 × 10ⁿ) mit etwa vier Linien.</summary>
    Protected Shared Function Schrittweite(maximum As Double) As Double
        If maximum <= 0 Then Return 1
        Dim roh = maximum / 4
        Dim basis = Math.Pow(10, Math.Floor(Math.Log10(roh)))
        For Each f In {1.0, 2.0, 2.5, 5.0, 10.0}
            If f * basis >= roh Then Return f * basis
        Next
        Return 10 * basis
    End Function

    ''' <summary>Gramm kompakt: unter 1000 in g, darüber in kg.</summary>
    Public Shared Function GrammKompakt(gramm As Double) As String
        If Math.Abs(gramm) >= 1000 Then Return (gramm / 1000).ToString(If(gramm Mod 1000 = 0, "N0", "N1")) & " kg"
        Return gramm.ToString("N0") & " g"
    End Function

End Class

''' <summary>Säulendiagramm (Zeitreihe, z. B. Verbrauch pro Monat).</summary>
Public Class SaeulenDiagramm
    Inherits DiagrammBasis

    Private _plot As Rectangle

    Private Function Bereich(g As Graphics, maximum As Double, schritt As Double) As Rectangle
        Dim yText = Enumerable.Range(0, CInt(Math.Ceiling(maximum / schritt)) + 1).
                    Select(Function(i) TextRenderer.MeasureText(g, GrammKompakt(i * schritt), Font).Width).Max()
        Dim links = yText + Px(Me, 10)
        Dim oben = Px(Me, 26)
        Dim unten = Px(Me, 26)
        Return New Rectangle(links, oben, Math.Max(10, Width - links - Px(Me, 8)), Math.Max(10, Height - oben - unten))
    End Function

    Protected Overrides Function TrefferIndex(p As Point) As Integer
        If _werte.Count = 0 OrElse _plot.Width <= 0 OrElse p.X < _plot.Left OrElse p.X > _plot.Right Then Return -1
        Return Math.Min(_werte.Count - 1, CInt(Math.Floor((p.X - _plot.Left) / (_plot.Width / _werte.Count))))
    End Function

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        If Not HatDaten Then
            LeerZeichnen(g)
            Return
        End If
        Dim maximum = _werte.Max(Function(w) w.Wert)
        Dim schritt = Schrittweite(maximum)
        Dim oberkante = Math.Ceiling(maximum / schritt) * schritt
        _plot = Bereich(g, oberkante, schritt)

        ' Hilfslinien und y-Achse (fein, durchgezogen, zurückhaltend)
        Using linie As New Pen(Theme.TabelleLinie)
            Dim wert = 0.0
            While wert <= oberkante + 0.001
                Dim y = _plot.Bottom - CInt(wert / oberkante * _plot.Height)
                g.DrawLine(linie, _plot.Left, y, _plot.Right, y)
                TextRenderer.DrawText(g, GrammKompakt(wert), Font, New Rectangle(0, y - 10, _plot.Left - Px(Me, 8), 20), Theme.TextLeise,
                                      TextFormatFlags.Right Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
                wert += schritt
            End While
        End Using

        Dim band = _plot.Width / CSng(_werte.Count)
        Dim saeule = Math.Min(Px(Me, 24), band * 0.6F)
        g.SmoothingMode = SmoothingMode.AntiAlias
        For i = 0 To _werte.Count - 1
            Dim w = _werte(i)
            Dim mitte = _plot.Left + band * (i + 0.5F)
            If w.Wert > 0 Then
                Dim h = CSng(w.Wert / oberkante * _plot.Height)
                Dim r As New RectangleF(mitte - saeule / 2, _plot.Bottom - h, saeule, h)
                Using pfad = BalkenPfad(r, endeOben:=True), b As New SolidBrush(If(i = _hover, FarbeHover, Farbe))
                    g.FillPath(b, pfad)
                End Using
                If w.Hervorheben AndAlso i <> _hover Then
                    TextRenderer.DrawText(g, w.WertText, Font, New Rectangle(CInt(mitte - band), CInt(r.Top) - Px(Me, 20), CInt(band * 2), Px(Me, 18)),
                                          Theme.TextNormal, TextFormatFlags.HorizontalCenter Or TextFormatFlags.Bottom Or TextFormatFlags.NoPadding)
                End If
            End If
            TextRenderer.DrawText(g, w.Beschriftung, Font, New Rectangle(CInt(mitte - band / 2), _plot.Bottom + Px(Me, 6), CInt(band), Px(Me, 18)),
                                  If(i = _hover, Theme.TextStark, Theme.TextLeise), TextFormatFlags.HorizontalCenter Or TextFormatFlags.NoPadding)
        Next
        ' Grundlinie
        Using basis As New Pen(Theme.RandEingabe)
            g.DrawLine(basis, _plot.Left, _plot.Bottom, _plot.Right, _plot.Bottom)
        End Using

        If _hover >= 0 Then
            Dim w = _werte(_hover)
            Dim mitte = CInt(_plot.Left + band * (_hover + 0.5F))
            Dim oben = _plot.Bottom - CInt(w.Wert / oberkante * _plot.Height)
            TooltipZeichnen(g, New Point(mitte, Math.Max(_plot.Top + 30, oben)), w)
        End If
    End Sub

End Class

''' <summary>Waagerechtes Balkendiagramm (Kategorien, z. B. Verbrauch nach Material). Wert steht am Balkenende.</summary>
Public Class BalkenDiagramm
    Inherits DiagrammBasis

    Private _zeilenHoehe As Single
    Private _oben As Integer

    Protected Overrides Function TrefferIndex(p As Point) As Integer
        If _werte.Count = 0 OrElse _zeilenHoehe <= 0 Then Return -1
        Dim i = CInt(Math.Floor((p.Y - _oben) / _zeilenHoehe))
        Return If(i >= 0 AndAlso i < _werte.Count, i, -1)
    End Function

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        If Not HatDaten Then
            LeerZeichnen(g)
            Return
        End If
        _oben = Px(Me, 4)
        _zeilenHoehe = Math.Min(Px(Me, 40), (Height - _oben - Px(Me, 4)) / CSng(_werte.Count))
        Dim beschriftungBreite = Math.Min(CInt(Width * 0.32), _werte.Max(Function(w) TextRenderer.MeasureText(g, w.Beschriftung, Theme.Schrift).Width) + Px(Me, 8))
        Dim wertBreite = _werte.Max(Function(w) TextRenderer.MeasureText(g, w.WertText, Font).Width) + Px(Me, 10)
        Dim links = beschriftungBreite + Px(Me, 8)
        Dim platz = Math.Max(10, Width - links - wertBreite - Px(Me, 4))
        Dim maximum = _werte.Max(Function(w) w.Wert)
        Dim dicke = Math.Min(Px(Me, 20), _zeilenHoehe * 0.5F)

        g.SmoothingMode = SmoothingMode.AntiAlias
        For i = 0 To _werte.Count - 1
            Dim w = _werte(i)
            Dim y = _oben + _zeilenHoehe * i
            If i = _hover Then
                Using b As New SolidBrush(Theme.TabelleKopf)
                    g.FillRectangle(b, 0, y, Width, _zeilenHoehe)
                End Using
            End If
            TextRenderer.DrawText(g, w.Beschriftung, Theme.Schrift, New Rectangle(0, CInt(y), beschriftungBreite, CInt(_zeilenHoehe)), Theme.TextNormal,
                                  TextFormatFlags.Right Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPadding)
            Dim laenge = CSng(If(maximum > 0, w.Wert / maximum * platz, 0))
            If laenge > 0 Then
                Dim r As New RectangleF(links, y + (_zeilenHoehe - dicke) / 2, Math.Max(2, laenge), dicke)
                Using pfad = BalkenPfad(r, endeOben:=False), b As New SolidBrush(If(i = _hover, FarbeHover, Farbe))
                    g.FillPath(b, pfad)
                End Using
            End If
            TextRenderer.DrawText(g, w.WertText, Font, New Rectangle(CInt(links + laenge) + Px(Me, 8), CInt(y), wertBreite, CInt(_zeilenHoehe)),
                                  Theme.TextNormal, TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
        Next
        ' Grundlinie
        Using basis As New Pen(Theme.RandEingabe)
            g.DrawLine(basis, links, _oben, links, _oben + _zeilenHoehe * _werte.Count)
        End Using

        If _hover >= 0 AndAlso _werte(_hover).Zusatz.Length > 0 Then
            Dim w = _werte(_hover)
            Dim ende = links + CInt(w.Wert / maximum * platz)
            TooltipZeichnen(g, New Point(Math.Min(ende, Width - Px(Me, 40)), CInt(_oben + _zeilenHoehe * (_hover + 0.5F))), w)
        End If
    End Sub

End Class

''' <summary>
''' Karte mit Titel und einem Diagramm, das sich per Knopf auf eine Tabelle umschalten lässt
''' (jedes Diagramm hat eine gleichwertige Tabellenansicht).
''' </summary>
Public Class DiagrammKarte
    Inherits Karte

    Private ReadOnly _diagramm As DiagrammBasis
    Private ReadOnly _tabelle As New WebTabelle()
    Private ReadOnly _quelle As New BindingSource()
    Private ReadOnly _umschalten As New WebButton("Tabelle", Theme.SymSpalten)
    Private ReadOnly _inhalt As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.White, .Margin = New Padding(0)}

    Public Sub New(titel As String, untertitel As String, diagramm As DiagrammBasis, wertSpalte As String)
        _diagramm = diagramm
        Dock = DockStyle.Fill
        Padding = New Padding(18, 14, 18, 12)

        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 2, .Margin = New Padding(0)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        t.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim texte As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 0, 0, 10)}
        texte.Controls.Add(New Label With {.Text = titel, .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark, .AutoSize = True, .Margin = New Padding(0)})
        texte.Controls.Add(New Label With {.Text = untertitel, .Font = Theme.SchriftKlein, .ForeColor = Theme.TextLeise, .AutoSize = True, .Margin = New Padding(0, 2, 0, 0)})
        _umschalten.Margin = New Padding(0)
        _umschalten.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        AddHandler _umschalten.Click, Sub(s, e) Umschalten()

        _tabelle.DataSource = _quelle
        _tabelle.Spalte("Titel", "Bezeichnung", 120)
        _tabelle.Spalte("WertText", wertSpalte, 0, rechtsbuendig:=True)
        _tabelle.Spalte("Zusatz", "Details", 140)
        _tabelle.Dock = DockStyle.Fill
        _tabelle.Visible = False
        diagramm.Dock = DockStyle.Fill
        _inhalt.Controls.Add(_tabelle)
        _inhalt.Controls.Add(diagramm)

        t.Controls.Add(texte, 0, 0)
        t.Controls.Add(_umschalten, 1, 0)
        t.Controls.Add(_inhalt, 0, 1)
        t.SetColumnSpan(_inhalt, 2)
        Controls.Add(t)
    End Sub

    Public Sub Setzen(werte As List(Of DiagrammWert))
        _diagramm.Setzen(werte)
        _quelle.DataSource = werte
    End Sub

    Private Sub Umschalten()
        Dim tabelleZeigen = Not _tabelle.Visible
        _tabelle.Visible = tabelleZeigen
        _diagramm.Visible = Not tabelleZeigen
        _umschalten.Text = If(tabelleZeigen, "Diagramm", "Tabelle")
        _umschalten.Symbol = If(tabelleZeigen, Theme.SymDiagramm, Theme.SymSpalten)
        _umschalten.PerformLayout()
        _umschalten.Parent?.PerformLayout()
    End Sub

End Class
