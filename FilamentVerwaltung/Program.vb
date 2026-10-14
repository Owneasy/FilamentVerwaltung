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

        Application.Run(New MainForm(db))
    End Sub

End Module
