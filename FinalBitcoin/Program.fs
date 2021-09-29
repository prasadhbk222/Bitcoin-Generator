// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp
#if INTERACTIVE
#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"
#endif

open System
open System.Text
open System.Diagnostics
open System.Security.Cryptography
open Akka.FSharp
open Akka.Remote
open Akka.Routing

let gatorid = "phadkar"

let config =
    Configuration.parse
        @"akka {
            log-config-on-start = on
            stdout-loglevel = DEBUG
            loglevel = DEBUG
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            }
            remote {
                helios.tcp {
                    transport-class = ""Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote""
                    applied-adapters = []
                    transport-protocol = tcp
                    port = 8080
                    hostname = localhost
                }
            }
        }"

let miningSystem = System.create "LOCAL" config
//Let miningSystem = System.create "miningSystem" (Configuration.load())


// To print and keep count of valid bitcoins

let coinPrinterIncrementer validash bitcoincount = 
    printfn "Bitcoin number %d : %s" bitcoincount  validash
    bitcoincount + 1
    

let coinAccumulator f startCount (mailbox: Actor<'a>) = 
    let rec loop lastState = actor {
        let! msg = mailbox.Receive()
        let newState = f msg lastState
        return! loop newState
    }
    loop startCount


// worket to mine bitcoin
let randomStringGenerator length =
    let charset = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
    let random = System.Random()
    seq {
        for i = 1 to length do
            yield charset.[random.Next(charset.Length)]
    }
    |> Seq.toArray
    |> (fun str -> String(str))



let hashFunction (hashInput: string) =
    let hashOutput=
        hashInput
        |> Encoding.UTF8.GetBytes
        |> (new SHA256Managed()).ComputeHash
        |> Array.map (fun (x : byte) -> String.Format("{0:X2}", x))
        |> String.concat ""
    hashOutput

let generateBitCoin uniqueIdentifier startIdx endIdx noOfZeroes coinAccumulatorActor = 
    for i = startIdx to endIdx do
        let nonce = uniqueIdentifier + string i
        let hashInput = gatorid + nonce
        let hashOutput:string = hashFunction  hashInput
        let mutable zeroString = ""
        for i = 1 to noOfZeroes do
            zeroString <- zeroString + "0"
        if hashOutput.[0..noOfZeroes].StartsWith zeroString then
            coinAccumulatorActor <! gatorid + ";" + nonce + " " + hashOutput


let minerworker coinAccumulatorActor (mailbox: Actor<'a>) = 
    let rec loop () = actor {
        let! (uniqueIdentifier, startIdx, endIdx, noOfZeroes) = mailbox.Receive ()
        let sender = mailbox.Sender()
        generateBitCoin uniqueIdentifier startIdx endIdx noOfZeroes coinAccumulatorActor
        sender <! "TASK DONE"
        
        return! loop ()
    }
    loop ()

let controller totalWork workSize noOfZeroes minerWorkerActor (mailbox: Actor<'a>) =
    let (fixedRandomString:string) = randomStringGenerator(10);
    let mutable i = 1
    let rec loop() = actor {
        let workSteps = totalWork / workSize;

        let! message = mailbox.Receive()
        let sender = mailbox.Sender()
        let noOfProcesses  = System.Environment.ProcessorCount * 100
        //let noOfProcesses  = 8
        match message with
        | "LOCAL" -> 
            printfn "@@@in server mode"
            while (i < noOfProcesses && i < workSteps) do
                minerWorkerActor <! (fixedRandomString, i * workSize , (i + 1) * workSize, noOfZeroes)
                i <- i + 1
            //printfn "%i" i
        | "REMOTE" ->
            printfn "Call from Client!!"
            let orderString = (string noOfZeroes) + "," + gatorid
            printfn "%s" orderString
            sender <! orderString
        | "TASK DONE" -> 
            // printfn "Worker finished alloted job"
            if (i < workSteps) then
                sender <! (fixedRandomString,  i * workSize , (i + 1) * workSize , noOfZeroes)
                i <- i + 1
        | _ -> 
            minerWorkerActor <! noOfZeroes
        return! loop()
    }
    loop()

[<EntryPoint>]
let main argv =
    let noOfZeroes = int argv.[0]
    let totalWork = 1000000000
    let workSize = 500

    let coinAccumulatorActor =
        coinAccumulator coinPrinterIncrementer 0
        |> spawn miningSystem "coinAccumulator"

    let minerWorkerActor =
        minerworker coinAccumulatorActor
        |> spawnOpt miningSystem "minerworker"
        <| [SpawnOption.Router(RoundRobinPool(System.Environment.ProcessorCount * 100))]

    let controllerActor =
        controller totalWork workSize noOfZeroes minerWorkerActor
        |> spawn miningSystem  "controllerActor"

 //// http://programmingcradle.blogspot.com/2012/08/f-profiling-how-to-measure-cpu-time-and.html - CPU time reference
    let proc = Process.GetCurrentProcess()
    let cpuTimeStamp = proc.TotalProcessorTime
    let timer = new Stopwatch()
    timer.Start()
    try
        controllerActor <! "LOCAL"
        printfn("Press any key to stop")
        Console.ReadLine() |> ignore
    finally
        let cpuTime = (proc.TotalProcessorTime - cpuTimeStamp).TotalMilliseconds
        printfn "CPU time = %d ms" (int64 cpuTime)
        printfn "Absolute time = %d ms" timer.ElapsedMilliseconds
        printfn "Ratio = %f" (cpuTime / float(timer.ElapsedMilliseconds))
    0
