Namespace Forms

Public Class Properties ' Creates Properties Dialog Window

    Private Sub OK_Button_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles OK_Button.Click
        Me.Text = Me.rtbComments.Rtf
        Me.Close()
    End Sub

    Private Sub Cancel_Button_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Cancel_Button.Click
        Me.Close()
    End Sub

        <CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification:="<Pending>")>
        Private Sub rtbComments_KeyUp(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyEventArgs) Handles rtbComments.KeyUp
        Dim fnt As System.Drawing.Font
        With Me.rtbComments
            If e.Control And Not e.Alt Then
                Select Case e.KeyCode
                    Case Keys.L
                        .SelectionBullet = Not .SelectionBullet
                    Case Keys.I
                        With .SelectionFont
                            Dim Stl As System.Drawing.FontStyle = FontStyle.Regular
                            If .Bold Then Stl = Stl Or FontStyle.Bold
                            If Not .Italic Then Stl = Stl Or FontStyle.Italic
                            If .Strikeout Then Stl = Stl Or FontStyle.Strikeout
                            If .Underline Then Stl = Stl Or FontStyle.Underline
                            fnt = New System.Drawing.Font(.FontFamily, .Size, Stl)
                        End With
                        .SelectionFont = fnt
                    Case Keys.B
                        With .SelectionFont
                            Dim Stl As System.Drawing.FontStyle = FontStyle.Regular
                            If Not .Bold Then Stl = Stl Or FontStyle.Bold
                            If .Italic Then Stl = Stl Or FontStyle.Italic
                            If .Strikeout Then Stl = Stl Or FontStyle.Strikeout
                            If .Underline Then Stl = Stl Or FontStyle.Underline
                            fnt = New System.Drawing.Font(.FontFamily, .Size, Stl)
                        End With
                        .SelectionFont = fnt
                    Case Keys.U
                        With .SelectionFont
                            Dim Stl As System.Drawing.FontStyle = FontStyle.Regular
                            If .Bold Then Stl = Stl Or FontStyle.Bold
                            If .Italic Then Stl = Stl Or FontStyle.Italic
                            If .Strikeout Then Stl = Stl Or FontStyle.Strikeout
                            If Not .Underline Then Stl = Stl Or FontStyle.Underline
                            fnt = New System.Drawing.Font(.FontFamily, .Size, Stl)
                        End With
                        .SelectionFont = fnt
                    Case Keys.S
                        With .SelectionFont
                            Dim Stl As System.Drawing.FontStyle = FontStyle.Regular
                            If .Bold Then Stl = Stl Or FontStyle.Bold
                            If .Italic Then Stl = Stl Or FontStyle.Italic
                            If Not .Strikeout Then Stl = Stl Or FontStyle.Strikeout
                            If .Underline Then Stl = Stl Or FontStyle.Underline
                            fnt = New System.Drawing.Font(.FontFamily, .Size, Stl)
                        End With
                        .SelectionFont = fnt
                    Case Keys.A
                        .SelectAll()
                    Case Keys.Z
                        If .CanUndo Then .Undo()
                    Case Keys.Y
                        If .CanRedo Then .Redo()
                End Select
            End If
        End With
    End Sub

    Public Sub AddProperty(ByVal Name As String, ByVal Value As Object)
        Select Case Value.GetType.Name.ToLower
            Case "string"
                If Value.ToString = "" Then
                    Me.lvProperties.Items.Add(Translate(Me, Name)).SubItems.Add(Translate(Me, "Unknown"))
                Else
                        Me.lvProperties.Items.Add(Translate(Me, Name)).SubItems.Add(Value.ToString)
                    End If
            Case "boolean" 'int32 datetime
                    Me.lvProperties.Items.Add(Translate(Me, Name)).SubItems.Add(Translate(Me, Value.ToString))
                Case Else
                Me.lvProperties.Items.Add(Translate(Me, Name)).SubItems.Add(Value.ToString)
        End Select
    End Sub

    Public Sub AddProperty(Of CustomType)(ByVal Name As String, ByVal Value As Object)
        If Value.ToString.Length > 0 Then
                Dim Obj As CustomType = CType(Value, CustomType)
                Me.lvProperties.Items.Add(Translate(Me, Name)).SubItems.Add(Translate(Me, Obj.ToString))
            Else
            Me.lvProperties.Items.Add(Translate(Me, Name)).SubItems.Add(Translate(Me, "Unknown"))
        End If
    End Sub
        
    Public Sub New()

        ' This call is required by the Windows Form Designer.
        InitializeComponent()

        ' Add any initialization after the InitializeComponent() call.
        Me.chProperty.Text = Translate(Me, Me.chProperty.Text)
        Me.chValue.Text = Translate(Me, Me.chValue.Text)

    End Sub
End Class

End namespace