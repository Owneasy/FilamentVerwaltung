Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Home Assistant: Der Server meldet Drucker und Lager per MQTT an (MQTT-Discovery). Hier stehen die Zugangsdaten
''' zum MQTT-Broker (in Home Assistant meist das Add-on "Mosquitto broker"). Gilt nur mit Server.
''' </summary>
Public Class HomeAssistantDialog
    Inherits Form

    Private ReadOnly _db As IDatenquelle
    Private ReadOnly _server As ServerDatenquelle
    Private ReadOnly chkAktiv As New CheckBox With {.Text = "Drucker und Lager an Home Assistant melden", .AutoSize = True, .Margin = New Padding(3, 3, 3, 10)}
    Private ReadOnly txtHost As New TextBox With {.Width = 240}
    Private ReadOnly numPort As New NumericUpDown With {.Minimum = 1, .Maximum = 65535, .Width = 80, .Value = HomeAssistantEinstellungen.StandardPort}
    Private ReadOnly txtBenutzer As New TextBox With {.Width = 240}
    Private ReadOnly txtPasswort As New TextBox With {.Width = 240, .UseSystemPasswordChar = True}
    Private ReadOnly txtPraefix As New TextBox With {.Width = 240}
    Private ReadOnly lblStatus As New Label With {.AutoSize = True, .MaximumSize = New Size(460, 0), .Margin = New Padding(3, 10, 3, 0)}
    Private ReadOnly btnTesten As New WebButton("Verbindung testen", Theme.SymServer)

    Public Sub New(db As IDatenquelle)
        _db = db
        _server = TryCast(db, ServerDatenquelle)
        OberflaecheAufbauen()
        WerteLaden()
    End Sub

    Private Sub OberflaecheAufbauen()
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Home Assistant"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim p As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoSize = True,
                                           .Padding = New Padding(20, 16, 20, 16)}
        p.Controls.Add(New Label With {
            .Text = "Der Server meldet jeden Drucker (Status, Fortschritt, Restzeit, Temperaturen, Ereignis ""Druckende"") und das Lager " &
                    "(Spulen, Verbrauch, Kosten, Fehldrucke) über MQTT. In Home Assistant erscheinen die Geräte von selbst unter " &
                    "Einstellungen → Geräte & Dienste → MQTT.",
            .AutoSize = True, .MaximumSize = New Size(460, 0), .Margin = New Padding(3, 0, 3, 6)})
        p.Controls.Add(New Label With {
            .Text = "Voraussetzung: Add-on ""Mosquitto broker"" und die MQTT-Integration in Home Assistant. Als Zugang am besten einen " &
                    "eigenen Home-Assistant-Benutzer anlegen (z. B. ""filament"").",
            .AutoSize = True, .MaximumSize = New Size(460, 0), .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein, .Margin = New Padding(3, 0, 3, 12)})
        p.Controls.Add(chkAktiv)

        Dim t As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .Margin = New Padding(0)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Zeile(t, "MQTT-Broker:", txtHost)
        Zeile(t, "Port:", numPort)
        Zeile(t, "Benutzer:", txtBenutzer)
        Zeile(t, "Passwort:", txtPasswort)
        Zeile(t, "Discovery-Präfix:", txtPraefix)
        p.Controls.Add(t)
        p.Controls.Add(New Label With {.Text = "Broker: IP oder Name von Home Assistant, wie ihn der Server erreicht (z. B. 192.168.1.20).",
                                       .AutoSize = True, .MaximumSize = New Size(460, 0), .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein,
                                       .Margin = New Padding(3, 4, 3, 0)})

        p.Controls.Add(lblStatus)
        btnTesten.Margin = New Padding(3, 8, 3, 0)
        AddHandler btnTesten.Click, Sub(s, e) Testen()
        p.Controls.Add(btnTesten)

        Dim knoepfe As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Anchor = AnchorStyles.Right,
                                                .Margin = New Padding(0, 16, 0, 0)}
        Dim btnOk As New WebButton("Speichern", "", KnopfStil.Primaer) With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0)}
        Dim btnAbbrechen As New WebButton("Abbrechen") With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0), .DialogResult = DialogResult.Cancel}
        AddHandler btnOk.Click, Sub(s, e) Speichern()
        knoepfe.Controls.Add(btnAbbrechen)
        knoepfe.Controls.Add(btnOk)
        p.Controls.Add(knoepfe)

        Controls.Add(p)
        AcceptButton = btnOk
        CancelButton = btnAbbrechen
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

    Private Function Eingaben() As HaVerbindung
        Return New HaVerbindung With {
            .Aktiv = chkAktiv.Checked,
            .Host = txtHost.Text.Trim(),
            .Port = CInt(EingabeWert(numPort)),
            .Benutzer = txtBenutzer.Text.Trim(),
            .Passwort = txtPasswort.Text,
            .Praefix = If(txtPraefix.Text.Trim().Length = 0, HomeAssistantEinstellungen.StandardPraefix, txtPraefix.Text.Trim())
        }
    End Function

    Private Sub WerteLaden()
        Try
            Dim v = HomeAssistantEinstellungen.Laden(_db)
            chkAktiv.Checked = v.Aktiv
            txtHost.Text = v.Host
            numPort.Value = v.Port
            txtBenutzer.Text = v.Benutzer
            txtPasswort.Text = v.Passwort
            txtPraefix.Text = v.Praefix
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try

        If _server Is Nothing Then
            StatusZeigen("Home Assistant bedient nur der Server. Im lokalen Betrieb (Datenbankdatei auf diesem PC) wird nichts gemeldet.", Theme.TextLeise)
            btnTesten.Visible = False
            Return
        End If
        Try
            Dim s = _server.HomeAssistantStatus()
            StatusZeigen("Zustand: " & s.Meldung, If(s.Verbunden, Theme.Gruen, Theme.TextLeise))
        Catch ex As Exception
            StatusZeigen("Zustand unbekannt: " & ex.Message, Theme.TextLeise)
        End Try
    End Sub

    Private Sub StatusZeigen(text As String, farbe As Color)
        lblStatus.Text = text
        lblStatus.ForeColor = farbe
    End Sub

    Private Async Sub Testen()
        If _server Is Nothing Then Return
        Dim v = Eingaben()
        btnTesten.Enabled = False
        StatusZeigen("Teste …", Theme.TextLeise)
        Try
            Dim s = Await Task.Run(Function() _server.HomeAssistantTesten(v))
            StatusZeigen(s.Meldung, If(s.Verbunden, Theme.Gruen, Theme.Rot))
        Catch ex As Exception
            StatusZeigen(ex.Message, Theme.Rot)
        End Try
        btnTesten.Enabled = True
    End Sub

    Private Sub Speichern()
        Dim v = Eingaben()
        If v.Aktiv AndAlso v.Host.Length = 0 Then
            MessageBox.Show(Me, "Bitte den MQTT-Broker eintragen.", "Eingabe prüfen", MessageBoxButtons.OK, MessageBoxIcon.Information)
            txtHost.Focus()
            Return
        End If
        Try
            HomeAssistantEinstellungen.Speichern(_db, v)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        DialogResult = DialogResult.OK
    End Sub

End Class
