using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Olspy.Util;

namespace Olspy;

/// <summary>
///  Contains types for the websocket protocol
/// </summary>
public static class Protocol
{
	/// <summary>
	///  A document, binary file or folder
	/// </summary>
	/// <param name="ID">A unique ID for this file</param>
	/// <param name="Name">Human-Readable name for this file</param>
	public record FileInfo(
		[property: JsonPropertyName("_id")]
		string ID,
		string Name
	);

	/// <summary>
	///  A deleted file
	/// </summary>
	/// <param name="DeletedAt"> UTC timestamp of deletion </param>
	public sealed record DeletedFileInfo(
		string ID,
		string Name,
		DateTime DeletedAt
	) : FileInfo(ID, Name);

	/// <summary>
	///  A binary file
	/// </summary>
	/// <param name="LinkedFileData"> TODO </param>
	/// <param name="Created"> UTC timestamp of creation </param>
	/// <returns></returns>
	public sealed record FileRefInfo(
		string ID,
		string Name,
		// TODO: find this type
		object? LinkedFileData,
		DateTime Created
	) : FileInfo(ID, Name);

	/// <summary>
	///  A folder
	/// </summary>
	/// <param name="Folders"> All sub-folders of this folder </param>
	/// <param name="FileRefs"> Binary files in this folder </param>
	/// <param name="Docs"> Editable files in this folder </param>
	/// <returns></returns>
	public sealed record FolderInfo(
		string ID,
		string Name,
		FolderInfo[] Folders,
		FileRefInfo[] FileRefs,
		FileInfo[] Docs
	) : FileInfo(ID, Name)
	{
		public override string ToString()
			=> $"FolderInfo( ID = {ID}, Name = {Name}, Folders = {Folders.Show()}, FileRefs = {FileRefs.Show()}, Docs = {Docs.Show()} )";

		/// <summary>
		///  Resolves a '/'-separated path.
		/// </summary>
		/// <param name="path"> The path. May be either absolute or relative </param>
		/// <returns> The file,document or folder at that location, or null if no such file exists </returns>
		public FileInfo? Lookup(string path)
		{
			var xs = new ArraySegment<string>(path.Split('/'));

			return Lookup(path[0] == '/' ? xs.Slice(1) : xs);
		}

		public FileInfo? Lookup(ArraySegment<string> path)
		{
			if(path.Count == 0 || (path.Count == 1 && path[0].Length == 0)) // trailing '/'
				return this;

			var p0 = path[0];
			var folder = Folders.FirstOrDefault(f => f.Name == p0);

			if(folder is not null)
				return folder.Lookup(path.Slice(1));
			if(path.Count > 1)
				return null;
			
			return FileRefs.FirstOrDefault(f => f.Name == p0) ?? Docs.FirstOrDefault(f => f.Name == p0);
		}
	
		/// <summary>
		///  Lists all files with their paths relative to this folder, i.e. not including its name
		/// </summary>
		public IEnumerable<(string path, FileInfo file)> List()
			=> Docs
				.Concat(FileRefs)
				.Select(f => (f.Name, f))
				.Concat(Folders.SelectMany(d => d.List()
					.Select(pf => (d.Name + "/" + pf.Item1, pf.Item2))));

		/// <summary>
		///  Lists all files in the folder and its sub-folders, recursively
		/// </summary>
		public IEnumerable<FileInfo> Files()
			=> Docs.Concat(FileRefs).Concat(Folders.SelectMany(d => d.Files()));
	}

	/// <summary>
	///  A user account
	/// </summary>
	/// <param name="Privileges"> The permissions of this user. Observed values: readAndWrite, owner </param>
	/// <param name="SignUpDate"> UTC timestamp of signup </param>
	/// <returns></returns>
	public sealed record UserInfo(
		[property: JsonPropertyName("_id")]
		string ID,
		[property: JsonPropertyName("first_name")]
		string FirstName,
		string Email,
		string Privileges,
		DateTime SignUpDate,
		[property: JsonPropertyName("last_name")]
		string LastName = ""
	);

	/// <summary>
	///  Misc Features of a project.
	/// </summary>
	/// <param name="Collaborators">Observed values: -1</param>
	/// <param name="Versioning"></param>
	/// <param name="Dropbox"></param>
	/// <param name="Github"></param>
	/// <param name="GitBridge"></param>
	/// <param name="CompileTimeout">Maximum number of seconds for a compile</param>
	/// <param name="CompileGroup">Observed values: standard</param>
	/// <param name="Templates"></param>
	/// <param name="References"></param>
	/// <param name="TrackChanges"></param>
	/// <param name="ReferencesSearch"></param>
	/// <param name="Mendeley"></param>
	/// <param name="TrackChangesVisible"></param>
	/// <param name="SymbolPalette"></param>
	public sealed record ProjectFeatures(
		int Collaborators,
		bool Versioning,
		bool Dropbox,
		bool Github,
		bool GitBridge,
		int CompileTimeout,
		string CompileGroup,
		bool Templates,
		bool References,
		bool TrackChanges,
		bool ReferencesSearch,
		bool Mendeley,
		bool TrackChangesVisible,
		bool SymbolPalette
	);

