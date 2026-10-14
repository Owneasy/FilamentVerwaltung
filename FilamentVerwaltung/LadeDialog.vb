Imports System.Drawing
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' Lädt eine Datei vom Server in den Zwischenspeicher – mit Fortschritt und Abbrechen, damit die App nicht einfriert.
''' Liegt die Datei schon vor (oder lokaler Betrieb), erscheint kein Dialog.
''' </summary>
Public Class LadeDialog
    Inherits Form

    Private ReadOnly _db As IDatenquelle
    Private ReadOnly _datei As Datei
    Private ReadOnly _abbruch As New CancellationTokenSource()
    Private ReadOnly balken As New Fortschrittsbalken With {.Width = 380, .Margin = New Padding(0, 10, 0, 6)}
    Private ReadOnly lblStand As New Label With {.AutoSize = True, .ForeColor = Theme.TextLeise, .Font = Theme.SchriftKlein, .Margin = New Padding(0)}
    Private _pfad As String

    ''' <summary>Pfad der lokalen Kopie; Nothing bei Abbruch oder Fehler (der Fehler wurde dann schon angezeigt).</summary>
    Public Shared Function DateiHolen(besitzer As IWin32Window, db As IDatenquelle, d As Datei) As String
        If db.DateiVorhanden(d) Then Return db.DateiPfad(d)
        Using dlg As New LadeDialog(db, d)
            dlg.ShowDialog(besitzer)
            Return dlg._pfad
        End Using
    End Function

    Private Sub New(db As IDatenquelle, d As Datei)
        _db = db
        _datei = d
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        DialogGestalten(Me)
        Text = "Datei vom Server laden"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim t As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoSize = True,
                                           .Padding = New Padding(20, 16, 20, 16)}
        t.Controls.Add(New Label With {.Text = d.Name, .AutoSize = True, .MaximumSize = New Size(380, 0), .Font = Theme.SchriftFett, .Margin = New Padding(0)})
        t.Controls.Add(balken)
        t.Controls.Add(lblStand)
        Dim btnAbbrechen As New WebButton("Abbrechen") With {.MinimumSize = New Size(100, 0), .Margin = New Padding(0, 14, 0, 0), .Anchor = AnchorStyles.Right}
        AddHandler btnAbbrechen.Click, Sub(s, e) _abbruch.Cancel()
        t.Controls.Add(btnAbbrechen)
        Controls.Add(t)
        lblStand.Text = $"0 von {GroesseText(d.Groesse)}"
    End Sub

    Protected Overrides Async Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        Dim fortschritt As New Progress(Of Double)(Sub(w)
                                                       balken.Wert = w
                                                       lblStand.Text = $"{GroesseText(CLng(w * _datei.Groesse))} von {GroesseText(_datei.Groesse)}"
                                                   End Sub)
        Try
            _pfad = Await _db.DateiLadenAsync(_datei, fortschritt, _abbruch.Token)
            DialogResult = DialogResult.OK
        Catch ex As OperationCanceledException
            DialogResult = DialogResult.Cancel
        Catch ex As Exception
            MessageBox.Show(Me, "Die Datei konnte nicht vom Server geladen werden." & Environment.NewLine & Environment.NewLine & ex.Message,
                            "Datei laden", MessageBoxButtons.OK, MessageBoxIcon.Error)
            DialogResult = DialogResult.Cancel
        End Try
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        ' Schließen per X = abbrechen; der Dialog geht zu, sobald das Laden wirklich beendet ist
        If DialogResult = DialogResult.None Then
            _abbruch.Cancel()
            e.Cancel = True
        End If
        MyBase.OnFormClosing(e)
    End Sub

End Class
