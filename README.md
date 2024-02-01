# Olspy
A basic interface for the Overleaf frontend

## What it can do
- Connect to any Overleaf instance that is accessible through the browser
- Read a project's file structure
- Read the contents of documents
- Request compilations

## What is can't do
- List a user's projects
- Make edits in a document
- Observe real-time edits to a document

## Usage
First, open a project using one of the overloads of `Olspy.Project.Open()`:
```cs
// join via a normal share link
var project = await Olspy.Project.Open(new Uri("https://my-overleaf-instance.com/SHARE-URL-HERE>"));
// join via project ID and session token
var project = await Olspy.Project.Open(new Uri("https://my-overleaf-instance.com/base-url"), "PROJECT ID HERE", "SESSION TOKEN COOKIE HERE");
// join via project ID and user credentials
var project = await Olspy.Project.Open(new Uri("https://my-overleaf-instance.com/base-url"), "PROJECT ID HERE", "YOUR@EMAIL.HERE", "YOUR PASSWORD HERE");
```
To get project data, either open a project session like this:
```cs
async using(var session = await project.Join())
{
	// contains general project info, including its file tree
	var info = await session.GetProjectInfo();
	var mainFile = info.project.RootDocID;
	// gets the lines of an editable document
	var lines = await session.GetDocumentByID(mainFile);
}
```
Or use equivalent methods on `project` that open temporary sessions automatically:
```cs
var info = await project.GetProjectInfo();
var mainFile = info.project.RootDocID;
// gets the lines of an editable document
var lines = await project.GetDocumentByID(mainFile);
```
