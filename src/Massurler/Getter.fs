namespace Massurler

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open ByteSizeLib
open System.IO
open Massurler.Utils

type Status =
    | Pending
    | Working
    | Ok
    | Error

type Getter(httpClient: HttpClient, url: string, outputname: string) as this =

    let mutable status = Pending

    let mutable totalBytes = 0L
    let mutable totalBytesDownloaded = 0L

    let mutable totalBytesDownloadedPreviously = 0L
    let mutable downloadSpeedInBytes = 0L

    let mutable error = ""

    let mutable task = Task.CompletedTask

    let updateDownloadSpeed () =
        let was = totalBytesDownloadedPreviously
        let now = totalBytesDownloaded

        totalBytesDownloadedPreviously <- now
        downloadSpeedInBytes <- now - was

    let reset () =
        totalBytes <- 0L
        totalBytesDownloaded <- 0L
        totalBytesDownloadedPreviously <- 0L
        downloadSpeedInBytes <- 0L
        error <- ""

    member _.Status = status

    member _.SizeTotal = ByteSize.FromBytes (float totalBytes)

    member _.SizeDownloaded = ByteSize.FromBytes (float totalBytesDownloaded)

    member _.DownloadSpeedPerSecond = ByteSize.FromBytes (float downloadSpeedInBytes)

    member _.PercentDownloaded =
        let percent =
            if totalBytes <> 0L then
                (float totalBytesDownloaded) / (float totalBytes) * 100.0
            else
                0.0
        sprintf "%3.2f%%" percent

    member _.Error = error

    member _.Url = url

    member _.OutputName = outputname

    member _.GetStatusString () =
        match status with
        | Pending -> "Pending"
        | Working ->
            sprintf "Working: %O / %O (%s, %O/sec)" this.SizeDownloaded this.SizeTotal
                                                    this.PercentDownloaded this.DownloadSpeedPerSecond
        | Ok -> "Ok"
        | Error -> sprintf "Error: %s" error

    member _.Start () =
        lock this (fun () ->
            match status with
            | Pending
            | Error ->
                reset ()
                status <- Working

                task <-
                    async {
                        try
                            use! response = httpClient.GetAsync (url, HttpCompletionOption.ResponseHeadersRead) |> Async.AwaitTask
                            if response.StatusCode <> HttpStatusCode.OK then
                                failwithf "Response returned %O" response.StatusCode

                            let contentLength = Option.ofNullable response.Content.Headers.ContentLength |> Option.defaultValue -1L
                            if contentLength <= 0L then
                                failwithf "Response's 'Content-Length' is empty or contains invalid value."

                            totalBytes <- contentLength
                            use! responseStream = response.Content.ReadAsStreamAsync () |> Async.AwaitTask
                            mkdirsForFile outputname
                            use resultStream = File.Create (outputname, bufferSize = megabyte 1)
                            let buffer: byte array = Array.zeroCreate (megabyte 1)

                            use _ = timer (TimeSpan.FromSeconds 1.0) updateDownloadSpeed

                            do! dowhile (fun () -> async {
                                let! actualBytesRead = responseStream.ReadAsync (buffer, 0, buffer.Length) |> Async.AwaitTask

                                if actualBytesRead > 0 then
                                    totalBytesDownloaded <- totalBytesDownloaded + (int64 actualBytesRead)
                                    do! resultStream.WriteAsync (buffer, 0, actualBytesRead) |> Async.AwaitTask
                                    return true
                                else
                                    return false
                            })

                            lock this (fun () ->
                                status <- Ok)


                        with
                        | exc ->
                            lock this (fun () ->
                                error <- exc.Message
                                status <- Error)
                    } |> Async.StartAsTask


            | Working
            | Ok as status -> failwithf "Attempt to start job with status %A" status)

