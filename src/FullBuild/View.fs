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
module View
open System.IO
open Env
open IoHelpers
open Anthology
open StringHelpers
open System.Xml.Linq
open MsBuildHelpers
open Configuration
open Collections
open Solution

let Drop (viewName : ViewId) =
    let vwDir = GetFolder Env.View
    let vwFile = GetFile (AddExt View viewName.toString) vwDir
    vwFile.Delete()

    let vwDefineFile = GetFile (AddExt Targets viewName.toString) vwDir
    vwDefineFile.Delete()

    let wsDir = Env.GetFolder Env.Workspace
    let viewFile = GetFile (AddExt Solution viewName.toString) wsDir
    viewFile.Delete ()

let List () =
    let vwDir = GetFolder Env.View
    vwDir.EnumerateFiles (AddExt  View "*") |> Seq.iter (fun x -> printfn "%s" (Path.GetFileNameWithoutExtension (x.Name)))

let Describe (viewName : ViewId) =
    let vwDir = GetFolder Env.View
    let vwFile = vwDir |> GetFile (AddExt View viewName.toString)
    File.ReadAllLines (vwFile.FullName) |> Seq.iter (fun x -> printfn "%s" x)


// find all referencing projects of a project
let private referencingProjects (projects : Project set) (current : ProjectId) =
    projects |> Set.filter (fun x -> x.ProjectReferences |> Set.contains current)

let rec private computePaths (findParents : ProjectId -> Project set) (goal : ProjectId set) (path : ProjectId set) (current : Project) =
    let currentId = current.ProjectGuid
    let parents = findParents currentId
    let newPath = Set.add currentId path
    let paths = parents |> Set.map (computePaths findParents goal (Set.add currentId path))
                        |> Set.unionMany
    if Set.contains currentId goal then Set.union newPath paths
    else paths

let ComputeProjectSelectionClosure (allProjects : Project set) (goal : ProjectId set) =
    let findParents = referencingProjects allProjects

    let seeds = allProjects |> Set.filter (fun x -> Set.contains x.ProjectGuid goal)
    let transitiveClosure = seeds |> Set.map (computePaths findParents goal Set.empty)
                                  |> Set.unionMany
    transitiveClosure

let FindViewProjects (viewName : ViewId) =
    // load back filter & generate view accordingly
    let vwDir = Env.GetFolder Env.View 
    let vwFile = vwDir |> GetFile (AddExt View viewName.toString)
    let filters = File.ReadAllLines(vwFile.FullName)

    let repoFilters = filters |> Set

    // build: <repository>/<project>
    let antho = Configuration.LoadAnthology ()
    let projects = antho.Projects |> Seq.map (fun x -> (sprintf "%s/%s" x.Repository.toString x.Output.toString, x.ProjectGuid))
                                  |> Map
    let projectNames = projects |> Seq.map (fun x -> x.Key) |> Set

    let matchRepoProject filter =
        projectNames |> Set.filter (fun x -> PatternMatching.Match x filter)

    let matches = repoFilters |> Set.map matchRepoProject
                              |> Set.unionMany
    let selectedProjectGuids = projects |> Map.filter (fun k v -> Set.contains k matches)
                                        |> Seq.map (fun x -> x.Value)
                                        |> Set

    // find projects
    let antho = Configuration.LoadAnthology ()
    let projectRefs = ComputeProjectSelectionClosure antho.Projects selectedProjectGuids |> Set
    let projects = antho.Projects |> Set.filter (fun x -> projectRefs |> Set.contains x.ProjectGuid)
    projects

let Generate (viewName : ViewId) =
    let projects = FindViewProjects viewName

    // generate solution defines
    let slnDefines = GenerateSolutionDefines projects
    let viewDir = GetFolder Env.View
    let slnDefineFile = viewDir |> GetFile (AddExt Targets viewName.toString)
    slnDefines.Save (slnDefineFile.FullName)

    // generate solution file
    let wsDir = GetFolder Env.Workspace
    let slnFile = wsDir |> GetFile (AddExt Solution viewName.toString)
    let slnContent = GenerateSolutionContent projects
    File.WriteAllLines (slnFile.FullName, slnContent)

let GenerateProjectNode (project : Project) =
    let isTest = project.RelativeProjectFile.toString.Contains(".Test.") || project.RelativeProjectFile.toString.Contains(".Tests.")
    let cat = if isTest then "TestProject"
              else "Project"

    XElement(NsDgml + "Node",
        XAttribute(NsNone + "Id", project.ProjectGuid.toString),
        XAttribute(NsNone + "Label", project.Output.toString),
        XAttribute(NsNone + "Category", cat),
        XAttribute(NsNone + "Fx", project.FxTarget.toString),
        XAttribute(NsNone + "Guid", project.ProjectGuid.toString),
        XAttribute(NsNone + "IsTest", isTest))

