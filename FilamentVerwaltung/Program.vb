Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Einstiegspunkt der Anwendung.
''' Optional kann der Pfad zur Datenbankdatei als Startparameter übergeben werden:
'''   FilamentVerwaltung.exe "D:\Daten\filament.db"
''' </summary>
Public Module Program

    <STAThread>
    Public Sub Main(args As String())
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Updater.Aufraeumen()

        Dim dbPfad As String
        If args IsNot Nothing AndAlso args.Length > 0 AndAlso Not String.IsNullOrWhiteSpace(args(0)) Then
            dbPfad = Path.GetFullPath(args(0))
        Else
            dbPfad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                  "FilamentVerwaltung", "filament.db")
        End If

        Dim db As Datenbank
        Try
            db = New Datenbank(dbPfad)
        Catch ex As Exception
            MessageBox.Show("Die Datenbank konnte nicht geöffnet werden:" & Environment.NewLine & dbPfad &
                            Environment.NewLine & Environment.NewLine & ex.Message,
                            "Filament-Verwaltung", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

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

        ' Einmal pro Tag eine Sicherung anlegen (schlägt das fehl, trotzdem starten und darauf hinweisen)
        Try
            Sicherung.AutomatischSichern(db)
        Catch ex As Exception
            MessageBox.Show("Die automatische Sicherung der Datenbank ist fehlgeschlagen:" & Environment.NewLine &
                            ex.Message & Environment.NewLine & Environment.NewLine & "Das Programm startet trotzdem.",
                            "Filament-Verwaltung", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try

        Application.Run(New MainForm(db, New DruckerUeberwachung(db)))
    End Sub

End Module
