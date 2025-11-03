Public Class StorageItem
    Public Property ElementId As Integer
    Public Property Quantity As Integer

    Public ReadOnly Property Name As String
        Get
            If IdCollection.DefaultStorageIDs.ContainsKey(ElementId) Then
                Return IdCollection.DefaultStorageIDs(ElementId)
            Else
                Return $"Unknown Item ({ElementId})" ' Return ID if name is unknown
            End If
        End Get
    End Property

    ' Optional: Constructor for convenience
    Public Sub New(id As Integer, qty As Integer)
        ElementId = id
        Quantity = qty
    End Sub
End Class
