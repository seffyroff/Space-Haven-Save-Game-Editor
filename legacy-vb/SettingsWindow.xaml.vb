Namespace SpaceHavenEditor2

    Class SettingsWindow
        Inherits Window

        ' Public property to get the final setting value
        Public ReadOnly Property BackupSetting As Boolean
            Get
                Return chkBackup.IsChecked.GetValueOrDefault(False) ' Return false if somehow null
            End Get
        End Property

        Public Sub New()
            InitializeComponent()
        End Sub

        ' Method for MainWindow to set the initial value when opening
        Public Sub SetInitialValue(currentValue As Boolean)
            chkBackup.IsChecked = currentValue
        End Sub

        ' OK Button Click Handler
        Private Sub btnOK_Click(sender As Object, e As RoutedEventArgs)
            Me.DialogResult = True ' Closes window and signals OK
        End Sub

        ' Cancel button uses IsCancel=True in XAML, no code needed here

    End Class

End Namespace