let GenerateNode (source : string) (label : string) (category : string) =
    XElement(NsDgml + "Node",
        XAttribute(NsNone + "Id", source),
        XAttribute(NsNone + "Label", label),
        XAttribute(NsNone + "Category", category))

let GenerateLink (source : string) (target : string) (category : string) =
    XElement(NsDgml + "Link",
        XAttribute(NsNone + "Source", source),
        XAttribute(NsNone + "Target", target),
        XAttribute(NsNone + "Category", category))

let GraphNodes (antho : Anthology) (projects : Project set) =
    let allReferencedProjects = projects |> Set.map (fun x -> x.ProjectReferences)
                                         |> Set.unionMany
                                         |> Set.map (fun x -> antho.Projects |> Seq.find (fun y -> y.ProjectGuid = x))
    let importedProjects = Set.difference allReferencedProjects projects
    let allPackageReferences = projects |> Seq.map (fun x -> x.PackageReferences)
                                        |> Seq.concat
    let allAssemblies = projects |> Seq.map (fun x -> x.AssemblyReferences)
                                 |> Seq.concat
    seq {
        yield XElement(NsDgml + "Node",
                XAttribute(NsNone + "Id", "Projects"),
                XAttribute(NsNone + "Label", "Projects"),
                XAttribute(NsNone + "Group", "Expanded"))

        yield XElement(NsDgml + "Node",
                XAttribute(NsNone + "Id", "Packages"),
                XAttribute(NsNone + "Label", "Packages"),
                XAttribute(NsNone + "Group", "Expanded"))

        yield XElement(NsDgml + "Node",
                XAttribute(NsNone + "Id", "Assemblies"),
                XAttribute(NsNone + "Label", "Assemblies"),
                XAttribute(NsNone + "Group", "Expanded"))

        for project in projects do
            yield GenerateProjectNode project

        for project in importedProjects do
            yield GenerateNode (project.ProjectGuid.toString) (project.Output.toString) "ProjectImport"

        for package in allPackageReferences do
            yield GenerateNode (package.toString) (package.toString) "Package"

        for assembly in allAssemblies do
            yield GenerateNode (assembly.toString) (assembly.toString) "Assembly"
    }

let GraphLinks (antho : Anthology) (projects : Project set) =
    let allReferencedProjects = projects |> Set.map (fun x -> x.ProjectReferences)
                                         |> Set.unionMany
                                         |> Set.map (fun x -> antho.Projects |> Seq.find (fun y -> y.ProjectGuid = x))
    let importedProjects = Set.difference allReferencedProjects projects

    seq {
        for project in projects do
            for projectRef in project.ProjectReferences do
                yield GenerateLink (project.ProjectGuid.toString) (projectRef.toString) "ProjectRef"

        for project in projects do
            for package in project.PackageReferences do
                yield GenerateLink (project.ProjectGuid.toString) (package.toString) "PackageRef"

        for project in projects do
            for assembly in project.AssemblyReferences do
                yield GenerateLink (project.ProjectGuid.toString) (assembly.toString) "AssemblyRef"

        for project in projects do
            yield GenerateLink "Projects" (project.ProjectGuid.toString) "Contains"

        for project in importedProjects do
            yield GenerateLink "Projects" (project.ProjectGuid.toString) "Contains"

        for project in projects do
            for package in project.PackageReferences do
                yield GenerateLink "Packages" (package.toString) "Contains"

        for project in projects do
            for assembly in project.AssemblyReferences do
                yield GenerateLink "Assemblies" (assembly.toString) "Contains"

    }

let GraphCategories () =
    let allCategories = [ ("Project", "Green")
                          ("TestProject", "Purple")
                          ("ProjectImport", "Navy")
                          ("Package", "Orange")
                          ("Assembly", "Red")
                          ("ProjectRef", "Green")
                          ("PackageRef", "Orange")
                          ("AssemblyRef", "Red") ]

    let generateCategory (cat) =
        let (key, value) = cat
        XElement(NsDgml + "Category", 
            XAttribute(NsNone + "Id", key), 
            XAttribute(NsNone + "Background", value))

    let res = (allCategories |> Seq.map generateCategory)
    seq {
        yield! (allCategories |> Seq.map generateCategory)
        yield XElement(NsDgml + "Category", 
                XAttribute(NsNone + "Id", "Contains"), 
                XAttribute(NsNone + "Label", "Contains"), 
                XAttribute(NsNone + "IsContainment", "True"),
                XAttribute(NsNone + "CanBeDataDriven", "False"),
                XAttribute(NsNone + "CanLinkedNodesBeDataDriven", "True"),
                XAttribute(NsNone + "IncomingActionLabel", "Contained By"),
                XAttribute(NsNone + "OutgoingActionLabel", "Contains"))
    }

