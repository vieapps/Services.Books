#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Books
{
	public static class Utility
	{

		#region Caching mechanism
		static int _CacheTime = 0;

		internal static int CacheTime
		{
			get
			{
				if (Utility._CacheTime < 1)
					try
					{
						Utility._CacheTime = UtilityService.GetAppSetting("CacheTime", "30").CastAs<int>();
					}
					catch
					{
						Utility._CacheTime = 30;
					}
				return Utility._CacheTime;
			}
		}

		static CacheManager _Cache = new CacheManager("VIEApps-Services-Books", "Sliding", Utility.CacheTime);

		public static CacheManager Cache { get { return Utility._Cache; } }

		static CacheManager _DataCache = new CacheManager("VIEApps-Services-Books-Data", "Absolute", Utility.CacheTime);

		public static CacheManager DataCache { get { return Utility._DataCache; } }
		#endregion

		#region Configuration settings
		static string _HttpFilesUri = null;

		static string HttpFilesUri
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._HttpFilesUri))
					Utility._HttpFilesUri = UtilityService.GetAppSetting("HttpFilesUri", "https://afs.vieapps.net");
				while (Utility._HttpFilesUri.EndsWith("/"))
					Utility._HttpFilesUri = Utility._HttpFilesUri.Left(Utility._HttpFilesUri.Length - 1);
				return Utility._HttpFilesUri;
			}
		}

		public static string MediaUri
		{
			get
			{
				return "book://media/";
			}
		}

		public static string MediaFolder
		{
			get
			{
				return "media-files";
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

		#region Helper methods
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

		public static string GetNormalized(this string @string)
		{
			int counter = -1;
			string result = @string.Trim();

			counter = 0;
			while (counter < 100 && (result.StartsWith("-") || result.StartsWith(".") || result.StartsWith(":")))
			{
				result = result.Right(result.Length - 1);
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.EndsWith("-") || result.EndsWith(".") || result.EndsWith(":")))
			{
				result = result.Left(result.Length - 1);
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.EndsWith("-") || result.EndsWith(".")))
			{
				result = result.Left(result.Length - 1);
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.IndexOf("( ") > -1))
			{
				result = result.Replace("( ", "(");
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.IndexOf(" )") > -1))
			{
				result = result.Replace(" )", ")");
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.IndexOf("( ") > -1))
			{
				result = result.Replace("( ", "(");
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.IndexOf(" )") > -1))
			{
				result = result.Replace(" )", ")");
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.IndexOf("  ") > -1))
			{
				result = result.Replace("  ", " ");
				counter++;
			}

			return result.Trim().GetCapitalizedWords();
		}

		public static string GetAuthor(string author)
		{
			var result
				= string.IsNullOrWhiteSpace(author)
					|| author.IsStartsWith("không rõ") || author.IsStartsWith("không xác định")
					|| author.IsStartsWith("sưu tầm") || author.Equals("vô danh")
					|| author.IsStartsWith("truyện ma") || author.IsStartsWith("kiếm hiệp")
					|| author.IsStartsWith("dân gian") || author.IsStartsWith("cổ tích")
				? "Khuyết Danh"
				: author.GetNormalized();

			result = result.Replace("(sưu tầm)", "").Replace("(dịch)", "").Trim();
			result = result.Replace("(phỏng dịch)", "").Replace("phỏng dịch", "").Trim();
			result = result.Replace("(phóng tác)", "").Replace("phóng tác", "").Trim();

			if (result.Equals("Andecxen")
				|| (result.IsStartsWith("Hans") && result.EndsWith("Andersen")))
				result = "Hans Christian Andersen";
			else if (result.Equals(result.ToUpper()))
				result = result.ToLower().GetNormalized();

			return result;
		}

		public static string GetFileSize(string filePath)
		{
			var fileInfo = new FileInfo(filePath);
			if (!fileInfo.Exists)
				return "generating...";

			var fileSize = Convert.ToDouble(fileInfo.Length);
			var size = fileSize / (1024 * 1024);
			if (size >= 1.0d)
				return size.ToString("##0.##") + " MBytes";
			else
			{
				size = fileSize / 1024;
				return size.ToString("##0.##") + " " + (size >= 1.0d ? "K" : "") + "Bytes";
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
			if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(attribute))
				return "";

			string json = UtilityService.ReadTextFile(filePath, 15).Aggregate((i, j) => i + "\n" + j).ToString();
			return Utility.GetDataOfJson(json, attribute);
		}
		#endregion

		#region Working with folders & files
		public static string GetFolderPathOfBook(string name)
		{
			return Utility.FolderOfDataFiles + (string.IsNullOrWhiteSpace(name) ? "" : @"\" + name.GetFirstChar().ToLower());
		}

		public static string GetFolderPath(this Book book)
		{
			return Utility.GetFolderPathOfBook(book.Name);
		}

		public static string GetFilePathOfBook(string name)
		{
			return Utility.GetFolderPathOfBook(name) + @"\" + UtilityService.GetNormalizedFilename(name);
		}

		public static string GetFilePathOfBook(string title, string author)
		{
			return Utility.GetFilePathOfBook(title.Trim() + (string.IsNullOrWhiteSpace(author) ? "" : " - " + Utility.GetAuthor(author)));
		}

		public static string GetFilePath(this Book book)
		{
			return Utility.GetFilePathOfBook(book.Title, book.Author);
		}

		public static string GetMediaFilePathOfBook(string uri, string name, string identifier)
		{
			var path = Utility.GetFolderPathOfBook(name) + @"\" + Utility.MediaFolder + @"\" + identifier + "-";
			return uri.Replace(Utility.MediaUri, path);
		}

		public static string GetMediaFilePath(this string uri, Book book)
		{
			return Utility.GetMediaFilePathOfBook(uri, book.Name, book.ID);
		}
		#endregion

		#region Working with URIs
		public static string GetMediaFileUri(this Book book)
		{
			return Utility.HttpFilesUri + "/books/" + Utility.MediaFolder + "/"
				+ (book != null
					? book.Name.Url64Encode() + "/" + book.PermanentID + "/"
					: "no-media-file".Url64Encode() + "/book.png");
		}

		public static string NormalizeMediaFileUris(this string content, Book book)
		{
			return string.IsNullOrWhiteSpace(content)
				? content
				: content.Replace(Utility.MediaUri, book.GetMediaFileUri());
		}

		public static string GetDownloadUri(this Book book)
		{
			return Utility.HttpFilesUri + "/books/download/" + book.Name.Url64Encode() + "/" + book.Name.GetANSIUri();
		}
		#endregion

	}

	//  --------------------------------------------------------------------------------------------

	[Serializable]
	[Repository]
	public abstract class Repository<T> : RepositoryBase<T> where T : class { }
}