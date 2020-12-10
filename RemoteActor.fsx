
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
#r "nuget: FSharp.Json"

#load @"./Constants.fsx"
#load @"./MessageTypes.fsx"
#load @"./Logger.fsx"

open Constants.Constants
open System.Text.RegularExpressions
open System
open Akka.FSharp
open Akka.Actor
open Akka.Configuration
open MessageTypes
open FSharp.Json
open Logger.Logging

let config =
    Configuration.parse
        @"akka {
            actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            remote.helios.tcp {
                hostname = ""127.0.0.1""
                port = 9002
            }
        }"
        
let system = System.create "Twitter" config


let sendMessageToEngine data = 
    let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9002/user/engineActor") system
    let json = Json.serialize data
    destinationRef <! json

let sendMessageToUser data id = 
    let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (id |> string)) system
    let json = Json.serialize data
    destinationRef <! json

let TwitterEngine (numNodesVal:int) (numTweetsVal:int) (mailbox : Actor<_>) = 

    let numNodes = numNodesVal 
    let numTweets = numTweetsVal
    let mutable subscribers = Map.empty
    let mutable tweetsToBeSent = Map.empty
    let mutable allTweets = Map.empty
    let mutable userSubscribedTweets = Map.empty
    let mutable myMentions = Map.empty
    let mutable offlineUsers = Array.empty
    let mutable hashtagTweets = Map.empty
    let mutable tweetsReceived = 0
    let selfStopwatchEngine = System.Diagnostics.Stopwatch()

    let searchMentions newTweet=
        let mutable newUser = ""
        let mutable userTweetNumber = Array.create 0 ""
        let mutable userFound = 0
        for c in newTweet do
            if userFound = 0 && c = '@' then
                userFound <- 1
            elif userFound = 1 && (c = ' ' || c = '@' || c = '#') then
                newUser <- newUser.Trim()
                if newUser <> "" then
                    userTweetNumber <- Array.concat [| userTweetNumber ; [|newUser|] |]
                userFound <- 0
                newUser <- ""
                if c = '@' then
                    userFound <- 1
            elif userFound = 1 && c <> '@' then
                newUser <- newUser + string(c)
        newUser <- newUser.Trim()
        if newUser <> "" then
            userTweetNumber <- Array.concat [| userTweetNumber ; [|newUser|] |]
        userTweetNumber

    let searchHashtags newTweet=
        let mutable newUser = ""
        let mutable userTweetNumber = Array.create 0 ""
        let mutable userFound = 0
        for c in newTweet do
            if userFound = 0 && c = '#' then
                userFound <- 1
            elif userFound = 1 && (c = ' ' || c = '@' || c = '#') then
                newUser <- newUser.Trim()
                if newUser <> "" then
                    userTweetNumber <- Array.concat [| userTweetNumber ; [|newUser|] |]
                userFound <- 0
                newUser <- ""
                if c = '#' then
                    userFound <- 1
            elif userFound = 1 && c <> '#' then
                newUser <- newUser + string(c)
        newUser <- newUser.Trim()
        if newUser <> "" then
            userTweetNumber <- Array.concat [| userTweetNumber ; [|newUser|] |]
        userTweetNumber

    let searchTweets (receivedTweets:string[]) (searchString:string)=
        let mutable searchedTweets = Array.create 0 ""
        for newTweets in receivedTweets do
            let mutable wordIndex = newTweets.IndexOf(searchString)
            if wordIndex <> -1 then
                searchedTweets <- Array.concat [| searchedTweets ; [|newTweets|] |]
        searchedTweets
                
    let matchSample r m =
        let r = Regex(r)
        let m1 = r.Match m
        let idFound = m1.Groups.[1] |> string |> int
        idFound

    let stripchars chars str =
        Seq.fold
            (fun (str: string) chr ->
            str.Replace(chr |> Char.ToUpper |> string, "").Replace(chr |> Char.ToLower |> string, ""))
            str chars

    
    let rec loop() = actor {
        log Debug "Actor called"
        let! json = mailbox.Receive()
        let message = Json.deserialize<TweetApiMessage> json
        log Debug "Actor called with %A" message

        let sender = mailbox.Sender()
        
        let operation = message.Operation
        
        match operation with

        | "StartEngine" ->
            log Debug "StartEngine"
            for i in 0..numNodes-1 do
                let mutable workerName = sprintf "User%i" i
                subscribers <- subscribers.Add(workerName, [|-1|])
                tweetsToBeSent <- tweetsToBeSent.Add(workerName, [|{Author = ""; Message = ""}|])
                allTweets <- allTweets.Add(workerName, [|""|])
                myMentions <- myMentions.Add(workerName, [|""|])
                userSubscribedTweets <- userSubscribedTweets.Add(workerName, [|""|])
            hashtagTweets <- hashtagTweets.Add("", [|""|])
            selfStopwatchEngine.Start()

        | "GetNumNodes" ->
            log Debug "GetNumNodes"
            let userId = message.Author |> int
            let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
            let data = {Author = userId |> string; Message = numNodes |> string; Operation = "GetNumNodes"}
            let json = Json.serialize data
            destinationRef <! json
            // let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
            // destinationRef <! GetNumNodes(numNodes, userId)

        | "Tweet" ->
            log Debug "Tweet"
            let userId = message.Author |> int
            let tweetString = message.Message
            tweetsReceived <- tweetsReceived + 1
            log Debug "Tweets received = %d" tweetsReceived
            if tweetsReceived > numTweets then
               ALL_COMPUTATIONS_DONE <- 1
            if userId < numNodes then
                let mutable userName = sprintf "User%i" userId

                // store tweet for user
                let mutable allTweetsByUser = allTweets.[userName]
                allTweetsByUser <- Array.concat [| allTweetsByUser ; [|tweetString|] |]
                allTweets <- allTweets.Add(userName, allTweetsByUser)

                // store hashtags for tweet
                let userHashtags = searchHashtags tweetString
                for hashtags in userHashtags do
                    if hashtagTweets.ContainsKey(hashtags) then
                        let mutable thisHashtagTweets = hashtagTweets.[hashtags]
                        thisHashtagTweets <- Array.concat [| thisHashtagTweets ; [|tweetString|] |]
                        hashtagTweets <- hashtagTweets.Add(hashtags, thisHashtagTweets)
                    else
                        hashtagTweets <- hashtagTweets.Add(hashtags, [|tweetString|]) 

                // store user mentions for tweet
                let userMentions = searchMentions tweetString
                for mentioned in userMentions do
                    let mutable myMentionedTweets = myMentions.[mentioned]
                    myMentionedTweets <- Array.concat [| myMentionedTweets ; [|tweetString|] |]
                    myMentions <- myMentions.Add(mentioned, myMentionedTweets)

                // who should this tweet be sent out to?
                let mutable allSubscribers = subscribers.[userName]
                allSubscribers <- allSubscribers |> Array.filter ((<>) -1 )
                
                for mentioned in userMentions do
                    let mentionedId = matchSample userRegexMatch mentioned
                    if mentionedId < numNodes then
                        allSubscribers <- allSubscribers |> Array.filter ((<>) mentionedId )
                        allSubscribers <- Array.concat [| allSubscribers ; [|mentionedId|] |]

                for subs in allSubscribers do
                    let mutable destination = (sprintf "User%i" subs)
                    // store in user subscribed tweets
                    let mutable newUserSubTweets = userSubscribedTweets.[destination]
                    newUserSubTweets <- Array.concat [| newUserSubTweets ; [|tweetString|] |]
                    userSubscribedTweets <- userSubscribedTweets.Add(destination, newUserSubTweets)

                    // should we send it or not?
                    let mutable userFoundOffline = false
                    let tweet = {Author = userName; Message = tweetString}
                    for offlineUsersCurrent in offlineUsers do
                        if not userFoundOffline then
                            if offlineUsersCurrent = subs then
                                userFoundOffline <- true
                    if userFoundOffline then
                        let mutable usertweetsToBeSent = tweetsToBeSent.[destination]
                        // usertweetsToBeSent <- usertweetsToBeSent |> Array.filter ((<>) tweetString )
                        // usertweetsToBeSent <- usertweetsToBeSent |> Array.filter ((<>) "" )
                        usertweetsToBeSent <- Array.concat [| usertweetsToBeSent ; [|tweet|] |]
                        tweetsToBeSent <- tweetsToBeSent.Add(destination, usertweetsToBeSent)
                    else
                        let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (subs |> string)) system
                        let data = {Author = userName |> string; Message = tweetString; Operation = "ReceiveTweet"}
                        let json = Json.serialize data
                        destinationRef <! json
                        // let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (subs |> string)) system
                        // destinationRef <! ReceiveTweet(tweet)
                    
        | "Retweet" ->
            // choose a random user and ask them for a random tweet
            log Debug "Retweet"
            let userId = message.Author |> int
            let mutable randomUserId = random.Next(numNodes)
            let mutable randomUserName = sprintf "User%i" randomUserId
            let allRandomUserTweets = allTweets.[randomUserName]
            let randomTweetNumber = random.Next(allRandomUserTweets.Length)
            if randomTweetNumber < allRandomUserTweets.Length then
                let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
                let data = {Author = "" |> string; Message = allRandomUserTweets.[randomTweetNumber]; Operation = "ReceiveTweet"}
                let json = Json.serialize data
                destinationRef <! json
                // let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
                // destinationRef <! RetweetReceive()

        | "Subscribe" ->
            log Debug "Subscribe"
            let userId = message.Author |> int
            let mutable allUsers = [|0..numNodes-1|]
            let mutable userName = sprintf "User%i" userId
            if userId < numNodes then
                let mutable userSubscribers = subscribers.[userName]
                userSubscribers <- userSubscribers |> Array.filter ((<>) -1 )
                if userSubscribers.Length < numNodes - 2 then
                    // remove already subscribed indexes and choose from among the remaining ones
                    for i in userSubscribers do
                        allUsers <- allUsers |> Array.filter ((<>) i )
                    allUsers <- allUsers |> Array.filter ((<>) userId )
                    let mutable randomNewSub = random.Next(allUsers.Length)
                    userSubscribers <- Array.concat [| userSubscribers ; [|randomNewSub|] |] 
                    subscribers <- subscribers.Add(userName, userSubscribers)
                    log Debug "%d is subscribing to %d" userId randomNewSub

        | "GoOffline" ->
            log Debug "GoOffline"
            let userId = message.Author |> int
            offlineUsers <- offlineUsers |> Array.filter ((<>) userId )
            offlineUsers <- Array.concat [| offlineUsers ; [|userId|] |] 
            // let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
            // destinationRef <! GoOffline(userId)
            let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
            let data = {Author = userId |> string; Message = ""; Operation = "GoOffline"}
            let json = Json.serialize data
            destinationRef <! json

        | "GoOnline" ->
            log Debug "GoOnline"
            let userId = message.Author |> int
            let mutable userName = sprintf "User%i" userId
            if userId < numNodes then
                let mutable usertweetsToBeSent = tweetsToBeSent.[userName]
                // usertweetsToBeSent <- usertweetsToBeSent |> Array.filter ((<>) "" )
                offlineUsers <- offlineUsers |> Array.filter ((<>) userId )
                for tweet in usertweetsToBeSent do
                    let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
                    let data = {Author = tweet.Author |> string; Message = tweet.Message; Operation = "ReceiveTweet"}
                    let json = Json.serialize data
                    destinationRef <! json
                    // let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
                    // destinationRef <! ReceiveTweet(tweet)
                tweetsToBeSent <- tweetsToBeSent.Add(userName, [|{Author = ""; Message = ""}|])

        | "QuerySubscribedTweets" ->
            log Debug "QuerySubscribedTweets"
            let userId = message.Author |> int
            let searchString = message.Message
            let mutable userName = sprintf "User%i" userId
            let mutable newUserSubTweets = userSubscribedTweets.[userName]
            newUserSubTweets <- newUserSubTweets |> Array.filter ((<>) "" )
            let mutable tweetsFound = searchTweets newUserSubTweets searchString
            // let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
            let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system

            if tweetsFound.Length <> 0 then
                // destinationRef <! ReceiveQuerySubscribedTweets(searchString, tweetsFound)
                let arr = Json.serialize tweetsFound    
                let data = {Author = searchString |> string; Message = arr; Operation = "ReceiveQuerySubscribedTweets"}
                let json = Json.serialize data
                destinationRef <! json
            else 
                let arr = Json.serialize [| "No tweets found" |]    
                let data = {Author = searchString |> string; Message = arr; Operation = "ReceiveQuerySubscribedTweets"}
                let json = Json.serialize data
                destinationRef <! json
                // destinationRef <! ReceiveQuerySubscribedTweets(searchString, [| "No tweets found" |])

        | "QueryHashtags" ->
            log Debug "QueryHashtags"
            let userId = message.Author |> int  
            let hashtagQuery = message.Message
            if userId < numNodes then
                let hashtagString = stripchars "#" hashtagQuery
                // let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
                let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
                if hashtagTweets.ContainsKey(hashtagString) then
                    let mutable tweetsFound = hashtagTweets.[hashtagString]
                    let arr = Json.serialize tweetsFound
                    let data = {Author = hashtagQuery |> string; Message = arr; Operation = "ReceiveQueryHashtags"}
                    let json = Json.serialize data
                    destinationRef <! json
                    // destinationRef <! ReceiveQueryHashtags(hashtagQuery, tweetsFound)
                else
                    let arr = Json.serialize [| "No tweets found" |]    
                    let data = {Author = hashtagQuery |> string; Message = arr; Operation = "ReceiveQueryHashtags"}
                    let json = Json.serialize data
                    destinationRef <! json
                    // destinationRef <! ReceiveQueryHashtags(hashtagQuery, [| "No tweets found" |])

        | "QueryMentions" ->
            log Debug "QueryMentions"
            let userId = message.Author |> int  
            let mutable userName = sprintf "User%i" userId
            let mutable userMentions = myMentions.[userName]
            // let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
            let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9001/user/User"+ (userId |> string)) system
            
            if userMentions.Length <> 0 then
                // destinationRef <! ReceiveQueryMentions(userMentions)
                let arr = Json.serialize userMentions    
                let data = {Author = "" |> string; Message = arr; Operation = "ReceiveQueryMentions"}
                let json = Json.serialize data
                destinationRef <! json
            else
                let arr = Json.serialize [| "No tweets found" |]    
                let data = {Author = "" |> string; Message = arr; Operation = "ReceiveQueryMentions"}
                let json = Json.serialize data
                destinationRef <! json
                // destinationRef <! ReceiveQueryMentions([| "No tweets found" |])
            
        | _ -> 
            log Debug "Invalid Request"
            sender <! "Invalid Request"

        return! loop()
    }
    loop ()

let main argv =
    let numNodes = 10
    let numTweets = 1000

    let engineActor = spawn system "engineActor" (TwitterEngine numNodes numTweets)
    let destinationRef = select ("akka.tcp://Twitter@127.0.0.1:9002/user/engineActor") system
    let data = {Author = ""; Message = ""; Operation = "StartEngine"}
    let json = Json.serialize data
    destinationRef <! json

    log Debug "Done with engineActor"

    while(ALL_COMPUTATIONS_DONE = 0) do
        0|>ignore

    system.Terminate() |> ignore
    0

main fsi.CommandLineArgs