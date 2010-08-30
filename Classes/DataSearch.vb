﻿' Utility to automatically download radio programmes, using a plugin framework for provider specific implementation.
' Copyright © 2007-2010 Matt Robinson
'
' This program is free software; you can redistribute it and/or modify it under the terms of the GNU General
' Public License as published by the Free Software Foundation; either version 2 of the License, or (at your
' option) any later version.
'
' This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
' implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public
' License for more details.
'
' You should have received a copy of the GNU General Public License along with this program; if not, write
' to the Free Software Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.

Option Strict On
Option Explicit On

Imports System.Data.SQLite
Imports System.Collections.Generic
Imports System.IO

Friend Class DataSearch
    <ThreadStatic()> _
    Private Shared dbConn As SQLiteConnection

    Private Shared searchInstance As DataSearch
    Private Shared searchInstanceLock As New Object

    Private dataInstance As Data

    Private _downloadQuery As String = String.Empty
    Private downloadsVisible As List(Of Integer)

    Private updateIndexLock As New Object
    Private downloadVisLock As New Object

    Public Shared Function GetInstance(ByVal instance As Data) As DataSearch
        ' Need to use a lock instead of declaring the instance variable as New,
        ' as otherwise New gets called before the Data class is ready
        SyncLock searchInstanceLock
            If searchInstance Is Nothing Then
                searchInstance = New DataSearch(instance)
            End If

            Return searchInstance
        End SyncLock
    End Function

    Private Sub New(ByVal instance As Data)
        dataInstance = instance

        Dim tableCols As New Dictionary(Of String, String())

        tableCols.Add("downloads", {"name", "description"})

        If CheckIndex(tableCols) = False Then
            ' Close & clean up the connection used for testing
            dbConn.Close()
            dbConn.Dispose()
            dbConn = Nothing

            ' Clean up the old index
            File.Delete(DatabasePath())

            Status.StatusText = "Building search index..."
            Status.ProgressBarMarquee = False
            Status.ProgressBarValue = 0
            Status.ProgressBarMax = 100 * tableCols.Count
            Status.Show()

            SyncLock updateIndexLock
                Using trans As SQLiteTransaction = FetchDbConn.BeginTransaction
                    ' Create the index tables
                    For Each table As KeyValuePair(Of String, String()) In tableCols
                        Using command As New SQLiteCommand(TableSql(table.Key, table.Value), FetchDbConn, trans)
                            command.ExecuteNonQuery()
                        End Using
                    Next

                    Status.StatusText = "Building search index for downloads..."

                    Dim progress As Integer = 1
                    Dim downloadItems As List(Of Data.DownloadData) = dataInstance.FetchDownloadList(False)

                    For Each downloadItem As Data.DownloadData In downloadItems
                        AddDownload(downloadItem)

                        Status.ProgressBarValue = CInt((progress / downloadItems.Count) * 100)
                        progress += 1
                    Next

                    Status.ProgressBarValue = 100

                    trans.Commit()
                End Using
            End SyncLock

            Status.Hide()
        End If
    End Sub

    Private Function DatabasePath() As String
        Return Path.Combine(FileUtils.GetAppDataFolder(), "searchindex.db")
    End Function

    Private Function FetchDbConn() As SQLiteConnection
        If dbConn Is Nothing Then
            dbConn = New SQLiteConnection("Data Source=" + DatabasePath() + ";Version=3")
            dbConn.Open()
        End If

        Return dbConn
    End Function

    Private Function CheckIndex(ByVal tableCols As Dictionary(Of String, String())) As Boolean
        Using command As New SQLiteCommand("select count(*) from sqlite_master where type='table' and name=@name and sql=@sql", FetchDbConn)
            Dim nameParam As New SQLiteParameter("@name")
            Dim sqlParam As New SQLiteParameter("@sql")

            command.Parameters.Add(nameParam)
            command.Parameters.Add(sqlParam)

            For Each table As KeyValuePair(Of String, String()) In tableCols
                nameParam.Value = table.Key
                sqlParam.Value = TableSql(table.Key, table.Value)

                If CInt(command.ExecuteScalar()) <> 1 Then
                    Return False
                End If
            Next
        End Using

        Return True
    End Function

    Private Function TableSql(ByVal tableName As String, ByVal columns As String()) As String
        Return "CREATE VIRTUAL TABLE " + tableName + " USING fts3(" + Join(columns, ", ") + ")"
    End Function

    Public Property DownloadQuery As String
        Get
            Return _downloadQuery
        End Get
        Set(ByVal newQuery As String)
            If newQuery <> _downloadQuery Then
                Try
                    RunQuery(newQuery)
                    _downloadQuery = newQuery
                Catch sqliteExp As SQLiteException When sqliteExp.ErrorCode = SQLiteErrorCode.Error
                    ' The search query is badly formed, so keep the old query
                End Try
            End If
        End Set
    End Property

    Private Sub RunQuery(ByVal query As String)
        SyncLock downloadVisLock
            Using command As New SQLiteCommand("select docid from downloads where downloads match @query", FetchDbConn)
                command.Parameters.Add(New SQLiteParameter("@query", query + "*"))

                Using reader As SQLiteDataReader = command.ExecuteReader()
                    Dim docidOrdinal As Integer = reader.GetOrdinal("docid")

                    downloadsVisible = New List(Of Integer)

                    While reader.Read
                        downloadsVisible.Add(reader.GetInt32(docidOrdinal))
                    End While
                End Using
            End Using
        End SyncLock
    End Sub

    Public Function DownloadIsVisible(ByVal epid As Integer) As Boolean
        If DownloadQuery = String.Empty Then
            Return True
        End If

        SyncLock downloadVisLock
            If downloadsVisible Is Nothing Then
                RunQuery(_downloadQuery)
            End If

            Return downloadsVisible.Contains(epid)
        End SyncLock
    End Function

    Private Sub AddDownload(ByVal storeData As Data.DownloadData)
        SyncLock updateIndexLock
            Using command As New SQLiteCommand("insert or replace into downloads (docid, name, description) values (@epid, @name, @description)", FetchDbConn)
                command.Parameters.Add(New SQLiteParameter("@epid", storeData.epid))
                command.Parameters.Add(New SQLiteParameter("@name", storeData.name))
                command.Parameters.Add(New SQLiteParameter("@description", storeData.description))

                command.ExecuteNonQuery()
            End Using
        End SyncLock

        SyncLock downloadVisLock
            downloadsVisible = Nothing
        End SyncLock
    End Sub

    Public Sub AddDownload(ByVal epid As Integer)
        Dim downloadData As Data.DownloadData = dataInstance.FetchDownloadData(epid)
        AddDownload(downloadData)
    End Sub

    Public Sub UpdateDownload(ByVal epid As Integer)
        SyncLock updateIndexLock
            Using trans As SQLiteTransaction = FetchDbConn.BeginTransaction
                RemoveDownload(epid)
                AddDownload(epid)
            End Using
        End SyncLock
    End Sub

    Public Sub RemoveDownload(ByVal epid As Integer)
        SyncLock updateIndexLock
            Using command As New SQLiteCommand("delete from downloads where docid = @epid", FetchDbConn)
                command.Parameters.Add(New SQLiteParameter("@epid", epid))
                command.ExecuteNonQuery()
            End Using
        End SyncLock

        ' No need to clear the visibility cache, as having an extra entry won't cause an issue
    End Sub
End Class