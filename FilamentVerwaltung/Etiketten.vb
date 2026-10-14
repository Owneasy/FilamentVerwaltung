Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Printing
Imports QRCoder

''' <summary>Etikettenformat: entweder ein A4-Bogen mit mehreren Etiketten oder ein Einzeletikett (Etikettendrucker).</summary>
Public Class EtikettFormat
    Public Property Name As String = ""
    ''' <summary>Größe eines Etiketts in mm.</summary>
    Public Property Breite As Single
    Public Property Hoehe As Single
    Public Property Spalten As Integer = 1
    Public Property Zeilen As Integer = 1
    ''' <summary>Abstand vom Blattrand zum ersten Etikett in mm.</summary>
    Public Property RandLinks As Single
    Public Property RandOben As Single
    ''' <summary>Zwischenraum zwischen den Etiketten in mm.</summary>
    Public Property AbstandX As Single
    Public Property AbstandY As Single
    ''' <summary>True = Etikettendrucker, ein Etikett pro Seite in Etikettengröße.</summary>
    Public Property Einzeln As Boolean

    Public ReadOnly Property ProBogen As Integer
        Get
            Return Spalten * Zeilen
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return Name
    End Function

    Public Shared ReadOnly Formate As EtikettFormat() = {
        New EtikettFormat With {.Name = "A4-Bogen · 3 × 8 · 70 × 37 mm (z. B. Avery 3474)", .Breite = 70, .Hoehe = 37, .Spalten = 3, .Zeilen = 8, .RandOben = 0.5},
        New EtikettFormat With {.Name = "A4-Bogen · 3 × 7 · 70 × 42,3 mm (z. B. Avery 3652)", .Breite = 70, .Hoehe = 42.3, .Spalten = 3, .Zeilen = 7, .RandOben = 0.45},
        New EtikettFormat With {.Name = "A4-Bogen · 2 × 7 · 99,1 × 38,1 mm (z. B. Avery L7163)", .Breite = 99.1, .Hoehe = 38.1, .Spalten = 2, .Zeilen = 7,
                                .RandLinks = 4.65, .RandOben = 15.15, .AbstandX = 2.5},
        New EtikettFormat With {.Name = "Etikettendrucker · 62 × 29 mm (z. B. Brother DK-11209)", .Breite = 62, .Hoehe = 29, .Einzeln = True},
        New EtikettFormat With {.Name = "Etikettendrucker · 54 × 25 mm (z. B. Dymo 11352)", .Breite = 54, .Hoehe = 25, .Einzeln = True}
    }
End Class

''' <summary>
''' Zeichnet und druckt Spulen-Etiketten: QR-Code mit der ID links, Angaben zur Spule rechts.
''' Alle Maße in Millimetern (Graphics.PageUnit = Millimeter).
''' </summary>
Public Class EtikettenDruck
    Inherits PrintDocument

    Private ReadOnly _spulen As List(Of Filament)
    Private ReadOnly _format As EtikettFormat
    Private ReadOnly _startPosition As Integer
    Private ReadOnly _rahmen As Boolean
    Private _naechste As Integer

    ''' <param name="startPosition">Erstes freie Etikett auf dem ersten Bogen (1 = oben links), für angebrochene Bögen.</param>
    Public Sub New(spulen As IEnumerable(Of Filament), format As EtikettFormat, startPosition As Integer, rahmen As Boolean)
        _spulen = spulen.ToList()
        _format = format
        _startPosition = Math.Max(1, Math.Min(startPosition, format.ProBogen))
        _rahmen = rahmen
        DocumentName = "Spulen-Etiketten"
        If format.Einzeln Then
            ' Seitengröße = Etikett (Angabe in 1/100 Zoll)
            Dim breite = CInt(Math.Round(format.Breite / 25.4 * 100)), hoehe = CInt(Math.Round(format.Hoehe / 25.4 * 100))
            DefaultPageSettings.PaperSize = New PaperSize($"Etikett {format.Breite} × {format.Hoehe} mm", breite, hoehe)
            DefaultPageSettings.Landscape = False
        Else
            For Each ps As PaperSize In PrinterSettings.PaperSizes
                If ps.Kind = PaperKind.A4 Then
                    DefaultPageSettings.PaperSize = ps
                    Exit For
                End If
            Next
        End If
        DefaultPageSettings.Margins = New Margins(0, 0, 0, 0)
    End Sub

    Public ReadOnly Property Seiten As Integer
        Get
            If _spulen.Count = 0 Then Return 0
            If _format.Einzeln Then Return _spulen.Count
            Return CInt(Math.Ceiling((_spulen.Count + _startPosition - 1) / CDbl(_format.ProBogen)))
        End Get
    End Property

    Protected Overrides Sub OnBeginPrint(e As PrintEventArgs)
        MyBase.OnBeginPrint(e)
        _naechste = 0
    End Sub

    Protected Overrides Sub OnPrintPage(e As PrintPageEventArgs)
        MyBase.OnPrintPage(e)
        Dim g = e.Graphics
        g.PageUnit = GraphicsUnit.Millimeter
        ' Der Ursprung liegt am Rand des bedruckbaren Bereichs → auf die Blattecke zurückrechnen
        If Not PrintController.IsPreview Then
            g.TranslateTransform(-e.PageSettings.HardMarginX * 0.254F, -e.PageSettings.HardMarginY * 0.254F)
        End If
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit

        If _format.Einzeln Then
            If _naechste < _spulen.Count Then Zeichnen(g, _spulen(_naechste), New RectangleF(0, 0, _format.Breite, _format.Hoehe), _rahmen)
            _naechste += 1
        Else
            ' Auf dem ersten Bogen ab der Startposition beginnen
            Dim platz = If(_naechste = 0, _startPosition - 1, 0)
            While platz < _format.ProBogen AndAlso _naechste < _spulen.Count
                Dim sp = platz Mod _format.Spalten, ze = platz \ _format.Spalten
                Dim r As New RectangleF(_format.RandLinks + sp * (_format.Breite + _format.AbstandX),
                                        _format.RandOben + ze * (_format.Hoehe + _format.AbstandY),
                                        _format.Breite, _format.Hoehe)
                Zeichnen(g, _spulen(_naechste), r, _rahmen)
                _naechste += 1
                platz += 1
            End While
        End If
        e.HasMorePages = _naechste < _spulen.Count
    End Sub

    ''' <summary>Zeichnet ein Etikett in das Rechteck (mm).</summary>
    Public Shared Sub Zeichnen(g As Graphics, f As Filament, r As RectangleF, rahmen As Boolean)
        If rahmen Then
            Using p As New Pen(Color.FromArgb(200, 200, 200), 0.2F)
                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height)
            End Using
        End If
        Dim pad = Math.Max(1.8F, r.Height * 0.07F)
        Dim skala = r.Height / 37.0F

        ' QR-Code links, quadratisch über die volle Höhe
        Dim qrGroesse = r.Height - 2 * pad
        QrZeichnen(g, f.Code, New RectangleF(r.X + pad * 0.6F, r.Y + pad, qrGroesse, qrGroesse))

        ' Text rechts
        Dim x = r.X + pad * 0.6F + qrGroesse + pad * 0.8F
        Dim breite = r.Right - pad - x
        Dim y = r.Y + pad * 0.9F
        Using fCode As New Font("Segoe UI Semibold", 13.0F * skala, GraphicsUnit.Point),
              fNormal As New Font("Segoe UI", 8.0F * skala, GraphicsUnit.Point),
              fFett As New Font("Segoe UI Semibold", 8.5F * skala, GraphicsUnit.Point),
              fKlein As New Font("Segoe UI", 6.5F * skala, GraphicsUnit.Point),
              format As New StringFormat With {.Trimming = StringTrimming.EllipsisCharacter, .FormatFlags = StringFormatFlags.NoWrap}
            y = Zeile(g, f.Code, fCode, Brushes.Black, x, y, breite, format)
            y += 0.4F * skala
            y = Zeile(g, f.Hersteller, fFett, Brushes.Black, x, y, breite, format)

            ' Material · Farbe mit Farbpunkt
            Dim farbe = FarbeZuName(f.Farbe)
            Dim textX = x
            Dim hoeheNormal = fNormal.GetHeight(g)
            If farbe.HasValue Then
                Dim d = hoeheNormal * 0.62F
                Dim punkt As New RectangleF(x, y + (hoeheNormal - d) / 2, d, d)
                Using b As New SolidBrush(farbe.Value), p As New Pen(Color.FromArgb(120, 0, 0, 0), 0.15F)
                    g.FillEllipse(b, punkt)
                    g.DrawEllipse(p, punkt)
                End Using
                textX += d + 1.0F
            End If
            y = Zeile(g, String.Join(" · ", {f.Material, f.Farbe}.Where(Function(t) t.Length > 0)), fNormal, Brushes.Black,
                      textX, y, breite - (textX - x), format)

            Dim details As New List(Of String) From {$"Ø {f.Durchmesser:0.00} mm", $"{f.Gewicht:N0} g"}
            If f.Temperaturen.Length > 0 Then details.Add(f.Temperaturen)
            y = Zeile(g, String.Join(" · ", details), fKlein, Brushes.DimGray, x, y + 0.3F * skala, breite, format)
            Dim unten = String.Join(" · ", {f.Lagerort, If(f.Kaufdatum > Date.MinValue, $"gekauft {f.Kaufdatum:MM/yyyy}", "")}.Where(Function(t) t.Length > 0))
            If unten.Length > 0 AndAlso y + fKlein.GetHeight(g) <= r.Bottom - pad * 0.5F Then
                Zeile(g, unten, fKlein, Brushes.DimGray, x, y, breite, format)
            End If
        End Using
    End Sub

    Private Shared Function Zeile(g As Graphics, text As String, f As Font, b As Brush, x As Single, y As Single,
                                  breite As Single, format As StringFormat) As Single
        Dim h = f.GetHeight(g)
        If text.Length > 0 Then g.DrawString(text, f, b, New RectangleF(x, y, Math.Max(1, breite), h), format)
        Return y + h
    End Function

    ''' <summary>QR-Code als gefüllte Rechtecke (scharf bei jeder Druckauflösung), mit Ruhezone.</summary>
    Public Shared Sub QrZeichnen(g As Graphics, text As String, r As RectangleF)
        Using gen As New QRCodeGenerator(), daten = gen.CreateQrCode(If(String.IsNullOrEmpty(text), "-", text), QRCodeGenerator.ECCLevel.M)
            Dim matrix = daten.ModuleMatrix
            Dim n = matrix.Count
            Dim modul = Math.Min(r.Width, r.Height) / n
            ' Kleine Überlappung verhindert feine Lücken zwischen den Modulen
            Dim ueber = modul * 0.04F
            For zi As Integer = 0 To n - 1
                For si As Integer = 0 To n - 1
                    If matrix(zi)(si) Then
                        g.FillRectangle(Brushes.Black, r.X + si * modul, r.Y + zi * modul, modul + ueber, modul + ueber)
                    End If
                Next
            Next
        End Using
    End Sub

End Class
