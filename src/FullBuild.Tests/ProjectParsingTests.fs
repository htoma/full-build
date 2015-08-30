﻿module ProjectParsingTests

open System
open System.IO
open System.Linq
open System.Xml.Linq
open ProjectParsing
open NUnit.Framework
open FsUnit
open Anthology
open StringHelpers
open MsBuildHelpers

let XDocumentLoader (fi : FileInfo) : XDocument option =
    let fileName = match fi.Name with
                   | "packages.config" -> "packages.xml"
                   | x -> x
    let xdoc = XDocument.Load (fileName)
    Some xdoc


[<Test>]
let CheckCastString () =
    let x = XElement (XNamespace.None + "Test", "42")
    let xs : string = !> x
    let xi : int = !> x
    xs |> should equal "42"
    xi |> should equal 42

[<Test>]
let CheckBasicParsingCSharp () =
    let expectedPackages = [ { Id=PackageId.Bind "FSharp.Data"; Version=PackageVersion "2.2.5" }
                             { Id=PackageId.Bind "FsUnit"; Version=PackageVersion "1.3.0.1" }
                             { Id=PackageId.Bind "Mini"; Version=PackageVersion "0.4.2.0" }
                             { Id=PackageId.Bind "Newtonsoft.Json"; Version=PackageVersion "7.0.1" }
                             { Id=PackageId.Bind "NLog"; Version=PackageVersion "4.0.1" }
                             { Id=PackageId.Bind "NUnit"; Version=PackageVersion "2.6.3" }
                             { Id=PackageId.Bind "xunit"; Version=PackageVersion "1.9.1" } ]
    
    let file = FileInfo ("./CSharpProjectSample1.xml")
    let prjDescriptor = ProjectParsing.ParseProjectContent XDocumentLoader file.Directory (RepositoryId.Bind "Test") file
    prjDescriptor.Project.ProjectGuid |> should equal (ProjectId (ParseGuid "3AF55CC8-9998-4039-BC31-54ECBFC91396"))
    prjDescriptor.Packages |> should equal expectedPackages

[<Test>]
let CheckBasicParsingFSharp () =
    let file = FileInfo ("./FSharpProjectSample1.xml")
    let prjDescriptor = ProjectParsing.ParseProjectContent XDocumentLoader file.Directory (RepositoryId.Bind "Test") file
    prjDescriptor.Project.ProjectGuid |> should equal (ProjectId (ParseGuid "5fde3939-c144-4287-bc57-a96ec2d1a9da"))

[<Test>]
let CheckParseVirginProject () =
    let file = FileInfo ("./VirginProject.xml")
    let prjDescriptor = ProjectParsing.ParseProjectContent XDocumentLoader file.Directory (RepositoryId.Bind "Test") file
    prjDescriptor.Project.ProjectReferences |> should equal [ProjectId.Bind (ParseGuid "c1d252b7-d766-4c28-9c46-0696f896846d")]


[<Test>]
let CheckParseConvertedProject () =
    let expectedAssemblies = Set [ AssemblyId.Bind "System"
                                   AssemblyId.Bind "System.Numerics"
                                   AssemblyId.Bind "System.Xml"
                                   AssemblyId.Bind "System.Configuration" ]

    let expectedPackages = Set [ { Id=PackageId.Bind "Rx-Core"; Version=PackageVersion "" }
                                 { Id=PackageId.Bind "Rx-Interfaces"; Version=PackageVersion "" }
                                 { Id=PackageId.Bind "Rx-Linq"; Version=PackageVersion "" }
                                 { Id=PackageId.Bind "Rx-PlatformServices"; Version=PackageVersion "" }
                                 { Id=PackageId.Bind "FSharp.Data"; Version=PackageVersion "2.2.5" }
                                 { Id=PackageId.Bind "FsUnit"; Version=PackageVersion "1.3.0.1" }
                                 { Id=PackageId.Bind "Mini"; Version=PackageVersion "0.4.2.0" }
                                 { Id=PackageId.Bind "Newtonsoft.Json"; Version=PackageVersion "7.0.1" }
                                 { Id=PackageId.Bind "NLog"; Version=PackageVersion "4.0.1" }
                                 { Id=PackageId.Bind "NUnit"; Version=PackageVersion "2.6.3" }
                                 { Id=PackageId.Bind "xunit"; Version=PackageVersion "1.9.1" } ]

    let expectedProject = { Repository = RepositoryId.Bind "Test"
                            RelativeProjectFile = ProjectRelativeFile "ConvertedProject.xml"
                            ProjectGuid = ProjectId.Bind (ParseGuid "c1d252b7-d766-4c28-9c46-0696f896846d") 
                            Output = AssemblyId.Bind "CassandraSharp"
                            OutputType = OutputType.Dll
                            FxTarget = FrameworkVersion "v4.5"
                            AssemblyReferences = Set [ AssemblyId.Bind "System"
                                                       AssemblyId.Bind "System.Numerics"
                                                       AssemblyId.Bind "System.Xml"
                                                       AssemblyId.Bind "System.Configuration" ]
                            PackageReferences = Set [ PackageId.Bind "Rx-Core"
                                                      PackageId.Bind "Rx-Interfaces"
                                                      PackageId.Bind "Rx-Linq"
                                                      PackageId.Bind "Rx-PlatformServices"
                                                      PackageId.Bind "FSharp.Data"
                                                      PackageId.Bind "FsUnit"
                                                      PackageId.Bind "Mini"
                                                      PackageId.Bind "Newtonsoft.Json"
                                                      PackageId.Bind "NLog"
                                                      PackageId.Bind "NUnit"
                                                      PackageId.Bind "xunit" ]
                            ProjectReferences = Set [ ProjectId.Bind (ParseGuid "6f6eb447-9569-406a-a23b-c09b6dbdbe10") ] }

    let expectedProjects = Set [ ProjectId.Bind (ParseGuid "6f6eb447-9569-406a-a23b-c09b6dbdbe10") ]

    let projectFile = FileInfo ("./ConvertedProject.xml")
    let prjDescriptor = ProjectParsing.ParseProjectContent XDocumentLoader projectFile.Directory (RepositoryId.Bind "Test") projectFile
    prjDescriptor.Project.ProjectReferences |> should equal [ProjectId.Bind (ParseGuid "6f6eb447-9569-406a-a23b-c09b6dbdbe10")]

    prjDescriptor.Packages |> Seq.iter (fun x -> printfn "%A" x.Id.Value)

    prjDescriptor.Packages |> should equal expectedPackages
    prjDescriptor.Project |> should equal expectedProject