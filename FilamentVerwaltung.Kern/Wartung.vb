''' <summary>
''' Wartungsaufgabe eines Druckers, fällig nach Druckstunden, nach Tagen oder beidem (was zuerst eintritt).
''' Die Druckstunden zählt die Drucker-Überwachung mit (<see cref="Drucker.Drucksekunden"/>).
''' </summary>
Public Class Wartung
    Public Property Id As Long
    Public Property DruckerId As Long
    Public Property Name As String = ""
    ''' <summary>Alle so viele Druckstunden (leer = nicht nach Stunden).</summary>
    Public Property IntervallStunden As Double?
    ''' <summary>Alle so viele Tage (leer = nicht nach Tagen).</summary>
    Public Property IntervallTage As Integer?
    ''' <summary>Zuletzt erledigt (bzw. angelegt).</summary>
    Public Property ErledigtAm As Date = Date.Now
    ''' <summary>Druckstunden des Druckers beim letzten Erledigen, in Sekunden.</summary>
    Public Property ErledigtBeiSekunden As Long
    Public Property Notiz As String = ""
    ''' <summary>Fälligkeit wurde schon per Push gemeldet (bis zum nächsten Erledigen).</summary>
    Public Property Gemeldet As Boolean

    ' --- Beim Laden ergänzt (nicht gespeichert)
    Public Property DruckerName As String = ""
    ''' <summary>Aktuelle Druckstunden des Druckers in Sekunden.</summary>
    Public Property DruckerSekunden As Long

    Public ReadOnly Property StundenSeitdem As Double
        Get
            Return Math.Max(0, DruckerSekunden - ErledigtBeiSekunden) / 3600.0
        End Get
    End Property

    Public ReadOnly Property TageSeitdem As Double
        Get
            Return Math.Max(0, (Date.Now - ErledigtAm).TotalDays)
        End Get
    End Property

    ''' <summary>Wie weit das Intervall verbraucht ist (1 = fällig); das knappere von Stunden und Tagen.</summary>
    Public ReadOnly Property Anteil As Double
        Get
            Dim a = 0.0
            If IntervallStunden.GetValueOrDefault() > 0 Then a = Math.Max(a, StundenSeitdem / IntervallStunden.Value)
            If IntervallTage.GetValueOrDefault() > 0 Then a = Math.Max(a, TageSeitdem / IntervallTage.Value)
            Return a
        End Get
    End Property

    Public ReadOnly Property Faellig As Boolean
        Get
            Return Anteil >= 1
        End Get
    End Property

    ''' <summary>Bald fällig (ab 90 %).</summary>
    Public ReadOnly Property Bald As Boolean
        Get
            Return Not Faellig AndAlso Anteil >= 0.9
        End Get
    End Property

    ''' <summary>z. B. "alle 200 h", "alle 7 Tage", "alle 200 h oder 30 Tage".</summary>
    Public ReadOnly Property IntervallText As String
        Get
            Dim teile As New List(Of String)
            If IntervallStunden.GetValueOrDefault() > 0 Then teile.Add($"{IntervallStunden.Value:0.#} h")
            If IntervallTage.GetValueOrDefault() > 0 Then teile.Add($"{IntervallTage.Value} {If(IntervallTage.Value = 1, "Tag", "Tage")}")
            Return If(teile.Count = 0, "ohne Intervall", "alle " & String.Join(" oder ", teile))
        End Get
    End Property

    ''' <summary>z. B. "in 37 h", "in 3 Tagen", "seit 12 h fällig".</summary>
    Public ReadOnly Property RestText As String
        Get
            Dim restStunden As Double? = If(IntervallStunden.GetValueOrDefault() > 0, IntervallStunden.Value - StundenSeitdem, CType(Nothing, Double?))
            Dim restTage As Double? = If(IntervallTage.GetValueOrDefault() > 0, IntervallTage.Value - TageSeitdem, CType(Nothing, Double?))
            If Not restStunden.HasValue AndAlso Not restTage.HasValue Then Return "–"
            If Faellig Then
                ' Was zuerst abgelaufen ist
                Dim ueberH = If(restStunden.HasValue AndAlso restStunden.Value <= 0, -restStunden.Value, -1.0)
                Dim ueberT = If(restTage.HasValue AndAlso restTage.Value <= 0, -restTage.Value, -1.0)
                If ueberH >= 0 AndAlso (ueberT < 0 OrElse ueberH / 24 >= ueberT) Then Return If(ueberH < 1, "jetzt fällig", $"seit {ueberH:0} h fällig")
                Return If(ueberT < 1, "jetzt fällig", $"seit {ueberT:0} {If(Math.Round(ueberT) = 1, "Tag", "Tagen")} fällig")
            End If
            Dim texte As New List(Of String)
            If restStunden.HasValue Then texte.Add($"{Math.Ceiling(restStunden.Value):0} h")
            If restTage.HasValue Then texte.Add($"{Math.Ceiling(restTage.Value):0} {If(Math.Ceiling(restTage.Value) = 1, "Tag", "Tagen")}")
            Return "in " & String.Join(" bzw. ", texte)
        End Get
    End Property
