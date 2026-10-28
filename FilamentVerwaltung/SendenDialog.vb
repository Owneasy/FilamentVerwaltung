Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' Druckauftrag an einen angebundenen Drucker senden und starten (mit Bestätigung).
''' Bambu: Platte und AMS-Fach je Filament wählen. Klipper/OctoPrint/PrusaLink: G-Code hochladen und drucken.
''' </summary>
Public Class SendenDialog
    Inherits Form

    Private ReadOnly _db As IDatenquelle
    Private ReadOnly _ueberwachung As IDruckerDienst
    Private ReadOnly _datei As Datei
    Private ReadOnly _pfad As String
    Private _drucker As New List(Of Drucker)
    Private _platten As New List(Of PlattenInfo)
    Private _filamente As New List(Of Filament)
    Private _abbruch As CancellationTokenSource
    Private _laeuft As Boolean
    Private ReadOnly _beschriftungen As New List(Of Label)
    Private ReadOnly _fachAuswahl As New List(Of (Filament As PlattenFilament, Auswahl As ComboBox, Slots As List(Of Integer)))

    Private ReadOnly cboDrucker As New ComboBox With {.Width = 380, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly lblDruckerStatus As New Label With {.AutoSize = True, .Font = Theme.SchriftKlein, .Margin = New Padding(3, 2, 3, 6)}
    Private ReadOnly cboPlatte As New ComboBox With {.Width = 380, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly faecher As New TableLayoutPanel With {.AutoSize = True, .ColumnCount = 2, .Margin = New Padding(0)}
    Private ReadOnly chkNivellieren As New CheckBox With {.Text = "Bett nivellieren", .AutoSize = True, .Checked = True}
    Private ReadOnly chkZeitraffer As New CheckBox With {.Text = "Zeitraffer aufnehmen", .AutoSize = True}
    Private ReadOnly chkFluss As New CheckBox With {.Text = "Fluss-Kalibrierung", .AutoSize = True}
    Private ReadOnly bambuBereich As New TableLayoutPanel With {.AutoSize = True, .ColumnCount = 2, .Margin = New Padding(0)}
    ''' <summary>Reicht das Filament? Je Fach eine Zeile (siehe Restmengen.Pruefen).</summary>
    Private ReadOnly lblRest As New Label With {.AutoSize = True, .MaximumSize = New Size(560, 0), .Margin = New Padding(3, 10, 3, 0)}
    Private _reichtNicht As Boolean
    Private ReadOnly lblWarnung As New Label With {.AutoSize = True, .MaximumSize = New Size(520, 0), .Margin = New Padding(3, 12, 3, 0)}
    Private ReadOnly balken As New ProgressBar With {.Width = 520, .Height = 8, .Visible = False, .Margin = New Padding(3, 10, 3, 0), .Maximum = 1000}
    Private ReadOnly lblStatus As New Label With {.AutoSize = True, .MaximumSize = New Size(520, 0), .ForeColor = Theme.TextLeise, .Margin = New Padding(3, 6, 3, 0)}
    Private ReadOnly btnStarten As New WebButton("Druck starten", Theme.SymDrucker, KnopfStil.Primaer) With {.MinimumSize = New Size(130, 0), .Margin = New Padding(8, 0, 0, 0)}
    Private ReadOnly btnAbbrechen As New WebButton("Abbrechen") With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0)}
    Private ReadOnly btnWarteschlange As New WebButton("In Warteschlange", Theme.SymHinzufuegen) With {.Margin = New Padding(8, 0, 0, 0)}
    Private ReadOnly _vorauswahlDrucker As Long?
    Private ReadOnly _vorauswahlPlatte As Integer
    ''' <summary>Spule vom letzten Druck: im AMS bevorzugt das Fach, dem sie zugeordnet ist.</summary>
    Private ReadOnly _vorauswahlSpule As Long?

    ''' <param name="druckerId">Vorauswahl (z. B. aus der Warteschlange), sonst der passende, bereite Drucker.</param>
    Public Sub New(db As IDatenquelle, ueberwachung As IDruckerDienst, datei As Datei, pfad As String,
                   Optional druckerId As Long? = Nothing, Optional platte As Integer = 1, Optional spuleId As Long? = Nothing)
        _db = db
        _vorauswahlDrucker = druckerId
        _vorauswahlPlatte = platte
        _vorauswahlSpule = spuleId
        _ueberwachung = ueberwachung
        _datei = datei
        _pfad = pfad
        OberflaecheAufbauen()
        Laden()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "An Drucker senden"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim t As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                            .Padding = New Padding(20, 16, 20, 16), .Location = New Point(0, 0)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        Dim titel As New Label With {.Text = _datei.Name, .Font = Theme.SchriftLogo, .ForeColor = Theme.TextStark, .AutoSize = True,
                                     .MaximumSize = New Size(560, 0), .Margin = New Padding(3, 0, 3, 2)}
        Dim info As New Label With {.AutoSize = True, .ForeColor = Theme.TextLeise, .Margin = New Padding(3, 0, 3, 12),
                                    .Text = String.Join(" · ", {GrammText(_datei.Gramm), DauerText(_datei.Druckzeit), _datei.Material}.Where(Function(x) x.Length > 0))}
        t.Controls.Add(titel, 0, 0) : t.SetColumnSpan(titel, 2)
        t.Controls.Add(info, 0, 1) : t.SetColumnSpan(info, 2)
        t.RowCount = 2

        Zeile(t, "Drucker:", cboDrucker)
        t.Controls.Add(lblDruckerStatus, 1, t.RowCount) : t.RowCount += 1

        ' Bambu: Platte, AMS-Fächer, Optionen
        bambuBereich.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        bambuBereich.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Zeile(bambuBereich, "Platte:", cboPlatte)
        faecher.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        faecher.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        bambuBereich.Controls.Add(faecher, 0, bambuBereich.RowCount) : bambuBereich.SetColumnSpan(faecher, 2) : bambuBereich.RowCount += 1
        Dim optionen As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 8, 0, 0)}
        For Each c In {chkNivellieren, chkZeitraffer, chkFluss}
            c.Margin = New Padding(3, 0, 16, 0)
            optionen.Controls.Add(c)
        Next
        bambuBereich.Controls.Add(optionen, 1, bambuBereich.RowCount) : bambuBereich.RowCount += 1
        t.Controls.Add(bambuBereich, 0, t.RowCount) : t.SetColumnSpan(bambuBereich, 2) : t.RowCount += 1

        Dim unten As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Margin = New Padding(0)}
        unten.Controls.Add(lblRest)
        unten.Controls.Add(lblWarnung)
        unten.Controls.Add(balken)
        unten.Controls.Add(lblStatus)
        t.Controls.Add(unten, 0, t.RowCount) : t.SetColumnSpan(unten, 2) : t.RowCount += 1

        Dim knoepfe As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Anchor = AnchorStyles.Right, .Margin = New Padding(0, 16, 0, 0)}
        knoepfe.Controls.Add(btnAbbrechen)
        knoepfe.Controls.Add(btnStarten)
        knoepfe.Controls.Add(btnWarteschlange)
        btnWarteschlange.Visible = ServerFunktionen.Verfuegbar(_db, ServerFunktionen.Warteschlange)
        AddHandler btnWarteschlange.Click, Sub(s, e) InWarteschlange()
        t.Controls.Add(knoepfe, 0, t.RowCount) : t.SetColumnSpan(knoepfe, 2) : t.RowCount += 1

        AddHandler cboDrucker.SelectedIndexChanged, Sub(s, e) DruckerGewechselt()
        AddHandler cboPlatte.SelectedIndexChanged, Sub(s, e) DruckerGewechselt()   ' baut auch die Fächer neu auf
        AddHandler btnStarten.Click, AddressOf Starten_Click
        AddHandler btnAbbrechen.Click, Sub(s, e)
                                           If _laeuft Then _abbruch?.Cancel() Else DialogResult = DialogResult.Cancel
                                       End Sub
        Controls.Add(t)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Private Sub Zeile(t As TableLayoutPanel, beschriftung As String, steuerelement As Control)
        Dim l As New Label With {.Text = beschriftung, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 7, 12, 3)}
        _beschriftungen.Add(l)
        steuerelement.Anchor = AnchorStyles.Left
        t.Controls.Add(l, 0, t.RowCount)
        t.Controls.Add(steuerelement, 1, t.RowCount)
        t.RowCount += 1
    End Sub

    Private Sub Laden()
        Try
            _drucker = _db.DruckerLaden().Where(Function(d) d.Aktiv).ToList()
            _filamente = _db.AlleLaden()
        Catch ex As Exception
            lblWarnung.ForeColor = Theme.Rot
            lblWarnung.Text = ex.Message
            Return
        End Try
        If _datei.Typ = "3MF" Then _platten = DreiMfPlatten(_pfad)
        For Each p In _platten
            cboPlatte.Items.Add(p)
        Next
        If _platten.Count > 0 Then cboPlatte.SelectedIndex = Math.Max(0, _platten.FindIndex(Function(p) p.Nummer = _vorauswahlPlatte))
        For Each d In _drucker
            cboDrucker.Items.Add($"{d.Name}  ({d.TypName})")
        Next
        If _drucker.Count = 0 Then
            lblWarnung.ForeColor = Theme.Rot
            lblWarnung.Text = "Es ist kein Drucker angebunden. Drucker unter ""Drucker"" hinzufügen."
            bambuBereich.Visible = False
            btnStarten.Enabled = False
            Return
        End If
        ' Bevorzugt einen passenden, bereiten Drucker vorschlagen
        Dim index = If(_vorauswahlDrucker.HasValue, _drucker.FindIndex(Function(d) d.Id = _vorauswahlDrucker.Value), -1)
        If index < 0 Then index = _drucker.FindIndex(Function(d) Passt(d) AndAlso IstBereit(d))
        If index < 0 Then index = _drucker.FindIndex(AddressOf Passt)
        cboDrucker.SelectedIndex = Math.Max(0, index)
    End Sub

    Private Function GewaehlterDrucker() As Drucker
        Return If(cboDrucker.SelectedIndex >= 0, _drucker(cboDrucker.SelectedIndex), Nothing)
    End Function

    Private Function Passt(d As Drucker) As Boolean
        Dim endungen = _ueberwachung.DruckbareEndungen(d.Id)
        Dim endung = Path.GetExtension(_datei.Name).ToLowerInvariant()
        If Not endungen.Contains(endung) Then Return False
        Return d.Typ <> Drucker.TypBambu OrElse _platten.Count > 0
    End Function

    Private Function IstBereit(d As Drucker) As Boolean
        Dim s = _ueberwachung.Status(d.Id)
        Return s IsNot Nothing AndAlso (s.Zustand = DruckerZustand.Bereit OrElse s.Zustand = DruckerZustand.Fertig OrElse s.Zustand = DruckerZustand.Abgebrochen)
    End Function

    Private Sub DruckerGewechselt()
        Dim d = GewaehlterDrucker()
        If d Is Nothing Then Return
        Dim s = _ueberwachung.Status(d.Id)
        Dim zustand = If(s Is Nothing, DruckerZustand.Unbekannt, s.Zustand)
        lblDruckerStatus.Text = "Status: " & DruckerStatus.ZustandText(zustand) & If(s IsNot Nothing AndAlso s.Datei.Length > 0 AndAlso s.DruckLaeuft, " – " & s.Datei, "")
        lblDruckerStatus.ForeColor = If(IstBereit(d), Theme.Gruen, Theme.Rot)
        bambuBereich.Visible = d.Typ = Drucker.TypBambu
        If d.Typ = Drucker.TypBambu Then FaecherAufbauen()

        Dim problem As String = Nothing
        If Not Passt(d) Then
            problem = If(d.Typ = Drucker.TypBambu,
                         "Für Bambu Lab wird eine in Bambu Studio geslicte 3MF (mit G-Code) benötigt – z. B. über ""Slicen …"" erzeugen.",
                         $"{d.TypName} druckt nur {String.Join(" / ", _ueberwachung.DruckbareEndungen(d.Id))}-Dateien aus dem passenden Slicer.")
        ElseIf d.Typ = Drucker.TypBambu AndAlso
               DreiMfOhneDruckerstart(_pfad, If(TryCast(cboPlatte.SelectedItem, PlattenInfo)?.Nummer, 1)) Then
            problem = "Diese Datei enthält nicht den Start-G-Code des Druckers (mit Version 0.0.5 geslict) – der Drucker würde kein " &
                      "Filament laden. Bitte das Modell über ""Slicen …"" neu slicen."
        ElseIf zustand = DruckerZustand.Offline OrElse zustand = DruckerZustand.Unbekannt Then
            problem = "Der Drucker ist gerade nicht erreichbar."
        ElseIf s.DruckLaeuft Then
            problem = "Der Drucker ist beschäftigt – warte, bis der aktuelle Druck fertig ist."
        End If
        RestmengePruefen()
        If problem IsNot Nothing Then
            lblWarnung.ForeColor = Theme.Rot
            lblWarnung.Text = problem
            btnStarten.Enabled = False
        Else
            lblWarnung.ForeColor = Theme.Gelb
            lblWarnung.Text = "⚠ Der Druck startet sofort nach dem Hochladen. Ist die Druckplatte frei, sauber und richtig eingelegt?"
            btnStarten.Enabled = True
        End If
    End Sub

    ''' <summary>Pro Filament der Platte ein AMS-Fach wählen (vorgeschlagen aus der Spulen-Zuordnung bzw. dem Material).</summary>
    Private Sub FaecherAufbauen()
        Dim d = GewaehlterDrucker()
        Dim platte = TryCast(cboPlatte.SelectedItem, PlattenInfo)
        faecher.SuspendLayout()
        For Each c In faecher.Controls.Cast(Of Control)().ToList()
            c.Dispose()
        Next
        faecher.Controls.Clear()
        faecher.RowCount = 0
        _fachAuswahl.Clear()
        If d Is Nothing OrElse platte Is Nothing Then
            faecher.ResumeLayout()
            Return
        End If
        Dim s = _ueberwachung.Status(d.Id)
        Dim zuordnung As Dictionary(Of Integer, Long)
        Try
            zuordnung = _db.SpulenZuordnung(d.Id)
        Catch
            zuordnung = New Dictionary(Of Integer, Long)
        End Try
        Dim verfuegbar = If(s?.Faecher, New List(Of DruckerFach)).Where(Function(f) Not f.Beschreibung.EndsWith("(leer)")).ToList()
        If Not verfuegbar.Any(Function(f) f.Slot = Drucker.SlotExtern) Then verfuegbar.Add(New DruckerFach With {.Slot = Drucker.SlotExtern, .Beschreibung = "Externe Spule"})
        Dim filamente = If(platte.Filamente.Count > 0, platte.Filamente, New List(Of PlattenFilament) From {New PlattenFilament With {.Id = 1, .Typ = _datei.Material}})

        For Each pf In filamente
            Dim beschriftung As New Label With {.Text = $"Filament {pf.Id}: {If(pf.Typ.Length > 0, pf.Typ, "?")}" & If(pf.Gramm > 0, $" ({pf.Gramm:N0} g)", "") & " →",
                                                .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 7, 12, 3)}
            Dim auswahl As New ComboBox With {.Width = 380, .DropDownStyle = ComboBoxStyle.DropDownList, .Anchor = AnchorStyles.Left}
            Dim slots As New List(Of Integer)
            Dim bester = -1, besterPunkte = -1
            For Each f In verfuegbar
                Dim spuleId As Long = 0
                zuordnung.TryGetValue(f.Slot, spuleId)
                Dim spule = _filamente.FirstOrDefault(Function(x) x.Id = spuleId)
                auswahl.Items.Add(f.Beschreibung & If(spule IsNot Nothing, $" – {spule.Code} {spule.Hersteller} {spule.Material} {spule.Farbe}", ""))
                slots.Add(f.Slot)
                ' Vorschlag: gleiches Material bei zugeordneter Spule > gleiches Material im Fach > aktives Fach
                Dim typ = pf.Typ.ToUpperInvariant()
                Dim punkte = 0
                If typ.Length > 0 AndAlso spule IsNot Nothing AndAlso spule.Material.ToUpperInvariant().Contains(typ) Then punkte += 4
                If typ.Length > 0 AndAlso f.Beschreibung.ToUpperInvariant().Contains(typ) Then punkte += 2
                If s?.Slot = f.Slot Then punkte += 1
                If spule IsNot Nothing AndAlso _vorauswahlSpule.HasValue AndAlso spule.Id = _vorauswahlSpule.Value Then punkte += 8
                If punkte > besterPunkte Then
                    besterPunkte = punkte
                    bester = auswahl.Items.Count - 1
                End If
            Next
            auswahl.SelectedIndex = Math.Max(0, bester)
            AddHandler auswahl.SelectedIndexChanged, Sub(s2, e2) RestmengePruefen()
            faecher.Controls.Add(beschriftung, 0, faecher.RowCount)
            faecher.Controls.Add(auswahl, 1, faecher.RowCount)
            faecher.RowCount += 1
            _fachAuswahl.Add((pf, auswahl, slots))
        Next
        faecher.ResumeLayout()
        SpaltenAngleichen()
    End Sub

    ''' <summary>
    ''' Drucker-, Platten- und Filament-Zeilen liegen in verschachtelten Tabellen. Damit alle Auswahlfelder
    ''' untereinander stehen, bekommt die Beschriftungsspalte überall dieselbe Breite (die der längsten Beschriftung).
    ''' </summary>
    Private Sub SpaltenAngleichen()
        Dim tabellen = {DirectCast(Controls(0), TableLayoutPanel), bambuBereich, faecher}
        Dim labels = _beschriftungen.Concat(faecher.Controls.OfType(Of Label)())
        Dim breite = labels.Max(Function(l) l.PreferredSize.Width + l.Margin.Horizontal)
        For Each t In tabellen
            t.ColumnStyles(0) = New ColumnStyle(SizeType.Absolute, breite)
        Next
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        SpaltenAngleichen()   ' nach der DPI-Skalierung neu messen
    End Sub

    ''' <summary>
    ''' Bedarf laut Slicer (je Filament bzw. ganze Datei) mit dem Restgewicht der zugeordneten Spulen vergleichen.
    ''' Reicht eine Spule nicht, wird vor dem Start nachgefragt.
    ''' </summary>
    Private Sub RestmengePruefen()
        Dim d = GewaehlterDrucker()
        _reichtNicht = False
        lblRest.Text = ""
        If d Is Nothing Then Return
        Dim bedarf As New List(Of FilamentBedarf)
        If d.Typ = Drucker.TypBambu Then
            Dim platte = TryCast(cboPlatte.SelectedItem, PlattenInfo)
            For Each f In _fachAuswahl
                Dim gramm = If(f.Filament.Gramm > 0, f.Filament.Gramm, If(_fachAuswahl.Count = 1, If(platte?.Gramm, 0.0), 0.0))
                bedarf.Add(New FilamentBedarf With {.Bezeichnung = $"Filament {f.Filament.Id}", .Gramm = gramm,
                                                     .Slot = f.Slots(Math.Max(0, f.Auswahl.SelectedIndex))})
            Next
        Else
            bedarf.Add(New FilamentBedarf With {.Bezeichnung = "Filament", .Gramm = If(_datei.Gramm, 0.0), .Slot = Drucker.SlotStandard})
        End If
        If Not bedarf.Any(Function(b) b.Gramm > 0) Then Return
        Dim zuordnung As Dictionary(Of Integer, Long)
        Try
            zuordnung = _db.SpulenZuordnung(d.Id)
        Catch
            Return
        End Try
        Dim hinweise = Restmengen.Pruefen(bedarf, zuordnung, _filamente)
        If hinweise.Count = 0 Then Return
        Dim zeichen = Function(h As RestmengenHinweis) As String
                          Select Case h.Ergebnis
                              Case RestmengenHinweis.Stufe.Reicht : Return "✓ "
                              Case RestmengenHinweis.Stufe.Knapp : Return "⚠ "
                              Case RestmengenHinweis.Stufe.ReichtNicht : Return "✕ "
                              Case Else : Return "? "
                          End Select
                      End Function
        lblRest.Text = String.Join(Environment.NewLine, hinweise.Select(Function(h) zeichen(h) & h.Text))
        _reichtNicht = hinweise.Any(Function(h) h.Ergebnis = RestmengenHinweis.Stufe.ReichtNicht)
        lblRest.ForeColor = If(_reichtNicht, Theme.Rot,
                               If(hinweise.Any(Function(h) h.Ergebnis = RestmengenHinweis.Stufe.Knapp), Theme.Gelb,
                                  If(hinweise.All(Function(h) h.Ergebnis = RestmengenHinweis.Stufe.Reicht), Theme.Gruen, Theme.TextLeise)))
    End Sub

    Private Async Sub Starten_Click(sender As Object, e As EventArgs)
        Dim d = GewaehlterDrucker()
        If d Is Nothing Then Return
        If _reichtNicht AndAlso MessageBox.Show(Me, "Mindestens eine Spule reicht laut Restgewicht nicht für diesen Druck:" & Environment.NewLine &
                                                    Environment.NewLine & lblRest.Text & Environment.NewLine & Environment.NewLine & "Trotzdem starten?",
                                                    "Filament reicht nicht", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                                                    MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Dim optionen As New SendeOptionen With {
            .Platte = If(TryCast(cboPlatte.SelectedItem, PlattenInfo)?.Nummer, 1),
            .Nivellieren = chkNivellieren.Checked,
            .Zeitraffer = chkZeitraffer.Checked,
            .FlussKalibrierung = chkFluss.Checked
        }
        For Each f In _fachAuswahl.OrderBy(Function(x) x.Filament.Id)
            optionen.AmsZuordnung.Add(f.Slots(Math.Max(0, f.Auswahl.SelectedIndex)))
        Next

        _laeuft = True
        _abbruch = New CancellationTokenSource()
        btnStarten.Enabled = False
        cboDrucker.Enabled = False
        bambuBereich.Enabled = False
        balken.Visible = True
        balken.Value = 0
        lblStatus.ForeColor = Theme.TextLeise
        lblStatus.Text = "Lade auf den Drucker hoch …"
        Dim fortschritt As New Progress(Of Double)(Sub(wert)
                                                       balken.Value = CInt(Math.Max(0, Math.Min(1, wert)) * 1000)
                                                       lblStatus.Text = If(wert >= 0.999, "Sende Startbefehl …", $"Lade auf den Drucker hoch … {wert:P0}")
                                                   End Sub)
        Try
            Await _ueberwachung.SendenAsync(d.Id, _pfad, _datei.Id, optionen, fortschritt, _abbruch.Token)
            _laeuft = False
            DialogResult = DialogResult.OK
        Catch ex As Exception
            _laeuft = False
            balken.Visible = False
            lblStatus.ForeColor = Theme.Rot
            lblStatus.Text = If(TypeOf ex Is OperationCanceledException, "Abgebrochen.", "Senden fehlgeschlagen: " & ex.Message) &
                             " Details stehen im Protokoll des Druckers (Seite Drucker)."
            btnStarten.Enabled = True
            cboDrucker.Enabled = True
            bambuBereich.Enabled = True
        End Try
    End Sub

    ''' <summary>Nicht jetzt drucken, sondern hinten in die Warteschlange (mit gewähltem Drucker und Platte).</summary>
    Private Sub InWarteschlange()
        Try
            Dim a As New Auftrag With {.DateiId = _datei.Id, .DruckerId = GewaehlterDrucker()?.Id,
                                       .Platte = If(TryCast(cboPlatte.SelectedItem, PlattenInfo)?.Nummer, 1)}
            _db.WarteschlangeSpeichern(a)
            MessageBox.Show(Me, $"""{_datei.Name}"" steht jetzt an Platz {a.Position} der Warteschlange." & Environment.NewLine &
                                "Ist der Drucker frei, kommt eine Benachrichtigung; gestartet wird unter Drucker → Warteschlange.",
                            "Warteschlange", MessageBoxButtons.OK, MessageBoxIcon.Information)
            DialogResult = DialogResult.Cancel
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Warteschlange", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If _laeuft Then
            _abbruch?.Cancel()
            e.Cancel = True
            Return
        End If
        MyBase.OnFormClosing(e)
    End Sub

End Class
