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
