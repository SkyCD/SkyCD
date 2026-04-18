Imports SkyCD.Controls
Imports SkyCD.AdvancedFunctions

Namespace Forms

    <CompilerServices.DesignerGenerated()>
    Partial Public Class About
        Inherits Form

        Friend WithEvents tmrFX As Timer
        Friend WithEvents DrawingBox1 As DrawingBox

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
        <DebuggerStepThrough()>
        Private Sub InitializeComponent()
            Me.components = New System.ComponentModel.Container()
            Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(About))
            Me.tmrFX = New System.Windows.Forms.Timer(Me.components)
            Me.DrawingBox1 = New DrawingBox()
            Me.SuspendLayout()
            '
            'tmrFX
            '
            Me.tmrFX.Enabled = True
            '
            'DrawingBox1
            '
            Me.DrawingBox1.BackColor = System.Drawing.Color.Black
            resources.ApplyResources(Me.DrawingBox1, "DrawingBox1")
            Me.DrawingBox1.ForeColor = System.Drawing.Color.Black
            Me.DrawingBox1.Name = "DrawingBox1"
            Me.DrawingBox1.PenAlingment = System.Drawing.Drawing2D.PenAlignment.Center
            Me.DrawingBox1.PenColor = System.Drawing.Color.Red
            Me.DrawingBox1.PenDashCap = System.Drawing.Drawing2D.DashCap.Flat
            Me.DrawingBox1.PenDashStyle = System.Drawing.Drawing2D.DashStyle.Solid
            Me.DrawingBox1.PenEndCap = System.Drawing.Drawing2D.LineCap.Flat
            Me.DrawingBox1.PenLineJoin = System.Drawing.Drawing2D.LineJoin.Miter
            Me.DrawingBox1.PenMiterLimit = 2.0!
            Me.DrawingBox1.PenStartCap = System.Drawing.Drawing2D.LineCap.Flat
            Me.DrawingBox1.PenWidth = 1.0!
            '
            'About
            '
            resources.ApplyResources(Me, "$this")
            Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
            Me.BackColor = System.Drawing.Color.Black
            Me.Controls.Add(Me.DrawingBox1)
            Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.Name = "About"
            Me.ResumeLayout(False)

        End Sub


    End Class

End Namespace