let GraphProperties () =
    let allProperties = [ ("Fx", "Target Framework Version", "System.String")
                          ("Guid", "Project Guid", "System.Guid") 
                          ("IsTest", "Test Project", "System.Boolean")]

    let generateProperty (prop) =
        let (id, label, dataType) = prop
        XElement(NsDgml + "Property", 
            XAttribute(NsNone + "Id", id), 
            XAttribute(NsNone + "Label", label),
            XAttribute(NsNone + "DataType", dataType))

    allProperties |> Seq.map generateProperty

let GraphStyles () =
    XElement(NsDgml + "Styles",
        XElement(NsDgml + "Style",
            XAttribute(NsNone + "TargetType", "Node"), XAttribute(NsNone + "GroupLabel", "Test Project"), XAttribute(NsNone + "ValueLabel", "True"),
            XElement(NsDgml + "Condition", XAttribute(NsNone + "Expression", "IsTest = 'True'")),
            XElement(NsDgml + "Setter", XAttribute(NsNone + "Property", "Icon"), XAttribute(NsNone + "Value", "CodeSchema_Event"))),
        XElement(NsDgml + "Style",
            XAttribute(NsNone + "TargetType", "Node"), XAttribute(NsNone + "GroupLabel", "Test Project"), XAttribute(NsNone + "ValueLabel", "False"),
            XElement(NsDgml + "Condition", XAttribute(NsNone + "Expression", "IsTest = 'False'")),
            XElement(NsDgml + "Setter", XAttribute(NsNone + "Property", "Icon"), XAttribute(NsNone + "Value", "CodeSchema_Method"))))


let GraphContent (antho : Anthology) (viewName : ViewId) =
    let projects = FindViewProjects viewName |> Set
    let xNodes = XElement(NsDgml + "Nodes", GraphNodes antho projects)
    let xLinks = XElement(NsDgml+"Links", GraphLinks antho projects)
    let xCategories = XElement(NsDgml + "Categories", GraphCategories ())
    let xProperties = XElement(NsDgml + "Properties", GraphProperties ())
    let xStyles = GraphStyles ()
    let xGraphDir = XAttribute(NsNone + "GraphDirection", "TopToBottom")
    let xLayout = XAttribute(NsNone + "Layout", "Sugiyama")
    XDocument(
        XElement(NsDgml + "DirectedGraph", 
            xLayout, 
            xGraphDir, 
            xNodes, 
            xLinks, 
            xCategories, 
            xProperties, 
            xStyles))

let Graph (viewName : ViewId) =
    let antho = Configuration.LoadAnthology ()
    let graph = GraphContent antho viewName

    let wsDir = Env.GetFolder Env.Workspace
    let graphFile = wsDir |> GetSubDirectory (AddExt Dgml viewName.toString)
    graph.Save graphFile.FullName

let Create (viewName : ViewId) (filters : string list) =
    if filters.Length = 0 then
        failwith "Expecting at least one filter"

    let vwDir = Env.GetFolder Env.View 
    let vwFile = vwDir |> GetFile (AddExt View viewName.toString)
    File.WriteAllLines (vwFile.FullName, filters)

    Generate viewName


// ---------------------------------------------------------------------------------------

let ExternalBuild (config : string) (viewFile : FileInfo) =
    let wsDir = Env.GetFolder Env.Workspace
    let args = sprintf "/nologo /p:Configuration=%s /v:m %A" config viewFile.Name

    if Env.IsMono () then Exec.Exec "xbuild" args wsDir
    else Exec.Exec "msbuild" args wsDir

let Build (name : ViewId) (config : string) =
    let vwDir = Env.GetFolder Env.View 
    let vwFile = vwDir |> GetFile (AddExt View name.toString)
    if vwFile.Exists |> not then failwithf "Unknown view name %A" name.toString

    let wsDir = Env.GetFolder Env.Workspace
    let viewFile = wsDir |> GetFile (AddExt Solution name.toString)
    let shouldRefresh = viewFile.Exists || vwFile.CreationTime > viewFile.CreationTime
    if shouldRefresh then name |> Generate

    viewFile |> ExternalBuild config
