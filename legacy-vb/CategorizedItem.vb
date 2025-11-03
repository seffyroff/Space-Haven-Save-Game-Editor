' Helper class to hold item details including category
Public Class CategorizedItem
    Public Property Category As String
    Public Property ItemName As String
    Public Property ItemId As Integer

    Public Sub New(cat As String, name As String, id As Integer)
        Category = cat
        ItemName = name
        ItemId = id
    End Sub
End Class