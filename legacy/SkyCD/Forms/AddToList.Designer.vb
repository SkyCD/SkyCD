Namespace Forms

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Public Class AddToList
        Inherits Form

#Const design = True

        'Form overrides dispose to clean up the component list.
        <System.Diagnostics.DebuggerNonUserCode()>
        Protected Overloads Overrides Sub Dispose(ByVal disposing As Boolean)
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub

        'Required by the Windows Form Designer
        Private components As System.ComponentModel.IContainer

        'NOTE: The following procedure is required by the Windows Form Designer
        'It can be modified using the Windows Form Designer.  
        'Do not modify it using the code editor.
        <System.Diagnostics.DebuggerStepThrough()>
        Private Sub InitializeComponent()
            Me.components = New System.ComponentModel.Container
            Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(AddToList))
            Me.Tikrink = New Timer(Me.components)
            Me.tcControl = New TabControl
            Me.gbWhereToAdd = New TabPage
            Me.rbAllContentAddToSelectedMediaFolder = New RadioButton
            Me.rbAllContentAddAsNewMedia = New RadioButton
            Me.GroupBox1 = New TabPage
            Me.chkIncludeExtendedInfo = New CheckBox
            Me.Panel3 = New Panel
            Me.pnlFromMedia = New Panel
            Me.chkIncludeMediaInfo = New CheckBox
            Me.cmbDrives = New ComboBox
            Me.lblSelectDrive = New Label
            Me.pnlFromInternet = New Panel
            Me.txtEnterInternetAdress = New TextBox
            Me.lblEnterInternetAdress = New Label
            Me.pnlFromFolder = New Panel
            Me.chkIncludeSubFolders = New CheckBox
            Me.cmdSelectFolder = New Button
            Me.txtSelexc = New TextBox
            Me.Label1 = New Label
            Me.Panel2 = New Panel
            Me.txtMediaName = New TextBox
            Me.lblMediaName = New Label
            Me.Panel1 = New Panel
            Me.ToolStrip1 = New ToolStrip
            Me.tsbFromCDROM = New ToolStripButton
            Me.tsbFromFolder = New ToolStripButton
            Me.tsbFromInternet = New ToolStripButton
            Me.TableLayoutPanel1 = New TableLayoutPanel
            Me.OK_Button = New Button
            Me.Cancel_Button = New Button
            Me.TableLayoutPanel2 = New TableLayoutPanel
            Me.tcControl.SuspendLayout()
            Me.gbWhereToAdd.SuspendLayout()
            Me.GroupBox1.SuspendLayout()
            Me.Panel3.SuspendLayout()
            Me.pnlFromMedia.SuspendLayout()
            Me.pnlFromInternet.SuspendLayout()
            Me.pnlFromFolder.SuspendLayout()
            Me.Panel2.SuspendLayout()
            Me.Panel1.SuspendLayout()
            Me.ToolStrip1.SuspendLayout()
            Me.TableLayoutPanel1.SuspendLayout()
            Me.TableLayoutPanel2.SuspendLayout()
            Me.SuspendLayout()
            '
            'Tikrink
            '
            '
            'tcControl
            '
            Me.tcControl.Alignment = TabAlignment.Bottom
            Me.TableLayoutPanel2.SetColumnSpan(Me.tcControl, 2)
            Me.tcControl.Controls.Add(Me.gbWhereToAdd)
            Me.tcControl.Controls.Add(Me.GroupBox1)
            Me.tcControl.Dock = DockStyle.Fill
            Me.tcControl.Location = New Point(99, 129)
            Me.tcControl.Name = "tcControl"
            Me.tcControl.SelectedIndex = 0
            Me.tcControl.Size = New Size(325, 100)
            Me.tcControl.TabIndex = 32
            '
            'gbWhereToAdd
            '
            Me.gbWhereToAdd.Controls.Add(Me.rbAllContentAddToSelectedMediaFolder)
            Me.gbWhereToAdd.Controls.Add(Me.rbAllContentAddAsNewMedia)
            Me.gbWhereToAdd.Location = New Point(4, 4)
            Me.gbWhereToAdd.Name = "gbWhereToAdd"
            Me.gbWhereToAdd.Padding = New Padding(3)
            Me.gbWhereToAdd.Size = New Size(317, 74)
            Me.gbWhereToAdd.TabIndex = 0
            Me.gbWhereToAdd.Text = "All contents add..."
            Me.gbWhereToAdd.UseVisualStyleBackColor = True
            '
            'rbAllContentAddToSelectedMediaFolder
            '
            Me.rbAllContentAddToSelectedMediaFolder.AutoSize = True
            Me.rbAllContentAddToSelectedMediaFolder.Location = New Point(9, 29)
            Me.rbAllContentAddToSelectedMediaFolder.Name = "rbAllContentAddToSelectedMediaFolder"
            Me.rbAllContentAddToSelectedMediaFolder.Size = New Size(147, 17)
            Me.rbAllContentAddToSelectedMediaFolder.TabIndex = 1
            Me.rbAllContentAddToSelectedMediaFolder.Text = "To selected Media/Folder"
            '
            'rbAllContentAddAsNewMedia
            '
            Me.rbAllContentAddAsNewMedia.AutoSize = True
            Me.rbAllContentAddAsNewMedia.Location = New Point(9, 6)
            Me.rbAllContentAddAsNewMedia.Name = "rbAllContentAddAsNewMedia"
            Me.rbAllContentAddAsNewMedia.Size = New Size(92, 17)
            Me.rbAllContentAddAsNewMedia.TabIndex = 0
            Me.rbAllContentAddAsNewMedia.Text = "As new Media"
            '
            'GroupBox1
            '
            Me.GroupBox1.Controls.Add(Me.chkIncludeExtendedInfo)
            Me.GroupBox1.Location = New Point(4, 4)
            Me.GroupBox1.Name = "GroupBox1"
            Me.GroupBox1.Padding = New Padding(3)
            Me.GroupBox1.Size = New Size(317, 74)
            Me.GroupBox1.TabIndex = 1
            Me.GroupBox1.Text = "Misc"
            Me.GroupBox1.UseVisualStyleBackColor = True
            '
            'chkIncludeExtendedInfo
            '
            Me.chkIncludeExtendedInfo.AutoSize = True
            Me.chkIncludeExtendedInfo.Location = New Point(9, 12)
            Me.chkIncludeExtendedInfo.Margin = New Padding(0)
            Me.chkIncludeExtendedInfo.Name = "chkIncludeExtendedInfo"
            Me.chkIncludeExtendedInfo.Size = New Size(130, 17)
            Me.chkIncludeExtendedInfo.TabIndex = 1
            Me.chkIncludeExtendedInfo.Text = "Include Extended Info"
            '
            'Panel3
            '
            Me.TableLayoutPanel2.SetColumnSpan(Me.Panel3, 2)
            Me.Panel3.Controls.Add(Me.pnlFromFolder)
            Me.Panel3.Controls.Add(Me.pnlFromMedia)
            Me.Panel3.Controls.Add(Me.pnlFromInternet)
            Me.Panel3.Dock = DockStyle.Fill
            Me.Panel3.Location = New Point(99, 54)
            Me.Panel3.Name = "Panel3"
            Me.Panel3.Size = New Size(325, 69)
            Me.Panel3.TabIndex = 31
            '
            'pnlFromMedia
            '
            Me.pnlFromMedia.Controls.Add(Me.chkIncludeMediaInfo)
            Me.pnlFromMedia.Controls.Add(Me.cmbDrives)
            Me.pnlFromMedia.Controls.Add(Me.lblSelectDrive)
            Me.pnlFromMedia.Dock = DockStyle.Fill
            Me.pnlFromMedia.Location = New Point(0, 0)
            Me.pnlFromMedia.Name = "pnlFromMedia"
            Me.pnlFromMedia.Size = New Size(325, 69)
            Me.pnlFromMedia.TabIndex = 27
            '
            'chkIncludeMediaInfo
            '
            Me.chkIncludeMediaInfo.AutoSize = True
            Me.chkIncludeMediaInfo.Location = New Point(13, 44)
            Me.chkIncludeMediaInfo.Name = "chkIncludeMediaInfo"
            Me.chkIncludeMediaInfo.Size = New Size(114, 17)
            Me.chkIncludeMediaInfo.TabIndex = 2
            Me.chkIncludeMediaInfo.Text = "Include Media Info"
            '
            'cmbDrives
            '
            Me.cmbDrives.DropDownStyle = ComboBoxStyle.DropDownList
            Me.cmbDrives.FormattingEnabled = True
            Me.cmbDrives.Location = New Point(13, 17)
            Me.cmbDrives.Name = "cmbDrives"
            Me.cmbDrives.Size = New Size(302, 21)
            Me.cmbDrives.TabIndex = 1
            '
            'lblSelectDrive
            '
            Me.lblSelectDrive.AutoSize = True
            Me.lblSelectDrive.Location = New Point(0, 0)
            Me.lblSelectDrive.Name = "lblSelectDrive"
            Me.lblSelectDrive.Size = New Size(66, 13)
            Me.lblSelectDrive.TabIndex = 0
            Me.lblSelectDrive.Text = "Select drive:"
            '
            'pnlFromInternet
            '
            Me.pnlFromInternet.Controls.Add(Me.txtEnterInternetAdress)
            Me.pnlFromInternet.Controls.Add(Me.lblEnterInternetAdress)
            Me.pnlFromInternet.Dock = DockStyle.Fill
            Me.pnlFromInternet.Location = New Point(0, 0)
            Me.pnlFromInternet.Name = "pnlFromInternet"
            Me.pnlFromInternet.Size = New Size(325, 69)
            Me.pnlFromInternet.TabIndex = 4
            '
            'txtEnterInternetAdress
            '
            Me.txtEnterInternetAdress.AutoCompleteMode = AutoCompleteMode.SuggestAppend
            Me.txtEnterInternetAdress.AutoCompleteSource = AutoCompleteSource.AllUrl
            Me.txtEnterInternetAdress.Location = New Point(13, 17)
            Me.txtEnterInternetAdress.Margin = New Padding(0)
            Me.txtEnterInternetAdress.Name = "txtEnterInternetAdress"
            Me.txtEnterInternetAdress.Size = New Size(301, 20)
            Me.txtEnterInternetAdress.TabIndex = 1
            '
            'lblEnterInternetAdress
            '
            Me.lblEnterInternetAdress.AutoSize = True
            Me.lblEnterInternetAdress.Location = New Point(0, 0)
            Me.lblEnterInternetAdress.Name = "lblEnterInternetAdress"
            Me.lblEnterInternetAdress.Size = New Size(114, 13)
            Me.lblEnterInternetAdress.TabIndex = 0
            Me.lblEnterInternetAdress.Text = "Enter Internet address:"
            '
            'pnlFromFolder
            '
            Me.pnlFromFolder.Controls.Add(Me.chkIncludeSubFolders)
            Me.pnlFromFolder.Controls.Add(Me.cmdSelectFolder)
            Me.pnlFromFolder.Controls.Add(Me.txtSelexc)
            Me.pnlFromFolder.Controls.Add(Me.Label1)
            Me.pnlFromFolder.Dock = DockStyle.Fill
            Me.pnlFromFolder.Location = New Point(0, 0)
            Me.pnlFromFolder.Name = "pnlFromFolder"
            Me.pnlFromFolder.Size = New Size(325, 69)
            Me.pnlFromFolder.TabIndex = 2
            '
            'chkIncludeSubFolders
            '
            Me.chkIncludeSubFolders.AutoSize = True
            Me.chkIncludeSubFolders.Location = New Point(13, 42)
            Me.chkIncludeSubFolders.Name = "chkIncludeSubFolders"
            Me.chkIncludeSubFolders.Size = New Size(114, 17)
            Me.chkIncludeSubFolders.TabIndex = 3
            Me.chkIncludeSubFolders.Text = "Include Subfolders"
            '
            'cmdSelectFolder
            '
            Me.cmdSelectFolder.Location = New Point(289, 16)
            Me.cmdSelectFolder.Name = "cmdSelectFolder"
            Me.cmdSelectFolder.Size = New Size(25, 23)
            Me.cmdSelectFolder.TabIndex = 2
            Me.cmdSelectFolder.Text = "..."
            '
            'txtSelexc
            '
            Me.txtSelexc.Location = New Point(13, 17)
            Me.txtSelexc.Name = "txtSelexc"
            Me.txtSelexc.Size = New Size(270, 20)
            Me.txtSelexc.TabIndex = 1
            '
            'Label1
            '
            Me.Label1.AutoSize = True
            Me.Label1.Location = New Point(0, 0)
            Me.Label1.Name = "Label1"
            Me.Label1.Size = New Size(72, 13)
            Me.Label1.TabIndex = 0
            Me.Label1.Text = "Select Folder:"
            '
            'Panel2
            '
            Me.TableLayoutPanel2.SetColumnSpan(Me.Panel2, 2)
            Me.Panel2.Controls.Add(Me.txtMediaName)
            Me.Panel2.Controls.Add(Me.lblMediaName)
            Me.Panel2.Dock = DockStyle.Fill
            Me.Panel2.Location = New Point(99, 4)
            Me.Panel2.Name = "Panel2"
            Me.Panel2.Size = New Size(325, 44)
            Me.Panel2.TabIndex = 22
            '
            'txtMediaName
            '
            Me.txtMediaName.Location = New Point(13, 17)
            Me.txtMediaName.Name = "txtMediaName"
            Me.txtMediaName.Size = New Size(301, 20)
            Me.txtMediaName.TabIndex = 5
            '
            'lblMediaName
            '
            Me.lblMediaName.AutoSize = True
            Me.lblMediaName.Location = New Point(0, 0)
            Me.lblMediaName.Name = "lblMediaName"
            Me.lblMediaName.Size = New Size(174, 13)
            Me.lblMediaName.TabIndex = 4
            Me.lblMediaName.Text = "Select full name for selected media:"
            '
            'Panel1
            '
            Me.Panel1.BorderStyle = BorderStyle.FixedSingle
            Me.Panel1.Controls.Add(Me.ToolStrip1)
            Me.Panel1.Location = New Point(4, 4)
            Me.Panel1.Name = "Panel1"
            Me.TableLayoutPanel2.SetRowSpan(Me.Panel1, 4)
            Me.Panel1.Size = New Size(89, 225)
            Me.Panel1.TabIndex = 21
            '
            'ToolStrip1
            '
            Me.ToolStrip1.AutoSize = False
            Me.ToolStrip1.BackColor = SystemColors.ButtonFace
            Me.ToolStrip1.CanOverflow = False
            Me.ToolStrip1.Dock = DockStyle.None
            Me.ToolStrip1.GripStyle = ToolStripGripStyle.Hidden
            Me.ToolStrip1.ImageScalingSize = New Size(48, 48)
            Me.ToolStrip1.Items.AddRange(New ToolStripItem() {Me.tsbFromCDROM, Me.tsbFromFolder, Me.tsbFromInternet})
            Me.ToolStrip1.LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow
            Me.ToolStrip1.Location = New Point(0, -2)
            Me.ToolStrip1.Name = "ToolStrip1"
            Me.ToolStrip1.RenderMode = ToolStripRenderMode.Professional
            Me.ToolStrip1.Size = New Size(86, 227)
            Me.ToolStrip1.TabIndex = 4
            '
            'tsbFromCDROM
            '
            Me.tsbFromCDROM.Checked = True
            Me.tsbFromCDROM.CheckState = CheckState.Checked
            Me.tsbFromCDROM.Image = My.Resources.Resources.cdrom_with_hardware
            Me.tsbFromCDROM.ImageAlign = ContentAlignment.TopCenter
            Me.tsbFromCDROM.ImageScaling = ToolStripItemImageScaling.None
            Me.tsbFromCDROM.ImageTransparentColor = Color.Magenta
            Me.tsbFromCDROM.Name = "tsbFromCDROM"
            Me.tsbFromCDROM.Size = New Size(84, 65)
            Me.tsbFromCDROM.Text = "From Media"
            Me.tsbFromCDROM.TextDirection = ToolStripTextDirection.Horizontal
            Me.tsbFromCDROM.TextImageRelation = TextImageRelation.ImageAboveText
            '
            'tsbFromFolder
            '
            Me.tsbFromFolder.Image = CType(resources.GetObject("tsbFromFolder.Image"), Image)
            Me.tsbFromFolder.ImageScaling = ToolStripItemImageScaling.None
            Me.tsbFromFolder.ImageTransparentColor = Color.Magenta
            Me.tsbFromFolder.Name = "tsbFromFolder"
            Me.tsbFromFolder.Size = New Size(84, 65)
            Me.tsbFromFolder.Text = "From Folder"
            Me.tsbFromFolder.TextImageRelation = TextImageRelation.ImageAboveText
            '
            'tsbFromInternet
            '
            Me.tsbFromInternet.Image = CType(resources.GetObject("tsbFromInternet.Image"), Image)
            Me.tsbFromInternet.ImageScaling = ToolStripItemImageScaling.None
            Me.tsbFromInternet.ImageTransparentColor = Color.Magenta
            Me.tsbFromInternet.Name = "tsbFromInternet"
            Me.tsbFromInternet.Size = New Size(84, 65)
            Me.tsbFromInternet.Text = "From Internet"
            Me.tsbFromInternet.TextImageRelation = TextImageRelation.ImageAboveText
            '
            'TableLayoutPanel1
            '
            Me.TableLayoutPanel1.ColumnCount = 2
            Me.TableLayoutPanel1.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0!))
            Me.TableLayoutPanel1.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0!))
            Me.TableLayoutPanel1.Controls.Add(Me.OK_Button, 0, 0)
            Me.TableLayoutPanel1.Controls.Add(Me.Cancel_Button, 1, 0)
            Me.TableLayoutPanel1.Dock = DockStyle.Fill
            Me.TableLayoutPanel1.GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            Me.TableLayoutPanel1.Location = New Point(269, 235)
            Me.TableLayoutPanel1.Name = "TableLayoutPanel1"
            Me.TableLayoutPanel1.RowCount = 1
            Me.TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Absolute, 31.0!))
            Me.TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Absolute, 31.0!))
            Me.TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Absolute, 31.0!))
            Me.TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Absolute, 31.0!))
            Me.TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Percent, 50.0!))
            Me.TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Percent, 50.0!))
            Me.TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Percent, 50.0!))
            Me.TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Percent, 50.0!))
            Me.TableLayoutPanel1.Size = New Size(155, 30)
            Me.TableLayoutPanel1.TabIndex = 30
            '
            'OK_Button
            '
            Me.OK_Button.DialogResult = DialogResult.OK
            Me.OK_Button.Dock = DockStyle.Fill
            Me.OK_Button.Location = New Point(3, 3)
            Me.OK_Button.Name = "OK_Button"
            Me.OK_Button.Size = New Size(71, 25)
            Me.OK_Button.TabIndex = 0
            Me.OK_Button.Text = "OK"
            '
            'Cancel_Button
            '
            Me.Cancel_Button.DialogResult = DialogResult.Cancel
            Me.Cancel_Button.Dock = DockStyle.Fill
            Me.Cancel_Button.Location = New Point(80, 3)
            Me.Cancel_Button.Name = "Cancel_Button"
            Me.Cancel_Button.Size = New Size(72, 25)
            Me.Cancel_Button.TabIndex = 1
            Me.Cancel_Button.Text = "Cancel"
            '
            'TableLayoutPanel2
            '
            Me.TableLayoutPanel2.ColumnCount = 3
            Me.TableLayoutPanel2.ColumnStyles.Add(New ColumnStyle)
            Me.TableLayoutPanel2.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.TableLayoutPanel2.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 161.0!))
            Me.TableLayoutPanel2.Controls.Add(Me.TableLayoutPanel1, 2, 3)
            Me.TableLayoutPanel2.Controls.Add(Me.Panel1, 0, 0)
            Me.TableLayoutPanel2.Controls.Add(Me.Panel2, 1, 0)
            Me.TableLayoutPanel2.Controls.Add(Me.Panel3, 1, 1)
            Me.TableLayoutPanel2.Controls.Add(Me.tcControl, 1, 2)
            Me.TableLayoutPanel2.Dock = DockStyle.Fill
            Me.TableLayoutPanel2.GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            Me.TableLayoutPanel2.Location = New Point(0, 0)
            Me.TableLayoutPanel2.Name = "TableLayoutPanel2"
            Me.TableLayoutPanel2.Padding = New Padding(1)
            Me.TableLayoutPanel2.RowCount = 4
            Me.TableLayoutPanel2.RowStyles.Add(New RowStyle(SizeType.Absolute, 50.0!))
            Me.TableLayoutPanel2.RowStyles.Add(New RowStyle(SizeType.Absolute, 75.0!))
            Me.TableLayoutPanel2.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0!))
            Me.TableLayoutPanel2.RowStyles.Add(New RowStyle(SizeType.Absolute, 36.0!))
            Me.TableLayoutPanel2.Size = New Size(428, 269)
            Me.TableLayoutPanel2.TabIndex = 26
            '
            'frmAddToList
            '
            Me.AutoScaleDimensions = New SizeF(6.0!, 13.0!)
            Me.AutoScaleMode = AutoScaleMode.Font
            Me.ClientSize = New Size(428, 269)
            Me.Controls.Add(Me.TableLayoutPanel2)
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.Name = "frmAddToList"
            Me.ShowInTaskbar = False
            Me.StartPosition = FormStartPosition.CenterParent
            Me.Text = "Add To List"
            Me.tcControl.ResumeLayout(False)
            Me.gbWhereToAdd.ResumeLayout(False)
            Me.gbWhereToAdd.PerformLayout()
            Me.GroupBox1.ResumeLayout(False)
            Me.GroupBox1.PerformLayout()
            Me.Panel3.ResumeLayout(False)
            Me.pnlFromMedia.ResumeLayout(False)
            Me.pnlFromMedia.PerformLayout()
            Me.pnlFromInternet.ResumeLayout(False)
            Me.pnlFromInternet.PerformLayout()
            Me.pnlFromFolder.ResumeLayout(False)
            Me.pnlFromFolder.PerformLayout()
            Me.Panel2.ResumeLayout(False)
            Me.Panel2.PerformLayout()
            Me.Panel1.ResumeLayout(False)
            Me.ToolStrip1.ResumeLayout(False)
            Me.ToolStrip1.PerformLayout()
            Me.TableLayoutPanel1.ResumeLayout(False)
            Me.TableLayoutPanel2.ResumeLayout(False)
            Me.ResumeLayout(False)

        End Sub
        Friend WithEvents Tikrink As Timer
        Friend WithEvents tcControl As TabControl
        Friend WithEvents TableLayoutPanel2 As TableLayoutPanel
        Friend WithEvents TableLayoutPanel1 As TableLayoutPanel
        Friend WithEvents OK_Button As Button
        Friend WithEvents Cancel_Button As Button
        Friend WithEvents Panel1 As Panel
        Friend WithEvents ToolStrip1 As ToolStrip
        Friend WithEvents tsbFromCDROM As ToolStripButton
        Friend WithEvents tsbFromFolder As ToolStripButton
        Friend WithEvents tsbFromInternet As ToolStripButton
        Friend WithEvents Panel2 As Panel
        Friend WithEvents txtMediaName As TextBox
        Friend WithEvents lblMediaName As Label
        Friend WithEvents Panel3 As Panel
        Friend WithEvents pnlFromFolder As Panel
        Friend WithEvents chkIncludeSubFolders As CheckBox
        Friend WithEvents cmdSelectFolder As Button
        Friend WithEvents txtSelexc As TextBox
        Friend WithEvents Label1 As Label
        Friend WithEvents pnlFromMedia As Panel
        Friend WithEvents chkIncludeMediaInfo As CheckBox
        Friend WithEvents cmbDrives As ComboBox
        Friend WithEvents lblSelectDrive As Label
        Friend WithEvents pnlFromInternet As Panel
        Friend WithEvents txtEnterInternetAdress As TextBox
        Friend WithEvents lblEnterInternetAdress As Label
        Friend WithEvents gbWhereToAdd As TabPage
        Friend WithEvents rbAllContentAddToSelectedMediaFolder As RadioButton
        Friend WithEvents rbAllContentAddAsNewMedia As RadioButton
        Friend WithEvents GroupBox1 As TabPage
        Friend WithEvents chkIncludeExtendedInfo As CheckBox

    End Class

End namespace