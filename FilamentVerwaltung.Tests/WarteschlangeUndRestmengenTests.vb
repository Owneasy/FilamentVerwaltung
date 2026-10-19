Imports Xunit

''' <summary>Druck-Warteschlange (Reihenfolge, Verschieben, gelöschte Dateien) und Restmengen-Prüfung vor dem Druck.</summary>
Public Class WarteschlangeUndRestmengenTests

    <Fact>
    Public Sub Warteschlange_Reihenfolge_Verschieben_und_geloeschte_Datei()
        Dim db = NeueDatenbank()
        Dim a = db.DateiImportieren(GcodeDatei("A.gcode"))
        Dim b = db.DateiImportieren(GcodeDatei("B.gcode"))
        Dim c = db.DateiImportieren(GcodeDatei("C.gcode"))
        For Each d In {a, b, c}
            db.WarteschlangeSpeichern(New Auftrag With {.DateiId = d.Id})
        Next
        Assert.Equal({"A.gcode", "B.gcode", "C.gcode"}, db.WarteschlangeLaden().Select(Function(x) x.DateiName))

        Dim auftragC = db.WarteschlangeLaden().Last()
        db.WarteschlangeVerschieben(auftragC.Id, nachVorn:=True)
        Assert.Equal({"A.gcode", "C.gcode", "B.gcode"}, db.WarteschlangeLaden().Select(Function(x) x.DateiName))
        db.WarteschlangeVerschieben(auftragC.Id, nachVorn:=True)
        db.WarteschlangeVerschieben(auftragC.Id, nachVorn:=True)   ' schon vorn – nichts passiert
        Assert.Equal("C.gcode", db.WarteschlangeLaden().First().DateiName)

        db.DateiLoeschen(a)
        Assert.Equal({"C.gcode", "B.gcode"}, db.WarteschlangeLaden().Select(Function(x) x.DateiName))
    End Sub

    <Fact>
    Public Sub Warteschlange_merkt_Drucker_und_Platte()
        Dim db = NeueDatenbank()
        Dim d As New Drucker With {.Name = "X1C", .Typ = Drucker.TypBambu}
        db.DruckerSpeichern(d)
        Dim datei = db.DateiImportieren(GcodeDatei("Teil.gcode", 33))
        db.WarteschlangeSpeichern(New Auftrag With {.DateiId = datei.Id, .DruckerId = d.Id, .Platte = 2})
        Dim a = db.WarteschlangeLaden().Single()
        Assert.Equal(d.Id, a.DruckerId)
        Assert.Equal("X1C", a.DruckerName)
        Assert.Equal(2, a.Platte)
        Assert.Equal(33.0, a.Gramm.GetValueOrDefault(), 1)
    End Sub

    <Fact>
    Public Sub Restmengen_alle_Faelle()
        Dim spulen As New List(Of Filament) From {
            New Filament With {.Id = 1, .Code = "FIL-0001", .Hersteller = "Sunlu", .Material = "PLA", .Farbe = "Schwarz", .Restgewicht = 140},
            New Filament With {.Id = 2, .Code = "FIL-0002", .Hersteller = "Sunlu", .Material = "PLA", .Farbe = "Schwarz", .Restgewicht = 950},
            New Filament With {.Id = 3, .Code = "FIL-0003", .Hersteller = "Sunlu", .Material = "PLA", .Farbe = "Schwarz", .Restgewicht = 400},
            New Filament With {.Id = 4, .Code = "FIL-0004", .Hersteller = "eSun", .Material = "PETG", .Farbe = "Weiß", .Restgewicht = 210},
            New Filament With {.Id = 5, .Code = "FIL-0005", .Hersteller = "eSun", .Material = "PETG", .Farbe = "Blau", .Restgewicht = 900}}
        Dim zuordnung As New Dictionary(Of Integer, Long) From {{0, 1}, {1, 4}, {2, 5}}
        Dim bedarf As New List(Of FilamentBedarf) From {
            New FilamentBedarf With {.Bezeichnung = "F1", .Gramm = 180, .Slot = 0},
            New FilamentBedarf With {.Bezeichnung = "F2", .Gramm = 200, .Slot = 1},
            New FilamentBedarf With {.Bezeichnung = "F3", .Gramm = 30, .Slot = 2},
            New FilamentBedarf With {.Bezeichnung = "F4", .Gramm = 50, .Slot = 2},
            New FilamentBedarf With {.Bezeichnung = "F5", .Gramm = 20, .Slot = 3}}
        Dim h = Restmengen.Pruefen(bedarf, zuordnung, spulen)

        Assert.Equal(4, h.Count)
        Assert.Equal(RestmengenHinweis.Stufe.ReichtNicht, h(0).Ergebnis)
        Assert.Equal("FIL-0003", h(0).Alternative.Code)   ' kleinste gleiche Sorte, die mit Reserve reicht
        Assert.Equal(RestmengenHinweis.Stufe.Knapp, h(1).Ergebnis)
        Assert.Equal(RestmengenHinweis.Stufe.Reicht, h(2).Ergebnis)
        Assert.Contains("80 g", h(2).Text)                ' zwei Filamente im selben Fach werden addiert
        Assert.Equal(RestmengenHinweis.Stufe.Unbekannt, h(3).Ergebnis)
    End Sub

    <Fact>
    Public Sub Restmengen_ohne_Bedarf_keine_Hinweise()
        Dim h = Restmengen.Pruefen({New FilamentBedarf With {.Gramm = 0, .Slot = 0}}, New Dictionary(Of Integer, Long), New List(Of Filament))
        Assert.Empty(h)
    End Sub

End Class
