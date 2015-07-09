﻿module FileExtensionsTests

open System
open System.IO
open FileExtensions
open NUnit.Framework
open FsUnit

[<Test>]
let CheckGetSubDirectory () =
    let currDir = new DirectoryInfo (Environment.CurrentDirectory)
    let subdir = currDir |> GetSubDirectory "toto"
    subdir.FullName.Contains("toto") |> should equal true

[<Test>]
let CheckGetFile () =
    let currDir = new DirectoryInfo (Environment.CurrentDirectory)
    let file = currDir |> GetFile "toto"
    file.FullName.Contains("toto") |> should equal true