End Class

''' <summary>Erledigte Wartung (Protokoll).</summary>
Public Class WartungEintrag
    Public Property Id As Long
    Public Property WartungId As Long
    Public Property DruckerId As Long
    Public Property DruckerName As String = ""
    Public Property Name As String = ""
    Public Property Datum As Date
    ''' <summary>Druckstunden des Druckers beim Erledigen.</summary>
    Public Property Druckstunden As Double
    Public Property Notiz As String = ""
End Class

''' <summary>Vorschläge beim Anlegen (übliche Werte – je nach Drucker anpassen).</summary>
Public Module WartungVorlagen
    Public ReadOnly Liste As (Name As String, Stunden As Double?, Tage As Integer?)() = {
        ("Druckplatte gründlich reinigen (Spülmittel)", Nothing, 7),
        ("Düse reinigen / Cold Pull", 100, Nothing),
        ("Achsen und Führungen reinigen und schmieren", 200, 90),
        ("Riemenspannung prüfen", 300, Nothing),
        ("Extruder-Zahnräder reinigen", 300, Nothing),
        ("Düse tauschen", 500, Nothing),
        ("Lüfter und Kühlkörper entstauben", 500, 180),
        ("Schrauben und Rahmen nachziehen", Nothing, 180)
    }
End Module

''' <summary>Eine Aufgabe aus einem Herstellerplan.</summary>
Public Class PlanAufgabe
    Public Property Name As String = ""
    Public Property Stunden As Double?
    Public Property Tage As Integer?
End Class

''' <summary>Wartungsplan eines Herstellers für eine Modellreihe (Werte aus der Hersteller-Dokumentation).</summary>
Public Class WartungsPlan
    Public Property Modell As String = ""
    ''' <summary>Druckertyp, zu dem der Plan passt (für die Vorauswahl).</summary>
    Public Property Typ As String = ""
    Public Property Quelle As String = ""
    Public Property Aufgaben As New List(Of PlanAufgabe)

    Public Overrides Function ToString() As String
        Return Modell
    End Function
End Class

