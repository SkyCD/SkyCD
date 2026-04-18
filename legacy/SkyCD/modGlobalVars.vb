Module modGlobal

    Public Settings As New SCD_XSettings(My.Application.Info.AssemblyName)
    Public Language As New clsXMLCfgFile(My.Application.Info.DirectoryPath & "\Languages\" & Settings.ReadSetting("Language", , "English").ToString & ".xml", "language")
    Public CanCreateForms As Boolean = True

    Public ReadOnly Property Translate(ByVal Obj As Object, ByVal Text As String, Optional ByVal DefaultValue As String = Nothing) As String
        Get
            'Return DefaultValue
            'Exit Function
            If IsNothing(DefaultValue) Then DefaultValue = Text
            Text = System.Text.RegularExpressions.Regex.Replace(Text, "[^A-z]+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            Dim Val As Collection = Language.GetConfigInfo(Obj.GetType.Name, Text, DefaultValue)
            Dim Rez As String = DefaultValue
            If Val.Count = 0 Then GoTo NxT
            If Val.Count = 1 Then
                If Val(1).ToString = DefaultValue Then GoTo NxT
                Rez = Val(1).ToString
            Else
                Dim I As Integer
                For I = 1 To Val.Count
                    Rez = Rez & Val(I).ToString & vbCrLf
                Next
            End If
            Return Rez
NxT:
            Language.WriteConfigInfo(Obj.GetType.Name, Text, DefaultValue)
            Return DefaultValue
        End Get
    End Property

End Module
