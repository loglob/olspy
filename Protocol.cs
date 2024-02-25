using System.Diagnostics.CodeAnalysis;
using System.Text;
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
	/// <param name="LinkedFileData"> If null, this is a local binary file.
	///		Otherwise, points to a file from another project </param>
	/// <param name="Created"> UTC timestamp of creation </param>
	/// <returns></returns>
	public sealed record FileRefInfo(
		string ID,
		string Name,
		LinkedFile? LinkedFileData,
		DateTime Created
	) : FileInfo(ID, Name);

	[JsonPolymorphic(TypeDiscriminatorPropertyName = "provider")]
	[JsonDerivedType(typeof(LinkedProjectFile), "project_file")]
	[JsonDerivedType(typeof(LinkedOutputFile), "project_output_file")]
	public abstract record LinkedFile(
		[property: JsonPropertyName("source_project_id")] string SourceProjectID
	);

	public record LinkedProjectFile(
		string SourceProjectID,
		[property: JsonPropertyName("source_entity_path")] string SourceEntityPath
	) : LinkedFile(SourceProjectID);

	public record LinkedOutputFile(
		string SourceProjectID,
		[property: JsonPropertyName("source_output_file_path")] string SourceOutputFilePath,
		[property: JsonPropertyName("build_id")] string BUildID
	) : LinkedFile(SourceProjectID);

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
					.Select(pf => (d.Name + "/" + pf.path, pf.file))));

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
		bool DeletedByExternalDataSource,
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
#if DEBUG
	// redundant url and type members are skipped
	[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
#endif
	// TODO: convert to polymorphic once .NET 9 comes out (ref: https://github.com/dotnet/runtime/issues/72604)
	public record OutputFile(
		string Path,
		string Build,
		object[]? Ranges = null,
		int? Size = null,
		DateTime? CreatedAt = null
	);

	/// <param name="LatexmkErrors"> Identical to `LatexRunsWithErrors0` (?) </param>
	/// <param name="LatexRuns"> Always 0 (?) </param>
	/// <param name="LatexRunsWithErrors"> Always 0 (?) See `LatexRunsWithErrors0` instead. </param>
	/// <param name="LatexRuns0"> Always 1 (?) </param>
	/// <param name="LatexRunsWithErrors0"> 1 if a successful compilation encountered any recoverable errors, 0 otherwise. </param>
	/// <param name="PdfSize"> Output PDF file size in bytes, if there is one. Identical to `OutputFile.Size`. </param>
	public sealed record CompileStats(
		[property: JsonPropertyName("latexmk-errors")]
		int LatexmkErrors,
		[property: JsonPropertyName("latex-runs")]
		int LatexRuns,
		[property: JsonPropertyName("latex-runs-with-errors")]
		int LatexRunsWithErrors,
		[property: JsonPropertyName("latex-runs-0")]
		int LatexRuns0,
		[property: JsonPropertyName("latex-runs-with-errors-0")]
		int LatexRunsWithErrors0,
		[property: JsonPropertyName("pdf-size")]
		int? PdfSize
	);

	/// <summary>
	///  Duration of the individual compile steps
	/// </summary>
	/// <param name="Sync"></param>
	/// <param name="Compile"></param>
	/// <param name="Output"></param>
	/// <param name="Total"> The total, end-to-end compile time </param>
	public sealed record CompileTimings(
		int Sync,
		int Compile,
		int Output,
		[property: JsonPropertyName("compileE2E")]
		int Total
	);

	[JsonConverter(typeof(EnumKebabCaseConverter<CompileStatus>))]
	public enum CompileStatus
	{ 
		/// <summary>
		///  A PDF was produced, but there may have been non-fatal errors
		/// </summary>
		Success,
		/// <summary>
		///  No PDF was produced due to a fatal error in the latex source code
		/// </summary>
		Failure,
		/// <summary>
		///  Either another compilation is already running or one finished very recently
		/// </summary>
		TooRecentlyCompiled,
		/// <summary>
		///  Auto-compile is enabled and a rate limit was hit
		/// </summary>
		AutocompileBackoff,
		/// <summary>
		///  Corresponds to internal HTTP code 409.
		///  Probably transient.
		/// </summary>
		Conflict,
		/// <summary>
		///  Internal compile server is temporarily unavailable.
		///  Corresponds to internal HTTP code 503.
		///  Also returned when the server is shutting down.
		/// </summary>
		Unavailable,
		/// <summary>
		///  Could not run compile because the latex configuration was invalid
		/// </summary>
		ValidationProblems,
		/// <summary>
		///  Corresponds to internal HTTP code 423
		/// </summary>
		CompileInProgress,
		/// <summary>
		///  Internal compile API hit some size limit
		/// </summary>
		ProjectTooLarge,
		/// <summary>
		///  Equivalent (?) to Failure if stoppedOnFirstError is active
		/// </summary>
		StoppedOnFirstError,
		/// <summary>
		///  Unknown internal error
		/// </summary>
		Error,
		Timedout,
		Terminated,
		/// <summary>
		///  Files weren't synchronized properly.
		///  Transient.
		/// </summary>
		Retry,
		/// <summary>
		///  A compile run failed on validation.
		/// </summary>
		ValidationFail,
		/// <summary>
		///  A validation-only (no compile) run was successful
		/// </summary>
		ValidationPass
	}

	/// <summary>
	///  Information returned by a compile API call
	/// </summary>
	/// <param name="Status">
	/// 	Whether compile was successful or failed due to some error.
	/// 	Note that Success is also returned if there were recoverable compilation errors. 
	/// </param>
	/// <param name="OutputFiles">
	/// 	Every file produced by the compilation.
	/// 	Note that a PDF may be produced even if there were compile errors.
	/// </param>
	/// <param name="OutputFiles"></param>
	/// <param name="CompileGroup"> Observed values: standard </param>
	/// <param name="Stats"> Information on compile errors </param>
	/// <param name="Timings"></param>
	/// <returns></returns>
	public sealed record CompileInfo(
		CompileStatus Status,
		OutputFile[] OutputFiles,
		string CompileGroup,
		CompileStats Stats,
		CompileTimings Timings
	) {
		/// <summary>
		///  Determines if a success status was indicated and a single output PDF file was produced
		/// </summary>
		public bool IsSuccess([MaybeNullWhen(false)] out OutputFile pdf)
		{
			if(Status != CompileStatus.Success)
			{
				pdf = null;
				return false;
			}
			
			return OutputFiles.Where(f => f.Path.EndsWith(".pdf")).IsSingle(out pdf);
		}
	}

	public enum OpCode
	{
		DISCONNECT = 0,
		CONNECT = 1,
		HEARTBEAT = 2,
		MESSAGE = 3,
		JSON = 4,
		EVENT = 5,
		ACK = 6,
		ERROR = 7,
		NOOP = 8
	}

	
	/// <summary>
	///  A packet sent over the socket.io interface
	/// </summary>
	/// <param name="OpCode"> The packet type </param>
	/// <param name="ID"> A sequence number, if present. For ACK packets it is the sequence number they acknowledge. </param>
	/// <param name="AckWithData"> Whether this packet should be acknowledged with extra data (?) </param>
	/// <param name="Endpoint"></param>
	/// <param name="Payload"> The actual byte payload </param>
	public sealed record Packet(
		OpCode OpCode,
		uint? ID,
		bool AckWithData,
		ArraySegment<byte> Endpoint,
		ArraySegment<byte> Payload
	) {
		// Overleaf's socket.io version (0.9 maybe?) is so outdated I couldn't find any implementations or even documentation on it.
		// the packet structure is inferred from here:
		// https://github.com/overleaf/socket.io/blob/ddbee7b2d0427d4e4954cf9761abc8053c290292/lib/parser.js
		
		private static uint parseDecimal(ArraySegment<byte> bytes, out int len)
		{
			uint x = 0;

			for (len = 0; len < bytes.Count && bytes[len] >= (byte)'0' && bytes[len] <= (byte)'9'; ++len)
				x = x * 10 + bytes[len] - '0';
			
			return x;
		}

		public static Packet Parse(ArraySegment<byte> data)
		{
			if(data.Count == 0)
				throw new FormatException("Empty packet data");
			if(data[0] < '0' || data[0] > '8' || data[1] != ':')
				throw new FormatException("Packet must start with single-byte opcode");
			
			var op = (OpCode)(data[0] - '0');
			int off = 2;

			uint id = parseDecimal(data.Slice(off), out int idLen);
			off += idLen;

			bool awd = off < data.Count && data[off] == '+';

			if(awd)
				++off;

			if(off >= data.Count || data[off++] != ':')
				throw new FormatException("Expected separator after packet id field");
			
			var ep = data.SliceWhile(off, c => c != ':');
			off += ep.Count;

			if(off < data.Count && data[off] == ':')
				++off;

			if(op == OpCode.ACK)
			{
				// the ID is in a different place for ACKs
				if(idLen > 0 || awd)
					throw new FormatException("ACK packets may not have message IDs");

				id = parseDecimal(data.Slice(off), out idLen);
				off += idLen;

				if(idLen == 0)
					throw new FormatException("ACK packets must have a message ID");
				
				awd = off < data.Count && data[off] == '+';

				if(awd)
					++off;
			}

			return new Packet(op, idLen > 0 ? id : null, awd, ep, data.Slice(off));
		}
	
		public bool ShouldAcknowledge
			=> ID.GetValueOrDefault(0) > 0;

		public string StringPayload
			=> Encoding.UTF8.GetString(Payload);

		public JsonNode? JsonPayload
			=> JsonNode.Parse(Payload);

		/// <summary>
		///  The payload of EVENT packets
		/// </summary>
		public (string name, JsonArray args) EventPayload
		{
			get
			{
				var obj = (JsonPayload ?? throw new FormatException("Malformed EVENT packet with null payload")).AsObject();

				if(obj["name"] is not JsonValue nameV || ((string?)nameV) is not string name)
					throw new FormatException("Malformed EVENT packet. Expected 'name' field");
				
				var argsF = obj["args"];

				if(obj.Count > (argsF is null ? 1 : 2))
					throw new FormatException("Malformed EVENT packet. Too many fields.");

				if(argsF is null)
					return (name, []);
				else if (argsF is not JsonArray args)
					throw new FormatException("Malformed EVENT packet. Expected the args field to be an array.");
				else
					return (name, args);
			}
		}

		/// <summary>
		///  The payload of ERROR packets
		/// </summary>
		public (string reason, string advice) ErrorPayload
		{
			get
			{
				var l = Payload.SliceWhile(0, c => c == '+');

				return (Encoding.UTF8.GetString(l), Encoding.UTF8.GetString(Payload.Slice(1 + l.Count)));
			}
		}
	}

	/// <summary>
	///  The user info given by meta entries.
	///  Provides a slightly different format than that given in ProjectInfo
	/// </summary>
	/// <param name="FirstName"></param>
	/// <param name="LastName"></param>
	/// <param name="Email"></param>
	/// <param name="ID"></param>
	/// <returns></returns>
	public sealed record UpdateMetaUser(
		[property: JsonPropertyName("first_name")]
		string FirstName,
		[property: JsonPropertyName("last_name")]
		string LastName,
		string Email,
		string ID
	);

	/// <param name="Kind"> Observed values: "history-migration" </param>
	public sealed record UpdateMetaOrigin(
		string Kind
	);

	/// <param name="Users"></param>
	/// <param name="Start"></param>
	/// <param name="End"></param>
	/// <param name="Origin"></param>
	public sealed record UpdateMeta(
		object[] Users,
		[property: JsonConverter(typeof(TimeStampConverter))]
		[property: JsonPropertyName("start_ts")]
		DateTime Start,
		[property: JsonConverter(typeof(TimeStampConverter))]
		[property: JsonPropertyName("end_ts")]
		DateTime End,
		UpdateMetaOrigin? Origin
	);

	public sealed record Rename(
		[property: JsonPropertyName("pathname")]
		string OldPath,
		[property: JsonPropertyName("newPathname")]
		string NewPath
	);

	public sealed record WrappedPath(
		[property: JsonPropertyName("pathname")]
		string Path
	);

	/// <summary>
	///  A project-level operation. Exactly one of the nullable fields is present.
	/// </summary>
	/// <param name="Add"> The path of an created file </param>
	/// <param name="Remove"> The path of a deleted file </param>
	/// <param name="Rename"> Information on a moved file </param>
	public sealed record UpdateProjectOp(
		uint atV,
		WrappedPath? Add = null,
		WrappedPath? Remove = null,
		Rename? Rename = null
	);

	/// <summary>
	///  A project-level operation.
	///  One of three cases Add, Remove or Rename. 
	/// </summary>
	/// <param name="atV"> The exact version number of this operation </param>
	[JsonConverter(typeof(ProjectOpConverter))]
	public abstract record BaseProjectOp(
		uint AtV,
		string Path
	) {
		public abstract T Distinguish<T>(Func<AddProjectOp, T> f, Func<RemoveProjectOp, T> g, Func<RenameProjectOp, T> h);
	}

	/// <summary>
	///  A file was created
	/// </summary>
	/// <param name="Path"> The newly created file </param>
	public sealed record AddProjectOp(
		uint AtV,
		string Path
	) : BaseProjectOp(AtV, Path)
	{
		public override T Distinguish<T>(Func<AddProjectOp, T> f, Func<RemoveProjectOp, T> _g, Func<RenameProjectOp, T> _h)
			=> f(this);
	}

	/// <summary>
	///  A file was deleted
	/// </summary>
	/// <param name="Path"> The removed file </param>
	public sealed record RemoveProjectOp(
		uint AtV,
		string Path
	) : BaseProjectOp(AtV, Path)
	{
		public override T Distinguish<T>(Func<AddProjectOp, T> _f, Func<RemoveProjectOp, T> g, Func<RenameProjectOp, T> _h)
			=> g(this);
	}

	/// <summary>
	///  A file was moved
	/// </summary>
	/// <param name="Path"> The old path of the file </param>
	/// <param name="NewPath"> The new path of the file </param>
	public sealed record RenameProjectOp(
		uint AtV,
		string Path,
		string NewPath
	) : BaseProjectOp(AtV, Path)
	{
		public override T Distinguish<T>(Func<AddProjectOp, T> _f, Func<RemoveProjectOp, T> _g, Func<RenameProjectOp, T> h)
			=> h(this);
	}

	/// <param name="ID"> A unique ID for this label </param>
	/// <param name="Comment"> The name given by the user </param>
	/// <param name="Version"> The exact version number of this label </param>
	/// <param name="UserID"> The UUID of the user that created this label </param>
	/// <param name="CreatedAt"> Creation timestamp in UTC </param>
	/// <returns></returns>
	public sealed record UpdateLabel(
		string ID,
		string Comment,
		uint Version,
		[property: JsonPropertyName("user_id")]
		string UserID,
		[property: JsonPropertyName("created_at")]
		DateTime CreatedAt
	);

	/// <summary>
	///  An update that aggregates multiple atomic changes
	/// </summary>
	/// <param name="FromV"> The version number before applying this update </param>
	/// <param name="ToV"> The version number after applying this update </param>
	/// <param name="Meta"> meta information (users and timestamp) on the update </param>
	/// <param name="Labels">labels applied to versions of this update </param>
	/// <param name="PathNames">
	/// 	The files that were edited in this update.
	/// 	Note that this covers only files edited via editor, not created deleted or moved files.
	/// </param>
	/// <param name="ProjectOps"> Project-level (i.e. create/delete/rename) operations </param>
	public sealed record Update(
		uint FromV,
		uint ToV,
		UpdateMeta Meta,
		UpdateLabel[] Labels,
		[property: JsonPropertyName("pathnames")]
		string[] PathNames,
		[property: JsonPropertyName("project_ops")]
		BaseProjectOp[] ProjectOps
	);

	/// <summary>
	///  The wrapped Update array that Overleaf returns
	/// </summary>
	public sealed record WrappedUpdates(
		Update[] Updates
	);

	/// <summary>
	///  Undoes the encoding overleaf applies to document contents with
	///  `unescape(encodeUriComponent(x))`
	/// </summary>
	public static string UnMangle(string mangled)
	{
		var escaped = new StringBuilder();

		// replicate javascript's escape()
		foreach (var c in mangled)
		{
			switch (c)
			{
				case char when char.IsAsciiLetterOrDigit(c):
				case '@':
				case '*':
				case '_':
				case '+':
				case '-':
				case '.':
				case '/':
					escaped.Append(c);
				break;

				default:
				{
					escaped.Append('%');

					if(c < 256)
						escaped.Append(((int)c).ToString("X2"));
					else
					{
						escaped.Append('u');
						escaped.Append(((int)c).ToString("X4"));
					}
				}
				break;
			}
		}

		return Uri.UnescapeDataString(escaped.ToString());
	}

	public const string RPC_JOIN_DOCUMENT = "joinDoc";
	public const string RPC_LEAVE_DOCUMENT = "leaveDoc";
	/// <summary>
	///  Name of an EVENT packet received when the client initiates the socket
	/// </summary>
	public const string RPC_JOIN_PROJECT = "joinProjectResponse";

	public static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	#if DEBUG
		,UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
	#endif
	};
}