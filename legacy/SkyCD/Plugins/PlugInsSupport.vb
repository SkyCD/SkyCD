Imports SkyCD.App.Forms
Imports SkyCD.AdvancedFunctions.File

Namespace Plugins

    Public Class PlugInsSupport
        'Inherits System.ComponentModel.Component

        Private Settings As New SCD_XSettings("SkyCD PlugIns")

        Friend LoadedPlugIn As String = ""

        Public Property Path() As String
            Get
                Return modGlobal.Settings.ReadSetting("PlugInsPath", , My.Application.Info.DirectoryPath + "\Plug-Ins\").ToString
            End Get
            Set(ByVal value As String)
                'MsgBox(Path)
                modGlobal.Settings.WriteSetting("PlugInsPath", value)
            End Set
        End Property

        Public Function GetPlugIns() As PlugInInfo()
            Dim Kx As Object(,) = Settings.ReadSettings("PlugIns")
            If IsNothing(Kx) Then Return Nothing
            Dim I As Integer = UBound(Kx)
            Dim Mas(I) As PlugInInfo
            For I = LBound(Kx) To UBound(Kx)
                Mas(I) = New PlugInInfo(Kx(I, 0).ToString)
            Next
            Return Mas
        End Function

        Public Function Exists(ByVal Name As String) As Boolean
            Return Settings.ReadSetting("PlugIn." + Name, "FileName", "").ToString <> ""
        End Function

        Public Sub LoadStartupPlugIns(ByVal FormX As Main)
            Try
                Dim Kx As String(,) = CType(Me.Settings.ReadSettings("StartUp"), String(,))
                Dim I As Integer = UBound(Kx)
                For I = LBound(Kx) To UBound(Kx)
                    Me.Load(Of iInterfacePlugIn)(Kx(I, 0)).Create(DirectCast(FormX, iMainForm))
                Next
            Catch ex As Exception

            End Try
        End Sub

        Public Sub Add(ByVal FileName As String)
            Dim Dll As System.Reflection.Assembly = System.Reflection.Assembly.LoadFrom(FileName)
            Dim Instance As Object = Nothing
            For Each type As Reflection.TypeInfo In Dll.DefinedTypes
                If Not (type.Name = "Main" And type.IsClass And Not type.IsAbstract And type.IsPublic) Then
                    Continue For
                End If
                Instance = Activator.CreateInstance(type)
                If Instance Is Nothing Then
                    Continue For
                ElseIf IsNothing(Instance.GetType.GetInterface("iFileFormat", True)) = False Then
                    Settings.WriteSetting("PlugIns", Dll.GetName.Name, Dll.GetName.FullName)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "FileName", FileName)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "HasConfig", DirectCast(Instance, iFileFormat).HasConfig)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "ImageRuntimeVersion", Dll.ImageRuntimeVersion)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "ProcessorArchitecture", Dll.GetName.ProcessorArchitecture)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "Type", 1)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "CanOpenSize", DirectCast(Instance, iFileFormat).CanOpenSize)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "CanSaveSize", DirectCast(Instance, iFileFormat).CanSaveSize)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "CanReadExtendedInfo", DirectCast(Instance, iFileFormat).CanReadExtendedInfo)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "CanSaveExtentedInfo", DirectCast(Instance, iFileFormat).CanSaveExtentedInfo)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "IsExactFormat", DirectCast(Instance, iFileFormat).IsExactFormat)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "Entry", type.FullName)
                    Dim FK() As String
                    For Each Item As String In DirectCast(Instance, iFileFormat).GetSupportedFileFormats
                        FK = Item.Split("|")
                        Settings.WriteSetting("FileFormats", FK(0), FK(1))
                        Settings.WriteSetting("FileFormat." + FK(0), "Handler", Dll.GetName.Name)
                        Settings.WriteSetting("FileFormat." + FK(0), "CanDo", DirectCast(Instance, iFileFormat).CanDo.ToString)
                    Next
                    'Tkx1.SupportedFormats = "All files|*.*"
                    'Me.Settings.WriteSetting(Dll.GetName.Name, SkyAdvancedFunctionsLibrary.Strings.SerializeToText(Tkx1), True)            
                ElseIf IsNothing(Instance.GetType.GetInterface("iInterfacePlugIn", True)) = False Then
                    Settings.WriteSetting("PlugIns", Dll.GetName.Name, Dll.GetName.FullName)
                    Settings.WriteSetting("StartUp", Dll.GetName.Name, Dll.GetName.FullName)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "FileName", FileName)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "HasConfig", DirectCast(Instance, iInterfacePlugIn).HasConfig)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "ImageRuntimeVersion", Dll.ImageRuntimeVersion)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "ProcessorArchitecture", Dll.GetName.ProcessorArchitecture)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "Type", 2)
                    Settings.WriteSetting("PlugIn." + Dll.GetName.Name, "Entry", type.FullName)
                End If
            Next
        End Sub

        Public Function GetSupportedFileFormatsForRead() As String
            Dim data(,) = Settings.ReadSettings("FileFormats")
            If data Is Nothing Then
                Return ""
            End If
            Dim I As Integer, Supported As String = "", Supported2 As String = ""
            For I = LBound(data) To UBound(data)
                If InStr(Settings.ReadSetting("CanDo", "FileFormat." & data(I, 0).ToString).ToString.ToLower, "read") > -1 Then
                    Supported = Supported & data(I, 0).ToString & " (" & data(I, 1).ToString & ")" & "|" & data(I, 1).ToString & "|"
                    If InStr(Supported2, data(I, 1).ToString & ";") < 1 Then
                        Supported2 = Supported2 & data(I, 1).ToString & ";"
                    End If
                End If
            Next
            Supported2 = Left(Supported2, Supported2.Length - 1)
            Return Supported & "All Supported File Formats (" & Supported2 & ")|" & Supported2
        End Function

        Public Function GetSupportedFileFormatsForWrite() As String
            Dim data(,) = Settings.ReadSettings("FileFormats")
            If data Is Nothing Then
                Return ""
            End If
            Dim I As Integer, Supported As String = ""
            For I = LBound(data) To UBound(data)
                If InStr(Settings.ReadSetting("CanDo", "FileFormat." & data(I, 0).ToString).ToString.ToLower, "write") > -1 Then
                    Supported = Supported & data(I, 0).ToString & " (" & data(I, 1).ToString & ")" & "|" & data(I, 1).ToString & "|"
                End If
            Next
            Return Left(Supported, Supported.Length - 1)
        End Function

        Public Sub UpdatePlugInsList(Optional ByVal CheckCount As Boolean = True)
            Dim Katalogas As New System.IO.DirectoryInfo(Me.Path)
            If Katalogas.Exists = False Then Exit Sub
            Dim Files() As System.IO.FileInfo = Katalogas.GetFiles("*.dll", IO.SearchOption.AllDirectories)
            If CheckCount And UBound(Files) = CInt(Me.Settings.ReadSetting("DLLsCount", "Default", 0)) Then
                Exit Sub
            End If
            Dim I As Integer
            Dim Kx As Object(,) = Settings.ReadSettings("PlugIns")
            Me.Settings.DeleteSetting("StartUp")
            If IsNothing(Kx) = False Then
                For I = LBound(Kx) To UBound(Kx)
                    Me.Settings.DeleteSetting("PlugIns", Kx(I, 0).ToString)
                    Me.Settings.DeleteSetting("PlugIn." + Kx(I, 0).ToString)
                Next
                Kx = Me.Settings.ReadSettings("FileFormats")
                For I = LBound(Kx) To UBound(Kx)
                    Me.Settings.DeleteSetting("FileFormat." + Kx(I, 0).ToString)
                Next
                Me.Settings.DeleteSetting("FileFormats")
            End If
            For Each File As System.IO.FileInfo In Files
                Try
                    Me.Add(File.FullName)
                Catch ex As Exception

                End Try
            Next
            Me.Settings.WriteSetting("DLLsCount", UBound(Files))
        End Sub

        Public Function GetHandlerForFile(ByVal FileName As String) As iFileFormat
            Dim data(,) = Settings.ReadSettings("FileFormats")
            If data Is Nothing Then
                Return Nothing
            End If
            Dim C As New Collection
            Dim I As Integer
            For I = LBound(data) To UBound(data)
                If GetFileExtension(data(I, 1).ToString) = GetFileExtension(FileName) Then
                    'MsgBox(SkyAdvancedFunctionsLibrary.File.GetFileExtension(data(I, 1)))
                    If InStr(Settings.ReadSetting("CanDo", "FileFormat." & data(I, 0).ToString).ToString.ToLower, "read") > -1 Then
                        C.Add(data(I, 0))
                    End If
                End If
            Next
            Dim dll As iFileFormat, Hl As String = ""
            For I = 1 To C.Count
                Hl = Settings.ReadSetting("Handler", "FileFormat." + C.Item(I).ToString).ToString
                If Hl.Length > 0 Then
                    dll = Me.Load(Of iFileFormat)(Hl)
                    If dll.IsSupported(FileName) Then Return dll
                End If
            Next
            Return Nothing
        End Function

        Public Function GetHandlerForFilterIndex(ByVal Index As Integer) As iFileFormat
            Dim data(,) = Settings.ReadSettings("FileFormats")
            Dim I As Integer
            Dim C As New Collection
            For I = LBound(data) To UBound(data)
                If InStr(Settings.ReadSetting("CanDo", "FileFormat." & data(I, 0).ToString).ToString.ToLower, "write") > -1 Then C.Add(data(I, 0))
            Next
            If C.Count < 1 Then Return Nothing
            Dim Hl As String = Settings.ReadSetting("Handler", "FileFormat." + C.Item(Index).ToString).ToString
            '        MsgBox(Hl)
            If Hl.Length > 0 Then
                If Hl.Length > 0 Then
                    Return Me.Load(Of iFileFormat)(Hl)
                End If
            End If
            Return Nothing
        End Function

        Public Function Load(Of Type)(ByVal Name As String) As Type
            If Name.Trim = "" Then Return Nothing
            Dim FileName As String = Settings.ReadSetting("FileName", "PlugIn." + Name).ToString
            Dim Entry As String = Settings.ReadSetting("Entry", "PlugIn." + Name).ToString
            Dim Dll As System.Reflection.Assembly = System.Reflection.Assembly.LoadFrom(FileName)
            Dim Instance As Object = Dll.CreateInstance(Entry, True)
            Me.LoadedPlugIn = Name
            Return CType(Instance, Type)
        End Function

    End Class

End Namespace