Imports MaterialDesignThemes.Wpf
Imports Microsoft.Win32
Imports System.Data
Imports System.IO
Imports System.Xml.Linq
Imports System.Collections.ObjectModel
Imports System.Collections.Generic
Imports System.Xml.XPath
Imports System.ComponentModel
Imports System.Windows.Threading
Imports Microsoft.VisualBasic.FileIO
Imports System.Windows.Automation
Imports System.Globalization ' Needed for CultureInfo
Imports System.Windows.Input ' Needed for Drag/Drop and Mouse events


Namespace SpaceHavenEditor2

    Class SpaceHavenEditor
        Inherits Window ' Make sure this inherits Window if it's the main window class

        Private currentFilePath As String = ""
        Private xmlDoc As XDocument ' Keep using this for XML operations
        Private characters As New List(Of Character)
        Private ships As New List(Of Ship) ' Your existing list of Ship objects

        Private currentShipStorageContainers As New List(Of StorageContainer)()
        Private CurrentContainerItems As ObservableCollection(Of StorageItem)
        Private _relationshipsCurrentPage As Integer = 1
        Private ReadOnly _relationshipsPageSize As Integer = 15

        'User Settings
        Private _backupEnabled As Boolean = True


        Public Sub New()
            InitializeComponent()
            Try
                _backupEnabled = My.Settings.BackupOnOpen
            Catch ex As Exception
                ' Handle potential error reading settings file
                MessageBox.Show($"Error loading settings: {ex.Message}{vbCrLf}Backup on open defaulted to True.", "Settings Warning", MessageBoxButton.OK, MessageBoxImage.Warning)
                _backupEnabled = True
            End Try

            PrepareGroupedStorageItems()

            Me.DataContext = Me
            AddHandler Me.Closing, AddressOf MainWindow_Closing
        End Sub

        ' 2. MODIFY the SelectionChanged handler to CALL the helper method
        Private Sub cmb_ships_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            Dim selectedShip = TryCast(cmb_ships.SelectedItem, Ship)

            ' Call the helper method to handle the selection
            ProcessShipSelection(selectedShip)
        End Sub


        Private Sub MainWindow_Closing(sender As Object, e As CancelEventArgs)
            Try
                My.Settings.BackupOnOpen = _backupEnabled ' Store current value
                My.Settings.Save() ' Persist settings
            Catch ex As Exception
                MessageBox.Show($"Error saving settings: {ex.Message}", "Settings Error", MessageBoxButton.OK, MessageBoxImage.Error)
                ' Don't cancel closing, just report error
            End Try
        End Sub

        Private Sub ResetApplicationState()
            ' Clear Data Objects
            xmlDoc = Nothing
            currentFilePath = String.Empty
            ships?.Clear()          ' Use null-conditional ?. if ships might be Nothing initially
            characters?.Clear()     ' Use null-conditional ?.
            currentShipStorageContainers?.Clear() ' Use null-conditional ?.
            CurrentContainerItems?.Clear()    ' Use null-conditional ?.

            ' Clear UI Elements - Add Try/Catch around UI access in case window isn't fully loaded/ready
            Try
                ClearGlobalSettingsUI()
                cmb_ships.ItemsSource = Nothing
                ClearUI() ' Clears ship details, crew list, grids
                ClearStorageDisplay() ' Clears storage combo and grid
                ' Clear other UI elements if needed (e.g., status bar text)
                ' txtStatus.Text = "Ready"
            Catch uiEx As Exception
                ' Log or ignore minor UI clearing errors during reset
                Console.WriteLine($"Minor error during UI reset: {uiEx.Message}")
            End Try

            SetWindowTitle() ' Reset window title
        End Sub

        Private Sub SetWindowTitle(Optional filePath As String = "", Optional hasUnsavedChanges As Boolean = False)
            Dim baseTitle = "Moragar's Space Haven Save Editor"
            If String.IsNullOrEmpty(filePath) Then
                Me.Title = baseTitle
            Else
                Dim fileNameOnly As String = "game" ' Default if path parsing fails
                Try
                    ' Try to get the save game folder name (e.g., "MySave")
                    Dim saveFolder = Path.GetDirectoryName(filePath) ' ...\save
                    If Not String.IsNullOrEmpty(saveFolder) Then
                        Dim saveNameFolder = Path.GetDirectoryName(saveFolder) ' ...\MySave
                        If Not String.IsNullOrEmpty(saveNameFolder) Then
                            fileNameOnly = Path.GetFileName(saveNameFolder)
                        End If
                    End If
                Catch pathEx As Exception
                    Console.WriteLine($"Error parsing path for title: {pathEx.Message}")
                End Try
                Me.Title = $"{baseTitle} - [{fileNameOnly}]{If(hasUnsavedChanges, " *", "")}"
            End If
        End Sub


        ' Handle "Open" menu click
        Private Sub OpenFileMenu_Click(sender As Object, e As RoutedEventArgs)

            ' --- Determine Initial Directory (Simpler Fallback) ---
            Dim initialSaveGamePath As String = String.Empty
            Try
                ' Default to My Documents as a common location
                initialSaveGamePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                ' Optionally, check if a 'Bugbyte' or 'SpaceHaven' subfolder exists here?
                ' For simplicity, just defaulting to My Documents for now.
            Catch ex As Exception
                ' Error finding path, let the dialog use its default
                Console.WriteLine($"Error determining initial save path: {ex.Message}")
                initialSaveGamePath = Nothing ' Let OpenFileDialog use default
            End Try
            ' --- End Determine Initial Directory ---


            Dim openFileDialog As New OpenFileDialog() With {
            .InitialDirectory = initialSaveGamePath, ' Use MyDocuments or system default
            .Filter = "Space Haven Save|game;*.sav|All Files (*.*)|*.*", ' Prioritize 'game' files
            .Title = "Select SpaceHaven Save Game File ('game')",
            .CheckFileExists = True,
            .CheckPathExists = True
        }

            If openFileDialog.ShowDialog() = True Then
                Dim selectedFilePath As String = openFileDialog.FileName
                Dim backupPerformedSuccessfully As Boolean = False

                ' --- Backup Logic (with error handling) ---
                If _backupEnabled Then
                    Try
                        Dim sourceDir = Path.GetDirectoryName(selectedFilePath) ' Should be the 'save' folder
                        If String.IsNullOrEmpty(sourceDir) OrElse Not Directory.Exists(sourceDir) Then Throw New DirectoryNotFoundException("Source save directory not found.")
                        Dim sourceName = Path.GetFileName(sourceDir) ' Should be 'save'
                        Dim parentDir = Path.GetDirectoryName(sourceDir) ' This should be the folder containing 'save' (e.g., YourSaveName)
                        If String.IsNullOrEmpty(parentDir) Then Throw New DirectoryNotFoundException("Parent directory (save name folder) not found.")
                        Dim saveGamesDir = Path.GetDirectoryName(parentDir) ' This should be the 'savegames' folder
                        If String.IsNullOrEmpty(saveGamesDir) Then Throw New DirectoryNotFoundException("Parent directory ('savegames') not found.")

                        Dim ts = Date.Now.ToString("yyyyMMdd_HHmmss")
                        Dim backupName = $"{Path.GetFileName(parentDir)}_{sourceName}_backup_{ts}"
                        Dim backupPath = Path.Combine(saveGamesDir, backupName)

                        FileSystem.CopyDirectory(sourceDir, backupPath, UIOption.OnlyErrorDialogs, UICancelOption.DoNothing)
                        backupPerformedSuccessfully = True
                    Catch ex As Exception
                        MessageBox.Show($"Backup failed: {ex.Message}{Environment.NewLine}Attempting to load original file anyway.", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Warning)
                        backupPerformedSuccessfully = False
                    End Try
                End If

                ' --- Reset UI and Data before loading ---
                ResetApplicationState()

                ' --- Loading Logic (with extensive Try...Catch) ---
                Try
                    currentFilePath = selectedFilePath

                    ' Step 1: Load the XML document
                    Try
                        xmlDoc = XDocument.Load(currentFilePath, LoadOptions.None)
                    Catch xmlEx As System.Xml.XmlException
                        MessageBox.Show($"Error parsing the save file XML:{Environment.NewLine}[{xmlEx.GetType().Name}] {xmlEx.Message}{Environment.NewLine}{Environment.NewLine}File: {currentFilePath}{Environment.NewLine}{Environment.NewLine}The file might be corrupted or not a valid Space Haven save.", "XML Load Error", MessageBoxButton.OK, MessageBoxImage.Error)
                        ResetApplicationState()
                        Return
                    Catch fileEx As Exception
                        MessageBox.Show($"Error reading the save file:{Environment.NewLine}[{fileEx.GetType().Name}] {fileEx.Message}{Environment.NewLine}{Environment.NewLine}File: {currentFilePath}", "File Read Error", MessageBoxButton.OK, MessageBoxImage.Error)
                        ResetApplicationState()
                        Return
                    End Try

                    ' Step 2: Basic Validation of loaded XML
                    If xmlDoc Is Nothing OrElse xmlDoc.Root Is Nothing OrElse xmlDoc.Root.Name <> "game" Then
                        MessageBox.Show("The loaded file is not a valid Space Haven save (missing root <game> element).", "Invalid File Structure", MessageBoxButton.OK, MessageBoxImage.Error)
                        ResetApplicationState()
                        Return
                    End If

                    ' Step 3: Load different data sections with individual Try...Catch
                    Dim loadErrors As New System.Text.StringBuilder()

                    ' --- Enhanced Error Reporting in Catch Blocks ---
                    Try : LoadGlobalSettings() : Catch ex As Exception : loadErrors.AppendLine($"- Error loading Global Settings: [{ex.GetType().Name}] {ex.Message}") : ClearGlobalSettingsUI() : End Try
                    Try : LoadShips() : Catch ex As Exception : loadErrors.AppendLine($"- Error loading Ships: [{ex.GetType().Name}] {ex.Message}") : cmb_ships.ItemsSource = Nothing : ClearUI() : ClearStorageDisplay() : End Try
                    Try : LoadCharacters() : Catch ex As Exception : loadErrors.AppendLine($"- Error loading Characters: [{ex.GetType().Name}] {ex.Message}") : lstCharacters.ItemsSource = Nothing : ClearDataGrids() : End Try

                    ' Step 4: Populate UI based on loaded data (if ships were loaded)
                    If ships IsNot Nothing AndAlso ships.Any() Then
                        Try : PrepareGroupedStorageItems() : Catch ex As Exception : loadErrors.AppendLine($"- Error preparing storage item list: [{ex.GetType().Name}] {ex.Message}") : End Try

                        If cmb_ships.Items.Count > 0 Then
                            cmb_ships.SelectedIndex = 0
                            ' Optional: Force processing if SelectionChanged isn't reliable on first load with single item
                            If cmb_ships.Items.Count = 1 Then
                                Dispatcher.BeginInvoke(New Action(Sub() ProcessShipSelection(TryCast(cmb_ships.SelectedItem, Ship))), System.Windows.Threading.DispatcherPriority.Background)
                            End If
                        Else
                            ClearUI() : ClearStorageDisplay()
                            loadErrors.AppendLine("- Warning: No <ship> elements found in the save file.")
                        End If
                    ElseIf loadErrors.Length = 0 Then ' Only report no ships if no other errors occurred
                        loadErrors.AppendLine("- No ships found in the save file.")
                    End If

                    ' Step 5: Report results
                    If loadErrors.Length > 0 Then
                        MessageBox.Show("Save file loaded, but some issues occurred during data reading:" & Environment.NewLine & loadErrors.ToString(), "Load Complete with Issues", MessageBoxButton.OK, MessageBoxImage.Warning)
                    Else
                        MessageBox.Show("Save game loaded successfully!", "Load Complete", MessageBoxButton.OK, MessageBoxImage.Information)
                    End If

                    SetWindowTitle(currentFilePath) ' Update window title

                Catch ex As Exception
                    ' Catch any unexpected errors during the overall loading process
                    ' Show full exception details including stack trace for debugging
                    MessageBox.Show($"An unexpected critical error occurred while loading the save file:{Environment.NewLine}{ex.ToString()}", "Critical Load Error", MessageBoxButton.OK, MessageBoxImage.Error)
                    ResetApplicationState()
                End Try
            End If
        End Sub



        Private Sub LoadGlobalSettings()
            If xmlDoc Is Nothing OrElse xmlDoc.Root Is Nothing Then Return ' Added Root check

            Try
                ' Load Player Credits
                Dim bankElement = xmlDoc.Root.Element("playerBank")
                Dim creditsValue As String = "0" ' Default
                If bankElement IsNot Nothing Then
                    Dim caAttribute = bankElement.Attribute("ca")
                    If caAttribute IsNot Nothing AndAlso Not String.IsNullOrEmpty(caAttribute.Value) Then
                        creditsValue = caAttribute.Value
                    End If
                End If
                txtPlayerCredits.Text = creditsValue

                ' Load Sandbox Mode (Using chkSandbox)
                Dim sandboxIsChecked As Boolean = False ' Default
                ' *** Check your actual XML: Is the element name 'difficulty' or 'diff'? Adjust below if needed. ***
                Dim settingsRootElement = xmlDoc.Root.Element("settings")
                If settingsRootElement IsNot Nothing Then
                    Dim diffElement = settingsRootElement.Element("diff") ' Assuming it's 'diff'
                    If diffElement IsNot Nothing Then
                        Dim sandboxAttribute = diffElement.Attribute("sandbox")
                        If sandboxAttribute IsNot Nothing Then
                            sandboxIsChecked = (sandboxAttribute.Value.ToLowerInvariant() = "true")
                        End If
                    End If
                End If
                chkSandbox.IsChecked = sandboxIsChecked

                ' Load Player Prestige Points
                Dim prestigePoints As Integer = 0 ' Default value
                Try
                    Dim questLines1 = xmlDoc.Root.Element("questLines")
                    If questLines1 IsNot Nothing Then
                        Dim questLines2 = questLines1.Element("questLines")
                        If questLines2 IsNot Nothing Then
                            Dim exodusFleetElement = questLines2.Elements("l").FirstOrDefault(Function(el)
                                                                                                  Dim typeAttr = el.Attribute("type")
                                                                                                  Return typeAttr IsNot Nothing AndAlso typeAttr.Value = "ExodusFleet"
                                                                                              End Function)
                            If exodusFleetElement IsNot Nothing Then
                                Dim prestigeAttr = exodusFleetElement.Attribute("playerPrestigePoints")
                                If prestigeAttr IsNot Nothing Then
                                    Integer.TryParse(prestigeAttr.Value, prestigePoints) ' TryParse handles errors, defaults to 0 if fail
                                End If
                            End If
                        End If
                    End If
                Catch ex As Exception
                    Console.WriteLine($"Error loading Prestige Points: {ex.Message}")
                    prestigePoints = 0 ' Ensure default on error
                End Try
                txtPrestigePoints.Text = prestigePoints.ToString()

            Catch ex As Exception
                MessageBox.Show($"Error loading global settings: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error)
                ' Reset UI elements to defaults on error
                txtPlayerCredits.Text = "0"
                chkSandbox.IsChecked = False
                txtPrestigePoints.Text = "0"
            End Try
        End Sub
        ' --- Click Handler for Update Global Settings Button ---
        Private Sub btnUpdateGlobalSettings_Click(sender As Object, e As RoutedEventArgs)
            If xmlDoc Is Nothing OrElse xmlDoc.Root Is Nothing Then ' Added Root check
                MessageBox.Show("No save file loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If

            Dim settingsUpdated As Boolean = False
            Dim validationError As Boolean = False
            Dim errorMessages As New System.Text.StringBuilder()

            Try
                ' --- Validate and Update Player Credits (Memory) ---
                Dim bankElement = xmlDoc.Root.Element("playerBank")
                If bankElement IsNot Nothing Then
                    Dim creditsValue As Double
                    If Double.TryParse(txtPlayerCredits.Text, NumberStyles.Any, CultureInfo.InvariantCulture, creditsValue) Then
                        Dim currentCreditsStr As String = Nothing
                        Dim caAttr = bankElement.Attribute("ca")
                        If caAttr IsNot Nothing Then currentCreditsStr = caAttr.Value

                        Dim newCreditsStr = creditsValue.ToString(CultureInfo.InvariantCulture)
                        If currentCreditsStr <> newCreditsStr Then
                            bankElement.SetAttributeValue("ca", newCreditsStr)
                            settingsUpdated = True
                        End If
                    Else
                        errorMessages.AppendLine("- Invalid Credits value. Please enter a valid number.")
                        validationError = True
                    End If
                Else
                    ' If txtPlayerCredits.Text <> "0" Then errorMessages.AppendLine("- Cannot find <playerBank> node to update credits.")
                End If

                ' --- Validate and Update Prestige Points (Memory) ---
                Dim prestigePoints As Integer
                If Integer.TryParse(txtPrestigePoints.Text, prestigePoints) AndAlso prestigePoints >= 0 Then
                    Dim questLines1 = xmlDoc.Root.Element("questLines")
                    If questLines1 IsNot Nothing Then
                        Dim questLines2 = questLines1.Element("questLines")
                        If questLines2 IsNot Nothing Then
                            Dim exodusFleetElement = questLines2.Elements("l").FirstOrDefault(Function(el)
                                                                                                  Dim typeAttr = el.Attribute("type")
                                                                                                  Return typeAttr IsNot Nothing AndAlso typeAttr.Value = "ExodusFleet"
                                                                                              End Function)
                            If exodusFleetElement IsNot Nothing Then
                                Dim currentPrestigeStr As String = Nothing
                                Dim prestigeAttr = exodusFleetElement.Attribute("playerPrestigePoints")
                                If prestigeAttr IsNot Nothing Then currentPrestigeStr = prestigeAttr.Value

                                Dim newPrestigeStr = prestigePoints.ToString()
                                If currentPrestigeStr <> newPrestigeStr Then
                                    exodusFleetElement.SetAttributeValue("playerPrestigePoints", newPrestigeStr)
                                    settingsUpdated = True
                                End If
                            Else
                                ' If txtPrestigePoints.Text <> "0" Then errorMessages.AppendLine("- Cannot find ExodusFleet quest element to update prestige.")
                            End If
                        End If
                    End If
                Else
                    errorMessages.AppendLine("- Invalid Prestige Points value. Please enter a non-negative whole number.")
                    validationError = True
                End If

                ' --- Update Sandbox Mode (Memory) (Using chkSandbox) ---
                ' *** Check your actual XML: Is the element name 'difficulty' or 'diff'? Adjust below if needed. ***
                Dim settingsRootElement = xmlDoc.Root.Element("settings")
                If settingsRootElement IsNot Nothing Then
                    Dim diffElement = settingsRootElement.Element("diff") ' Assuming it's 'diff'
                    If diffElement IsNot Nothing Then
                        Dim currentSandbox As String = Nothing
                        Dim sandboxAttr = diffElement.Attribute("sandbox")
                        If sandboxAttr IsNot Nothing Then currentSandbox = sandboxAttr.Value.ToLowerInvariant()

                        Dim newSandbox = If(chkSandbox.IsChecked.GetValueOrDefault(), "true", "false")
                        If currentSandbox <> newSandbox Then
                            diffElement.SetAttributeValue("sandbox", newSandbox)
                            settingsUpdated = True
                        End If
                    Else
                        ' If chkSandbox.IsChecked.HasValue Then errorMessages.AppendLine("- Cannot find <settings>/<diff> node to update sandbox mode.")
                    End If
                End If

                ' --- Feedback ---
                If validationError Then
                    MessageBox.Show("Please correct the following errors:" & Environment.NewLine & errorMessages.ToString(),
                                 "Validation Failed", MessageBoxButton.OK, MessageBoxImage.Warning)
                ElseIf settingsUpdated Then
                    MessageBox.Show("Global settings updated in memory. Use File -> Save to make permanent.",
                                 "Globals Updated", MessageBoxButton.OK, MessageBoxImage.Information)
                    ' TODO: Indicate unsaved changes
                    ' hasUnsavedChanges = True
                    ' UpdateWindowTitle(currentFilePath, True)
                Else
                    MessageBox.Show("No changes detected in global settings values.",
                                 "Info", MessageBoxButton.OK, MessageBoxImage.Information)
                End If

            Catch ex As Exception
                MessageBox.Show($"Error updating global settings in memory: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End Sub

        ' --- ADDED: Click Handler for Update Credits Button ---
        Private Sub btnUpdateCredits_Click(sender As Object, e As RoutedEventArgs)
            If xmlDoc Is Nothing OrElse xmlDoc.Root Is Nothing Then
                MessageBox.Show("No save file loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                Return
            End If

            Dim newCreditsString As String = txtPlayerCredits.Text
            Dim newCreditsValue As Integer ' Or Long if needed

            If Not Integer.TryParse(newCreditsString, newCreditsValue) OrElse newCreditsValue < 0 Then
                MessageBox.Show("Invalid credits value. Enter non-negative number.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If

            Try
                ' --- Find playerBank anywhere under the root ---
                Dim playerBankNode As XElement = xmlDoc.Root.Descendants("playerBank").FirstOrDefault()
                ' --- End Find ---

                If playerBankNode IsNot Nothing Then
                    ' Found it, just update the attribute
                    playerBankNode.SetAttributeValue("ca", newCreditsValue.ToString())
                    MessageBox.Show($"Player credits updated to {newCreditsValue} (in memory). Save the file to make changes permanent.", "Credits Updated", MessageBoxButton.OK, MessageBoxImage.Information)
                Else
                    ' Could not find it - inform user, don't create it automatically
                    MessageBox.Show("<playerBank> element not found in the save file. Cannot update credits automatically.", "XML Error", MessageBoxButton.OK, MessageBoxImage.Error)
                    ' NOTE: If desired, code could be added here to create the <settings> and <playerBank> nodes
                    '       if they are missing entirely, but that's riskier if the structure is unknown.
                End If

            Catch ex As Exception
                MessageBox.Show($"Error updating credits in XML: {ex.Message}", "XML Update Error", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End Sub



        Private Sub SettingsMenu_Click(sender As Object, e As RoutedEventArgs)
            Dim settingsWin As New SettingsWindow()
            settingsWin.Owner = Me
            settingsWin.SetInitialValue(_backupEnabled) ' Pass current setting to window

            Dim result = settingsWin.ShowDialog()

            If result.HasValue AndAlso result.Value = True Then
                ' User clicked OK, update the setting from the dialog
                _backupEnabled = settingsWin.BackupSetting
                MessageBox.Show($"Backup on Open setting is now: {_backupEnabled}. Change takes effect next time you open a file.", "Settings Updated", MessageBoxButton.OK, MessageBoxImage.Information)
                ' Setting is saved automatically when main window closes
            End If
        End Sub



        ' --- SIMPLIFIED SaveFileMenu_Click ---
        Private Sub SaveFileMenu_Click(sender As Object, e As RoutedEventArgs)
            If String.IsNullOrEmpty(currentFilePath) OrElse xmlDoc Is Nothing Then
                MessageBox.Show("No file loaded to save.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If

            ' Optional: Commit any pending grid edits if needed
            Try : dgvStorage.CommitEdit(DataGridEditingUnit.Row, True) : Catch : End Try
            Try : dgvRelationships.CommitEdit(DataGridEditingUnit.Row, True) : Catch : End Try

            Try
                ' Global settings should have been updated in memory via the 'Update Globals' button.
                ' Grid/List changes should be handled by their respective edit/add/delete handlers updating the xmlDoc in memory.

                ' TODO: Add any final pre-save update logic if necessary (e.g., character stats if not done elsewhere)
                ' UpdateXmlWithCharacterChanges() ' Example placeholder

                ' Save the entire in-memory XML document to the file
                xmlDoc.Save(currentFilePath)
                MessageBox.Show("File saved successfully!", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information)

                ' TODO: Reset unsaved changes flag and update window title if implementing that
                ' hasUnsavedChanges = False
                ' UpdateWindowTitle(currentFilePath, False)

            Catch ex As Exception
                MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End Sub


        ' --- Add Save Menu Handler ---
        'Private Sub SaveFileMenu_Click(sender As Object, e As RoutedEventArgs)
        '    ' Force commit any pending grid edit before saving
        '    Try : dgvStorage.CommitEdit(DataGridEditingUnit.Row, True) : Catch : End Try

        '    If xmlDoc Is Nothing OrElse String.IsNullOrEmpty(currentFilePath) Then MessageBox.Show("No file loaded.", "Warn", MessageBoxButton.OK, MessageBoxImage.Warning) : Return

        '    Dim settingsUpdated As Boolean = False ' Track if any global setting actually changed

        '    ' --- Actual Save Operation ---
        '    Try
        '        ' --- ADDED: Update Global Settings IN MEMORY before saving ---
        '        ' Update Credits
        '        Dim newCreditsString = txtPlayerCredits.Text : Dim newCreditsValue As Integer
        '        If Integer.TryParse(newCreditsString, newCreditsValue) AndAlso newCreditsValue >= 0 Then
        '            Dim playerBankNode = xmlDoc.Root.Descendants("playerBank").FirstOrDefault()
        '            If playerBankNode IsNot Nothing Then
        '                If playerBankNode.Attribute("ca")?.Value <> newCreditsValue.ToString() Then
        '                    playerBankNode.SetAttributeValue("ca", newCreditsValue.ToString())
        '                    settingsUpdated = True
        '                End If
        '            Else
        '                ' Optionally warn user that playerBank node wasn't found if they tried to edit
        '                If Not String.IsNullOrWhiteSpace(txtPlayerCredits.Text) Then ' Check if user actually typed something
        '                    MessageBox.Show("<playerBank> node not found. Credits cannot be saved.", "Save Warning", MessageBoxButton.OK, MessageBoxImage.Warning)
        '                End If
        '            End If
        '        ElseIf Not String.IsNullOrWhiteSpace(txtPlayerCredits.Text) Then ' Check if user typed invalid data
        '            MessageBox.Show($"Invalid Credits value ('{newCreditsString}') entered. Credits not saved. Please correct and save again.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error)
        '            Return ' Stop the save if credits are invalid
        '        End If

        '        ' Update Sandbox
        '        Dim diffNode = xmlDoc.Root.Element("settings")?.Element("diff")
        '        If diffNode IsNot Nothing Then
        '            Dim currentSandboxValue = diffNode.Attribute("sandbox")?.Value
        '            Dim newSandboxValue = If(chkSandbox.IsChecked.GetValueOrDefault(False), "true", "false") ' Use GetValueOrDefault
        '            If currentSandboxValue <> newSandboxValue Then
        '                diffNode.SetAttributeValue("sandbox", newSandboxValue)
        '                settingsUpdated = True
        '            End If
        '        Else
        '            ' Optionally warn if user changed checkbox but diff node is missing
        '            ' If chkSandbox.IsChecked.HasValue Then ...
        '        End If
        '        ' --- End Update Global Settings ---


        '        UpdateXmlWithCharacterChanges() ' TODO: Implement saving character edits

        '        ' Save the entire in-memory XML document
        '        xmlDoc.Save(currentFilePath)

        '        ' Optionally provide combined feedback
        '        If settingsUpdated Then
        '            MessageBox.Show("File saved successfully (including updated global settings).", "Success", MessageBoxButton.OK, MessageBoxImage.Information)
        '        Else
        '            MessageBox.Show("File saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information)
        '        End If

        '    Catch ex As Exception
        '        MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error)
        '    End Try ' End Try block for saving

        'End Sub

        Private Sub ClearGlobalSettingsUI()
            txtPlayerCredits.Text = ""
            chkSandbox.IsChecked = False
        End Sub


        ' Placeholder for updating XML with character changes before saving
        Private Sub UpdateXmlWithCharacterChanges()
            ' TODO: Iterate through your 'characters' list and update the xmlDoc
            ' This needs to find the character's <c> node in xmlDoc based on CharacterEntityId
            ' Then find the specific <attr>, <skills>, <traits> sections
            ' And update the attributes (points, level, id) based on the values
            ' currently held in the DataProp lists of your 'character' objects.
            ' Example for one attribute of one character (needs loop):
            ' Dim characterToUpdate = characters.FirstOrDefault() ' Example: Get first character
            ' If characterToUpdate IsNot Nothing AndAlso xmlDoc IsNot Nothing Then
            '    Dim charNode = xmlDoc.Descendants("c").FirstOrDefault(Function(c) c.Attribute("entId")?.Value = characterToUpdate.CharacterEntityId.ToString())
            '    If charNode IsNot Nothing Then
            '        Dim attrsElement = charNode.Element("pers")?.Element("attr")
            '        If attrsElement IsNot Nothing Then
            '            For Each localAttr In characterToUpdate.CharacterAttributes
            '                Dim attrNode = attrsElement.Elements("a").FirstOrDefault(Function(a) a.Attribute("id")?.Value = localAttr.Id.ToString())
            '                If attrNode IsNot Nothing Then
            '                     attrNode.SetAttributeValue("points", localAttr.Value.ToString())
            '                ' Else: Handle adding attribute if it doesn't exist?
            '                End If
            '            Next
            '        End If
            '        ' Similar logic for Skills (<skills> -> <s>, update level/mxn)
            '        ' Similar logic for Traits (<traits> -> <t>, add/remove <t> elements based on list)
            '    End If
            ' End If
        End Sub

        Private Sub LoadOwnerInfo(shipSid As Integer) ' Helper to load owner info
            Dim ownerText = "Owner: Unknown" ' Default text
            If xmlDoc IsNot Nothing Then
                Try
                    ' Find the specific ship element in the loaded XML
                    Dim shipElement = xmlDoc.Descendants("ship").FirstOrDefault(Function(s) s.Attribute("sid")?.Value = shipSid.ToString())
                    If shipElement IsNot Nothing Then
                        ' Find the <settings> element within this ship
                        Dim settingsElement = shipElement.Element("settings")
                        If settingsElement IsNot Nothing Then
                            ' Get the value of the "owner" attribute
                            Dim ownerValue As String = settingsElement.Attribute("owner")?.Value
                            If Not String.IsNullOrEmpty(ownerValue) Then
                                ' Check if the owner value indicates the player
                                ownerText = If(ownerValue.Equals("Player", StringComparison.OrdinalIgnoreCase),
                                       "Owner: Player",
                                       $"Owner: {ownerValue}") ' Display actual owner if not "Player"
                            Else
                                ownerText = "Owner: Not Specified" ' Owner attribute missing value
                            End If
                        Else
                            ownerText = "Owner: Settings Missing" ' <settings> tag missing in ship
                        End If
                    Else
                        ownerText = "Owner: Ship Node Error" ' Ship XML element not found (shouldn't happen if SID is valid)
                    End If
                Catch ex As Exception
                    ' Handle potential errors during XML parsing for this specific part
                    ownerText = "Owner: Load Error"
                    ' Optionally log the exception ex.Message
                End Try
            End If
            ' Update the label text
            lbl_owner.Text = ownerText
        End Sub

        Private Sub ProcessShipSelection(selectedShip As Ship)
            If selectedShip Is Nothing OrElse selectedShip.Sid = -1 Then
                ClearUI()
                ClearStorageDisplay()
                Return
            End If

            ' --- This is the logic moved from the original SelectionChanged handler ---
            ' Display ship size
            lbl_shipSize.Text = $"Size: {selectedShip.Sx}x{selectedShip.Sy}"
            Dim canvasWidth As Integer = selectedShip.Sx \ 28 ' Integer division
            Dim canvasHeight As Integer = selectedShip.Sy \ 28 ' Integer division
            lbl_CanvasSize.Text = $"Canvas Size: {canvasWidth} W x {canvasHeight} H squares"

            ' Load owner info
            LoadOwnerInfo(selectedShip.Sid)

            ' Load crew for the selected ship
            LoadCrewForShip(selectedShip.Sid)

            ' Load Storage Containers for the selected ship
            LoadStorageContainers(selectedShip.Sid)
            ' --- End of moved logic ---
        End Sub



        ' Load ships into the ComboBox and internal list
        Private Sub LoadShips()
            If xmlDoc Is Nothing Then Return

            cmb_ships.ItemsSource = Nothing
            cmb_ships.Items.Clear()
            ships.Clear()
            Dim tempShipList As New List(Of Ship)()

            For Each shipXml In xmlDoc.Descendants("ship")
                Dim sid = 0 : Integer.TryParse(shipXml.Attribute("sid")?.Value, sid)
                If sid = 0 Then Continue For ' Skip if SID is invalid

                Dim sname = If(shipXml.Attribute("sname")?.Value, "Unnamed Ship")
                Dim sxVal = 0 : Integer.TryParse(shipXml.Attribute("sx")?.Value, sxVal)
                Dim syVal = 0 : Integer.TryParse(shipXml.Attribute("sy")?.Value, syVal)

                ' Check if ship with this SID already exists in the main 'ships' list before adding
                If Not ships.Any(Function(s) s.Sid = sid) Then
                    Dim newShip = New Ship() With {.Sid = sid, .Sname = sname, .Sx = sxVal, .Sy = syVal}
                    ships.Add(newShip) ' Add to the main list
                    tempShipList.Add(newShip) ' Add to the list for the ComboBox
                End If
            Next

            ' Bind the temporary list (sorted) to the ComboBox
            cmb_ships.ItemsSource = tempShipList.OrderBy(Function(s) s.Sname).ToList()
            cmb_ships.DisplayMemberPath = "Sname"
            cmb_ships.SelectedValuePath = "Sid"

            ' --- CHANGE IS HERE ---
            If tempShipList.Any() Then
                cmb_ships.SelectedIndex = 0 ' Select the first item

                ' *** If there's only one ship, manually trigger the processing ***
                If tempShipList.Count = 1 Then
                    Dim singleShip = TryCast(cmb_ships.SelectedItem, Ship)
                    If singleShip IsNot Nothing Then
                        ' Use Dispatcher to ensure UI is ready before processing
                        Dispatcher.BeginInvoke(New Action(Sub() ProcessShipSelection(singleShip)), System.Windows.Threading.DispatcherPriority.Background)
                    End If
                End If
                ' --- END OF CHANGE ---
            Else
                ' No ships found, clear related UI
                ClearUI()
                ClearStorageDisplay()
            End If
        End Sub

        ' Load characters into the internal list
        Private Sub LoadCharacters()
            characters.Clear() : If xmlDoc Is Nothing Then Return
            For Each shipXml In xmlDoc.Descendants("ship")
                Dim shipSid = 0 : Integer.TryParse(shipXml.Attribute("sid")?.Value, shipSid) : If shipSid = 0 Then Continue For
                Dim charactersElement = shipXml.Element("characters")
                If charactersElement IsNot Nothing Then
                    For Each cNode In charactersElement.Elements("c")
                        Dim charName = If(cNode.Attribute("name")?.Value, "Unknown")
                        Dim entId = 0 : Integer.TryParse(cNode.Attribute("entId")?.Value, entId)
                        If Not characters.Any(Function(ch) ch.CharacterEntityId = entId) Then
                            Dim character As New Character With {.CharacterName = charName, .CharacterEntityId = entId, .ShipSid = shipSid}
                            LoadSkillsTraitsAttrs(cNode, character) ' Use helper
                            characters.Add(character)
                        End If
                    Next
                End If
            Next
        End Sub
        ' Helper to load details for a character node
        Private Sub LoadSkillsTraitsAttrs(cNode As XElement, character As Character)
            Dim persNode = cNode.Element("pers")
            ' Load Skills
            Dim skillsEl = cNode.Element("pers")?.Element("skills")
            If skillsEl IsNot Nothing Then
                For Each sNode In skillsEl.Elements("s")
                    Dim skillId = 0 : Integer.TryParse(sNode.Attribute("sk")?.Value, skillId)
                    Dim skillLevel = 0 : Integer.TryParse(sNode.Attribute("level")?.Value, skillLevel)
                    character.CharacterSkills.Add(New DataProp With {.Id = skillId, .Name = GetSkillNameById(skillId), .Value = skillLevel})
                Next
            End If
            ' Load Traits
            Dim traitsEl = cNode.Element("pers")?.Element("traits")
            If traitsEl IsNot Nothing Then
                For Each tNode In traitsEl.Elements("t")
                    Dim traitId = 0 : Integer.TryParse(tNode.Attribute("id")?.Value, traitId)
                    character.CharacterTraits.Add(New DataProp With {.Id = traitId, .Name = GetTraitNameById(traitId)})
                Next
            End If
            ' Load Attributes
            Dim attrsEl = cNode.Element("pers")?.Element("attr")
            If attrsEl IsNot Nothing Then
                For Each aNode In attrsEl.Elements("a")
                    Dim attrId = 0 : Integer.TryParse(aNode.Attribute("id")?.Value, attrId)
                    Dim attrPoints = 0 : Integer.TryParse(aNode.Attribute("points")?.Value, attrPoints)
                    character.CharacterAttributes.Add(New DataProp With {.Id = attrId, .Name = GetAttributeNameById(attrId), .Value = attrPoints})
                Next
            End If
            ' Load Conditions ---

            Dim conditionsEl = persNode.Element("conditions")
            If conditionsEl IsNot Nothing Then
                For Each conditionElement In conditionsEl.Elements("c") ' Use correct element name "c"
                    Dim condId As Integer = 0
                    If Integer.TryParse(conditionElement.Attribute("id")?.Value, condId) Then
                        ' --- Check if the ID exists in our dictionary ---
                        If IdCollection.ConditionsIDs.ContainsKey(condId) Then
                            ' Only add if the condition ID is known
                            Dim condName As String = IdCollection.ConditionsIDs(condId)
                            character.CharacterConditions.Add(New DataProp With {.Id = condId, .Name = condName})
                            ' Else: ID not in dictionary, so we simply do nothing (skip/hide it)
                        End If
                        ' --- End check ---
                    End If
                Next
            End If

            Dim socialityNode = persNode.Element("sociality")
            Dim relationshipsNode = socialityNode?.Element("relationships")
            If relationshipsNode IsNot Nothing Then
                For Each relElement In relationshipsNode.Elements("l")
                    Dim targetId As Integer = 0
                    Dim friendship As Integer = 0
                    Dim attraction As Integer = 0
                    Dim compatibility As Integer = 0

                    ' Safely parse attributes
                    Integer.TryParse(relElement.Attribute("targetId")?.Value, targetId)
                    Integer.TryParse(relElement.Attribute("friendship")?.Value, friendship)
                    Integer.TryParse(relElement.Attribute("attraction")?.Value, attraction)
                    Integer.TryParse(relElement.Attribute("compatibility")?.Value, compatibility)

                    If targetId <> 0 Then ' Only add if targetId is valid
                        ' Lookup target character name from the main 'characters' list
                        Dim targetCharacter = characters.FirstOrDefault(Function(c) c.CharacterEntityId = targetId)
                        Dim targetName As String = If(targetCharacter IsNot Nothing, targetCharacter.CharacterName, $"Unknown ID ({targetId})")

                        character.CharacterRelationships.Add(New RelationshipInfo(targetId, targetName, friendship, attraction, compatibility))
                    End If
                Next
            End If
        End Sub

        ' Load crew list for the selected ship ID
        Private Sub LoadCrewForShip(shipSid As Integer)
            Dim shipCrew = characters.Where(Function(c) c.ShipSid = shipSid) _
                                 .OrderBy(Function(c) c.CharacterName) _
                                 .ToList()
            lstCharacters.ItemsSource = shipCrew
            If shipCrew IsNot Nothing Then
                txtCrewCount.Text = $"Total Crew: {shipCrew.Count}"
            Else
                txtCrewCount.Text = "Total Crew: 0"
            End If
            ClearDataGrids()
        End Sub

        ' Handle character selection change
        Private Sub lstCharacters_SelectedIndexChanged(sender As Object, e As SelectionChangedEventArgs)
            If lstCharacters.SelectedItem Is Nothing Then
                ClearDataGrids()
                Return
            End If
            Dim selectedCharacter = TryCast(lstCharacters.SelectedItem, Character)
            If selectedCharacter IsNot Nothing Then
                dgvAttributes.ItemsSource = New ObservableCollection(Of DataProp)(selectedCharacter.CharacterAttributes)
                dgvSkills.ItemsSource = New ObservableCollection(Of DataProp)(selectedCharacter.CharacterSkills.OrderBy(Function(s) s.Id))
                dgvTraits.ItemsSource = New ObservableCollection(Of DataProp)(selectedCharacter.CharacterTraits)
                lstConditions.ItemsSource = New ObservableCollection(Of DataProp)(selectedCharacter.CharacterConditions)
                _relationshipsCurrentPage = 1 ' Reset to first page
                LoadRelationshipsPage()

                'dgvRelationships.ItemsSource = New ObservableCollection(Of RelationshipInfo)(selectedCharacter.CharacterRelationships.OrderBy(Function(r) r.TargetName))
            End If
        End Sub


        Private Sub LoadRelationshipsPage()
            Dim selectedCharacter = TryCast(lstCharacters.SelectedItem, Character)
            If selectedCharacter Is Nothing OrElse selectedCharacter.CharacterRelationships Is Nothing Then
                dgvRelationships.ItemsSource = Nothing
                txtRelationshipPageInfo.Text = "Page 0 of 0"
                btnRelPrev.IsEnabled = False
                btnRelNext.IsEnabled = False
                Return
            End If

            Dim totalItems = selectedCharacter.CharacterRelationships.Count
            Dim totalPages = CInt(Math.Ceiling(CDbl(totalItems) / _relationshipsPageSize))
            If totalPages = 0 Then totalPages = 1 ' Show page 1 of 1 even if empty

            ' Clamp current page within valid range
            If _relationshipsCurrentPage < 1 Then _relationshipsCurrentPage = 1
            If _relationshipsCurrentPage > totalPages Then _relationshipsCurrentPage = totalPages

            ' Get the subset for the current page
            Dim pagedList = selectedCharacter.CharacterRelationships _
                                .OrderBy(Function(r) r.TargetName) _
                                .Skip((_relationshipsCurrentPage - 1) * _relationshipsPageSize) _
                                .Take(_relationshipsPageSize) _
                                .ToList()

            dgvRelationships.ItemsSource = New ObservableCollection(Of RelationshipInfo)(pagedList)

            ' Update paging info text and button states
            txtRelationshipPageInfo.Text = $"Page {_relationshipsCurrentPage} of {totalPages}"
            btnRelPrev.IsEnabled = (_relationshipsCurrentPage > 1)
            btnRelNext.IsEnabled = (_relationshipsCurrentPage < totalPages)
        End Sub

        Private Sub btnRelNext_Click(sender As Object, e As RoutedEventArgs)
            ' Calculate total pages again in case data changed (though unlikely without full reload here)
            Dim selectedCharacter = TryCast(lstCharacters.SelectedItem, Character)
            If selectedCharacter IsNot Nothing AndAlso selectedCharacter.CharacterRelationships IsNot Nothing Then
                Dim totalItems = selectedCharacter.CharacterRelationships.Count
                Dim totalPages = CInt(Math.Ceiling(CDbl(totalItems) / _relationshipsPageSize))
                If _relationshipsCurrentPage < totalPages Then
                    _relationshipsCurrentPage += 1
                    LoadRelationshipsPage()
                End If
            End If
        End Sub

        ' --- ADDED: Click Handlers for Paging Buttons ---
        Private Sub btnRelPrev_Click(sender As Object, e As RoutedEventArgs)
            If _relationshipsCurrentPage > 1 Then
                _relationshipsCurrentPage -= 1
                LoadRelationshipsPage()
            End If
        End Sub

        ''' <summary>
        ''' Loads storage containers based on <feat> elements having an "eatAllowed" attribute (value ignored)
        ''' and containing an <inv> descendant.
        ''' </summary>
        ''' <param name="shipSid">The SID of the ship to load containers for.</param>
        Private Sub LoadStorageContainers(shipSid As Integer)
            ClearStorageDisplay()
            currentShipStorageContainers.Clear()
            If xmlDoc Is Nothing Then Return

            Dim shipElement = xmlDoc.Descendants("ship").FirstOrDefault(Function(s) s.Attribute("sid")?.Value = shipSid.ToString())
            If shipElement Is Nothing Then Return

            ' --- LINQ: Find <feat> elements that HAVE eatAllowed attribute AND contain <inv> ---
            Dim storageFeatElementsQuery = From feat In shipElement.Descendants("feat")
                                           Where feat.Attribute("eatAllowed") IsNot Nothing AndAlso feat.Descendants("inv").Any()
                                           Select feat

            Dim storageFeatElements As List(Of XElement)
            Try
                storageFeatElements = storageFeatElementsQuery.ToList()
            Catch ex As Exception
                Return ' silently fail or log if needed
            End Try

            Dim containerIndex As Integer = 0
            For Each featElement As XElement In storageFeatElements
                Dim container As New StorageContainer(featElement, containerIndex)

                Dim invElement = featElement.Descendants("inv").FirstOrDefault()
                If invElement IsNot Nothing Then
                    For Each itemElement In invElement.Elements("s")
                        Dim itemId As Integer
                        Dim quantity As Integer
                        If Integer.TryParse(itemElement.Attribute("elementaryId")?.Value, itemId) AndAlso
                   Integer.TryParse(itemElement.Attribute("inStorage")?.Value, quantity) AndAlso quantity > 0 Then
                            container.Items.Add(New StorageItem(itemId, quantity))
                        End If
                    Next

                    If container.Items.Any() Then
                        currentShipStorageContainers.Add(container)
                    End If
                End If

                containerIndex += 1
            Next

            cmbStorageContainers.ItemsSource = currentShipStorageContainers.OrderBy(Function(c) c.DisplayName).ToList()
            If cmbStorageContainers.Items.Count > 0 Then
                cmbStorageContainers.SelectedIndex = 0
            Else
                dgvStorage.ItemsSource = Nothing
            End If
        End Sub








        Private Sub cmbStorageContainers_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            If cmbStorageContainers.SelectedItem Is Nothing Then
                dgvStorage.ItemsSource = Nothing
                CurrentContainerItems = Nothing
                ' txtContainerInfo.Text = "(Select Container)" ' Update handled by helper call
                UpdateContainerInfoText(Nothing) ' Call helper with Nothing
                Return
            End If

            Dim selectedContainer = TryCast(cmbStorageContainers.SelectedItem, StorageContainer)

            If selectedContainer IsNot Nothing Then
                CurrentContainerItems = New ObservableCollection(Of StorageItem)(selectedContainer.Items.OrderBy(Function(item) item.Name))
                dgvStorage.ItemsSource = CurrentContainerItems

                ' --- Use Helper Function ---
                UpdateContainerInfoText(CurrentContainerItems)
                ' --- End Change ---

            Else
                dgvStorage.ItemsSource = Nothing
                CurrentContainerItems = Nothing
                UpdateContainerInfoText(Nothing) ' Call helper with Nothing
            End If
        End Sub

        ' *** btnAddItem_Click - Uses FeatElement Reference ***
        'Private Sub btnAddItem_Click(sender As Object, e As RoutedEventArgs)
        '    If cmbStorageContainers.SelectedItem Is Nothing Then
        '        MessageBox.Show("Please select a storage container first.", "No Container Selected", MessageBoxButton.OK, MessageBoxImage.Warning) : Return
        '    End If
        '    If cmbAddItem.SelectedValue Is Nothing Then
        '        MessageBox.Show("Please select an item to add.", "No Item Selected", MessageBoxButton.OK, MessageBoxImage.Warning) : Return
        '    End If
        '    If xmlDoc Is Nothing Then
        '        MessageBox.Show("Save file not loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error) : Return
        '    End If

        '    Dim selectedContainer = TryCast(cmbStorageContainers.SelectedItem, StorageContainer)
        '    Dim itemId = CInt(cmbAddItem.SelectedValue)
        '    Dim quantity As Integer
        '    If Not Integer.TryParse(txtAddQuantity.Text, quantity) OrElse quantity <= 0 Then
        '        MessageBox.Show("Please enter a valid positive quantity.", "Invalid Quantity", MessageBoxButton.OK, MessageBoxImage.Warning) : Return
        '    End If

        '    ' Get the target <feat> element directly from the selected container
        '    Dim targetFeatElement = selectedContainer.FeatElement
        '    If targetFeatElement Is Nothing Then
        '        MessageBox.Show("Error: Selected container object missing its XML element reference.", "Internal Error", MessageBoxButton.OK, MessageBoxImage.Error) : Return
        '    End If

        '    ' Find the <inv> element within this specific <feat> element
        '    Dim targetInv = targetFeatElement.Descendants("inv").FirstOrDefault()
        '    If targetInv Is Nothing Then
        '        MessageBox.Show($"Container '{selectedContainer.DisplayName}' lacks an <inv> node. Creating one.", "Info", MessageBoxButton.OK, MessageBoxImage.Information)
        '        targetInv = New XElement("inv")
        '        targetFeatElement.Add(targetInv) ' Add it to the <feat> element
        '    End If

        '    ' Add/Update item within the targetInv
        '    Dim existingItemElement = targetInv.Elements("s").FirstOrDefault(Function(s) s.Attribute("elementaryId")?.Value = itemId.ToString())
        '    If existingItemElement IsNot Nothing Then
        '        Dim currentQuantity As Integer = 0 : Integer.TryParse(existingItemElement.Attribute("inStorage")?.Value, currentQuantity)
        '        existingItemElement.SetAttributeValue("inStorage", (currentQuantity + quantity).ToString())
        '        existingItemElement.SetAttributeValue("onTheWayIn", "0") : existingItemElement.SetAttributeValue("onTheWayOut", "0")
        '    Else
        '        Dim newItemElement = New XElement("s", New XAttribute("elementaryId", itemId.ToString()), New XAttribute("inStorage", quantity.ToString()), New XAttribute("onTheWayIn", "0"), New XAttribute("onTheWayOut", "0"))
        '        targetInv.Add(newItemElement)
        '    End If

        '    ' Update in-memory list and refresh UI
        '    Dim itemInMemory = selectedContainer.Items.FirstOrDefault(Function(i) i.ElementId = itemId)
        '    If itemInMemory IsNot Nothing Then
        '        itemInMemory.Quantity += quantity
        '    Else
        '        selectedContainer.Items.Add(New StorageItem(itemId, quantity)) ' Assumes StorageItem class exists
        '    End If
        '    dgvStorage.ItemsSource = Nothing ' Force refresh
        '    dgvStorage.ItemsSource = selectedContainer.Items.OrderBy(Function(item) item.Name).ToList()
        '    MessageBox.Show($"{quantity}x {CType(cmbAddItem.SelectedItem, KeyValuePair(Of Integer, String)).Value} added/updated in {selectedContainer.DisplayName} (in memory). Save the file to persist.", "Item Added/Updated", MessageBoxButton.OK, MessageBoxImage.Information)
        'End Sub


        ''' <summary>
        ''' Adds the selected item and quantity to the currently selected storage container.
        ''' </summary>
        Private Sub btnAddItem_Click(sender As Object, e As RoutedEventArgs)
            ' Check if a container is selected
            If cmbStorageContainers.SelectedItem Is Nothing Then
                MessageBox.Show("Please select a storage container first.", "No Container Selected", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return ' Exit Sub if no container selected
            End If ' This End If is CORRECT

            ' Check if an item is selected in the Add Item dropdown
            If cmbAddItem.SelectedValue Is Nothing Then
                MessageBox.Show("Please select an item to add.", "No Item Selected", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return ' Exit Sub if no item selected
            End If ' This End If is CORRECT

            ' Check if the XML document is loaded
            If xmlDoc Is Nothing Then
                MessageBox.Show("Save file not loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                Return ' Exit Sub if file not loaded
            End If ' This End If is CORRECT

            Dim selectedContainer = TryCast(cmbStorageContainers.SelectedItem, StorageContainer)
            Dim itemId As Integer = CInt(cmbAddItem.SelectedValue) ' Get the selected Item ID
            Dim itemName As String = IdCollection.DefaultStorageIDs(itemId) ' Get name for message box
            Dim quantity As Integer

            ' Validate the quantity input
            If Not Integer.TryParse(txtAddQuantity.Text, quantity) OrElse quantity <= 0 Then
                MessageBox.Show("Please enter a valid positive quantity.", "Invalid Quantity", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return ' Exit Sub if quantity invalid
            End If ' This End If is CORRECT

            ' Get the target <feat> element directly from the selected container object
            Dim targetFeatElement As XElement = selectedContainer.FeatElement
            If targetFeatElement Is Nothing Then
                MessageBox.Show("Error: Selected container object is missing its internal XML element reference. Cannot modify.", "Internal Error", MessageBoxButton.OK, MessageBoxImage.Error)
                Return ' Exit Sub if reference is broken
            End If ' This End If is CORRECT

            ' Find the <inv> element within this specific <feat> element
            Dim targetInv As XElement = targetFeatElement.Descendants("inv").FirstOrDefault()

            ' If <inv> doesn't exist within the <feat>, create it
            If targetInv Is Nothing Then
                MessageBox.Show($"Container '{selectedContainer.DisplayName}' lacks an <inv> node. Creating one.", "Info", MessageBoxButton.OK, MessageBoxImage.Information)
                targetInv = New XElement("inv")
                targetFeatElement.Add(targetInv) ' Add it to the <feat> element
            End If

            ' --- Add/Update item logic within the specific targetInv ---
            Dim existingItemElement As XElement = targetInv.Elements("s").FirstOrDefault(Function(s) s.Attribute("elementaryId")?.Value = itemId.ToString())

            If existingItemElement IsNot Nothing Then
                ' Item already exists in this container, update quantity
                Dim currentQuantity As Integer = 0
                Integer.TryParse(existingItemElement.Attribute("inStorage")?.Value, currentQuantity) ' Get current amount
                existingItemElement.SetAttributeValue("inStorage", (currentQuantity + quantity).ToString()) ' Add new quantity
                ' Ensure other tracking attributes are reset (if they exist)
                If existingItemElement.Attribute("onTheWayIn") IsNot Nothing Then existingItemElement.SetAttributeValue("onTheWayIn", "0")
                If existingItemElement.Attribute("onTheWayOut") IsNot Nothing Then existingItemElement.SetAttributeValue("onTheWayOut", "0")
            Else
                ' Item doesn't exist, add a new <s> element
                Dim newItemElement As New XElement("s",
            New XAttribute("elementaryId", itemId.ToString()),
            New XAttribute("inStorage", quantity.ToString()),
            New XAttribute("onTheWayIn", "0"), ' Default values likely needed
            New XAttribute("onTheWayOut", "0")
        )
                targetInv.Add(newItemElement) ' Add the new element to the <inv> node
            End If

            ' --- Refresh the UI ---
            ' 1. Update the in-memory list for the container object
            Dim itemInMemory = selectedContainer.Items.FirstOrDefault(Function(i) i.ElementId = itemId)
            If itemInMemory IsNot Nothing Then
                itemInMemory.Quantity += quantity ' Update existing item in list
            Else
                selectedContainer.Items.Add(New StorageItem(itemId, quantity)) ' Add new item to list
            End If

            ' 2. Refresh the DataGrid by resetting its ItemsSource (simple refresh method)
            dgvStorage.ItemsSource = Nothing ' Clear binding
            dgvStorage.ItemsSource = selectedContainer.Items.OrderBy(Function(i) i.Name).ToList() ' Rebind sorted list

            MessageBox.Show($"{quantity}x {itemName} added/updated in {selectedContainer.DisplayName} (in memory). Save the file to persist changes.", "Item Added/Updated", MessageBoxButton.OK, MessageBoxImage.Information)
        End Sub

        ' *** btnDeleteItem_Click - Uses FeatElement Reference ***
        Private Sub btnDeleteItem_Click(sender As Object, e As RoutedEventArgs)
            If cmbStorageContainers.SelectedItem Is Nothing Then
                MessageBox.Show("Please select a storage container first.", "No Container Selected", MessageBoxButton.OK, MessageBoxImage.Warning) : Return
            End If
            If dgvStorage.SelectedItem Is Nothing Then
                MessageBox.Show("Please select an item in the grid to delete.", "No Item Selected", MessageBoxButton.OK, MessageBoxImage.Warning) : Return
            End If
            If xmlDoc Is Nothing Then
                MessageBox.Show("Save file not loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error) : Return
            End If

            Dim selectedContainer = TryCast(cmbStorageContainers.SelectedItem, StorageContainer)
            Dim selectedStorageItem = TryCast(dgvStorage.SelectedItem, StorageItem)
            Dim itemIdToDelete = selectedStorageItem.ElementId

            ' Get the target <feat> element directly
            Dim targetFeatElement = selectedContainer.FeatElement
            If targetFeatElement Is Nothing Then Return ' Should not happen

            Dim targetInv = targetFeatElement.Descendants("inv").FirstOrDefault()
            If targetInv Is Nothing Then Return ' Should not happen if items were loaded

            ' Find the specific <s> element for the item within this <inv>
            Dim itemElementToDelete = targetInv.Elements("s").FirstOrDefault(Function(s) s.Attribute("elementaryId")?.Value = itemIdToDelete.ToString())
            If itemElementToDelete Is Nothing Then
                MessageBox.Show($"Item {itemIdToDelete} not found in XML for {selectedContainer.DisplayName}.", "XML Error", MessageBoxButton.OK, MessageBoxImage.Error) : Return
            End If

            Dim confirmResult = MessageBox.Show($"Are you sure you want to delete ALL {selectedStorageItem.Quantity} x {selectedStorageItem.Name} from {selectedContainer.DisplayName}?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Question)
            If confirmResult = MessageBoxResult.Yes Then
                itemElementToDelete.Remove() ' Remove from XML
                selectedContainer.Items.Remove(selectedStorageItem) ' Remove from memory
                dgvStorage.ItemsSource = Nothing : dgvStorage.ItemsSource = selectedContainer.Items.OrderBy(Function(item) item.Name).ToList() ' Refresh grid
                MessageBox.Show($"{selectedStorageItem.Name} removed from {selectedContainer.DisplayName} (in memory). Save the file to persist.", "Item Deleted", MessageBoxButton.OK, MessageBoxImage.Information)
            End If
        End Sub

        ' Helper Method to Clear Character Details DataGrids
        Private Sub ClearDataGrids()
            dgvAttributes.ItemsSource = Nothing
            dgvSkills.ItemsSource = Nothing
            dgvTraits.ItemsSource = Nothing
            lstConditions.ItemsSource = Nothing
            dgvRelationships.ItemsSource = Nothing ' Clear Relationships Grid
            txtRelationshipPageInfo.Text = "Page 0 of 0" ' Clear Paging Label
            btnRelPrev.IsEnabled = False
            btnRelNext.IsEnabled = False



        End Sub

        Private Sub UpdateXmlWithShipSize(shipToUpdate As Ship)
            ' Ensure xmlDoc and the ship object are valid
            If xmlDoc Is Nothing OrElse xmlDoc.Root Is Nothing OrElse shipToUpdate Is Nothing Then
                MessageBox.Show("Cannot update XML: XML document or ship data is missing.", "Internal Error", MessageBoxButton.OK, MessageBoxImage.Error)
                Return
            End If

            Try
                ' Find the specific <ship> element using its 'sid' attribute
                Dim shipElement = xmlDoc.Root.Descendants("ship").FirstOrDefault(
                                Function(x) x.Attribute("sid")?.Value = shipToUpdate.Sid.ToString()
                              )

                If shipElement IsNot Nothing Then
                    ' Found the ship element, update its 'sx' and 'sy' attributes
                    shipElement.SetAttributeValue("sx", shipToUpdate.Sx.ToString())
                    shipElement.SetAttributeValue("sy", shipToUpdate.Sy.ToString())

                    ' Optional: Inform user that memory was updated (save still needed)
                    ' MessageBox.Show($"Ship '{shipToUpdate.Sname}' size updated in memory to {shipToUpdate.Sx}x{shipToUpdate.Sy}. Use File->Save.", "Size Updated (Memory)", MessageBoxButton.OK, MessageBoxImage.Information)

                    ' TODO: Set a flag here to indicate unsaved changes if you have one
                    ' hasUnsavedChanges = True
                    ' UpdateWindowTitle(currentFilePath, True) ' Example call

                Else
                    ' Ship element with the matching SID was not found in the XML
                    MessageBox.Show($"Could not find ship with SID {shipToUpdate.Sid} in the loaded XML file to update its size.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                End If
            Catch ex As Exception
                ' Handle any errors during XML searching or attribute setting
                MessageBox.Show($"Error updating ship size in XML: {ex.Message}", "XML Update Error", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End Sub


        ' Update ship size button click
        Private Sub btn_updateSize_Click(sender As Object, e As RoutedEventArgs)
            If cmb_ships.SelectedItem Is Nothing OrElse TryCast(cmb_ships.SelectedItem, Ship)?.Sid = -1 Then
                MessageBox.Show("Please select a valid ship first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If
            Dim selectedShip = CType(cmb_ships.SelectedItem, Ship)

            ' Pass current Sx and Sy to the constructor
            Dim updateWindow As New UpdateShipSizeWindow(selectedShip.Sx, selectedShip.Sy)
            updateWindow.Owner = Me ' Set owner

            ' Show the dialog and check the result
            If updateWindow.ShowDialog() = True Then
                ' If user clicked "Update" and validation passed...

                ' Get the CALCULATED Sx/Sy values back from the dialog's properties
                Dim newSx = updateWindow.ShipWidth
                Dim newSy = updateWindow.ShipHeight

                ' Update the Ship object in our internal list (optional but good practice)
                selectedShip.Sx = newSx
                selectedShip.Sy = newSy

                ' Update labels in main window UI
                lbl_shipSize.Text = $"Size: {selectedShip.Sx}x{selectedShip.Sy}"
                Dim canvasWidth As Integer = selectedShip.Sx \ 28 ' Use integer division
                Dim canvasHeight As Integer = selectedShip.Sy \ 28 ' Use integer division
                lbl_CanvasSize.Text = $"Canvas Size: {canvasWidth} W x {canvasHeight} H squares"

                ' *** CRUCIAL: Update the XML document in memory ***
                UpdateXmlWithShipSize(selectedShip)
            End If
        End Sub

        '' Update XML with Ship Sizes (Uses Descendants for robustness)
        'Private Sub UpdateXmlWithShips()
        '    If xmlDoc Is Nothing Then Return
        '    Dim shipsList = xmlDoc.Descendants("ship")
        '    If Not shipsList.Any Then Return ' No ships found
        '    For Each shipInMemory In ships
        '        Dim shipElement = shipsList.FirstOrDefault(Function(x) x.Attribute("sid")?.Value = shipInMemory.Sid.ToString())
        '        If shipElement IsNot Nothing Then
        '            shipElement.SetAttributeValue("sx", shipInMemory.Sx.ToString())
        '            shipElement.SetAttributeValue("sy", shipInMemory.Sy.ToString())
        '        End If
        '    Next
        '    Try
        '        xmlDoc.Save(currentFilePath)
        '        MessageBox.Show("Ship size updated in the XML.", "Success - Saved", MessageBoxButton.OK, MessageBoxImage.Information)
        '    Catch ex As Exception
        '        MessageBox.Show($"Error saving ship size update: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error)
        '    End Try
        'End Sub

        ' Exit menu click
        Private Sub ExitMenu_Click(sender As Object, e As RoutedEventArgs)
            Application.Current.Shutdown()
        End Sub

        ' --- Helper functions for IDs ---
        Private Function GetAttributeNameById(id As Integer) As String
            Return If(IdCollection.DefaultAttributeIDs.ContainsKey(id), IdCollection.DefaultAttributeIDs(id), $"Unknown ({id})")
        End Function
        Private Function GetSkillNameById(id As Integer) As String
            Return If(IdCollection.DefaultSkillIDs.ContainsKey(id), IdCollection.DefaultSkillIDs(id), $"Unknown ({id})")
        End Function
        Private Function GetTraitNameById(id As Integer) As String
            Return If(IdCollection.DefaultTraitIDs.ContainsKey(id), IdCollection.DefaultTraitIDs(id), $"Unknown ({id})")
        End Function

        ' --- Populate Dropdowns ---
        Private Sub PopulateTraitDropdown()
            Dim sortedTraits = IdCollection.DefaultTraitIDs.OrderBy(Function(kvp) kvp.Value).ToList()
            cmb_addTrait.ItemsSource = sortedTraits
            'cmb_addTrait.ItemsSource = IdCollection.DefaultTraitIDs.ToList()
            cmb_addTrait.DisplayMemberPath = "Value"
            cmb_addTrait.SelectedValuePath = "Key"
            If cmb_addTrait.Items.Count > 0 Then cmb_addTrait.SelectedIndex = 0
        End Sub
        'Private Sub PopulateStorageItemDropdown()
        'cmbAddItem.ItemsSource = IdCollection.DefaultStorageIDs.ToList()
        'cmbAddItem.DisplayMemberPath = "Value"
        'cmbAddItem.SelectedValuePath = "Key"
        'mbAddItem.Items.Count > 0 Then cmbAddItem.SelectedIndex = 0
        'End Sub

        ' --- Trait Add/Delete (Full versions) ---
        Private Sub btn_addTrait_Click(sender As Object, e As RoutedEventArgs)
            Dim selectedCharacter As Character = TryCast(lstCharacters.SelectedItem, Character)
            If selectedCharacter Is Nothing Then
                MessageBox.Show("Please select a character first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If
            If cmb_addTrait.SelectedValue Is Nothing Then
                MessageBox.Show("Please select a trait to add.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If
            Dim selectedTraitId = CInt(cmb_addTrait.SelectedValue)
            Dim selectedTraitName = CType(cmb_addTrait.SelectedItem, KeyValuePair(Of Integer, String)).Value
            If Not selectedCharacter.CharacterTraits.Any(Function(t) t.Id = selectedTraitId) Then
                Dim newTrait As New DataProp With {.Id = selectedTraitId, .Name = selectedTraitName}
                selectedCharacter.CharacterTraits.Add(newTrait)
                dgvTraits.ItemsSource = Nothing
                dgvTraits.ItemsSource = New ObservableCollection(Of DataProp)(selectedCharacter.CharacterTraits)
                MessageBox.Show($"Trait '{selectedTraitName}' added to character (in memory). Save the file to persist.", "Info", MessageBoxButton.OK, MessageBoxImage.Information)
                ' TODO: Update XML in UpdateXmlWithCharacterChanges or here if desired
            Else
                MessageBox.Show("Trait already exists for this character.", "Info", MessageBoxButton.OK, MessageBoxImage.Information)
            End If
        End Sub

        Private Sub btn_deleteTrait_Click(sender As Object, e As RoutedEventArgs)
            Dim selectedCharacter As Character = TryCast(lstCharacters.SelectedItem, Character)
            If selectedCharacter Is Nothing Then
                MessageBox.Show("Please select a character first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If
            Dim selectedTrait As DataProp = TryCast(dgvTraits.SelectedItem, DataProp)
            If selectedTrait IsNot Nothing Then
                selectedCharacter.CharacterTraits.Remove(selectedTrait)
                dgvTraits.ItemsSource = Nothing
                dgvTraits.ItemsSource = New ObservableCollection(Of DataProp)(selectedCharacter.CharacterTraits)
                MessageBox.Show($"Trait '{selectedTrait.Name}' removed from character (in memory). Save the file to persist.", "Info", MessageBoxButton.OK, MessageBoxImage.Information)
                ' TODO: Update XML in UpdateXmlWithCharacterChanges or here if desired
            Else
                MessageBox.Show("Please select a trait to remove from the grid.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning)
            End If
        End Sub

        ' --- Clear UI Helpers ---
        Private Sub ClearUI()
            lbl_owner.Text = "Owner: "
            lbl_shipSize.Text = "Size: "
            lbl_CanvasSize.Text = "Canvas Size: "
            lstCharacters.ItemsSource = Nothing
            ClearDataGrids()
            txtCrewCount.Text = "Total Crew: N/A"
        End Sub

        ' --- Set All Buttons (Full versions, save immediately) ---
        Private Sub btnSetAllSkills_Click(sender As Object, e As RoutedEventArgs)
            If xmlDoc Is Nothing OrElse String.IsNullOrEmpty(currentFilePath) Then
                MessageBox.Show("Load a save file first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If

            ' Update in-memory data (optional)
            For Each character In characters
                For Each skill In character.CharacterSkills
                    skill.Value = 8
                Next
            Next

            ' Refresh grid for selected character
            If lstCharacters.SelectedItem IsNot Nothing Then
                Dim selectedChar = CType(lstCharacters.SelectedItem, Character)
                dgvSkills.ItemsSource = Nothing
                dgvSkills.ItemsSource = New ObservableCollection(Of DataProp)(selectedChar.CharacterSkills)
            End If

            ' Update XML
            Dim shipNodes = xmlDoc.Descendants("ship") ' Find all ships
            If Not shipNodes.Any() Then Return

            For Each shipXml In shipNodes
                Dim charactersElement = shipXml.Element("characters")
                If charactersElement IsNot Nothing Then
                    For Each cNode In charactersElement.Elements("c")
                        Dim skillsElement = cNode.Element("pers")?.Element("skills")
                        If skillsElement IsNot Nothing Then
                            For Each sNode In skillsElement.Elements("s")
                                sNode.SetAttributeValue("level", "8")
                                sNode.SetAttributeValue("mxn", "8")
                            Next
                        End If
                    Next
                End If
            Next

            ' Save changes immediately
            Try
                xmlDoc.Save(currentFilePath)
                MessageBox.Show("All skills set to 8 (level & max) for every character and saved!", "Success - Saved", MessageBoxButton.OK, MessageBoxImage.Information)
            Catch ex As Exception
                MessageBox.Show($"Error saving skill changes: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End Sub

        Private Sub btnSetAllAttributes_Click(sender As Object, e As RoutedEventArgs)
            If xmlDoc Is Nothing OrElse String.IsNullOrEmpty(currentFilePath) Then
                MessageBox.Show("Load a save file first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If

            ' Update in-memory data (optional)
            For Each character In characters
                For Each attrib In character.CharacterAttributes
                    attrib.Value = 5
                Next
            Next

            ' Refresh grid for selected character
            If lstCharacters.SelectedItem IsNot Nothing Then
                Dim selectedChar = CType(lstCharacters.SelectedItem, Character)
                dgvAttributes.ItemsSource = Nothing
                dgvAttributes.ItemsSource = New ObservableCollection(Of DataProp)(selectedChar.CharacterAttributes)
            End If

            ' Update XML
            Dim shipNodes = xmlDoc.Descendants("ship") ' Find all ships
            If Not shipNodes.Any() Then Return

            For Each shipXml In shipNodes
                Dim charactersElement = shipXml.Element("characters")
                If charactersElement IsNot Nothing Then
                    For Each cNode In charactersElement.Elements("c")
                        Dim attrElement = cNode.Element("pers")?.Element("attr")
                        If attrElement IsNot Nothing Then
                            For Each aNode In attrElement.Elements("a")
                                aNode.SetAttributeValue("points", "5") ' Set to 5
                            Next
                        End If
                    Next
                End If
            Next

            ' Save changes immediately
            Try
                xmlDoc.Save(currentFilePath)
                MessageBox.Show("All attributes set to 5 for every character and saved!", "Success - Saved", MessageBoxButton.OK, MessageBoxImage.Information)
            Catch ex As Exception
                MessageBox.Show($"Error saving attribute changes: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End Sub

        ' --- Storage Helpers ---
        Private Sub ClearStorageDisplay()
            cmbStorageContainers.ItemsSource = Nothing
            dgvStorage.ItemsSource = Nothing
            currentShipStorageContainers.Clear() ' Clear internal list too
            CurrentContainerItems = Nothing
            txtContainerInfo.Text = "(Select Container)"
        End Sub
        Private Sub ClearStorageGrid() ' Renamed from previous ambiguous name
            dgvStorage.ItemsSource = Nothing
        End Sub

        '--- TODO: Implement UpdateXmlWithCharacterChanges if you want grid edits saved via the Save menu ---

        Private Sub PrepareGroupedStorageItems()
            Dim categorizedItems As New List(Of CategorizedItem)()
            For Each kvp In IdCollection.DefaultStorageIDs.OrderBy(Function(x) x.Value)
                Dim category As String = GetCategoryForItem(kvp.Key, kvp.Value)
                categorizedItems.Add(New CategorizedItem(category, kvp.Value, kvp.Key))
            Next
            Dim sortedItems = categorizedItems.OrderBy(Function(item) item.Category).ThenBy(Function(item) item.ItemName).ToList()

            ' Create CollectionViewSource for grouping
            Dim cvs As New CollectionViewSource()
            cvs.Source = sortedItems
            If cvs.GroupDescriptions.Count = 0 Then
                cvs.GroupDescriptions.Add(New PropertyGroupDescription("Category"))
            End If

            ' --- SET ItemsSource Directly ---
            cmbAddItem.ItemsSource = cvs.View
            ' --- End Change ---

            ' Removed: GroupedStorageItemsView = cvs.View
            ' Removed: FlatStorageItems = sortedItems
            ' Removed: Debug MessageBox
        End Sub

        Private Function GetCategoryForItem(itemId As Integer, itemName As String) As String
            ' Prioritize specific IDs if needed
            Select Case itemId
                Case 725, 728, 729, 760, 1152, 3069, 3070, 3071, 3072, 3961, 3962
                    Return "Weapons"
                Case 2715, 1926
                    Return "Ammo"
                Case 3960, 3967, 3968, 3969, 4076 ' Weapon Attachments
                    Return "Attachments"
                Case 3384 ' Armor
                    Return "Armor/Apparel"
                Case 3388, 4065 ' Oxygen/Suit stuff
                    Return "Equipment"
            End Select

            ' Categorize by keywords in name (adjust keywords as needed)
            Dim lowerName = itemName.ToLower()
            If lowerName.Contains("food") OrElse lowerName.Contains("meat") OrElse lowerName.Contains("vegetables") OrElse
               lowerName.Contains("fruits") OrElse lowerName.Contains("nuts and seeds") OrElse lowerName.Contains("alcohol") Then
                Return "Food & Drink"
            End If
            If lowerName.Contains("medical") OrElse lowerName.Contains("fluid") OrElse lowerName.Contains("bandage") OrElse
               lowerName.Contains("painkillers") OrElse lowerName.Contains("stimulant") OrElse lowerName.Contains("wound dressing") Then
                Return "Medical"
            End If
            If lowerName.Contains("scrap") OrElse lowerName.Contains("rubble") Then
                Return "Scrap & Waste"
            End If
            If lowerName.Contains("block") OrElse lowerName.Contains("plates") Then
                Return "Building Blocks"
            End If
            If lowerName.Contains("component") OrElse lowerName.Contains("parts") Then
                Return "Components"
            End If
            If lowerName.Contains("rod") OrElse lowerName.Contains("cell") OrElse lowerName.Contains("fuel") OrElse
               lowerName.Contains("energ") OrElse lowerName.Contains("hyperium") Then ' Energium, Energy Rod, Hyperfuel etc.
                Return "Energy & Fuel"
            End If
            If lowerName.Contains("ore") OrElse lowerName.Contains("metals") OrElse lowerName.Contains("carbon") OrElse
               lowerName.Contains("ice") OrElse lowerName.Contains("water") OrElse lowerName.Contains("chemicals") OrElse
               lowerName.Contains("plastics") OrElse lowerName.Contains("fibers") OrElse lowerName.Contains("fabrics") OrElse
               lowerName.Contains("bio matter") Then
                Return "Raw Materials"
            End If
            If lowerName.Contains("corpse") OrElse lowerName.Contains("organs") Then
                Return "Biological"
            End If
            If lowerName.Contains("fertilizer") OrElse lowerName.Contains("grain") Then
                Return "Farming"
            End If

            ' Default category if none matched
            Return "Miscellaneous"
        End Function

        'Private Sub dgvStorage_RowEditEnding(sender As Object, e As DataGridRowEditEndingEventArgs) Handles dgvStorage.RowEditEnding

        '    If e.EditAction <> DataGridEditAction.Commit Then Exit Sub

        '    ' Delay processing until after the row edit is fully committed to avoid reentrancy issues.
        '    Dispatcher.BeginInvoke(New Action(Sub()
        '                                          Dim editedItem As StorageItem = TryCast(e.Row.Item, StorageItem)
        '                                          If editedItem Is Nothing Then Exit Sub

        '                                          Dim newQuantity As Integer = editedItem.Quantity

        '                                          ' Validate the new quantity.
        '                                          If newQuantity < 0 Then
        '                                              MessageBox.Show("Invalid quantity. Please enter 0 or a positive number.", "Invalid Quantity", MessageBoxButton.OK, MessageBoxImage.Warning)
        '                                              Exit Sub
        '                                          End If

        '                                          ' Ensure a storage container is selected.
        '                                          Dim selectedContainer As StorageContainer = TryCast(cmbStorageContainers.SelectedItem, StorageContainer)
        '                                          If selectedContainer Is Nothing Then
        '                                              MessageBox.Show("No storage container selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
        '                                              Exit Sub
        '                                          End If

        '                                          ' Ensure the XML document is loaded.
        '                                          If xmlDoc Is Nothing Then
        '                                              MessageBox.Show("No file loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
        '                                              Exit Sub
        '                                          End If

        '                                          ' Retrieve the corresponding XML <feat> element for the container.
        '                                          Dim targetFeatElement As XElement = selectedContainer.FeatElement
        '                                          If targetFeatElement Is Nothing Then
        '                                              MessageBox.Show("Selected container is missing its XML reference.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
        '                                              Exit Sub
        '                                          End If

        '                                          ' Locate the <inv> node within the container.
        '                                          Dim targetInv As XElement = targetFeatElement.Descendants("inv").FirstOrDefault()
        '                                          If targetInv Is Nothing Then
        '                                              MessageBox.Show("Inventory node (<inv>) not found in the selected container.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
        '                                              Exit Sub
        '                                          End If

        '                                          ' Find the <s> element that matches the StorageItem’s ElementId.
        '                                          Dim itemElement As XElement = targetInv.Elements("s").FirstOrDefault(Function(s) s.Attribute("elementaryId")?.Value = editedItem.ElementId.ToString())

        '                                          If newQuantity > 0 Then
        '                                              ' If the item does not exist in the XML, create it.
        '                                              If itemElement Is Nothing Then
        '                                                  itemElement = New XElement("s",
        '                                                                              New XAttribute("elementaryId", editedItem.ElementId.ToString()),
        '                                                                              New XAttribute("inStorage", newQuantity.ToString()),
        '                                                                              New XAttribute("onTheWayIn", "0"),
        '                                                                              New XAttribute("onTheWayOut", "0"))
        '                                                  targetInv.Add(itemElement)
        '                                              Else
        '                                                  ' Otherwise, update the existing XML element’s quantity.
        '                                                  itemElement.SetAttributeValue("inStorage", newQuantity.ToString())
        '                                              End If
        '                                          Else
        '                                              ' If newQuantity is 0, remove the <s> element if it exists.
        '                                              If itemElement IsNot Nothing Then
        '                                                  itemElement.Remove()
        '                                              End If
        '                                          End If

        '                                      End Sub), System.Windows.Threading.DispatcherPriority.Background)


        'End Sub



        Private Sub dgvStorage_RowEditEnding(sender As Object, e As DataGridRowEditEndingEventArgs) Handles dgvStorage.RowEditEnding
            If e.EditAction <> DataGridEditAction.Commit Then Exit Sub

            Dispatcher.BeginInvoke(New Action(Sub()
                                                  Dim editedItem As StorageItem = TryCast(e.Row.Item, StorageItem)
                                                  If editedItem Is Nothing Then Exit Sub

                                                  Dim newQuantity As Integer = editedItem.Quantity ' Assumes binding updated this

                                                  If newQuantity < 0 Then
                                                      MessageBox.Show("Invalid quantity. Must be 0 or positive.", "Invalid Quantity", MessageBoxButton.OK, MessageBoxImage.Warning)
                                                      ' TODO: Revert UI/Data change if possible
                                                      Exit Sub
                                                  End If

                                                  Dim selectedContainer As StorageContainer = TryCast(cmbStorageContainers.SelectedItem, StorageContainer)
                                                  If selectedContainer Is Nothing OrElse xmlDoc Is Nothing OrElse selectedContainer.FeatElement Is Nothing Then Exit Sub ' Add null checks

                                                  Dim targetFeatElement As XElement = selectedContainer.FeatElement
                                                  Dim targetInv As XElement = targetFeatElement.Descendants("inv").FirstOrDefault()
                                                  If targetInv Is Nothing Then Exit Sub ' Should exist if loaded

                                                  Dim itemElement As XElement = targetInv.Elements("s").FirstOrDefault(Function(s) s.Attribute("elementaryId")?.Value = editedItem.ElementId.ToString())

                                                  Try
                                                      If newQuantity > 0 Then
                                                          If itemElement Is Nothing Then
                                                              itemElement = New XElement("s", New XAttribute("elementaryId", editedItem.ElementId.ToString()), New XAttribute("inStorage", newQuantity.ToString()), New XAttribute("onTheWayIn", "0"), New XAttribute("onTheWayOut", "0"))
                                                              targetInv.Add(itemElement)
                                                          Else
                                                              itemElement.SetAttributeValue("inStorage", newQuantity.ToString())
                                                          End If
                                                      Else ' newQuantity is 0
                                                          If itemElement IsNot Nothing Then itemElement.Remove()
                                                      End If

                                                      ' Update the backing list (selectedContainer.Items) as well
                                                      Dim itemInBackingList = selectedContainer.Items.FirstOrDefault(Function(i) i.ElementId = editedItem.ElementId)
                                                      If newQuantity > 0 Then
                                                          If itemInBackingList Is Nothing Then selectedContainer.Items.Add(editedItem) Else itemInBackingList.Quantity = newQuantity
                                                      Else
                                                          If itemInBackingList IsNot Nothing Then selectedContainer.Items.Remove(itemInBackingList)
                                                          ' CurrentContainerItems updates visually when item removed via grid if bound correctly
                                                      End If

                                                      ' --- ADDED: Update total count label ---
                                                      UpdateContainerInfoText(CurrentContainerItems)
                                                      ' --- End Addition ---

                                                      ' Optional: Keep a success message?
                                                      ' MessageBox.Show($"Quantity for {editedItem.Name} updated to {newQuantity} (in memory). Save to persist.", "Qty Updated", MessageBoxButton.OK, MessageBoxImage.Information)

                                                  Catch ex As Exception
                                                      MessageBox.Show($"Error updating XML for item {editedItem.ElementId}: {ex.Message}", "XML Update Error", MessageBoxButton.OK, MessageBoxImage.Error)
                                                      ' TODO: Revert UI changes if XML update fails?
                                                  End Try

                                              End Sub), DispatcherPriority.Background) ' End Dispatcher.BeginInvoke
        End Sub





        Private Sub UpdateContainerInfoText(itemsToSum As IEnumerable(Of StorageItem))
            If itemsToSum IsNot Nothing Then
                Dim currentTotalQuantity As Integer = itemsToSum.Sum(Function(item) item.Quantity)
                txtContainerInfo.Text = $"Total Items: {currentTotalQuantity}"
            Else
                txtContainerInfo.Text = "(No Items)" ' Or "(Select Container)"
            End If
        End Sub

        ' --- ADD THIS NEW SUBROUTINE ---
        Private Sub HelpMenu_Click(sender As Object, e As RoutedEventArgs)
            Dim helpText As String = GenerateHelpText() ' Get the help text
            Dim helpWin As New HelpWindow(helpText)   ' Create the window, passing text
            helpWin.Owner = Me ' Make it owned by the main window
            helpWin.ShowDialog() ' Show it as a modal dialog
        End Sub

        ' --- ADD THIS NEW FUNCTION TO GENERATE THE HELP TEXT ---
        Private Function GenerateHelpText() As String
            Dim sb As New System.Text.StringBuilder()

            sb.AppendLine("Moragar's Space Haven Save Editor - Help & Instructions")
            sb.AppendLine("======================================================")
            sb.AppendLine() ' Blank line for spacing

            sb.AppendLine("*** DISCLAIMER ***")
            sb.AppendLine("Use this tool at your own risk. Editing save files can lead to unexpected")
            sb.AppendLine("issues or corrupted saves. The creator is not responsible for any damage")
            sb.AppendLine("to your save games, even if the backup feature is enabled.")
            sb.AppendLine("Always keep manual backups of important saves!")
            sb.AppendLine("******************")
            sb.AppendLine() ' Blank line for spacing


            sb.AppendLine("--- Getting Started ---")
            sb.AppendLine("- File -> Open: Use this to load your Space Haven save game.")
            sb.AppendLine("  - Navigate to your save game folder. The typical path is:")
            sb.AppendLine("    Steam\steamapps\common\SpaceHaven\savegames\[YourSaveGameName]\save\")
            sb.AppendLine("  - Select the file named 'game' (it usually has no file extension).")
            sb.AppendLine("- Backups: If enabled in Settings, a timestamped backup of your save folder")
            sb.AppendLine("    (e.g., '[YourSaveGameName]_backup_YYYYMMDDHHMMSS') will be created")
            sb.AppendLine("    automatically in the 'savegames' directory *each time* you open a file.")
            sb.AppendLine("    This is highly recommended to prevent data loss!")
            sb.AppendLine("- File -> Save: IMPORTANT! Click this after making edits to permanently write")
            sb.AppendLine("    your changes back to the 'game' file.")
            sb.AppendLine("- File -> Exit: Closes the editor. Any unsaved changes will be lost.")
            sb.AppendLine("- Edit -> Settings: Opens the Settings window where you can toggle automatic backups.")
            sb.AppendLine("- Help -> Help / Instructions: Shows this information again.")
            sb.AppendLine("- Help -> About: Shows application version and credits.")
            sb.AppendLine()

            sb.AppendLine("--- Editing Your Save ---")
            sb.AppendLine("- Global Settings (Top Section):")
            sb.AppendLine("  - Player Credits: Enter the desired amount of credits.")
            sb.AppendLine("  - Sandbox Mode: Check or uncheck to enable/disable sandbox mode.")
            sb.AppendLine("  - Player Prestige Points: Enter the desired amount of Exodus Fleet Prestige Points")
            sb.AppendLine("  - NOTE: Changes here are applied to memory only when you click File -> Save.")
            sb.AppendLine("- Ship Selection (Middle Section):")
            sb.AppendLine("  - Use the dropdown to select the specific ship you want to view or edit.")
            sb.AppendLine("  - Basic info like Owner and Size is shown below the dropdown.")
            sb.AppendLine("  - Update Size Button: Allows changing the selected ship's dimensions (Sx, Sy values).")
            sb.AppendLine("      - The 'Canvas Size' shows the size in buildable grid squares (Sx/28 x Sy/28).")
            sb.AppendLine("      - Max recommended Canvas Size is 8W x 8H squares (Sx=224, Sy=224).")
            sb.AppendLine() ' Blank line for spacing
            sb.AppendLine("      - **WARNING:** Significantly increasing ship size might cause graphical glitches")
            sb.AppendLine("        or conflicts with other ships/stations in-game. Use with caution!")
            sb.AppendLine("      - Ship size changes save immediately to memory and require File -> Save.")
            sb.AppendLine("- Main Tabs (Crew / Storage):")
            sb.AppendLine("  - Select the 'Crew' or 'Storage' tab to edit details for the currently selected ship.")
            sb.AppendLine()

            sb.AppendLine("--- Crew Tab Details ---")
            sb.AppendLine("- Crew List (Left): Select a crew member. The list shows names; total count is above.")
            sb.AppendLine("- Create New Crew Member: Button below the list opens a window to add a new character.")
            sb.AppendLine("    You can customize their name, stats, and traits before creation.")
            sb.AppendLine("- Editing Tabs (Right - Attributes, Skills, Traits, Conditions, Relationships):")
            sb.AppendLine("  - Attributes/Skills: Double-click a cell in the 'Value' or 'Level' column to edit.")
            sb.AppendLine("      Type the new number and press Enter or click another row to confirm the change.")
            sb.AppendLine("      Use the 'Set All...' buttons for quick presets (these affect ALL characters")
            sb.AppendLine("      and save immediately to memory - use File -> Save to make permanent).")
            sb.AppendLine("  - Traits: Select a trait from the dropdown, click 'Add Trait'. To remove, select a trait")
            sb.AppendLine("      in the grid above, then click 'Delete Trait'.")
            sb.AppendLine("  - Conditions: Shows current status effects/injuries. Select one and click")
            sb.AppendLine("      'Delete Selected Condition' to remove it.")
            sb.AppendLine("  - Relationships: Shows how the selected character feels about others. Edit the")
            sb.AppendLine("      Friendship, Attraction, or Compatibility values by double-clicking the cell,")
            sb.AppendLine("      typing a new number, and pressing Enter or changing rows.")
            sb.AppendLine("  - NOTE: Adding crew, editing grids (Attributes, Skills, Relationships), or changing")
            sb.AppendLine("      Traits/Conditions requires using File -> Save to make permanent.")
            sb.AppendLine()

            sb.AppendLine("--- Storage Tab Details ---")
            sb.AppendLine("- Container Selection: Choose a specific storage container (e.g., 'Storage (Type: storageMedium) - 1')")
            sb.AppendLine("    from the dropdown. The 'Total Items' count for that container is shown next to it.")
            sb.AppendLine("- Item Grid: Shows items in the selected container.")
            sb.AppendLine("- Edit Quantity: Double-click a cell in the 'Quantity' column, type the new amount,")
            sb.AppendLine("    and press Enter or change row to confirm. Setting quantity to 0 removes the stack on save.")
            sb.AppendLine("- Add Item: Below the grid, select an item category, then the specific item. Enter the")
            sb.AppendLine("    desired quantity and click 'Add to Container'.")
            sb.AppendLine("- Delete Item Stack: Select a row in the grid and click 'Delete Selected'.")
            sb.AppendLine("- NOTE: All storage changes (adding, deleting, editing quantity) require File -> Save.")
            sb.AppendLine()

            sb.AppendLine("--- Final Reminder ---")
            sb.AppendLine("- Don't forget File -> Save! Most edits only change the data in memory until saved.")
            sb.AppendLine("- Keep backups of your original save files just in case!")

            Return sb.ToString()
        End Function



        ' --- (Ensure all your other existing methods are still present) ---

        'Private Sub dgvStorage_CellEditEnding(sender As Object, e As DataGridCellEditEndingEventArgs)
        '    MessageBox.Show("DEBUG: dgvStorage_CellEditEnding START", "Debug", MessageBoxButton.OK, MessageBoxImage.Information) ' Start of handler

        '    If e.EditAction = DataGridEditAction.Cancel Then
        '        MessageBox.Show("DEBUG: EditAction was Cancel. Exiting.", "Debug", MessageBoxButton.OK, MessageBoxImage.Warning)
        '        Return ' Edit was cancelled by user (e.g., Esc key)
        '    End If

        '    Dim quantityColumn As DataGridTextColumn = TryCast(e.Column, DataGridTextColumn)
        '    If quantityColumn Is Nothing OrElse quantityColumn.Header?.ToString() <> "Quantity" Then
        '        Dim headerInfo As String = If(e.Column Is Nothing, "Column is Nothing", $"Header is '{If(e.Column?.Header?.ToString(), "NULL")}'")
        '        MessageBox.Show($"DEBUG EXIT: Edited column test failed. {headerInfo}", "Debug Exit", MessageBoxButton.OK, MessageBoxImage.Error)
        '        Return ' Exit because it's not the quantity column or column is null
        '    End If

        '    Dim editedItem As StorageItem = TryCast(e.Row?.Item, StorageItem)
        '    If editedItem Is Nothing Then
        '        Dim itemTypeInfo As String = If(e.Row?.Item Is Nothing, "Row.Item is Nothing", $"Row.Item type is {e.Row.Item.GetType().Name}")
        '        MessageBox.Show($"DEBUG EXIT: Failed to cast Row.Item to StorageItem. {itemTypeInfo}", "Debug Exit", MessageBoxButton.OK, MessageBoxImage.Error)
        '        Return ' Exit because the data item for the row is null or not a StorageItem
        '    End If

        '    Dim editingTextBox As TextBox = TryCast(e.EditingElement, TextBox)
        '    If editingTextBox Is Nothing Then
        '        Dim elementInfo As String = If(e.EditingElement Is Nothing, "EditingElement is Nothing", $"EditingElement type is {e.EditingElement.GetType().Name}")
        '        MessageBox.Show($"DEBUG EXIT: Failed to cast EditingElement to TextBox. {elementInfo}", "Debug Exit", MessageBoxButton.OK, MessageBoxImage.Error)
        '        Return ' Exit because the control used for editing isn't a TextBox
        '    End If

        '    MessageBox.Show("DEBUG: Initial checks passed (Column, Row Item, Edit Control). Proceeding to validation.", "Debug Progress", MessageBoxButton.OK, MessageBoxImage.Information)

        '    Dim newValueString As String = editingTextBox.Text
        '    Dim newQuantity As Integer

        '    If Not Integer.TryParse(newValueString, newQuantity) OrElse newQuantity < 0 Then
        '        MessageBox.Show($"DEBUG EXIT: Invalid quantity input ('{newValueString}'). Cancelling edit.", "Debug Validation", MessageBoxButton.OK, MessageBoxImage.Warning)
        '        e.Cancel = True
        '        editingTextBox.Text = editedItem.Quantity.ToString() ' Attempt to restore visually
        '        Return
        '    End If

        '    MessageBox.Show($"DEBUG: Input '{newValueString}' parsed as valid quantity {newQuantity}.", "Debug Validation", MessageBoxButton.OK, MessageBoxImage.Information)

        '    ' --- Start XML Update Logic with Try...Catch ---
        '    Dim xmlWasModified As Boolean = False
        '    Dim exceptionMessage As String = Nothing ' To store potential error message
        '    Dim targetFeatElement As XElement = Nothing ' Declare here to use in Tuple later

        '    Try
        '        If xmlDoc Is Nothing Then Throw New Exception("xmlDoc is Nothing")
        '        Dim selectedContainer = TryCast(cmbStorageContainers.SelectedItem, StorageContainer)
        '        If selectedContainer Is Nothing Then Throw New Exception("selectedContainer is Nothing")
        '        If selectedContainer.FeatElement Is Nothing Then Throw New Exception("selectedContainer.FeatElement is Nothing")

        '        targetFeatElement = selectedContainer.FeatElement ' Assign within Try block
        '        MessageBox.Show($"DEBUG: Trying to find <inv> within <feat> (Parent objId='{selectedContainer.ParentObjId}', Parent entId='{If(selectedContainer.ParentEntId.HasValue, selectedContainer.ParentEntId.Value.ToString(), "N/A")}')", "Debug Progress", MessageBoxButton.OK, MessageBoxImage.Information)
        '        Dim targetInv = targetFeatElement.Descendants("inv").FirstOrDefault()

        '        If targetInv Is Nothing Then Throw New Exception("Cannot find <inv> node within the selected container's <feat> element.")

        '        MessageBox.Show($"DEBUG: Found <inv> node. Trying to find <s> element with elementaryId='{editedItem.ElementId}'.", "Debug Progress", MessageBoxButton.OK, MessageBoxImage.Information)
        '        Dim itemElementToUpdate = targetInv.Elements("s").FirstOrDefault(Function(s) s.Attribute("elementaryId")?.Value = editedItem.ElementId.ToString())

        '        If itemElementToUpdate Is Nothing Then MessageBox.Show($"DEBUG: <s> element for Item ID {editedItem.ElementId} was NOT FOUND within the <inv> node.", "Debug XML Info", MessageBoxButton.OK, MessageBoxImage.Warning) Else MessageBox.Show($"DEBUG: Found existing <s> element for Item ID {editedItem.ElementId}.", "Debug XML Info", MessageBoxButton.OK, MessageBoxImage.Information)

        '        ' --- Perform XML Update ---
        '        If itemElementToUpdate Is Nothing AndAlso newQuantity > 0 Then
        '            MessageBox.Show($"DEBUG INFO: Item ID {editedItem.ElementId} not in XML, creating new <s>.", "Debug XML Action", MessageBoxButton.OK, MessageBoxImage.Information)
        '            itemElementToUpdate = New XElement("s", New XAttribute("elementaryId", editedItem.ElementId.ToString()), New XAttribute("inStorage", newQuantity.ToString()), New XAttribute("onTheWayIn", "0"), New XAttribute("onTheWayOut", "0"))
        '            targetInv.Add(itemElementToUpdate) : xmlWasModified = True
        '        ElseIf itemElementToUpdate IsNot Nothing Then
        '            If newQuantity > 0 Then
        '                Dim currentQtyXml = itemElementToUpdate.Attribute("inStorage")?.Value
        '                If currentQtyXml <> newQuantity.ToString() Then
        '                    MessageBox.Show($"DEBUG INFO: Updating XML 'inStorage' for Item ID {editedItem.ElementId} from '{currentQtyXml}' to '{newQuantity}'.", "Debug XML Action", MessageBoxButton.OK, MessageBoxImage.Information)
        '                    itemElementToUpdate.SetAttributeValue("inStorage", newQuantity.ToString()) : xmlWasModified = True
        '                Else
        '                    MessageBox.Show($"DEBUG INFO: XML 'inStorage' already matched {newQuantity}. No XML change.", "Debug XML Action", MessageBoxButton.OK, MessageBoxImage.Information)
        '                    xmlWasModified = False
        '                End If
        '            Else ' newQuantity is 0
        '                MessageBox.Show($"DEBUG INFO: Removing <s> element for Item ID {editedItem.ElementId} (quantity is 0).", "Debug XML Action", MessageBoxButton.OK, MessageBoxImage.Information)
        '                itemElementToUpdate.Remove() : xmlWasModified = True
        '            End If
        '        Else ' itemElement is Nothing and newQuantity is 0
        '            MessageBox.Show($"DEBUG INFO: Item ID {editedItem.ElementId} not in XML and new quantity is 0. No XML change.", "Debug XML Action", MessageBoxButton.OK, MessageBoxImage.Information)
        '            xmlWasModified = False
        '        End If

        '    Catch ex As Exception
        '        exceptionMessage = $"DEBUG EXCEPTION during XML Update: {ex.Message}"
        '        e.Cancel = True ' Cancel edit on error
        '    End Try

        '    ' --- Handle results ---
        '    If exceptionMessage IsNot Nothing Then
        '        MessageBox.Show(exceptionMessage, "Debug Error", MessageBoxButton.OK, MessageBoxImage.Error)
        '        TryCast(e.EditingElement, TextBox).Text = editedItem.Quantity.ToString() ' Attempt to restore visually
        '        Return ' Exit handler on error
        '    End If

        '    ' --- Update In-Memory Data (Only if XML update was successful or not needed) ---
        '    MessageBox.Show("DEBUG: Starting In-Memory Update.", "Debug Progress", MessageBoxButton.OK, MessageBoxImage.Information)
        '    Dim itemInMemoryList = selectedContainer.Items.FirstOrDefault(Function(i) i.ElementId = editedItem.ElementId)

        '    ' --- CORRECTED In-Memory Update Logic ---
        '    If newQuantity > 0 Then
        '        ' Always update the Quantity property of the item object from the grid event
        '        If editedItem IsNot Nothing Then
        '            editedItem.Quantity = newQuantity
        '        End If

        '        ' Check if the item exists in the underlying list (selectedContainer.Items)
        '        If itemInMemoryList IsNot Nothing Then
        '            ' It exists, just update its quantity
        '            itemInMemoryList.Quantity = newQuantity
        '        Else
        '            ' It doesn't exist in the list. This happens if it was just re-added to the XML.
        '            ' Add the editedItem (which now has the correct quantity) to the list.
        '            ' Make sure editedItem is valid before adding.
        '            If editedItem IsNot Nothing AndAlso xmlWasModified Then
        '                ' Check if really not in list before adding to avoid potential duplicates
        '                If Not selectedContainer.Items.Any(Function(i) i.ElementId = editedItem.ElementId) Then
        '                    selectedContainer.Items.Add(editedItem)
        '                    ' Also add to the ObservableCollection bound to the grid
        '                    If CurrentContainerItems IsNot Nothing AndAlso Not CurrentContainerItems.Any(Function(i) i.ElementId = editedItem.ElementId) Then
        '                        CurrentContainerItems.Add(editedItem)
        '                        ' Note: Adding might disrupt sorting. Re-sorting/refreshing might be needed here if order is critical.
        '                    End If
        '                End If
        '            End If
        '        End If
        '    Else ' newQuantity is 0
        '        ' Remove the item from the underlying list if it exists
        '        If itemInMemoryList IsNot Nothing Then
        '            selectedContainer.Items.Remove(itemInMemoryList)
        '        End If
        '        ' Remove the item from the ObservableCollection (bound to grid) if it exists
        '        If CurrentContainerItems IsNot Nothing AndAlso editedItem IsNot Nothing Then
        '            Dim itemInObservable = CurrentContainerItems.FirstOrDefault(Function(i) i.ElementId = editedItem.ElementId)
        '            If itemInObservable IsNot Nothing Then
        '                CurrentContainerItems.Remove(itemInObservable) ' This updates the grid visually
        '            End If
        '        End If
        '    End If
        '    ' --- End Corrected Block ---

        '    MessageBox.Show("DEBUG: Finished In-Memory Update.", "Debug Progress", MessageBoxButton.OK, MessageBoxImage.Information)

        '    ' --- Final Messages & Storing Debug Info ---
        '    Dim currentItemName = If(editedItem?.Name, "Unknown Item")
        '    MessageBox.Show($"Quantity for {currentItemName} updated to {newQuantity} (in memory). Save file to persist.", "Quantity Updated", MessageBoxButton.OK, MessageBoxImage.Information)

        '    If xmlWasModified AndAlso targetFeatElement IsNot Nothing Then ' Check targetFeatElement isn't nothing
        '        lastEditedStorageItemDetails = Tuple.Create(targetFeatElement, editedItem.ElementId.ToString(), newQuantity.ToString())
        '        MessageBox.Show($"DEBUG: Set lastEditedStorageItemDetails for Item {editedItem.ElementId}, New Qty {newQuantity}", "CellEditEnding Debug", MessageBoxButton.OK, MessageBoxImage.Asterisk)
        '    Else
        '        lastEditedStorageItemDetails = Nothing
        '        MessageBox.Show($"DEBUG: XML Was NOT Modified or Error occurred. lastEditedStorageItemDetails NOT set.", "CellEditEnding Debug", MessageBoxButton.OK, MessageBoxImage.Information)
        '    End If

        '    MessageBox.Show("DEBUG: dgvStorage_CellEditEnding END", "Debug", MessageBoxButton.OK, MessageBoxImage.Information)

        'End Sub


        ' --- ADDED: Handler for Create New Crew Button ---

        Private Sub btnAddNewCrew_Click(sender As Object, e As RoutedEventArgs)
            ' Ensure a ship is selected and XML is loaded
            If cmb_ships.SelectedItem Is Nothing OrElse TryCast(cmb_ships.SelectedItem, Ship)?.Sid = -1 Then
                MessageBox.Show("Please select a ship first before adding crew.", "No Ship Selected", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If
            If xmlDoc Is Nothing OrElse xmlDoc.Root Is Nothing Then
                MessageBox.Show("Please load a save file first.", "No File Loaded", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If

            Dim selectedShip = TryCast(cmb_ships.SelectedItem, Ship)
            Dim shipElement = xmlDoc.Descendants("ship").FirstOrDefault(Function(s) s.Attribute("sid")?.Value = selectedShip.Sid.ToString())
            Dim charactersNode = shipElement?.Element("characters")

            If shipElement Is Nothing OrElse charactersNode Is Nothing Then
                MessageBox.Show($"Could not find ship (SID:{selectedShip.Sid}) or its <characters> node in the XML.", "XML Error", MessageBoxButton.OK, MessageBoxImage.Error)
                Return
            End If

            ' --- Find a template character ---
            Dim templateCharacterNode = charactersNode.Elements("c").FirstOrDefault()
            If templateCharacterNode Is Nothing Then
                MessageBox.Show($"The selected ship (SID:{selectedShip.Sid}) has no existing crew members to use as a template.", "Cannot Create Crew", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If

            ' --- Show New Crew Window ---
            Dim newCrewWin As New NewCrewWindow() With {.Owner = Me}
            Dim result = newCrewWin.ShowDialog()

            If result.HasValue AndAlso result.Value = True Then ' User clicked Create
                ' --- Get Data From Window ---
                Dim newName = newCrewWin.NewCrewName
                Dim newAttributes = newCrewWin.Attributes
                Dim newSkills = newCrewWin.Skills
                Dim newTraits = newCrewWin.Traits

                Try
                    ' --- Get and Increment ID Counter ---
                    Dim idCounterAttr = xmlDoc.Root.Attribute("idCounter")
                    If idCounterAttr Is Nothing Then Throw New Exception("Cannot find 'idCounter' attribute on root <game> element.")
                    Dim nextId As Long = 0
                    If Not Long.TryParse(idCounterAttr.Value, nextId) Then Throw New Exception($"Cannot parse 'idCounter' value '{idCounterAttr.Value}'.")
                    nextId += 1
                    Dim newEntIdStr = nextId.ToString()
                    idCounterAttr.Value = newEntIdStr
                    MessageBox.Show($"Assigned new Entity ID: {newEntIdStr}", "Info", MessageBoxButton.OK, MessageBoxImage.Information)

                    ' --- Clone Template and Modify ---
                    Dim newCharacterNode As New XElement(templateCharacterNode) ' Creates a deep copy

                    ' Update Basic Info
                    newCharacterNode.SetAttributeValue("name", newName)
                    newCharacterNode.SetAttributeValue("entId", newEntIdStr)
                    newCharacterNode.Element("state")?.SetAttributeValue("bedLink", Nothing) ' Clear inherited bed link

                    ' Update Stats within <pers>
                    Dim persNode = newCharacterNode.Element("pers")
                    If persNode IsNot Nothing Then
                        ' Attributes
                        Dim attrNode = persNode.Element("attr")
                        If attrNode IsNot Nothing Then
                            ' --- Corrected Attribute Loop ---
                            For Each newAttr In newAttributes
                                Dim existingA = attrNode.Elements("a").FirstOrDefault(Function(a) a.Attribute("id")?.Value = newAttr.Id.ToString())
                                If existingA IsNot Nothing Then
                                    existingA.SetAttributeValue("points", newAttr.Value.ToString())
                                End If ' <<< Added End If
                            Next ' <<< Next for attributes
                            ' --- End Correction ---
                        End If

                        ' Skills
                        Dim skillsNode = persNode.Element("skills")
                        If skillsNode IsNot Nothing Then
                            ' --- Corrected Skill Loop ---
                            For Each newSkill In newSkills
                                Dim existingS = skillsNode.Elements("s").FirstOrDefault(Function(s) s.Attribute("sk")?.Value = newSkill.Id.ToString())
                                If existingS IsNot Nothing Then
                                    existingS.SetAttributeValue("level", newSkill.Value.ToString())
                                    existingS.SetAttributeValue("mxn", newSkill.Value.ToString()) ' Set max known too
                                End If ' <<< Added End If
                            Next ' <<< Next for skills
                            ' --- End Correction ---
                        End If

                        ' Traits
                        Dim traitsNode = persNode.Element("traits")
                        If traitsNode IsNot Nothing Then
                            traitsNode.RemoveNodes() ' Clear existing template traits
                            For Each newTrait In newTraits
                                traitsNode.Add(New XElement("t", New XAttribute("id", newTrait.Id.ToString())))
                            Next ' <<< Next for traits
                        End If

                        ' Clear Conditions and Relationships
                        Dim conditionsNode = persNode.Element("conditions")
                        If conditionsNode IsNot Nothing Then conditionsNode.RemoveNodes()

                        Dim socialityNode = persNode.Element("sociality")
                        If socialityNode IsNot Nothing Then
                            Dim relationshipsNode = socialityNode.Element("relationships")
                            If relationshipsNode IsNot Nothing Then relationshipsNode.RemoveNodes()
                        End If

                    Else
                        MessageBox.Show("Warning: Template <pers> node missing.", "XML Warning", MessageBoxButton.OK, MessageBoxImage.Warning)
                    End If

                    ' Add New Character Node to XML Characters Node
                    charactersNode.Add(newCharacterNode)

                    ' Create and Add Character Object to In-Memory List
                    Dim newCharacter As New Character With {
                    .CharacterName = newName, .CharacterEntityId = CInt(newEntIdStr), .ShipSid = selectedShip.Sid,
                    .CharacterAttributes = newAttributes.Select(Function(dp) New DataProp With {.Id = dp.Id, .Name = dp.Name, .Value = dp.Value}).ToList(),
                    .CharacterSkills = newSkills.Select(Function(dp) New DataProp With {.Id = dp.Id, .Name = dp.Name, .Value = dp.Value}).ToList(),
                    .CharacterTraits = newTraits.Select(Function(dp) New DataProp With {.Id = dp.Id, .Name = dp.Name}).ToList(),
                    .CharacterConditions = New List(Of DataProp)() ' Start with empty conditions list
                }
                    characters.Add(newCharacter)

                    ' Refresh UI
                    LoadCrewForShip(selectedShip.Sid)
                    MessageBox.Show($"Crew member '{newName}' added (in memory). Save file.", "Crew Added", MessageBoxButton.OK, MessageBoxImage.Information)

                Catch ex As Exception
                    MessageBox.Show($"Error creating crew: {ex.Message}", "Creation Error", MessageBoxButton.OK, MessageBoxImage.Error)
                End Try
            End If ' End If DialogResult = True

        End Sub

        Private Sub btnDeleteCondition_Click(sender As Object, e As RoutedEventArgs)
            ' Get selected character and selected condition from UI
            Dim selectedCharacter = TryCast(lstCharacters.SelectedItem, Character)
            Dim selectedCondition = TryCast(lstConditions.SelectedItem, DataProp)

            If selectedCharacter Is Nothing Then
                MessageBox.Show("Please select a character first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If
            If selectedCondition Is Nothing Then
                MessageBox.Show("Please select a condition from the list to remove.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If
            If xmlDoc Is Nothing Then
                MessageBox.Show("Save file not loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                Return
            End If

            ' Confirmation
            Dim confirmResult = MessageBox.Show($"Are you sure you want to remove the condition '{selectedCondition.Name}' from {selectedCharacter.CharacterName}?",
                                                 "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Question)
            If confirmResult <> MessageBoxResult.Yes Then Return

            Try
                ' Find the character's node in XML
                Dim characterNode = xmlDoc.Descendants("c").FirstOrDefault(Function(c) c.Attribute("entId")?.Value = selectedCharacter.CharacterEntityId.ToString())
                If characterNode Is Nothing Then Throw New Exception($"Character node with entId {selectedCharacter.CharacterEntityId} not found.")

                ' Find the conditions node
                Dim conditionsNode = characterNode.Element("pers")?.Element("conditions")
                If conditionsNode Is Nothing Then Throw New Exception("<pers><conditions> node not found for character.")

                ' Find the specific condition element to remove by ID
                Dim conditionElementToRemove = conditionsNode.Elements("c").FirstOrDefault(Function(c) c.Attribute("id")?.Value = selectedCondition.Id.ToString())

                If conditionElementToRemove IsNot Nothing Then
                    ' Remove from XML
                    conditionElementToRemove.Remove()

                    ' Remove from in-memory list
                    Dim conditionInMemory = selectedCharacter.CharacterConditions.FirstOrDefault(Function(c) c.Id = selectedCondition.Id)
                    If conditionInMemory IsNot Nothing Then
                        selectedCharacter.CharacterConditions.Remove(conditionInMemory)
                    End If

                    ' Refresh ListBox
                    lstConditions.ItemsSource = Nothing
                    lstConditions.ItemsSource = New ObservableCollection(Of DataProp)(selectedCharacter.CharacterConditions) ' Rebind to update UI

                    MessageBox.Show($"Condition '{selectedCondition.Name}' removed (in memory). Save file.", "Condition Removed", MessageBoxButton.OK, MessageBoxImage.Information)
                Else
                    MessageBox.Show($"Condition '{selectedCondition.Name}' (ID: {selectedCondition.Id}) not found in the character's XML <conditions> node. It might have already been removed.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning)
                    ' Optionally refresh list anyway in case memory was out of sync
                    lstConditions.ItemsSource = Nothing
                    lstConditions.ItemsSource = New ObservableCollection(Of DataProp)(selectedCharacter.CharacterConditions)
                End If

            Catch ex As Exception
                MessageBox.Show($"Error removing condition: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End Sub

        Private Sub dgvRelationships_RowEditEnding(sender As Object, e As DataGridRowEditEndingEventArgs) Handles dgvRelationships.RowEditEnding
            If e.EditAction <> DataGridEditAction.Commit Then Exit Sub

            ' Use Dispatcher to ensure data object is updated by binding first
            Dispatcher.BeginInvoke(New Action(Sub()
                                                  Dim editedRel As RelationshipInfo = TryCast(e.Row.Item, RelationshipInfo)
                                                  If editedRel Is Nothing Then Exit Sub

                                                  Dim currentCharacter As Character = TryCast(lstCharacters.SelectedItem, Character)
                                                  If currentCharacter Is Nothing OrElse xmlDoc Is Nothing Then Exit Sub ' Need context

                                                  ' Optional: Validate new values (e.g., ensure they are within -100 to 100 or other game limits)
                                                  ' Example:
                                                  ' If editedRel.Friendship < -100 OrElse editedRel.Friendship > 100 Then ' Adjust range as needed
                                                  '      MessageBox.Show("Friendship value must be between -100 and 100.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning)
                                                  '      ' TODO: Revert Change - This is tricky without full MVVM/Undo
                                                  '      Exit Sub
                                                  ' End If
                                                  ' (Add similar validation for Attraction, Compatibility if needed)

                                                  ' Find the XML nodes
                                                  Dim characterNode = xmlDoc.Descendants("c").FirstOrDefault(Function(c) c.Attribute("entId")?.Value = currentCharacter.CharacterEntityId.ToString())
                                                  Dim relationshipsNode = characterNode?.Element("pers")?.Element("sociality")?.Element("relationships")

                                                  If relationshipsNode Is Nothing Then
                                                      MessageBox.Show("Could not find <relationships> node in XML.", "XML Error", MessageBoxButton.OK, MessageBoxImage.Error)
                                                      Exit Sub
                                                  End If

                                                  ' Find the specific relationship <l> element
                                                  Dim relElement As XElement = relationshipsNode.Elements("l").FirstOrDefault(Function(l) l.Attribute("targetId")?.Value = editedRel.TargetId.ToString())

                                                  If relElement Is Nothing Then
                                                      MessageBox.Show($"Could not find relationship XML node for target ID {editedRel.TargetId}.", "XML Error", MessageBoxButton.OK, MessageBoxImage.Error)
                                                      Exit Sub
                                                  End If

                                                  ' Update attributes in the in-memory XML
                                                  Try
                                                      relElement.SetAttributeValue("friendship", editedRel.Friendship.ToString())
                                                      relElement.SetAttributeValue("attraction", editedRel.Attraction.ToString())
                                                      relElement.SetAttributeValue("compatibility", editedRel.Compatibility.ToString())

                                                      MessageBox.Show($"Relationship with {editedRel.TargetName} updated (in memory). Save file.", "Relationship Updated", MessageBoxButton.OK, MessageBoxImage.Information)

                                                  Catch ex As Exception
                                                      MessageBox.Show($"Error updating relationship XML: {ex.Message}", "XML Update Error", MessageBoxButton.OK, MessageBoxImage.Error)
                                                  End Try

                                              End Sub), DispatcherPriority.Background)
        End Sub

        Private Sub AboutMenu_Click(sender As Object, e As RoutedEventArgs)
            ' Create an instance of the new About window
            Dim aboutWin As New AboutWindow()

            ' Set the owner to the main window (so it centers correctly)
            aboutWin.Owner = Me

            ' Show the window as a modal dialog (blocks interaction with main window)
            aboutWin.ShowDialog()
        End Sub
    End Class ' End of SpaceHavenEditor Class

End Namespace