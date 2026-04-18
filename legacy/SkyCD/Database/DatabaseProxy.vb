Imports System.Collections.Generic
Imports SkyCD.Database
Imports System.Data.SQLite
Imports System.Data.Common
Imports System.Reflection
Imports System.Data

Public Class DatabaseProxy
    Implements iConnection

    Protected Database As SQLiteConnection

    Public Sub New(Filename As String)
        Me.Database = New SQLiteConnection("Data Source=" + Filename + ";Version=3;New=True;Compress=True;")
    End Sub

    Public Sub Execute(query As String, ParamArray args() As Object) Implements iConnection.Execute
        Dim cmd As SQLiteCommand = Me.CreateCommand(query, args)
        If Not Me.Database.State = Data.ConnectionState.Open Then
            Me.Database.Open()
        End If
        cmd.ExecuteNonQuery()
        'Me.Database.Close()
    End Sub

    Public Function Insert(table As String, item As Item) As Integer Implements iConnection.Insert
        Static cmd As SQLiteCommand = Nothing
        Dim properties As PropertyInfo() = GetType(Item).GetProperties
        If cmd Is Nothing Then
            Dim sql As String = "INSERT INTO `" + table + "` ("
            Dim field_names As New List(Of String)
            Dim field_values As New List(Of Object)
            Dim rep As String = ""
            For Each p As PropertyInfo In properties
                If Not p.CanRead Then Continue For
                field_names.Add(p.Name)
                field_values.Add(p.GetValue(item))
                rep += "?,"
            Next
            If rep.Length > 0 Then
                rep = rep.Substring(0, rep.Length - 1)
            Else
                Return Nothing
            End If
            sql += String.Join(",", field_names.ToArray()) + ") VALUES(" + rep + ");"
            cmd = Me.CreateCommand(sql, field_values.ToArray())
        Else
            cmd.Parameters.Clear()
            For Each p As PropertyInfo In properties
                cmd.Parameters.Add(New SQLiteParameter(DbType.String, p.GetValue(item)))
            Next
        End If
        If Not Me.Database.State = Data.ConnectionState.Open Then
            Me.Database.Open()
        End If
        Dim ret As Integer = cmd.ExecuteNonQuery()
        ' Me.Database.Close()
        Return ret
    End Function

    Private Function CreateCommand(query As String, ParamArray args() As Object) As SQLiteCommand
        Dim cmd As New SQLiteCommand(query, Me.Database)
        For Each arg As Object In args
            cmd.Parameters.Add(New SQLiteParameter(DbType.String, arg))
        Next
        Return cmd
    End Function

    Public Function [Select](query As String, ParamArray args() As Object) As List(Of Item) Implements iConnection.Select
        Dim ret As New List(Of Item)
        Dim cmd As SQLiteCommand = Me.CreateCommand(query, args)
        If Not Me.Database.State = Data.ConnectionState.Open Then
            Me.Database.Open()
        End If
        Dim reader As DbDataReader = cmd.ExecuteReader()
        Dim properties As PropertyInfo() = GetType(Item).GetProperties
        Dim item As Item
        Do While reader.NextResult
            item = New Item()
            For Each p As PropertyInfo In properties
                p.SetValue(item, reader.Item(p.Name))
            Next
            ret.Add(item)
        Loop
        Return ret
    End Function

    Public Function [Select](Of T)(query As String, ParamArray args() As Object) As T Implements iConnection.Select
        Dim ret As T
        With Me.Database.CreateCommand
            For Each arg As Object In args
                .Parameters.Add(New SQLiteParameter(DbType.String, arg))
            Next
            .CommandText = query
            ret = CType(.ExecuteScalar(), T)
        End With
        Return ret
    End Function

    Public Function SelectOnlyOne(query As String, ParamArray args() As Object) As Item Implements iConnection.SelectOnlyOne
        Dim ret As List(Of Item) = [Select](query, args)
        If ret.Count > 0 Then
            Return ret(0)
        End If
        Return Nothing
    End Function

    Public Function CreateTransaction() As Object Implements iConnection.CreateTransaction
        Return Me.Database.BeginTransaction()
    End Function

    Sub CommitTransaction(ByRef Transaction As Object) Implements iConnection.CommitTransaction
        CType(Transaction, SQLiteTransaction).Commit()
    End Sub

    Sub RollbackTransaction(ByRef Transaction As Object) Implements iConnection.RollbackTransaction
        CType(Transaction, SQLiteTransaction).Rollback()
    End Sub
End Class
