Imports System.Data

Public Class Character

    Public Property CharacterName As String
    Public Property CharacterEntityId As Integer
    Public Property ShipSid As Integer
    Public Property CharacterStats As New List(Of DataProp)
    Public Property CharacterAttributes As New List(Of DataProp)
    Public Property CharacterSkills As New List(Of DataProp)
    Public Property CharacterTraits As New List(Of DataProp)

    Public Property CharacterConditions As New List(Of DataProp)

    Public Property CharacterRelationships As New List(Of RelationshipInfo)
    Public Sub New()
        CharacterAttributes = New List(Of DataProp)
        CharacterSkills = New List(Of DataProp)
        CharacterTraits = New List(Of DataProp)
        CharacterConditions = New List(Of DataProp)
        CharacterRelationships = New List(Of RelationshipInfo)

    End Sub

End Class
