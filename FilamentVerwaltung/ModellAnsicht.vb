Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Windows.Forms
Imports System.Xml

''' <summary>
''' 3D-Modell als Liste von Dreiecken (9 Werte pro Dreieck), zentriert und auf Radius 1 skaliert.
''' </summary>
Public Class Netz

    Public ReadOnly Property Dreiecke As Single()
    ''' <summary>Abmessungen des Originalmodells (in mm bei STL/3MF).</summary>
    Public ReadOnly Property GroesseX As Single
    Public ReadOnly Property GroesseY As Single
    Public ReadOnly Property GroesseZ As Single

    Public ReadOnly Property Anzahl As Integer
        Get
            Return Dreiecke.Length \ 9
        End Get
    End Property

    Private Sub New(werte As List(Of Single))
        If werte.Count < 9 Then Throw New InvalidDataException("Die Datei enthält keine Dreiecke.")
        Dim minX = Single.MaxValue, minY = Single.MaxValue, minZ = Single.MaxValue
        Dim maxX = Single.MinValue, maxY = Single.MinValue, maxZ = Single.MinValue
        For i = 0 To werte.Count - 3 Step 3
            minX = Math.Min(minX, werte(i)) : maxX = Math.Max(maxX, werte(i))
            minY = Math.Min(minY, werte(i + 1)) : maxY = Math.Max(maxY, werte(i + 1))
            minZ = Math.Min(minZ, werte(i + 2)) : maxZ = Math.Max(maxZ, werte(i + 2))
        Next
        GroesseX = maxX - minX
        GroesseY = maxY - minY
        GroesseZ = maxZ - minZ
        Dim cx = (minX + maxX) / 2, cy = (minY + maxY) / 2, cz = (minZ + maxZ) / 2
        Dim radius = CSng(Math.Sqrt(GroesseX * GroesseX + GroesseY * GroesseY + GroesseZ * GroesseZ) / 2)
        If radius <= 0 Then radius = 1
        Dim n = (werte.Count \ 9) * 9
        Dim d(n - 1) As Single
        For i = 0 To n - 3 Step 3
            d(i) = (werte(i) - cx) / radius
            d(i + 1) = (werte(i + 1) - cy) / radius
            d(i + 2) = (werte(i + 2) - cz) / radius
        Next
        Dreiecke = d
    End Sub

    Public Shared Function KannLaden(pfad As String) As Boolean
        Return {".stl", ".obj", ".3mf"}.Contains(Path.GetExtension(pfad).ToLowerInvariant())
    End Function

    Public Shared Function Laden(pfad As String) As Netz
        Select Case Path.GetExtension(pfad).ToLowerInvariant()
            Case ".stl" : Return New Netz(StlLesen(pfad))
            Case ".obj" : Return New Netz(ObjLesen(pfad))
            Case ".3mf" : Return New Netz(DreiMfLesen(pfad))
        End Select
        Throw New NotSupportedException("Für diesen Dateityp gibt es keine Vorschau.")
    End Function

    Private Shared Function StlLesen(pfad As String) As List(Of Single)
        Dim bytes = File.ReadAllBytes(pfad)
        Dim beginntMitSolid = bytes.Length >= 5 AndAlso Encoding.ASCII.GetString(bytes, 0, 5).Equals("solid", StringComparison.OrdinalIgnoreCase)
        If bytes.Length >= 84 Then
            Dim anzahl = CLng(BitConverter.ToUInt32(bytes, 80))
            Dim passt = 84L + anzahl * 50L = bytes.Length
            ' Binär-STL (manche Programme schreiben trotzdem "solid" in den Kopf – dann entscheidet die Länge)
            If passt OrElse Not beginntMitSolid Then
                anzahl = Math.Min(anzahl, (bytes.Length - 84L) \ 50L)
                Dim liste As New List(Of Single)(CInt(anzahl) * 9)
                For t = 0 To CInt(anzahl) - 1
                    Dim o = 84 + t * 50 + 12
                    For k = 0 To 8
                        liste.Add(BitConverter.ToSingle(bytes, o + k * 4))
                    Next
                Next
                Return liste
            End If
        End If

        ' ASCII-STL: "vertex x y z"
        Dim werte As New List(Of Single)
        For Each zeile In Encoding.ASCII.GetString(bytes).Split(ControlChars.Lf)
            Dim z = zeile.Trim()
            If Not z.StartsWith("vertex", StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim teile = z.Split({" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
            If teile.Length < 4 Then Continue For
            For k = 1 To 3
                werte.Add(Single.Parse(teile(k), NumberStyles.Float, CultureInfo.InvariantCulture))
            Next
        Next
        Return werte
    End Function

    Private Shared Function ObjLesen(pfad As String) As List(Of Single)
        Dim punkte As New List(Of Single)
        Dim werte As New List(Of Single)
        For Each zeile In File.ReadLines(pfad)
            Dim teile = zeile.Split({" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
            If teile.Length < 4 Then Continue For
            If teile(0) = "v" Then
                For k = 1 To 3
                    punkte.Add(Single.Parse(teile(k), NumberStyles.Float, CultureInfo.InvariantCulture))
                Next
            ElseIf teile(0) = "f" Then
                ' Flächen können Vielecke sein ("f 1/1/1 2/2/2 3/3/3 4/4/4") → als Fächer in Dreiecke zerlegen
                Dim idx As New List(Of Integer)
                For k = 1 To teile.Length - 1
                    Dim i = Integer.Parse(teile(k).Split("/"c)(0), CultureInfo.InvariantCulture)
                    idx.Add(If(i < 0, punkte.Count \ 3 + i, i - 1))
                Next
                For k = 1 To idx.Count - 2
                    For Each p In {idx(0), idx(k), idx(k + 1)}
                        If p < 0 OrElse p * 3 + 2 >= punkte.Count Then Continue For
                        werte.Add(punkte(p * 3)) : werte.Add(punkte(p * 3 + 1)) : werte.Add(punkte(p * 3 + 2))
                    Next
                Next
            End If
        Next
        Return werte
    End Function

    ''' <summary>3MF = ZIP mit XML-Dateien unter 3D/ (Bambu Studio legt die Objekte in 3D/Objects/ ab).</summary>
    Private Shared Function DreiMfLesen(pfad As String) As List(Of Single)
        Dim werte As New List(Of Single)
        Using zip = ZipFile.OpenRead(pfad)
            For Each eintrag In zip.Entries
                If Not eintrag.FullName.StartsWith("3D/", StringComparison.OrdinalIgnoreCase) OrElse
                   Not eintrag.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim punkte As New List(Of Single)
                Using s = eintrag.Open(), r = XmlReader.Create(s)
                    While r.Read()
                        If r.NodeType <> XmlNodeType.Element Then Continue While
                        Select Case r.LocalName
                            Case "mesh"
                                punkte.Clear()
                            Case "vertex"
                                punkte.Add(Wert(r, "x")) : punkte.Add(Wert(r, "y")) : punkte.Add(Wert(r, "z"))
                            Case "triangle"
                                For Each name In {"v1", "v2", "v3"}
                                    Dim p = CInt(Wert(r, name))
                                    If p < 0 OrElse p * 3 + 2 >= punkte.Count Then Continue For
                                    werte.Add(punkte(p * 3)) : werte.Add(punkte(p * 3 + 1)) : werte.Add(punkte(p * 3 + 2))
                                Next
                        End Select
                    End While
                End Using
            Next
        End Using
        Return werte
    End Function

    Private Shared Function Wert(r As XmlReader, attribut As String) As Single
        Dim s As Single
        Single.TryParse(r.GetAttribute(attribut), NumberStyles.Float, CultureInfo.InvariantCulture, s)
        Return s
    End Function

End Class

''' <summary>
''' 3D-Vorschau mit eigenem Software-Renderer (Z-Buffer, Flat-Shading, 2× Kantenglättung).
''' Linke Maustaste ziehen = drehen, Mausrad = zoomen, Doppelklick = Ansicht zurücksetzen.
''' </summary>
Public Class ModellAnsicht
    Inherits Control

    Private Const StartDrehung As Single = -0.65F
    Private Const StartNeigung As Single = 0.5F

    Private _netz As Netz
    Private _bild As Bitmap
    Private _hinweis As String = ""
    Private _drehung As Single = StartDrehung
    Private _neigung As Single = StartNeigung
    Private _zoom As Single = 1.0F
    Private _ziehen As Boolean
    Private _letzte As Point
    Private ReadOnly _feinTimer As New Timer With {.Interval = 200}

    ''' <summary>Grundfarbe des Modells.</summary>
    Private ReadOnly _modellFarbe As Color = ColorTranslator.FromHtml("#4F86F7")

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        BackColor = Theme.TabelleKopf
        AddHandler _feinTimer.Tick, Sub(s, e)
                                        _feinTimer.Stop()
                                        Neuzeichnen(schnell:=False)
                                    End Sub
    End Sub

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Netz As Netz
        Get
            Return _netz
        End Get
        Set(value As Netz)
            _netz = value
            _drehung = StartDrehung
            _neigung = StartNeigung
            _zoom = 1.0F
            Cursor = If(value Is Nothing, Cursors.Default, Cursors.SizeAll)
            Neuzeichnen(schnell:=False)
        End Set
    End Property

    ''' <summary>Text, der ohne Modell angezeigt wird (z. B. "Lade Vorschau …").</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Hinweis As String
        Get
            Return _hinweis
        End Get
        Set(value As String)
            _hinweis = If(value, "")
            Invalidate()
        End Set
    End Property

    Private Sub Neuzeichnen(schnell As Boolean)
        _bild?.Dispose()
        _bild = Nothing
        If _netz IsNot Nothing AndAlso Width > 10 AndAlso Height > 10 Then _bild = Rendern(Width, Height, schnell)
        Invalidate()
    End Sub

    Private Sub FeinSpaeter()
        _feinTimer.Stop()
        _feinTimer.Start()
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        If _netz Is Nothing Then Return
        Neuzeichnen(schnell:=True)
        FeinSpaeter()
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        Focus()
        If e.Button = MouseButtons.Left AndAlso _netz IsNot Nothing Then
            _ziehen = True
            _letzte = e.Location
        End If
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If Not _ziehen Then Return
        _drehung -= (e.X - _letzte.X) * 0.01F
        _neigung = Math.Max(-1.55F, Math.Min(1.55F, _neigung + (e.Y - _letzte.Y) * 0.01F))
        _letzte = e.Location
        Neuzeichnen(schnell:=True)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If Not _ziehen Then Return
        _ziehen = False
        Neuzeichnen(schnell:=False)
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        If _netz Is Nothing Then Return
        _zoom = Math.Max(0.3F, Math.Min(8.0F, _zoom * If(e.Delta > 0, 1.15F, 1 / 1.15F)))
        Neuzeichnen(schnell:=True)
        FeinSpaeter()
    End Sub

    Protected Overrides Sub OnDoubleClick(e As EventArgs)
        MyBase.OnDoubleClick(e)
        If _netz Is Nothing Then Return
        _drehung = StartDrehung
        _neigung = StartNeigung
        _zoom = 1.0F
        Neuzeichnen(schnell:=False)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        If _bild IsNot Nothing Then
            g.DrawImageUnscaled(_bild, 0, 0)
        Else
            Using b As New LinearGradientBrush(ClientRectangle, Theme.TabelleKopf, Theme.Rand, 90.0F)
                g.FillRectangle(b, ClientRectangle)
            End Using
            Dim kreis = Px(Me, 72)
            Dim k As New Rectangle((Width - kreis) \ 2, Height \ 2 - kreis + Px(Me, 4), kreis, kreis)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Using b As New SolidBrush(Hell(Theme.Akzent, 0.1))
                g.FillEllipse(b, k)
            End Using
            TextRenderer.DrawText(g, Theme.SymWuerfel, Theme.SymbolSchriftRiesig, k, Hell(Theme.Akzent, 0.7),
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
            TextRenderer.DrawText(g, _hinweis, Theme.Schrift,
                                  New Rectangle(Px(Me, 16), k.Bottom + Px(Me, 12), Width - Px(Me, 32), Px(Me, 48)), Theme.TextLeise,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.WordBreak Or TextFormatFlags.NoPadding)
            Return
        End If

        Dim flags = TextFormatFlags.Left Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis
        Dim rand = Px(Me, 10)
        TextRenderer.DrawText(g, "Ziehen: drehen · Mausrad: zoomen · Doppelklick: zurücksetzen", Theme.SchriftKlein,
                              New Rectangle(rand, rand, Width - 2 * rand, Px(Me, 18)), Theme.Grau, flags)
        Dim info = $"{_netz.Anzahl:N0} Dreiecke · {_netz.GroesseX:0.#} × {_netz.GroesseY:0.#} × {_netz.GroesseZ:0.#} mm"
        TextRenderer.DrawText(g, info, Theme.SchriftKlein,
                              New Rectangle(rand, Height - rand - Px(Me, 18), Width - 2 * rand, Px(Me, 18)), Theme.TextLeise, flags)
    End Sub

    ''' <summary>Rendert das Modell; schnell = halbe Auflösung ohne Kantenglättung (beim Drehen).</summary>
    Private Function Rendern(breite As Integer, hoehe As Integer, schnell As Boolean) As Bitmap
        Dim faktor = If(schnell, 1, 2)
        Dim w = breite * faktor, h = hoehe * faktor
        Dim farbe(w * h - 1) As Integer
        Dim tiefe(w * h - 1) As Single
        Array.Fill(tiefe, Single.MaxValue)

        Dim cosD = CSng(Math.Cos(_drehung)), sinD = CSng(Math.Sin(_drehung))
        Dim cosN = CSng(Math.Cos(_neigung)), sinN = CSng(Math.Sin(_neigung))
        Dim skala = Math.Min(w, h) * 0.46F * _zoom
        Dim mx = w / 2.0F, my = h / 2.0F

        ' Licht im Blickraum (x rechts, y oben, z in den Bildschirm hinein): von links oben vorne
        Dim lx = -0.45F, ly = 0.75F, lz = -0.5F
        Dim ll = CSng(Math.Sqrt(lx * lx + ly * ly + lz * lz))
        lx /= ll : ly /= ll : lz /= ll

        Dim d = _netz.Dreiecke
        Dim n = _netz.Anzahl
        Dim schritt = If(schnell AndAlso n > 250000, CInt(Math.Ceiling(n / 250000.0)), 1)
        Dim vx(2), vy(2), vz(2) As Single
        Dim basisR = _modellFarbe.R, basisG = _modellFarbe.G, basisB = _modellFarbe.B

        For t = 0 To n - 1 Step schritt
            Dim o = t * 9
            For k = 0 To 2
                Dim x = d(o + k * 3), y = d(o + k * 3 + 1), z = d(o + k * 3 + 2)
                ' Drehung um die Hochachse (Z), dann Neigung zur Kamera
                Dim x1 = x * cosD - y * sinD
                Dim y1 = x * sinD + y * cosD
                vx(k) = x1
                vy(k) = y1 * sinN + z * cosN
                vz(k) = y1 * cosN - z * sinN
            Next

            ' Flächennormale (beidseitig beleuchtet, da STL-Normalen oft falsch sind)
            Dim ax = vx(1) - vx(0), ay = vy(1) - vy(0), az = vz(1) - vz(0)
            Dim bx = vx(2) - vx(0), by = vy(2) - vy(0), bz = vz(2) - vz(0)
            Dim nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx
            Dim laenge = CSng(Math.Sqrt(nx * nx + ny * ny + nz * nz))
            If laenge < 0.0000000001F Then Continue For
            nx /= laenge : ny /= laenge : nz /= laenge

            Dim diffus = Math.Abs(nx * lx + ny * ly + nz * lz)
            Dim kopflicht = Math.Abs(nz)
            Dim tiefeMitte = (vz(0) + vz(1) + vz(2)) / 3.0F
            Dim hell = (0.2F + 0.55F * diffus + 0.3F * kopflicht) * (1.0F - 0.15F * (tiefeMitte + 1.0F) / 2.0F)
            Dim glanz = CSng(Math.Pow(Math.Max(0F, kopflicht), 24)) * 40.0F
            Dim r = Math.Min(255, CInt(basisR * hell + glanz))
            Dim gr = Math.Min(255, CInt(basisG * hell + glanz))
            Dim b = Math.Min(255, CInt(basisB * hell + glanz))
            Dim c = (255 << 24) Or (r << 16) Or (gr << 8) Or b

            Dreieck(farbe, tiefe, w, h,
                    mx + vx(0) * skala, my - vy(0) * skala, vz(0),
                    mx + vx(1) * skala, my - vy(1) * skala, vz(1),
                    mx + vx(2) * skala, my - vy(2) * skala, vz(2), c)
        Next

        ' Hintergrund: senkrechter Verlauf in den Farben des Designs
        Dim oben = Theme.TabelleKopf, unten = Theme.Rand
        For iy = 0 To h - 1
            Dim a = CSng(iy) / Math.Max(1, h - 1)
            Dim hg = (255 << 24) Or (CInt(oben.R + (CInt(unten.R) - oben.R) * a) << 16) Or
                     (CInt(oben.G + (CInt(unten.G) - oben.G) * a) << 8) Or CInt(oben.B + (CInt(unten.B) - oben.B) * a)
            Dim zeile = iy * w
            For ix = 0 To w - 1
                If tiefe(zeile + ix) = Single.MaxValue Then farbe(zeile + ix) = hg
            Next
        Next

        ' Kantenglättung: 2×2 Pixel mitteln
        Dim ergebnis As Integer()
        If faktor = 1 Then
            ergebnis = farbe
        Else
            ReDim ergebnis(breite * hoehe - 1)
            For iy = 0 To hoehe - 1
                Dim z0 = (iy * 2) * w, z1 = (iy * 2 + 1) * w
                For ix = 0 To breite - 1
                    Dim p0 = farbe(z0 + ix * 2), p1 = farbe(z0 + ix * 2 + 1), p2 = farbe(z1 + ix * 2), p3 = farbe(z1 + ix * 2 + 1)
                    Dim rr = (((p0 >> 16) And 255) + ((p1 >> 16) And 255) + ((p2 >> 16) And 255) + ((p3 >> 16) And 255)) >> 2
                    Dim gg = (((p0 >> 8) And 255) + ((p1 >> 8) And 255) + ((p2 >> 8) And 255) + ((p3 >> 8) And 255)) >> 2
                    Dim bb = ((p0 And 255) + (p1 And 255) + (p2 And 255) + (p3 And 255)) >> 2
                    ergebnis(iy * breite + ix) = (255 << 24) Or (rr << 16) Or (gg << 8) Or bb
                Next
            Next
        End If

        Dim bild As New Bitmap(breite, hoehe, PixelFormat.Format32bppArgb)
        Dim daten = bild.LockBits(New Rectangle(0, 0, breite, hoehe), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        Try
            Marshal.Copy(ergebnis, 0, daten.Scan0, ergebnis.Length)
        Finally
            bild.UnlockBits(daten)
        End Try
        Return bild
    End Function

    ''' <summary>Rastert ein Dreieck mit Z-Buffer (kleinere Tiefe = näher an der Kamera).</summary>
    Private Shared Sub Dreieck(farbe As Integer(), tiefe As Single(), w As Integer, h As Integer,
                               x0 As Single, y0 As Single, z0 As Single,
                               x1 As Single, y1 As Single, z1 As Single,
                               x2 As Single, y2 As Single, z2 As Single, c As Integer)
        Dim flaeche = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
        If Math.Abs(flaeche) < 0.000001F Then Return
        Dim inv = 1.0F / flaeche
        Dim minX = Math.Max(0, CInt(Math.Floor(Math.Min(x0, Math.Min(x1, x2)))))
        Dim maxX = Math.Min(w - 1, CInt(Math.Ceiling(Math.Max(x0, Math.Max(x1, x2)))))
        Dim minY = Math.Max(0, CInt(Math.Floor(Math.Min(y0, Math.Min(y1, y2)))))
        Dim maxY = Math.Min(h - 1, CInt(Math.Ceiling(Math.Max(y0, Math.Max(y1, y2)))))
        If minX > maxX OrElse minY > maxY Then Return
        Const Toleranz As Single = -0.0001F

        For iy = minY To maxY
            Dim pyc = iy + 0.5F
            Dim zeile = iy * w
            For ix = minX To maxX
                Dim pxc = ix + 0.5F
                Dim w0 = ((x1 - pxc) * (y2 - pyc) - (x2 - pxc) * (y1 - pyc)) * inv
                If w0 < Toleranz Then Continue For
                Dim w1 = ((x2 - pxc) * (y0 - pyc) - (x0 - pxc) * (y2 - pyc)) * inv
                If w1 < Toleranz Then Continue For
                Dim w2 = 1.0F - w0 - w1
                If w2 < Toleranz Then Continue For
                Dim z = w0 * z0 + w1 * z1 + w2 * z2
                Dim i = zeile + ix
                If z < tiefe(i) Then
                    tiefe(i) = z
                    farbe(i) = c
                End If
            Next
        Next
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _feinTimer.Dispose()
            _bild?.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class
