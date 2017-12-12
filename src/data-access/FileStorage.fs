module DataAccess.FileStorage
open System
open Marten
open Giraffe.Tasks
open System.Threading.Tasks
open Giraffe.Tasks
open Marten
open System.Xml.Linq
open System.Diagnostics
open System.Collections.Concurrent
open System.Runtime.InteropServices.ComTypes
open Giraffe.HttpHandlers
open Fake.Tools.Git.Rebase

[<RequireQualifiedAccessAttribute>]
/// Represents special type of stored file
type StoredFileType =
    /// Json file from BenchmarkDotNet, that may be processed into test results
    // | BenchmarkDotNet_Json
    /// Collected stacks of a test, may be merged with other simmilar ones to reduce space
    | CollectedStacks_Text
    // | CollectedStacks_Bin
    /// Anything, will not touch (maybe lossless compress or so)
    | AnyAttachement

[<RequireQualifiedAccessAttribute>]
type FileArchiveInfo =
    | Plain
    | GZipped

[<CLIMutableAttribute>]
type StoredFileInfo = {
    Id: Guid
    /// Path to file on disk
    FilePath: string
    ArchiveInfo: FileArchiveInfo
    DateCreated: DateTime
    Type: StoredFileType
    /// Any classification, may help deduplication or file classification
    Tags: string []
}

let archiveTypes =
    Map.ofList [
        "zip", "application/zip"
        "flame", "image/svg+xml"
    ]

let parseStacks (file: IO.Stream) =
    use reader = new IO.StreamReader(file)
    Seq.unfold (fun (reader: IO.StreamReader) ->
        let line = reader.ReadLine()
        if isNull line then
            None
        else
            let lastSpace = line.LastIndexOf ' '
            let name = line.Remove lastSpace
            let count = line.Substring lastSpace |> Int32.Parse
            Some ((name, count), reader)
    ) reader |> Seq.toArray

let mergeStacks (stacks : (string * int)[] seq) : seq<string * int> =
    let mutable totalTotal = 0.0;
    let counts = stacks |> Seq.map (fun m ->
        let total = Array.sumBy snd m |> float
        totalTotal <- totalTotal + total
        m |> Seq.map (fun (x, c) -> x, (float c / total)))
    counts |> Seq.concat |> Seq.groupBy (fst) |> Seq.map (fun (stack, cnt) -> stack, (Seq.sumBy snd (cnt) * totalTotal |> int))


let digInMethods target stacks =
    stacks |> Seq.choose (fun (stack: string, count) ->
        let frames = stack.Split(';')
        frames |> Array.tryFindIndex (fun f -> Seq.exists f.Contains target)
            |> Option.map (fun firstFrame ->
                (String.Join(";", frames |> Seq.skip firstFrame), count))
    )

let getFlameGraph (args: Map<string, string[]>) (stacks: (string * int) seq) outStream = task {

    let stacks =
        match args.TryFind "dig" with
        | Some target -> digInMethods target stacks
        | None -> stacks

    let title = args.TryFind "title" |> Option.bind (Seq.tryHead) |> Option.defaultValue "Flame graph"
    let colors = args.TryFind "colors" |> Option.bind (Seq.tryHead) |> Option.defaultValue "hot"
    let width = args.TryFind "width" |> Option.bind (Seq.tryHead) |> Option.defaultValue "1200"

    let startInfo = System.Diagnostics.ProcessStartInfo("../../FlameGraph/flamegraph.pl", sprintf "--hash --title \"%s\" --colors \"%s\" --width \"%s\"" title colors width)
    startInfo.RedirectStandardInput <- true
    startInfo.RedirectStandardOutput <- true
    let proc = Diagnostics.Process.Start startInfo
    for (s, count) in stacks do
        proc.StandardInput.WriteLine(sprintf "%s %d" s count)
    proc.StandardInput.Close()
    do! proc.StandardOutput.BaseStream.CopyToAsync outStream
    proc.WaitForExit(20*1000) |> ignore
}

let archivers : Map<string, (Map<string, string[]> -> ((unit -> Task<IO.Stream>) * string) seq -> IO.Stream -> Task<unit>)> =
    Map.ofList [
        "zip", (fun _args files output -> task {
            use zip = new IO.Compression.ZipArchive(output, IO.Compression.ZipArchiveMode.Create, true)
            for file, name in files do
                use! fileStream = file()
                let entry = zip.CreateEntry(name, IO.Compression.CompressionLevel.Optimal)
                use entryStream = entry.Open()
                do! fileStream.CopyToAsync(entryStream)
            return ()
        })
        "flame", (fun args files output -> task {
            let stacks = ResizeArray()
            for file, _name in files do
                use! fileStream = file()
                stacks.Add( parseStacks fileStream)
            let allStacks = mergeStacks stacks
            do! getFlameGraph args allStacks output
        })
    ]

let fileStoragePath = "./blobStorage"

let storeFile fileId t tags (stream: IO.Stream) (s: IDocumentSession) = task {
    IO.Directory.CreateDirectory(fileStoragePath) |> ignore
    let extension =
        match t with
        // | StoredFileType.BenchmarkDotNet_Json -> ".bdn.json"
        | StoredFileType.CollectedStacks_Text -> ".stacks"
        | StoredFileType.AnyAttachement ->
            if Array.contains "log" tags then ".log"
            else if Array.contains "html" tags then ".html"
            else if Array.contains "BdnReport" tags && Array.contains "json" tags then ".bdn.json"
            else if Array.contains "json" tags then ".json"
            else if Array.contains "csv" tags then ".csv"
            else if Array.contains "xml" tags then ".xml"
            else if Array.contains "markdown" tags then ".md"
            else ".attachement"
    let fileName = IO.Path.Combine(fileStoragePath, (string fileId) + extension) |> IO.Path.GetFullPath
    use file = new IO.Compression.GZipStream(IO.File.OpenWrite(fileName), IO.Compression.CompressionLevel.Optimal)
    do! stream.CopyToAsync(file)
    let entity = {
        StoredFileInfo.Id = fileId
        FilePath = fileName
        ArchiveInfo = FileArchiveInfo.GZipped
        Type = t
        DateCreated = DateTime.Now
        Tags = tags
         }
    s.Insert entity
    return entity
}

let openFileStream (fid:Guid) (s: IDocumentSession) = task {
    let! entity = s.LoadAsync<StoredFileInfo> fid
    let file = IO.File.OpenRead (entity.FilePath) :> IO.Stream
    return match entity.ArchiveInfo with
           | FileArchiveInfo.Plain -> file
           | FileArchiveInfo.GZipped -> new IO.Compression.GZipStream(file, IO.Compression.CompressionMode.Decompress) :> IO.Stream
}

let dumpFiles archiver parameters (files: (string * Guid[]) seq) (outStream: IO.Stream) dbSession =
    let archiver = Map.find archiver archivers
    archiver parameters (files |> Seq.collect (fun (name, files) -> seq {
        for f in files do
            let name =
                if files.Length = 1 then name
                else name + (string f)
            yield (fun () -> openFileStream f dbSession), name
    })) outStream