﻿// Copyright (c) 2014-2015, Pierre Chalamet
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//     * Redistributions of source code must retain the above copyright
//       notice, this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//     * Neither the name of Pierre Chalamet nor the
//       names of its contributors may be used to endorse or promote products
//       derived from this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL PIERRE CHALAMET BE LIABLE FOR ANY
// DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
// ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
module Workspace

open System.IO
open IoHelpers
open Env
open Configuration
open Vcs
open Anthology
open MsBuildHelpers
open System.Linq
open System.Xml.Linq
open StringHelpers
open Collections
open System

let private FindKnownProjects (repoDir : DirectoryInfo) =
    [AddExt "*" CsProj
     AddExt "*" VbProj
     AddExt "*" FsProj] |> Seq.map (fun x -> repoDir.EnumerateFiles (x, SearchOption.AllDirectories)) 
                        |> Seq.concat

let private ParseRepositoryProjects (parser) (repoRef : RepositoryRef) (repoDir : DirectoryInfo) =
    repoDir |> FindKnownProjects 
            |> Seq.map (parser repoDir repoRef)

let private ParseWorkspaceProjects (parser) (wsDir : DirectoryInfo) (repos : Repository seq) = 
    repos |> Seq.map (fun x -> GetSubDirectory x.Name wsDir) 
          |> Seq.filter (fun x -> x.Exists) 
          |> Seq.map (fun x -> ParseRepositoryProjects parser (RepositoryRef.Bind(x.Name)) x)
          |> Seq.concat

let Init(path : string) = 
    let wsDir = DirectoryInfo(path)
    wsDir.Create()
    if IsWorkspaceFolder wsDir then failwith "Workspace already exists"
    VcsCloneRepo wsDir GlobalConfig.Repository

let Create(path : string) = 
    let wsDir = DirectoryInfo(path)
    wsDir.Create()
    if IsWorkspaceFolder wsDir then failwith "Workspace already exists"
    VcsCloneRepo wsDir GlobalConfig.Repository
    let antho = { Applications = Set.empty
                  Bookmarks = Set.empty
                  Repositories = Set.empty
                  Projects = Set.empty }
    Configuration.SaveAnthology antho

    failwith "FIXME"
    // FIXME
    //  create git repo in .full-build
    //       generate .gitignore
    //       add content of .full-build to git repo
    //       commit

let Index () = 
    let wsDir = WorkspaceFolder()
    let antho = LoadAnthology()
    let projects = ParseWorkspaceProjects ProjectParsing.ParseProject wsDir antho.Repositories

    // FIXME: before merging, it would be better to tell about conflicts

    // merge packages
    let foundPackages = projects |> Seq.map (fun x -> x.Packages) 
                                 |> Seq.concat
    let existingPackages = PaketParsing.ParsePaketDependencies ()
    let packagesToAdd = foundPackages |> Seq.filter (fun x -> not <| Set.contains x.Id existingPackages)
                                      |> Seq.distinctBy (fun x -> x.Id)
                                      |> Set
    PaketParsing.AppendDependencies packagesToAdd

    // merge projects
    let foundProjects = projects |> Seq.map (fun x -> x.Project)
    let newProjects = antho.Projects |> Seq.append foundProjects 
                                     |> Seq.distinctBy ProjectRef.Bind 
                                     |> set

    let newAntho = { antho 
                     with Projects = newProjects }

    SaveAnthology newAntho


let StringifyOutputType (outputType : OutputType) =
    match outputType with
    | OutputType.Exe -> ".exe"
    | OutputType.Dll -> ".dll"
    | _ -> failwithf "Unknown OutputType %A" outputType


