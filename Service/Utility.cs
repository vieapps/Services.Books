#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

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
						Utility._CacheExpirationTime = UtilityService.GetAppSetting("CacheExpirationTime", "30").CastAs<int>();
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
				return Utility._Cache ?? (Utility._Cache = new Cache("VIEApps-Services-Books", Utility.CacheExpirationTime, UtilityService.GetAppSetting("CacheProvider")));
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
					Utility._FilesHttpUri = UtilityService.GetAppSetting("FilesHttpUri", "https://afs.vieapps.net");
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
					Utility._FilesPath = UtilityService.GetAppSetting("BookFilesPath");
					if (string.IsNullOrWhiteSpace(Utility._FilesPath))
						Utility._FilesPath = Directory.GetCurrentDirectory() + @"\data-files\books";
					if (!Utility._FilesPath.EndsWith(@"\"))
						Utility._FilesPath += @"\";
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
			string result = UtilityService.GetNormalizedFilename(@string).ConvertUnicodeToANSI().Trim();
			if (string.IsNullOrWhiteSpace(result))
				return "0";

			string[] specials = new string[] { "-", ".", "'", "+", "&", "“", "”" };
			foreach (string special in specials)
			{
				while (result.StartsWith(special))
					result = result.Right(result.Length - 1).Trim();
				while (result.EndsWith(special))
					result = result.Left(result.Length - 1).Trim();
			}
			if (string.IsNullOrWhiteSpace(result))
				return "0";

			int index = 0;
			bool isCorrect = false;
			while (!isCorrect && index < result.Length)
			{
				char @char = result.ToUpper()[index];
				isCorrect = (@char >= '0' && @char <= '9') || (@char >= 'A' && @char <= 'Z');
				if (!isCorrect)
					index++;
			}

			char firstChar = index < result.Length
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
			return Utility.FolderOfDataFiles + (string.IsNullOrWhiteSpace(name) ? "" : Path.DirectorySeparatorChar.ToString() + name.GetFirstChar().ToLower());
		}

		public static string GetFilePathOfBook(string name)
		{
			return Path.Combine(Utility.GetFolderPathOfBook(name), UtilityService.GetNormalizedFilename(name));
		}

		public static string GetFilePathOfBook(string title, string author)
		{
			return Utility.GetFilePathOfBook(title.Trim() + (string.IsNullOrWhiteSpace(author) ? "" : " - " + author.GetAuthor()));
		}

		public static string GetFolderPath(this Book book)
		{
			return Utility.GetFolderPathOfBook(book.Name);
		}

		public static string GetMediaFilePathOfBook(string uri, string name, string identifier)
		{
			var path = Path.Combine(Utility.GetFolderPathOfBook(name), Definitions.MediaFolder, identifier + "-");
			return uri.Replace(Definitions.MediaUri, path);
		}

		public static string GetMediaFilePath(this Book book)
		{
			return Path.Combine(book.GetFolderPath(), Definitions.MediaFolder) + Path.DirectorySeparatorChar.ToString()
				+ (book != null
					? (!string.IsNullOrWhiteSpace(book.PermanentID) ? book.PermanentID : book.ID) + "-"
					: "no-media-file".Url64Encode() + "/no/cover/image.png");
		}
		#endregion

		#region Working with file (file size, get data of JSON file, ...)
		public static string GetFileSize(string filePath)
		{
			var fileInfo = new FileInfo(filePath);
			if (!fileInfo.Exists)
				return "generating...";

			var fileSize = fileInfo.Length.CastAs<double>();
			var size = fileSize / (1024 * 1024);
			if (size >= 1.0d)
				return size.ToString("##0.##") + " M";
			else
			{
				size = fileSize / 1024;
				return size.ToString("##0.##") + " " + (size >= 1.0d ? "K" : "B");
			}
		}

		public static string GetDataOfJson(string json, string attribute)
		{
			if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(attribute))
				return "";

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
				: "";
		}

		public static string GetDataFromJsonFile(string filePath, string attribute)
		{
			filePath = UtilityService.GetNormalizedFilename(filePath);
			if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(attribute))
				return "";

			string json = UtilityService.ReadTextFile(filePath, 15).Aggregate((i, j) => i + "\n" + j).ToString();
			return Utility.GetDataOfJson(json, attribute);
		}
		#endregion

		#region Working with URIs
		public static string GetMediaFileUri(this Book book)
		{
			return Utility.FilesHttpUri + "/books/" + Definitions.MediaFolder + "/"
				+ (book != null
					? book.Title.Url64Encode() + "/" + (!string.IsNullOrWhiteSpace(book.PermanentID) ? book.PermanentID : book.ID) + "/"
					: "no-media-file".Url64Encode() + "/no/cover/image.png");
		}

		public static string NormalizeMediaFileUris(this string content, Book book)
		{
			return string.IsNullOrWhiteSpace(content)
				? content
				: content.Replace(Definitions.MediaUri, book.GetMediaFileUri());
		}

		public static string NormalizeMediaFilePaths(this string content, Book book)
		{
			return string.IsNullOrWhiteSpace(content)
				? content
				: content.Replace(Definitions.MediaUri, book.GetMediaFilePath());
		}

		public static string GetDownloadUri(this Book book)
		{
			return Utility.FilesHttpUri + "/books/download/" + book.Name.Url64Encode() + "/" + book.ID.Url64Encode() + "/" + book.Title.GetANSIUri();
		}
		#endregion

		#region Working with JSON file
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

		internal static bool IsExisted(string name)
		{
			var isExisted = File.Exists(Path.Combine(Utility.GetFolderPathOfBook(name), UtilityService.GetNormalizedFilename(name) + ".json"));
			if (!isExisted)
				isExisted = File.Exists(Path.Combine(Utility.FolderOfTrashFiles, UtilityService.GetNormalizedFilename(name) + ".json"));
			return isExisted;
		}

		internal static bool IsExisted(string title, string author)
		{
			return Utility.IsExisted(title.Trim() + (string.IsNullOrWhiteSpace(author) ? "" : " - " + author.GetAuthor()));
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
		#endregion

	}

	//  --------------------------------------------------------------------------------------------

	[Serializable]
	[Repository]
	public abstract class Repository<T> : RepositoryBase<T> where T : class { }
}