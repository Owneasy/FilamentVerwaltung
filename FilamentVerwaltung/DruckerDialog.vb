Imports System.Drawing
Imports System.Threading
Imports System.Windows.Forms

''' <summary>Drucker hinzufügen oder bearbeiten, mit Verbindungstest.</summary>
Public Class DruckerDialog
    Inherits Form

    Private ReadOnly _d As Drucker
    Private ReadOnly _dienst As IDruckerDienst

    Private ReadOnly txtName As New TextBox With {.Width = 320, .PlaceholderText = "z. B. X1 Carbon Werkstatt"}
    Private ReadOnly cboTyp As New ComboBox With {.Width = 320, .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly txtAdresse As New TextBox With {.Width = 320, .PlaceholderText = "z. B. 192.168.1.50"}
    Private ReadOnly txtBenutzer As New TextBox With {.Width = 320}
    Private ReadOnly txtZugang As New TextBox With {.Width = 320, .UseSystemPasswordChar = True}
    Private ReadOnly chkAnzeigen As New CheckBox With {.Text = "anzeigen", .AutoSize = True, .Margin = New Padding(8, 5, 0, 0)}
    Private ReadOnly txtSerien As New TextBox With {.Width = 320, .CharacterCasing = CharacterCasing.Upper}
    Private ReadOnly chkAktiv As New CheckBox With {.Text = "Drucker überwachen", .AutoSize = True, .Checked = True}
    Private ReadOnly lblHilfe As New Label With {.AutoSize = True, .MaximumSize = New Size(440, 0), .ForeColor = Theme.TextLeise,
                                                .Font = Theme.SchriftKlein, .Margin = New Padding(3, 4, 3, 8)}
    Private ReadOnly lblTest As New Label With {.AutoSize = True, .MaximumSize = New Size(440, 0), .Margin = New Padding(3, 8, 3, 0)}
    Private ReadOnly btnTesten As New WebButton("Verbindung testen", Theme.SymSuche)
    Private ReadOnly zeileBenutzer As New List(Of Control)
    Private ReadOnly zeileSerien As New List(Of Control)
    Private ReadOnly lblZugang As New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 7, 12, 3)}

    ''' <summary>True, wenn der Drucker gelöscht werden soll.</summary>
    Public ReadOnly Property Loeschen As Boolean

    Public Sub New(d As Drucker, titel As String, dienst As IDruckerDienst)
        _d = d
        _dienst = dienst
        OberflaecheAufbauen(titel)
        For Each t In Drucker.Typen
            cboTyp.Items.Add(t.Name)
        Next
        txtName.Text = d.Name
        cboTyp.SelectedIndex = Math.Max(0, Array.FindIndex(Drucker.Typen, Function(t) t.Wert = d.Typ))
        txtAdresse.Text = d.Adresse
        txtBenutzer.Text = If(d.Id = 0 AndAlso d.Benutzer.Length = 0, "maker", d.Benutzer)
        txtZugang.Text = d.Zugang
        txtSerien.Text = d.Seriennummer
        chkAktiv.Checked = d.Aktiv
        AddHandler cboTyp.SelectedIndexChanged, Sub(s, e) TypGewechselt()
        AddHandler chkAnzeigen.CheckedChanged, Sub(s, e) txtZugang.UseSystemPasswordChar = Not chkAnzeigen.Checked
        TypGewechselt()
    End Sub

    Private ReadOnly Property Typ As String
        Get
            Return Drucker.Typen(Math.Max(0, cboTyp.SelectedIndex)).Wert
        End Get
    End Property

    Private Sub OberflaecheAufbauen(titel As String)
        SuspendLayout()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = titel
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

        Zeile(t, "Name:", txtName)
        Zeile(t, "Art:", cboTyp)
        t.Controls.Add(lblHilfe, 1, t.RowCount)
        t.RowCount += 1
        Zeile(t, "Adresse:", txtAdresse)
        zeileSerien.AddRange(Zeile(t, "Seriennummer:", txtSerien))
        zeileBenutzer.AddRange(Zeile(t, "Benutzer:", txtBenutzer))
        Dim zugang As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0), .Anchor = AnchorStyles.Left}
        zugang.Controls.Add(txtZugang)
        zugang.Controls.Add(chkAnzeigen)
        t.Controls.Add(lblZugang, 0, t.RowCount)
        t.Controls.Add(zugang, 1, t.RowCount)
        t.RowCount += 1
        t.Controls.Add(chkAktiv, 1, t.RowCount)
        chkAktiv.Margin = New Padding(3, 8, 3, 0)
        t.RowCount += 1

        Dim test As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
                                              .Margin = New Padding(0, 12, 0, 0)}
        test.Controls.Add(btnTesten)
        test.Controls.Add(lblTest)
        AddHandler btnTesten.Click, AddressOf Testen_Click
        t.Controls.Add(test, 1, t.RowCount)
        t.RowCount += 1

        t.Controls.Add(New Label With {
            .Text = "Hinweis: Die Zugangsdaten werden in der Datenbank gespeichert (im Server-Betrieb auf dem Server).",
            .AutoSize = True, .MaximumSize = New Size(440, 0), .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein,
            .Margin = New Padding(3, 14, 3, 0)}, 1, t.RowCount)
        t.RowCount += 1

        Dim knoepfe As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .Dock = DockStyle.Fill, .Margin = New Padding(0, 16, 0, 0)}
        knoepfe.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        knoepfe.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim rechts As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0), .Anchor = AnchorStyles.Right}
        Dim btnOk As New WebButton("Speichern", "", KnopfStil.Primaer) With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0)}
        Dim btnAbbrechen As New WebButton("Abbrechen") With {.MinimumSize = New Size(100, 0), .Margin = New Padding(8, 0, 0, 0), .DialogResult = DialogResult.Cancel}
        rechts.Controls.Add(btnAbbrechen)
        rechts.Controls.Add(btnOk)
        AddHandler btnOk.Click, AddressOf Ok_Click
        If _d.Id <> 0 Then
            Dim btnLoeschen As New WebButton("Entfernen", Theme.SymLoeschen, KnopfStil.Gefahr) With {.Margin = New Padding(0)}
            AddHandler btnLoeschen.Click, Sub(s, e)
                                              If MessageBox.Show(Me, $"Drucker ""{_d.Name}"" entfernen? Bereits gebuchte Drucke bleiben erhalten.",
                                                                 "Drucker entfernen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
                                                  _Loeschen = True
                                                  DialogResult = DialogResult.OK
                                              End If
                                          End Sub
            knoepfe.Controls.Add(btnLoeschen, 0, 0)
        End If
        knoepfe.Controls.Add(rechts, 1, 0)
        t.Controls.Add(knoepfe, 0, t.RowCount)
        t.SetColumnSpan(knoepfe, 2)
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

    Private Sub TypGewechselt()
        Dim typ = Me.Typ
        For Each c In zeileBenutzer
            c.Visible = typ = Drucker.TypPrusaLink
        Next
        For Each c In zeileSerien
            c.Visible = typ = Drucker.TypBambu
        Next
        Select Case typ
            Case Drucker.TypBambu
                lblZugang.Text = "Zugangscode:"
                txtAdresse.PlaceholderText = "IP-Adresse, z. B. 192.168.1.60"
                lblHilfe.Text = "Am Drucker unter Einstellungen → Netzwerk den LAN-Modus bzw. Entwicklermodus aktivieren und " &
                                "IP-Adresse sowie Zugangscode (8 Zeichen) ablesen. Die Seriennummer steht unter Einstellungen → Gerät " &
                                "oder in Bambu Studio. Verbindung: MQTT über TLS, Port 8883."
            Case Drucker.TypPrusaLink
                lblZugang.Text = "Passwort:"
                txtAdresse.PlaceholderText = "IP-Adresse, z. B. 192.168.1.70"
                lblHilfe.Text = "Am Drucker unter Einstellungen → Netzwerk → PrusaLink: IP-Adresse, Benutzer (meist ""maker"") und " &
                                "Passwort ablesen. Ältere Geräte mit API-Key: Benutzer leer lassen und den Key als Passwort eintragen."
            Case Drucker.TypOctoPrint
                lblZugang.Text = "API-Key:"
                txtAdresse.PlaceholderText = "z. B. 192.168.1.80 oder octopi.local"
                lblHilfe.Text = "In OctoPrint unter Einstellungen → Application Keys (oder API) einen Schlüssel erzeugen und hier eintragen."
            Case Else
                lblZugang.Text = "API-Key:"
                txtAdresse.PlaceholderText = "z. B. 192.168.1.50 oder 192.168.1.50:7125"
                lblHilfe.Text = "Adresse von Mainsail bzw. Fluidd. Ein API-Key ist nur nötig, wenn in Moonraker die Anmeldung aktiviert ist."
        End Select
        lblHilfe.Text = lblHilfe.Text
    End Sub

    Private Function AusFeldern() As Drucker
        Dim d = _d.Kopie()
        d.Name = txtName.Text.Trim()
        d.Typ = Typ
        d.Adresse = txtAdresse.Text.Trim()
        d.Benutzer = If(Typ = Drucker.TypPrusaLink, txtBenutzer.Text.Trim(), "")
        d.Zugang = txtZugang.Text.Trim()
        d.Seriennummer = If(Typ = Drucker.TypBambu, txtSerien.Text.Trim(), "")
        d.Aktiv = chkAktiv.Checked
        Return d
    End Function

    Private Async Sub Testen_Click(sender As Object, e As EventArgs)
        Dim d = AusFeldern()
        If d.Adresse.Length = 0 Then
            lblTest.ForeColor = Theme.Rot
            lblTest.Text = "Bitte zuerst die Adresse eintragen."
            Return
        End If
        btnTesten.Enabled = False
        lblTest.ForeColor = Theme.TextLeise
        lblTest.Text = If(TypeOf _dienst Is ServerDruckerDienst, "Der Server verbindet sich mit dem Drucker …", "Verbinde …")
        Using abbruch As New CancellationTokenSource(TimeSpan.FromSeconds(30))
            Try
                Dim s = Await _dienst.TestenAsync(d, abbruch.Token)
                lblTest.ForeColor = Theme.Gruen
                lblTest.Text = $"✓ Verbunden: {DruckerStatus.ZustandText(s.Zustand)}" &
                               If(s.Datei.Length > 0, $" · {s.Datei}", "") &
                               If(s.Faecher.Count > 0, $" · {s.Faecher.Count} Fächer erkannt", "")
            Catch ex As Exception
                lblTest.ForeColor = Theme.Rot
                lblTest.Text = "✗ " & If(TypeOf ex Is OperationCanceledException, "Keine Antwort (Zeitüberschreitung). Adresse und Netzwerk prüfen.", ex.Message)
            End Try
        End Using
        btnTesten.Enabled = True
    End Sub

    Private Sub Ok_Click(sender As Object, e As EventArgs)
        Dim d = AusFeldern()
        If d.Name.Length = 0 Then
            Hinweis("Bitte einen Namen eingeben.", txtName)
            Return
        End If
        If d.Adresse.Length = 0 Then
            Hinweis("Bitte die Adresse (IP oder Hostname) eintragen.", txtAdresse)
            Return
        End If
        If d.Typ = Drucker.TypBambu AndAlso (d.Seriennummer.Length = 0 OrElse d.Zugang.Length = 0) Then
            Hinweis("Für Bambu Lab werden Seriennummer und Zugangscode benötigt.", If(d.Seriennummer.Length = 0, CType(txtSerien, Control), txtZugang))
            Return
        End If
        _d.Name = d.Name : _d.Typ = d.Typ : _d.Adresse = d.Adresse : _d.Benutzer = d.Benutzer
        _d.Zugang = d.Zugang : _d.Seriennummer = d.Seriennummer : _d.Aktiv = d.Aktiv
        DialogResult = DialogResult.OK
    End Sub

    Private Sub Hinweis(text As String, feld As Control)
        MessageBox.Show(Me, text, "Eingabe prüfen", MessageBoxButtons.OK, MessageBoxIcon.Information)
        feld.Focus()
    End Sub

End Class
