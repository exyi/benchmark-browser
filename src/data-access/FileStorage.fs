module DataAccess.FileStorage
open System
open Marten
open Giraffe.Tasks

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

let fileStoragePath = "./blobStorage"

let storeFile t tags (stream: IO.Stream) (s: IDocumentSession) = task {
    let id = Guid.NewGuid()
    IO.Directory.CreateDirectory(fileStoragePath) |> ignore
    let extension =
        match t with
        // | StoredFileType.BenchmarkDotNet_Json -> ".bdn.json"
        | StoredFileType.CollectedStacks_Text -> ".stacks"
        | StoredFileType.AnyAttachement ->
            if Array.contains "log" tags then ".log"
            else if Array.contains "html" tags then ".html"
            else if Array.contains "BDN_json" tags then ".bdn.json"
            else if Array.contains "json" tags then ".json"
            else if Array.contains "csv" tags then ".csv"
            else if Array.contains "xml" tags then ".xml"
            else ".attachement"
    let fileName = IO.Path.Combine(fileStoragePath, (string id) + extension) |> IO.Path.GetFullPath
    use file = IO.File.OpenWrite(fileName)
    do! stream.CopyToAsync(file)
    let entity = {
        StoredFileInfo.Id = id
        FilePath = fileName
        ArchiveInfo = FileArchiveInfo.Plain
        Type = t
        DateCreated = DateTime.Now
        Tags = tags
         }
    s.Store entity
    return entity
}
