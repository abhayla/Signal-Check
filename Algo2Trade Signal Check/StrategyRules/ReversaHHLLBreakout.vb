﻿Imports System.Threading
Imports Algo2TradeBLL
Imports Utilities.Numbers.NumberManipulation

Public Class ReversaHHLLBreakout
    Inherits Rule
    Public Sub New(ByVal canceller As CancellationTokenSource, ByVal stockCategory As String, ByVal stockName As String, ByVal timeFrame As Integer, ByVal useHA As Boolean)
        MyBase.New(canceller, stockCategory, stockName, timeFrame, useHA)
    End Sub
    Public Overrides Async Function RunAsync(ByVal startDate As Date, ByVal endDate As Date) As Task(Of DataTable)
        Await Task.Delay(0).ConfigureAwait(False)
        Dim ret As New DataTable
        ret.Columns.Add("Date")
        ret.Columns.Add("Instrument")
        ret.Columns.Add("Signal")
        Dim stockData As StockSelection = New StockSelection(_canceller, _category, _name)
        AddHandler stockData.Heartbeat, AddressOf OnHeartbeat
        AddHandler stockData.WaitingFor, AddressOf OnWaitingFor
        AddHandler stockData.DocumentRetryStatus, AddressOf OnDocumentRetryStatus
        AddHandler stockData.DocumentDownloadComplete, AddressOf OnDocumentDownloadComplete
        Dim chkDate As Date = startDate
        While chkDate <= endDate
            _canceller.Token.ThrowIfCancellationRequested()
            Dim stockList As List(Of String) = Nothing
            If _name Is Nothing OrElse _name = "" Then
                stockList = Await stockData.GetStockList(chkDate).ConfigureAwait(False)
            Else
                stockList = New List(Of String)
                stockList.Add(_name)
            End If
            _canceller.Token.ThrowIfCancellationRequested()
            If stockList IsNot Nothing AndAlso stockList.Count > 0 Then
                For Each stock In stockList
                    _canceller.Token.ThrowIfCancellationRequested()
                    Dim stockPayload As Dictionary(Of Date, Payload) = Nothing
                    Select Case _category
                        Case "Cash"
                            stockPayload = _common.GetRawPayload(Common.DataBaseTable.Intraday_Cash, stock, chkDate.AddDays(-7), chkDate)
                        Case "Currency"
                            stockPayload = _common.GetRawPayload(Common.DataBaseTable.Intraday_Currency, stock, chkDate.AddDays(-7), chkDate)
                        Case "Commodity"
                            stockPayload = _common.GetRawPayload(Common.DataBaseTable.Intraday_Commodity, stock, chkDate.AddDays(-7), chkDate)
                        Case "Future"
                            stockPayload = _common.GetRawPayload(Common.DataBaseTable.Intraday_Futures, stock, chkDate.AddDays(-7), chkDate)
                        Case Else
                            Throw New NotImplementedException
                    End Select
                    _canceller.Token.ThrowIfCancellationRequested()
                    If stockPayload IsNot Nothing AndAlso stockPayload.Count > 0 Then
                        Dim XMinutePayload As Dictionary(Of Date, Payload) = Nothing
                        If _timeFrame > 1 Then
                            XMinutePayload = _common.ConvertPayloadsToXMinutes(stockPayload, _timeFrame)
                        Else
                            XMinutePayload = stockPayload
                        End If
                        _canceller.Token.ThrowIfCancellationRequested()
                        Dim inputPayload As Dictionary(Of Date, Payload) = Nothing
                        If _useHA Then
                            Indicator.HeikenAshi.ConvertToHeikenAshi(XMinutePayload, inputPayload)
                        Else
                            inputPayload = XMinutePayload
                        End If
                        _canceller.Token.ThrowIfCancellationRequested()
                        Dim currentDayPayload As Dictionary(Of Date, Payload) = Nothing
                        For Each runningPayload In inputPayload.Keys
                            _canceller.Token.ThrowIfCancellationRequested()
                            If runningPayload.Date = chkDate.Date Then
                                If currentDayPayload Is Nothing Then currentDayPayload = New Dictionary(Of Date, Payload)
                                currentDayPayload.Add(runningPayload, inputPayload(runningPayload))
                            End If
                        Next
                        'Main logic
                        Dim ATRPayload As Dictionary(Of Date, Decimal) = Nothing
                        Indicator.ATR.CalculateATR(14, inputPayload, ATRPayload)
                        If currentDayPayload IsNot Nothing AndAlso currentDayPayload.Count > 0 Then
                            For Each runningPayload In currentDayPayload.Values
                                _canceller.Token.ThrowIfCancellationRequested()
                                If runningPayload.PreviousCandlePayload IsNot Nothing AndAlso
                                    runningPayload.PreviousCandlePayload.PreviousCandlePayload.PayloadDate.Date = runningPayload.PayloadDate.Date Then
                                    Dim buffer As Decimal = CalculateBuffer(runningPayload.High, RoundOfType.Floor)
                                    If runningPayload.PreviousCandlePayload.High > runningPayload.PreviousCandlePayload.PreviousCandlePayload.High + buffer AndAlso
                                        runningPayload.PreviousCandlePayload.Low > runningPayload.PreviousCandlePayload.PreviousCandlePayload.Low + buffer Then
                                        If runningPayload.Low < runningPayload.PreviousCandlePayload.PreviousCandlePayload.Low - buffer Then
                                            Dim row As DataRow = ret.NewRow
                                            row("Date") = runningPayload.PayloadDate
                                            row("Instrument") = runningPayload.TradingSymbol
                                            row("Signal") = -1
                                            ret.Rows.Add(row)
                                        End If
                                    ElseIf runningPayload.PreviousCandlePayload.Low < runningPayload.PreviousCandlePayload.PreviousCandlePayload.Low - buffer AndAlso
                                        runningPayload.PreviousCandlePayload.High < runningPayload.PreviousCandlePayload.PreviousCandlePayload.High - buffer Then
                                        If runningPayload.High > runningPayload.PreviousCandlePayload.PreviousCandlePayload.High + buffer Then
                                            Dim row As DataRow = ret.NewRow
                                            row("Date") = runningPayload.PayloadDate
                                            row("Instrument") = runningPayload.TradingSymbol
                                            row("Signal") = +1
                                            ret.Rows.Add(row)
                                        End If
                                    End If
                                End If
                            Next
                        End If
                    End If
                Next
            End If
            chkDate = chkDate.AddDays(1)
        End While
        Return ret
    End Function
End Class