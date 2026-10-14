Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Lokale Datenbank oder Server wählen, mit Verbindungstest.</summary>
Public Class VerbindungDialog
    Inherits Form

    Private ReadOnly _v As Verbindung

    Private ReadOnly optLokal As New RadioButton With {.Text = "Lokal – Datenbankdatei auf diesem PC", .AutoSize = True, .Margin = New Padding(3, 3, 3, 2)}
    Private ReadOnly optServer As New RadioButton With {.Text = "Server – Daten liegen auf dem Server (z. B. Docker auf Unraid)", .AutoSize = True, .Margin = New Padding(3, 2, 3, 8)}
    Private ReadOnly txtPfad As New TextBox With {.Width = 290}
    Private ReadOnly btnPfad As New WebButton("…") With {.Margin = New Padding(6, 0, 0, 0)}
    Private ReadOnly txtIntern As New TextBox With {.Width = 340, .PlaceholderText = "z. B. http://192.168.1.10:8080"}
    Private ReadOnly txtExtern As New TextBox With {.Width = 340, .PlaceholderText = "z. B. https://filament.meinedomain.de"}
    Private ReadOnly txtToken As New TextBox With {.Width = 340, .UseSystemPasswordChar = True}
    Private ReadOnly txtCfId As New TextBox With {.Width = 340, .PlaceholderText = "optional – Cloudflare Access Service-Token"}
    Private ReadOnly txtCfGeheim As New TextBox With {.Width = 340, .UseSystemPasswordChar = True, .PlaceholderText = "optional"}
    Private ReadOnly lblFehler As New Label With {.AutoSize = True, .MaximumSize = New Size(480, 0), .ForeColor = Theme.Rot,
                                                  .Margin = New Padding(3, 0, 3, 10), .Visible = False}
    Private ReadOnly lblTest As New Label With {.AutoSize = True, .MaximumSize = New Size(440, 0), .Margin = New Padding(3, 8, 3, 0)}
    Private ReadOnly btnTesten As New WebButton("Verbindung testen", Theme.SymSuche)
    Private ReadOnly zeilenLokal As New List(Of Control)
    Private ReadOnly zeilenServer As New List(Of Control)

    ''' <summary>Die gespeicherte Verbindung (nach OK).</summary>
    Public ReadOnly Property Ergebnis As Verbindung

    ''' <param name="fehler">Meldung, warum die bisherige Verbindung nicht geklappt hat (oder Nothing).</param>
    Public Sub New(v As Verbindung, Optional fehler As String = Nothing)
        _v = v.Kopie()
        OberflaecheAufbauen()
        optLokal.Checked = Not _v.IstServer
        optServer.Checked = _v.IstServer
        txtPfad.Text = If(_v.DatenbankPfad.Length > 0, _v.DatenbankPfad, Verbindung.StandardDatenbank)
        txtIntern.Text = _v.Intern
        txtExtern.Text = _v.Extern
        txtToken.Text = _v.Token
        txtCfId.Text = _v.CloudflareId
        txtCfGeheim.Text = _v.CloudflareGeheimnis
        If Not String.IsNullOrEmpty(fehler) Then
            lblFehler.Text = fehler
            lblFehler.Visible = True
        End If
        AddHandler optLokal.CheckedChanged, Sub(s, e) ModusGewechselt()
        ModusGewechselt()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Verbindung"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim t As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                            .Padding = New Padding(20, 16, 20, 16), .Location = New Point(0, 0)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        t.Controls.Add(lblFehler, 0, t.RowCount)
        t.SetColumnSpan(lblFehler, 2)
        t.RowCount += 1
        t.Controls.Add(optLokal, 0, t.RowCount)
        t.SetColumnSpan(optLokal, 2)
        t.RowCount += 1
        t.Controls.Add(optServer, 0, t.RowCount)
        t.SetColumnSpan(optServer, 2)
        t.RowCount += 1

        Dim pfad As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        pfad.Controls.Add(txtPfad)
        pfad.Controls.Add(btnPfad)
        AddHandler btnPfad.Click, Sub(s, e) PfadWaehlen()
        zeilenLokal.AddRange(Zeile(t, "Datenbank:", pfad))

        zeilenServer.AddRange(Zeile(t, "Adresse im Heimnetz:", txtIntern))
        zeilenServer.AddRange(Zeile(t, "Adresse unterwegs:", txtExtern))
        zeilenServer.AddRange(Zeile(t, "Zugangs-Token:", txtToken))
        zeilenServer.AddRange(Zeile(t, "Cloudflare Client-ID:", txtCfId))
        zeilenServer.AddRange(Zeile(t, "Cloudflare Secret:", txtCfGeheim))
        Dim hilfe As New Label With {
            .Text = "Die Adresse im Heimnetz wird zuerst versucht, sonst die für unterwegs (z. B. über den Cloudflare-Tunnel). " &
                    "Das Token steht in token.txt im Datenordner des Servers bzw. im Container-Log. Die Cloudflare-Felder nur " &
                    "ausfüllen, wenn der Tunnel mit Cloudflare Access (Service-Token) geschützt ist.",
            .AutoSize = True, .MaximumSize = New Size(380, 0), .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein,
            .Margin = New Padding(3, 4, 3, 0)}
        t.Controls.Add(hilfe, 1, t.RowCount)
        t.RowCount += 1
        zeilenServer.Add(hilfe)

        Dim test As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
                                              .Margin = New Padding(0, 12, 0, 0)}
        test.Controls.Add(btnTesten)
        test.Controls.Add(lblTest)
        AddHandler btnTesten.Click, Sub(s, e) Testen()
        t.Controls.Add(test, 1, t.RowCount)
        t.RowCount += 1

        t.Controls.Add(New Label With {
            .Text = "Die Verbindung wird nur auf diesem PC gespeichert. Alle anderen Einstellungen (Design, Spalten …) liegen " &
                    "bei den Daten – auf dem Server also für alle PCs gleich. Nach dem Speichern startet die App neu.",
            .AutoSize = True, .MaximumSize = New Size(480, 0), .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein,
            .Margin = New Padding(3, 14, 3, 0)}, 0, t.RowCount)
        t.SetColumnSpan(t.Controls(t.Controls.Count - 1), 2)
        t.RowCount += 1

        Dim rechts As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 16, 0, 0), .Anchor = AnchorStyles.Right}
        Dim btnOk As New WebButton("Speichern", "", KnopfStil.Primaer) With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0)}
        Dim btnAbbrechen As New WebButton("Abbrechen") With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0), .DialogResult = DialogResult.Cancel}
        rechts.Controls.Add(btnAbbrechen)
        rechts.Controls.Add(btnOk)
        AddHandler btnOk.Click, Sub(s, e) Ok()
        t.Controls.Add(rechts, 0, t.RowCount)
        t.SetColumnSpan(rechts, 2)
        t.RowCount += 1

        Controls.Add(t)
        AcceptButton = btnOk
        CancelButton = btnAbbrechen
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Private Shared Function Zeile(t As TableLayoutPanel, beschriftung As String, steuerelement As Control) As Control()
        Dim l As New Label With {.Text = beschriftung, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 7, 12, 3)}
        steuerelement.Anchor = AnchorStyles.Left
        t.Controls.Add(l, 0, t.RowCount)
        t.Controls.Add(steuerelement, 1, t.RowCount)
        t.RowCount += 1
        Return {l, steuerelement}
    End Function

    Private Sub ModusGewechselt()
        For Each c In zeilenLokal
            c.Visible = optLokal.Checked
        Next
        For Each c In zeilenServer
            c.Visible = optServer.Checked
        Next
        lblTest.Text = ""
    End Sub

    Private Sub PfadWaehlen()
        Using dlg As New SaveFileDialog With {.Title = "Datenbankdatei wählen oder neu anlegen", .Filter = "Datenbank (*.db)|*.db|Alle Dateien (*.*)|*.*",
                                              .OverwritePrompt = False, .FileName = IO.Path.GetFileName(txtPfad.Text)}
            Try
                dlg.InitialDirectory = IO.Path.GetDirectoryName(txtPfad.Text)
            Catch ex As Exception
            End Try
            If dlg.ShowDialog(Me) = DialogResult.OK Then txtPfad.Text = dlg.FileName
        End Using
    End Sub

    Private Function AusFeldern() As Verbindung
        Dim v = _v.Kopie()
        v.Modus = If(optServer.Checked, Verbindung.ModusServer, Verbindung.ModusLokal)
        Dim pfad = txtPfad.Text.Trim()
        v.DatenbankPfad = If(String.Equals(pfad, Verbindung.StandardDatenbank, StringComparison.OrdinalIgnoreCase), "", pfad)
        v.Intern = txtIntern.Text.Trim()
        v.Extern = txtExtern.Text.Trim()
        v.Token = txtToken.Text.Trim()
        v.CloudflareId = txtCfId.Text.Trim()
        v.CloudflareGeheimnis = txtCfGeheim.Text.Trim()
        Return v
    End Function

    ''' <summary>
    ''' Prüft die Eingaben; beim Server wird eine echte Verbindung aufgebaut. Läuft im Hintergrund
    ''' (kein Zugriff auf die Oberfläche), damit das Fenster nicht einfriert.
    ''' </summary>
    Private Shared Function Pruefen(v As Verbindung) As (Ok As Boolean, Meldung As String)
        If Not v.IstServer Then
            If v.DatenbankPfad.Length > 0 AndAlso Not IO.Path.IsPathFullyQualified(v.DatenbankPfad) Then
                Return (False, "Bitte einen vollständigen Pfad zur Datenbankdatei angeben.")
            End If
            Return (True, "Lokale Datenbank: " & If(v.DatenbankPfad.Length > 0, v.DatenbankPfad, Verbindung.StandardDatenbank))
        End If
        If v.Intern.Length = 0 AndAlso v.Extern.Length = 0 Then Return (False, "Bitte mindestens eine Server-Adresse eintragen.")
        If v.Token.Length = 0 Then Return (False, "Bitte das Zugangs-Token eintragen.")
        Using server As New ServerDatenquelle(v.Zugang(), Verbindung.Zwischenspeicher)
            Try
                Dim version = server.Verbinden()
                Return (True, $"Verbunden mit {server.Basis} (Server-Version {version})")
            Catch ex As Exception
                Return (False, ex.Message)
            End Try
        End Using
    End Function

    Private _pruefeGerade As Boolean

    ''' <summary>Prüfen im Hintergrund; währenddessen sind Testen/Speichern gesperrt.</summary>
    Private Async Function PruefenAsync(v As Verbindung) As Task(Of (Ok As Boolean, Meldung As String))
        _pruefeGerade = True
        btnTesten.Enabled = False
        UseWaitCursor = True
        lblTest.ForeColor = Theme.TextLeise
        lblTest.Text = If(v.IstServer, "Verbinde mit dem Server …", "Prüfe …")
        Try
            Dim ergebnis = Await Task.Run(Function() Pruefen(v))
            lblTest.ForeColor = If(ergebnis.Ok, Theme.Gruen, Theme.Rot)
            lblTest.Text = If(ergebnis.Ok, "✓ ", "✗ ") & ergebnis.Meldung
            Return ergebnis
        Finally
            UseWaitCursor = False
            btnTesten.Enabled = True
            _pruefeGerade = False
        End Try
    End Function

    Private Async Sub Testen()
        If _pruefeGerade Then Return
        Await PruefenAsync(AusFeldern())
    End Sub

    Private Async Sub Ok()
        If _pruefeGerade Then Return
        Dim v = AusFeldern()
        Dim ergebnis = Await PruefenAsync(v)
        If IsDisposed Then Return
        If Not ergebnis.Ok Then
            If MessageBox.Show(Me, ergebnis.Meldung & Environment.NewLine & Environment.NewLine & "Trotzdem speichern?", "Verbindung",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        End If
        Try
            v.Speichern()
        Catch ex As Exception
            MessageBox.Show(Me, "Die Verbindung konnte nicht gespeichert werden: " & ex.Message, "Verbindung",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        _Ergebnis = v
        DialogResult = DialogResult.OK
    End Sub
End Class
