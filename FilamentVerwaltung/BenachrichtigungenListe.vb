Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Aufklappliste der Glocke unten in der Seitenleiste: die zuletzt vom Server verschickten Benachrichtigungen
''' (Druck fertig, Spule fast leer, Drucker-Fehler …), neue hervorgehoben. Klick auf einen Eintrag öffnet die passende
''' Seite; schließt sich, sobald man woanders hinklickt.
''' </summary>
Public Class BenachrichtigungenListe
    Inherits Form

    ''' <summary>Eintrag angeklickt – Ziel wie auf der Handy-Seite (z. B. "/#buchen").</summary>
    Public Event ZielGewaehlt(ziel As String)
    Public Event EinstellungenGewaehlt()

    Private ReadOnly _liste As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
                                                          .AutoScroll = True, .Padding = New Padding(0, 0, 0, 6)}

    ''' <param name="eintraege">Neueste zuerst.</param>
    ''' <param name="gelesenBis">Höchste Id, die schon angesehen wurde – neuere werden hervorgehoben.</param>
    Public Sub New(eintraege As List(Of Benachrichtigung), gelesenBis As Long)
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        FormBorderStyle = FormBorderStyle.None
        ShowInTaskbar = False
        StartPosition = FormStartPosition.Manual
        KeyPreview = True
        BackColor = Theme.Rand
        Padding = New Padding(1)
        Size = New Size(380, 460)
        Font = Theme.Schrift

        Dim flaeche As New Panel With {.Dock = DockStyle.Fill, .BackColor = Theme.KartenFlaeche}
        Dim kopf As New Panel With {.Dock = DockStyle.Top, .Height = 44, .BackColor = Theme.KartenFlaeche, .Padding = New Padding(16, 0, 12, 0)}
        kopf.Controls.Add(New Label With {.Text = "Benachrichtigungen", .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark,
                                          .AutoSize = False, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft})
        Dim einstellungen As New LinkLabel With {.Text = "Einstellungen", .AutoSize = False, .Dock = DockStyle.Right, .Width = 100,
                                                 .TextAlign = ContentAlignment.MiddleRight, .LinkColor = Theme.Akzent,
                                                 .ActiveLinkColor = Theme.AkzentHover, .LinkBehavior = LinkBehavior.HoverUnderline}
        AddHandler einstellungen.LinkClicked, Sub(s, e)
                                                  Close()
                                                  RaiseEvent EinstellungenGewaehlt()
                                              End Sub
        kopf.Controls.Add(einstellungen)
        Dim linie As New Panel With {.Dock = DockStyle.Top, .Height = 1, .BackColor = Theme.Rand}

        _liste.BackColor = Theme.KartenFlaeche
        If eintraege.Count = 0 Then
            _liste.Controls.Add(New Label With {
                .Text = "Noch keine Benachrichtigungen." & Environment.NewLine &
                        "Hier erscheinen z. B. fertige Drucke, fast leere Spulen und Fehler am Drucker.",
                .ForeColor = Theme.TextLeise, .AutoSize = True, .MaximumSize = New Size(330, 0), .Margin = New Padding(16, 16, 16, 0)})
        Else
            For Each b In eintraege
                _liste.Controls.Add(Eintrag(b, b.Id > gelesenBis))
            Next
        End If

        flaeche.Controls.Add(_liste)
        flaeche.Controls.Add(linie)
        flaeche.Controls.Add(kopf)
        Controls.Add(flaeche)
    End Sub

    Private Function Eintrag(b As Benachrichtigung, neu As Boolean) As Control
        Dim breite = 380 - 2 - SystemInformation.VerticalScrollBarWidth
        Dim p As New Panel With {.Width = breite, .Margin = New Padding(0), .Padding = New Padding(16, 10, 12, 10),
                                 .BackColor = Theme.KartenFlaeche, .Cursor = Cursors.Hand}
        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 2, .BackColor = Color.Transparent}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim titel As New Label With {.Text = b.Titel, .Font = If(neu, Theme.SchriftFett, Theme.Schrift), .ForeColor = Theme.TextStark,
                                     .AutoSize = True, .MaximumSize = New Size(breite - 120, 0), .Margin = New Padding(0)}
        Dim zeit As New Label With {.Text = ZeitText(b.Zeitpunkt), .Font = Theme.SchriftKlein, .ForeColor = If(neu, Theme.Akzent, Theme.TextLeise),
                                    .AutoSize = True, .Margin = New Padding(8, 1, 0, 0)}
        Dim text As New Label With {.Text = String.Join(" · ", b.Text.Split(vbLf).Where(Function(z) Not z.StartsWith("Antippen", StringComparison.Ordinal))), .Font = Theme.SchriftKlein, .ForeColor = Theme.TextLeise,
                                    .AutoSize = True, .MaximumSize = New Size(breite - 32, 0), .Margin = New Padding(0, 3, 0, 0)}
        t.Controls.Add(titel, 0, 0)
        t.Controls.Add(zeit, 1, 0)
        t.Controls.Add(text, 0, 1)
        t.SetColumnSpan(text, 2)
        p.Controls.Add(t)
        ' Neue Einträge: Akzentstreifen links
        If neu Then AddHandler p.Paint, Sub(s, e)
                                            Using br As New SolidBrush(Theme.Akzent)
                                                e.Graphics.FillRectangle(br, 0, Px(p, 10), Px(p, 3), p.Height - Px(p, 20))
                                            End Using
                                        End Sub
        AddHandler p.Layout, Sub(s, e) p.Height = t.PreferredSize.Height + p.Padding.Vertical + 1
        ' Trennlinie unten
        AddHandler p.Paint, Sub(s, e)
                                Using stift As New Pen(Theme.TabelleLinie)
                                    e.Graphics.DrawLine(stift, Px(p, 16), p.Height - 1, p.Width, p.Height - 1)
                                End Using
                            End Sub

        ' Ganze Fläche klickbar, mit Hover
        Dim ziel = b.Ziel
        For Each c In New Control() {p, t, titel, zeit, text}
            c.Cursor = Cursors.Hand
            AddHandler c.Click, Sub(s, e)
                                    Close()
                                    RaiseEvent ZielGewaehlt(ziel)
                                End Sub
            AddHandler c.MouseEnter, Sub(s, e) p.BackColor = Theme.TabelleKopf
            AddHandler c.MouseLeave, Sub(s, e)
                                         If Not p.ClientRectangle.Contains(p.PointToClient(Cursor.Position)) Then p.BackColor = Theme.KartenFlaeche
                                     End Sub
        Next
        Return p
    End Function

    Private Shared Function ZeitText(z As Date) As String
        Dim lokal = z.ToLocalTime()
        If lokal.Date = Date.Today Then Return lokal.ToString("HH:mm")
        If lokal.Date = Date.Today.AddDays(-1) Then Return "gestern " & lokal.ToString("HH:mm")
        Return lokal.ToString("dd.MM. HH:mm")
    End Function

    ''' <summary>Über dem Anker (Glocke) öffnen, links bündig, innerhalb des Bildschirms.</summary>
    Public Sub ZeigenUeber(anker As Control)
        Dim punkt = anker.PointToScreen(Point.Empty)
        Dim bereich = Screen.FromControl(anker).WorkingArea
        Dim x = Math.Max(bereich.Left, Math.Min(punkt.X - Px(anker, 16), bereich.Right - Width))
        Dim y = Math.Max(bereich.Top, punkt.Y - Height - Px(anker, 6))
        Location = New Point(x, y)
        Show(anker.FindForm())
    End Sub

    Protected Overrides Sub OnDeactivate(e As EventArgs)
        MyBase.OnDeactivate(e)
        Close()
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If e.KeyCode = Keys.Escape Then Close()
    End Sub

End Class
