Imports Xunit

''' <summary>Ordner für Dateien: anlegen, einsortieren, umbenennen (mit Unterordnern), löschen (Inhalt wandert nach oben).</summary>
Public Class OrdnerTests

    <Fact>
    Public Sub Pfade_werden_vereinheitlicht()
        Assert.Equal("Halter/Wand", DateiOrdner.Normalisieren(" /Halter\\ Wand / "))
        Assert.Equal("", DateiOrdner.Normalisieren("  / "))
        Assert.Equal("Halter", DateiOrdner.Eltern("Halter/Wand"))
        Assert.Equal("", DateiOrdner.Eltern("Halter"))
        Assert.Equal("Wand", DateiOrdner.Name("Halter/Wand"))
        Assert.True(DateiOrdner.IstInnerhalb("Halter/Wand", "halter"))
        Assert.False(DateiOrdner.IstInnerhalb("Halterungen", "Halter"))   ' nur ganze Ordnernamen
        Assert.Equal({"A", "A/B", "A/B/C"}, DateiOrdner.MitEltern({"A/B/C"}))
    End Sub

    <Fact>
    Public Sub Leere_Ordner_bleiben_und_Dateiordner_erscheinen_mit_Eltern()
        Dim db = NeueDatenbank()
        db.OrdnerAnlegen("Leer")
        Dim d = db.DateiImportieren(GcodeDatei("Teil.gcode"))
        db.DateiOrdnerSetzen(d.Id, "Halter/Wand")
        Assert.Equal({"Halter", "Halter/Wand", "Leer"}, db.OrdnerLaden())
        Assert.Equal("Halter/Wand", db.DateienLaden().Single().Ordner)
    End Sub

    <Fact>
    Public Sub Umbenennen_nimmt_Unterordner_und_Dateien_mit()
        Dim db = NeueDatenbank()
        Dim a = db.DateiImportieren(GcodeDatei("A.gcode"))
        Dim b = db.DateiImportieren(GcodeDatei("B.gcode"))
        Dim c = db.DateiImportieren(GcodeDatei("C.gcode"))
        db.DateiOrdnerSetzen(a.Id, "Halter")
        db.DateiOrdnerSetzen(b.Id, "Halter/Wand")
        db.DateiOrdnerSetzen(c.Id, "Halterungen")         ' ähnlicher Name – darf nicht mit umbenannt werden
        db.OrdnerAnlegen("Halter/Leer")

        db.OrdnerUmbenennen("Halter", "Werkstatt/Halter")
        Dim ordner = db.DateienLaden().ToDictionary(Function(x) x.Name, Function(x) x.Ordner)
        Assert.Equal("Werkstatt/Halter", ordner("A.gcode"))
        Assert.Equal("Werkstatt/Halter/Wand", ordner("B.gcode"))
        Assert.Equal("Halterungen", ordner("C.gcode"))
        Assert.Contains("Werkstatt/Halter/Leer", db.OrdnerLaden())
        Assert.DoesNotContain("Halter", db.OrdnerLaden())
    End Sub

    <Fact>
    Public Sub Ordner_nicht_in_sich_selbst_verschieben()
        Dim db = NeueDatenbank()
        db.OrdnerAnlegen("A/B")
        Assert.Throws(Of ArgumentException)(Sub() db.OrdnerUmbenennen("A", "A/B/A"))
    End Sub

    <Fact>
    Public Sub Loeschen_verschiebt_Inhalt_eine_Ebene_nach_oben()
        Dim db = NeueDatenbank()
        Dim a = db.DateiImportieren(GcodeDatei("A.gcode"))
        Dim b = db.DateiImportieren(GcodeDatei("B.gcode"))
        db.DateiOrdnerSetzen(a.Id, "Projekt/Teile")
        db.DateiOrdnerSetzen(b.Id, "Projekt/Teile/Klein")
        db.OrdnerLoeschen("Projekt/Teile")
        Dim ordner = db.DateienLaden().ToDictionary(Function(x) x.Name, Function(x) x.Ordner)
        Assert.Equal("Projekt", ordner("A.gcode"))
        Assert.Equal("Projekt/Klein", ordner("B.gcode"))
        Assert.Equal(2, db.DateienLaden().Count)            ' nichts gelöscht
    End Sub

End Class
