Imports System.Xml

Public Class clsXMLCfgFile

    '     roland.demeester@skynet.be
    ' 
    ' The two functions GetConfigInfo and WriteconfigInfo are replacing the former
    ' GetPrivateProfileString and WritePrivateProfileString functions.
    ' The main difference is that all (INI) settings are now stored in an XML file
    ' 
    ' GetConfigInfo returns a collection iso of a string(buffer)
    ' if the file doesn't exist it is created:
    ' <configuration> </configuration>
    ' if the key is empty("") a list of all keys belonging to the section is returned in the collection
    ' if the section is empty, a list of all sections is returned.
    ' if neither is empty, the value is returned, or the default, if nothing found.
    ' 
    ' WriteConfigInfo returns True or False for success
    ' the section is mandatory
    ' if no key is provided, all keys for the section are removed
    ' if no value is provided for the key, the key is removed
    ' if all three are provided, section and key are created if necessary

    Dim Doc As New XmlDocument()
    Dim FileName As String
    Dim doesExist As Boolean
    Private SNode As String = "configuration"

    Public Property StartNode() As String
        Get
            Return Me.SNode
        End Get
        Set(ByVal value As String)
            Me.SNode = value
        End Set
    End Property

    Private Sub Init(ByVal aFileName As String)
        Dim FL As New System.IO.FileInfo(aFileName)
        If FL.Exists = False Then _
            If FL.Directory.Exists = False Then FL.Directory.Create()
        FileName = aFileName
        Try
            Doc.Load(aFileName)
            doesExist = True
        Catch ex As Exception
            If Err.Number = 53 Then
                Doc.LoadXml(("<" & Me.SNode & ">" & "</" & Me.SNode & ">"))
                Doc.Save(aFileName)
            End If
        End Try
    End Sub

    Public Sub New(ByVal FileName As String)
        Init(FileName)
    End Sub

    Public Sub New(ByVal FileName As String, ByVal StartNode As String)
        Me.SNode = StartNode
        Init(FileName)
    End Sub

    Public Function GetConfigInfo(ByVal aSection As String, ByVal aKey As String, ByVal aDefaultValue As String) As Collection
        ' return immediately if the file didn't exist
        If doesExist = False Then
            Return New Collection()
        End If
        If aSection = "" Then
            ' if aSection = "" then get all section names
            Return getchildren("")
        ElseIf aKey = "" Then
            ' if aKey = "" then get all keynames for the section
            Return getchildren(aSection)
        Else
            Dim col As New Collection()
            col.Add(getKeyValue(aSection, aKey, aDefaultValue))
            Return col
        End If
    End Function

    Public Function WriteConfigInfo(ByVal aSection As String, ByVal aKey As String, ByVal aValue As String) As Boolean
        Dim node1 As XmlNode
        Dim node2 As XmlNode
        If aKey = "" Then
            ' find the section, remove all its keys and remove the section
            node1 = (Doc.DocumentElement).SelectSingleNode("/" & Me.SNode & "/" & aSection)
            ' if no such section, return True
            If node1 Is Nothing Then Return True
            ' remove all its children
            node1.RemoveAll()
            ' select its parent ("configuration")
            node2 = (Doc.DocumentElement).SelectSingleNode(Me.SNode)
            ' remove the section
            node2.RemoveChild(node1)
        ElseIf aValue = "" Then
            ' find the section of this key
            node1 = (Doc.DocumentElement).SelectSingleNode("/" & Me.SNode & "/" & aSection)
            ' return if the section doesn't exist
            If node1 Is Nothing Then Return True
            ' find the key
            node2 = (Doc.DocumentElement).SelectSingleNode("/" & Me.SNode & "/" & aSection & "/" & aKey)
            ' return true if the key doesn't exist
            If node2 Is Nothing Then Return True
            ' remove the key
            If node1.RemoveChild(node2) Is Nothing Then Return False
        Else
            ' Both the Key and the Value are filled 
            ' Find the key
            node1 = (Doc.DocumentElement).SelectSingleNode("/" & Me.SNode & "/" & aSection & "/" & aKey)
            If node1 Is Nothing Then
                ' The key doesn't exist: find the section
                node2 = (Doc.DocumentElement).SelectSingleNode("/" & Me.SNode & "/" & aSection)
                If node2 Is Nothing Then
                    ' Create the section first
                    Dim e As Xml.XmlElement = Doc.CreateElement(aSection)
                    ' Add the new node at the end of the children of ("configuration")
                    node2 = Doc.DocumentElement.AppendChild(e)
                    ' return false if failure
                    If node2 Is Nothing Then Return False
                    ' now create key and value
                    e = Doc.CreateElement(aKey)
                    e.InnerText = aValue
                    ' Return False if failure
                    If (node2.AppendChild(e)) Is Nothing Then Return False
                Else
                    ' Create the key and put the value
                    Dim e As Xml.XmlElement = Doc.CreateElement(aKey)
                    e.InnerText = aValue
                    node2.AppendChild(e)
                End If
            Else
                ' Key exists: set its Value
                node1.InnerText = aValue
            End If
        End If
        ' Save the document
        Doc.Save(FileName)
    End Function

    Private Function getKeyValue(ByVal aSection As String, ByVal aKey As String, ByVal aDefaultValue As String) As String
        Dim node As XmlNode
        node = (Doc.DocumentElement).SelectSingleNode("/" & Me.SNode & "/" & aSection & "/" & aKey)
        If node Is Nothing Then Return aDefaultValue
        Return node.InnerText
    End Function

    Private Function getchildren(ByVal aNodeName As String) As Collection
        Dim col As New Collection()
        Dim node As XmlNode = Nothing
        Try
            ' Select the root if the Node is empty
            If aNodeName = "" Then
                node = Doc.DocumentElement
            Else
                ' Select the node given
                node = Doc.DocumentElement.SelectSingleNode(aNodeName)
            End If
        Catch
        End Try
        ' exit with an empty collection if nothing here
        If node Is Nothing Then Return col
        ' exit with an empty colection if the node has no children
        If node.HasChildNodes = False Then Return col
        ' get the nodelist of all children
        Dim nodeList As XmlNodeList = node.ChildNodes
        Dim i As Integer
        ' transform the Nodelist into an ordinary collection
        For i = 0 To nodeList.Count - 1
            col.Add(nodeList.Item(i).Name)
        Next
        Return col
    End Function

    Public Shared Function GetConfigInfo(ByVal aSection As String, ByVal aKey As String, _
                    ByVal aDefaultValue As String, ByVal aFileName As String) As Collection

        If aFileName = "" Then Return New Collection()
        ' create in instance of the class and get the config file info
        Dim XmlFile As New clsXMLCfgFile(aFileName)
        Return XmlFile.GetConfigInfo(aSection, aKey, aDefaultValue)

    End Function

    Public Shared Function WriteConfigInfo(ByVal aSection As String, ByVal aKey As String, _
                    ByVal aValue As String, ByVal aFileName As String) As Boolean

        If aSection = "" Then Return False
        If aFileName = "" Then Return (False)
        ' create in instance of the class and write the config file info
        Dim XmlFile As New clsXMLCfgFile(aFileName)
        Return XmlFile.WriteConfigInfo(aSection, aKey, aValue)

    End Function

End Class