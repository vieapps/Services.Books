#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Books
{
	public static class Utility
	{
		public static Components.Caching.Cache Cache { get; internal set; }

		public static string FilesURI { get; internal set; }

		public static string FilesPath { get; internal set; }

		public static string FolderOfDataFiles => $"{Utility.FilesPath}files";

		public static string FolderOfStatisticFiles => $"{Utility.FilesPath}statistics";

		public static string FolderOfContributedFiles => $"{Utility.FilesPath}contributions";

		public static string FolderOfTempFiles => $"{Utility.FilesPath}temp";

		public static string FolderOfTrashFiles => $"{Utility.FilesPath}trash";

		public static string FolderOfIntroductionsFiles => $"{Utility.FilesPath}introductions";

		#region Avalable characters
		public static List<string> Chars { get; internal set; }

		public static string GetFirstChar(this string @string, bool userLower = true)
		{
			var result = UtilityService.GetNormalizedFilename(@string).ConvertUnicodeToANSI().Trim();
			if (string.IsNullOrWhiteSpace(result))
				return "0";

			new[] { "-", ".", "'", "+", "&", "“", "”" }.ForEach(special =>
			{
				while (result.StartsWith(special))
					result = result.Right(result.Length - 1).Trim();
				while (result.EndsWith(special))
					result = result.Left(result.Length - 1).Trim();
			});
			if (string.IsNullOrWhiteSpace(result))
				return "0";

			var index = 0;
			var isCorrect = false;
			while (!isCorrect && index < result.Length)
			{
				var @char = result.ToUpper()[index];
				isCorrect = (@char >= '0' && @char <= '9') || (@char >= 'A' && @char <= 'Z');
				if (!isCorrect)
					index++;
			}

			var firstChar = index < result.Length
				? result[index]
				: '0';

			return (firstChar >= '0' && firstChar <= '9')
				? "0"
				: userLower
					? firstChar.ToString().ToLower()
					: firstChar.ToString().ToUpper();
		}
		#endregion

		#region Working with folders & files
		public static string GetFolderPathOfBook(this string name)
			=> Path.Combine(Utility.FolderOfDataFiles, name.GetFirstChar().ToLower());

		public static string GetFolderPath(this Book book)
			=> Utility.GetFolderPathOfBook(book.Name);

		public static string GetFilePathOfBook(this string name)
			=> Path.Combine(Utility.GetFolderPathOfBook(name), UtilityService.GetNormalizedFilename(name));

		public static string GetFilePathOfBook(this string title, string author)
			=> Utility.GetFilePathOfBook(title.Trim() + (string.IsNullOrWhiteSpace(author) ? "" : " - " + author.GetAuthor()));

		public static string GetFilePath(this Book book)
			=> Utility.GetFilePathOfBook(book.Name);

		public static string GetMediaFilePathOfBook(this string uri, string name, string identifier)
			=> uri.Replace(Definitions.MediaURI, Path.Combine(Utility.GetFolderPathOfBook(name), Definitions.MediaDirectory, identifier + "-"));

		public static string GetFileSize(this string filePath)
			=> UtilityService.GetFileSize(filePath) ?? "generating...";
		#endregion

		#region Working with related data of JSON
		public static string GetBookAttribute(string filePath, string attribute)
		{
			if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(attribute))
				return "";

			var json = UtilityService.ReadTextFile(filePath, 20).Aggregate((i, j) => i + "\n" + j).ToString();
			var indicator = "\"" + attribute + "\":";
			var start = json.PositionOf(indicator);
			start = start < 0
				? -1
				: json.PositionOf("\"", start + indicator.Length);
			var end = start < 0
				? -1
				: json.PositionOf("\"", start + 1);

			return start > -1 && end > 0
				? json.Substring(start + 1, end - start - 1).Trim()
				: null;
		}

		public static Book GetBook(this Book book)
		{
			var key = book.GetCacheKey() + ":json";
			var full = Utility.Cache.Get<Book>(key);
			if (full == null)
			{
				full = book.Clone();
				if (File.Exists(book.GetFilePath() + ".json"))
					full.Copy(JObject.Parse(UtilityService.ReadTextFile(book.GetFilePath() + ".json")));
				Utility.Cache.SetAsFragments(key, full);
			}
			return full;
		}

		public static async Task<Book> GetBookAsync(this Book book, CancellationToken cancellationToken = default(CancellationToken))
		{
			var key = book.GetCacheKey() + ":json";
			var full = await Utility.Cache.GetAsync<Book>(key, cancellationToken).ConfigureAwait(false);
			if (full == null)
			{
				full = book.Clone();
				if (File.Exists(book.GetFilePath() + ".json"))
					full.Copy(JObject.Parse(await UtilityService.ReadTextFileAsync(book.GetFilePath() + ".json", null, cancellationToken).ConfigureAwait(false)));
				await Utility.Cache.SetAsFragmentsAsync(key, full, 0, cancellationToken).ConfigureAwait(false);
			}
			return full;
		}

		internal static void Copy(this Book book, JObject json)
		{
			book.PermanentID = (json["PermanentID"] as JValue).Value as string;
			book.SourceUrl = json["SourceUrl"] != null
				? json.Get<string>("SourceUrl")
				: json["SourceUri"] != null
					? json.Get<string>("SourceUri")
					: "";

			book.TOCs = new List<string>();
			var array = json["TOCs"] as JArray;
			foreach (JValue item in array)
				book.TOCs.Add(item.Value as string);

			book.Chapters = new List<string>();
			array = json["Chapters"] as JArray;
			foreach (JValue item in array)
				book.Chapters.Add(item.Value as string);
		}
		#endregion

		#region Working with URIs
		public static string GetMediaFileUri(this Book book) => Utility.FilesURI + "/books/" + Definitions.MediaDirectory + "/";

		public static string GetCoverImageUri(this Book book)
			=> string.IsNullOrWhiteSpace(book.Cover)
				? book.GetMediaFileUri() + "no-media-file".Url64Encode() + "/no/cover/image.png"
				: book.Cover.Replace(Definitions.MediaURI, book.GetMediaFileUri() + book.Title.Url64Encode() + "/" + book.GetPermanentID() + "/");

		public static string NormalizeMediaFileUris(this Book book, string content)
			=> string.IsNullOrWhiteSpace(content)
				? content
				: content.Replace(Definitions.MediaURI, book.GetMediaFileUri() + book.Title.Url64Encode() + "/" + book.GetPermanentID() + "/");

		public static string NormalizeMediaFilePaths(this string content, Book book)
			=> string.IsNullOrWhiteSpace(content)
				? content
				: content.Replace(Definitions.MediaURI, Path.Combine(book.GetFolderPath(), Definitions.MediaDirectory, book.GetPermanentID() + "-"));

		public static string GetDownloadUri(this Book book)
			=> Utility.FilesURI + "/books/download/" + book.Name.Url64Encode() + "/" + book.ID.Url64Encode() + "/" + book.Title.GetANSIUri();
		#endregion

		#region Statistics
		static Statistics _Categories = null;

		internal static Statistics Categories
		{
			get
			{
				if (Utility._Categories == null)
				{
					Utility._Categories = new Statistics();
					Utility._Categories.Load(Utility.FolderOfStatisticFiles, "categories.json");
				}
				return Utility._Categories;
			}
		}

		static Statistics _Authors = null;

		internal static Statistics Authors
		{
			get
			{
				if (Utility._Authors == null)
				{
					Utility._Authors = new Statistics();
					Utility._Authors.Load(Utility.FolderOfStatisticFiles, "authors-{0}.json", true);
				}
				return Utility._Authors;
			}
		}

		static Statistics _Status = null;

		internal static Statistics Status
		{
			get
			{
				if (Utility._Status == null)
				{
					Utility._Status = new Statistics();
					Utility._Status.Load(Utility.FolderOfStatisticFiles, "status.json");
					if (Utility._Status.Count < 1)
						(new List<string>() { "Books", "Authors" }).ForEach(name =>
						{
							Utility._Status.Add(new StatisticInfo()
							{
								Name = name,
								Counters = 0
							});
						});
					Utility._Status.Save(Utility.FolderOfStatisticFiles, "status.json");
				}
				return Utility._Status;
			}
		}

		public static string GetAuthorName(this string author)
		{
			var start = author.IndexOf(",");
			if (start > 0)
				return author.Substring(0, start).GetAuthorName();

			var name = author.GetNormalized();
			new List<string>() { "(", "[", "{", "<" }.ForEach(indicator =>
			{
				start = name.IndexOf(indicator);
				while (start > -1)
				{
					name = name.Remove(start).Trim();
					start = name.IndexOf(indicator);
				}
			});

			new List<string>() { ".", " ", "-" }.ForEach(indicator =>
			{
				start = name.IndexOf(indicator);
				while (start > -1)
				{
					name = name.Remove(0, start + indicator.Length).Trim();
					start = name.IndexOf(indicator);
				}
			});

			return name;
		}

		internal static List<string> GetAuthorNames(this string author)
		{
			var authors = new List<string>();

			var theAuthors = author.GetNormalized();
			new List<string>() { "&", " và ", " - ", "/" }.ForEach(indicator =>
			{
				var start = theAuthors.PositionOf(indicator);
				while (start > -1)
				{
					authors.Add(theAuthors.Substring(0, start).GetNormalized());
					theAuthors = theAuthors.Remove(0, start + indicator.Length).Trim();
					start = theAuthors.PositionOf(indicator);
				}
			});

			if (!string.IsNullOrWhiteSpace(theAuthors))
				authors.Add(theAuthors.GetNormalized());

			return authors;
		}
		#endregion

		public static string GetPermanentID(this Book book)
			=> !string.IsNullOrWhiteSpace(book.PermanentID) && book.PermanentID.IsValidUUID()
				? book.PermanentID
				: Utility.GetBookAttribute(book.GetFilePath() + ".json", "PermanentID") ?? book.ID;

		public static async Task<bool> ExistsAsync(this IBookParser parser, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!string.IsNullOrWhiteSpace(parser.Title) && !string.IsNullOrWhiteSpace(parser.Author)
				&& await Utility.Cache.ExistsAsync((parser.Title + " - " + parser.Author).Trim().ToLower().GenerateUUID()).ConfigureAwait(false))
				return true;

			var filename = UtilityService.GetNormalizedFilename(parser.Title + " - " + parser.Author) + ".json";
			if (File.Exists(Path.Combine(Utility.GetFilePathOfBook(filename), filename)))
				return true;
			else if (File.Exists(Path.Combine(Utility.FolderOfTrashFiles, filename)))
				return true;

			return (!string.IsNullOrWhiteSpace(parser.Title) && !string.IsNullOrWhiteSpace(parser.Author)
				? await Book.GetAsync<Book>((parser.Title + " - " + parser.Author).Trim().ToLower().GenerateUUID(), cancellationToken).ConfigureAwait(false)
				: await Book.GetAsync(parser.Title, parser.Author, cancellationToken).ConfigureAwait(false)
			) != null;
		}
	}

	//  --------------------------------------------------------------------------------------------

	[Serializable, Repository]
	public abstract class Repository<T> : RepositoryBase<T> where T : class
	{
		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string ServiceName => ServiceBase.ServiceComponent.ServiceName;
	}
}