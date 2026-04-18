Imports SkyCD.AdvancedFunctions.File
Imports SkyCD.Simple
Imports SkyCD.Simple.skycd_simple
Imports System.IO
Imports SkyCD.App.Plugins
Imports Convert2 = SkyCD.AdvancedFunctions.Convert
Imports SkyCD.AdvancedFunctions.Network.FTP

Namespace Forms

    Public Class Main
        Implements iMainForm

        Private SortBy As String = "type"

        Public WithEvents FileName As String = ""

        Private WithEvents FileStream As iFileFormat = Nothing

        Private WithEvents DBConnection As DatabaseProxy

        'Friend Settings As New SCD_XSettings(My.Application.Info.AssemblyName)
        Public PlugInsSuport As New PlugInsSupport()

        Public ReadOnly Property Toolbar As Object Implements iMainForm.Toolbar
            Get
                Return tsToolBarFile
            End Get
        End Property

        Private Overloads ReadOnly Property Menu As Object Implements iMainForm.Menu
            Get
                Return Me.MenuStrip1
            End Get
        End Property

        Private Sub FileStream_DebugWrite(ByVal Text As Object) Handles FileStream.DebugWrite
            Debug.Print(Text.ToString)
        End Sub

        Private Sub FileStream_NeedDoEvents() Handles FileStream.NeedDoEvents
            Application.DoEvents()
        End Sub

        Private Sub FileStream_UpdateStatus(ByVal e As iFileFormat.scdStatus) Handles FileStream.UpdateStatus
            'Debug.Print(e.scdEvent.ToString)        
            Select Case e.scdEvent
                Case iFileFormat.scdStatus.scdProcedure.scdLoading
                    If Not Me.tsProgressBar.Visible Then
                        Me.StatusStrip1.Items(0).Text = modGlobal.Translate(Me, "Loading...")
                        Me.tsProgressBar.Minimum = 0
                        Me.tsProgressBar.Maximum = 100
                        Me.tsProgressBar.Value = 0
                        Me.tsProgressBar.Visible = True
                        Me.tsProgressBar.Width = Me.StatusStrip1.Width - Me.StatusStrip1.Items(0).Width - 10
                    Else
                        Me.tsProgressBar.Value = e.scdValue
                    End If
                    Exit Select
                Case iFileFormat.scdStatus.scdProcedure.scdSaving
                    If Not Me.tsProgressBar.Visible Then
                        Me.StatusStrip1.Items(0).Text = modGlobal.Translate(Me, "Saving...")
                        Me.tsProgressBar.Minimum = 0
                        Me.tsProgressBar.Maximum = 100
                        Me.tsProgressBar.Value = 0
                        Me.tsProgressBar.Visible = True
                        Me.tsProgressBar.Style = ProgressBarStyle.Blocks
                        Me.tsProgressBar.Width = Me.StatusStrip1.Width - Me.StatusStrip1.Items(0).Width - 10
                    Else
                        Me.tsProgressBar.Value = e.scdValue
                    End If
                    Exit Select
                Case iFileFormat.scdStatus.scdProcedure.scdDone
                    Me.tsProgressBar.Visible = False
                    Me.tsProgressBar.Style = ProgressBarStyle.Blocks
                    Me.StatusStrip1.Items(0).Text = modGlobal.Translate(Me, "Done.")
                    Exit Select
                Case iFileFormat.scdStatus.scdProcedure.scdExporting
                    If Me.StatusStrip1.Items(0).Text <> modGlobal.Translate(Me, "Updating...") Then
                        Me.StatusStrip1.Items(0).Text = modGlobal.Translate(Me, "Updating...")
                        Me.tsProgressBar.Minimum = 0
                        Me.tsProgressBar.Maximum = 100
                        Me.tsProgressBar.Value = 0
                        Me.tsProgressBar.Visible = True
                        Me.tsProgressBar.Width = Me.StatusStrip1.Width - Me.StatusStrip1.Items(0).Width - 10
                        Application.DoEvents()
                    Else
                        Me.tsProgressBar.Value = e.scdValue
                    End If
                    Exit Select
                Case iFileFormat.scdStatus.scdProcedure.scdImporting
                    If Me.StatusStrip1.Items(0).Text <> modGlobal.Translate(Me, "Parsing...") Then
                        Me.StatusStrip1.Items(0).Text = modGlobal.Translate(Me, "Parsing...")
                        Me.tsProgressBar.Minimum = 0
                        Me.tsProgressBar.Maximum = 100
                        Me.tsProgressBar.Value = 0
                        Me.tsProgressBar.Visible = True
                        Me.tsProgressBar.Width = Me.StatusStrip1.Width - Me.StatusStrip1.Items(0).Width - 10
                        Me.tsProgressBar.Style = ProgressBarStyle.Blocks
                        Application.DoEvents()
                    Else
                        Me.tsProgressBar.Value = e.scdValue
                    End If
                    Exit Select
            End Select
        End Sub

        Private Sub SplitContainer1_SplitterMoved(ByVal sender As Object, ByVal e As SplitterEventArgs) Handles SplitContainer1.SplitterMoved
            Me.lvBrowse.Width = Me.SplitContainer1.Panel2.Width
            Me.tvTree.Width = Me.SplitContainer1.Panel1.Width
        End Sub

        Private Sub Form1_FormClosing(ByVal sender As Object, ByVal e As FormClosingEventArgs) Handles Me.FormClosing
            If Me.saveToolStripButton.Enabled Then
                Dim Rez As MsgBoxResult = MsgBox(modGlobal.Translate(Me, "This collection was edited. Do you want to save it?"), MsgBoxStyle.YesNoCancel Or MsgBoxStyle.Exclamation)
                If Rez = MsgBoxResult.Cancel Then Exit Sub
                If Rez = MsgBoxResult.Yes Then Me.SaveToolStripMenuItem_Click(sender, e)
            End If
            Me.DBConnection.Execute("DELETE FROM list WHERE AID = ?", Me.Handle.ToInt64)
        End Sub

        Private Sub Form1_Load(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Load

            Me.DBConnection = New DatabaseProxy(My.Application.Info.DirectoryPath + "\data.db")
            ''Me.lvBrowse.DataBindings.Add()

            Me.Left = CInt(modGlobal.Settings.ReadSetting("Left", "Window", Me.Left))
            Me.Top = CInt(modGlobal.Settings.ReadSetting("Top", "Window", Me.Top))
            Me.Width = CInt(modGlobal.Settings.ReadSetting("Width", "Window", Me.Width))
            Me.Height = CInt(modGlobal.Settings.ReadSetting("Height", "Window", Me.Height))

            Me.StatusStrip1.Visible = System.Convert.ToBoolean(modGlobal.Settings.ReadSetting("StatusBar", "Window", Me.StatusStrip1.Visible))
            Me.StatusBarToolStripMenuItem.Checked = Me.StatusStrip1.Visible

            Select Case modGlobal.Settings.ReadSetting("ListView/View", "Window", "largeicon").ToString.ToLower
                Case "details"
                    Me.DetailsToolStripMenuItem.Checked = True
                    Me.TilesToolStripMenuItem.Checked = False
                    Me.IconsToolStripMenuItem.Checked = False
                    Me.ListToolStripMenuItem.Checked = False
                    Me.LargeIconsToolStripMenuItem.Checked = False
                    Me.lvBrowse.View = View.Details
                Case "list"
                    Me.DetailsToolStripMenuItem.Checked = False
                    Me.TilesToolStripMenuItem.Checked = False
                    Me.IconsToolStripMenuItem.Checked = False
                    Me.ListToolStripMenuItem.Checked = True
                    Me.LargeIconsToolStripMenuItem.Checked = False
                    Me.lvBrowse.View = View.List
                Case "tile"
                    Me.DetailsToolStripMenuItem.Checked = False
                    Me.TilesToolStripMenuItem.Checked = True
                    Me.IconsToolStripMenuItem.Checked = False
                    Me.ListToolStripMenuItem.Checked = False
                    Me.LargeIconsToolStripMenuItem.Checked = False
                    Me.lvBrowse.View = View.Tile
                Case "smallicon"
                    Me.DetailsToolStripMenuItem.Checked = False
                    Me.TilesToolStripMenuItem.Checked = False
                    Me.IconsToolStripMenuItem.Checked = True
                    Me.ListToolStripMenuItem.Checked = False
                    Me.LargeIconsToolStripMenuItem.Checked = False
                    Me.lvBrowse.View = View.SmallIcon
                Case "largeicon"
                    Me.DetailsToolStripMenuItem.Checked = False
                    Me.TilesToolStripMenuItem.Checked = False
                    Me.IconsToolStripMenuItem.Checked = False
                    Me.ListToolStripMenuItem.Checked = False
                    Me.LargeIconsToolStripMenuItem.Checked = True
                    Me.lvBrowse.View = View.LargeIcon
            End Select

            Select Case modGlobal.Settings.ReadSetting("ListView/SortBy", "Window", "name").ToString
                Case "name"
                    Me.FileNameToolStripMenuItem.Checked = True
                    Me.TypeToolStripMenuItem.Checked = False
                    Me.SortBy = "name"
                Case "type"
                    Me.FileNameToolStripMenuItem.Checked = False
                    Me.TypeToolStripMenuItem.Checked = True
                    Me.SortBy = "type"
                Case "size"
                    Me.FileNameToolStripMenuItem.Checked = False
                    Me.TypeToolStripMenuItem.Checked = False
                    Me.SortBy = "size"
            End Select

            Me.SaveToolStripMenuItem.Enabled = False
            Me.SaveAsToolStripMenuItem.Enabled = False

        End Sub

        Private Sub Form1_Resize(ByVal sender As Object, ByVal e As EventArgs) Handles Me.Resize
            'Me.lvBrowse.Width = Me.SplitContainer1.Panel2.Width
            'Me.tvTree.Width = Me.SplitContainer1.Panel1.Width
            'Me.tvTree.Height = Me.SplitContainer1.Panel1.Height
            'Me.lvBrowse.Height = Me.SplitContainer1.Panel2.Height
            Me.tvTree.Top = 0
            Me.lvBrowse.Top = 0
            Me.tvTree.Left = 0
            Me.lvBrowse.Left = 0
        End Sub

        Private Sub OpenToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles OpenToolStripMenuItem.Click
            Dim dr As New DialogResult
            Dim ofdOpen As New OpenFileDialog()
            Me.PlugInsSuport.UpdatePlugInsList()
            ofdOpen.Filter = Me.PlugInsSuport.GetSupportedFileFormatsForRead()
            ofdOpen.Title = Translate(ofdOpen, "Open")
            ofdOpen.FilterIndex = CInt(modGlobal.Settings.ReadSetting("FilterIndex", "OpenDialog", 1))
            dr = ofdOpen.ShowDialog()
            If dr = DialogResult.Cancel Then Exit Sub
            'Me.FileStream = me.PlugInsSuport.GetHandlerForFile   System.Reflection.Assembly.LoadFrom(My.Application.Info.DirectoryPath & "\..\Plug-Ins\FileSupport_TextFormat.dll").CreateInstance("Main", True)
            Me.FileStream = Me.PlugInsSuport.GetHandlerForFile(ofdOpen.FileName)
            If IsNothing(Me.FileStream) Then
                MsgBox(modGlobal.Translate(Me, "Bad file format!"), MsgBoxStyle.Exclamation, modGlobal.Translate(Me, "Error"))
                Exit Sub
            End If
            modGlobal.Settings.WriteSetting("OpenDialog", "FilterIndex", ofdOpen.FilterIndex)
            Me.LoadFile(ofdOpen.FileName)
            ''MsgBox(TF.IsSupported(Me.ofdOpen.FileName), MsgBoxStyle.Information)
        End Sub

        Public Sub LoadFile(ByVal FileName As String)
            'Me.PlugInsSuport.UpdatePlugInsList()
            Me.FileStream = Me.PlugInsSuport.GetHandlerForFile(FileName)
            If Me.FileStream Is Nothing Then Exit Sub
            Me.MainMenuStrip.Enabled = False
            Me.tvTree.Enabled = False
            Me.tsToolBarFile.Enabled = False
            Me.tsToolBarFile.Enabled = False
            Me.lvBrowse.Enabled = False
            Me.ToolStripStatusLabel1.Text = Translate(Me, "Loading...")
            Me.FileStream.Database = Me.DBConnection
            Me.FileStream.ApplicationGUID = Me.Handle.ToInt64.ToString
            Me.tvTree.Nodes.Clear()
            Me.lvBrowse.Items.Clear()
            Me.FileStream.Load(FileName)
            Me.UpdateTree()
            Me.FileName = FileName
            Dim FI As New IO.FileInfo(Me.FileName)
            Me.Text = "SkyCD - " + FI.Name
            FI = Nothing
            If Me.tvTree.Nodes.Count > 0 Then
                Me.tvTree.SelectedNode = Me.tvTree.Nodes.Item(0)
                Me.tvTree.SelectedNode.Expand()
            End If
            FileStream = Nothing
            Me.SaveToolStripMenuItem.Enabled = False
            Me.SaveAsToolStripMenuItem.Enabled = True
            Me.ToolStripStatusLabel1.Text = Translate(Me, "Done.")
            Me.MainMenuStrip.Enabled = True
            Me.tvTree.Enabled = True
            Me.tsToolBarFile.Enabled = True
            Me.tsToolBarFile.Enabled = True
            Me.lvBrowse.Enabled = True
        End Sub

        Public Function UpdateList(Optional ByVal ParentID As Integer = -1) As Boolean
            'On Error Resume Next
            Dim Ext As String
            Me.StatusStrip1.Text = modGlobal.Translate(Me, "Reading directory content...")
            Me.StatusStrip1.Items(0).Text = modGlobal.Translate(Me, "Reading directory content...")
            'Me.tsProgressBar.Width = Me.StatusStrip1.Width - Me.StatusStrip1.Items(0).Width - 10
            'Me.tsProgressBar.Visible = True
            'Me.tsProgressBar.Value = 0        
            'Me.odbcDatabase.SelectCommand.Cancel()
            Dim sql As String = "SELECT * FROM list WHERE ParentID = ? AND AID = ? ORDER BY "
            If Me.SortBy.ToLower = "type" Then
                sql += "Type DESC"
            Else
                sql += Me.SortBy
            End If
            Me.lvBrowse.Items.Clear()
            'Me.lvBrowse.LargeImageList = Nothing
            'Me.imlBrowseIcons.Images.Clear()           
            'If Me.imlBrowseIcons.Images.IndexOfKey("FOLDER") Then
            ' Me.imlBrowseIcons.Images.Add("FOLDER", My.Resources.NeededIcons.scdFolder)
            'End If
            Me.tsProgressBar.Maximum = 1
            Dim Nfo As scdProperties
            Dim tvar As String
            For Each item As Database.Item In Me.DBConnection.Select(sql, ParentID.ToString, Me.Handle.ToInt64)
                Select Case item.Type
                    Case "scdFile"
                        Ext = GetFileExtension(item.Name)
                        Nfo = New scdProperties(item.Properties)
                        tvar = Me.imlBrowseIcons.Images.IndexOfKey(item.Type & "/../" & item.Name).ToString
                        If Nfo.Item("Icon").ToString <> "" Then
                            If System.Convert.ToDouble(tvar) < 0 Then Me.imlBrowseIcons.Images.Add(tvar, GetFileIcon3(Nfo.Item("Icon").ToString))
                            Me.lvBrowse.Items.Add(item.ID.ToString, item.Name, item.Type & "/../" & item.Name).SubItems.Add(Convert2.Long2Size(DirectCast(Nfo.Item("Size"), Long)))
                        Else
                            If Me.imlBrowseIcons.Images.IndexOfKey(item.Type & "/" & Ext) < 0 Then
                                Me.imlBrowseIcons.Images.Add(item.Type & "/" & Ext, GetFileIcon3(item.Name.ToString))
                            End If
                            Me.lvBrowse.Items.Add(item.ID.ToString, item.Name, item.Type + "/" + Ext).SubItems.Add(Convert2.Long2Size(DirectCast(Nfo.Item("Size"), Long)))
                        End If
                    Case Else
                        If Not Me.imlBrowseIcons.Images.ContainsKey(item.Type) Then
                            Me.imlBrowseIcons.Images.Add(item.Type, DirectCast(My.Resources.NeededIcons.ResourceManager.GetObject(item.Type), Image))
                        End If
                        Me.lvBrowse.Items.Add(item.ID.ToString, item.Name, item.Type)
                End Select
                'Me.tsProgressBar.Value = Me.tsProgressBar.Value + 1
                'Me.tsProgressBar.Maximum = Me.tsProgressBar.Maximum + 1
            Next
            If Me.lvBrowse.Items.Count > 0 Then
                Me.lvBrowse.SelectedItems.Clear()
                Me.lvBrowse.FocusedItem = Me.lvBrowse.Items(0)
                Me.lvBrowse.Select()
            End If
            'MsgBox(Me.lvBrowse.Items(0).ImageKey)
            'Me.lvBrowse.LargeImageList = Me.imlBrowseIcons
            'Me.lvBrowse.SmallImageList = Me.imlBrowseIcons
            Me.tsProgressBar.Visible = False
            Me.StatusStrip1.Text = modGlobal.Translate(Me, "Done.")
            Me.StatusStrip1.Items(0).Text = modGlobal.Translate(Me, "Done.")
            Me.tvTree.SelectedNode.Expand()
            Return True
        End Function

        Public Sub UpdateTree(Optional ByVal ParentID As Integer = -1)
            Dim tnode As TreeNode = Nothing
            Dim tnodes() As TreeNode
            Dim chk As New Collection
            Me.StatusStrip1.Text = modGlobal.Translate(Me, "Reading directory tree...")
            Me.StatusStrip1.Items(0).Text = modGlobal.Translate(Me, "Reading directory tree...")
            'Me.tsProgressBar.Visible = True
            'Me.tsProgressBar.Value = 0        
            Dim sql As String = "SELECT * FROM list WHERE ParentID = ? AND (NOT (Type = ?)) AND AID = ?"
            If Not (ParentID = -1) Then
                tnodes = Me.tvTree.Nodes.Find(ParentID.ToString, True)
                tnode = tnodes(0)
            End If
            Me.tsProgressBar.Style = ProgressBarStyle.Marquee
            For Each item As Database.Item In Me.DBConnection.Select(sql, ParentID, "scdFile", Me.Handle.ToInt64)
                If Not Me.imlTreeIcons.Images.ContainsKey(item.Type) Then
                    Me.imlTreeIcons.Images.Add(item.Type, DirectCast(My.Resources.NeededIcons.ResourceManager.GetObject(item.Type), Image))
                End If
                If ParentID = -1 Then
                    Me.tvTree.Nodes.Add(item.ID.ToString, item.Name, item.Type, item.Type).EnsureVisible()
                Else
                    tnode.Nodes.Add(item.ID.ToString, item.Name, item.Type, item.Type).EnsureVisible()
                End If
                'Me.tsProgressBar.Value = Me.tsProgressBar.Value + 0.5
                chk.Add(item.ID.ToString)
            Next
            For Each This As String In chk
                tnodes = Me.tvTree.Nodes.Find(This, True)
                If IsNothing(tnodes) Then
                    Exit For
                End If
                tnode = tnodes(LBound(tnodes))
                For Each item As Database.Item In Me.DBConnection.Select(sql, This.ToString, "scdFile", Me.Handle.ToInt64)
                    If Not Me.imlTreeIcons.Images.ContainsKey(item.Type) Then
                        Me.imlTreeIcons.Images.Add(item.Type, DirectCast(My.Resources.NeededIcons.ResourceManager.GetObject(item.Type), Image))
                    End If
                    If Not Me.imlTreeIcons.Images.ContainsKey(item.Type & "_selected") Then
                        tnode.Nodes.Add(item.ID.ToString, item.Name, item.Type, item.Type)
                    Else
                        tnode.Nodes.Add(item.ID.ToString, item.Name, item.Type, item.Type & "_selected")
                    End If
                Next
                Me.tsProgressBar.Value = CInt(Me.tsProgressBar.Value + 0.5)
            Next
            'Me.tsProgressBar.Visible = False
            Me.StatusStrip1.Text = modGlobal.Translate(Me, "Done.")
            Me.StatusStrip1.Items(0).Text = modGlobal.Translate(Me, "Done.")
            Me.SplitContainer1.Panel1Collapsed = Me.tvTree.GetNodeCount(True) = 1
        End Sub

        Private Sub tvTree_AfterExpand(ByVal sender As Object, ByVal e As TreeViewEventArgs) Handles tvTree.AfterExpand
            e.Node.Nodes.Clear()
            Me.UpdateTree(CInt(e.Node.Name))
        End Sub

        Private Sub tvTree_AfterSelect(ByVal sender As Object, ByVal e As TreeViewEventArgs) Handles tvTree.AfterSelect
            If IsNothing(e.Node) Then Exit Sub
            Me.UpdateList(CInt(e.Node.Name))
        End Sub

        Private Sub lvBrowse_ItemActivate(ByVal sender As Object, ByVal e As EventArgs) Handles lvBrowse.ItemActivate
            Dim tnodes() As TreeNode = Me.tvTree.SelectedNode.Nodes.Find(Me.lvBrowse.FocusedItem.Name, False)
            If tnodes.Length > 0 Then Me.tvTree.SelectedNode = tnodes(0)
            Exit Sub
        End Sub

        Private Sub StatusBarToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles StatusBarToolStripMenuItem.Click
            Me.StatusStrip1.Visible = Me.StatusBarToolStripMenuItem.Checked
            modGlobal.Settings.WriteSetting("Window", "StatusBar", Me.StatusStrip1.Visible)
            Me.Form1_Resize(sender, e)
        End Sub

        Private Sub TilesToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles TilesToolStripMenuItem.Click
            Me.lvBrowse.View = View.Tile
            Me.DetailsToolStripMenuItem.Checked = False
            Me.TilesToolStripMenuItem.Checked = True
            Me.IconsToolStripMenuItem.Checked = False
            Me.ListToolStripMenuItem.Checked = False
            Me.LargeIconsToolStripMenuItem.Checked = False
            modGlobal.Settings.WriteSetting("Window", "ListView/View", "Tile")
        End Sub

        Private Sub IconsToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles IconsToolStripMenuItem.Click
            Me.lvBrowse.View = View.SmallIcon
            Me.DetailsToolStripMenuItem.Checked = False
            Me.TilesToolStripMenuItem.Checked = False
            Me.IconsToolStripMenuItem.Checked = True
            Me.ListToolStripMenuItem.Checked = False
            Me.LargeIconsToolStripMenuItem.Checked = False
            modGlobal.Settings.WriteSetting("Window", "ListView/View", "SmallIcon")
        End Sub

        Private Sub LargeIconsToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles LargeIconsToolStripMenuItem.Click
            Me.lvBrowse.View = View.LargeIcon
            Me.DetailsToolStripMenuItem.Checked = False
            Me.TilesToolStripMenuItem.Checked = False
            Me.IconsToolStripMenuItem.Checked = False
            Me.ListToolStripMenuItem.Checked = False
            Me.LargeIconsToolStripMenuItem.Checked = True
            modGlobal.Settings.WriteSetting("Window", "ListView/View", "LargeIcon")
        End Sub

        Private Sub ListToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles ListToolStripMenuItem.Click
            Me.lvBrowse.View = View.List
            Me.DetailsToolStripMenuItem.Checked = False
            Me.TilesToolStripMenuItem.Checked = False
            Me.IconsToolStripMenuItem.Checked = False
            Me.ListToolStripMenuItem.Checked = True
            Me.LargeIconsToolStripMenuItem.Checked = False
            modGlobal.Settings.WriteSetting("Window", "ListView/View", "List")
        End Sub

        Private Sub DetailsToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles DetailsToolStripMenuItem.Click
            Me.lvBrowse.View = View.Details
            Me.DetailsToolStripMenuItem.Checked = True
            Me.TilesToolStripMenuItem.Checked = False
            Me.IconsToolStripMenuItem.Checked = False
            Me.ListToolStripMenuItem.Checked = False
            Me.LargeIconsToolStripMenuItem.Checked = False
            modGlobal.Settings.WriteSetting("Window", "ListView/View", "Details")
        End Sub

        Private Sub RefreshToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles RefreshToolStripMenuItem.Click
            Me.RefreshData()
        End Sub

        Public Sub RefreshData()
            If IsNothing(Me.tvTree.SelectedNode) Then Exit Sub
            Me.tvTree.SelectedNode.Nodes.Clear()
            Me.UpdateTree(CInt(Me.tvTree.SelectedNode.Name))
            Me.UpdateList(CInt(Me.tvTree.SelectedNode.Name))
        End Sub

        Private Sub FileNameToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles FileNameToolStripMenuItem.Click
            Me.FileNameToolStripMenuItem.Checked = True
            Me.TypeToolStripMenuItem.Checked = False
            Me.SortBy = "name"
            modGlobal.Settings.WriteSetting("Window", "ListView/SortBy", Me.SortBy)
            Me.RefreshData()
        End Sub

        Private Sub TypeToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles TypeToolStripMenuItem.Click
            Me.FileNameToolStripMenuItem.Checked = False
            Me.TypeToolStripMenuItem.Checked = True
            Me.SortBy = "type"
            modGlobal.Settings.WriteSetting("Window", "ListView/SortBy", Me.SortBy)
            Me.RefreshData()
        End Sub

        Private Sub Form1_ResizeEnd(ByVal sender As Object, ByVal e As EventArgs) Handles Me.ResizeEnd
            If Me.WindowState = FormWindowState.Normal Then
                modGlobal.Settings.WriteSetting("Window", "Left", Me.Left)
                modGlobal.Settings.WriteSetting("Window", "Top", Me.Top)
                modGlobal.Settings.WriteSetting("Window", "Width", Me.Width)
                modGlobal.Settings.WriteSetting("Window", "Height", Me.Height)
            End If
            modGlobal.Settings.WriteSetting("Window", "State", Me.WindowState)
        End Sub

        Private Sub NewToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles NewToolStripMenuItem.Click
            Dim Frx As New Main()
            Frx.Left = Me.Left + 50
            Frx.Top = Me.Left + 50
            Frx.Show()
        End Sub

        Private Sub SaveToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles SaveToolStripMenuItem.Click
            If Me.FileName = "" Then Me.SaveAsToolStripMenuItem_Click(sender, e)
            Me.SaveFile(Me.FileName)
        End Sub

        Public Sub SaveFile(ByVal FileName As String)
            Me.FileStream = Me.PlugInsSuport.Load(Of iFileFormat)(Me.PlugInsSuport.LoadedPlugIn)
            If Me.FileStream.IsExactFormat = False Then
                Select Case MsgBox(Translate(Me, "You selected to save this file in format, witch can't correctly store all features supported by this " + Application.ProductName + " version. If you are using some of this features (for example: file comments), some data can be lost. Do you want to continue?"), MsgBoxStyle.Information Or MsgBoxStyle.YesNoCancel, Translate(Me, "File Save"))
                    Case MsgBoxResult.No
                        Me.FileStream = Nothing
                        Me.SaveAsToolStripMenuItem_Click(Me, New EventArgs())
                        Exit Sub
                    Case MsgBoxResult.Cancel
                        Me.FileStream = Nothing
                        Exit Sub
                End Select
            End If
            Me.cmsContext.Enabled = False
            Me.tsToolBarFile.Enabled = False
            Me.MainMenuStrip.Enabled = False
            Me.FileStream.ApplicationGUID = Me.Handle.ToInt64.ToString
            Me.FileStream.Database = Me.DBConnection
            Me.FileStream.Save(FileName)
            If Me.FileName <> FileName Then Me.FileName = FileName
            Me.cmsContext.Enabled = True
            Me.tsToolBarFile.Enabled = True
            Me.MainMenuStrip.Enabled = True
            Me.FileStream = Nothing
            Me.saveToolStripButton.Enabled = False
        End Sub

        Private Sub SaveAsToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles SaveAsToolStripMenuItem.Click
            Dim fsDialog As New SaveFileDialog()
            With fsDialog
                Me.PlugInsSuport.UpdatePlugInsList()
                .CheckFileExists = False
                .CheckPathExists = True
                .FileName = Me.FileName
                .Title = modGlobal.Translate(fsDialog, "Save As")
                .ValidateNames = True
                .Filter = Me.PlugInsSuport.GetSupportedFileFormatsForWrite
                .FilterIndex = CInt(modGlobal.Settings.ReadSetting("FilterIndex", "SaveAsDialog", 1))
                If .ShowDialog() = Windows.Forms.DialogResult.Cancel Then Exit Sub
                Me.FileName = .FileName
                modGlobal.Settings.WriteSetting("SaveAsDialog", "FilterIndex", .FilterIndex)
                Me.tvTree.Enabled = False
                Me.lvBrowse.Enabled = False
                Me.MainMenuStrip.Enabled = False
                Me.FileStream = Me.PlugInsSuport.GetHandlerForFilterIndex(.FilterIndex)
                If Me.FileStream.IsExactFormat = False Then
                    Select Case MsgBox(Translate(Me, "You selected to save this file in format, witch can't correctly store all features supported by this " + Application.ProductName + " version. If you are using some of this features (for example: file comments), some data can be lost. Do you want to continue?"), MsgBoxStyle.Information Or MsgBoxStyle.YesNoCancel, Translate(Me, "File Save"))
                        Case MsgBoxResult.No
                            Me.FileStream = Nothing
                            Me.lvBrowse.Enabled = True
                            Me.tvTree.Enabled = True
                            Me.MainMenuStrip.Enabled = True
                            Me.saveToolStripButton.Enabled = False
                            Me.SaveAsToolStripMenuItem_Click(Me, New EventArgs())
                            Exit Sub
                        Case MsgBoxResult.Cancel
                            Me.FileStream = Nothing
                            Me.lvBrowse.Enabled = True
                            Me.tvTree.Enabled = True
                            Me.MainMenuStrip.Enabled = True
                            Me.saveToolStripButton.Enabled = False
                            Exit Sub
                    End Select
                End If
                Me.FileStream.ApplicationGUID = Me.Handle.ToInt64.ToString
                Me.FileStream.Database = Me.DBConnection
                Me.FileStream.Save(.FileName)
                Dim FI As New FileInfo(.FileName)
                Me.Text = "SkyCD - " + FI.Name
                FI = Nothing
                Me.FileStream = Nothing
                Me.lvBrowse.Enabled = True
                Me.tvTree.Enabled = True
                Me.MainMenuStrip.Enabled = True
                Me.saveToolStripButton.Enabled = False
            End With
        End Sub

        Private Sub ExitToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles ExitToolStripMenuItem.Click
            If Me.saveToolStripButton.Enabled Then
                Dim Rez As MsgBoxResult = MsgBox(modGlobal.Translate(Me, "This collection was edited. Do you want to save it?"), MsgBoxStyle.YesNoCancel Or MsgBoxStyle.Exclamation)
                If Rez = MsgBoxResult.Cancel Then Exit Sub
                If Rez = MsgBoxResult.Yes Then Me.SaveToolStripMenuItem_Click(sender, e)
            End If
            Application.Exit()
        End Sub

        Public Sub New()
            ' This call is required by the Windows Form Designer.
            InitializeComponent()

            ' Add any initialization after the InitializeComponent() call.  
            'MsgBox(My.Application.Info.DirectoryPath & "\Languages\" & SkyCD.modGlobal.Settings.ReadSetting("Language", , "English") & ".xml", , "language")
            Me.PlugInsSuport.LoadStartupPlugIns(Me)

            Me.TranslateMenu(Me.MenuStrip1)
            Me.TranslateMenu(Me.cmsContext.Items)
            Me.newToolStripButton.Text = Translate(Me, Me.newToolStripButton.Text)
            Me.openToolStripButton.Text = Translate(Me, Me.openToolStripButton.Text)
            Me.saveToolStripButton.Text = Translate(Me, Me.saveToolStripButton.Text)
            Me.ProjectWebsiteInSourceforgeToolStripMenuItem.Text = Translate(Me, Me.ProjectWebsiteInSourceforgeToolStripMenuItem.Text)
            Me.ProjectAreainGithubToolStripMenuItem.Text = Translate(Me, Me.ProjectAreainGithubToolStripMenuItem.Text)
            Me.ToolStripStatusLabel1.Text = Translate(Me, Me.ToolStripStatusLabel1.Text)

            Exit Sub
            Me.Show()

            If modGlobal.CanCreateForms = False Then Exit Sub
            If My.Application.CommandLineArgs.Count > 0 Then
                modGlobal.CanCreateForms = False
                Dim FI As FileInfo = New FileInfo(My.Application.CommandLineArgs.Item(0))
                If FI.Exists Then Me.LoadFile(FI.FullName)
                Dim I As Integer
                For I = 1 To My.Application.CommandLineArgs.Count - 1
                    My.Application.OpenNew(My.Application.CommandLineArgs.Item(I))
                Next
            End If
        End Sub

        Public Sub TranslateMenu(ByVal Control As System.Windows.Forms.MenuStrip)
            Dim I As Integer, Obj As ToolStripMenuItem
            For I = 0 To Control.Items.Count - 1
                Try
                    Obj = CType(Control.Items.Item(I), ToolStripMenuItem)
                    Obj.Text = modGlobal.Translate(Me, Control.Items.Item(I).Text)
                    Me.TranslateMenu(Obj.DropDownItems)
                Catch ex As Exception

                End Try
                'Me.TranslateMenu(Control.Items.Item(I).ToolStripItemAccessibleObjec)
            Next
        End Sub

        Public Sub TranslateMenu(ByVal Control As System.Windows.Forms.ToolStripItemCollection)
            Dim I As Integer, Obj As ToolStripMenuItem
            If Control.Count = 0 Then Exit Sub
            For I = 0 To Control.Count - 1
                Control.Item(I).Text = modGlobal.Translate(Me, Control.Item(I).Text)
                Try
                    Obj = CType(Control.Item(I), ToolStripMenuItem)
                    Obj.Text = modGlobal.Translate(Me, Control.Item(I).Text)
                    Me.TranslateMenu(Obj.DropDownItems)
                Catch ex As Exception

                End Try
            Next
        End Sub

        Private Sub AboutToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles AboutToolStripMenuItem.Click
            About.ShowDialog()
        End Sub

        Private Sub newToolStripButton_Click(ByVal sender As Object, ByVal e As EventArgs) Handles newToolStripButton.Click
            Me.NewToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub openToolStripButton_Click(ByVal sender As Object, ByVal e As EventArgs) Handles openToolStripButton.Click
            Me.OpenToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub saveToolStripButton_Click(ByVal sender As Object, ByVal e As EventArgs) Handles saveToolStripButton.Click
            Me.SaveToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub AddToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles AddToolStripMenuItem.Click
            Dim AddToList As New AddToList()
            Dim I As Integer = 0
            If AddToList.ShowDialog = DialogResult.Cancel Then Exit Sub
            Dim Adding As New Adding()
            Me.Enabled = False
            Try
                If AddToList.tsbFromFolder.Checked Or AddToList.tsbFromCDROM.Checked Then
                    Dim Folders As New Collection
                    Dim IDs As New Collection
                    Dim DInfo As DirectoryInfo
                    Dim Buffer As New skycd_simple()
                    Dim ID As Integer
                    Adding.Show(Me)
                    Dim sql As String = "SELECT * FROM list WHERE AID = ?"
                    Adding.DoIt(modGlobal.Translate(Me, "Preparing database for modifications..."), Nothing)
                    For Each item As Database.Item In Me.DBConnection.Select(sql, Me.Handle.ToInt64)
                        Select Case item.Type.ToLower
                            Case "scdfile"
                                ID = Buffer.Add(item.Name, scdItemType.scdFile, item.ParentID)
                            Case "scdcddisk"
                                ID = Buffer.Add(item.Name, scdItemType.scdCDDisk, item.ParentID)
                            Case "scdfolder"
                                ID = Buffer.Add(item.Name, scdItemType.scdFolder, item.ParentID)
                            Case "scdnetworkrecource"
                                ID = Buffer.Add(item.Name, scdItemType.scdNetworkRecource, item.ParentID)
                            Case Else
                                ID = Buffer.Add(item.Name, scdItemType.scdUnknown, item.ParentID)
                        End Select
                        Buffer.Items(ID).Size = item.Size
                        Buffer.Items(ID).AdvancedInfo.All = item.Properties.ToString
                    Next
                    If AddToList.rbAllContentAddToSelectedMediaFolder.Checked Then
                        If IsNothing(Me.tvTree.SelectedNode) Then
                            ID = -1
                        Else
                            ID = CInt(Me.tvTree.SelectedNode.Name)
                        End If
                    Else
                        ID = -1
                    End If
                    If Buffer.Exists(AddToList.txtMediaName.Text, ID) > -1 Then
                        Adding.Close()
                        MsgBox(modGlobal.Translate(Me, "There already exists media named exatcly in selected place"), MsgBoxStyle.Exclamation Or MsgBoxStyle.OkOnly, modGlobal.Translate(Me, "Error"))
                        Me.Enabled = True
                        Exit Sub
                    End If
                    Me.DBConnection.Execute("DELETE FROM list WHERE AID = ?", Me.Handle.ToInt64)
                    If AddToList.tsbFromFolder.Checked Then
                        DInfo = New DirectoryInfo(AddToList.txtSelexc.Text)
                        ID = Buffer.Add(AddToList.txtMediaName.Text, scdItemType.scdFolder, ID)
                        IDs.Add(ID)
                        If AddToList.chkIncludeMediaInfo.Checked Then
                            Buffer.Items(ID).AdvancedInfo.Item("RealDirectoryFullName") = DInfo.FullName
                            Buffer.Items(ID).AdvancedInfo.Item("RealDirectoryName") = DInfo.Name
                            Buffer.Items(ID).AdvancedInfo.Item("CreationTime") = DInfo.CreationTimeUtc
                            Buffer.Items(ID).AdvancedInfo.Item("LastAccessTime") = DInfo.LastAccessTimeUtc
                            Buffer.Items(ID).AdvancedInfo.Item("LastWriteTime") = DInfo.LastWriteTimeUtc
                            Buffer.Items(ID).AdvancedInfo.Item("Attributes") = DInfo.Attributes
                        End If
                        Folders.Add(AddToList.txtSelexc.Text)
                    Else
                        Dim DSK As New DriveInfo(AddToList.cmbDrives.Text.Substring(0, 2))
                        ID = Buffer.Add(AddToList.txtMediaName.Text, scdItemType.scdCDDisk, ID)
                        IDs.Add(ID)
                        If AddToList.chkIncludeExtendedInfo.Checked Then
                            Buffer.Items(ID).AdvancedInfo.Item("Title") = DSK.Name
                            Buffer.Items(ID).AdvancedInfo.Item("AvailableFreeSpace") = DSK.AvailableFreeSpace * 8
                            Buffer.Items(ID).AdvancedInfo.Item("RootDirectory") = DSK.RootDirectory.ToString
                            Buffer.Items(ID).AdvancedInfo.Item("TotalFreeSpace") = DSK.TotalFreeSpace * 8
                            Buffer.Items(ID).AdvancedInfo.Item("TotalSize") = DSK.TotalSize * 8
                            Buffer.Items(ID).AdvancedInfo.Item("VolumeLabel") = DSK.VolumeLabel
                        End If
                        Folders.Add(DSK.RootDirectory)
                    End If
                    Adding.pbProgress.Minimum = 0
                    Do
                        DInfo = New DirectoryInfo(Folders(I + 1).ToString)
                        Adding.DoIt(String.Format(Translate(Adding, "Reading '{0}'..."), DInfo.Name), Nothing)
                        If Not (AddToList.tsbFromFolder.Checked And AddToList.chkIncludeSubFolders.Checked = False) Then
                            For Each That As DirectoryInfo In DInfo.GetDirectories()
                                Folders.Add(That.FullName)
                                ID = Buffer.Add(That.Name, scdItemType.scdFolder, CInt(IDs(I + 1)))
                                IDs.Add(ID)
                                If AddToList.chkIncludeExtendedInfo.Checked Then
                                    Buffer.Items(ID).AdvancedInfo.Item("RealDirectoryFullName") = That.FullName
                                    Buffer.Items(ID).AdvancedInfo.Item("RealDirectoryName") = That.Name
                                    Buffer.Items(ID).AdvancedInfo.Item("CreationTime") = That.CreationTimeUtc
                                    Buffer.Items(ID).AdvancedInfo.Item("LastAccessTime") = That.LastAccessTimeUtc
                                    Buffer.Items(ID).AdvancedInfo.Item("LastWriteTime") = That.LastWriteTimeUtc
                                    Buffer.Items(ID).AdvancedInfo.Item("Attributes") = That.Attributes
                                End If
                            Next
                        End If
                        For Each That As FileInfo In DInfo.GetFiles()
                            ID = Buffer.Add(That.Name, scdItemType.scdFile, CInt(IDs(I + 1)))
                            If AddToList.chkIncludeExtendedInfo.Checked Then
                                Buffer.Items(ID).AdvancedInfo.Item("Name") = That.Name.ToString
                                Buffer.Items(ID).AdvancedInfo.Item("Size") = That.Length * 8
                                Buffer.Items(ID).AdvancedInfo.Item("LastWriteTime") = That.LastWriteTimeUtc
                                Buffer.Items(ID).AdvancedInfo.Item("LastAccessTime") = That.LastAccessTimeUtc
                                Buffer.Items(ID).AdvancedInfo.Item("IsReadOnly") = That.IsReadOnly
                                Buffer.Items(ID).AdvancedInfo.Item("Attributes") = That.Attributes
                                Buffer.Items(ID).AdvancedInfo.Item("CreationTime") = That.CreationTimeUtc
                                If That.Extension.ToLower = ".exe" Or That.Extension.ToLower = ".cur" Or That.Extension.ToLower = ".ico" Then Buffer.Items(ID).AdvancedInfo.Item("Icon") = GetFileIcon2(That.FullName)
                            End If
                        Next
                        I = I + 1
                        Application.DoEvents()
                    Loop Until I >= Folders.Count
                    I = LBound(Buffer.Items)
                    Dim transaction = Me.DBConnection.CreateTransaction()
                    Me.DBConnection.Execute("DELETE FROM list WHERE AID = ?", Me.Handle.ToInt64)
                    Dim db_item As Database.Item
                    For Each Item As scdItem In Buffer.Items
                        If Item.Name = "" Then Continue For
                        db_item = New Database.Item(0, Item.Name, Item.ParentID, Item.ItemType.ToString, Item.AdvancedInfo.All.ToString, Item.Size, CInt(Me.Handle.ToInt64))
                        Me.DBConnection.Insert("list", db_item)
                        I = I + 1
                        If I Mod 3 = 5 Then Application.DoEvents()
                    Next
                    Me.DBConnection.CommitTransaction(transaction)
                    Me.tvTree.Nodes.Clear()
                    Me.UpdateTree()
                    If Me.tvTree.Nodes.Count > 0 Then
                        Me.tvTree.SelectedNode = Me.tvTree.Nodes.Item(0)
                    End If
                    Adding.Close()
                    'MsgBox(Buffer.Items.Length)
                ElseIf AddToList.tsbFromInternet.Checked Then
                    Dim Url As New System.Uri(AddToList.txtEnterInternetAdress.Text)
                    If Url.Scheme.ToLower <> "ftp" Then
                        MsgBox(Translate(Me, "Only File Transfer Protocol currently is supported"), MsgBoxStyle.Exclamation, Translate(Me, "Error"))
                        Me.Enabled = True
                        Exit Sub
                    End If
                    Dim Login As New Login()
                    If Login.ShowDialog = Windows.Forms.DialogResult.Cancel Then
                        MsgBox(Translate(Me, "Because you are pressed cancel, other operations are not completed"), MsgBoxStyle.Exclamation, Translate(Me, "Error"))
                        Me.Enabled = True
                        Exit Sub
                    End If
                    If Login.UsernameTextBox.Text.Length = 0 Then
                        MsgBox(Translate(Me, "Bad username"), MsgBoxStyle.Exclamation, Translate(Me, "Error"))
                        Me.Enabled = True
                        Exit Sub
                    End If
                    If Login.PasswordTextBox.Text.Length = 0 Then
                        Me.Enabled = True
                        MsgBox(Translate(Me, "Bad password"), MsgBoxStyle.Exclamation, Translate(Me, "Error"))
                        Exit Sub
                    End If
                    Adding.Show(Me)
                    Dim FTP As New FTPclient(AddToList.txtEnterInternetAdress.Text, Login.UsernameTextBox.Text, Login.PasswordTextBox.Text)
                    Dim Folders As New Collection
                    Dim IDs As New Collection
                    Dim Buffer As New skycd_simple()
                    Dim ID As Integer
                    Dim sql As String = "SELECT * FROM list WHERE AID = ?"
                    Adding.DoIt(modGlobal.Translate(Me, "Preparing database for modifications..."), Nothing)
                    For Each item As Database.Item In Me.DBConnection.Select(sql, Me.Handle.ToInt64)
                        Select Case item.Type.ToLower
                            Case "scdfile"
                                ID = Buffer.Add(item.Name, scdItemType.scdFile, item.ParentID)
                            Case "scdcddisk"
                                ID = Buffer.Add(item.Name, scdItemType.scdCDDisk, item.ParentID)
                            Case "scdfolder"
                                ID = Buffer.Add(item.Name, scdItemType.scdFolder, item.ParentID)
                            Case "scdnetworkrecource"
                                ID = Buffer.Add(item.Name, scdItemType.scdNetworkRecource, item.ParentID)
                            Case Else
                                ID = Buffer.Add(item.Name, scdItemType.scdUnknown, item.ParentID)
                        End Select
                        Buffer.Items(ID).Size = item.Size
                        Buffer.Items(ID).AdvancedInfo.All = item.Properties.ToString
                    Next
                    If AddToList.rbAllContentAddToSelectedMediaFolder.Checked Then
                        If IsNothing(Me.tvTree.SelectedNode) Then
                            ID = -1
                        Else
                            ID = CInt(Me.tvTree.SelectedNode.Name)
                        End If
                    Else
                        ID = -1
                    End If
                    If Buffer.Exists(AddToList.txtMediaName.Text, ID) > -1 Then
                        Adding.Close()
                        MsgBox(modGlobal.Translate(Me, "There already exists media named exatcly in selected place"), MsgBoxStyle.Exclamation Or MsgBoxStyle.OkOnly, modGlobal.Translate(Me, "Error"))
                        Me.Enabled = True
                        Exit Sub
                    End If
                    Me.DBConnection.Execute("DELETE FROM list WHERE AID = ?", Me.Handle.ToInt64)
                    ID = Buffer.Add(AddToList.txtMediaName.Text, scdItemType.scdNetworkRecource, ID)
                    If AddToList.chkIncludeExtendedInfo.Checked Then
                        Buffer.Items(ID).AdvancedInfo.Item("URL") = Url.ToString
                    End If
                    IDs.Add(ID)
                    Folders.Add(FTP.CurrentDirectory)
                    Adding.pbProgress.Minimum = 0
                    Dim entry As FTPdirectory
                    Dim O As Integer
                    I = 1
                    Adding.DoIt(Translate(Adding, "Reading directory from remote server..."), Nothing)
                    Do
                        entry = FTP.ListDirectoryDetail(Folders.Item(I).ToString)
                        For O = 0 To entry.Count - 1
                            Application.DoEvents()
                            Select Case entry.Item(O).FileType
                                Case FTPfileInfo.DirectoryEntryTypes.File
                                    Try
                                        ID = Buffer.Add(entry.Item(O).Filename, scdItemType.scdFile, CInt(IDs.Item(I)))
                                    Catch ex As Exception

                                    End Try
                                    If AddToList.chkIncludeExtendedInfo.Checked Then
                                        Buffer.Items(ID).AdvancedInfo.Item("RealPath") = entry.Item(O).Path
                                        Buffer.Items(ID).AdvancedInfo.Item("FullName") = entry.Item(O).FullName
                                        Buffer.Items(ID).AdvancedInfo.Item("Permission") = entry.Item(O).Permission
                                        Buffer.Items(ID).AdvancedInfo.Item("Size") = entry.Item(O).Size * 8
                                        Buffer.Items(ID).AdvancedInfo.Item("FileDateTime") = entry.Item(O).FileDateTime
                                    End If
                                Case FTPfileInfo.DirectoryEntryTypes.Directory
                                    ID = Buffer.Add(entry.Item(O).Filename, scdItemType.scdFolder, CInt(IDs.Item(I)))
                                    IDs.Add(ID)
                                    Folders.Add(entry.Item(O).FullName)
                                    If AddToList.chkIncludeExtendedInfo.Checked Then
                                        Buffer.Items(ID).AdvancedInfo.Item("RealPath") = entry.Item(O).Path
                                        Buffer.Items(ID).AdvancedInfo.Item("FullName") = entry.Item(O).FullName
                                        Buffer.Items(ID).AdvancedInfo.Item("Permission") = entry.Item(O).Permission
                                        Buffer.Items(ID).AdvancedInfo.Item("FileDateTime") = entry.Item(O).FileDateTime
                                    End If
                            End Select
                        Next
                        Adding.DoIt(String.Format(Translate(Adding, "Reading '{0}'...", Folders.Item(I).ToString)), Nothing)
                        I = I + 1
                        Application.DoEvents()
                    Loop Until I >= Folders.Count
                    I = LBound(Buffer.Items)
                    Dim transaction = Me.DBConnection.CreateTransaction()
                    Me.DBConnection.Execute("DELETE FROM list WHERE AID = ?", Me.Handle.ToInt64)
                    For Each Item As scdItem In Buffer.Items
                        If Item.Name = "" Then
                            Continue For
                        End If
                        Me.DBConnection.Insert("list", New Database.Item(0, Item.Name, Item.ParentID, Item.ItemType.ToString, Item.AdvancedInfo.All.ToString, Item.Size, CInt(Handle.ToInt64)))
                        I = I + 1
                        If I Mod 3 = 5 Then
                            Application.DoEvents()
                        End If
                    Next
                    Me.DBConnection.CommitTransaction(transaction)
                    Me.tvTree.Nodes.Clear()
                    Me.UpdateTree()
                    Me.tvTree.SelectedNode = Me.tvTree.Nodes.Item(0)
                    Adding.Close()
                    'MsgBox(Buffer.Items.Length)
                End If
            Catch ex As DirectoryNotFoundException
                If (Adding Is Nothing) = False Then Adding.Close()
                MsgBox(Translate(Me, "Directory not found."), MsgBoxStyle.Exclamation, Translate(Me, "Error"))
            Catch ex As PathTooLongException
                If (Adding Is Nothing) = False Then Adding.Close()
                MsgBox(Translate(Me, "Your entered path is too long."), MsgBoxStyle.Exclamation, Translate(Me, "Error"))
            Catch ex As DriveNotFoundException
                If (Adding Is Nothing) = False Then Adding.Close()
                MsgBox(Translate(Me, "Drive not found."), MsgBoxStyle.Exclamation, Translate(Me, "Error"))
            Catch ex As FileNotFoundException
                If (Adding Is Nothing) = False Then Adding.Close()
                MsgBox(Translate(Me, "File not found."), MsgBoxStyle.Exclamation, Translate(Me, "Error"))
            End Try
            Me.saveToolStripButton.Enabled = True
            Me.Enabled = True
        End Sub

        Private Sub DeleteToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles DeleteToolStripMenuItem.Click
            Dim I As Integer
            If Me.lvBrowse.Focused Then
                For I = 0 To Me.lvBrowse.SelectedItems.Count - 1
                    Me.Delete(CInt(Me.lvBrowse.SelectedItems(I).Name))
                Next
                Dim Text As String = Me.tvTree.SelectedNode.Name
                Me.lvBrowse.Items.Clear()
                Me.UpdateList(CInt(Text))
                Try
                    Me.tvTree.Nodes.Clear()
                    Me.UpdateTree()
                    Me.tvTree.SelectedNode.Expand()
                Catch ex As Exception

                End Try
            Else
                Me.Delete(CInt(Me.tvTree.SelectedNode.Name))
                Me.tvTree.Nodes.Clear()
                Me.UpdateTree()
            End If
            Me.saveToolStripButton.Enabled = True
        End Sub

        Public Sub Delete(ByVal ID As Integer)
            Me.DBConnection.Execute("DELETE FROM list WHERE AID = ? AND (ID = ? OR ParentID = ?)", Me.Handle.ToInt64, ID, ID)
        End Sub

        Private Sub helpToolStripButton_Click(ByVal sender As Object, ByVal e As EventArgs)
            Me.ContentsToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub lvBrowse_KeyUp(ByVal sender As Object, ByVal e As KeyEventArgs) Handles lvBrowse.KeyUp
            If e.Modifiers = Keys.None And (e.KeyData = Keys.Back Or e.KeyData = Keys.BrowserBack) Then
                If IsNothing(Me.tvTree.SelectedNode.Parent) = False Then
                    Me.tvTree.SelectedNode = Me.tvTree.SelectedNode.Parent
                End If
            End If
        End Sub

        Private Sub cmsContext_Opening(ByVal sender As Object, ByVal e As System.ComponentModel.CancelEventArgs) Handles cmsContext.Opening

            If Me.tvTree.Focused Then
                Me.DeleteStripMenuItem.Enabled = Not IsNothing(Me.tvTree.SelectedNode)
                If IsNothing(Me.tvTree.SelectedNode) = False Then
                    If Me.tvTree.SelectedNode.IsExpanded Then
                        Me.ExpandMenuItem.Visible = False
                        Me.CollapseMenuItem.Visible = True
                    Else
                        Me.ExpandMenuItem.Visible = True
                        Me.CollapseMenuItem.Visible = False
                    End If
                    'Me.CollapseMenuItem.Visible = Not Me.tvTree.SelectedNode.IsExpanded
                    Me.ExpandOrCollapseSeparator11.Visible = True
                    Me.PropertiesToolStripMenuItem.Visible = True
                    Me.ToolStripSeparator12.Visible = True
                    Me.DeleteStripMenuItem.Enabled = True
                Else
                    Me.ExpandMenuItem.Visible = False
                    Me.CollapseMenuItem.Visible = False
                    Me.ExpandOrCollapseSeparator11.Visible = False
                    Me.PropertiesToolStripMenuItem.Visible = False
                    Me.ToolStripSeparator12.Visible = False
                    Me.DeleteStripMenuItem.Enabled = False
                End If
                Me.ViewToolStripMenuItem1.Visible = False
                Me.ToolStripSeparator8.Visible = False
                Me.ArrangeIconsByMenuItem.Visible = False
            ElseIf Me.lvBrowse.Focused Then
                Me.DeleteStripMenuItem.Enabled = Me.lvBrowse.SelectedItems.Count > 0
                Me.ExpandMenuItem.Visible = False
                Me.CollapseMenuItem.Visible = False
                Me.ExpandOrCollapseSeparator11.Visible = False
                Me.ViewToolStripMenuItem1.Visible = True
                Me.ToolStripSeparator8.Visible = True
                Me.ArrangeIconsByMenuItem.Visible = True
                Me.PropertiesToolStripMenuItem.Visible = True
                Me.ToolStripSeparator12.Visible = True
            End If
            Me.TilesStripMenuItem.Checked = (Me.lvBrowse.View = View.Tile)
            Me.DetailsStripMenuItem.Checked = (Me.lvBrowse.View = View.Details)
            Me.SmallIconsStripMenuItem.Checked = (Me.lvBrowse.View = View.SmallIcon)
            Me.LargeIconsStripMenuItem.Checked = (Me.lvBrowse.View = View.LargeIcon)
            Me.ListStripMenuItem.Checked = (Me.lvBrowse.View = View.List)
            Me.ArrangeIconsByNameStripMenuItem.Checked = Me.FileNameToolStripMenuItem.Checked
            Me.ArrangeIconsByTypeMenuToolStripItem.Checked = Me.TypeToolStripMenuItem.Checked
            Me.PropertiesToolStripMenuItem.Enabled = Not ((Me.tvTree.SelectedNode Is Nothing) And (Me.lvBrowse.SelectedItems.Count < 1))
        End Sub

        Private Sub ExpandMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles ExpandMenuItem.Click
            Me.tvTree.SelectedNode.Expand()
        End Sub

        Private Sub CollapseMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles CollapseMenuItem.Click
            Me.tvTree.SelectedNode.Collapse()
        End Sub

        Private Sub DeleteStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles DeleteStripMenuItem.Click
            Me.DeleteToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub AddStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles AddStripMenuItem.Click
            Me.AddToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub PropertiesToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles PropertiesToolStripMenuItem.Click
            Me.PropertiesToolStripMenuItem1_Click(sender, e)
        End Sub

        Private Sub PropertiesToolStripMenuItem1_Click(ByVal sender As Object, ByVal e As EventArgs) Handles PropertiesToolStripMenuItem1.Click
            Dim Fm As New Properties()
            Dim ObjInfo As New scdProperties()
            If Me.tvTree.Focused Then
                Fm.Tag = Me.tvTree.SelectedNode.Name
                Fm.pbIcon.Image = Me.imlTreeIcons.Images(Me.tvTree.SelectedNode.ImageKey)
            ElseIf Me.lvBrowse.Focused Then
                If IsNothing(Me.lvBrowse.FocusedItem) Then
                    Fm.Tag = Me.tvTree.SelectedNode.Name
                    Fm.pbIcon.Image = Me.imlTreeIcons.Images(Me.tvTree.SelectedNode.ImageKey)
                Else
                    Fm.Tag = Me.lvBrowse.FocusedItem.Name
                    Fm.pbIcon.Image = Me.imlBrowseIcons.Images(Me.lvBrowse.FocusedItem.ImageKey)
                End If
            Else
                MsgBox(Translate(Fm, "Unknown selected object"), MsgBoxStyle.Exclamation, Translate(Me, "Error"))
                Exit Sub
            End If
            Dim sql As String = "SELECT Name, Properties, Type FROM list WHERE ID = ? AND AID = ? LIMIT 1"
            Dim item As Database.Item = Me.DBConnection.SelectOnlyOne(sql, Fm.Tag, Me.Handle.ToInt64)
            If item Is Nothing Then
                MsgBox(Translate(Fm, "Can't read item info"), MsgBoxStyle.Exclamation, Translate(Me, "Error"))
                Exit Sub
            End If
            Fm.txtName.Text = item.Name
            Dim Nfo As New scdProperties(item.Properties.ToString)
            Fm.rtbComments.Rtf = Nfo.Item("Comments").ToString
            Select Case item.Type.ToLower
                Case "scdfile"
                    Fm.AddProperty("Creation Time", Nfo.Item("CreationTime"))
                    Fm.AddProperty("Last Access Time", Nfo.Item("LastAccessTime"))
                    Fm.AddProperty("Last Write Time", Nfo.Item("LastWriteTime"))
                    Fm.AddProperty("Creation Time", Nfo.Item("CreationTime"))
                    Fm.AddProperty("Size", Convert2.Long2Size(DirectCast(Nfo.Item("Size"), Long)))
                    Fm.AddProperty(Of FileAttributes)("Attributes", Nfo.Item("Attributes"))
                    Fm.AddProperty("Is Read Only", Nfo.Item("IsReadOnly"))
                Case "scdfolder"
                    Fm.AddProperty("Creation Time", Nfo.Item("CreationTime"))
                    Fm.AddProperty("Last Access Time", Nfo.Item("LastAccessTime"))
                    Fm.AddProperty("Last Write Time", Nfo.Item("LastWriteTime"))
                    Fm.AddProperty("Creation Time", Nfo.Item("CreationTime"))
                    Fm.AddProperty(Of FileAttributes)("Attributes", Nfo.Item("Attributes"))
                Case "scdCDDisk".ToLower
                    Fm.AddProperty("Creation Time", Nfo.Item("CreationTime"))
                    Fm.AddProperty("Last Access Time", Nfo.Item("LastAccessTime"))
                    Fm.AddProperty("Last Write Time", Nfo.Item("LastWriteTime"))
                    Fm.AddProperty("Creation Time", Nfo.Item("CreationTime"))
                    Fm.AddProperty("Attributes", Nfo.Item("Attributes"))
                    Fm.AddProperty("Title", Nfo.Item("Title"))
                    Fm.AddProperty("Available FreeSpace", Convert2.Long2Size(DirectCast(Nfo.Item("AvailableFreeSpace"), Long)))
                    Fm.AddProperty("Root Directory", Nfo.Item("RootDirectory"))
                    Fm.AddProperty("Volume Label", Nfo.Item("VolumeLabel"))
                    Fm.AddProperty("Total Size", Convert2.Long2Size(DirectCast(Nfo.Item("TotalSize"), Long)))
                    Fm.AddProperty("Total Free Space", Convert2.Long2Size(DirectCast(Nfo.Item("TotalFreeSpace"), Long)))
                Case "scdNetworkRecource".ToLower
                    Fm.AddProperty("Address", Nfo.Item("URL"))
                Case Else
                    'Fm.tpObjectInfo.v()
                    Fm.tbControl.TabPages.RemoveByKey("tpObjectInfo")
            End Select
            Select Case Fm.ShowDialog()
                Case Windows.Forms.DialogResult.Cancel
                    Fm.Dispose()
                    Exit Sub
                Case Windows.Forms.DialogResult.OK
                    Nfo.Item("Comments") = Fm.Text
                    Me.DBConnection.Execute("UPDATE list SET Properties = ? WHERE ID = ? AND AID = ?", Nfo.All, Fm.Tag, Me.Handle.ToInt64.ToString)
                    Fm.Dispose()
                    Me.saveToolStripButton.Enabled = True
            End Select
        End Sub

        Private Sub ToolStripMenuItem4_Click(ByVal sender As Object, ByVal e As EventArgs) Handles ToolStripMenuItem4.Click
            Me.RefreshToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub ArrangeIconsByNameStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles ArrangeIconsByNameStripMenuItem.Click
            Me.FileNameToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub ArrangeIconsByTypeMenuToolStripItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles ArrangeIconsByTypeMenuToolStripItem.Click
            Me.TypeToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub SmallIconsStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles SmallIconsStripMenuItem.Click
            Me.IconsToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub LargeIconsStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles LargeIconsStripMenuItem.Click
            Me.LargeIconsToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub ListStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles ListStripMenuItem.Click
            Me.ListToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub DetailsStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles DetailsStripMenuItem.Click
            Me.DetailsToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub TilesStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles TilesStripMenuItem.Click
            Me.TilesToolStripMenuItem_Click(sender, e)
        End Sub

        Private Sub OptionsToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles OptionsToolStripMenuItem.Click
            Forms.Options.ShowDialog()
        End Sub

        Private Sub ViewToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs) Handles ViewToolStripMenuItem.Click
            Me.TilesToolStripMenuItem.Checked = (Me.lvBrowse.View = View.Tile)
            Me.LargeIconsToolStripMenuItem.Checked = (Me.lvBrowse.View = View.LargeIcon)
            Me.IconsToolStripMenuItem.Checked = (Me.lvBrowse.View = View.SmallIcon)
            Me.DetailsToolStripMenuItem.Checked = (Me.lvBrowse.View = View.Details)
            Me.ListToolStripMenuItem.Checked = (Me.lvBrowse.View = View.List)
        End Sub

        Private Sub MenuStrip1_ItemClicked(ByVal sender As Object, ByVal e As System.Windows.Forms.ToolStripItemClickedEventArgs) Handles MenuStrip1.ItemClicked
            Me.DeleteToolStripMenuItem.Enabled = Not ((Me.tvTree.SelectedNode Is Nothing) And (Me.lvBrowse.SelectedItems.Count < 1))
            Me.SaveAsToolStripMenuItem.Enabled = Me.tvTree.Nodes.Count > 0
            Me.SaveToolStripMenuItem.Enabled = Me.saveToolStripButton.Enabled
            Me.PropertiesToolStripMenuItem1.Enabled = Not ((Me.tvTree.SelectedNode Is Nothing) And (Me.lvBrowse.SelectedItems.Count < 1))
        End Sub

        Private Sub ContentsToolStripMenuItem_Click(ByVal sender As Object, ByVal e As EventArgs)
            Dim Fi As New FileInfo(modGlobal.Settings.ReadSetting("HelpFile", , Application.StartupPath & "\Help\index.html").ToString)
            If Fi.Exists Then
                Help.ShowHelp(Me, Fi.FullName)
            Else
                MsgBox(Translate(Me, "Can't find help file." + vbCrLf + vbCrLf + "Please reinstall application."), MsgBoxStyle.Exclamation, Translate(Me, "Error"))
            End If
        End Sub

        Protected Overrides Sub Finalize()
            MyBase.Finalize()
        End Sub

        Private Sub ProjectWebsiteInSourceforgeToolStripMenuItem_Click(sender As Object, e As EventArgs) Handles ProjectWebsiteInSourceforgeToolStripMenuItem.Click
            Process.Start(New ProcessStartInfo("https://sourceforge.net/projects/skycd/") With {.UseShellExecute = True})
        End Sub

        Private Sub ProjectAreainGithubToolStripMenuItem_Click(sender As Object, e As EventArgs) Handles ProjectAreainGithubToolStripMenuItem.Click
            Process.Start(New ProcessStartInfo("https://github.com/SkyCD/SkyCD") With {.UseShellExecute = True})
        End Sub
    End Class


End Namespace