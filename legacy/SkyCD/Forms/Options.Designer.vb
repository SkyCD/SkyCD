Namespace Forms

#Const design = True
    <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
    Partial Public Class Options
        Inherits System.Windows.Forms.Form

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
            Me.OK = New System.Windows.Forms.Button()
            Me.Cancel = New System.Windows.Forms.Button()
            Me.Tab = New System.Windows.Forms.TabControl()
            Me.tpPlugIns = New System.Windows.Forms.TabPage()
            Me.cmdConfigurePlugIn = New System.Windows.Forms.Button()
            Me.cmdRefreshPlugIns = New System.Windows.Forms.Button()
            Me.lvPlugIns = New System.Windows.Forms.ListView()
            Me.chPlugInName = CType(New System.Windows.Forms.ColumnHeader(), System.Windows.Forms.ColumnHeader)
            Me.chPlugInType = CType(New System.Windows.Forms.ColumnHeader(), System.Windows.Forms.ColumnHeader)
            Me.chExtendedInfo = CType(New System.Windows.Forms.ColumnHeader(), System.Windows.Forms.ColumnHeader)
            Me.lblPlugIns = New System.Windows.Forms.Label()
            Me.cmdBrowsePlugInPath = New System.Windows.Forms.Button()
            Me.txtPlugIsPath = New System.Windows.Forms.TextBox()
            Me.lblPlugInsPath = New System.Windows.Forms.Label()
            Me.tbLanguage = New System.Windows.Forms.TabPage()
            Me.lstLanguages = New System.Windows.Forms.ListBox()
            Me.lblSelectLanguage = New System.Windows.Forms.Label()
            Me.Tab.SuspendLayout()
            Me.tpPlugIns.SuspendLayout()
            Me.tbLanguage.SuspendLayout()
            Me.SuspendLayout()
            '
            'OK
            '
            Me.OK.DialogResult = System.Windows.Forms.DialogResult.OK
            Me.OK.Location = New System.Drawing.Point(262, 246)
            Me.OK.Name = "OK"
            Me.OK.Size = New System.Drawing.Size(75, 23)
            Me.OK.TabIndex = 2
            Me.OK.Text = "OK"
            '
            'Cancel
            '
            Me.Cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel
            Me.Cancel.Location = New System.Drawing.Point(343, 246)
            Me.Cancel.Name = "Cancel"
            Me.Cancel.Size = New System.Drawing.Size(75, 23)
            Me.Cancel.TabIndex = 1
            Me.Cancel.Text = "Cancel"
            '
            'Tab
            '
            Me.Tab.Controls.Add(Me.tpPlugIns)
            Me.Tab.Controls.Add(Me.tbLanguage)
            Me.Tab.Location = New System.Drawing.Point(1, 2)
            Me.Tab.Name = "Tab"
            Me.Tab.SelectedIndex = 0
            Me.Tab.Size = New System.Drawing.Size(421, 238)
            Me.Tab.TabIndex = 3
            '
            'tpPlugIns
            '
            Me.tpPlugIns.Controls.Add(Me.cmdConfigurePlugIn)
            Me.tpPlugIns.Controls.Add(Me.cmdRefreshPlugIns)
            Me.tpPlugIns.Controls.Add(Me.lvPlugIns)
            Me.tpPlugIns.Controls.Add(Me.lblPlugIns)
            Me.tpPlugIns.Controls.Add(Me.cmdBrowsePlugInPath)
            Me.tpPlugIns.Controls.Add(Me.txtPlugIsPath)
            Me.tpPlugIns.Controls.Add(Me.lblPlugInsPath)
            Me.tpPlugIns.Location = New System.Drawing.Point(4, 22)
            Me.tpPlugIns.Name = "tpPlugIns"
            Me.tpPlugIns.Padding = New System.Windows.Forms.Padding(3)
            Me.tpPlugIns.Size = New System.Drawing.Size(413, 212)
            Me.tpPlugIns.TabIndex = 0
            Me.tpPlugIns.Text = "Plug-Ins"
            '
            'cmdConfigurePlugIn
            '
            Me.cmdConfigurePlugIn.Enabled = False
            Me.cmdConfigurePlugIn.Location = New System.Drawing.Point(250, 183)
            Me.cmdConfigurePlugIn.Name = "cmdConfigurePlugIn"
            Me.cmdConfigurePlugIn.Size = New System.Drawing.Size(75, 23)
            Me.cmdConfigurePlugIn.TabIndex = 6
            Me.cmdConfigurePlugIn.Text = "&Configure..."
            '
            'cmdRefreshPlugIns
            '
            Me.cmdRefreshPlugIns.Location = New System.Drawing.Point(331, 183)
            Me.cmdRefreshPlugIns.Name = "cmdRefreshPlugIns"
            Me.cmdRefreshPlugIns.Size = New System.Drawing.Size(75, 23)
            Me.cmdRefreshPlugIns.TabIndex = 5
            Me.cmdRefreshPlugIns.Text = "&Refresh"
            '
            'lvPlugIns
            '
            Me.lvPlugIns.Columns.AddRange(New System.Windows.Forms.ColumnHeader() {Me.chPlugInName, Me.chPlugInType, Me.chExtendedInfo})
            Me.lvPlugIns.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None
            Me.lvPlugIns.LabelWrap = False
            Me.lvPlugIns.Location = New System.Drawing.Point(10, 68)
            Me.lvPlugIns.MultiSelect = False
            Me.lvPlugIns.Name = "lvPlugIns"
            Me.lvPlugIns.Size = New System.Drawing.Size(397, 109)
            Me.lvPlugIns.TabIndex = 4
            Me.lvPlugIns.UseCompatibleStateImageBehavior = False
            '
            'chPlugInName
            '
            Me.chPlugInName.Text = "Name"
            '
            'chPlugInType
            '
            Me.chPlugInType.Text = "Type"
            '
            'chExtendedInfo
            '
            Me.chExtendedInfo.Text = "Extended Info"
            '
            'lblPlugIns
            '
            Me.lblPlugIns.AutoSize = True
            Me.lblPlugIns.Location = New System.Drawing.Point(9, 51)
            Me.lblPlugIns.Name = "lblPlugIns"
            Me.lblPlugIns.Size = New System.Drawing.Size(63, 13)
            Me.lblPlugIns.TabIndex = 3
            Me.lblPlugIns.Text = "Plug-Ins &list:"
            '
            'cmdBrowsePlugInPath
            '
            Me.cmdBrowsePlugInPath.Location = New System.Drawing.Point(384, 24)
            Me.cmdBrowsePlugInPath.Name = "cmdBrowsePlugInPath"
            Me.cmdBrowsePlugInPath.Size = New System.Drawing.Size(22, 23)
            Me.cmdBrowsePlugInPath.TabIndex = 2
            Me.cmdBrowsePlugInPath.Text = "..."
            Me.cmdBrowsePlugInPath.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            Me.cmdBrowsePlugInPath.UseMnemonic = False
            '
            'txtPlugIsPath
            '
            Me.txtPlugIsPath.Location = New System.Drawing.Point(9, 24)
            Me.txtPlugIsPath.Name = "txtPlugIsPath"
            Me.txtPlugIsPath.Size = New System.Drawing.Size(373, 20)
            Me.txtPlugIsPath.TabIndex = 1
            '
            'lblPlugInsPath
            '
            Me.lblPlugInsPath.AutoSize = True
            Me.lblPlugInsPath.Location = New System.Drawing.Point(8, 7)
            Me.lblPlugInsPath.Name = "lblPlugInsPath"
            Me.lblPlugInsPath.Size = New System.Drawing.Size(72, 13)
            Me.lblPlugInsPath.TabIndex = 0
            Me.lblPlugInsPath.Text = "&Plug-Ins path:"
            '
            'tbLanguage
            '
            Me.tbLanguage.Controls.Add(Me.lstLanguages)
            Me.tbLanguage.Controls.Add(Me.lblSelectLanguage)
            Me.tbLanguage.Location = New System.Drawing.Point(4, 22)
            Me.tbLanguage.Name = "tbLanguage"
            Me.tbLanguage.Padding = New System.Windows.Forms.Padding(3)
            Me.tbLanguage.Size = New System.Drawing.Size(413, 212)
            Me.tbLanguage.TabIndex = 1
            Me.tbLanguage.Text = "Language"
            '
            'lstLanguages
            '
            Me.lstLanguages.FormattingEnabled = True
            Me.lstLanguages.Location = New System.Drawing.Point(9, 23)
            Me.lstLanguages.Name = "lstLanguages"
            Me.lstLanguages.Size = New System.Drawing.Size(397, 173)
            Me.lstLanguages.Sorted = True
            Me.lstLanguages.TabIndex = 1
            '
            'lblSelectLanguage
            '
            Me.lblSelectLanguage.AccessibleRole = System.Windows.Forms.AccessibleRole.List
            Me.lblSelectLanguage.AutoSize = True
            Me.lblSelectLanguage.Location = New System.Drawing.Point(8, 7)
            Me.lblSelectLanguage.Name = "lblSelectLanguage"
            Me.lblSelectLanguage.Size = New System.Drawing.Size(87, 13)
            Me.lblSelectLanguage.TabIndex = 0
            Me.lblSelectLanguage.Text = "&Select language:"
            '
            'Options
            '
            Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
            Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
            Me.ClientSize = New System.Drawing.Size(423, 276)
            Me.Controls.Add(Me.Tab)
            Me.Controls.Add(Me.OK)
            Me.Controls.Add(Me.Cancel)
            Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.Name = "Options"
            Me.Text = "Options"
            Me.Tab.ResumeLayout(False)
            Me.tpPlugIns.ResumeLayout(False)
            Me.tpPlugIns.PerformLayout()
            Me.tbLanguage.ResumeLayout(False)
            Me.tbLanguage.PerformLayout()
            Me.ResumeLayout(False)

        End Sub
        Friend WithEvents Cancel As System.Windows.Forms.Button
        Friend WithEvents OK As System.Windows.Forms.Button
        Friend WithEvents Tab As System.Windows.Forms.TabControl
        Friend WithEvents tpPlugIns As System.Windows.Forms.TabPage
        Friend WithEvents txtPlugIsPath As System.Windows.Forms.TextBox
        Friend WithEvents lblPlugInsPath As System.Windows.Forms.Label
        Friend WithEvents cmdBrowsePlugInPath As System.Windows.Forms.Button
        Friend WithEvents lblPlugIns As System.Windows.Forms.Label
        Friend WithEvents lvPlugIns As System.Windows.Forms.ListView
        Friend WithEvents chPlugInName As System.Windows.Forms.ColumnHeader
        Friend WithEvents chPlugInType As System.Windows.Forms.ColumnHeader
        Friend WithEvents cmdRefreshPlugIns As System.Windows.Forms.Button
        Friend WithEvents cmdConfigurePlugIn As System.Windows.Forms.Button
        Friend WithEvents chExtendedInfo As System.Windows.Forms.ColumnHeader
        Friend WithEvents tbLanguage As System.Windows.Forms.TabPage
        Friend WithEvents lblSelectLanguage As System.Windows.Forms.Label
        Friend WithEvents lstLanguages As System.Windows.Forms.ListBox
    End Class

End namespace