	/// <summary>
	///  Summary of a project returned by a join project message
	/// </summary>
	/// <param name="ID"> The same ID as the corresponding Project object </param>
	/// <param name="Name"> Human-Readable project name </param>
	/// <param name="RootDocID"> The main file to use for compiling by default </param>
	/// <param name="RootFolder"> The folder(s) containing all other files </param>
	/// <param name="PublicAccessLevel"> Observed values: tokenBased </param>
	/// <param name="DropboxEnabled"> TODO </param>
	/// <param name="Compiler"> Which compiler is used. Observed values: pdflatex </param>
	/// <param name="SpellCheckLanguage"> Observed values: en </param>
	/// <param name="DeletedByExternalDataSources"></param>
	/// <param name="DeletedDocs"> Documents that have been deleted in the past </param>
	/// <param name="Members"> Every user with access to this project </param>
	/// <param name="Invites"> TODO </param>
	/// <param name="ImageName"> Which texlive image is used for compiles (?) Doesn't seem to reflect custom images </param>
	/// <param name="Features"> TODO </param>
	/// <param name="TrackChangesState"> TODO </param>
	/// <returns></returns>
	public sealed record ProjectInfo(
		[property: JsonPropertyName("_id")]
		string ID,
		string Name,
		[property: JsonPropertyName("rootDoc_id")]
		string RootDocID,
		FolderInfo[] RootFolder,
		[property: JsonPropertyName("publicAccesLevel")] // sic
		string PublicAccessLevel,
		bool DropboxEnabled,
		string Compiler,
		string Description,
		string SpellCheckLanguage,
		bool DeletedByExternalDataSources,
		DeletedFileInfo[] DeletedDocs,
		UserInfo[] Members,
		JsonObject[] Invites,
		string ImageName,
		UserInfo Owner,
		ProjectFeatures Features,
		bool TrackChangesState
	);

	/// <param name="PublicID"></param>
	/// <param name="Project"></param>
	/// <param name="PermissionsLevel"></param>
	/// <param name="ProtocolVersion"> Observed values: 2 </param>
	public sealed record JoinProjectArgs(
		[property: JsonPropertyName("publicId")]
		string PublicID,
		ProjectInfo Project,
		string PermissionsLevel,
		int ProtocolVersion
	);

	/// <summary>
	///  A file created by a compilation
	/// </summary>
	/// <param name="Path">
	/// 	The filename that was created, relative to the output directory
	/// </param>
	/// <param name="Build"> The build ID that produced this file </param>
	/// <param name="Ranges"> TODO: find this type </param>
	/// <param name="Size"> The size of a PDF in bytes </param>
	/// <param name="CreatedAt"> Timestamp for this compilation, only on PDFs </param>
	public record OutputFile(
		string Path,
		string Build,
		object[]? Ranges = null,
		int? Size = null,
		DateTime? CreatedAt = null
	);

	/// <summary>
	///  Duration of the individual compile steps
	/// </summary>
	/// <param name="Sync"></param>
	/// <param name="Compile"></param>
	/// <param name="Output"></param>
	/// <param name="Total"> The total, end-to-end compile time </param>
	public sealed record Timings(
		int Sync,
		int Compile,
		int Output,
		[property: JsonPropertyName("CompileE2E")]
		int Total
	);

	/// <summary>
	///  Information returned by a compile API call
	/// </summary>
	/// <param name="OutputFiles"></param>
	/// <param name="CompileGroup"> Observed values: standard </param>
	/// <param name="Stats"> TODO: more precise type </param>
	/// <param name="Timings"></param>
	/// <returns></returns>
	public sealed record CompileInfo(
		OutputFile[] OutputFiles,
		string CompileGroup,
		Dictionary<string, int> Stats,
		Timings Timings
	) {
		public OutputFile PDF
			=> OutputFiles.Where(f => f.Path.EndsWith(".pdf")).Single();
	}

	public const byte HEARTBEAT_REC = (byte)'2';
	public const byte HEARTBEAT_SEND = (byte)'2';
	public const byte INIT_REC = (byte)'1';
	public const byte JOIN_PROJECT_REC = (byte)'5';
	public const byte RPC_RESULT_REC = (byte)'6';
	public const byte RPC_SEND = (byte)'5';
	public const byte UPDATE_POS_SEND = (byte)'5';

	public const string RPC_JOIN_DOCUMENT = "joinDoc";
	public const string RPC_LEAVE_DOCUMENT = "leaveDoc";

	public static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};
}