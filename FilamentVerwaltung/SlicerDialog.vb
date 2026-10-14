Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' Modell mit Bambu Studio slicen: Drucker-, Prozess- und Filamentprofil aus der installierten Bambu Studio wählen.
''' Das Ergebnis (3MF mit G-Code, Gewicht und Druckzeit) wird in "Dateien" übernommen.
''' </summary>
Public Class SlicerDialog
    Inherits Form

    Private Const KeineSpule As String = "(ohne Spule – Filamentprofil selbst wählen)"

    Private ReadOnly _db As Datenbank
    Private ReadOnly _bambu As BambuStudio
    Private ReadOnly _quelle As Datei
    Private ReadOnly _modellPfad As String
    Private ReadOnly _spulen As List(Of Filament)
    Private _abbruch As CancellationTokenSource
    Private _laeuft As Boolean
    Private _letztesProtokoll As String = ""

    Private ReadOnly cboDrucker As New ComboBox With {.Width = 380, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly cboSpule As New ComboBox With {.Width = 380, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly cboFilament As New ComboBox With {.Width = 380, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly cboProzess As New ComboBox With {.Width = 380, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly cboPlatte As New ComboBox With {.Width = 200, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly chkAusrichten As New CheckBox With {.Text = "Modell automatisch ausrichten", .AutoSize = True, .Checked = True}
    Private ReadOnly balken As New ProgressBar With {.Style = ProgressBarStyle.Marquee, .Width = 380, .Height = 6, .Visible = False, .Margin = New Padding(3, 12, 3, 0)}
    Private ReadOnly lblStatus As New Label With {.AutoSize = True, .MaximumSize = New Size(400, 0), .ForeColor = Theme.TextLeise, .Margin = New Padding(3, 8, 3, 0)}
    Private ReadOnly btnProtokoll As New WebButton("Protokoll anzeigen", Theme.SymSpalten) With {.Visible = False, .Margin = New Padding(3, 8, 3, 0)}
    Private ReadOnly btnSlicen As New WebButton("Slicen", Theme.SymWuerfel, KnopfStil.Primaer) With {.MinimumSize = New Size(110, 0), .Margin = New Padding(8, 0, 0, 0)}
    Private ReadOnly btnAbbrechen As New WebButton("Abbrechen") With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0)}

    Private _druckerProfile As New List(Of SlicerProfil)
    Private _prozessProfile As New List(Of SlicerProfil)
    Private _filamentProfile As New List(Of SlicerProfil)
    Private _spulenAuswahl As New List(Of Filament)

    ''' <summary>Die erzeugte und in "Dateien" übernommene Datei.</summary>
    Public ReadOnly Property Ergebnis As Datei

    Public Sub New(db As Datenbank, bambu As BambuStudio, quelle As Datei, modellPfad As String, spulen As List(Of Filament))
        _db = db
        _bambu = bambu
        _quelle = quelle
        _modellPfad = modellPfad
        _spulen = spulen
        OberflaecheAufbauen()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Slicen mit Bambu Studio"
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

        Dim titel As New Label With {.Text = _quelle.Name, .Font = Theme.SchriftLogo, .ForeColor = Theme.TextStark, .AutoSize = True,
                                     .MaximumSize = New Size(520, 0), .Margin = New Padding(3, 0, 3, 12)}
        t.Controls.Add(titel, 0, 0)
        t.SetColumnSpan(titel, 2)
        t.RowCount = 1
        Zeile(t, "Drucker:", cboDrucker)
        Zeile(t, "Spule:", cboSpule)
        Zeile(t, "Filamentprofil:", cboFilament)
        Zeile(t, "Prozess:", cboProzess)
        Zeile(t, "Druckplatte:", cboPlatte)
        t.Controls.Add(chkAusrichten, 1, t.RowCount)
        chkAusrichten.Margin = New Padding(3, 6, 3, 0)
        t.RowCount += 1

        Dim status As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Margin = New Padding(0)}
        status.Controls.Add(balken)
        status.Controls.Add(lblStatus)
        status.Controls.Add(btnProtokoll)
        t.Controls.Add(status, 1, t.RowCount)
        t.RowCount += 1

        Dim knoepfe As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Anchor = AnchorStyles.Right,
                                                .Margin = New Padding(0, 16, 0, 0)}
        knoepfe.Controls.Add(btnAbbrechen)
        knoepfe.Controls.Add(btnSlicen)
        t.Controls.Add(knoepfe, 0, t.RowCount)
        t.SetColumnSpan(knoepfe, 2)
        t.RowCount += 1

        For Each p In BambuStudio.Druckplatten
            cboPlatte.Items.Add(p)
        Next
        AddHandler cboDrucker.SelectedIndexChanged, Sub(s, e) DruckerGewechselt()
        AddHandler cboSpule.SelectedIndexChanged, Sub(s, e) SpuleGewechselt()
        AddHandler btnSlicen.Click, AddressOf Slicen_Click
        AddHandler btnAbbrechen.Click, Sub(s, e)
                                           If _laeuft Then _abbruch?.Cancel() Else DialogResult = DialogResult.Cancel
                                       End Sub
        AddHandler btnProtokoll.Click, Sub(s, e) ProtokollZeigen()

        Controls.Add(t)
        AcceptButton = btnSlicen
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Private Shared Sub Zeile(t As TableLayoutPanel, beschriftung As String, steuerelement As Control)
        Dim l As New Label With {.Text = beschriftung, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 7, 12, 3)}
        steuerelement.Anchor = AnchorStyles.Left
        t.Controls.Add(l, 0, t.RowCount)
        t.Controls.Add(steuerelement, 1, t.RowCount)
        t.RowCount += 1
    End Sub

    Private Function Einstellung(schluessel As String) As String
        Try
            Return If(_db.EinstellungLesen("Slicer." & schluessel), "")
        Catch ex As Exception
            Return ""
        End Try
    End Function

    Protected Overrides Async Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        EingabenAktiv(False)
        btnSlicen.Enabled = False
        balken.Visible = True
        lblStatus.Text = "Lade Profile aus Bambu Studio …"
        Try
            _druckerProfile = Await Task.Run(Function() _bambu.DruckerProfile())
        Catch ex As Exception
            balken.Visible = False
            lblStatus.ForeColor = Theme.Rot
            lblStatus.Text = "Die Profile von Bambu Studio konnten nicht gelesen werden: " & ex.Message
            Return
        End Try
        balken.Visible = False
        lblStatus.Text = ""

        ' Spulen mit Restbestand anbieten
        _spulenAuswahl = _spulen.Where(Function(f) f.Restgewicht > 0).OrderBy(Function(f) f.Code).ToList()
        cboSpule.Items.Add(KeineSpule)
        For Each f In _spulenAuswahl
            cboSpule.Items.Add($"{f.Code} · {f.Hersteller} {f.Material} {f.Farbe} ({f.Restgewicht:N0} g)")
        Next

        For Each p In _druckerProfile
            cboDrucker.Items.Add(p.Name)
        Next
        Dim gespeichert = Einstellung("Drucker")
        Dim index = _druckerProfile.FindIndex(Function(p) p.Name = gespeichert)
        If index < 0 Then index = _druckerProfile.FindIndex(Function(p) p.Name = "Bambu Lab A1 0.4 nozzle")
        cboDrucker.SelectedIndex = Math.Max(0, index)
        Dim platte = Einstellung("Platte")
        cboPlatte.SelectedIndex = Math.Max(0, Array.IndexOf(BambuStudio.Druckplatten, platte))
        chkAusrichten.Checked = Einstellung("Ausrichten") <> "0"
        Dim spule = Einstellung("Spule")
        Dim spulenIndex = _spulenAuswahl.FindIndex(Function(f) f.Id.ToString() = spule)
        cboSpule.SelectedIndex = If(spulenIndex >= 0, spulenIndex + 1, 0)
        EingabenAktiv(True)
        btnSlicen.Enabled = True
    End Sub

    Private Sub DruckerGewechselt()
        Dim drucker = GewaehlterDrucker()
        If drucker Is Nothing Then Return
        _prozessProfile = _bambu.ProzessProfile(drucker.Name)
        _filamentProfile = _bambu.FilamentProfile(drucker.Name)
        ListeFuellen(cboProzess, _prozessProfile, Einstellung("Prozess"), Function(p) p.KurzName.StartsWith("0.20mm Standard"))
        ListeFuellen(cboFilament, _filamentProfile, Einstellung("Filament"), Function(p) p.KurzName.StartsWith("Generic PLA"))
        SpuleGewechselt()
    End Sub

    Private Shared Sub ListeFuellen(cb As ComboBox, profile As List(Of SlicerProfil), gespeichert As String, standard As Func(Of SlicerProfil, Boolean))
        cb.BeginUpdate()
        cb.Items.Clear()
        For Each p In profile
            cb.Items.Add(p)
        Next
        cb.EndUpdate()
        Dim index = profile.FindIndex(Function(p) p.Name = gespeichert)
        If index < 0 Then index = profile.FindIndex(Function(p) standard(p))
        If profile.Count > 0 Then cb.SelectedIndex = Math.Max(0, index)
    End Sub

    ''' <summary>Passendes Filamentprofil für die gewählte Spule vorschlagen.</summary>
    Private Sub SpuleGewechselt()
        Dim spule = GewaehlteSpule()
        If spule Is Nothing OrElse _filamentProfile.Count = 0 Then Return
        Dim vorschlag = BambuStudio.FilamentVorschlagen(_filamentProfile, spule)
        If vorschlag IsNot Nothing Then cboFilament.SelectedItem = vorschlag
        lblStatus.ForeColor = Theme.TextLeise
        lblStatus.Text = If(vorschlag IsNot Nothing, $"Filamentprofil passend zur Spule gewählt: {vorschlag.KurzName}",
                            "Kein passendes Filamentprofil gefunden – bitte selbst wählen.")
    End Sub

    Private Function GewaehlterDrucker() As SlicerProfil
        Return If(cboDrucker.SelectedIndex >= 0 AndAlso cboDrucker.SelectedIndex < _druckerProfile.Count, _druckerProfile(cboDrucker.SelectedIndex), Nothing)
    End Function

    Private Function GewaehlteSpule() As Filament
        Return If(cboSpule.SelectedIndex > 0, _spulenAuswahl(cboSpule.SelectedIndex - 1), Nothing)
    End Function

    Private Sub EingabenAktiv(aktiv As Boolean)
        For Each c As Control In New Control() {cboDrucker, cboSpule, cboFilament, cboProzess, cboPlatte, chkAusrichten}
            c.Enabled = aktiv
        Next
    End Sub

    Private Async Sub Slicen_Click(sender As Object, e As EventArgs)
        Dim drucker = GewaehlterDrucker()
        Dim prozess = TryCast(cboProzess.SelectedItem, SlicerProfil)
        Dim filament = TryCast(cboFilament.SelectedItem, SlicerProfil)
        If drucker Is Nothing OrElse prozess Is Nothing OrElse filament Is Nothing Then
            lblStatus.ForeColor = Theme.Rot
            lblStatus.Text = "Bitte Drucker, Filamentprofil und Prozess wählen."
            Return
        End If
        Dim platte = CStr(cboPlatte.SelectedItem)
        Dim spule = GewaehlteSpule()
        Try
            For Each paar In {("Drucker", drucker.Name), ("Prozess", prozess.Name), ("Filament", filament.Name), ("Platte", platte),
                              ("Ausrichten", If(chkAusrichten.Checked, "1", "0")), ("Spule", If(spule?.Id.ToString(), ""))}
                _db.EinstellungSpeichern("Slicer." & paar.Item1, paar.Item2)
            Next
        Catch ex As Exception
            ' Auswahl merken ist nicht kritisch
        End Try

        _laeuft = True
        _abbruch = New CancellationTokenSource()
        EingabenAktiv(False)
        btnSlicen.Enabled = False
        btnProtokoll.Visible = False
        balken.Visible = True
        lblStatus.ForeColor = Theme.TextLeise
        lblStatus.Text = "Bambu Studio slict das Modell …"
        Dim name = $"{IO.Path.GetFileNameWithoutExtension(_quelle.Name)} ({filament.KurzName}, {prozess.KurzName})"
        Dim farbe = If(spule IsNot Nothing, FarbeZuName(spule.Farbe), CType(Nothing, Color?))
        Dim ergebnis = Await _bambu.SlicenAsync(_modellPfad, drucker, prozess, filament, platte, chkAusrichten.Checked, farbe, name, _abbruch.Token)
        _laeuft = False
        balken.Visible = False
        _letztesProtokoll = ergebnis.Protokoll

        If Not ergebnis.Erfolgreich Then
            lblStatus.ForeColor = Theme.Rot
            lblStatus.Text = ergebnis.Fehler
            btnProtokoll.Visible = ergebnis.Protokoll.Length > 0
            EingabenAktiv(True)
            btnSlicen.Enabled = True
            Return
        End If

        Try
            Dim neu = _db.DateiImportieren(ergebnis.Datei)
            Dim notiz = $"Geslict aus ""{_quelle.Name}"" mit Bambu Studio: {drucker.Name} · {prozess.KurzName} · {filament.KurzName} · {platte}" &
                        If(spule IsNot Nothing, $" · für Spule {spule.Code}", "")
            _db.DateiNotizSpeichern(neu.Id, notiz)
            neu.Notiz = notiz
            _Ergebnis = neu
        Catch ex As Exception
            lblStatus.ForeColor = Theme.Rot
            lblStatus.Text = "Die geslicte Datei konnte nicht übernommen werden: " & ex.Message
            EingabenAktiv(True)
            btnSlicen.Enabled = True
            Return
        Finally
            BambuStudio.Aufraeumen(ergebnis)
        End Try
        DialogResult = DialogResult.OK
    End Sub

    Private Sub ProtokollZeigen()
        Using f As New Form With {.Text = "Protokoll – Bambu Studio", .Size = New Size(900, 520), .StartPosition = FormStartPosition.CenterParent,
                                  .ShowInTaskbar = False, .MinimizeBox = False}
            DialogGestalten(f)
            f.Controls.Add(New TextBox With {.Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Both, .WordWrap = False,
                                             .Dock = DockStyle.Fill, .Font = New Font("Consolas", 9.0F), .Text = _letztesProtokoll,
                                             .BackColor = Theme.TabelleKopf, .ForeColor = Theme.TextNormal})
            f.ShowDialog(Me)
        End Using
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
