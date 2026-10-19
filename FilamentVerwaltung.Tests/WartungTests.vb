Imports Xunit

''' <summary>Wartung: Druckstunden, Fälligkeit nach Stunden und Tagen, Erledigt, Protokoll, Herstellerpläne.</summary>
Public Class WartungTests

    Private Shared Function Drucker(db As Datenbank, name As String, Optional typ As String = FilamentVerwaltung.Drucker.TypBambu) As Drucker
        Dim d As New Drucker With {.Name = name, .Typ = typ}
        db.DruckerSpeichern(d)
        Return d
    End Function

    <Fact>
    Public Sub Faellig_nach_Druckstunden_und_Erledigt_setzt_zurueck()
        Dim db = NeueDatenbank()
        Dim d = Drucker(db, "X1C")
        db.DrucksekundenSetzen(d.Id, 250 * 3600)
        Dim w As New Wartung With {.DruckerId = d.Id, .Name = "Düse reinigen", .IntervallStunden = 100}
        db.WartungSpeichern(w)
        Assert.False(db.WartungLaden().Single().Faellig)

        db.DrucksekundenAddieren(d.Id, 99 * 3600)
        Assert.False(db.WartungLaden().Single().Faellig)
        Assert.True(db.WartungLaden().Single().Bald)
        db.DrucksekundenAddieren(d.Id, 3600)
        Assert.True(db.WartungLaden().Single().Faellig)

        db.WartungErledigt(w.Id, "Cold Pull")
        Dim nachher = db.WartungLaden().Single()
        Assert.False(nachher.Faellig)
        Assert.Equal(350 * 3600L, nachher.ErledigtBeiSekunden)
        Dim eintrag = db.WartungProtokollLaden().Single()
        Assert.Equal("Cold Pull", eintrag.Notiz)
        Assert.Equal(350.0, eintrag.Druckstunden, 3)
    End Sub

    <Fact>
    Public Sub Faellig_nach_Tagen()
        Dim db = NeueDatenbank()
        Dim d = Drucker(db, "A1")
        Dim w As New Wartung With {.DruckerId = d.Id, .Name = "Druckplatte reinigen", .IntervallTage = 7}
        db.WartungSpeichern(w)
        Assert.False(db.WartungLaden().Single().Faellig)
        Sql(db, $"UPDATE Wartung SET ErledigtAm = '{Date.Now.AddDays(-8):o}' WHERE Id = {w.Id}")
        Dim geladen = db.WartungLaden().Single()
        Assert.True(geladen.Faellig)
        Assert.Contains("fällig", geladen.RestText)
    End Sub

    <Fact>
    Public Sub Was_zuerst_eintritt_zaehlt()
        Dim w As New Wartung With {.IntervallStunden = 200, .IntervallTage = 90, .ErledigtAm = Date.Now.AddDays(-91),
                                   .ErledigtBeiSekunden = 0, .DruckerSekunden = 10 * 3600}
        Assert.True(w.Faellig)   ' Tage abgelaufen, Stunden noch lange nicht
        w.ErledigtAm = Date.Now
        w.DruckerSekunden = 201 * 3600
        Assert.True(w.Faellig)   ' Stunden abgelaufen
    End Sub

    <Fact>
    Public Sub Drucker_loeschen_entfernt_seine_Wartung()
        Dim db = NeueDatenbank()
        Dim d = Drucker(db, "Alt")
        db.WartungSpeichern(New Wartung With {.DruckerId = d.Id, .Name = "x", .IntervallTage = 1})
        db.DruckerLoeschen(d.Id)
        Assert.Empty(db.WartungLaden())
    End Sub

    <Fact>
    Public Sub Herstellerplan_passt_zum_Typ_und_legt_nichts_doppelt_an()
        Dim db = NeueDatenbank()
        Dim bambu = Drucker(db, "Bambu")
        Dim prusa = Drucker(db, "MK4", FilamentVerwaltung.Drucker.TypPrusaLink)
        Assert.StartsWith("Bambu Lab", WartungsPlaene.Fuer(bambu.Typ).First().Modell)
        Assert.StartsWith("Prusa", WartungsPlaene.Fuer(prusa.Typ).First().Modell)

        Dim plan = WartungsPlaene.Fuer(bambu.Typ).First()
        Assert.Equal(plan.Aufgaben.Count, WartungsPlaene.Anlegen(db, bambu.Id, plan))
        Assert.Equal(0, WartungsPlaene.Anlegen(db, bambu.Id, plan))
        Assert.Equal(plan.Aufgaben.Count, db.WartungLaden().Count)
        ' Jeder Plan hat ein Intervall je Aufgabe und eine Quelle
        For Each p In WartungsPlaene.Liste
            Assert.StartsWith("https://", p.Quelle)
            Assert.All(p.Aufgaben, Sub(a) Assert.True(a.Stunden.GetValueOrDefault() > 0 OrElse a.Tage.GetValueOrDefault() > 0, a.Name))
        Next
    End Sub

End Class
