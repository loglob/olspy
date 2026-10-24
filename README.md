# Olspy
A basic read-only API for Overleaf

## What it can do
- Connect to any Overleaf instance that is accessible through the browser
- Read a project's file structure
- Read the contents of documents
- Request compilations
- Read edit history

## What it can't do
- List a user's projects
- Make edits in a document
- Observe real-time edits to a document

## Installation
The package is available via [nuget](https://www.nuget.org/packages/olspy), so you can add it to your project with
```
dotnet add package olspy
```

## Usage
First, open a project using one of the overloads of `Olspy.Project.Open()`:
```cs
// join via a normal share link
var viaShareLink = await Olspy.Project.Open(new Uri("https://my-overleaf-instance.com/SHARE-URL-HERE"));
// join via project ID and session token
var viaSessionToken = await Olspy.Project.Open(new Uri("https://my-overleaf-instance.com/base-url"), "PROJECT ID HERE", "SESSION TOKEN COOKIE HERE");
// join via project ID and user credentials
var viaCredentials = await Olspy.Project.Open(new Uri("https://my-overleaf-instance.com/base-url"), "PROJECT ID HERE", "YOUR@EMAIL.HERE", "YOUR PASSWORD HERE");
```

### Project Information and File Structure
To get project information such as its name, the file structure and the contents of documents, either open a project session like this:
```cs
await using(var session = await project.Join())
{
	// contains general project info, including its file tree
	var info = await session.GetProjectInfo();
	// info.project holds miscellaneous project information
	var mainFile = info.Project.RootDocID;
	// gets the lines of an editable document
	var lines = await session.GetDocumentByID(mainFile);
}
```
Or use equivalent methods on `project` that open temporary sessions automatically:
```cs
var info = await project.GetInfo();
// info.project holds miscellaneous project information 
var mainFile = info.Project.RootDocID;
// gets the lines of an editable document
var lines = await project.GetDocumentByID(mainFile);
```

### Compiling
To compile a document, use the `Project.Compile()` method like this
```cs
// you can also specify different main files, draft mode, etc.
var compilation = await project.Compile();
```
The return value specifies the created files:
```cs
// First, find the wanted output file
var aux = compilation.OutputFiles.First(f => f.Path.EndsWith(".aux"));
// Then retrieve it via the project
var auxContent = await project.GetOutFile(aux);
// GetOutFile() returns a HttpContent
var auxString = await auxContent.ReadAsStringAsync();
```
To check for success (defined as producing a PDF, even if there may have been non-fatal compile errors) use `IsSuccess()`:
```cs
if(compilation.IsSuccess(out var pdf))
{
	// pdf is the produced PDF file
	var pdfContent = await project.GetOutFile(pdf);

	using(var f = File.Create("compiled.pdf"))
		await pdfContent.CopyToAsync(f);
}
```
To recover compiler error messages, you can use `ReadLogs()`:
```cs
await foreach(var line in project.ReadLogs(compilation))
{
	// output is already filtered to only warnings and errors
	Console.Error.WriteLine(line);
}
```

### Project History
> [!IMPORTANT]
> Reading a Project's history requires read-write permissions! 
> The calls do not modify the project in any way.

To read the edit history, use `GetUpdateHistory()`, which returns updates sorted from newest to oldest:
```cs
foreach(var update in await project.GetUpdateHistory())
{
	// each update has a monotonic version number as well as edit timestamps
	Console.WriteLine($"v{update.FromV} -> v{update.ToV} ({update.Meta.Start} - {update.Meta.End})");
}
```
