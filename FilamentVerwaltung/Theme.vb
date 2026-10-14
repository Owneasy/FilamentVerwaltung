Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

''' <summary>
''' Farben, Schriften und Zeichenhilfen für die Oberfläche im Web-Dashboard-Stil.
''' Alle Farben an einer Stelle – zum Anpassen des Aussehens nur hier ändern.
''' </summary>
Public Module Theme

    ' --- Flächen
    Public ReadOnly Seite As Color = ColorTranslator.FromHtml("#F1F5F9")
    Public ReadOnly KartenFlaeche As Color = Color.White
    Public ReadOnly Rand As Color = ColorTranslator.FromHtml("#E2E8F0")
    Public ReadOnly RandEingabe As Color = ColorTranslator.FromHtml("#CBD5E1")
    Public ReadOnly TabelleKopf As Color = ColorTranslator.FromHtml("#F8FAFC")
    Public ReadOnly TabelleLinie As Color = ColorTranslator.FromHtml("#EEF2F7")
    Public ReadOnly Auswahl As Color = ColorTranslator.FromHtml("#EFF6FF")

    ' --- Seitenleiste
    Public ReadOnly Leiste As Color = ColorTranslator.FromHtml("#0F172A")
    Public ReadOnly LeisteHover As Color = ColorTranslator.FromHtml("#1E293B")
    Public ReadOnly LeisteText As Color = ColorTranslator.FromHtml("#94A3B8")
    Public ReadOnly LeisteTextAktiv As Color = Color.White

    ' --- Text
    Public ReadOnly TextStark As Color = ColorTranslator.FromHtml("#0F172A")
    Public ReadOnly TextNormal As Color = ColorTranslator.FromHtml("#334155")
    Public ReadOnly TextLeise As Color = ColorTranslator.FromHtml("#64748B")

    ' --- Akzente
    Public ReadOnly Akzent As Color = ColorTranslator.FromHtml("#2563EB")
    Public ReadOnly AkzentHover As Color = ColorTranslator.FromHtml("#1D4ED8")
    Public ReadOnly AkzentGedrueckt As Color = ColorTranslator.FromHtml("#1E40AF")
    Public ReadOnly Gruen As Color = ColorTranslator.FromHtml("#16A34A")
    Public ReadOnly Gelb As Color = ColorTranslator.FromHtml("#D97706")
    Public ReadOnly Rot As Color = ColorTranslator.FromHtml("#DC2626")
    Public ReadOnly RotHell As Color = ColorTranslator.FromHtml("#FEF2F2")
    Public ReadOnly RotRand As Color = ColorTranslator.FromHtml("#FECACA")
    Public ReadOnly Grau As Color = ColorTranslator.FromHtml("#94A3B8")

    ' --- Schriften
    Public ReadOnly Schrift As New Font("Segoe UI", 10.0F)
    Public ReadOnly SchriftKlein As New Font("Segoe UI", 9.0F)
    Public ReadOnly SchriftFett As New Font("Segoe UI Semibold", 10.0F)
    Public ReadOnly SchriftKopf As New Font("Segoe UI Semibold", 9.0F)
    Public ReadOnly SchriftTitel As New Font("Segoe UI Semibold", 18.0F)
    Public ReadOnly SchriftLogo As New Font("Segoe UI Semibold", 12.0F)
    Public ReadOnly SchriftKennzahl As New Font("Segoe UI Semibold", 17.0F)
    Public ReadOnly SymbolSchrift As Font = SymbolFont(11.0F)
    Public ReadOnly SymbolSchriftGross As Font = SymbolFont(14.0F)
    Public ReadOnly SymbolSchriftRiesig As Font = SymbolFont(36.0F)

    ' --- Symbole (Segoe Fluent Icons / Segoe MDL2 Assets)
    Public Const SymNeu As String = ChrW(&HE710)
    Public Const SymBearbeiten As String = ChrW(&HE70F)
    Public Const SymKopieren As String = ChrW(&HE8C8)
    Public Const SymLoeschen As String = ChrW(&HE74D)
    Public Const SymSuche As String = ChrW(&HE721)
    Public Const SymExport As String = ChrW(&HE896)
    Public Const SymOrdnerOffen As String = ChrW(&HE838)
    Public Const SymOrdner As String = ChrW(&HE8B7)
    Public Const SymWaage As String = ChrW(&HE8EF)
    Public Const SymSpule As String = ChrW(&HE81E)
    Public Const SymBox As String = ChrW(&HE7B8)
    Public Const SymDiagramm As String = ChrW(&HE9D2)
    Public Const SymGeld As String = ChrW(&HE8C7)
    Public Const SymWarnung As String = ChrW(&HE7BA)
    Public Const SymWuerfel As String = ChrW(&HF158)
    Public Const SymSpalten As String = ChrW(&HE8FD)
    Public Const SymDrucker As String = ChrW(&HE749)
    Public Const SymUhr As String = ChrW(&HE916)
    Public Const SymVerlauf As String = ChrW(&HE81C)
    Public Const SymHinzufuegen As String = ChrW(&HE8F4)
    Public Const SymOeffnen As String = ChrW(&HE8E5)
    Public Const SymDatei As String = ChrW(&HE8A5)
    Public Const SymSicherung As String = ChrW(&HE74E)

    ' --- Farben für Buchungsarten
    Public ReadOnly Tuerkis As Color = ColorTranslator.FromHtml("#0D9488")
    Public ReadOnly Lila As Color = ColorTranslator.FromHtml("#7C3AED")

    Private Function SymbolFont(groesse As Single) As Font
        Dim f As New Font("Segoe Fluent Icons", groesse)
        If f.Name = "Segoe Fluent Icons" Then Return f
        f.Dispose()
        Return New Font("Segoe MDL2 Assets", groesse)
    End Function

    ''' <summary>Rechnet einen Wert bei 96 DPI in Pixel für den Bildschirm des Steuerelements um.</summary>
    Public Function Px(c As Control, wert As Integer) As Integer
        Return c.LogicalToDeviceUnits(wert)
    End Function

    ''' <summary>Rechteck mit abgerundeten Ecken.</summary>
    Public Function Abgerundet(r As Rectangle, radius As Integer) As GraphicsPath
        Dim p As New GraphicsPath()
        Dim d = Math.Min(radius * 2, Math.Min(r.Width, r.Height))
        If d <= 0 Then
            p.AddRectangle(r)
            Return p
        End If
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function

    ''' <summary>Mischt eine Farbe mit Weiß (anteil 0 = weiß, 1 = Originalfarbe).</summary>
    Public Function Hell(c As Color, anteil As Double) As Color
        Return Color.FromArgb(CInt(255 - (255 - c.R) * anteil),
                              CInt(255 - (255 - c.G) * anteil),
                              CInt(255 - (255 - c.B) * anteil))
    End Function

    ' --- Titelleiste einfärben (ab Windows 11, unter Windows 10 ohne Wirkung)

    <DllImport("dwmapi.dll")>
    Private Function DwmSetWindowAttribute(hwnd As IntPtr, attr As Integer, ByRef wert As Integer, groesse As Integer) As Integer
    End Function

    Private Const DWMWA_BORDER_COLOR As Integer = 34
    Private Const DWMWA_CAPTION_COLOR As Integer = 35
    Private Const DWMWA_TEXT_COLOR As Integer = 36

    Public Sub TitelleisteFaerben(f As Form, hintergrund As Color, schrift As Color)
        Try
            Dim bg = ColorTranslator.ToWin32(hintergrund)
            Dim fg = ColorTranslator.ToWin32(schrift)
            DwmSetWindowAttribute(f.Handle, DWMWA_CAPTION_COLOR, bg, 4)
            DwmSetWindowAttribute(f.Handle, DWMWA_BORDER_COLOR, bg, 4)
            DwmSetWindowAttribute(f.Handle, DWMWA_TEXT_COLOR, fg, 4)
        Catch ex As Exception
            ' Ältere Windows-Versionen: Standard-Titelleiste behalten
        End Try
    End Sub

    ''' <summary>Gibt einem Dialog das helle Aussehen der Hauptoberfläche.</summary>
    Public Sub DialogGestalten(f As Form)
        f.BackColor = KartenFlaeche
        f.ForeColor = TextNormal
        f.Font = Schrift
    End Sub

    ''' <summary>Ordnet bekannten Farbnamen eine Anzeigefarbe zu (für den Farbpunkt in der Tabelle).</summary>
    Public Function FarbeZuName(name As String) As Color?
        If String.IsNullOrWhiteSpace(name) Then Return Nothing
        Dim n = name.ToLowerInvariant()
        Dim tabelle As (Wort As String, Farbe As String)() = {
            ("schwarz", "#111827"), ("black", "#111827"), ("weiß", "#FFFFFF"), ("weiss", "#FFFFFF"), ("white", "#FFFFFF"),
            ("natur", "#F5F0E1"), ("transparent", "#E0F2FE"), ("klar", "#E0F2FE"),
            ("dunkelgrau", "#4B5563"), ("grau", "#9CA3AF"), ("grey", "#9CA3AF"), ("gray", "#9CA3AF"), ("silber", "#C0C4CC"),
            ("gold", "#D4A72C"), ("dunkelblau", "#1E3A8A"), ("hellblau", "#7DD3FC"), ("blau", "#2563EB"), ("blue", "#2563EB"),
            ("türkis", "#14B8A6"), ("tuerkis", "#14B8A6"), ("dunkelgrün", "#166534"), ("hellgrün", "#86EFAC"),
            ("grün", "#16A34A"), ("gruen", "#16A34A"), ("green", "#16A34A"), ("gelb", "#FACC15"), ("yellow", "#FACC15"),
            ("orange", "#F97316"), ("dunkelrot", "#991B1B"), ("rot", "#DC2626"), ("red", "#DC2626"),
            ("rosa", "#F9A8D4"), ("pink", "#EC4899"), ("magenta", "#D946EF"), ("lila", "#9333EA"), ("violett", "#7C3AED"),
            ("purple", "#9333EA"), ("braun", "#92400E"), ("brown", "#92400E"), ("holz", "#B07A4A"), ("beige", "#E7D8B9")}
        For Each eintrag In tabelle
            If n.Contains(eintrag.Wort) Then Return ColorTranslator.FromHtml(eintrag.Farbe)
        Next
        Return Nothing
    End Function

End Module
