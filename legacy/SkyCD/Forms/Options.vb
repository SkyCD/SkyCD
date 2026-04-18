Imports System.IO
Imports SkyCD.App.Plugins

Namespace Forms

    Public Class Options

        Public Event CancelPressed()
        Public Event OKPressed()

        Dim PPath As String = Forms.Main.PlugInsSuport.Path

        Private Sub frmOptions_FormClosing(ByVal sender As Object, ByVal e As FormClosingEventArgs) Handles Me.FormClosing
            If Me.txtPlugIsPath.Text = "" Then
                Forms.Main.PlugInsSuport.Path = Me.PPath
            End If
        End Sub

        Private Sub frmOptions_Load(ByVal sender As Object, ByVal e As EventArgs) Handles MyBase.Load
            Me.txtPlugIsPath.Text = PPath
            Me.lvPlugIns.View = View.Details
            UpdatePlugInsList()
            Me.lstLanguages.Items.Clear()
            'Exit Sub
            Try
                Dim Dir As New DirectoryInfo(My.Application.Info.DirectoryPath & "\Languages")
                For Each That As FileInfo In Dir.GetFiles("*.xml")
                    Me.lstLanguages.Items.Add(That.Name.Substring(0, That.Name.Length - That.Extension.Length))
                Next
            Catch ex As Exception

            End Try
            Dim Fk As Integer = Me.lstLanguages.Items.IndexOf(modGlobal.Settings.ReadSetting("Language", , "English"))
            If Fk > -1 Then
                Me.lstLanguages.SelectedIndex = Fk
            Else
                Me.lstLanguages.SelectedIndex = 0
            End If
        End Sub

        Private Sub Cancel_Click(ByVal sender As Object, ByVal e As EventArgs) Handles Cancel.Click
            Me.txtPlugIsPath.Text = ""
            RaiseEvent CancelPressed()
            Me.Close()
        End Sub

        Private Sub cmdBrowsePlugInPath_Click(ByVal sender As Object, ByVal e As EventArgs) Handles cmdBrowsePlugInPath.Click
            Dim FS As New FolderBrowserDialog()
            With FS
                Dim path As String = Me.txtPlugIsPath.Text
                While Not Directory.Exists(path)
                    path = Directory.GetParent(path).FullName
                End While
                .SelectedPath = path
                .ShowNewFolderButton = False
                .Description = Translate(FS, "Select Plug-Ins Folder")
                If .ShowDialog = Windows.Forms.DialogResult.Cancel Then Exit Sub
                Me.txtPlugIsPath.Text = .SelectedPath
            End With
        End Sub

        Private Sub cmdRefreshPlugIns_Click(ByVal sender As Object, ByVal e As EventArgs) Handles cmdRefreshPlugIns.Click
            Forms.Main.PlugInsSuport.UpdatePlugInsList(False)
            UpdatePlugInsList()
        End Sub

        Public Sub UpdatePlugInsList()
            Me.lvPlugIns.Items.Clear()
            'Dim LV As ListViewItem
            Dim List() As PlugInInfo = Forms.Main.PlugInsSuport.GetPlugIns()
            If IsNothing(List) Then Exit Sub
            For Each That As PlugInInfo In List
                Try
                    Me.lvPlugIns.Items.Add(That.Name).SubItems.Add(That.Type)
                Catch ex As NullReferenceException

                End Try
            Next
        End Sub

        Public Sub New()

            ' This call is required by the Windows Form Designer.
            InitializeComponent()

            ' Add any initialization after the InitializeComponent() call.
            Me.lvPlugIns.Columns.Item(0).Width = CInt(Me.lvPlugIns.Width / 2)
            Me.lvPlugIns.Columns.Item(1).Width = CInt(Me.lvPlugIns.Width / 3)
        End Sub

        Private Sub lvPlugIns_Click(ByVal sender As Object, ByVal e As EventArgs) Handles lvPlugIns.Click
            Me.cmdConfigurePlugIn.Enabled = (New PlugInInfo(Me.lvPlugIns.SelectedItems.Item(0).Text)).HasConfig
        End Sub

        Private Sub cmdConfigurePlugIn_Click(ByVal sender As Object, ByVal e As EventArgs) Handles cmdConfigurePlugIn.Click
            Dim Dll As New PlugInsSupport()
            Dim plugin As Object = Dll.Load(Of Object)(Me.lvPlugIns.SelectedItems.Item(0).Text)
            plugin.GetType().GetMethod("ShowDialog").Invoke(plugin, Nothing)
        End Sub

        Private Sub txtPlugIsPath_TextChanged(ByVal sender As Object, ByVal e As EventArgs) Handles txtPlugIsPath.TextChanged
            Forms.Main.PlugInsSuport.Path = Me.txtPlugIsPath.Text
            Forms.Main.PlugInsSuport.UpdatePlugInsList(False)
            UpdatePlugInsList()
        End Sub

        Private Sub OK_Click(ByVal sender As Object, ByVal e As EventArgs) Handles OK.Click
            RaiseEvent OKPressed()
            Dim t As Boolean = (Me.lstLanguages.SelectedItem.ToString <> modGlobal.Settings.ReadSetting("Language", , "English").ToString)
            t = t Or (Forms.Main.PlugInsSuport.Path <> PPath)
            modGlobal.Settings.WriteSetting("Language", Me.lstLanguages.SelectedItem.ToString)
            Forms.Main.PlugInsSuport.Path = Me.txtPlugIsPath.Text
            Me.Close()
            If t Then
                If MsgBox(Translate(Me, "Because you have changed some setting, this application must restart. Do you want restart now?"), MsgBoxStyle.YesNo Or MsgBoxStyle.Question, Translate(Me, "Restart")) = MsgBoxResult.Yes Then
                    Application.Restart()
                Else
                    MsgBox(Translate(Me, "New settings will be applied after application restart"), MsgBoxStyle.Information Or MsgBoxStyle.OkOnly, Translate(Me, "Restart"))
                End If
            End If
        End Sub

    End Class


End Namespace