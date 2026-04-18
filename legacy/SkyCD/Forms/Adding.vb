Namespace Forms

    Public Class Adding

        Private DidList As New Collection

        Public Event CancelWork(ByVal DidList As Collection)

        Private Sub frmAdding_Load(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles MyBase.Load
            Me.DidList.Clear()
        End Sub

        Public Sub DoIt(ByVal Message As String, ByVal Code As Object)
            Me.lblAction.Text = Message
            Me.DidList.Add(Code)
        End Sub

        Dim X As Single, Y As Single

        Private Sub frmAdding_MouseDown(ByVal sender As Object, ByVal e As MouseEventArgs) Handles Me.MouseDown
            If e.Button = Windows.Forms.MouseButtons.Left Then
                X = e.X
                Y = e.Y
            End If
        End Sub

        Private Sub frmAdding_MouseMove(ByVal sender As Object, ByVal e As MouseEventArgs) Handles Me.MouseMove
            If e.Button = Windows.Forms.MouseButtons.Left Then
                Me.Left = Me.Left + CInt(e.X - Me.X)
                Me.Top = Me.Top + CInt(e.Y - Me.Y)
            End If
        End Sub
    End Class


End Namespace