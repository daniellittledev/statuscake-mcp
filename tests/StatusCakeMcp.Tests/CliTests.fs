module StatusCakeMcp.Tests.CliTests

open Xunit
open StatusCakeMcp
open StatusCakeMcp.Cli

let private ok (cmd: Command) (result: Result<Command, string>) =
    match result with
    | Ok c -> Assert.Equal<Command>(cmd, c)
    | Error e -> Assert.Fail(sprintf "expected Ok but got Error: %s" e)

let private isError = function
    | Error _ -> ()
    | Ok c -> Assert.Fail(sprintf "expected Error but got Ok: %A" c)

[<Fact>]
let ``no args is Help`` () = ok Help (parse [||])

[<Theory>]
[<InlineData("--help")>]
[<InlineData("-h")>]
let ``help flags are Help`` (flag: string) = ok Help (parse [| flag |])

[<Fact>]
let ``list with no flags uses defaults`` () =
    ok (List("", "", 1, 50)) (parse [| "list" |])

[<Fact>]
let ``list parses all flags`` () =
    ok (List("ea", "down", 2, 10)) (parse [| "list"; "--filter"; "ea"; "--status"; "down"; "--page"; "2"; "--limit"; "10" |])

[<Fact>]
let ``down with no flags uses defaults`` () =
    ok (Down("", 1, 50)) (parse [| "down" |])

[<Fact>]
let ``down parses filter and paging`` () =
    ok (Down("mci", 3, 25)) (parse [| "down"; "--filter"; "mci"; "--page"; "3"; "--limit"; "25" |])

[<Fact>]
let ``get captures positional id`` () = ok (Get "123") (parse [| "get"; "123" |])

[<Fact>]
let ``history uses default limit`` () = ok (History("123", 10)) (parse [| "history"; "123" |])

[<Fact>]
let ``history parses limit`` () = ok (History("123", 5)) (parse [| "history"; "123"; "--limit"; "5" |])

[<Fact>]
let ``ssl uses defaults`` () = ok (Ssl(30, 1, 50)) (parse [| "ssl" |])

[<Fact>]
let ``ssl parses days`` () = ok (Ssl(7, 1, 50)) (parse [| "ssl"; "--days"; "7" |])

[<Fact>]
let ``pause and resume capture id`` () =
    ok (Pause "456") (parse [| "pause"; "456" |])
    ok (Resume "789") (parse [| "resume"; "789" |])

[<Fact>]
let ``unknown command is an error`` () = isError (parse [| "frobnicate" |])

[<Fact>]
let ``get without id is an error`` () = isError (parse [| "get" |])

[<Fact>]
let ``pause without id is an error`` () = isError (parse [| "pause" |])

[<Fact>]
let ``unknown flag is an error`` () = isError (parse [| "list"; "--bogus"; "x" |])

[<Fact>]
let ``non-numeric limit is an error`` () = isError (parse [| "list"; "--limit"; "abc" |])

[<Fact>]
let ``flag without value is an error`` () = isError (parse [| "list"; "--limit" |])