let GenerateProjectTarget (project : Project) =
    let projectProperty = ProjectPropertyName project
    let srcCondition = sprintf "'$(%s)' != ''" projectProperty
    let binCondition = sprintf "'$(%s)' == ''" projectProperty
    let projectFile = sprintf "%s/%s/%s" MSBUILD_SOLUTION_DIR (project.Repository.Print()) project.RelativeProjectFile
    let binFile = sprintf "%s/%s/%s%s" MSBUILD_SOLUTION_DIR MSBUILD_BIN_OUTPUT (project.Output.Print ()) <| StringifyOutputType project.OutputType

    // This is the import targets that will be Import'ed inside a proj file.
    // First we include full-build view configuration (this is done to avoid adding an extra import inside proj)
    // Then we end up either importing output assembly or project depending on view configuration
    XDocument (
        XElement(NsMsBuild + "Project", 
            XElement (NsMsBuild + "Import",
                XAttribute (NsNone + "Project", "$(SolutionDir)/.full-build/views/$(SolutionName).targets"),
                XAttribute (NsNone + "Condition", "'$(FullBuild_Config)' == ''")),
            XElement (NsMsBuild + "ItemGroup",
                XElement(NsMsBuild + "ProjectReference",
                    XAttribute (NsNone + "Include", projectFile),
                    XAttribute (NsNone + "Condition", srcCondition),
                    XElement (NsMsBuild + "Project", StringifyGuid project.ProjectGuid),
                    XElement (NsMsBuild + "Name", project.Output.Print())),
                XElement (NsMsBuild + "Reference",
                    XAttribute (NsNone + "Include", project.Output.Print()),
                    XAttribute (NsNone + "Condition", binCondition),
                    XElement (NsMsBuild + "HintPath", binFile),
                    XElement (NsMsBuild + "Private", "true")))))

let GenerateProjects (projects : Project seq) (xdocSaver : FileInfo -> XDocument -> Unit) =
    let prjDir = WorkspaceProjectFolder ()
    for project in projects do
        let content = GenerateProjectTarget project
        let projectFile = prjDir |> GetFile (AddExt (project.ProjectGuid.ToString("D")) Targets)
        xdocSaver projectFile content

let ConvertProject (xproj : XDocument) (project : Project) (nugetFiles : Set<AssemblyRef>) =
    let filterProject (xel : XElement) =
        let attr = !> (xel.Attribute (NsNone + "Project")) : string
        attr.StartsWith(MSBUILD_PROJECT_FOLDER, StringComparison.CurrentCultureIgnoreCase)

    let filterPackage (xel : XElement) =
        let attr = !> (xel.Attribute (NsNone + "Project")) : string
        attr.StartsWith(MSBUILD_PACKAGE_FOLDER, StringComparison.CurrentCultureIgnoreCase)

    let filterNuget (xel : XElement) =
        let attr = !> (xel.Attribute (NsNone + "Project")) : string
        attr.StartsWith("$(SolutionDir)\.nuget\NuGet.targets", StringComparison.CurrentCultureIgnoreCase)

    let filterNugetTarget (xel : XElement) =
        let attr = !> (xel.Attribute (NsNone + "Name")) : string
        String.Equals(attr, "EnsureNuGetPackageBuildImports", StringComparison.CurrentCultureIgnoreCase)

    let filterAssemblies (assFiles) (xel : XElement) =
        let inc = !> xel.Attribute(XNamespace.None + "Include") : string
        let assName = inc.Split([| ',' |], StringSplitOptions.RemoveEmptyEntries).[0]
        let assRef = AssemblyRef.Bind (System.Reflection.AssemblyName(assName))
        nugetFiles |> Set.contains assRef

    let hasNoChild (xel : XElement) =
        not <| xel.DescendantNodes().Any()

    let setOutputPath (xel : XElement) =
        xel.Value <- MSBUILD_BIN_FOLDER

    let stringifyGuid (guid : System.Guid) =
        guid.ToString("D")

    // cleanup everything that will be modified
    let cproj = XDocument (xproj)

    // remove project references
    cproj.Descendants(NsMsBuild + "ProjectReference").Remove()
    
    // remove file references (assemblies & nuget)
    let filesToRemove = nugetFiles |> Set.union project.AssemblyReferences
