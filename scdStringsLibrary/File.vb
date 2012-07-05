Public Class File

    Public Shared Function GetFileExtension(ByVal FileName As String) As String
        If InStrRev(FileName, ".") > -1 Then
            Return Right(FileName, Len(FileName) - InStrRev(FileName, ".") + 1)
        End If
        Return ""
    End Function

    Public Shared Function GetFileIcon(ByVal FileName As String) As System.Drawing.Icon
        Dim FL As New System.IO.FileInfo(My.Application.Info.DirectoryPath & "\" & FileName)
        Dim Img As System.Drawing.Icon = Nothing
        Try
            If Not FL.Exists Then FL.Create().Close()
            Img = System.Drawing.Icon.ExtractAssociatedIcon(My.Application.Info.DirectoryPath & "\" & FileName)
            'Dim Img As System.Drawing.Image = System.Drawing.Image.FromHbitmap(Ico.ToBitmap.GetHbitmap)
            FL.Delete()
        Catch ex As Exception

        End Try
        Return Img
    End Function

    Public Shared Function GetFileIcon2(ByVal FileName As String) As String
        Dim File As New System.IO.MemoryStream()
        System.Drawing.Icon.ExtractAssociatedIcon(FileName).ToBitmap.Save(File, System.Drawing.Imaging.ImageFormat.Png)
        File.Seek(0, IO.SeekOrigin.Begin)
        Return System.Convert.ToBase64String(File.GetBuffer)
    End Function

    Public Shared Function GetFileIcon3(ByVal IconText As String) As System.Drawing.Image
        Dim File As New System.IO.MemoryStream(System.Convert.FromBase64String(IconText))
        Return New System.Drawing.Bitmap(File)                    
    End Function

End Class
