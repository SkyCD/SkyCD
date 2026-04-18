Imports SkyCD.App.Forms

Public Module Startup

    Function Open() As Main
        Dim Km As New Main
        Km.Show()
        Dim FileName As String = Nothing
        If My.Application.CommandLineArgs.Count > 0 Then _
            FileName = My.Application.CommandLineArgs.Item(0)
        If (FileName Is Nothing) = False Then
            Dim nfo As New System.IO.FileInfo(FileName)
            If nfo.Exists Then
                Km.LoadFile(FileName)
            Else
                'MsgBox(Translate(Km, "Can't open selected file"), MsgBoxStyle.Exclamation, Translate(Km, "Error"))
            End If
        End If
        Return Km
    End Function



End Module
