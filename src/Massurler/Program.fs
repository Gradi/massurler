module Massurler.Program

open System.Net.Http
open System.Runtime.InteropServices
open Argu
open Utils
open System
open System.IO

type CliArgs =
    | [<Mandatory>] [<AltCommandLine("--maxp")>] MaxParallelDownloads of int
    | TimeoutSeconds of int

    interface IArgParserTemplate with

        member this.Usage =
            match this with
            | MaxParallelDownloads _ -> "Specifies maximum number of parallel downloads."
            | TimeoutSeconds _ -> "Specifies connection timeout in seconds. Default 30."

let currentJobs =
    System.Collections.Generic.List<Getter>()

let filterByStatus status =
    currentJobs
    |> Seq.where (fun g -> g.Status = status)

let printNewLine times =
    for i in 0..times do
        printfn ""


let activatePending maxParallelDowns =
    lock currentJobs (fun () ->
        try
            let working = filterByStatus Working |> Seq.length
            if working < maxParallelDowns then
                let toActivateCount = maxParallelDowns - working

                filterByStatus Pending
                |> Seq.truncate toActivateCount
                |> Seq.iter _.Start()

        with
        | exc ->
            eprintfn "Error on activating pending download jobs."
            eprintfn "%O" exc)


let rec askInputString prefix =
    printf "(%s) -> " prefix
    let input = Console.ReadLine()
    if isNull input then
        None
    else
        let trimmed = input.Trim ()
        if String.IsNullOrWhiteSpace trimmed then
            None
        else
            Some trimmed

let printHelp () =
    printfn "Help:"
    printfn "'help' - print this screen."
    printfn "'add' - adds new download."
    printfn "'show' - show current download statuses."
    printfn "'cls', 'clear' - clears console."
    printfn "'restart_errored' - restarts downloads with errors statuses."
    printfn "'clear_errored' - removes downloads with erors statuses (also deletes their output files (if any))."
    printfn "'exit' - exits application. Will ask confirmation first."

let addNewDownload (httpClient: HttpClient) =
    match askInputString "Download URL" with
    | Some url ->
        match askInputString "Path to result file" with
        | Some savepath ->
            lock currentJobs (fun () -> currentJobs.Add (Getter (httpClient, url, savepath)))
        | None ->
            printfn "Bad filename input."

    | None ->
        printfn "Bad URL input."

let showStatuses () =
    lock currentJobs (fun () ->
        currentJobs
        |> Seq.groupBy _.Status
        |> Seq.sortBy fst
        |> Seq.iter (fun (status, jobs) ->

            match status with
            | Pending
            | Ok -> printfn "%A %d" status (Seq.length jobs)

            | Working
            | Error ->
                printfn "%A %d" status (Seq.length jobs)
                jobs
                |> Seq.sortBy _.OutputName
                |> Seq.iter (fun job -> printfn "%s -> %s" job.OutputName (job.GetStatusString()) )))


let restartErrored maxParallelDowns =
    lock currentJobs (fun () ->
        let working = filterByStatus Working |> Seq.length
        if working < maxParallelDowns then
            let toRestartCount = maxParallelDowns - working

            let errored =
                filterByStatus Error
                |> Seq.truncate toRestartCount
                |> Seq.toList

            errored
            |> List.iter (_.Start())
            printfn "Restarted %d" (List.length errored)
        else
            printfn "Can't restart errored jobs. Current working %d, but maximum downloads %d" working maxParallelDowns)

let clearErrored () =
    lock currentJobs (fun () ->
        let errored = filterByStatus Error |> Seq.toList
        printfn "There are %d errored jobs." (List.length errored)

        errored
        |> List.iter (fun job ->
            try
                if File.Exists job.OutputName then
                    printfn "Deleting %s" job.OutputName
                    File.Delete job.OutputName

                currentJobs.Remove job |> ignore
            with
            | exc ->
                eprintfn "Error on attempt to delete file %s" job.OutputName
                eprintfn "%O" exc))

let askConfirmation () =
    lock currentJobs (fun () ->
        let working = filterByStatus Working |> Seq.length
        printfn "There are %d working jobs." working
        match askInputString "type 'yes' to confirm exit" with
        | Some "yes" -> true
        | Some _
        | None -> false)


let rec runConsoleGui (httpClient: HttpClient) maxParallelDowns =
    let mutable continueGui = true
    try
        printNewLine 2
        printfn "Welcome to Advanced Console GUI (ACG)."
        match askInputString "command name (for help: 'help')" with
        | Some "help" -> printHelp ()
        | Some "add" -> addNewDownload httpClient
        | Some "show" -> showStatuses ()
        | Some "cls"
        | Some "clear" -> Console.Clear ()
        | Some "restart_errored" -> restartErrored maxParallelDowns
        | Some "clear_errored" -> clearErrored ()
        | Some "exit" ->
            continueGui <- not (askConfirmation ())

        | Some _
        | None ->
            printfn "Don't know what you've typed. Try again."
    with
    | exc ->
        eprintfn "Error on console gui."
        eprintfn "Try again."
        eprintfn "%O" exc

    if continueGui then
        runConsoleGui httpClient maxParallelDowns


[<EntryPoint>]
let main argv =
    try
        let cliArgs = Argu.ArgumentParser.Create<CliArgs>().Parse(argv)
        let maxDowns = cliArgs.GetResult MaxParallelDownloads
        let timeout = cliArgs.GetResult (TimeoutSeconds, defaultValue = 30)

        use _ = timer (TimeSpan.FromSeconds 5.0) (fun () -> activatePending maxDowns)

        use httpClient = new HttpClient ()
        httpClient.Timeout <- TimeSpan.FromSeconds timeout

        // Prevent user from accidentally closing app.
        use _ = PosixSignalRegistration.Create (PosixSignal.SIGINT, (fun ctx ->
            ctx.Cancel <- true
            printNewLine 2
            printf "'Ctrl+C' (or 'SIGINT') is disabled. Use 'exit' command."
            printNewLine 2))

        runConsoleGui httpClient maxDowns
        0
    with
    | :? ArguParseException as exc ->
        printfn "%s" exc.Message
        1
    | exc ->
        eprintfn "%O" exc
        2