//    cproj.Descendants(NsMsBuild + "Reference").Where(filterNuget nugetFiles).Remove()
    cproj.Descendants(NsMsBuild + "Reference").Where(filterAssemblies filesToRemove).Remove()

    // remove full-build imports
    cproj.Descendants(NsMsBuild + "Import").Where(filterProject).Remove()
    cproj.Descendants(NsMsBuild + "Import").Where(filterPackage).Remove()

    // remove nuget stuff
    cproj.Descendants(NsMsBuild + "Import").Where(filterNuget).Remove()
    cproj.Descendants(NsMsBuild + "Target").Where(filterNugetTarget).Remove()

    // set OutputPath
    cproj.Descendants(NsMsBuild + "OutputPath") |> Seq.iter setOutputPath

    // cleanup project
    cproj.Descendants(NsMsBuild + "BaseIntermediateOutputPath").Remove()
    cproj.Descendants(NsMsBuild + "ItemGroup").Where(hasNoChild).Remove()

    // add project refereces
    let afterItemGroup = cproj.Descendants(NsMsBuild + "ItemGroup").First()
    for projectReference in project.ProjectReferences do
        let prjRef = projectReference.Print()
        let importFile = sprintf "%s%s.targets" MSBUILD_PROJECT_FOLDER prjRef
        let import = XElement (NsMsBuild + "Import",
                        XAttribute (NsNone + "Project", importFile))
        afterItemGroup.AddAfterSelf (import)

    // add nuget references
    for packageReference in project.PackageReferences do
        let pkgId = packageReference.Print()
        let importFile = sprintf "%s%s/package.targets" MSBUILD_PACKAGE_FOLDER pkgId
        let pkgProperty = PackagePropertyName pkgId
        let condition = sprintf "'$(%s)' == ''" pkgProperty
        let import = XElement (NsMsBuild + "Import",
                        XAttribute (NsNone + "Project", importFile),
                        XAttribute(NsNone + "Condition", condition))
        afterItemGroup.AddAfterSelf (import)
    cproj

let ConvertProjectContent (xproj : XDocument) (project : Project) (package2Files : Map<PackageRef, Set<AssemblyRef>>) =
    let usedPackage2Files = package2Files |> Map.filter (fun id _ -> project.PackageReferences |> Set.contains id)
    let nugetFiles = usedPackage2Files |> Map.toSeq
                                       |> Seq.map (fun (id, files) -> files)
                                       |> Seq.concat
                                       |> Set

    let convxproj = ConvertProject xproj project nugetFiles
    convxproj

let ConvertProjects (antho : Anthology) (package2Files : Map<PackageRef, Set<AssemblyRef>>) xdocLoader xdocSaver =
    let wsDir = WorkspaceFolder ()
    for project in antho.Projects do
        let repoDir = wsDir |> GetSubDirectory (project.Repository.Print())
        let projFile = repoDir |> GetFile project.RelativeProjectFile 
        printfn "Converting %A" projFile.FullName
        let xproj = xdocLoader projFile
        let convxproj = ConvertProjectContent xproj project package2Files

        xdocSaver projFile convxproj

let RemoveUselessStuff (antho : Anthology) =
    let wsDir = WorkspaceFolder ()
    for repo in antho.Repositories do
        let repoDir = wsDir |> GetSubDirectory (repo.Name)
        repoDir.EnumerateFiles("*.sln") |> Seq.iter (fun x -> x.Delete())
        repoDir.EnumerateFiles("packages.config") |> Seq.iter (fun x -> x.Delete())
        repoDir.EnumerateDirectories("packages") |> Seq.iter (fun x -> x.Delete(true))

let XDocumentLoader (fileName : FileInfo) =
    XDocument.Load fileName.FullName

let XDocumentSaver (fileName : FileInfo) (xdoc : XDocument) =
    xdoc.Save (fileName.FullName)

let Convert () = 
    let antho = LoadAnthology ()

    // generate project targets
    GenerateProjects antho.Projects XDocumentSaver

    // generate paket.dependencies and install packages
    Package.Install ()

    // for each package, get all assemblies
    let packageRefs = antho.Projects |> Seq.map (fun x -> x.PackageReferences)
                                     |> Seq.concat
                                     |> Set
    let package2Files = packageRefs 
                        |> Seq.map (fun x -> (x, Package.GatherAllAssemblies x))
                        |> Map

    ConvertProjects antho package2Files XDocumentLoader XDocumentSaver
    RemoveUselessStuff antho

