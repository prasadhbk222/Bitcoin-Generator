#if INTERACTIVE
#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"
#endif

open System
open System.Text
open System.Security.Cryptography
open System.Threading
open Akka.FSharp
open Akka.Remote
open Akka.Routing



let config =
    Configuration.parse
        @"akka {
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            }
            remote {
                helios.tcp {
                    transport-class = ""Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote""
		            applied-adapters = []
		            transport-protocol = tcp
		            port = 0
		            hostname = localhost
                }
            }
        }"


// let system = System.create "my-system" (Configuration.load())
let remoteSystem = System.create "remoteSystem" config


let controller ipAddress workerRef (mailbox: Actor<'a>) =
    let url = "akka.tcp://LOCAL@" + ipAddress + ":8080/user/controllerActor"
    let ControllerServer = select url remoteSystem
    let rec loop() = actor {
        let! message = mailbox.Receive()
        match box message with
        | :? string as msg when msg="Ready to Work" -> 
            printfn "Requesting Server"
            ControllerServer <! "REMOTE"
        | :? string as msg when (msg |> String.exists (fun char -> char = ',')) ->
            printfn "Message from server%%%%%%%%%%%%%% %s" msg
            let result = msg.Split ','
            let noOfZeroes = int result.[0]
            let gatorid = result.[1]
            for i = 1 to System.Environment.ProcessorCount do
                workerRef <! string noOfZeroes + "," + gatorid
        | _ -> ()

        return! loop()
    }
    loop()
 

// Worker
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

let generateBitCoin noOfZeroes gatorid coinAccumulatorActor = 
    let mutable count = 1
    while true do
        if count > 10000000 then
            count <- 1
        else 
            count <- count + 1
        let nonce = randomStringGenerator(6) + string count
        let hashInput = gatorid + nonce
        let hashOutput:string = hashFunction  hashInput
        let mutable zeroString = ""
        for i = 1 to noOfZeroes do
            zeroString <- zeroString + "0"
        if hashOutput.[0..noOfZeroes].StartsWith zeroString then
            coinAccumulatorActor <! " [From remote] " + gatorid + ";" + nonce + " " + hashOutput






let minerworker ipAddress (mailbox: Actor<'a>) =
    let url = "akka.tcp://LOCAL@" + ipAddress + ":8080/user/coinAccumulator"
    let coinAccumulatorActor = select url remoteSystem 
    let rec loop () = actor {
        let! message = mailbox.Receive()
        match box message with
        | :? string as msg when (msg |> String.exists (fun char -> char = ',')) ->
            let result = msg.Split ','
            let noOfZeroes = int result.[0]
            let gatorid = result.[1]
            generateBitCoin noOfZeroes gatorid coinAccumulatorActor
            | _ -> ()

        return! loop ()
    }
    loop ()

    
[<EntryPoint>]
let main argv =
    let ipAddress = string argv.[0]
    printfn "Server Ip Address: %s" ipAddress


    // Worker
    let minerworkerActor =
        minerworker ipAddress
        |> spawnOpt remoteSystem "minerworker"
        <| [SpawnOption.Router(RoundRobinPool(System.Environment.ProcessorCount))]

    let controllerActor =
        controller ipAddress minerworkerActor
        |> spawn remoteSystem "controller"

    controllerActor <! "Ready to Work"
    Thread.Sleep(6000000);
    0