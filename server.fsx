#r "nuget: Akka.FSharp" 
#r "nuget: Akka.TestKit" 

open System
open Akka.Actor
open Akka.FSharp
open System.Security.Cryptography
open System.Text
open System.Collections.Generic
open System.Globalization

type User ={
    Username: String
    Password: String
    Feeds: int[]
    
}

let Server (mailbox: Actor<_>) =
    let rec loop() = actor {
        let! msg = mailbox.Receive()
        match msg with 

        return! loop()
    }
    loop()

let User (mailbox: Actor<_>) =

    let rec loop() = actor {
        let! msg = mailbox.Receive()
        match msg with 
        return! loop()
    }
    loop()

let HashTagFunction (mailbox: Actor<_>) =

    let rec loop() = actor {
        let! msg = mailbox.Receive()
        match msg with 
        return! loop()
    }
    loop()


let MentionsFunction (mailbox: Actor<_>) =

    let rec loop() = actor {
        let! msg = mailbox.Receive()
        match msg with 
        return! loop()
    }
    loop()


let UpdateDatabase (mailbox: Actor<_>) =

    let rec loop() = actor {
        let! msg = mailbox.Receive()
        match msg with 
        return! loop()
    }
    loop()


let PushTweets (mailbox: Actor<_>) =

    let rec loop() = actor {
        let! msg = mailbox.Receive()
        match msg with 
        return! loop()
    }
    loop()


let Login (mailbox: Actor<_>) =

    let rec loop() = actor {
        let! msg = mailbox.Receive()
        match msg with 
        return! loop()
    }
    loop()


let Logout (mailbox: Actor<_>) =

    let rec loop() = actor {
        let! msg = mailbox.Receive()
        match msg with 
        return! loop()
    }
    loop()