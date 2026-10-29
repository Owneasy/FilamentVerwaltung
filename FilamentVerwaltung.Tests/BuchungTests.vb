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
    Public Sub Stromkosten_kommen_zu_den_Materialkosten_und_lassen_sich_nachrechnen()
        Dim db = NeueDatenbank()
        Dim f = Spule(db, "FIL-0001", rest:=800, gewicht:=1000, preis:=20D)
        db.DruckerSpeichern(New Drucker With {.Name = "X1C", .Typ = Drucker.TypBambu, .Watt = 100})

        ' Ohne Strompreis: nur Material
        Dim ohne = Druck(f, 50) : ohne.Drucker = "X1C" : ohne.Druckzeit = 2 * 3600
        db.DruckSpeichern(ohne)
        Assert.Equal(1D, db.BuchungenLaden().Single().Kosten)

        ' 2 h × 100 W = 0,2 kWh × 0,30 € = 0,06 €
        db.EinstellungSpeichern(Strom.PreisSchluessel, "0.30")
        Dim mit = Druck(f, 50) : mit.Drucker = "x1c" : mit.Druckzeit = 2 * 3600
        db.DruckSpeichern(mit)
        Dim b = db.BuchungenLaden().Single(Function(x) x.Id = mit.Id)
        Assert.Equal(0.06D, b.Stromkosten)
        Assert.Equal(1.06D, b.Kosten)
        Assert.Equal(1D, b.Materialkosten)

        ' Unbekannter Drucker oder keine Druckzeit: kein Strom
        Dim fremd = Druck(f, 50) : fremd.Drucker = "Anderer" : fremd.Druckzeit = 3600
        db.DruckSpeichern(fremd)
        Assert.Equal(0D, db.BuchungenLaden().Single(Function(x) x.Id = fremd.Id).Stromkosten)

        ' Neuer Preis: beide X1C-Drucke werden nachgerechnet, Material bleibt
        db.EinstellungSpeichern(Strom.PreisSchluessel, "0,50")
        Assert.Equal(2, db.StromkostenNachrechnen())
        Assert.All(db.BuchungenLaden().Where(Function(x) x.Drucker <> "Anderer"), Sub(x)
                                                                                      Assert.Equal(0.1D, x.Stromkosten)
                                                                                      Assert.Equal(1.1D, x.Kosten)
                                                                                  End Sub)
        Assert.Equal(0, db.StromkostenNachrechnen())
    End Sub

    <Fact>
    Public Sub Aufheizen_zaehlt_einmal_pro_Druck()
        Dim db = NeueDatenbank()
        Dim f = Spule(db, "FIL-0001", preis:=0D)
        ' Aufheizen 600 W × 5 min = 50 Wh, Drucken 1 h × 100 W = 100 Wh → 0,15 kWh × 0,40 € = 0,06 €
        db.DruckerSpeichern(New Drucker With {.Name = "P1S", .Typ = Drucker.TypBambu, .Watt = 100, .AufheizWatt = 600, .AufheizMinuten = 5})
        Dim d = db.DruckerLaden().Single()
        Assert.Equal(600, d.AufheizWatt) : Assert.Equal(5, d.AufheizMinuten)
        db.EinstellungSpeichern(Strom.PreisSchluessel, "0.40")
        Dim b = Druck(f, 10) : b.Drucker = "P1S" : b.Druckzeit = 3600
        db.DruckSpeichern(b)
        Assert.Equal(0.06D, db.BuchungenLaden().Single().Stromkosten)
        ' Kurzer Druck: das Aufheizen dominiert (50 Wh + 10 Wh)
        Assert.Equal(0.02D, Strom.Kosten(360, 100, 0.4D, 600, 5))
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
