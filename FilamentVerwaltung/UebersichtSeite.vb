Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

''' <summary>
''' Startseite "Übersicht": Kennzahlen, laufende Drucke (live), Verbrauch der letzten Wochen,
''' letzte Drucke und was nachgekauft werden sollte.
''' </summary>
Public Class UebersichtSeite
    Inherits UserControl
    Implements ISeite

    Public Const ZielFilament As String = "Filament"
    Public Const ZielDrucke As String = "Drucke"
    Public Const ZielDrucker As String = "Drucker"
    Public Const ZielNachkauf As String = "Nachkauf"
    Public Const ZielStatistik As String = "Statistik"

    ''' <summary>"Alle anzeigen" wurde geklickt – MainForm wechselt zur passenden Seite.</summary>
    Public Event SeiteOeffnen(ziel As String)

    Private ReadOnly _db As Datenbank
    Private ReadOnly _ueberwachung As DruckerUeberwachung
    Private _drucker As New List(Of Drucker)

    Private ReadOnly kopf As New SeitenKopf("Übersicht", "")
    Private ReadOnly kzLager As New KennzahlKarte("Lagerbestand", Theme.SymSpule, Theme.Akzent)
    Private ReadOnly kzDrucker As New KennzahlKarte("Drucker", Theme.SymNetzDrucker, Theme.Tuerkis)
    Private ReadOnly kzMonat As New KennzahlKarte("Verbrauch diesen Monat", Theme.SymDiagramm, Theme.Lila)
    Private ReadOnly kzNachkauf As New KennzahlKarte("Nachkaufen", Theme.SymWarnung, Theme.Rot)

    Private ReadOnly karteDrucker As New ListenKarte("Drucker", "Live-Status", "Zu den Druckern", spalten:=2)
    Private ReadOnly wochen As New SaeulenDiagramm With {.LeerText = "Noch kein Verbrauch erfasst – buche Drucke oder wiege Spulen nach."}
    Private ReadOnly karteWochen As DiagrammKarte
    Private ReadOnly karteDrucke As New ListenKarte("Letzte Drucke", "", "Alle Drucke")
    Private ReadOnly karteNachkauf As New ListenKarte("Nachkaufen", "Sorten, die knapp werden", "Zur Nachkaufliste")
    Private _druckerSignatur As String = Nothing

    Public Sub New(db As Datenbank, ueberwachung As DruckerUeberwachung)
        _db = db
        _ueberwachung = ueberwachung
        karteWochen = New DiagrammKarte("Verbrauch pro Woche", "Letzte 12 Kalenderwochen", wochen, "Verbrauch")
        OberflaecheAufbauen()
    End Sub

    Public Sub Aktivieren() Implements ISeite.Aktivieren
    End Sub

    Public Sub Neuladen() Implements ISeite.Neuladen
        DatenLaden()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        Dock = DockStyle.Fill
        BackColor = Theme.Seite
        Font = Theme.Schrift
        Padding = New Padding(28, 24, 28, 24)

        Dim seite As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4, .Margin = New Padding(0)}
        seite.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        seite.RowStyles.Add(New RowStyle(SizeType.Absolute, 134))
        seite.RowStyles.Add(New RowStyle(SizeType.Percent, 48))
        seite.RowStyles.Add(New RowStyle(SizeType.Percent, 52))
        seite.Controls.Add(kopf, 0, 0)
        seite.Controls.Add(FilamentSeite.KennzahlZeile(kzLager, kzDrucker, kzMonat, kzNachkauf), 0, 1)
        karteDrucker.Dock = DockStyle.Fill
        karteDrucker.Margin = New Padding(0, 0, 0, 16)
        seite.Controls.Add(karteDrucker, 0, 2)
        seite.Controls.Add(Nebeneinander(karteDrucke, karteWochen, karteNachkauf), 0, 3)

        AddHandler karteDrucker.AlleGeklickt, Sub() RaiseEvent SeiteOeffnen(ZielDrucker)
        AddHandler karteDrucke.AlleGeklickt, Sub() RaiseEvent SeiteOeffnen(ZielDrucke)
        AddHandler karteNachkauf.AlleGeklickt, Sub() RaiseEvent SeiteOeffnen(ZielNachkauf)
        AddHandler _ueberwachung.StatusGeaendert, Sub(id)
                                                      If Visible Then DruckerAktualisieren()
                                                  End Sub
        Controls.Add(seite)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    ''' <summary>Karten gleich breit nebeneinander.</summary>
    Private Shared Function Nebeneinander(ParamArray karten As Control()) As TableLayoutPanel
        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = karten.Length, .RowCount = 1, .Margin = New Padding(0)}
        For i = 0 To karten.Length - 1
            t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F / karten.Length))
            karten(i).Dock = DockStyle.Fill
            karten(i).Margin = New Padding(If(i = 0, 0, 8), 0, If(i = karten.Length - 1, 0, 8), 0)
            t.Controls.Add(karten(i), i, 0)
        Next
        Return t
    End Function

    ' ------------------------------------------------------------------ Daten

    Private Sub DatenLaden()
        Dim filamente As List(Of Filament), buchungen As List(Of Buchung)
        Dim einstellungen As (Mindestbestand As Integer, Ausgeblendet As HashSet(Of String))
        Try
            filamente = _db.AlleLaden()
            buchungen = _db.BuchungenLaden()
            _drucker = _db.DruckerLaden()
            einstellungen = NachkaufSeite.EinstellungenLesen(_db)
        Catch ex As Exception
            MessageBox.Show(Me, "Die Übersicht konnte nicht geladen werden." & Environment.NewLine & Environment.NewLine & ex.Message,
                            "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        Dim kultur = CultureInfo.CurrentCulture
        Dim stunde = Date.Now.Hour
        Dim gruss = If(stunde < 11, "Guten Morgen", If(stunde < 18, "Hallo", "Guten Abend"))
        kopf.Untertitel = $"{gruss} – {Date.Today.ToString("dddd, d. MMMM yyyy", kultur)}"

        ' Lager
        Dim mitRest = filamente.Where(Function(f) f.Restgewicht > 0).ToList()
        Dim fastLeer = mitRest.Where(Function(f) f.Restgewicht < 150).Count()
        kzLager.Setzen(KiloText(mitRest.Sum(Function(f) CDbl(f.Restgewicht))),
                       $"{mitRest.Count} {If(mitRest.Count = 1, "Spule", "Spulen")} mit Rest" & If(fastLeer > 0, $" · {fastLeer} fast leer", ""))

        ' Dieser Monat
        Dim monatsbeginn = New Date(Date.Today.Year, Date.Today.Month, 1)
        Dim imMonat = buchungen.Where(Function(b) IstVerbrauch(b) AndAlso b.Datum >= monatsbeginn).ToList()
        Dim druckeImMonat = imMonat.Where(Function(b) b.IstDruck).Count()
        Dim kosten = imMonat.Sum(Function(b) b.Kosten)
        kzMonat.Setzen(KiloText(imMonat.Sum(Function(b) CDbl(b.Gramm))),
                       $"{druckeImMonat} {If(druckeImMonat = 1, "Druck", "Drucke")}" & If(kosten > 0, $" · {kosten.ToString("C", kultur)} Material", ""))

        ' Nachkauf
        Dim sorten = NachkaufSeite.SortenBerechnen(filamente, buchungen, einstellungen.Mindestbestand, einstellungen.Ausgeblendet).
                     Where(Function(s) Not s.Ausgeblendet).ToList()
        Dim nachkaufen = sorten.Where(Function(s) s.Status = SorteInfo.StatusNachkaufen).ToList()
        Dim bald = sorten.Where(Function(s) s.Status = SorteInfo.StatusBald).ToList()
        kzNachkauf.Setzen(If(nachkaufen.Count = 0, "Alles da", $"{nachkaufen.Count} {If(nachkaufen.Count = 1, "Sorte", "Sorten")}"),
                          If(bald.Count > 0, $"{bald.Count} weitere bald knapp", If(nachkaufen.Count > 0, "jetzt nachbestellen", "nichts wird knapp")))
        NachkaufFuellen(nachkaufen.Concat(bald).OrderBy(Function(s) s.Dringlichkeit).ThenBy(Function(s) s.ReichweiteTage).ThenBy(Function(s) s.Rest).Take(6).ToList())

        WochenFuellen(buchungen, kultur)
        DruckeFuellen(buchungen.Where(Function(b) b.IstDruck).OrderByDescending(Function(b) b.Datum).Take(6).ToList())
        _druckerSignatur = Nothing
        DruckerAktualisieren()
    End Sub

    Private Shared Function KiloText(gramm As Double) As String
        Return If(gramm >= 1000, $"{gramm / 1000:N1} kg", $"{gramm:N0} g")
    End Function

    Private Sub WochenFuellen(buchungen As List(Of Buchung), kultur As CultureInfo)
        Dim montag = Date.Today.AddDays(-((CInt(Date.Today.DayOfWeek) + 6) Mod 7))
        Dim werte As New List(Of DiagrammWert)
        Dim summe = 0.0
        For i = 11 To 0 Step -1
            Dim von = montag.AddDays(-7 * i), bis = von.AddDays(7)
            Dim woche = buchungen.Where(Function(b) IstVerbrauch(b) AndAlso b.Datum >= von AndAlso b.Datum < bis).ToList()
            Dim gramm = woche.Sum(Function(b) CDbl(b.Gramm))
            summe += gramm
            Dim kw = ISOWeek.GetWeekOfYear(von)
            werte.Add(New DiagrammWert With {
                .Beschriftung = CStr(kw),
                .Titel = $"KW {kw} ({von:dd.MM.} – {bis.AddDays(-1):dd.MM.})",
                .Wert = gramm,
                .WertText = KiloText(gramm),
                .Zusatz = $"{woche.Where(Function(b) b.IstDruck).Count()} Drucke",
                .Hervorheben = i = 0})
        Next
        karteWochen.Setzen(If(summe > 0, werte, New List(Of DiagrammWert)))
    End Sub

    Private Sub DruckeFuellen(drucke As List(Of Buchung))
        karteDrucke.Leeren()
        If drucke.Count = 0 Then
            karteDrucke.Hinweis("Noch keine Drucke gebucht.")
            Return
        End If
        For Each b In drucke
            Dim titel = If(b.Bezeichnung.Length > 0, b.Bezeichnung, If(b.DateiName.Length > 0, b.DateiName, "Druck"))
            karteDrucke.Zeile(b.Datum.ToString("dd.MM."), titel, $"{b.FilamentCode} · {b.FilamentText}", $"{b.Gramm:N0} g")
        Next
    End Sub

    Private Sub NachkaufFuellen(sorten As List(Of SorteInfo))
        karteNachkauf.Leeren()
        If sorten.Count = 0 Then
            karteNachkauf.Hinweis("Alles gut – keine Sorte wird knapp.")
            Return
        End If
        For Each s In sorten
            Dim farbe = If(s.Status = SorteInfo.StatusNachkaufen, Theme.Rot, Theme.Gelb)
            karteNachkauf.Zeile(If(s.Status = SorteInfo.StatusNachkaufen, "Nachkaufen", "Bald"), s.Bezeichnung,
                                $"{s.Spulen} {If(s.Spulen = 1, "Spule", "Spulen")} · " &
                                If(s.Rest <= 0, "aufgebraucht", If(s.ProMonat <= 0, "zuletzt nicht genutzt", "reicht " & s.ReichweiteText)), $"{s.Rest:N0} g", farbe)
        Next
    End Sub

    ' ------------------------------------------------------------------ Drucker (live)

    Private Sub DruckerAktualisieren()
        Dim aktive = _drucker.Where(Function(d) d.Aktiv).ToList()
        Dim status = aktive.Select(Function(d) (Drucker:=d, Status:=_ueberwachung.Status(d.Id))).
                            OrderBy(Function(x) If(x.Status IsNot Nothing AndAlso x.Status.DruckLaeuft, 0, 1)).ThenBy(Function(x) x.Drucker.Name).ToList()

        ' Kennzahl
        Dim druckend = status.Where(Function(x) x.Status IsNot Nothing AndAlso x.Status.DruckLaeuft).ToList()
        Dim offline = status.Where(Function(x) x.Status Is Nothing OrElse x.Status.Zustand = DruckerZustand.Offline).Count()
        If aktive.Count = 0 Then
            kzDrucker.Setzen("–", "kein Drucker angebunden")
        ElseIf druckend.Count > 0 Then
            Dim rest = druckend.Where(Function(x) x.Status.RestSekunden.HasValue).Select(Function(x) x.Status.RestSekunden.Value).DefaultIfEmpty(-1).Min()
            kzDrucker.Setzen(If(druckend.Count = 1, "1 Druck läuft", $"{druckend.Count} Drucke laufen"),
                             If(rest >= 0, $"fertig in {DauerText(rest)}", $"von {aktive.Count} Druckern"))
        Else
            kzDrucker.Setzen(If(offline = aktive.Count, "Offline", "Bereit"),
                             $"{aktive.Count} Drucker" & If(offline > 0 AndAlso offline < aktive.Count, $" · {offline} offline", ""))
        End If

        ' Zeilen: nur neu aufbauen, wenn sich Drucker oder Reihenfolge ändern
        Dim signatur = String.Join("|", status.Select(Function(x) x.Drucker.Id))
        If signatur <> _druckerSignatur Then
            _druckerSignatur = signatur
            karteDrucker.Leeren()
            If aktive.Count = 0 Then
                karteDrucker.Hinweis("Noch kein Drucker angebunden – unter ""Drucker"" hinzufügen.")
                Return
            End If
            For Each x In status
                karteDrucker.Hinzufuegen(New DruckerZeile(x.Drucker))
            Next
        End If
        For Each zeile In karteDrucker.Inhalt.Controls.OfType(Of DruckerZeile)()
            zeile.Aktualisieren(_ueberwachung.Status(zeile.Drucker.Id))
        Next
    End Sub

End Class

''' <summary>Karte mit Titel, Untertitel, Link "Alle anzeigen" und einer Liste von Zeilen.</summary>
Friend Class ListenKarte
    Inherits Karte

    Public Event AlleGeklickt()
    ''' <summary>Zeilen; liegt oben angedockt in einem scrollbaren Rahmen (so entsteht nie ein waagrechter Scrollbalken).</summary>
    Public ReadOnly Inhalt As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Margin = New Padding(0)}
    Private ReadOnly rahmen As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Margin = New Padding(0)}
    Private ReadOnly lblUntertitel As Label
    Private ReadOnly _spalten As Integer
    Private _anzahl As Integer

    Public Sub New(titel As String, untertitel As String, linkText As String, Optional spalten As Integer = 1)
        _spalten = spalten
        Padding = New Padding(18, 14, 18, 12)
        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .Margin = New Padding(0)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))   ' sonst schiebt eine breite Kopfzeile den Inhalt rechts hinaus
        t.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        t.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim kopf As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .ColumnCount = 3, .Margin = New Padding(0, 0, 0, 8)}
        kopf.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        kopf.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        kopf.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        kopf.Controls.Add(New Label With {.Text = titel, .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark, .AutoSize = True,
                                          .Margin = New Padding(0, 0, 10, 0)}, 0, 0)
        lblUntertitel = New Label With {.Text = untertitel, .Font = Theme.SchriftKlein, .ForeColor = Theme.TextLeise, .AutoSize = False,
                                        .AutoEllipsis = True, .Dock = DockStyle.Fill, .Height = 18, .Margin = New Padding(0, 2, 8, 0)}
        kopf.Controls.Add(lblUntertitel, 1, 0)
        Dim link As New LinkLabel With {.Text = linkText & " →", .AutoSize = True, .Font = Theme.SchriftKlein, .LinkColor = Theme.AkzentText,
                                        .ActiveLinkColor = Theme.AkzentText, .LinkBehavior = LinkBehavior.HoverUnderline, .Margin = New Padding(0, 2, 0, 0)}
        AddHandler link.LinkClicked, Sub(s, e) RaiseEvent AlleGeklickt()
        kopf.Controls.Add(link, 2, 0)
        t.Controls.Add(kopf, 0, 0)

        Inhalt.ColumnCount = spalten
        For i = 1 To spalten
            Inhalt.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F / spalten))
        Next
        rahmen.Controls.Add(Inhalt)
        t.Controls.Add(rahmen, 0, 1)
        Controls.Add(t)
    End Sub

    Public Sub Leeren()
        Inhalt.SuspendLayout()
        For Each c In Inhalt.Controls.Cast(Of Control)().ToList()
            c.Dispose()
        Next
        Inhalt.Controls.Clear()
        Inhalt.RowStyles.Clear()
        Inhalt.RowCount = 0
        _anzahl = 0
        Inhalt.ResumeLayout()
    End Sub

    Public Sub Hinzufuegen(c As Control, Optional ganzeBreite As Boolean = False)
        c.Dock = DockStyle.Fill
        If ganzeBreite AndAlso _anzahl Mod _spalten <> 0 Then _anzahl += _spalten - _anzahl Mod _spalten
        Dim zeile = _anzahl \ _spalten, spalte = _anzahl Mod _spalten
        If spalte = 0 Then
            Inhalt.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            Inhalt.RowCount = zeile + 1
        End If
        If _spalten > 1 Then c.Margin = New Padding(If(spalte = 0, 0, 6), c.Margin.Top, If(spalte = _spalten - 1, 0, 6), c.Margin.Bottom)
        Inhalt.Controls.Add(c, spalte, zeile)
        If ganzeBreite Then
            Inhalt.SetColumnSpan(c, _spalten)
            _anzahl += _spalten
        Else
            _anzahl += 1
        End If
    End Sub

    Public Sub Hinweis(text As String)
        Hinzufuegen(New Label With {.Text = text, .AutoSize = True, .ForeColor = Theme.TextLeise, .Margin = New Padding(0, 6, 0, 0)}, ganzeBreite:=True)
    End Sub

    ''' <summary>Zeile: Markierung links (Datum oder Badge), Titel mit Unterzeile, Wert rechts.</summary>
    Public Sub Zeile(markierung As String, titel As String, unterzeile As String, wert As String, Optional badgeFarbe As Color? = Nothing)
        Dim z As New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 3, .RowCount = 2, .Margin = New Padding(0, 0, 0, 2),
                                            .Padding = New Padding(0, 5, 0, 5)}
        z.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, Px(Me, 96)))
        z.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        z.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        If badgeFarbe.HasValue Then
            Dim badge As New StatusBadge With {.Anchor = AnchorStyles.Left, .Margin = New Padding(0, 2, 0, 0)}
            badge.Setzen(markierung, badgeFarbe.Value)
            z.Controls.Add(badge, 0, 0)
        Else
            z.Controls.Add(New Label With {.Text = markierung, .AutoSize = True, .ForeColor = Theme.TextLeise, .Margin = New Padding(0, 1, 0, 0)}, 0, 0)
        End If
        z.SetRowSpan(z.Controls(0), 2)
        z.Controls.Add(New Label With {.Text = titel, .AutoSize = False, .AutoEllipsis = True, .Dock = DockStyle.Fill, .Height = Px(Me, 20),
                                       .Font = Theme.SchriftFett, .ForeColor = Theme.TextNormal, .Margin = New Padding(0)}, 1, 0)
        z.Controls.Add(New Label With {.Text = unterzeile, .AutoSize = False, .AutoEllipsis = True, .Dock = DockStyle.Fill, .Height = Px(Me, 18),
                                       .Font = Theme.SchriftKlein, .ForeColor = Theme.TextLeise, .Margin = New Padding(0)}, 1, 1)
        Dim lblWert As New Label With {.Text = wert, .AutoSize = True, .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark,
                                       .Anchor = AnchorStyles.Right, .Margin = New Padding(12, 0, 0, 0)}
        z.Controls.Add(lblWert, 2, 0)
        z.SetRowSpan(lblWert, 2)
        AddHandler z.Paint, Sub(s, e)
                                Using p As New Pen(Theme.Rand)
                                    e.Graphics.DrawLine(p, 0, z.Height - 1, z.Width, z.Height - 1)
                                End Using
                            End Sub
        Hinzufuegen(z)
    End Sub
End Class

''' <summary>Kompakte Live-Zeile eines Druckers: Name, Status, Datei, Fortschritt, Phase und Werte.</summary>
Friend Class DruckerZeile
    Inherits TableLayoutPanel

    Public ReadOnly Property Drucker As Drucker
    Private ReadOnly badge As New StatusBadge With {.Anchor = AnchorStyles.Right, .Margin = New Padding(0)}
    Private ReadOnly lblDatei As New Label With {.AutoSize = False, .AutoEllipsis = True, .Dock = DockStyle.Fill, .Height = 20,
                                                 .TextAlign = ContentAlignment.MiddleLeft, .ForeColor = Theme.TextNormal, .Margin = New Padding(0, 0, 8, 0)}
    Private ReadOnly balken As New Fortschrittsbalken With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 8, 0, 6), .Visible = False}
    Private ReadOnly lblInfo As New Label With {.AutoSize = False, .AutoEllipsis = True, .Dock = DockStyle.Fill, .Height = 18,
                                                .Font = Theme.SchriftKlein, .ForeColor = Theme.TextLeise, .Margin = New Padding(0)}
    Private _druckt As Boolean

    Public Sub New(d As Drucker)
        _Drucker = d
        AutoSize = True
        ColumnCount = 3
        Margin = New Padding(0, 0, 0, 8)
        Padding = New Padding(12, 8, 10, 8)
        ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)

        Controls.Add(New Label With {.Text = d.Name, .AutoSize = True, .Font = Theme.SchriftFett, .ForeColor = Theme.TextStark,
                                     .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 0, 10, 0)}, 0, 0)
        Controls.Add(lblDatei, 1, 0)
        Controls.Add(badge, 2, 0)
        Controls.Add(balken, 0, 1) : SetColumnSpan(balken, 3)
        Controls.Add(lblInfo, 0, 2) : SetColumnSpan(lblInfo, 3)
        badge.Setzen("Verbinde …", Theme.Grau)
        Flaeche()
    End Sub

    Public Sub Aktualisieren(s As DruckerStatus)
        If s Is Nothing Then Return
        Dim farbe = If(s.Zustand = DruckerZustand.Druckt, Theme.Akzent,
                    If(s.Zustand = DruckerZustand.Pausiert, Theme.Gelb,
                    If(s.Zustand = DruckerZustand.Fertig OrElse s.Zustand = DruckerZustand.Bereit, Theme.Gruen,
                    If(s.Zustand = DruckerZustand.Abgebrochen OrElse s.Zustand = DruckerZustand.Fehler OrElse s.Zustand = DruckerZustand.Offline, Theme.Rot, Theme.Grau))))
        badge.Setzen(DruckerStatus.ZustandText(s.Zustand), farbe)
        lblDatei.Text = If(s.DruckLaeuft AndAlso s.Datei.Length > 0, s.Datei,
                        If(s.Zustand = DruckerZustand.Offline, "Nicht erreichbar", If(s.Zustand = DruckerZustand.Fertig AndAlso s.Datei.Length > 0, "Fertig: " & s.Datei, "Kein Druckauftrag")))
        lblDatei.Font = If(s.DruckLaeuft, Theme.SchriftFett, Theme.Schrift)
        balken.Visible = s.DruckLaeuft
        balken.Wert = s.Fortschritt

        Dim teile As New List(Of String)
        If s.DruckLaeuft Then
            teile.Add(s.Fortschritt.ToString("P0"))
            If s.RestSekunden.HasValue Then teile.Add("noch " & DauerText(s.RestSekunden))
            If s.Schicht.GetValueOrDefault() > 0 Then teile.Add(If(s.SchichtenGesamt.GetValueOrDefault() > 0, $"Schicht {s.Schicht} / {s.SchichtenGesamt}", $"Schicht {s.Schicht}"))
        End If
        If s.Zustand <> DruckerZustand.Offline Then
            teile.AddRange(s.Temperaturen.Where(Function(t) t.Name <> "Kammer" OrElse s.DruckLaeuft).Select(Function(t) $"{t.Name} {t.WertText}"))
        End If
        Dim phase = s.PhaseText()
        lblInfo.Text = If(phase.Length > 0, phase & "  ·  ", "") & String.Join(" · ", teile)
        lblInfo.ForeColor = If(phase.Length > 0, Theme.AkzentText, Theme.TextLeise)
        lblInfo.Visible = lblInfo.Text.Length > 0

        If _druckt <> s.DruckLaeuft Then
            _druckt = s.DruckLaeuft
            Flaeche()
        End If
    End Sub

    ''' <summary>Laufende Drucke farbig hinterlegt, damit sie sofort auffallen (Beschriftungen übernehmen die Farbe).</summary>
    Private Sub Flaeche()
        BackColor = If(_druckt, Hell(Theme.Akzent, If(Theme.Dunkel, 0.16, 0.07)), Theme.Seite)
        For Each c As Control In Controls
            c.Invalidate()
        Next
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(If(Parent IsNot Nothing, Parent.BackColor, Theme.KartenFlaeche))
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        Using pfad = Abgerundet(New Rectangle(0, 0, Width - 1, Height - 1), Px(Me, 8)), b As New SolidBrush(BackColor)
            g.FillPath(b, pfad)
        End Using
        If _druckt Then
            Using b As New SolidBrush(Theme.Akzent)
                g.FillRectangle(b, 0, Px(Me, 6), Px(Me, 3), Height - Px(Me, 12))
            End Using
        End If
    End Sub
End Class
