Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

Public Enum KnopfStil
    Primaer
    Sekundaer
    Gefahr
    Navigation
End Enum

''' <summary>
''' Flacher Knopf mit abgerundeten Ecken und optionalem Symbol – wie ein Button auf einer Webseite.
''' Erbt von Button, damit DialogResult, AcceptButton und Tastaturbedienung weiter funktionieren.
''' </summary>
Public Class WebButton
    Inherits Button

    Private _hover As Boolean
    Private _gedrueckt As Boolean
    Private _aktiv As Boolean
    Private _symbol As String = ""

    Public Sub New(beschriftung As String, Optional symbol As String = "", Optional stil As KnopfStil = KnopfStil.Sekundaer)
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        FlatStyle = FlatStyle.Flat
        FlatAppearance.BorderSize = 0
        Cursor = Cursors.Hand
        Font = If(stil = KnopfStil.Navigation, Theme.Schrift, Theme.SchriftFett)
        Me.Stil = stil
        _symbol = symbol
        Text = beschriftung
        AutoSize = stil <> KnopfStil.Navigation
        Margin = New Padding(0, 0, 8, 0)
    End Sub

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Stil As KnopfStil

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Symbol As String
        Get
            Return _symbol
        End Get
        Set(value As String)
            _symbol = If(value, "")
            Invalidate()
        End Set
    End Property

    ''' <summary>Markiert den aktuell gewählten Eintrag in der Navigation.</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Aktiv As Boolean
        Get
            Return _aktiv
        End Get
        Set(value As Boolean)
            _aktiv = value
            Invalidate()
        End Set
    End Property

    Private _unterpunkt As Boolean
    Private _zahl As String = ""

    ''' <summary>Navigation: eingerückter Unterpunkt (Text bündig mit dem Text des übergeordneten Eintrags).</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Unterpunkt As Boolean
        Get
            Return _unterpunkt
        End Get
        Set(value As Boolean)
            _unterpunkt = value
            Invalidate()
        End Set
    End Property

    ''' <summary>Navigation: Zahl rechtsbündig (z. B. Anzahl Einträge), leer = keine.</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Zahl As String
        Get
            Return _zahl
        End Get
        Set(value As String)
            If _zahl = If(value, "") Then Return
            _zahl = If(value, "")
            Invalidate()
        End Set
    End Property

    Private _punkt As Color = Color.Empty

    ''' <summary>Navigation: farbiger Statuspunkt rechtsbündig (z. B. Verbindung), Color.Empty = keiner.</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Punkt As Color
        Get
            Return _punkt
        End Get
        Set(value As Color)
            If _punkt = value Then Return
            _punkt = value
            Invalidate()
        End Set
    End Property

    Private ReadOnly Property Innenabstand As Integer
        Get
            If _unterpunkt Then Return Px(Me, 16 + 18 + 12)
            Return Px(Me, If(Stil = KnopfStil.Navigation, 16, 14))
        End Get
    End Property

    Private ReadOnly Property SymbolBreite As Integer
        Get
            Return If(_symbol.Length > 0, Px(Me, 18), 0)
        End Get
    End Property

    Private ReadOnly Property Abstand As Integer
        Get
            Return If(_symbol.Length > 0 AndAlso Text.Length > 0, Px(Me, If(Stil = KnopfStil.Navigation, 12, 8)), 0)
        End Get
    End Property

    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Dim textBreite = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix).Width
        Return New Size(Math.Max(MinimumSize.Width, Innenabstand * 2 + SymbolBreite + Abstand + textBreite),
                        Math.Max(MinimumSize.Height, Px(Me, 36)))
    End Function

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        _hover = True
        Invalidate()
        MyBase.OnMouseEnter(e)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        _hover = False
        _gedrueckt = False
        Invalidate()
        MyBase.OnMouseLeave(e)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        If e.Button = MouseButtons.Left Then _gedrueckt = True
        Invalidate()
        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        _gedrueckt = False
        Invalidate()
        MyBase.OnMouseUp(e)
    End Sub

    Protected Overrides Sub OnEnabledChanged(e As EventArgs)
        Cursor = If(Enabled, Cursors.Hand, Cursors.Default)
        MyBase.OnEnabledChanged(e)
    End Sub

    Private Sub Farben(ByRef hinter As Color, ByRef vorder As Color, ByRef rahmen As Color)
        rahmen = Color.Empty
        Select Case Stil
            Case KnopfStil.Primaer
                hinter = If(_gedrueckt, Theme.AkzentGedrueckt, If(_hover, Theme.AkzentHover, Theme.Akzent))
                vorder = Color.White
            Case KnopfStil.Gefahr
                hinter = If(_gedrueckt, Theme.RotRand, If(_hover, Theme.RotHell, Theme.KartenFlaeche))
                vorder = Theme.Rot
                rahmen = Theme.RotRand
            Case KnopfStil.Navigation
                hinter = If((_aktiv AndAlso Not _unterpunkt) OrElse _hover, Theme.LeisteHover, Color.Empty)
                vorder = If(_aktiv, Theme.LeisteTextAktiv, If(_hover, Color.FromArgb(226, 232, 240), Theme.LeisteText))
            Case Else
                hinter = If(_gedrueckt, Theme.Rand, If(_hover, Theme.TabelleKopf, Theme.KartenFlaeche))
                vorder = Theme.TextNormal
                rahmen = Theme.RandEingabe
        End Select
        If Not Enabled Then
            If Stil = KnopfStil.Primaer Then hinter = Hell(Theme.Akzent, 0.4)
            vorder = If(Stil = KnopfStil.Primaer, Color.White, Theme.Grau)
            If Not rahmen.IsEmpty Then rahmen = Theme.Rand
        End If
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(If(Parent IsNot Nothing, Parent.BackColor, Theme.Seite))
        g.SmoothingMode = SmoothingMode.AntiAlias

        Dim hinter, vorder, rahmen As Color
        Farben(hinter, vorder, rahmen)

        Dim r As New Rectangle(0, 0, Width - 1, Height - 1)
        Using pfad = Abgerundet(r, Px(Me, 7))
            If Not hinter.IsEmpty Then
                Using b As New SolidBrush(hinter)
                    g.FillPath(b, pfad)
                End Using
            End If
            If Not rahmen.IsEmpty Then
                Using p As New Pen(rahmen)
                    g.DrawPath(p, pfad)
                End Using
            End If
            If Focused AndAlso ShowFocusCues AndAlso Stil <> KnopfStil.Navigation Then
                Using p As New Pen(Hell(Theme.Akzent, 0.5), Px(Me, 2))
                    g.DrawPath(p, pfad)
                End Using
            End If
        End Using

        If Stil = KnopfStil.Navigation AndAlso _aktiv Then
            Using b As New SolidBrush(Color.FromArgb(59, 130, 246))
                If _unterpunkt Then
                    ' Unterpunkt: kleiner Punkt vor dem Text statt Balken
                    Dim d = Px(Me, 6)
                    g.FillEllipse(b, Innenabstand - Px(Me, 14), (Height - d) \ 2, d, d)
                Else
                    g.FillRectangle(b, 0, Px(Me, 8), Px(Me, 3), Height - Px(Me, 16))
                End If
            End Using
        End If
        Dim zahlBreite = 0
        If Stil = KnopfStil.Navigation AndAlso _zahl.Length > 0 Then
            zahlBreite = TextRenderer.MeasureText(g, _zahl, Theme.SchriftKlein, Size.Empty, TextFormatFlags.NoPadding).Width + Px(Me, 12)
            TextRenderer.DrawText(g, _zahl, Theme.SchriftKlein, New Rectangle(0, 0, Width - Px(Me, 12), Height),
                                  If(_aktiv, Theme.LeisteTextAktiv, Theme.LeisteText),
                                  TextFormatFlags.Right Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine)
        End If

        If Stil = KnopfStil.Navigation AndAlso Not _punkt.IsEmpty Then
            Dim d = Px(Me, 9)
            Using b As New SolidBrush(_punkt)
                g.FillEllipse(b, Width - Px(Me, 16) - d - zahlBreite, (Height - d) \ 2, d, d)
            End Using
            zahlBreite += d + Px(Me, 8)
        End If
        ' Inhalt: links bündig in der Navigation, sonst zentriert
        Dim textBreite = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix).Width
        Dim inhalt = SymbolBreite + Abstand + textBreite
        Dim x = If(Stil = KnopfStil.Navigation, Innenabstand, Math.Max(Innenabstand \ 2, (Width - inhalt) \ 2))
        If _symbol.Length > 0 Then
            TextRenderer.DrawText(g, _symbol, Theme.SymbolSchrift, New Rectangle(x, 0, SymbolBreite, Height), vorder,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
            x += SymbolBreite + Abstand
        End If
        Dim rechts = If(_unterpunkt, Px(Me, 8), Innenabstand \ 2) + zahlBreite
        TextRenderer.DrawText(g, Text, Font, New Rectangle(x, 0, Math.Max(0, Width - x - rechts), Height), vorder,
                              TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding Or
                              TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix)
    End Sub

End Class

''' <summary>
''' Weiße Fläche mit abgerundeten Ecken und feinem Rahmen (wie eine "Card" auf Webseiten).
''' </summary>
Public Class Karte
    Inherits Panel

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
        BackColor = Theme.KartenFlaeche
        Padding = New Padding(1)
        Margin = New Padding(0)
    End Sub

    Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(If(Parent IsNot Nothing, Parent.BackColor, Theme.Seite))
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using pfad = Abgerundet(New Rectangle(0, 0, Width - 1, Height - 1), Px(Me, 10))
            Using b As New SolidBrush(BackColor)
                g.FillPath(b, pfad)
            End Using
            Using p As New Pen(Theme.Rand)
                g.DrawPath(p, pfad)
            End Using
        End Using
    End Sub

End Class

''' <summary>
''' Kennzahl-Kachel: Titel, großer Wert, Hinweiszeile und Symbol.
''' </summary>
Public Class KennzahlKarte
    Inherits Karte

    Private _wert As String = "–"
    Private _hinweis As String = ""
    Private ReadOnly _titel As String
    Private ReadOnly _symbol As String
    Private ReadOnly _farbe As Color

    Public Sub New(titel As String, symbol As String, farbe As Color)
        _titel = titel
        _symbol = symbol
        _farbe = farbe
        Dock = DockStyle.Fill
    End Sub

    Public Sub Setzen(wert As String, hinweis As String)
        _wert = wert
        _hinweis = hinweis
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim rand = Px(Me, 18)
        Dim kreis = Px(Me, 40)

        ' Symbol im farbigen Kreis rechts oben
        Dim k As New Rectangle(Width - rand - kreis, rand, kreis, kreis)
        Using b As New SolidBrush(Hell(_farbe, 0.12))
            g.FillEllipse(b, k)
        End Using
        TextRenderer.DrawText(g, _symbol, Theme.SymbolSchriftGross, k, _farbe,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)

        Dim textBreite = Math.Max(0, k.Left - rand - Px(Me, 8))
        Dim flags = TextFormatFlags.Left Or TextFormatFlags.NoPadding Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine
        Dim y = rand - Px(Me, 2)
        TextRenderer.DrawText(g, _titel, Theme.SchriftKlein, New Rectangle(rand, y, textBreite, Px(Me, 18)), Theme.TextLeise, flags)
        y += Px(Me, 22)
        Dim hoehe = TextRenderer.MeasureText(g, "0", Theme.SchriftKennzahl, Size.Empty, TextFormatFlags.NoPadding).Height
        TextRenderer.DrawText(g, _wert, Theme.SchriftKennzahl, New Rectangle(rand, y, textBreite, hoehe), Theme.TextStark, flags)
        y += hoehe + Px(Me, 2)
        TextRenderer.DrawText(g, _hinweis, Theme.SchriftKlein, New Rectangle(rand, y, Width - 2 * rand, Px(Me, 18)), Theme.TextLeise, flags)
    End Sub

End Class

''' <summary>
''' Suchfeld mit Lupe und abgerundetem Rahmen.
''' </summary>
Public Class SuchFeld
    Inherits Panel

    Public ReadOnly Eingabe As New TextBox With {
        .BorderStyle = BorderStyle.None,
        .Font = Theme.Schrift,
        .ForeColor = Theme.TextStark,
        .BackColor = Theme.KartenFlaeche,
        .PlaceholderText = "Suchen …"
    }

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
        BackColor = Theme.KartenFlaeche
        Size = New Size(280, 36)
        Margin = New Padding(0, 0, 8, 0)
        Cursor = Cursors.IBeam
        Controls.Add(Eingabe)
        AddHandler Eingabe.GotFocus, Sub(s, e) Invalidate()
        AddHandler Eingabe.LostFocus, Sub(s, e) Invalidate()
        AddHandler Click, Sub(s, e) Eingabe.Focus()
    End Sub

    Protected Overrides Sub OnLayout(levent As LayoutEventArgs)
        MyBase.OnLayout(levent)
        Dim links = Px(Me, 36)
        Eingabe.SetBounds(links, (Height - Eingabe.Height) \ 2, Math.Max(10, Width - links - Px(Me, 10)), Eingabe.Height)
    End Sub

    Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(If(Parent IsNot Nothing, Parent.BackColor, Theme.KartenFlaeche))
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using pfad = Abgerundet(New Rectangle(0, 0, Width - 1, Height - 1), Px(Me, 7))
            Using fl As New SolidBrush(Theme.KartenFlaeche)
                g.FillPath(fl, pfad)
            End Using
            Dim fokus = Eingabe.Focused
            Using p As New Pen(If(fokus, Theme.Akzent, Theme.RandEingabe), If(fokus, Px(Me, 2), 1))
                g.DrawPath(p, pfad)
            End Using
        End Using
        TextRenderer.DrawText(g, Theme.SymSuche, Theme.SymbolSchrift, New Rectangle(0, 0, Px(Me, 36), Height), Theme.TextLeise,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
    End Sub

End Class

''' <summary>
''' Gemeinsame Schnittstelle der Seiten im Hauptfenster.
''' </summary>
Public Interface ISeite
    ''' <summary>Daten neu laden (wird beim Anzeigen der Seite aufgerufen).</summary>
    Sub Neuladen()
    ''' <summary>Fokus auf das wichtigste Steuerelement setzen.</summary>
    Sub Aktivieren()
End Interface

''' <summary>
''' Tabelle im Web-Stil: helle Kopfzeile, Linien nur zwischen den Zeilen, Hover-Effekt,
''' Sortierung per Klick auf den Spaltenkopf und Hilfen zum Zeichnen von Badges.
''' </summary>
Public Class WebTabelle
    Inherits DataGridView

    Private _hoverZeile As Integer = -1

    ''' <summary>Wird ausgelöst, wenn per Klick auf einen Spaltenkopf die Sortierung geändert wurde.</summary>
    Public Event SortierungGeaendert As EventHandler

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property SortSpalte As String = ""

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property SortAufsteigend As Boolean = True

    Public Sub New()
        Dock = DockStyle.Fill
        Margin = New Padding(0)
        AutoGenerateColumns = False
        [ReadOnly] = True
        AllowUserToAddRows = False
        AllowUserToDeleteRows = False
        AllowUserToResizeRows = False
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
        MultiSelect = False
        RowHeadersVisible = False
        BackgroundColor = Theme.KartenFlaeche
        BorderStyle = BorderStyle.None
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
        GridColor = Theme.TabelleLinie
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        EnableHeadersVisualStyles = False
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        RowTemplate.Height = Px(Me, 40)

        With ColumnHeadersDefaultCellStyle
            .BackColor = Theme.TabelleKopf
            .ForeColor = Theme.TextLeise
            .SelectionBackColor = Theme.TabelleKopf
            .SelectionForeColor = Theme.TextLeise
            .Font = Theme.SchriftKopf
            .Padding = New Padding(6, 10, 6, 10)
            .WrapMode = DataGridViewTriState.False
        End With
        With DefaultCellStyle
            .BackColor = Theme.KartenFlaeche
            .ForeColor = Theme.TextNormal
            .SelectionBackColor = Theme.Auswahl
            .SelectionForeColor = Theme.TextStark
            .Font = Theme.Schrift
            .Padding = New Padding(6, 0, 6, 0)
        End With
    End Sub

    ''' <summary>
    ''' Fügt eine Spalte hinzu. breite = 0: Breite passt sich dem Inhalt an, sonst Anteil am Restplatz.
    ''' </summary>
    Public Function Spalte(eigenschaft As String, titel As String, breite As Single,
                           Optional zahlenformat As String = Nothing, Optional rechtsbuendig As Boolean = False,
                           Optional minBreite As Integer = 70, Optional menueName As String = Nothing) As DataGridViewColumn
        Dim s As New DataGridViewTextBoxColumn With {
            .DataPropertyName = eigenschaft,
            .Name = eigenschaft,
            .HeaderText = titel,
            .ToolTipText = If(menueName, titel),
            .Tag = If(menueName, titel),
            .SortMode = DataGridViewColumnSortMode.Programmatic
        }
        If breite > 0 Then
            s.FillWeight = breite
            s.MinimumWidth = Px(Me, minBreite)
        Else
            s.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
        End If
        If zahlenformat IsNot Nothing Then s.DefaultCellStyle.Format = zahlenformat
        If rechtsbuendig Then
            s.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
            s.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight
        End If
        Columns.Add(s)
        Return s
    End Function

    ''' <summary>Sortiert die Einträge nach SortSpalte und setzt das Sortiersymbol im Spaltenkopf.</summary>
    Public Function Sortieren(Of T)(eintraege As IEnumerable(Of T)) As List(Of T)
        For Each sp As DataGridViewColumn In Columns
            sp.HeaderCell.SortGlyphDirection = If(sp.DataPropertyName = SortSpalte,
                                                  If(SortAufsteigend, SortOrder.Ascending, SortOrder.Descending),
                                                  SortOrder.None)
        Next
        Dim eigenschaft = If(String.IsNullOrEmpty(SortSpalte), Nothing, GetType(T).GetProperty(SortSpalte))
        If eigenschaft Is Nothing Then Return eintraege.ToList()
        If SortAufsteigend Then Return eintraege.OrderBy(Function(x) eigenschaft.GetValue(x)).ToList()
        Return eintraege.OrderByDescending(Function(x) eigenschaft.GetValue(x)).ToList()
    End Function

    Public Function Eintrag(Of T As Class)(zeile As Integer) As T
        If zeile < 0 OrElse zeile >= Rows.Count Then Return Nothing
        Return TryCast(Rows(zeile).DataBoundItem, T)
    End Function

    Public Function Ausgewaehlt(Of T As Class)() As T
        If CurrentRow Is Nothing Then Return Nothing
        Return TryCast(CurrentRow.DataBoundItem, T)
    End Function

    ''' <summary>Wählt die Zeile aus, deren Eintrag die Bedingung erfüllt.</summary>
    Public Sub Auswaehlen(Of T As Class)(bedingung As Func(Of T, Boolean))
        For Each zeile As DataGridViewRow In Rows
            Dim e = TryCast(zeile.DataBoundItem, T)
            If e IsNot Nothing AndAlso bedingung(e) Then
                Dim ersteSichtbare = Columns.GetFirstColumn(DataGridViewElementStates.Visible)
                If ersteSichtbare IsNot Nothing Then CurrentCell = zeile.Cells(ersteSichtbare.Index)
                Return
            End If
        Next
    End Sub


    Protected Overrides Sub OnColumnHeaderMouseClick(e As DataGridViewCellMouseEventArgs)
        MyBase.OnColumnHeaderMouseClick(e)
        If e.Button <> MouseButtons.Left Then Return
        Dim eigenschaft = Columns(e.ColumnIndex).DataPropertyName
        If eigenschaft = SortSpalte Then
            SortAufsteigend = Not SortAufsteigend
        Else
            SortSpalte = eigenschaft
            SortAufsteigend = True
        End If
        RaiseEvent SortierungGeaendert(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Sub OnCellMouseDown(e As DataGridViewCellMouseEventArgs)
        ' Rechtsklick wählt die Zeile aus, damit sich das Kontextmenü auf sie bezieht
        If e.Button = MouseButtons.Right AndAlso e.RowIndex >= 0 Then
            CurrentCell = Rows(e.RowIndex).Cells(Math.Max(0, e.ColumnIndex))
        End If
        MyBase.OnCellMouseDown(e)
    End Sub

    Protected Overrides Sub OnCellMouseEnter(e As DataGridViewCellEventArgs)
        MyBase.OnCellMouseEnter(e)
        HoverSetzen(e.RowIndex)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        HoverSetzen(-1)
    End Sub

    Protected Overrides Sub OnDataBindingComplete(e As DataGridViewBindingCompleteEventArgs)
        _hoverZeile = -1
        MyBase.OnDataBindingComplete(e)
    End Sub

    Protected Overrides Sub OnCellFormatting(e As DataGridViewCellFormattingEventArgs)
        MyBase.OnCellFormatting(e)
        If e.RowIndex >= 0 AndAlso e.RowIndex = _hoverZeile AndAlso e.CellStyle IsNot Nothing Then
            e.CellStyle.BackColor = Theme.TabelleKopf
        End If
    End Sub

    ''' <summary>
    ''' Zellen ohne die eingebauten Rahmen zeichnen und nur die Linie unten ergänzen – die DataGridView zeichnet
    ''' sonst je nach Windows-Farbmodus zusätzliche senkrechte Rahmen.
    ''' </summary>
    Protected Overrides Sub OnCellPainting(e As DataGridViewCellPaintingEventArgs)
        ' Kantenglättung aus selbst gezeichneten Zellen (Badges, Balken) nicht weitergeben – sonst entstehen an den
        ' Zellkanten halbtransparente Pixel, die als feine Rahmen sichtbar werden
        If e.Graphics IsNot Nothing Then e.Graphics.SmoothingMode = SmoothingMode.Default
        MyBase.OnCellPainting(e)
        If e.Graphics IsNot Nothing Then e.Graphics.SmoothingMode = SmoothingMode.Default
        If e.Handled OrElse e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return
        e.Paint(e.ClipBounds, DataGridViewPaintParts.All And Not DataGridViewPaintParts.Border)
        ZelleAbschliessen(e)
    End Sub

    Private Sub HoverSetzen(zeile As Integer)
        If zeile = _hoverZeile Then Return
        Dim alt = _hoverZeile
        _hoverZeile = zeile
        If alt >= 0 AndAlso alt < RowCount Then InvalidateRow(alt)
        If zeile >= 0 AndAlso zeile < RowCount Then InvalidateRow(zeile)
    End Sub

    ''' <summary>Zeichnet den Zellinhalt als abgerundetes Badge (z. B. Material, Dateityp).</summary>
    Public Sub BadgeZeichnen(e As DataGridViewCellPaintingEventArgs, text As String, vorder As Color, hinter As Color)
        e.PaintBackground(e.CellBounds, True)
        Dim g = e.Graphics
        If Not String.IsNullOrEmpty(text) Then
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim b = e.CellBounds
            Dim pad = e.CellStyle.Padding.Left
            Dim groesse = TextRenderer.MeasureText(g, text, Theme.SchriftKopf, Size.Empty, TextFormatFlags.NoPadding)
            Dim h = groesse.Height + Px(Me, 6)
            Dim w = Math.Min(groesse.Width + Px(Me, 16), b.Width - 2 * pad)
            Dim r As New Rectangle(b.X + pad, b.Y + (b.Height - h) \ 2, w, h)
            Using pfad = Abgerundet(r, h \ 2), pinsel As New SolidBrush(hinter)
                g.FillPath(pinsel, pfad)
            End Using
            TextRenderer.DrawText(g, text, Theme.SchriftKopf, r, vorder,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or
                                  TextFormatFlags.NoPadding Or TextFormatFlags.EndEllipsis)
        End If
        ZelleAbschliessen(e)
    End Sub

    ''' <summary>Zeichnet die Trennlinie unter einer selbst gezeichneten Zelle und markiert sie als erledigt.</summary>
    Public Sub ZelleAbschliessen(e As DataGridViewCellPaintingEventArgs)
        Using p As New Pen(GridColor)
            e.Graphics.DrawLine(p, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1)
        End Using
        e.Handled = True
    End Sub

End Class

''' <summary>
''' Karte mit Werkzeugleiste oben, Inhalt (meist eine WebTabelle) in der Mitte und Fußzeile unten.
''' </summary>
Public Class TabellenKarte
    Inherits Karte

    Public ReadOnly Werkzeug As New FlowLayoutPanel With {
        .Dock = DockStyle.Fill,
        .AutoSize = True,
        .WrapContents = True,
        .Padding = New Padding(16, 14, 16, 14),
        .Margin = New Padding(0)
    }
    Public ReadOnly FussLinks As New Label With {
        .Dock = DockStyle.Left, .AutoSize = True, .TextAlign = ContentAlignment.MiddleLeft,
        .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein
    }
    Public ReadOnly FussRechts As New Label With {
        .Dock = DockStyle.Right, .AutoSize = True, .TextAlign = ContentAlignment.MiddleRight,
        .ForeColor = Theme.Grau, .Font = Theme.SchriftKlein
    }

    Public Sub New(inhalt As Control)
        Dock = DockStyle.Fill
        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .Margin = New Padding(0)}
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        t.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        t.RowStyles.Add(New RowStyle(SizeType.Absolute, 44))

        AddHandler Werkzeug.Paint, Sub(s, e) Linie(e.Graphics, Werkzeug, unten:=True)

        Dim fuss As New Panel With {.Dock = DockStyle.Fill, .Margin = New Padding(0), .Padding = New Padding(16, 1, 16, 0)}
        AddHandler fuss.Paint, Sub(s, e) Linie(e.Graphics, fuss, unten:=False)
        fuss.Controls.Add(FussLinks)
        fuss.Controls.Add(FussRechts)
        ' AutoSize-Labels mit Dock brauchen die volle Höhe für die vertikale Zentrierung
        AddHandler fuss.Resize, Sub(s, e)
                                    FussLinks.MinimumSize = New Size(0, fuss.ClientSize.Height)
                                    FussRechts.MinimumSize = New Size(0, fuss.ClientSize.Height)
                                End Sub

        inhalt.Dock = DockStyle.Fill
        inhalt.Margin = New Padding(0)
        t.Controls.Add(Werkzeug, 0, 0)
        t.Controls.Add(inhalt, 0, 1)
        t.Controls.Add(fuss, 0, 2)
        Controls.Add(t)
    End Sub

    Private Shared Sub Linie(g As Graphics, c As Control, unten As Boolean)
        Using p As New Pen(Theme.Rand)
            Dim y = If(unten, c.Height - 1, 0)
            g.DrawLine(p, 0, y, c.Width, y)
        End Using
    End Sub

    Private ReadOnly _kompakt As New List(Of (Knopf As WebButton, Text As String))

    ''' <summary>
    ''' Diese Knöpfe zeigen nur ihr Symbol, wenn die Werkzeugleiste sonst in eine zweite Zeile umbrechen würde
    ''' (die Tooltips erklären sie weiterhin).
    ''' </summary>
    Public Sub KompaktFaehig(ParamArray knoepfe As WebButton())
        For Each b In knoepfe
            _kompakt.Add((b, b.Text))
        Next
        AddHandler Werkzeug.SizeChanged, Sub(s, e) KompaktAnpassen()
        KompaktAnpassen()
    End Sub

    Private Sub KompaktAnpassen()
        If _kompakt.Count = 0 OrElse Werkzeug.ClientSize.Width <= 0 Then Return
        ' Benötigte Breite mit vollen Beschriftungen
        Dim noetig = Werkzeug.Padding.Horizontal
        For Each c As Control In Werkzeug.Controls
            If Not c.Visible Then Continue For
            Dim kompaktKnopf = _kompakt.FirstOrDefault(Function(k) k.Knopf Is c)
            Dim breite As Integer
            If kompaktKnopf.Knopf IsNot Nothing Then
                Dim aktuell = c.Text
                c.Text = kompaktKnopf.Text
                breite = c.GetPreferredSize(Size.Empty).Width
                c.Text = aktuell
            Else
                breite = If(c.AutoSize, c.GetPreferredSize(Size.Empty).Width, c.Width)
            End If
            noetig += breite + c.Margin.Horizontal
        Next
        Dim kompakt = noetig > Werkzeug.ClientSize.Width
        For Each k In _kompakt
            Dim soll = If(kompakt, "", k.Text)
            If k.Knopf.Text <> soll Then k.Knopf.Text = soll
        Next
    End Sub

End Class

''' <summary>
''' Kopfbereich einer Seite: großer Titel, Untertitel und rechts optionale Knöpfe.
''' </summary>
Public Class SeitenKopf
    Inherits TableLayoutPanel

    Public ReadOnly Knoepfe As New FlowLayoutPanel With {
        .AutoSize = True,
        .WrapContents = False,
        .Anchor = AnchorStyles.Right,
        .Margin = New Padding(0)
    }

    Public Sub New(titel As String, untertitel As String)
        ColumnCount = 2
        RowCount = 1
        AutoSize = True
        Dock = DockStyle.Fill
        Margin = New Padding(0, 0, 0, 20)
        ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        Dim texte As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False,
            .AutoSize = True,
            .Margin = New Padding(0)
        }
        texte.Controls.Add(New Label With {.Text = titel, .Font = Theme.SchriftTitel, .ForeColor = Theme.TextStark,
                                           .AutoSize = True, .Margin = New Padding(0)})
        lblUntertitel = New Label With {.Text = untertitel, .Font = Theme.Schrift, .ForeColor = Theme.TextLeise,
                                        .AutoSize = True, .Margin = New Padding(1, 2, 0, 0)}
        texte.Controls.Add(lblUntertitel)
        Controls.Add(texte, 0, 0)
        Controls.Add(Knoepfe, 1, 0)
    End Sub

    Private ReadOnly lblUntertitel As Label

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property Untertitel As String
        Get
            Return lblUntertitel.Text
        End Get
        Set(value As String)
            lblUntertitel.Text = value
        End Set
    End Property

End Class

''' <summary>Hilfen für Eingabefelder.</summary>
Public Module Eingabe

    ''' <summary>
    ''' Aktueller Wert eines Zahlenfelds inkl. noch nicht bestätigter Eingabe
    ''' (NumericUpDown.Value ändert sich erst beim Verlassen des Felds).
    ''' </summary>
    Public Function EingabeWert(n As NumericUpDown) As Decimal
        Dim wert As Decimal
        If Decimal.TryParse(n.Text, Globalization.NumberStyles.Number, Globalization.CultureInfo.CurrentCulture, wert) Then
            Return Math.Min(n.Maximum, Math.Max(n.Minimum, wert))
        End If
        Return n.Value
    End Function

End Module

''' <summary>Anklickbarer Statuspunkt (z. B. Verbindung zum Server): farbiger Kreis, beim Überfahren mit Ring.</summary>
Public Class StatusPunkt
    Inherits Control

    Private _farbe As Color = Theme.Grau
    Private _hover As Boolean

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw Or ControlStyles.SupportsTransparentBackColor, True)
        Cursor = Cursors.Hand
        Size = New Size(22, 22)
    End Sub

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Farbe As Color
        Get
            Return _farbe
        End Get
        Set(value As Color)
            _farbe = value
            Invalidate()
        End Set
    End Property

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        MyBase.OnMouseEnter(e)
        _hover = True
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        _hover = False
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        Dim d = Px(Me, 10)
        Dim r As New Rectangle((Width - d) \ 2, (Height - d) \ 2, d, d)
        If _hover Then
            Dim ring = r
            ring.Inflate(Px(Me, 4), Px(Me, 4))
            Using b As New SolidBrush(Color.FromArgb(60, _farbe))
                g.FillEllipse(b, ring)
            End Using
        End If
        Using b As New SolidBrush(_farbe)
            g.FillEllipse(b, r)
        End Using
    End Sub
