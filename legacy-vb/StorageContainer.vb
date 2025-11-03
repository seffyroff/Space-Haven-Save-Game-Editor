Imports System.Collections.Generic
Imports System.Linq
Imports System.Xml.Linq ' Needed for XElement

Public Class StorageContainer
    ' Reference to the actual <feat> element in the XML document
    Public Property FeatElement As XElement

    ' A user-friendly name for the container, generated on creation
    Public Property DisplayName As String

    ' The list of items within this specific container
    Public Property Items As New List(Of StorageItem)

    ' Store the parent EntId if found, useful for display/debugging
    Public Property ParentEntId As Integer? = Nothing
    ' Store the parent ObjId if found, useful for display/debugging
    Public Property ParentObjId As String = "N/A"


    ' Constructor
    Public Sub New(featureElement As XElement, index As Integer)
        Me.FeatElement = featureElement
        Me.Items = New List(Of StorageItem)()
        Me.DisplayName = GenerateFriendlyName(featureElement, index)
    End Sub

    ' --- Improved Naming Logic ---
    Private Function GenerateFriendlyName(featElement As XElement, index As Integer) As String
        Dim parentE = featElement.Ancestors("e").FirstOrDefault()
        Dim entIdStr As String = parentE?.Attribute("entId")?.Value
        Dim localEntId As Integer? = Nothing
        Dim localObjId As String = parentE?.Attribute("objId")?.Value

        ' Store parent IDs for potential later use/display
        If Integer.TryParse(entIdStr, 0) Then localEntId = CInt(entIdStr)
        If Not String.IsNullOrEmpty(localObjId) Then Me.ParentObjId = localObjId
        Me.ParentEntId = localEntId

        ' Generate display name
        If localEntId.HasValue AndAlso localEntId.Value <> 0 Then
            ' Best case: Use entId if available and valid
            Return $"Container (ID: {localEntId.Value})"
        ElseIf Not String.IsNullOrEmpty(localObjId) Then
            ' Fallback 1: Use objId (type) and index
            Return $"Storage (Type: {localObjId}) - {index + 1}"
        Else
            ' Fallback 2: Generic index
            Return $"Storage Bay - {index + 1}"
        End If
    End Function

    Public Overrides Function ToString() As String
        Return DisplayName
    End Function
End Class