''' <summary>
''' Wartungspläne der Hersteller. Stand Oktober 2026 nach Bambu Lab Wiki (Basic Maintenance je Modellreihe) und
''' Prusa Knowledge Base (Regular printer maintenance MK4/MK4S/MK3.9). Bambu nennt Zeitabstände, Prusa Druckstunden.
''' </summary>
Public Module WartungsPlaene
    Private Function Neu(name As String, stunden As Double?, tage As Integer?) As PlanAufgabe
        Return New PlanAufgabe With {.Name = name, .Stunden = stunden, .Tage = tage}
    End Function

    Public ReadOnly Liste As New List(Of WartungsPlan) From {
        New WartungsPlan With {
            .Modell = "Bambu Lab X1 / X1 Carbon / X1E", .Typ = Drucker.TypBambu,
            .Quelle = "https://wiki.bambulab.com/en/x1/maintenance/basic-maintenance",
            .Aufgaben = New List(Of PlanAufgabe) From {
                Neu("Carbonstäbe X-Achse prüfen und reinigen (Isopropanol, kein Fett; bei ABS/ASA alle 5 Rollen)", Nothing, 30),
                Neu("Linearstangen Y- und Z-Achse auf Staub prüfen und reinigen", Nothing, 30),
                Neu("Linearstangen Y- und Z-Achse Rostschutz (Öl)", Nothing, 90),
                Neu("Z-Gewindespindeln fetten", Nothing, 90)}},
        New WartungsPlan With {
            .Modell = "Bambu Lab P1P / P1S", .Typ = Drucker.TypBambu,
            .Quelle = "https://wiki.bambulab.com/en/p1/maintenance/p1p-maintenance",
            .Aufgaben = New List(Of PlanAufgabe) From {
                Neu("Carbonstäbe X-Achse prüfen und reinigen (Isopropanol, kein Fett; bei ABS/ASA alle 5 Rollen)", Nothing, 30),
                Neu("Linearstangen Y- und Z-Achse auf Staub prüfen und reinigen", Nothing, 30),
                Neu("Linearstangen Y- und Z-Achse Rostschutz (Öl)", Nothing, 90),
                Neu("Z-Gewindespindeln fetten", Nothing, 90)}},
        New WartungsPlan With {
            .Modell = "Bambu Lab A1", .Typ = Drucker.TypBambu,
            .Quelle = "https://wiki.bambulab.com/en/a1/maintenance/basic-maintenance",
            .Aufgaben = New List(Of PlanAufgabe) From {
                Neu("Y-Schiene reinigen und ölen (danach Druckbett hin und her schieben)", Nothing, 30),
                Neu("Z-Gewindespindeln fetten (danach X-Achse von Hand hoch und runter)", Nothing, 90)}},
        New WartungsPlan With {
            .Modell = "Bambu Lab A1 mini", .Typ = Drucker.TypBambu,
            .Quelle = "https://wiki.bambulab.com/en/a1-mini/maintenance",
            .Aufgaben = New List(Of PlanAufgabe) From {
                Neu("Y-Schiene reinigen und ölen (danach Druckbett hin und her schieben)", Nothing, 30),
                Neu("Z-Gewindespindel fetten (danach X-Achse von Hand hoch und runter)", Nothing, 90)}},
        New WartungsPlan With {
            .Modell = "Prusa MK4 / MK4S / MK3.9", .Typ = Drucker.TypPrusaLink,
            .Quelle = "https://help.prusa3d.com/article/regular-printer-maintenance-mk4-s-mk3-9-s_419000",
            .Aufgaben = New List(Of PlanAufgabe) From {
                Neu("Achsen prüfen: glatte Stangen reinigen und schmieren (nicht die Z-Gewindespindel), Riemen und Riemenräder säubern", 200, Nothing),
                Neu("Nextruder prüfen und reinigen", 200, Nothing),
                Neu("Lüfter prüfen und reinigen", 300, Nothing),
                Neu("Elektrische Steckverbindungen prüfen und neu stecken", 600, Nothing)}}
    }

    ''' <summary>Pläne, die zum Druckertyp passen (die übrigen kommen danach).</summary>
    Public Function Fuer(typ As String) As List(Of WartungsPlan)
        Return Liste.Where(Function(p) p.Typ = typ).Concat(Liste.Where(Function(p) p.Typ <> typ)).ToList()
    End Function

    ''' <summary>
    ''' Legt die Aufgaben eines Plans für einen Drucker an; vorhandene Aufgaben mit gleichem Namen werden übersprungen.
    ''' Gibt die Zahl der neu angelegten zurück.
    ''' </summary>
    Public Function Anlegen(db As IDatenquelle, druckerId As Long, plan As WartungsPlan) As Integer
        Dim vorhanden = db.WartungLaden().Where(Function(w) w.DruckerId = druckerId).Select(Function(w) w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
        Dim neu = 0
        For Each a In plan.Aufgaben.Where(Function(x) Not vorhanden.Contains(x.Name))
            db.WartungSpeichern(New Wartung With {.DruckerId = druckerId, .Name = a.Name, .IntervallStunden = a.Stunden, .IntervallTage = a.Tage,
                                                  .Notiz = "Herstellerplan " & plan.Modell})
            neu += 1
        Next
        Return neu
    End Function
End Module
