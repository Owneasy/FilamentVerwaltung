Imports Xunit

''' <summary>Buchen, Restgewicht und Fehldruck – das Herzstück: das Restgewicht der Spulen muss immer stimmen.</summary>
Public Class BuchungTests

    Private Shared Function Druck(f As Filament, gramm As Integer, Optional bezeichnung As String = "Test") As Buchung
        Return New Buchung With {.FilamentId = f.Id, .Datum = Date.Now, .Art = Buchung.ArtDruck, .Gramm = gramm, .Bezeichnung = bezeichnung}
    End Function

    <Fact>
    Public Sub Druck_zieht_Gramm_ab_und_berechnet_Kosten()
        Dim db = NeueDatenbank()
        Dim f = Spule(db, "FIL-0001", rest:=800, gewicht:=1000, preis:=20D)
        Dim b = Druck(f, 50)
        db.DruckSpeichern(b)
        Assert.Equal(750, Rest(db, f.Id))
        Assert.Equal(1D, Math.Round(db.BuchungenLaden().Single().Kosten, 2))   ' 50 g von 1000 g zu 20 €
    End Sub

    <Fact>
    Public Sub Bearbeiten_bucht_Differenz_und_Spulenwechsel_richtig()
        Dim db = NeueDatenbank()
        Dim a = Spule(db, "FIL-0001", rest:=800)
        Dim c = Spule(db, "FIL-0002", rest:=500)
        Dim b = Druck(a, 100)
        db.DruckSpeichern(b)
        b.Gramm = 30
        db.DruckSpeichern(b)
        Assert.Equal(770, Rest(db, a.Id))
        ' Auf andere Spule umbuchen: erste bekommt alles zurück
        b.FilamentId = c.Id
        db.DruckSpeichern(b)
        Assert.Equal(800, Rest(db, a.Id))
        Assert.Equal(470, Rest(db, c.Id))
        Assert.Single(db.BuchungenLaden())
    End Sub

    <Fact>
    Public Sub Loeschen_mit_und_ohne_Zurueckbuchen()
        Dim db = NeueDatenbank()
        Dim f = Spule(db, "FIL-0001", rest:=800)
        Dim b1 = Druck(f, 100), b2 = Druck(f, 40)
        db.DruckSpeichern(b1)
        db.DruckSpeichern(b2)
        db.BuchungLoeschen(b1.Id, True)
        Assert.Equal(760, Rest(db, f.Id))
        db.BuchungLoeschen(b2.Id, False)
        Assert.Equal(760, Rest(db, f.Id))
        Assert.Empty(db.BuchungenLaden())
    End Sub

    <Fact>
    Public Sub Restgewicht_faellt_nie_unter_null()
        Dim db = NeueDatenbank()
        Dim f = Spule(db, "FIL-0001", rest:=30)
        db.DruckSpeichern(Druck(f, 80))
        Assert.Equal(0, Rest(db, f.Id))
    End Sub

    <Fact>
    Public Sub Fehldruck_mit_Grund_wird_gespeichert_und_ohne_Fehldruck_geleert()
        Dim db = NeueDatenbank()
        Dim f = Spule(db, "FIL-0001")
        Dim b = Druck(f, 20)
        b.Fehldruck = True
        b.FehldruckGrund = "Spaghetti"
        b.Drucker = "X1C"
        db.DruckSpeichern(b)
        Dim geladen = db.BuchungenLaden().Single()
        Assert.True(geladen.Fehldruck)
        Assert.Equal("Spaghetti", geladen.FehldruckGrund)
        Assert.Equal("X1C", geladen.Drucker)

        geladen.Fehldruck = False
        db.DruckSpeichern(geladen)
        Assert.Equal("", db.BuchungenLaden().Single().FehldruckGrund)
    End Sub

    <Fact>
    Public Sub Alte_Datenbank_wird_umgestellt_Drucker_und_Abbruch_aus_der_Notiz()
        Dim db = NeueDatenbank()
        Dim f = Spule(db, "FIL-0001")
        Dim a = Druck(f, 10) : a.Notiz = "Erkannt am Drucker X1C Büro, abgebrochen bei 42 %. Gewicht: laut Datei."
        Dim b = Druck(f, 10) : b.Notiz = "Erkannt am Drucker Prusa MK4. Gewicht: laut Datei."
        Dim c = Druck(f, 10) : c.Notiz = "Von Hand"
        db.DruckSpeichern(a) : db.DruckSpeichern(b) : db.DruckSpeichern(c)
        ' Stand vor 0.2.4 nachbauen: Spalten fehlen
        Sql(db, "ALTER TABLE Buchung DROP COLUMN Drucker; ALTER TABLE Buchung DROP COLUMN Fehldruck; ALTER TABLE Buchung DROP COLUMN FehldruckGrund;")

        Dim neu As New Datenbank(db.Ort)
        Dim liste = neu.BuchungenLaden()
        Dim x = liste.Single(Function(z) z.Id = a.Id), y = liste.Single(Function(z) z.Id = b.Id), z2 = liste.Single(Function(z) z.Id = c.Id)
        Assert.Equal("X1C Büro", x.Drucker) : Assert.True(x.Fehldruck)
        Assert.Equal("Prusa MK4", y.Drucker) : Assert.False(y.Fehldruck)
        Assert.Equal("", z2.Drucker) : Assert.False(z2.Fehldruck)
    End Sub

End Class
