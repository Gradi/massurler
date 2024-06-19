module Massurler.Utils

open System
open ByteSizeLib
open System.IO

let dowhile (f: unit -> Async<bool>) : Async<unit> = async {
    let mutable run = true
    while run do
        let! nextRun = f ()
        run <- nextRun
}

let megabyte (count: int) = (ByteSize.FromMebiBytes (float count)).Bytes |> int

let timer (interval: TimeSpan) (f: unit -> unit) =
    if interval <= TimeSpan.Zero then
        failwithf "Timer interval is <= 0 (%O)" interval

    let locker = obj ()
    let mutable disposed = false

    let timer = new System.Timers.Timer (interval)
    timer.AutoReset <- false
    timer.Elapsed.Add (fun _ ->
        lock locker (fun () ->
            if disposed then ()
            else
                try
                    try
                        f ()
                    with
                    | _ -> ()
                finally
                    timer.Start ()))

    timer.Start()

    { new IDisposable with
        member _.Dispose () =
            lock locker (fun () ->
                disposed <- true
                timer.Stop ()
                timer.Dispose ()) }

let mkdirsForFile (filepath: string) =
    let dir = Path.GetDirectoryName filepath
    if not (String.IsNullOrWhiteSpace dir) then
        Directory.CreateDirectory dir |> ignore
