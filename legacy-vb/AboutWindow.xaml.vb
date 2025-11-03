Imports System.Windows.Input ' Required for MouseButtonEventArgs
Namespace SpaceHavenEditor2
    Public Class AboutWindow
    Inherits Window

    Public Sub New()
        InitializeComponent()
    End Sub

    ' Handler to allow dragging the borderless window
    Private Sub TitleBar_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        If e.ButtonState = MouseButtonState.Pressed Then
            Me.DragMove()
        End If
    End Sub

    ' Handler for the OK button
    Private Sub CloseButton_Click(sender As Object, e As RoutedEventArgs)
        Me.DialogResult = True ' Or just Me.Close()
    End Sub

End Class


End Namespace