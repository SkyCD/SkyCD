Imports System.Drawing.Drawing2D

Namespace Forms

    Public Class About

        Dim msg As Object
        Dim background_nr As Integer
        Dim rotate_mode As Boolean = True
        Dim msglayer_nr As Integer
        Dim txt As New Collection
        Dim jx As Long = 0, jy1 As Integer = 1, jy2 As Integer = 1
        Dim jc As Integer = 0
        Dim tw As Boolean = False
        Dim zingsnis As Integer = 12

        <CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification:="<Pending>")>
        Private Sub tmrFX_Tick(ByVal sender As Object, ByVal e As EventArgs) Handles tmrFX.Tick
            Dim Matrix As New Matrix()
            Me.DrawingBox1.DeleteLayer(Me.msglayer_nr)
            Me.DrawingBox1.ForeColor = Color.FromArgb(CInt(255 - jc * 2.4), Color.White)
            If jc > 90 Then
                Me.DrawingBox1.PenColor = Color.FromArgb(0, Me.DrawingBox1.ForeColor)
            Else
                Me.DrawingBox1.PenColor = Color.FromArgb(90 - jc, Me.DrawingBox1.ForeColor)
            End If
            Me.msglayer_nr = Me.DrawingBox1.AddLayer()
            Dim Text As String = ""
            Dim I As Integer
            Dim kc As Integer = 0
            If jy1 > Me.txt.Count Or jy2 > Me.txt.Count Then
                jy1 = 1
                jy2 = 1
            End If
            For I = jy1 To jy2 - 1
                If Me.txt.Item(I).ToString.Substring(0, 2) <> "//" Then
                    Text = Text + Me.txt.Item(I).ToString + vbCrLf
                    kc = kc + 1
                End If
            Next
            If tw Then
                Text = Text & Me.txt.Item(jy2).ToString.Substring(0, CInt(jx))
            Else
                Text = Text & Me.txt.Item(jy2).ToString
                jx = Me.txt.Item(jy2).ToString.Length + 1
            End If
            With Me.DrawingBox1.Item(Me.msglayer_nr)
                .Path.AddString(Text, Me.Font.FontFamily, Me.Font.Style, Me.Font.Size, New PointF(20, 60), New StringFormat())
            End With
            jx = jx + 1
            If jx > Me.txt.Item(jy2).ToString.Length Then
                jy2 = jy2 + 1
                jx = 0
            End If
            If Me.txt.Item(jy2).ToString = "//textwrite" Then
                Me.tw = True
                jy2 = jy2 + 1
            End If
            If Me.txt.Item(jy2).ToString = "//newpage" Then
                If jc < 100 Then
                    jy2 = jy2 - 1
                    jx = Me.txt.Item(jy2).ToString.Length
                    jc = jc + 1
                Else
                    jy2 = jy2 + 1
                    jy1 = jy2
                    jc = 0
                    Me.tw = False
                End If
            ElseIf kc > Me.zingsnis Then
                jy1 = jy2
                Me.tw = True
            End If
            If jy2 > Me.txt.Count Then
                jy1 = 1
                jy2 = 1
            End If
            'Matrix.Rotate(-laipsnis)
            'laipsnis = laipsnis + 0.5
            If rotate_mode Then
                Matrix.Rotate(0.5)
            Else
                Matrix.Rotate(-0.5)
            End If
            With Me.DrawingBox1.Item(Me.background_nr).Path
                .Transform(Matrix)
            End With
            '        Randomize()        
            'Me.DrawingBox1.Transform(Matrix)
            'Exit Sub                
            Me.Refresh()
            'Me.bitmap.SetPixel(vm, vh, Color.AliceBlue)
            'Me.DrawBox.Image = Me.bitmap
            Exit Sub
            '    Me.tmrFX.Enabled = False
            'Static km As Single = Me.Height / 100
            'Static ng As Single = Me.Height
            'Me.Height = km
            'km = km + ng / 100
            'If km >= ng Then Me.tmrFX.Enabled = False
        End Sub


        Dim mchange As Boolean = False
        Dim ky As Single

        Dim schange As Boolean = False
        Private Sub DrawingBox1_KeyDown(ByVal sender As Object, ByVal e As KeyEventArgs) Handles DrawingBox1.KeyDown
            If Convert.ToBoolean(modGlobal.Settings.ReadSetting("MouseWheel.SlowMotion.Effect", "About", False)) Then
                If e.KeyCode = Keys.ShiftKey Then schange = True
            End If
        End Sub

        Private Sub DrawingBox1_KeyUp(ByVal sender As Object, ByVal e As KeyEventArgs) Handles DrawingBox1.KeyUp
            If e.KeyCode = Keys.P Then
                tmrFX.Enabled = Not tmrFX.Enabled
                If tmrFX.Enabled Then
                    Text = Text.Substring(0, Text.Length - 4)
                Else
                    Text += " [p]"
                End If
            End If
            If schange = True Then schange = False
        End Sub

        <CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification:="<Pending>")>
        Private Sub DrawingBox1_MouseDown(ByVal sender As Object, ByVal e As MouseEventArgs) Handles DrawingBox1.MouseDown
            If e.Button = MouseButtons.Left Then
                If (e.X < 20 Or e.X > (Me.DrawingBox1.Width - 20)) Or (e.Y < 20 Or e.Y > (Me.DrawingBox1.Height - 20)) Then
                    mchange = True
                    ky = e.Y
                    Me.Cursor = New Cursor(My.Resources.Cursors.empty.Handle)
                End If
            End If
        End Sub

        Private Sub DrawingBox1_MouseLeave(ByVal sender As Object, ByVal e As System.EventArgs) Handles DrawingBox1.MouseLeave
            If mchange Then
                mchange = False
                Me.Cursor = Cursors.Default
            End If
        End Sub

        Private Sub DrawingBox1_MouseMove(ByVal sender As Object, ByVal e As MouseEventArgs) Handles DrawingBox1.MouseMove
            If e.Button = MouseButtons.Left Then
                If mchange Then
                    If ky < e.Y Then
                        Me.rotate_mode = True
                    Else
                        Me.rotate_mode = False
                    End If
                End If
            End If
        End Sub

        Private Sub DrawingBox1_MouseUp(ByVal sender As Object, ByVal e As MouseEventArgs) Handles DrawingBox1.MouseUp
            If e.Button = MouseButtons.Left Then
                If mchange Then
                    mchange = False
                    Me.Cursor = Cursors.Default
                Else
                    'If ((e.X < 20 Or e.X > (Me.DrawingBox1.Width - 20)) Or (e.Y < 20 Or e.Y > (Me.DrawingBox1.Height - 20))) = False Then
                    'jy2 = jy2 + Me.zingsnis - (jy2 - jy1) + 1
                    'Dim eb As New System.EventArgs()
                    'Me.tmrFX_Tick(sender, eb)
                    'End If
                End If
            End If
        End Sub

        <CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification:="<Pending>")>
        Public Sub New()

            ' This call is required by the Windows Form Designer.
            InitializeComponent()

            Dim I As Integer
            Dim ApplicationTitle As String
            If My.Application.Info.Title <> "" Then
                ApplicationTitle = My.Application.Info.Title
            Else
                ApplicationTitle = IO.Path.GetFileNameWithoutExtension(My.Application.Info.AssemblyName)
            End If
            ' Add any initialization after the InitializeComponent() call.
            Me.DrawingBox1.PenColor = Color.BlueViolet
            Me.background_nr = Me.DrawingBox1.AddLayer()
            Me.DrawingBox1.Item(Me.background_nr).Pen.Color = Color.WhiteSmoke
            Me.DrawingBox1.Item(Me.background_nr).Pen.Width = 1
            Me.DrawingBox1.ForeColor = Color.FromArgb(80, Color.Blue)
            Me.DrawingBox1.PenColor = Color.FromArgb(80, Color.White)
            With Me.DrawingBox1.Item(Me.DrawingBox1.AddLayer())
                .Path.AddRectangle(New RectangleF(10, 10, Me.DrawingBox1.Width - 20, Me.DrawingBox1.Height - 20))
            End With
            Me.DrawingBox1.ForeColor = Color.Gold
            Me.DrawingBox1.PenColor = Color.FromArgb(90, Me.DrawingBox1.ForeColor)
            Dim frmat As New StringFormat
            frmat.Alignment = StringAlignment.Center
            With Me.DrawingBox1.Item(Me.DrawingBox1.AddLayer())
                .Path.AddString(ApplicationTitle, Me.DrawingBox1.Font.FontFamily, Me.DrawingBox1.Font.Style, 21, New PointF(Convert.ToSingle(Me.DrawingBox1.Width / 2), 20), frmat)
            End With
            Randomize()
            Dim r As Single = CInt(Math.Sqrt(Me.DrawingBox1.Width ^ 2 + Me.DrawingBox1.Height ^ 2))
            With Me.DrawingBox1.Item(Me.background_nr).Path
                For I = 0 To CInt(modGlobal.Settings.ReadSetting("Background.Points", "About", 2100))
                    .AddPie(Math.Abs(Fix(Rnd() * (r * 2))) - r, Math.Abs(Fix(Rnd() * (r * 2))) - r, 1, 1, 0, 360)
                Next
            End With
            Me.msglayer_nr = Me.DrawingBox1.AddLayer()
            Dim Sk As String = "-----------------------------------"
            With Me.txt
                '    .Add("Description")
                '   .Add(Sk)
                '.Add("//textwrite")
                'My.Application.Info.Description
                'Dim Mas() As String = Strings.Split(My.Resources.Licenses.En.ToString, vbCrLf)
                'Dim Mt() As String
                'For Each That As String In Mas
                'If That.Length > 65 Then
                '   Mt = Strings.Split(That, " ")
                'Text = ""
                'For Each Item As String In Mt
                'If Item.Length + Text.Length > 65 Then
                '.Add(Text)
                'Text = ""
                'Else
                'Text = Text & Item & " "
                'End If
                'Next
                'If Text <> "" Then .Add(Text)
                ''For L = 1 To That.Length Step 42
                ''.Add(Strings.Mid(That, L, 42))
                ''Next
                'Else
                '.Add(That)
                'End If
                'Next
                '.Add("//newpage")
                .Add(modGlobal.Translate(Me, "Info"))
                .Add(Sk)
                .Add("//textwrite")
                .Add(String.Format(modGlobal.Translate(Me, "Version: {0}"), My.Application.Info.Version.ToString))
                .Add(String.Format(modGlobal.Translate(Me, "Authors: {0}"), "Raimondas Rimkevièius (aka MekDrop)"))
                .Add(Microsoft.VisualBasic.Strings.StrDup(String.Format(modGlobal.Translate(Me, "Authors: {0}"), "").Length, " ") + "<skycd@mekdrop.name>")
                .Add("//newpage")

                .Add("//textwrite")
                .Add(modGlobal.Translate(Me, "Thanks To:"))
                .Add(" -> Freeware-base.de " + "(" + Translate(Me, "for adding to their programs list SkyCD") + ")")
                .Add(" -> SourceForge.Net " + "(" + Translate(Me, "for webspace/project page") + ")")
                .Add("//newpage")

                .Add("//textwrite")
                .Add(modGlobal.Translate(Me, "Thanx also goes to these Persons:"))
                .Add(" -> GBiT " + "(" + Translate(Me, "ideas") + ")")
                .Add(" -> doc. V. Slivickas " + "(" + Translate(Me, "support") + ")")
                .Add(" ... " + Translate(Me, "and You!") + " :)")
                .Add("//newpage")
            End With
            Me.Text = Translate(Me, Me.Text)
        End Sub

        Private Sub About_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        End Sub

        <CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification:="<Pending>")>
        Private Function JustifyText(ByVal Text As String) As String
            Static Tarpas As Single = Me.DrawingBox1.CreateGraphics.MeasureString(" ", Me.Font, New PointF(20, 20), New StringFormat()).ToSize.Width
            Dim Ilgis As Single = Me.DrawingBox1.CreateGraphics.MeasureString(Text, Me.Font, New PointF(0, 0), New StringFormat()).Width
            Dim Mas() As String = Text.Split(" ")
            Dim L As Long = 1, K As Long = Convert.ToInt64(Math.Round(Math.Abs((DrawingBox1.Width - 40 - Ilgis) / Tarpas), MidpointRounding.AwayFromZero) - 2)
            Dim M As Integer
            Do
                For M = 0 To Mas.Length - 3
                    If L >= K Then Exit Do
                    L = L + 1
                    Mas(M) = Mas(M) + " "
                Next
            Loop Until L >= K
            Return String.Join(" ", Mas)
        End Function

        Private Sub DrawingBox1_MouseWheel(ByVal sender As Object, ByVal e As MouseEventArgs) Handles DrawingBox1.MouseWheel
            If schange Then
                Static defInterval As Integer = Me.tmrFX.Interval
                Dim iv As Integer = defInterval - e.Delta
                If iv < 0 Then
                    Me.tmrFX.Interval = 1
                Else
                    Me.tmrFX.Interval = iv
                End If
            End If
        End Sub


    End Class

End Namespace