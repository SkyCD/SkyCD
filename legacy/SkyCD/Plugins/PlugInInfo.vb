Namespace Plugins
    Public Class PlugInInfo

        Private Settings As New SCD_XSettings("SkyCD PlugIns")
        Protected Friend Name As String

        Sub New(ByVal Name As String)
            Me.Name = Name
        End Sub

        Public ReadOnly Property FileName() As String
            Get
                Return Settings.ReadSetting("FileName", "PlugIn." + Me.Name).ToString
            End Get
        End Property

        Public ReadOnly Property FullInfo() As String
            Get
                Return Settings.ReadSetting("FullInfo", "PlugIn." + Me.Name).ToString
            End Get
        End Property

        Public ReadOnly Property Type() As String
            Get
                Select Case CInt(Settings.ReadSetting("Type", "PlugIn." + Me.Name))
                    Case 1
                        Return Translate(Me, "File Format Support")
                    Case 2
                        Return Translate(Me, "Interface PlugIn")
                End Select
                Return Translate(Me, "Unknown")
            End Get
        End Property

        Public ReadOnly Property CanOpenSize() As String
            Get
                Return Settings.ReadSetting("CanOpenSize", "PlugIn." + Me.Name).ToString
            End Get
        End Property

        Public ReadOnly Property CanSaveSize() As String
            Get
                Return Settings.ReadSetting("CanSaveSize", "PlugIn." + Me.Name).ToString
            End Get
        End Property

        Public ReadOnly Property CanReadExtendedInfo() As String
            Get
                Return Settings.ReadSetting("CanReadExtendedInfo", "PlugIn." + Me.Name).ToString
            End Get
        End Property

        Public ReadOnly Property CanSaveExtentedInfo() As String
            Get
                Return Settings.ReadSetting("CanSaveExtentedInfo", "PlugIn." + Me.Name).ToString
            End Get
        End Property

        Public ReadOnly Property IsExactFormat() As String
            Get
                Return Settings.ReadSetting("IsExactFormat", "PlugIn." + Me.Name).ToString
            End Get
        End Property

        Public ReadOnly Property ProcessorArchitecture() As String
            Get
                Return Settings.ReadSetting("ProcessorArchitecture", "PlugIn." + Me.Name).ToString
            End Get
        End Property

        Public ReadOnly Property ImageRuntimeVersion() As String
            Get
                Return Settings.ReadSetting("ImageRuntimeVersion", "PlugIn." + Me.Name).ToString
            End Get
        End Property

        Public ReadOnly Property HasConfig() As Boolean
            Get
                Return Convert.ToBoolean(Settings.ReadSetting("HasConfig", "PlugIn." + Me.Name, False))
            End Get
        End Property

    End Class
End Namespace
