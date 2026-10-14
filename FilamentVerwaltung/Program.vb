Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Einstiegspunkt der Anwendung.
''' Womit sich die App verbindet (lokale Datei oder Server), steht in der <see cref="Verbindung"/>.
''' Ein Pfad als Startparameter öffnet direkt diese lokale Datenbank:
'''   FilamentVerwaltung.exe "D:\Daten\filament.db"
''' </summary>
Public Module Program

    <STAThread>
    Public Sub Main(args As String())
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Updater.Aufraeumen()

        Dim db = DatenquelleOeffnen(args)
        If db Is Nothing Then Return

        ' Design (hell / dunkel / wie Windows) vor dem ersten Fenster anwenden
        Dim design = DesignEinstellung.Hell
        Try
            design = If(db.EinstellungLesen(DesignEinstellung.Schluessel), DesignEinstellung.Hell)
        Catch ex As Exception
            ' Standard: hell
        End Try
        Dim dunkel = design = DesignEinstellung.Dunkel OrElse (design = DesignEinstellung.WieWindows AndAlso Theme.WindowsIstDunkel())
        Theme.Anwenden(dunkel)
        Application.SetColorMode(If(dunkel, SystemColorMode.Dark, SystemColorMode.Classic))

        ' Lokal: einmal pro Tag eine Sicherung anlegen (der Server sichert selbst).
        ' Schlägt das fehl, trotzdem starten und darauf hinweisen.
        Dim lokal = TryCast(db, Datenbank)
        If lokal IsNot Nothing Then
            Try
                Sicherung.AutomatischSichern(lokal)
            Catch ex As Exception
                MessageBox.Show("Die automatische Sicherung der Datenbank ist fehlgeschlagen:" & Environment.NewLine &
                                ex.Message & Environment.NewLine & Environment.NewLine & "Das Programm startet trotzdem.",
                                "Filament-Verwaltung", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End If

        Application.Run(New MainForm(db, New DruckerUeberwachung(db)))
    End Sub

    ''' <summary>
    ''' Öffnet die Datenbank bzw. verbindet mit dem Server. Klappt das nicht, kann die Verbindung im Dialog
    ''' korrigiert werden; Nothing = Abbruch.
    ''' </summary>
    Private Function DatenquelleOeffnen(args As String()) As IDatenquelle
        If args IsNot Nothing AndAlso args.Length > 0 AndAlso Not String.IsNullOrWhiteSpace(args(0)) Then
            Dim pfad = Path.GetFullPath(args(0))
            Try
                Return New Datenbank(pfad)
            Catch ex As Exception
                MessageBox.Show("Die Datenbank konnte nicht geöffnet werden:" & Environment.NewLine & pfad &
                                Environment.NewLine & Environment.NewLine & ex.Message,
                                "Filament-Verwaltung", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return Nothing
            End Try
        End If

        Dim v = Verbindung.Laden()
        Do
            Dim fehler As String
            Try
                Cursor.Current = Cursors.WaitCursor
                Return v.Oeffnen()
            Catch ex As Exception
                fehler = If(v.IstServer, "", "Die Datenbank konnte nicht geöffnet werden: ") & ex.Message
            Finally
                Cursor.Current = Cursors.Default
            End Try
            Using dlg As New VerbindungDialog(v, fehler)
                If dlg.ShowDialog() <> DialogResult.OK Then Return Nothing
                v = dlg.Ergebnis
            End Using
        Loop
    End Function

End Module
