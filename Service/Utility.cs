#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Books
{
	public static class Utility
	{

		#region Caching mechanism
		static int _CacheExpirationTime = -1;

		internal static int CacheExpirationTime
		{
			get
			{
				if (Utility._CacheExpirationTime < 0)
					try
					{
						Utility._CacheExpirationTime = UtilityService.GetAppSetting("Cache:ExpirationTime", "30").CastAs<int>();
					}
					catch
					{
						Utility._CacheExpirationTime = 30;
					}
				return Utility._CacheExpirationTime;
			}
		}

		static Cache _Cache = null;

		public static Cache Cache
		{
			get
			{
				return Utility._Cache ?? (Utility._Cache = new Cache(UtilityService.GetAppSetting("Books:CacheName","VIEApps-Services-Books"), Utility.CacheExpirationTime, UtilityService.GetAppSetting("Cache:Provider")));
			}
		}
		#endregion

		#region Configuration settings
		static string _FilesHttpUri = null;

		static string FilesHttpUri
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._FilesHttpUri))
					Utility._FilesHttpUri = UtilityService.GetAppSetting("HttpUri:Files", "https://afs.vieapps.net");
				while (Utility._FilesHttpUri.EndsWith("/"))
					Utility._FilesHttpUri = Utility._FilesHttpUri.Left(Utility._FilesHttpUri.Length - 1);
				return Utility._FilesHttpUri;
			}
		}

		static string _FilesPath = null;

		internal static string FilesPath
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._FilesPath))
				{
					Utility._FilesPath = UtilityService.GetAppSetting("Path:Books");
					if (string.IsNullOrWhiteSpace(Utility._FilesPath))
						Utility._FilesPath = Path.Combine(Directory.GetCurrentDirectory(), "data-files", "books");
					if (!Utility._FilesPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
						Utility._FilesPath += Path.DirectorySeparatorChar.ToString();
				}
				return Utility._FilesPath;
			}
		}

		/// <summary>
		/// Gets the folder of data files
		/// </summary>
		public static string FolderOfDataFiles
		{
			get
			{
				return Utility.FilesPath + "files";
			}
		}

		/// <summary>
		/// Gets the folder of statistic files
		/// </summary>
		public static string FolderOfStatisticFiles
		{
			get
			{
				return Utility.FilesPath + "statistics";
			}
		}

		/// <summary>
		/// Gets the folder of contributed files
		/// </summary>
		public static string FolderOfContributedFiles
		{
			get
			{
				return Utility.FilesPath + "contributions";
			}
		}

		/// <summary>
		/// Gets the folder of temp files
		/// </summary>
		public static string FolderOfTempFiles
		{
			get
			{
				return Utility.FilesPath + "temp";
			}
		}

		/// <summary>
		/// Gets the folder of trash files
		/// </summary>
		public static string FolderOfTrashFiles
		{
			get
			{
				return Utility.FilesPath + "trash";
			}
		}
		#endregion

		#region Avalable characters
		static List<string> _Chars = null;

		public static List<string> Chars
		{
			get
			{
				if (Utility._Chars == null)
				{
					Utility._Chars = new List<string>() { "0" };
					for (char @char = 'A'; @char <= 'Z'; @char++)
						Utility._Chars.Add(@char.ToString());
				}
				return Utility._Chars;
			}
		}

		public static string GetFirstChar(this string @string, bool userLower = true)
		{
			var result = UtilityService.GetNormalizedFilename(@string).ConvertUnicodeToANSI().Trim();
			if (string.IsNullOrWhiteSpace(result))
				return "0";

			var specials = new string[] { "-", ".", "'", "+", "&", "“", "”" };
			foreach (var special in specials)
			{
				while (result.StartsWith(special))
					result = result.Right(result.Length - 1).Trim();
				while (result.EndsWith(special))
					result = result.Left(result.Length - 1).Trim();
			}
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
		public static string GetFolderPathOfBook(string name)
		{
			return Path.Combine(Utility.FolderOfDataFiles, name.GetFirstChar().ToLower());
		}

		public static string GetFolderPath(this Book book)
		{
			return Utility.GetFolderPathOfBook(book.Name);
		}

		public static string GetFilePathOfBook(string name)
		{
			return Path.Combine(Utility.GetFolderPathOfBook(name), UtilityService.GetNormalizedFilename(name));
		}

		public static string GetFilePathOfBook(string title, string author)
		{
			return Utility.GetFilePathOfBook(title.Trim() + (string.IsNullOrWhiteSpace(author) ? "" : " - " + author.GetAuthor()));
		}

		public static string GetFilePath(this Book book)
		{
			return Utility.GetFilePathOfBook(book.Name);
		}

		public static string GetMediaFilePathOfBook(string uri, string name, string identifier)
		{
			var path = Path.Combine(Utility.GetFolderPathOfBook(name), Definitions.MediaFolder, identifier + "-");
			return uri.Replace(Definitions.MediaURI, path);
		}

		public static string GetFileSize(string filePath)
		{
			return UtilityService.GetFileSize(filePath) ?? "generating...";
		}
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
			var key = book.GetCacheKey() + "-json";
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

		public static async Task<Book> GetBookAsync(this Book book)
		{
			var key = book.GetCacheKey() + "-json";
			var full = await Utility.Cache.GetAsync<Book>(key).ConfigureAwait(false);
			if (full == null)
			{
				full = book.Clone();
				if (File.Exists(book.GetFilePath() + ".json"))
					full.Copy(JObject.Parse(await UtilityService.ReadTextFileAsync(book.GetFilePath() + ".json").ConfigureAwait(false)));
				await Utility.Cache.SetAsFragmentsAsync(key, full).ConfigureAwait(false);
			}
			return full;
		}

		internal static void Copy(this Book book, JObject json)
		{
			book.PermanentID = (json["PermanentID"] as JValue).Value as string;
			book.SourceUrl = json["SourceUrl"] != null
				? (json["SourceUrl"] as JValue).Value as string
				: json["SourceUri"] != null
					? (json["SourceUri"] as JValue).Value as string
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
		public static string GetMediaFileUri(this Book book)
		{
			return Utility.FilesHttpUri + "/books/" + Definitions.MediaFolder + "/";
		}

		public static string GetCoverImageUri(this Book book)
		{
			return string.IsNullOrWhiteSpace(book.Cover)
				? book.GetMediaFileUri() + "no-media-file".Url64Encode() + "/no/cover/image.png"
				: book.Cover.Replace(Definitions.MediaURI, book.GetMediaFileUri() + book.Title.Url64Encode() + "/" + book.GetPermanentID() + "/");

		}

		public static string NormalizeMediaFileUris(this Book book, string content)
		{
			return string.IsNullOrWhiteSpace(content)
				? content
				: content.Replace(Definitions.MediaURI, book.Title.Url64Encode() + "/" + book.GetPermanentID() + "/");
		}

		public static string NormalizeMediaFilePaths(this string content, Book book)
		{
			return string.IsNullOrWhiteSpace(content)
				? content
				: content.Replace(Definitions.MediaURI, Path.Combine(book.GetFolderPath(), Definitions.MediaFolder, book.GetPermanentID() + "-"));
		}

		public static string GetDownloadUri(this Book book)
		{
			return Utility.FilesHttpUri + "/books/download/" + book.Name.Url64Encode() + "/" + book.ID.Url64Encode() + "/" + book.Title.GetANSIUri();
		}
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
		{
			return !string.IsNullOrWhiteSpace(book.PermanentID) && book.PermanentID.IsValidUUID()
				? book.PermanentID
				: Utility.GetBookAttribute(book.GetFilePath() + ".json", "PermanentID") ?? book.ID;
		}

		public static async Task<bool> ExistsAsync(this IBookParser parser, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!string.IsNullOrWhiteSpace(parser.Title) && !string.IsNullOrWhiteSpace(parser.Author)
				&& await Utility.Cache.ExistsAsync((parser.Title + " - " + parser.Author).Trim().ToLower().GetMD5()).ConfigureAwait(false))
				return true;

			var filename = UtilityService.GetNormalizedFilename(parser.Title + " - " + parser.Author) + ".json";
			if (File.Exists(Path.Combine(Utility.GetFilePathOfBook(filename), filename)))
				return true;
			else if (File.Exists(Path.Combine(Utility.FolderOfTrashFiles, filename)))
				return true;

			return (!string.IsNullOrWhiteSpace(parser.Title) && !string.IsNullOrWhiteSpace(parser.Author)
				? await Book.GetAsync<Book>((parser.Title + " - " + parser.Author).Trim().ToLower().GetMD5(), cancellationToken).ConfigureAwait(false)
				: await Book.GetAsync(parser.Title, parser.Author, cancellationToken).ConfigureAwait(false)
			) != null;
		}
	}

	//  --------------------------------------------------------------------------------------------

	[Serializable]
	[Repository]
	public abstract class Repository<T> : RepositoryBase<T> where T : class { }
}