Namespace Forms

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Public Class Login
        Inherits Form

        'Form overrides dispose to clean up the component list.
        <System.Diagnostics.DebuggerNonUserCode()>
        Protected Overloads Overrides Sub Dispose(ByVal disposing As Boolean)
            If disposing Then
                If Not (components Is Nothing) Then
                    components.Dispose()
                End If
            End If
            MyBase.Dispose(disposing)
        End Sub
        Friend WithEvents LogoPictureBox As PictureBox

        'Required by the Windows Form Designer
        Private components As System.ComponentModel.IContainer

        'NOTE: The following procedure is required by the Windows Form Designer
        'It can be modified using the Windows Form Designer.  
        'Do not modify it using the code editor.
        <System.Diagnostics.DebuggerStepThrough()>
        Private Sub InitializeComponent()
            Me.LogoPictureBox = New PictureBox
            Me.TableLayoutPanel1 = New TableLayoutPanel
            Me.Panel1 = New Panel
            Me.Cancel = New Button
            Me.OK = New Button
            Me.PasswordTextBox = New TextBox
            Me.UsernameTextBox = New TextBox
            Me.PasswordLabel = New Label
            Me.UsernameLabel = New Label
            CType(Me.LogoPictureBox, System.ComponentModel.ISupportInitialize).BeginInit()
            Me.TableLayoutPanel1.SuspendLayout()
            Me.Panel1.SuspendLayout()
            Me.SuspendLayout()
            '
            'LogoPictureBox
            '
            Me.LogoPictureBox.BackColor = Color.White
            Me.LogoPictureBox.BorderStyle = BorderStyle.FixedSingle
            Me.LogoPictureBox.Dock = DockStyle.Fill
            Me.LogoPictureBox.Image = My.Resources.Resources.keys
            Me.LogoPictureBox.Location = New Point(3, 3)
            Me.LogoPictureBox.Name = "LogoPictureBox"
            Me.LogoPictureBox.Size = New Size(102, 139)
            Me.LogoPictureBox.SizeMode = PictureBoxSizeMode.CenterImage
            Me.LogoPictureBox.TabIndex = 0
            Me.LogoPictureBox.TabStop = False
            '
            'TableLayoutPanel1
            '
            Me.TableLayoutPanel1.ColumnCount = 2
            Me.TableLayoutPanel1.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 31.17647!))
            Me.TableLayoutPanel1.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 68.82353!))
            Me.TableLayoutPanel1.Controls.Add(Me.LogoPictureBox, 0, 0)
            Me.TableLayoutPanel1.Controls.Add(Me.Panel1, 1, 0)
            Me.TableLayoutPanel1.Dock = DockStyle.Fill
            Me.TableLayoutPanel1.Location = New Point(0, 0)
            Me.TableLayoutPanel1.Name = "TableLayoutPanel1"
            Me.TableLayoutPanel1.RowCount = 1
            Me.TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Percent, 50.0!))
            Me.TableLayoutPanel1.Size = New Size(347, 145)
            Me.TableLayoutPanel1.TabIndex = 6
            '
            'Panel1
            '
            Me.Panel1.Controls.Add(Me.Cancel)
            Me.Panel1.Controls.Add(Me.OK)
            Me.Panel1.Controls.Add(Me.PasswordTextBox)
            Me.Panel1.Controls.Add(Me.UsernameTextBox)
            Me.Panel1.Controls.Add(Me.PasswordLabel)
            Me.Panel1.Controls.Add(Me.UsernameLabel)
            Me.Panel1.Dock = DockStyle.Fill
            Me.Panel1.Location = New Point(111, 3)
            Me.Panel1.Name = "Panel1"
            Me.Panel1.Size = New Size(233, 139)
            Me.Panel1.TabIndex = 1
            '
            'Cancel
            '
            Me.Cancel.DialogResult = DialogResult.Cancel
            Me.Cancel.Location = New Point(131, 112)
            Me.Cancel.Name = "Cancel"
            Me.Cancel.Size = New Size(94, 23)
            Me.Cancel.TabIndex = 11
            Me.Cancel.Text = "&Cancel"
            '
            'OK
            '
            Me.OK.DialogResult = DialogResult.OK
            Me.OK.Location = New Point(31, 112)
            Me.OK.Name = "OK"
            Me.OK.Size = New Size(94, 23)
            Me.OK.TabIndex = 10
            Me.OK.Text = "&OK"
            '
            'PasswordTextBox
            '
            Me.PasswordTextBox.Location = New Point(5, 77)
            Me.PasswordTextBox.Name = "PasswordTextBox"
            Me.PasswordTextBox.PasswordChar = Global.Microsoft.VisualBasic.ChrW(42)
            Me.PasswordTextBox.Size = New Size(220, 20)
            Me.PasswordTextBox.TabIndex = 9
            '
            'UsernameTextBox
            '
            Me.UsernameTextBox.Location = New Point(5, 20)
            Me.UsernameTextBox.Name = "UsernameTextBox"
            Me.UsernameTextBox.Size = New Size(220, 20)
            Me.UsernameTextBox.TabIndex = 7
            '
            'PasswordLabel
            '
            Me.PasswordLabel.Location = New Point(3, 57)
            Me.PasswordLabel.Name = "PasswordLabel"
            Me.PasswordLabel.Size = New Size(220, 23)
            Me.PasswordLabel.TabIndex = 8
            Me.PasswordLabel.Text = "&Password"
            Me.PasswordLabel.TextAlign = ContentAlignment.MiddleLeft
            '
            'UsernameLabel
            '
            Me.UsernameLabel.Location = New Point(3, 0)
            Me.UsernameLabel.Name = "UsernameLabel"
            Me.UsernameLabel.Size = New Size(220, 23)
            Me.UsernameLabel.TabIndex = 6
            Me.UsernameLabel.Text = "&User name"
            Me.UsernameLabel.TextAlign = ContentAlignment.MiddleLeft
            '
            'frmLogin
            '
            Me.AcceptButton = Me.OK
            Me.AutoScaleDimensions = New SizeF(6.0!, 13.0!)
            Me.AutoScaleMode = AutoScaleMode.Font
            Me.CancelButton = Me.Cancel
            Me.ClientSize = New Size(347, 145)
            Me.Controls.Add(Me.TableLayoutPanel1)
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.Name = "frmLogin"
            Me.SizeGripStyle = SizeGripStyle.Hide
            Me.StartPosition = FormStartPosition.CenterParent
            Me.Text = "Server login"
            CType(Me.LogoPictureBox, System.ComponentModel.ISupportInitialize).EndInit()
            Me.TableLayoutPanel1.ResumeLayout(False)
            Me.Panel1.ResumeLayout(False)
            Me.Panel1.PerformLayout()
            Me.ResumeLayout(False)

        End Sub
        Friend WithEvents TableLayoutPanel1 As TableLayoutPanel
        Friend WithEvents Panel1 As Panel
        Friend WithEvents Cancel As Button
        Friend WithEvents OK As Button
        Friend WithEvents PasswordTextBox As TextBox
        Friend WithEvents UsernameTextBox As TextBox
        Friend WithEvents PasswordLabel As Label
        Friend WithEvents UsernameLabel As Label

    End Class


End namespace