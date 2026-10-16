Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' Einheitliches Aussehen aller aufklappenden Menüs (Einstellungen, Design, Rechtsklick in Tabellen …):
''' Kartenfarbe statt Windows-Grau, abgerundete Ecken mit feinem Rahmen und Schatten, mehr Abstand,
''' abgerundete Hervorhebung beim Überfahren und ein Häkchen in der Akzentfarbe.
''' Wird in Program.vb einmal als <see cref="ToolStripManager.Renderer"/> gesetzt und gilt dann für alle Menüs.
''' </summary>
Public Class MenueRenderer
    Inherits ToolStripProfessionalRenderer

    Public Sub New()
        MyBase.New(New Farben())
        RoundedEdges = False
    End Sub

    ' ------------------------------------------------------------------ Abstände

    Protected Overrides Sub Initialize(ts As ToolStrip)
        MyBase.Initialize(ts)
        Dim dd = TryCast(ts, ToolStripDropDownMenu)
        If dd Is Nothing Then Return
        dd.Padding = New Padding(4, 6, 4, 6)
        dd.DropShadowEnabled = True
        ' Abgerundete Ecken über die Fensterform (Windows rundet Menüfenster nicht selbst); Größe kann sich je Öffnen ändern
        AddHandler dd.SizeChanged, Sub(s, e) FormRunden(dd)
        AddHandler dd.Opening, Sub(s, e) FormRunden(dd)
    End Sub

    Private Shared Sub FormRunden(dd As ToolStripDropDown)
        If dd.Width <= 0 OrElse dd.Height <= 0 Then Return
        Using pfad = Abgerundet(New Rectangle(0, 0, dd.Width, dd.Height), Px(dd, 7))
            dd.Region = New Region(pfad)
        End Using
    End Sub

    Protected Overrides Sub InitializeItem(item As ToolStripItem)
        MyBase.InitializeItem(item)
        If TypeOf item Is ToolStripMenuItem AndAlso TypeOf item.Owner Is ToolStripDropDownMenu Then
            item.Padding = New Padding(4, 7, 8, 7)
        End If
    End Sub

    ' ------------------------------------------------------------------ Zeichnen

    Protected Overrides Sub OnRenderToolStripBackground(e As ToolStripRenderEventArgs)
        If TypeOf e.ToolStrip Is ToolStripDropDown Then
            Using b As New SolidBrush(Theme.KartenFlaeche)
                e.Graphics.FillRectangle(b, e.AffectedBounds)
            End Using
        Else
            MyBase.OnRenderToolStripBackground(e)
        End If
    End Sub

    Protected Overrides Sub OnRenderToolStripBorder(e As ToolStripRenderEventArgs)
        If TypeOf e.ToolStrip Is ToolStripDropDown Then
            ' Feiner Rahmen entlang der abgerundeten Form
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
            Using pfad = Abgerundet(New Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), Px(e.ToolStrip, 7)),
                  p As New Pen(Theme.RandEingabe)
                e.Graphics.DrawPath(p, pfad)
            End Using
        Else
            MyBase.OnRenderToolStripBorder(e)
        End If
    End Sub

    Protected Overrides Sub OnRenderImageMargin(e As ToolStripRenderEventArgs)
        ' Kein grauer Streifen links – Häkchen und Symbole stehen auf der Kartenfläche
    End Sub

    Protected Overrides Sub OnRenderMenuItemBackground(e As ToolStripItemRenderEventArgs)
        If Not (TypeOf e.ToolStrip Is ToolStripDropDown) Then
            MyBase.OnRenderMenuItemBackground(e)
            Return
        End If
        If Not e.Item.Selected OrElse Not e.Item.Enabled Then Return
        ' In Menü-Koordinaten rechnen: links und rechts gleich weit vom Fensterrand
        ' (Item-Breite und Padding enthalten je nach Menü den Rand für Häkchen bzw. reichen bis an den Rand)
        Dim abstand = Px(e.ToolStrip, 6)
        Dim r As New Rectangle(abstand - e.Item.Bounds.X, 1, e.ToolStrip.Width - 2 * abstand, e.Item.Height - 2)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Using pfad = Abgerundet(r, Px(e.ToolStrip, 5)), b As New SolidBrush(Theme.Auswahl)
            e.Graphics.FillPath(b, pfad)
        End Using
    End Sub

    Protected Overrides Sub OnRenderItemText(e As ToolStripItemTextRenderEventArgs)
        If TypeOf e.ToolStrip Is ToolStripDropDown Then
            ' Eigene Farbe behalten (z. B. rotes "Löschen"), sonst Theme-Text
            If Not e.Item.Enabled Then
                e.TextColor = Theme.TextLeise
            ElseIf e.Item.ForeColor <> Theme.Rot Then
                e.TextColor = If(e.Item.Selected, Theme.TextStark, Theme.TextNormal)
            End If
        End If
        MyBase.OnRenderItemText(e)
    End Sub

    Protected Overrides Sub OnRenderItemCheck(e As ToolStripItemImageRenderEventArgs)
        ' Häkchen als Symbol in der Akzentfarbe statt des Windows-Kästchens
        Dim r = e.ImageRectangle
        r.Inflate(Px(e.ToolStrip, 3), Px(e.ToolStrip, 3))
        TextRenderer.DrawText(e.Graphics, ChrW(&HE73E), Theme.SymbolSchrift, r, Theme.Akzent,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
    End Sub

    Protected Overrides Sub OnRenderSeparator(e As ToolStripSeparatorRenderEventArgs)
        If Not (TypeOf e.ToolStrip Is ToolStripDropDown) Then
            MyBase.OnRenderSeparator(e)
            Return
        End If
        Dim y = e.Item.Height \ 2
        Using p As New Pen(Theme.Rand)
            e.Graphics.DrawLine(p, Px(e.ToolStrip, 10), y, e.Item.Width - Px(e.ToolStrip, 10), y)
        End Using
    End Sub

    Protected Overrides Sub OnRenderArrow(e As ToolStripArrowRenderEventArgs)
        e.ArrowColor = If(e.Item IsNot Nothing AndAlso e.Item.Selected, Theme.TextStark, Theme.TextLeise)
        MyBase.OnRenderArrow(e)
    End Sub

    ''' <summary>Farben für die Teile, die der Basis-Renderer selbst zeichnet.</summary>
    Private Class Farben
        Inherits ProfessionalColorTable

        Public Sub New()
            UseSystemColors = False
        End Sub

        Public Overrides ReadOnly Property ToolStripDropDownBackground As Color
            Get
                Return Theme.KartenFlaeche
            End Get
        End Property

        Public Overrides ReadOnly Property MenuBorder As Color
            Get
                Return Theme.Rand
            End Get
        End Property

        Public Overrides ReadOnly Property MenuItemBorder As Color
            Get
                Return Color.Transparent
            End Get
        End Property

        Public Overrides ReadOnly Property SeparatorDark As Color
            Get
                Return Theme.Rand
            End Get
        End Property

        Public Overrides ReadOnly Property SeparatorLight As Color
            Get
                Return Theme.Rand
            End Get
        End Property
    End Class

End Class
