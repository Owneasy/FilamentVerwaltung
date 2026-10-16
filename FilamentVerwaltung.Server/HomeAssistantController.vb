Imports System.Threading
Imports Microsoft.AspNetCore.Mvc

''' <summary>Home Assistant (MQTT): Zustand der Verbindung und Verbindungstest für die Einstellungen in der App.</summary>
<ApiController>
<Route("api/homeassistant")>
Public Class HomeAssistantController
    Inherits ControllerBase

    Private ReadOnly _dienst As HomeAssistantDienst

    Public Sub New(dienst As HomeAssistantDienst)
        _dienst = dienst
    End Sub

    <HttpGet("status")>
    Public Function Status() As HaStatus
        Return _dienst.Status
    End Function

    <HttpPost("testen")>
    Public Async Function Testen(<FromBody> v As HaVerbindung, abbruch As CancellationToken) As Task(Of HaStatus)
        Return Await HomeAssistantDienst.TestenAsync(v, abbruch)
    End Function
End Class
