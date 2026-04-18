Namespace Forms

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Public Class Properties
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()> _
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
    <System.Diagnostics.DebuggerStepThrough()> _
    Private Sub InitializeComponent()
        Me.TableLayoutPanel1 = New System.Windows.Forms.TableLayoutPanel
        Me.OK_Button = New System.Windows.Forms.Button
        Me.Cancel_Button = New System.Windows.Forms.Button
        Me.tbControl = New System.Windows.Forms.TabControl
        Me.tp = New System.Windows.Forms.TabPage
        Me.rtbComments = New System.Windows.Forms.RichTextBox
        Me.lblComments = New System.Windows.Forms.Label
        Me.pbIcon = New System.Windows.Forms.PictureBox
        Me.txtName = New System.Windows.Forms.TextBox
        Me.lblName = New System.Windows.Forms.Label
        Me.tpObjectInfo = New System.Windows.Forms.TabPage
        Me.lvProperties = New System.Windows.Forms.ListView
        Me.chProperty = New System.Windows.Forms.ColumnHeader
        Me.chValue = New System.Windows.Forms.ColumnHeader
        Me.TableLayoutPanel1.SuspendLayout()
        Me.tbControl.SuspendLayout()
        Me.tp.SuspendLayout()
        CType(Me.pbIcon, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.tpObjectInfo.SuspendLayout()
        Me.SuspendLayout()
        '
        'TableLayoutPanel1
        '
        Me.TableLayoutPanel1.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.TableLayoutPanel1.ColumnCount = 2
        Me.TableLayoutPanel1.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50.0!))
        Me.TableLayoutPanel1.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50.0!))
        Me.TableLayoutPanel1.Controls.Add(Me.OK_Button, 0, 0)
        Me.TableLayoutPanel1.Controls.Add(Me.Cancel_Button, 1, 0)
        Me.TableLayoutPanel1.Location = New System.Drawing.Point(261, 374)
        Me.TableLayoutPanel1.Name = "TableLayoutPanel1"
        Me.TableLayoutPanel1.RowCount = 1
        Me.TableLayoutPanel1.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50.0!))
        Me.TableLayoutPanel1.Size = New System.Drawing.Size(146, 29)
        Me.TableLayoutPanel1.TabIndex = 0
        '
        'OK_Button
        '
        Me.OK_Button.Anchor = System.Windows.Forms.AnchorStyles.None
        Me.OK_Button.DialogResult = System.Windows.Forms.DialogResult.OK
        Me.OK_Button.Location = New System.Drawing.Point(3, 3)
        Me.OK_Button.Name = "OK_Button"
        Me.OK_Button.Size = New System.Drawing.Size(67, 23)
        Me.OK_Button.TabIndex = 0
        Me.OK_Button.Text = "&OK"
        '
        'Cancel_Button
        '
        Me.Cancel_Button.Anchor = System.Windows.Forms.AnchorStyles.None
        Me.Cancel_Button.DialogResult = System.Windows.Forms.DialogResult.Cancel
        Me.Cancel_Button.Location = New System.Drawing.Point(76, 3)
        Me.Cancel_Button.Name = "Cancel_Button"
        Me.Cancel_Button.Size = New System.Drawing.Size(67, 23)
        Me.Cancel_Button.TabIndex = 1
        Me.Cancel_Button.Text = "&Cancel"
        '
        'tbControl
        '
        Me.tbControl.Controls.Add(Me.tp)
        Me.tbControl.Controls.Add(Me.tpObjectInfo)
        Me.tbControl.Location = New System.Drawing.Point(0, 2)
        Me.tbControl.Name = "tbControl"
        Me.tbControl.SelectedIndex = 0
        Me.tbControl.Size = New System.Drawing.Size(411, 366)
        Me.tbControl.TabIndex = 1
        '
        'tp
        '
        Me.tp.Controls.Add(Me.rtbComments)
        Me.tp.Controls.Add(Me.lblComments)
        Me.tp.Controls.Add(Me.pbIcon)
        Me.tp.Controls.Add(Me.txtName)
        Me.tp.Controls.Add(Me.lblName)
        Me.tp.Location = New System.Drawing.Point(4, 22)
        Me.tp.Name = "tp"
        Me.tp.Padding = New System.Windows.Forms.Padding(3)
        Me.tp.Size = New System.Drawing.Size(403, 340)
        Me.tp.TabIndex = 0
        Me.tp.Text = "General"
        '
        'rtbComments
        '
        Me.rtbComments.AutoWordSelection = True
        Me.rtbComments.EnableAutoDragDrop = True
        Me.rtbComments.Location = New System.Drawing.Point(10, 77)
        Me.rtbComments.MaxLength = 2000
        Me.rtbComments.Name = "rtbComments"
        Me.rtbComments.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.ForcedVertical
        Me.rtbComments.ShortcutsEnabled = False
        Me.rtbComments.ShowSelectionMargin = True
        Me.rtbComments.Size = New System.Drawing.Size(385, 257)
        Me.rtbComments.TabIndex = 5
        Me.rtbComments.Text = ""
        '
        'lblComments
        '
        Me.lblComments.AutoSize = True
        Me.lblComments.Location = New System.Drawing.Point(9, 61)
        Me.lblComments.Name = "lblComments"
        Me.lblComments.Size = New System.Drawing.Size(59, 13)
        Me.lblComments.TabIndex = 3
        Me.lblComments.Text = "&Comments:"
        '
        'pbIcon
        '
        Me.pbIcon.Anchor = System.Windows.Forms.AnchorStyles.None
        Me.pbIcon.Location = New System.Drawing.Point(10, 7)
        Me.pbIcon.Name = "pbIcon"
        Me.pbIcon.Size = New System.Drawing.Size(32, 32)
        Me.pbIcon.TabIndex = 0
        Me.pbIcon.TabStop = False
        '
        'txtName
        '
        Me.txtName.Location = New System.Drawing.Point(49, 23)
        Me.txtName.Name = "txtName"
        Me.txtName.ReadOnly = True
        Me.txtName.Size = New System.Drawing.Size(346, 20)
        Me.txtName.TabIndex = 2
        '
        'lblName
        '
        Me.lblName.AutoSize = True
        Me.lblName.Location = New System.Drawing.Point(48, 7)
        Me.lblName.Name = "lblName"
        Me.lblName.Size = New System.Drawing.Size(38, 13)
        Me.lblName.TabIndex = 1
        Me.lblName.Text = "&Name:"
        '
        'tpObjectInfo
        '
        Me.tpObjectInfo.Controls.Add(Me.lvProperties)
        Me.tpObjectInfo.Location = New System.Drawing.Point(4, 22)
        Me.tpObjectInfo.Name = "tpObjectInfo"
        Me.tpObjectInfo.Padding = New System.Windows.Forms.Padding(3)
        Me.tpObjectInfo.Size = New System.Drawing.Size(403, 340)
        Me.tpObjectInfo.TabIndex = 1
        Me.tpObjectInfo.Text = "Info"
        '
        'lvProperties
        '
        Me.lvProperties.Columns.AddRange(New System.Windows.Forms.ColumnHeader() {Me.chProperty, Me.chValue})
        Me.lvProperties.Dock = System.Windows.Forms.DockStyle.Fill
        Me.lvProperties.FullRowSelect = True
        Me.lvProperties.GridLines = True
        Me.lvProperties.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable
        Me.lvProperties.Location = New System.Drawing.Point(3, 3)
        Me.lvProperties.Name = "lvProperties"
        Me.lvProperties.ShowGroups = False
        Me.lvProperties.Size = New System.Drawing.Size(397, 334)
        Me.lvProperties.Sorting = System.Windows.Forms.SortOrder.Ascending
        Me.lvProperties.TabIndex = 0
        Me.lvProperties.UseCompatibleStateImageBehavior = False
        Me.lvProperties.View = System.Windows.Forms.View.Details
        '
        'chProperty
        '
        Me.chProperty.Text = "Property"
        Me.chProperty.Width = 183
        '
        'chValue
        '
        Me.chValue.Text = "Value"
        Me.chValue.Width = 209
        '
        'frmProperties
        '
        Me.AcceptButton = Me.OK_Button
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.CancelButton = Me.Cancel_Button
        Me.ClientSize = New System.Drawing.Size(411, 406)
        Me.Controls.Add(Me.tbControl)
        Me.Controls.Add(Me.TableLayoutPanel1)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.Name = "frmProperties"
        Me.ShowInTaskbar = False
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        Me.Text = "Properties"
        Me.TableLayoutPanel1.ResumeLayout(False)
        Me.tbControl.ResumeLayout(False)
        Me.tp.ResumeLayout(False)
        Me.tp.PerformLayout()
        CType(Me.pbIcon, System.ComponentModel.ISupportInitialize).EndInit()
        Me.tpObjectInfo.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub
    Friend WithEvents TableLayoutPanel1 As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents OK_Button As System.Windows.Forms.Button
    Friend WithEvents Cancel_Button As System.Windows.Forms.Button
    Friend WithEvents tbControl As System.Windows.Forms.TabControl
    Friend WithEvents tp As System.Windows.Forms.TabPage
    Friend WithEvents tpObjectInfo As System.Windows.Forms.TabPage
    Friend WithEvents pbIcon As System.Windows.Forms.PictureBox
    Friend WithEvents lblName As System.Windows.Forms.Label
    Friend WithEvents txtName As System.Windows.Forms.TextBox
    Friend WithEvents lblComments As System.Windows.Forms.Label
    Friend WithEvents rtbComments As System.Windows.Forms.RichTextBox
    Friend WithEvents lvProperties As System.Windows.Forms.ListView
    Friend WithEvents chProperty As System.Windows.Forms.ColumnHeader
    Friend WithEvents chValue As System.Windows.Forms.ColumnHeader

End Class


End namespace