End Class

''' <summary>
''' Schmale Bildlaufleiste im Stil der Seitenleiste (statt der Windows-Leiste): abgerundeter Balken ohne Pfeile,
''' beim Überfahren breiter und heller. Ziehen am Balken oder Klick daneben (eine Seite weiter).
''' </summary>
Public Class SchmaleBildlaufleiste
    Inherits Control

    Private _wert As Integer
    Private _maximum As Integer
    Private _seite As Integer = 1
    Private _hover As Boolean
    Private _ziehen As Boolean
    Private _ziehStartY As Integer
    Private _ziehStartWert As Integer

    ''' <summary>Neue Position (0 … Maximum).</summary>
    Public Event WertGeaendert As EventHandler

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw, True)
        Width = 10
    End Sub

    ''' <summary>Größte Position (Inhaltshöhe minus sichtbare Höhe).</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Maximum As Integer
        Get
            Return _maximum
        End Get
        Set(value As Integer)
            _maximum = Math.Max(0, value)
            If _wert > _maximum Then Wert = _maximum Else Invalidate()
        End Set
    End Property

    ''' <summary>Sichtbare Höhe – bestimmt die Länge des Balkens.</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Seite As Integer
        Get
            Return _seite
        End Get
        Set(value As Integer)
            _seite = Math.Max(1, value)
            Invalidate()
        End Set
    End Property

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Wert As Integer
        Get
            Return _wert
        End Get
        Set(value As Integer)
            Dim neu = Math.Max(0, Math.Min(_maximum, value))
            If neu = _wert Then Return
            _wert = neu
            Invalidate()
            RaiseEvent WertGeaendert(Me, EventArgs.Empty)
        End Set
    End Property

    Private Function Balken() As Rectangle
        Dim rand = Px(Me, 2)
        Dim spur = Height - 2 * rand
        Dim laenge = Math.Max(Px(Me, 30), CInt(spur * _seite / CDbl(_seite + _maximum)))
        Dim y = rand + If(_maximum > 0, CInt((spur - laenge) * _wert / CDbl(_maximum)), 0)
        Return New Rectangle(0, y, Width, laenge)
    End Function

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        e.Graphics.Clear(BackColor)
        If _maximum <= 0 Then Return
        Dim r = Balken()
        Dim breite = Px(Me, If(_hover OrElse _ziehen, 6, 4))
        Dim griff As New RectangleF(Width - breite - Px(Me, 2), r.Y, breite, r.Height)
        Dim farbe = If(_ziehen, Color.FromArgb(148, 163, 184), If(_hover, Color.FromArgb(100, 116, 139), Color.FromArgb(51, 65, 85)))
        e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        Using pfad As New GraphicsPath(), b As New SolidBrush(farbe)
            Dim d = griff.Width
            pfad.AddArc(griff.X, griff.Y, d, d, 180, 180)
            pfad.AddArc(griff.X, griff.Bottom - d, d, d, 0, 180)
            pfad.CloseFigure()
            e.Graphics.FillPath(b, pfad)
        End Using
    End Sub

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        MyBase.OnMouseEnter(e)
        _hover = True
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        _hover = False
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button <> MouseButtons.Left OrElse _maximum <= 0 Then Return
        Dim r = Balken()
        If e.Y < r.Top Then
            Wert -= _seite
        ElseIf e.Y > r.Bottom Then
            Wert += _seite
        Else
            _ziehen = True
            _ziehStartY = e.Y
            _ziehStartWert = _wert
            Capture = True
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If Not _ziehen Then Return
        Dim r = Balken()
        Dim spielraum = Height - 2 * Px(Me, 2) - r.Height
        If spielraum > 0 Then Wert = _ziehStartWert + CInt((e.Y - _ziehStartY) * _maximum / CDbl(spielraum))
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If Not _ziehen Then Return
        _ziehen = False
        Capture = False
        Invalidate()
    End Sub
End Class
