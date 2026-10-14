Imports System.Diagnostics
Imports System.Drawing
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' Zeigt eine neue Version mit Versionshinweisen an und installiert sie auf Wunsch.
''' </summary>
Public Class UpdateDialog
    Inherits Form

    Private ReadOnly _info As UpdateInfo
    Private _abbruch As New CancellationTokenSource()
    Private _laeuft As Boolean

    Private ReadOnly txtHinweise As New TextBox With {
        .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .Width = 480, .Height = 200,
        .BackColor = Theme.TabelleKopf, .BorderStyle = BorderStyle.FixedSingle
    }
    Private ReadOnly balken As New ProgressBar With {.Width = 480, .Height = 8, .Visible = False, .Margin = New Padding(3, 12, 3, 0)}
    Private ReadOnly lblStatus As New Label With {.AutoSize = True, .ForeColor = Theme.TextLeise, .Margin = New Padding(3, 6, 3, 0)}
    Private ReadOnly btnJetzt As New WebButton("Jetzt aktualisieren", Theme.SymExport, KnopfStil.Primaer) With {.Margin = New Padding(8, 0, 0, 0)}
    Private ReadOnly btnSpaeter As New WebButton("Später") With {.Margin = New Padding(8, 0, 0, 0)}
    Private ReadOnly btnUeberspringen As New WebButton("Diese Version überspringen") With {.Margin = New Padding(0)}

    ''' <summary>True, wenn der Benutzer "Diese Version überspringen" gewählt hat.</summary>
    Public ReadOnly Property Uebersprungen As Boolean

    Public Sub New(info As UpdateInfo)
        _info = info
        OberflaecheAufbauen()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Update verfügbar"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim p As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoSize = True,
            .Padding = New Padding(20, 16, 20, 16), .Location = New Point(0, 0)
        }
        p.Controls.Add(New Label With {
            .Text = $"Version {_info.Version} ist verfügbar", .Font = Theme.SchriftLogo, .ForeColor = Theme.TextStark,
            .AutoSize = True, .Margin = New Padding(3, 0, 3, 2)})
        p.Controls.Add(New Label With {
            .Text = $"Installiert ist Version {AktuelleVersion}. Deine Daten (Datenbank und Dateien) bleiben beim Update erhalten.",
            .ForeColor = Theme.TextLeise, .AutoSize = True, .MaximumSize = New Size(480, 0), .Margin = New Padding(3, 0, 3, 12)})
        p.Controls.Add(New Label With {.Text = "Was ist neu?", .Font = Theme.SchriftFett, .AutoSize = True, .Margin = New Padding(3, 0, 3, 4)})
        txtHinweise.Text = If(String.IsNullOrWhiteSpace(_info.Hinweise), "(keine Versionshinweise)",
                              _info.Hinweise.Replace(vbCrLf, vbLf).Replace(vbLf, vbCrLf))
        p.Controls.Add(txtHinweise)
        p.Controls.Add(balken)
        p.Controls.Add(lblStatus)

        If Not KannInstallieren Then
            ' Aus Visual Studio gestartet: nicht selbst ersetzen, nur auf die Release-Seite verweisen
            btnJetzt.Text = "Release-Seite öffnen"
            btnJetzt.Symbol = Theme.SymOeffnen
            lblStatus.Text = "Automatisch installieren geht nur in der veröffentlichten EXE (nicht beim Start aus Visual Studio)."
        ElseIf String.IsNullOrEmpty(_info.DownloadUrl) Then
            btnJetzt.Enabled = False
            lblStatus.Text = $"Am Release hängt keine Datei ""{AssetName}"" – bitte auf GitHub prüfen."
            lblStatus.ForeColor = Theme.Rot
        End If

        Dim unten As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .Width = 486, .Margin = New Padding(0, 16, 0, 0)}
        unten.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        unten.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim rechts As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0), .Anchor = AnchorStyles.Right}
        rechts.Controls.Add(btnSpaeter)
        rechts.Controls.Add(btnJetzt)
        unten.Controls.Add(btnUeberspringen, 0, 0)
        unten.Controls.Add(rechts, 1, 0)
        p.Controls.Add(unten)

        AddHandler btnJetzt.Click, AddressOf Jetzt_Click
        AddHandler btnSpaeter.Click, Sub(s, e) Close()
        AddHandler btnUeberspringen.Click, Sub(s, e)
                                               _Uebersprungen = True
                                               Close()
                                           End Sub

        Controls.Add(p)
        AcceptButton = btnJetzt
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        txtHinweise.SelectionLength = 0
        btnJetzt.Focus()
    End Sub

    Private Async Sub Jetzt_Click(sender As Object, e As EventArgs)
        If Not KannInstallieren Then
            Try
                Process.Start(New ProcessStartInfo(_info.ReleaseSeite) With {.UseShellExecute = True})
            Catch ex As Exception
                lblStatus.Text = ex.Message
            End Try
            Return
        End If

        _abbruch.Dispose()
        _abbruch = New CancellationTokenSource()
        _laeuft = True
        btnJetzt.Enabled = False
        btnUeberspringen.Enabled = False
        btnSpaeter.Text = "Abbrechen"
        balken.Visible = True
        lblStatus.ForeColor = Theme.TextLeise
        lblStatus.Text = "Lade Version " & _info.Version.ToString() & " herunter …"
        Dim fortschritt As New Progress(Of Integer)(Sub(prozent)
                                                         balken.Value = Math.Max(0, Math.Min(100, prozent))
                                                         lblStatus.Text = $"Lade herunter … {prozent} %"
                                                     End Sub)
        Try
            Dim datei = Await HerunterladenAsync(_info, fortschritt, _abbruch.Token)
            lblStatus.Text = "Installiere und starte neu …"
            Refresh()
            _laeuft = False   ' sonst blockiert OnFormClosing das Beenden
            InstallierenUndNeustarten(datei)
        Catch ex As OperationCanceledException
            lblStatus.Text = "Update abgebrochen."
            Zuruecksetzen()
        Catch ex As Exception
            lblStatus.Text = "Update fehlgeschlagen: " & ex.Message
            lblStatus.ForeColor = Theme.Rot
            lblStatus.MaximumSize = New Size(480, 0)
            Zuruecksetzen()
        End Try
    End Sub

    Private Sub Zuruecksetzen()
        _laeuft = False
        balken.Visible = False
        btnJetzt.Enabled = True
        btnUeberspringen.Enabled = True
        btnSpaeter.Text = "Später"
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        ' Während des Downloads schließt "Abbrechen" / X den Dialog erst nach dem Abbruch
        If _laeuft Then
            _abbruch.Cancel()
            e.Cancel = True
            Return
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _abbruch.Dispose()
        MyBase.Dispose(disposing)
    End Sub

End Class
