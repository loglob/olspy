using System.Net.WebSockets;
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
	public record DeletedFileInfo(
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
	public record FileRefInfo(
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
	public record FolderInfo(
		string ID,
		string Name,
		FolderInfo[] Folders,
		FileRefInfo[] FileRefs,
		FileInfo[] Docs
	) : FileInfo(ID, Name) {
		public override string ToString()
			=> $"FolderInfo( ID = {ID}, Name = {Name}, Folders = {Folders.Show()}, FileRefs = {FileRefs.Show()}, Docs = {Docs.Show()} )";
	}

	/// <summary>
	///  A user account
	/// </summary>
	/// <param name="Privileges"> The permissions of this user. Possible values: readAndWrite, owner </param>
	/// <param name="SignUpDate"> UTC timestamp of signup </param>
	/// <returns></returns>
	public record UserInfo(
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
	public record ProjectFeatures(
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
	/// <param name="RootDocID"> Folder ID for the root folder </param>
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
	public record ProjectInfo(
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

	/// <summary>
	/// 
	/// </summary>
	/// <param name="PublicID"></param>
	/// <param name="Project"></param>
	/// <param name="PermissionsLevel"></param>
	/// <param name="ProtocolVersion"> Observed values: 2 </param>
	/// <returns></returns>
	public record JoinProjectArgs(
		[property: JsonPropertyName("publicId")]
		string PublicID,
		ProjectInfo Project,
		string PermissionsLevel,
		int ProtocolVersion
	);

	public const byte INIT_REC = (byte)'1';
	public const byte HEARTBEAT_SEND = (byte)'2';
	public const byte HEARTBEAT_REC = (byte)'2';
	public const byte JOIN_PROJECT_REC = (byte)'5';

	public static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};
}