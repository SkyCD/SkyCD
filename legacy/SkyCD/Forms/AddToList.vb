Namespace Forms

Public Class AddToList

    'Private WithEvents Tikrink As New Timers.Timer(50)
    Private Drives() As String
    Private Settings As New SCD_XSettings(My.Application.Info.AssemblyName)

    Private Sub tsbFromCDROM_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles tsbFromCDROM.Click
        Me.tsbFromFolder.Checked = False
        Me.tsbFromInternet.Checked = False
        Me.tsbFromCDROM.Checked = True
        Me.pnlFromFolder.Visible = False
        Me.pnlFromMedia.Visible = True
        Me.pnlFromInternet.Visible = False
        Me.OK_Button.Enabled = Me.cmbDrives.Enabled
    End Sub

    Private Sub tsbFromFolder_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles tsbFromFolder.Click
        Me.tsbFromFolder.Checked = True
        Me.tsbFromInternet.Checked = False
        Me.tsbFromCDROM.Checked = False
        Me.pnlFromFolder.Visible = True
        Me.pnlFromMedia.Visible = False
        Me.pnlFromInternet.Visible = False
        Me.OK_Button.Enabled = Me.txtSelexc.TextLength > 0
    End Sub

    Private Sub tsbFromInternet_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles tsbFromInternet.Click
        Me.tsbFromFolder.Checked = False
        Me.tsbFromInternet.Checked = True
        Me.tsbFromCDROM.Checked = False
        Me.pnlFromFolder.Visible = False
        Me.pnlFromMedia.Visible = False
        Me.pnlFromInternet.Visible = True
        Me.OK_Button.Enabled = Me.txtEnterInternetAdress.TextLength > 0
    End Sub

    Public Sub New()

        ' This call is required by the Windows Form Designer.
        InitializeComponent()

        ' Add any initialization after the InitializeComponent() call.
        Me.UpdateDrivers()
        If Me.cmbDrives.Items.Count < 1 Then
            'Me.cmbDrives.SelectedIndex = 0
            Me.cmbDrives.Enabled = False
        Else
            Me.cmbDrives.Enabled = True
        End If

        Me.Text = Translate(Me, "Add To List")
        Me.Label1.Text = Translate(Me, "Select Folder:")
        Me.chkIncludeSubFolders.Text = Translate(Me, "Include Subfolders")
        Me.lblEnterInternetAdress.Text = Translate(Me, "Enter Internet address:")
        Me.lblSelectDrive.Text = Translate(Me, "Select drive:")
        Me.chkIncludeMediaInfo.Text = Translate(Me, "Include Media Info")
        Me.lblMediaName.Text = Translate(Me, "Select full name for selected media:")
        Me.rbAllContentAddToSelectedMediaFolder.Text = Translate(Me, "To selected Media/Folder")
        Me.rbAllContentAddAsNewMedia.Text = Translate(Me, "As new Media")
        Me.gbWhereToAdd.Text = Translate(Me, "All contents add...")
        Me.chkIncludeExtendedInfo.Text = Translate(Me, "Include Extended Info")
        Me.GroupBox1.Text = Translate(Me, "Misc")
        Me.Cancel_Button.Text = Translate(Me, "Cancel")
        Me.OK_Button.Text = Translate(Me, "OK")
        Me.tsbFromInternet.Text = Translate(Me, "From Internet")
        Me.tsbFromCDROM.Text = Translate(Me, "From Media")
        Me.tsbFromFolder.Text = Translate(Me, "From Folder")

        If Me.tsbFromCDROM.Checked Then Me.OK_Button.Enabled = Me.cmbDrives.Enabled
    End Sub

    Public Sub UpdateDrivers()
        Dim DrvNfo As System.IO.DriveInfo
        Drives = System.IO.Directory.GetLogicalDrives()
        Dim I As Integer = 0
        Me.Tikrink.Enabled = False
        For Each This As String In Drives
            DrvNfo = New System.IO.DriveInfo(This)
            If DrvNfo.DriveType <> IO.DriveType.Fixed And DrvNfo.DriveType <> IO.DriveType.Network And DrvNfo.DriveType <> IO.DriveType.NoRootDirectory And DrvNfo.DriveType <> IO.DriveType.Ram Then
                I = I + 1
            End If
        Next
        I = 0
        For Each This As String In Drives
            DrvNfo = New System.IO.DriveInfo(This)
            If DrvNfo.DriveType <> IO.DriveType.Fixed And DrvNfo.DriveType <> IO.DriveType.Network And DrvNfo.DriveType <> IO.DriveType.NoRootDirectory And DrvNfo.DriveType <> IO.DriveType.Ram Then
                If DrvNfo.IsReady Then
                    Me.cmbDrives.Items.Add(DrvNfo.Name.Substring(0, 2) & " [" & DrvNfo.VolumeLabel & "]")
                    'Me.cmbDrives.Items.Add(DrvNfo.Nam)
                End If
                I = I + 1
            End If
        Next
        Me.Tikrink.Enabled = True
    End Sub

    Private Sub UpdateDrivesList()
        Dim k As Integer = Me.cmbDrives.SelectedIndex
        Dim txt As String = Me.cmbDrives.SelectedText
        Me.cmbDrives.Items.Clear()
        Me.UpdateDrivers()
        If Me.cmbDrives.Items.Count > 0 Then
            If Me.cmbDrives.Items.Contains(txt) Then
                Me.cmbDrives.SelectedIndex = Me.cmbDrives.Items.IndexOf(txt)
            Else
                If k > Me.cmbDrives.Items.Count - 1 Then k = k - 1
                'Me.cmbDrives.SelectedIndex = k
            End If
            Me.cmbDrives.Enabled = True
        Else
            Me.cmbDrives.Enabled = False
        End If
        If Me.tsbFromCDROM.Checked Then Me.OK_Button.Enabled = Me.cmbDrives.Enabled
    End Sub

    Private Sub Tikrink_Tick(ByVal sender As Object, ByVal e As System.EventArgs) Handles Tikrink.Tick
        Static I As Integer = 0
        If Me.cmbDrives.Items.Count > 0 Then
            If Me.cmbDrives.Text.Length > 0 Then Me.cmbDrives.SelectedIndex = 0
        End If
        Dim drvs = System.IO.Directory.GetLogicalDrives()
        If Me.Drives.Length <> drvs.Length Then
            Me.UpdateDrivesList()
            Exit Sub
        End If
        If I > Me.Drives.Length - 1 Then I = 0
        Dim DrvNfo As New System.IO.DriveInfo(Me.Drives(I))
        If DrvNfo.DriveType = IO.DriveType.CDRom Then
            If DrvNfo.IsReady Then
                'MsgBox(Me.Drives(I))
                If Me.cmbDrives.Items.Contains(DrvNfo.Name.Substring(0, 2) & " [" & DrvNfo.VolumeLabel & "]") = False Then
                    I = 0
                    Me.UpdateDrivesList()
                End If
            End If
        End If
        I = I + 1        
    End Sub

    Private Sub frmAddToList_Load(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles MyBase.Load
            Select Case CInt(Me.Settings.ReadSetting("Selected.Tab", "AddToListForm", 0))
                Case 2
                    Me.tsbFromFolder.Checked = False
                    Me.tsbFromInternet.Checked = True
                    Me.tsbFromCDROM.Checked = False
                    Me.pnlFromFolder.Visible = False
                    Me.pnlFromMedia.Visible = False
                    Me.pnlFromInternet.Visible = True
                Case 1
                    Me.tsbFromFolder.Checked = True
                    Me.tsbFromInternet.Checked = False
                    Me.tsbFromCDROM.Checked = False
                    Me.pnlFromFolder.Visible = True
                    Me.pnlFromMedia.Visible = False
                    Me.pnlFromInternet.Visible = False
                Case Else
                    Me.tsbFromFolder.Checked = False
                    Me.tsbFromInternet.Checked = False
                    Me.tsbFromCDROM.Checked = True
                    Me.pnlFromFolder.Visible = False
                    Me.pnlFromMedia.Visible = True
                    Me.pnlFromInternet.Visible = False
            End Select
            Me.txtEnterInternetAdress.Text = Me.Settings.ReadSetting("LastInternetAdress", "AddToListForm").ToString
            Me.txtMediaName.Text = Me.Settings.ReadSetting("LastMediaName", "AddToListForm").ToString
            Me.txtSelexc.Text = Me.Settings.ReadSetting("LastFolder", "AddToListForm").ToString
            Me.txtEnterInternetAdress.SelectionStart = Me.txtEnterInternetAdress.Text.Length
        Me.txtMediaName.SelectionStart = Me.txtMediaName.Text.Length
        Me.txtSelexc.SelectionStart = Me.txtSelexc.Text.Length
            Me.chkIncludeExtendedInfo.Checked = Convert.ToBoolean(Settings.ReadSetting("IncludeExtendedInfo", "AddToListForm", True))
            Me.chkIncludeMediaInfo.Checked = Convert.ToBoolean(Me.Settings.ReadSetting("IncludeMediaInfo", "AddToListForm", True))
            Me.chkIncludeSubFolders.Checked = Convert.ToBoolean(Me.Settings.ReadSetting("IncludeSubFolders", "AddToListForm", True))
            Me.rbAllContentAddAsNewMedia.Checked = Convert.ToBoolean(Me.Settings.ReadSetting("AllContentAddAsNewMedia", "AddToListForm", True))
            Me.rbAllContentAddToSelectedMediaFolder.Checked = Convert.ToBoolean(Me.Settings.ReadSetting("AllContentAddToSelectedMediaFolder", "AddToListForm", False))
            Me.cmbDrives.SelectedText = Me.Settings.ReadSetting("Selected Drive", "AddToListForm", Me.cmbDrives.Text).ToString
        End Sub

    Private Sub cmdSelectFolder_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cmdSelectFolder.Click
        Dim ms As New FolderBrowserDialog()
        ms.SelectedPath = Me.txtSelexc.Text
        ms.ShowNewFolderButton = False
        ms.Description = "Select folder wich one you want to add to Your list."
        If ms.ShowDialog = Windows.Forms.DialogResult.Cancel Then Exit Sub
        Me.txtSelexc.Text = ms.SelectedPath
    End Sub

    Private Sub Cancel_Button_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Cancel_Button.Click
        Me.Dispose()
    End Sub

    Private Sub OK_Button_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles OK_Button.Click
        Dim k As String = "AddToListForm"
        With Me.Settings
            .WriteSetting(k, "LastInternetAdress", Me.txtEnterInternetAdress.Text)
            .WriteSetting(k, "LastMediaName", Me.txtMediaName.Text)
            .WriteSetting(k, "LastFolder", Me.txtSelexc.Text)
            .WriteSetting(k, "IncludeExtendedInfo", Me.chkIncludeExtendedInfo.Checked)
            .WriteSetting(k, "IncludeMediaInfo", Me.chkIncludeMediaInfo.Checked)
            .WriteSetting(k, "IncludeSubFolders", Me.chkIncludeSubFolders.Checked)
            .WriteSetting(k, "AllContentAddAsNewMedia", Me.rbAllContentAddAsNewMedia.Checked)
            .WriteSetting(k, "AllContentAddToSelectedMediaFolder", Me.rbAllContentAddToSelectedMediaFolder.Checked)
            If Me.tsbFromFolder.Checked Then .WriteSetting(k, "Selected.Tab", 1)
            If Me.tsbFromInternet.Checked Then .WriteSetting(k, "Selected.Tab", 2)
            If Me.tsbFromCDROM.Checked Then .WriteSetting(k, "Selected.Tab", 0)
            .WriteSetting(k, "Selected.Drive", Me.cmbDrives.Text)
        End With
    End Sub

    Private Sub cmbDrives_SelectedIndexChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cmbDrives.SelectedIndexChanged
        Dim DRV As New System.IO.DriveInfo(cmbDrives.Text.Substring(0, 2))
        Me.txtMediaName.Text = DRV.VolumeLabel
        Me.OK_Button.Enabled = Me.txtMediaName.Text.Length > 0
    End Sub

    Private Sub txtSelexc_TextChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles txtSelexc.TextChanged
        Dim I As Integer = InStrRev(Me.txtSelexc.Text, "\")
        If I < 0 Then Exit Sub
        Me.txtMediaName.Text = Me.txtSelexc.Text.Substring(I)
        Me.OK_Button.Enabled = (Me.txtSelexc.TextLength > 0) And (Me.txtMediaName.Text.Length > 0)
    End Sub

    Private Sub pnlFromMedia_Paint(ByVal sender As System.Object, ByVal e As System.Windows.Forms.PaintEventArgs) Handles pnlFromMedia.Paint

    End Sub

    Private Sub txtMediaName_TextChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles txtMediaName.TextChanged
        If Me.tsbFromInternet.Checked Then
            Me.OK_Button.Enabled = (Me.txtEnterInternetAdress.Text.Length > 0) And (Me.txtMediaName.Text.Length > 0)
        ElseIf Me.tsbFromFolder.Checked Then
            Me.OK_Button.Enabled = (Me.txtSelexc.TextLength > 0) And (Me.txtMediaName.Text.Length > 0)
        Else
            Me.OK_Button.Enabled = Me.txtMediaName.Text.Length > 0
        End If
    End Sub

    Private Sub txtEnterInternetAdress_TextChanged(ByVal sender As Object, ByVal e As System.EventArgs) Handles txtEnterInternetAdress.TextChanged
        Me.OK_Button.Enabled = (Me.txtEnterInternetAdress.Text.Length > 0) And (Me.txtMediaName.Text.Length > 0)
    End Sub
End Class


End namespace