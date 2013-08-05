﻿namespace Slumber.Tests

open FsUnit
open Xunit
open Xunit.Extensions
open System
open System.IO
open Slumber

module ``Discovery facts`` =

    open Discovery
    open Http
    open Configuration
    open Setup.Bindings

    let teapot (_ : string option) = 
        OperationResult.StatusOnly 418 //Teapot

    let endpoint = 
        {
            Template = "/people/{personCode}";
            Bindings = [ get teapot ]; 
        }

    let [<Literal>] ModuleName = "Discovery"

    [<AutoOpen>] 
    module Helpers = 
        
        let getArgs (relativeUrl : String) =     
        
            let reader _ _ = 
                None

            let writer _ = 
                []

            let relativeUrl' = 
                if (relativeUrl.StartsWith "/") then
                    relativeUrl.Substring (1)
                else
                    relativeUrl

            let baseUrl = 
                Uri ("http://localhost:8080/api/", UriKind.Absolute)

            {
                Request = 
                    {
                        Request.Empty 
                        with
                            Url = 
                                {
                                    Urls.Empty
                                    with
                                        Raw = Uri (baseUrl, relativeUrl');
                                        Path = relativeUrl;
                                        BaseUrl = baseUrl;
                                };
                            Verb = "GET";
                            Payload = 
                                {
                                    Payload.Empty
                                    with
                                        Body = (Some (new MemoryStream () :> Stream));
                                }
                    } ;
                Container =
                    {
                        Container.Empty
                        with
                            Endpoints = [ endpoint; ];
                            IO = 
                                {
                                    IOConfig.Empty
                                    with
                                        Readers = [ ("text/xml", reader); ("application/json", reader) ];
                                        Writers = [ ("text/xml", writer); ("application/json", writer) ];
                                }
                            BaseUrl = baseUrl;
                    }
            }

        let addHeader name value args = 

            let payload'  = 
                { args.Request.Payload with Headers = (name, value) :: args.Request.Payload.Headers; }

            { args with Request = { args.Request with Payload = payload'; }; }

        let addForwardedType from to' args = 

            let container' =
                {
                    args.Container
                    with
                        IO = 
                            {
                                args.Container.IO
                                with
                                    ForwardedTypes = [ (from, to'); ];
                            }
                }

            { args with Container = container'; }
      
        let makePrivate auth args = 
            { args with Container = { args.Container with SecurityMode = (Private auth); }; }

    module ``Matching facts`` = 

        open Discovery.Matching
        open Attempt

        let [<Literal>] ModuleName = "Discovery.Matching"

        [<Trait (Traits.Names.Module, ModuleName)>]
        module ``asyncMatchEndpoint function`` = 

            let matchEndpoint = 
                asyncMatchEndpoint ()
                >> Async.RunSynchronously

            let [<Fact>] ``Unregistered URL returns HTTP 404`` () =

                let isNotFound outcome = 
                    match outcome with
                    | Failure statusCode -> statusCode = StatusCodes.NotFound
                    | _ -> false

                "/addresses"
                |> getArgs
                |> matchEndpoint
                |> isNotFound
                |> should be True

            let [<Fact>] ``Matching URL returns success`` () = 

                let isSuccess outcome = 
                    match outcome with
                    | Success _ -> true
                    | _ -> false

                "/people/12345"
                |> getArgs
                |> matchEndpoint
                |> isSuccess
                |> should be True

            let [<Fact>] ``Trailing slashes are ignored`` () =

                let isSuccess outcome = 
                    match outcome with
                    | Success _ -> true
                    | _ -> false

                "/people/12345/"
                |> getArgs
                |> matchEndpoint
                |> isSuccess
                |> should be True

            let [<Fact>] ``Successful match includes correct endpoint`` () =

                let getTemplate outcome = 
                    match outcome with
                    | Success info -> info.Endpoint.Template
                    | _ -> String.Empty

                "/people/12345"
                |> getArgs
                |> matchEndpoint
                |> getTemplate
                |> should equal "/people/{personCode}"

            let [<Fact>] ``Successful match includes matched URL parameters`` () =
                
                let getParameters outcome = 
                    match outcome with
                    | Success (info : EndpointInfo) -> info.Parameters
                    | _ -> []

                "/people/12345"
                |> getArgs
                |> matchEndpoint
                |> getParameters
                |> List.same [ ("PERSONCODE", "12345") ] //Note UriTemplate converts placeholders to upper case. Potentially annoying.
                |> should be True

        [<Trait (Traits.Names.Module, ModuleName)>]
        module ``asyncMatchBinding function`` =

            let matchBinding endpoint = 
                "/people/12345"
                |> getArgs
                |> asyncMatchBinding endpoint 
                |> Async.RunSynchronously

            let [<Fact>] ``No registered binding returns HTTP 405`` () = 

                let isNotSupported outcome = 
                    match outcome with
                    | Failure statusCode -> statusCode = StatusCodes.MethodNotAllowed
                    | _ -> false

                let endpoint' = 
                    {
                        Endpoint = { endpoint with Bindings = [ post teapot ] };
                        Parameters = [];
                    }

                endpoint'
                |> matchBinding
                |> isNotSupported
                |> should be True
                
            let [<Fact>] ``Registered binding returns success`` () =

                let isSuccess outcome = 
                    match outcome with
                    | Success _ -> true
                    | _ -> false

                let endpoint' = 
                    {
                        Endpoint = endpoint;
                        Parameters = [];
                    }

                endpoint'
                |> matchBinding
                |> isSuccess
                |> should be True

            let [<Fact>] ``Success includes URL parameters`` () =
                
                let getParameters outcome = 
                    match outcome with
                    | Success (info : BindingInfo) -> info.Parameters
                    | _ -> []

                let endpoint' =
                    {
                        Endpoint = endpoint;
                        Parameters = [ ("PERSONCODE", "12345") ];
                    }

                endpoint'
                |> matchBinding
                |> getParameters
                |> List.same [ ("PERSONCODE", "12345") ]
                |> should be True

            let [<Fact>] ``Success includes operation`` () =
                
                let executeOp outcome = 
                    
                    let (context : OperationContext) = 

                        let baseUrl = 
                            Uri ("http://localhost/api", UriKind.Absolute)

                        {
                            Metadata = 
                                {
                                    OperationMetadata.Empty
                                    with
                                        Url = 
                                            {
                                                Raw = Uri (baseUrl, "people/12345");
                                                Path = "/people/12345";
                                                Query = [];
                                                BaseUrl = baseUrl;
                                            };
                                        Parameters = [];
                                };
                            Message = None;
                        }

                    match outcome with
                    | Success (info : BindingInfo) -> Some (info.Binding.Operation context)
                    | _ -> None

                let endpoint' = 
                    {
                        Endpoint = endpoint;
                        Parameters = [];
                    }
                
                endpoint'
                |> matchBinding
                |> executeOp
                |> should be (Some' ({ StatusCode = Some 418; Resource = None; Headers = []; }))

            let [<Fact>] ``Success includes message type`` () =

                let isStringMessage result = 
                    match result with
                    | Success (info : BindingInfo) -> info.Binding.MessageType = (Some typedefof<String>)
                    | _ -> false

                let endpoint' = 
                    {
                        Endpoint = endpoint;
                        Parameters = [];
                    }

                endpoint'
                |> matchBinding
                |> isStringMessage
                |> should be True

        [<Trait (Traits.Names.Module, ModuleName)>]
        module ``asyncGetBinding function`` =

            let getBinding = 
                asyncGetBinding
                >> Async.RunSynchronously

            let [<Fact>] ``No registered URL returns HTTP 404`` () =
                
                let isNotFound outcome = 
                    match outcome with
                    | Failure statusCode -> statusCode = 404
                    | _ -> false

                "/addresses"
                |> getArgs
                |> getBinding
                |> isNotFound
                |> should be True

            let [<Fact>] ``No registered operation returns HTTP 405`` () =
                
                let isNotSupported outcome = 
                    match outcome with
                    | Failure statusCode -> statusCode = 405
                    | _ -> false

                let changeVerb args = 
                    { args with Request = { args.Request with Verb = "POST"; }; }

                "/people/12345"
                |> getArgs
                |> changeVerb
                |> getBinding
                |> isNotSupported
                |> should be True

            let [<Fact>] ``Registered URL and binding returns success`` () =

                let isSuccess outcome = 
                    match outcome with
                    | Success _ -> true
                    | _ -> false

                "/people/12345"
                |> getArgs
                |> getBinding
                |> isSuccess
                |> should be True

    module ``Security facts`` =

        open Security

        let [<Literal>] ModuleName = "Discovery.Security"
            
        [<Trait (Traits.Names.Module, ModuleName)>]
        module ``asyncAuthenticateRequest function`` =
            
            open Matching

            [<AutoOpen>]
            module Helpers = 

                let authenticateRequest binding args = 
                    asyncAuthenticateRequest binding args
                    |> Async.RunSynchronously

                let isSuccess result = 
                    match result with
                    | Success _ -> true
                    | _ -> false

                let publicBinding = 
                    { Binding = { Binding.Empty with IsPublic = true; }; Parameters = []; }

                let privateBinding = 
                    { Binding = { Binding.Empty with IsPublic = false; }; Parameters = []; }


                let isUnauthorised result = 
                    match result with
                    | Failure statusCode -> statusCode = StatusCodes.Unauthorised
                    | _ -> false

            let [<Fact>] ``Public binding returns success in private container`` () =
                getArgs "/people/12345"
                |> makePrivate (fun _ -> Deny)
                |> authenticateRequest publicBinding
                |> isSuccess
                |> should be True

            let [<Fact>] ``Public binding returns success in public container`` () =
                getArgs "/people/12345"
                |> authenticateRequest publicBinding
                |> isSuccess
                |> should be True

            let [<Fact>] ``Public binding returns no user data`` () =
                
                let isEmpty result = 
                    match result with
                    | Success None -> true
                    | _ -> false

                getArgs "/people/12345"
                |> authenticateRequest publicBinding
                |> isEmpty
                |> should be True
        
            let [<Fact>] ``Private binding returns success if authentication is successful`` () =
                getArgs "/people/12345"
                |> makePrivate (fun _ -> Allow (None))
                |> authenticateRequest privateBinding
                |> isSuccess
                |> should be True

            let [<Fact>] ``Private binding returns user data if authentication is successful`` () =

                let isDataCorrect result = 
                    match result with
                    | Success user -> 
                        match user with
                        | Some (data : UserData) -> data.Id = "user.name"
                        | _ -> false
                    | _ -> false

                getArgs "/people/12345"
                |> makePrivate (fun _ -> Allow (Some { Id = "user.name"; Properties = []; }))
                |> authenticateRequest privateBinding
                |> isDataCorrect
                |> should be True

            let [<Fact>] ``Private binding returns success if no authentication function is set`` () =
                getArgs "/people/12345"
                |> authenticateRequest privateBinding
                |> isSuccess
                |> should be True

            let [<Fact>] ``Private binding returns HTTP 401 if authentication fails`` () =
                getArgs "/people/12345"
                |> makePrivate (fun _ -> Deny)
                |> authenticateRequest privateBinding
                |> isUnauthorised
                |> should be True

    module ``Negotiation facts`` =

        open Discovery.Negotiation

        let [<Literal>] ModuleName = "Discovery.Negotiation"

        [<Trait (Traits.Names.Module, ModuleName)>]
        module ``asyncGetReader function`` =

            let getRequestType = 

                let getContentType result = 
                    match result with
                    | Some (contentType, _) -> Some contentType
                    | _ -> None

                asyncGetReader
                >> Async.RunSynchronously
                >> getContentType

            let [<Fact>] ``Specified content type is selected if supported`` () =
                getArgs "/people/12345"
                |> addHeader "Content-Type" "text/xml"
                |> getRequestType
                |> should be (Some' "text/xml")

            let [<Fact>] ``Nothing is selected if content type is not supported`` () =
                getArgs "/people/12345"
                |> addHeader "Content-Type" "text/html"
                |> getRequestType
                |> should be None'<String>

            let [<Fact>] ``Default content type is selected if none specified`` () =
                getArgs "/people/12345"
                |> getRequestType
                |> should be (Some' DefaultContentType)

            let [<Fact>] ``Target content type is used if specified content type is forwarded`` () =
                getArgs "/people/12345"
                |> addHeader "Content-Type" "text/html"
                |> addForwardedType "text/html" "text/xml"
                |> getRequestType
                |> should be (Some' "text/xml")

            let [<Fact>] ``Target content type is used in preference to specified content type even if supported`` () =
                getArgs "/people/12345"
                |> addHeader "Content-Type" "text/xml"
                |> addForwardedType "text/xml" "application/json"
                |> getRequestType
                |> should be (Some' "application/json")
                
        [<Trait (Traits.Names.Module, ModuleName)>]
        module ``asyncGetWriter function`` =

            let getResponseType =

                let getContentType result = 
                    match result with
                    | Some (contentType, _) -> Some contentType
                    | _ -> None

                asyncGetWriter
                >> Async.RunSynchronously
                >> getContentType

            let [<Fact>] ``Specified content type is selected if supported`` () =
                getArgs "/people/12345"
                |> addHeader "Accept" "text/xml"
                |> getResponseType
                |> should be (Some' "text/xml")

            let [<Fact>] ``First supported content type is selected if multiple types are specified`` () =
                getArgs "/people/12345"
                |> addHeader "Accept" "text/html, text/xml, application/json"
                |> getResponseType
                |> should be (Some' "text/xml")

            let [<Fact>] ``Nothing is selected if content type is not supported`` () =
                getArgs "/people/12345"
                |> addHeader "Accept" "text/html"
                |> getResponseType
                |> should be None'<String>

            let [<Fact>] ``Default content type is selected if none specified`` () =
                getArgs "/people/12345"
                |> getResponseType
                |> should be (Some' DefaultContentType)

            let [<Fact>] ``Default content type is selected if */* is specified`` () =
                getArgs "/people/12345"
                |> addHeader "Accept" "*/*"
                |> getResponseType
                |> should be (Some' DefaultContentType)

            let [<Fact>] ``Target content type is used if specified content type is forwarded`` () =
                getArgs "/people/12345"
                |> addHeader "Accept" "text/html"
                |> addForwardedType "text/html" "text/xml"
                |> getResponseType
                |> should be (Some' "text/xml")

            let [<Fact>] ``Target content type is used in preference to specified content type even if supported`` () =
                getArgs "/people/12345"
                |> addHeader "Accept" "text/xml"
                |> addForwardedType "text/xml" "application/json"
                |> getResponseType
                |> should be (Some' "application/json")

            let [<Fact>] ``Content type forwarding is applied when multiple content types are specified`` () =
                getArgs "/people/12345"
                |> addHeader "Accept" "text/html, text/plain, application/json"
                |> addForwardedType "text/html" "text/xml"
                |> getResponseType
                |> should be (Some' "text/xml")

    [<Trait (Traits.Names.Module, ModuleName)>]
    module ``run function`` = 

        open Framework
        open Discovery.Negotiation

        let [<Fact>] ``Unsupported content type returns HTTP 415`` () =

            let isNotSupported state = 
                match state with
                | Stopped stopType -> 
                    match stopType with
                    | Completed response ->
                        match response.ResponseType with
                        | StatusCode statusCode -> statusCode = StatusCodes.ContentTypeNotSupported
                        | _ -> false
                    | _ -> false
                | _ -> false

            getArgs "/people/12345"
            |> addHeader "Content-Type" "text/html"
            |> run
            |> isNotSupported
            |> should be True

        let [<Fact>] ``Bindings without messages return no reader information`` () =

            let hasNoReader state = 
                match state with
                | Running (args : Execution.ExecutionArgs) -> Option.isNone args.Reader
                | _ -> false

            let setBinding args = 

                let binding = 
                    get (fun () -> "Hello, World")

                let container = 
                    { args.Container with Endpoints = [ { endpoint with Bindings = [ binding; ]; } ]; }

                { args with Container = container; }

            getArgs "/people/12345"
            |> setBinding
            |> run
            |> hasNoReader
            |> should be True

        let [<Fact>] ``Bindings with messages return correct reader information`` () =
            
            let isReaderCorrect state = 
                match state with
                | Running (args : Execution.ExecutionArgs) -> 
                    match args.Reader with
                    | Some reader -> 
                        reader.ContentType = DefaultContentType
                            && reader.MessageType = typedefof<String>
                    | _ -> false
                | _ -> false

            getArgs "/people/12345"
            |> run
            |> isReaderCorrect
            |> should be True

        let [<Fact>] ``Correct writer information is returned`` () =
            
            let isWriterCorrect state = 
                match state with
                | Running (args : Execution.ExecutionArgs) ->
                    match args.Writer with
                    | Some writer -> writer.ContentType = DefaultContentType
                    | _ -> false
                | _ -> false

            getArgs "/people/12345"
            |> run
            |> isWriterCorrect
            |> should be True

        let [<Fact>] ``Unauthorised request returns HTTP 401`` () =

            let isUnauthorised' state = 
                match state with
                | Stopped stopType ->
                    match stopType with
                    | Completed response ->
                        match response.ResponseType with
                        | StatusCode statusCode -> statusCode = StatusCodes.Unauthorised
                        | _ -> false
                    | _ -> false
                | _ -> false

            getArgs "/people/12345"
            |> makePrivate (fun _ -> Deny)
            |> run
            |> isUnauthorised'
            |> should be True

        let [<Fact>] ``User details are returned if provided by authentication function`` () =
            
            let hasUserDetails state = 
                match state with
                | Running (args : Execution.ExecutionArgs) -> 
                    match args.User with
                    | Some data -> data.Id = "user.name"
                    | _ -> false
                | _ -> false

            getArgs "/people/12345"
            |> makePrivate (fun _ -> Allow (Some { Id = "user.name"; Properties = []; }))
            |> run
            |> hasUserDetails
            |> should be True
