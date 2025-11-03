Imports System.ComponentModel ' Optional: For INotifyPropertyChanged later if needed

' Class to hold info about a relationship with one other character
Public Class RelationshipInfo ' Implement INotifyPropertyChanged if using advanced binding later
    Private _targetId As Integer
    Private _targetName As String
    Private _friendship As Integer
    Private _attraction As Integer
    Private _compatibility As Integer

    Public Property TargetId As Integer
        Get
            Return _targetId
        End Get
        Set(value As Integer)
            _targetId = value
            ' RaisePropertyChanged("TargetId") ' For INotifyPropertyChanged
        End Set
    End Property

    Public Property TargetName As String
        Get
            Return _targetName
        End Get
        Set(value As String)
            _targetName = value
            ' RaisePropertyChanged("TargetName")
        End Set
    End Property

    Public Property Friendship As Integer
        Get
            Return _friendship
        End Get
        Set(value As Integer)
            _friendship = value
            ' RaisePropertyChanged("Friendship")
        End Set
    End Property

    Public Property Attraction As Integer
        Get
            Return _attraction
        End Get
        Set(value As Integer)
            _attraction = value
            ' RaisePropertyChanged("Attraction")
        End Set
    End Property

    Public Property Compatibility As Integer
        Get
            Return _compatibility
        End Get
        Set(value As Integer)
            _compatibility = value
            ' RaisePropertyChanged("Compatibility")
        End Set
    End Property

    ' Constructor with corrected parameter name
    Public Sub New(tId As Integer, tName As String, friendshipValue As Integer, attract As Integer, compat As Integer)
        TargetId = tId
        TargetName = tName
        Friendship = friendshipValue ' Use the corrected parameter name here
        Attraction = attract
        Compatibility = compat
    End Sub

    ' Required for INotifyPropertyChanged if implemented
    ' Public Event PropertyChanged As PropertyChangedEventHandler
    ' Private Sub RaisePropertyChanged(propertyName As String)
    '     RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
    ' End Sub
End Class
