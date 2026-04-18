Namespace Forms

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Public Class Adding
        Inherits Form

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
            Me.pbProgress = New ProgressBar
            Me.PictureBox1 = New PictureBox
            Me.lblAction = New Label
            CType(Me.PictureBox1, System.ComponentModel.ISupportInitialize).BeginInit()
            Me.SuspendLayout()
            '
            'pbProgress
            '
            Me.pbProgress.Location = New Point(72, 42)
            Me.pbProgress.Name = "pbProgress"
            Me.pbProgress.Size = New Size(354, 20)
            Me.pbProgress.Style = ProgressBarStyle.Marquee
            Me.pbProgress.TabIndex = 0
            '
            'PictureBox1
            '
            Me.PictureBox1.Image = My.Resources.CD_6
            Me.PictureBox1.Location = New Point(13, 12)
            Me.PictureBox1.Name = "PictureBox1"
            Me.PictureBox1.Size = New Size(52, 50)
            Me.PictureBox1.TabIndex = 2
            Me.PictureBox1.TabStop = False
            '
            'lblAction
            '
            Me.lblAction.Location = New Point(71, 12)
            Me.lblAction.Name = "lblAction"
            Me.lblAction.Size = New Size(355, 26)
            Me.lblAction.TabIndex = 4
            '
            'frmAdding
            '
            Me.AutoScaleDimensions = New SizeF(6.0!, 13.0!)
            Me.AutoScaleMode = AutoScaleMode.Font
            Me.ClientSize = New Size(435, 73)
            Me.ControlBox = False
            Me.Controls.Add(Me.lblAction)
            Me.Controls.Add(Me.PictureBox1)
            Me.Controls.Add(Me.pbProgress)
            Me.FormBorderStyle = FormBorderStyle.FixedToolWindow
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.Name = "frmAdding"
            Me.ShowIcon = False
            Me.ShowInTaskbar = False
            Me.StartPosition = FormStartPosition.CenterScreen
            CType(Me.PictureBox1, System.ComponentModel.ISupportInitialize).EndInit()
            Me.ResumeLayout(False)

        End Sub
        Friend WithEvents pbProgress As ProgressBar
        Friend WithEvents PictureBox1 As PictureBox
        Friend WithEvents lblAction As Label

    End Class


End namespace