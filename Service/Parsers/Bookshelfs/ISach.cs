#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Books.Parsers.Bookshelfs
{
	public class ISach : IBookshelfParser
	{

		#region Properties
		[JsonIgnore]
		public string UrlPattern { get; set; } = "";
		[JsonIgnore]
		public List<string> UrlParameters { get; set; }
		public int TotalPages { get; set; } = 0;
		public int CurrentPage { get; set; } = 1;
		[JsonIgnore]
		public List<IBookParser> BookParsers { get; set; } = new List<IBookParser>();
		public string Category { get; set; } = ISach.Categories[0];
		public int CategoryIndex { get; set; } = 0;
		public string Char { get; set; } = "0";
		[JsonIgnore]
		public string ReferUrl { get; set; } = "https://isach.info/mobile/index.php";

		public static List<string> Categories
		{
			get
			{
				return new List<string>() { "kiem_hiep", "tien_hiep", "tuoi_hoc_tro", "co_tich", "truyen_ngan", "truyen_cuoi", "kinh_di", "khoa_hoc", "tuy_but", "tieu_thuyet", "ngon_tinh", "trinh_tham", "trung_hoa", "ky_nang_song", "nghe_thuat_song" };
			}
		}
		public static HashSet<string> LargeCategories
		{
			get
			{
				return new HashSet<string>() { "truyen_ngan", "truyen_cuoi", "tieu_thuyet", "nghe_thuat_song" };
			}
		}
		#endregion

		public IBookshelfParser Initialize(string folder = null)
		{
			var filePath = (!string.IsNullOrWhiteSpace(folder) ? folder + @"\" : "") + "isach.info.status.json";
			var json = File.Exists(filePath)
				? JObject.Parse(new FileInfo(filePath).ReadAsText())
				: new JObject
				{
					{ "TotalPages", 0 },
					{ "CurrentPage", 0 },
					{ "Category", ISach.Categories[0] },
					{ "CategoryIndex", 0 },
					{ "Char", "0" },
					{ "LastActivity", DateTime.Now },
				};

			this.TotalPages = (json["TotalPages"] as JValue).Value.CastAs<int>();
			this.CurrentPage = (json["CurrentPage"] as JValue).Value.CastAs<int>();
			this.Category = (json["Category"] as JValue).Value.CastAs<string>();
			this.CategoryIndex = (json["CategoryIndex"] as JValue).Value.CastAs<int>();
			this.Char = ISach.LargeCategories.Contains(this.Category)
				? (json["Char"] as JValue).Value.CastAs<string>()
				: null;

			return this.Prepare();
		}

		public IBookshelfParser FinaIize(string folder)
		{
			if (string.IsNullOrWhiteSpace(folder))
				throw new ArgumentNullException(nameof(folder));
			else if (!Directory.Exists(folder))
				throw new DirectoryNotFoundException($"Not found [{folder}]");

			var json = JObject.FromObject(this);
			json.Add(new JProperty("LastActivity", DateTime.Now));
			json.ToString(Formatting.Indented).ToBytes().ToMemoryStream().SaveAsTextAsync(Path.Combine(folder, "isach.info.status.json")).Run(true);

			return this;
		}

		public IBookshelfParser Prepare()
		{
			if (this.TotalPages < 1)
			{
				this.TotalPages = 0;
				this.CurrentPage = 0;
				this.UrlPattern = "https://isach.info/mobile/most_reading.php?sort=last_update_date";
			}
			else if (this.CurrentPage >= this.TotalPages)
			{
				this.UrlPattern = null;
				/*
				this.TotalPages = 0;
				this.CurrentPage = 0;
				var index = ISach.Categories.IndexOf(this.Category);
				if (!string.IsNullOrWhiteSpace(this.Char))
				{
					if (this.Char[0].Equals('0'))
						this.Char = "A";
					else if (this.Char[0] < 'Z')
					{
						char ch = this.Char[0];
						ch++;
						this.Char = ch.ToString();
					}
					else
					{
						index++;
						this.Char = null;
					}
				}
				else
				{
					index++;
					this.Char = null;
				}
				this.Category = index < ISach.Categories.Count ? ISach.Categories[index] : null;
				this.Char = string.IsNullOrWhiteSpace(this.Category)
					? null
					: string.IsNullOrWhiteSpace(this.Char) && ISach.LargeCategories.Contains(this.Category) ? "0" : this.Char;
				*/
			}

			this.CurrentPage++;

			/*
			this.UrlPattern = string.IsNullOrWhiteSpace(this.Category)
				? null
				: string.IsNullOrWhiteSpace(this.Char)
					? "https://isach.info/mobile/story.php?list=story&category={0}&order=last_update_date&page={1}"
					: "https://isach.info/mobile/story.php?list=story&category={0}&order=last_update_date&char={1}&page={2}";
			*/

			this.UrlParameters = new List<string>();
			/*
			if (!string.IsNullOrWhiteSpace(this.Category))
				this.UrlParameters.Add(this.Category);
			if (!string.IsNullOrWhiteSpace(this.Char))
				this.UrlParameters.Add(this.Char);
			*/
			this.UrlParameters.Add(this.CurrentPage.ToString());

			return this;
		}

		public async Task<IBookshelfParser> ParseAsync(Action<IBookshelfParser, long> onCompleted = null, Action<IBookshelfParser, Exception> onError = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var stopwatch = new Stopwatch();
			stopwatch.Start();

			// get HTML
			var url = this.UrlPattern;
			this.UrlParameters.ForEach((parameter, index) => url = url.Replace("{" + index + "}", parameter));

			var html = "";
			try
			{
				html = await UtilityService.FetchHttpAsync(url, new Dictionary<string, string> { ["User-Agent"] = UtilityService.SpiderUserAgent, ["Referer"] = this.ReferUrl }, 90, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				onError?.Invoke(this, ex);
				if (onError == null)
					throw;
				return this;
			}

			// parse
			await this.ParseAsync(html, cancellationToken).ConfigureAwait(false);

			// callback when done
			stopwatch.Stop();
			onCompleted?.Invoke(this, stopwatch.ElapsedMilliseconds);
			return this;
		}

		async Task ParseAsync(string html, CancellationToken cancellationToken = default)
		{
			// pages
			int start = -1, end = -1;
			if (this.TotalPages < 1)
			{
				start = html.PositionOf("paging_box_top");
				start = start < 0 ? -1 : html.PositionOf("<ul", start + 1);
				end = start < 0 ? -1 : html.PositionOf("</div>", start + 1);
				if (start > 0 && end > 0)
				{
					string info = html.Substring(start, end - start), data = "";
					start = info.PositionOf("<a");
					while (start > -1)
					{
						end = info.PositionOf(">", start + 1);
						string anchor = info.Substring(start, end - start);
						if (anchor.PositionOf("class='navigator'") < 0)
						{
							start = info.PositionOf("href=\"", start + 1) + 6;
							end = info.PositionOf("\"", start + 1);
							data = info.Substring(start, end - start);
						}
						start = info.PositionOf("<a", start + 1);
					}
					start = data.PositionOf("page=");
					this.TotalPages = Convert.ToInt32(data.Substring(start + 5));
				}
				else if (html.PositionOf("paging_box_empty") > 0)
					this.TotalPages = 1;
				else
					this.TotalPages = 1;
			}

			// books
			this.BookParsers = new List<IBookParser>();

			start = html.PositionOf("<div class='ms_list_item'>", start + 1);
			start = start < 0 ? -1 : html.PositionOf("<div class='ms_list_item'>", start + 1);
			end = start < 0 ? -1 : html.PositionOf("<div class='ms_quote'", start + 1);
			html = start > 0 && end > 0 ? html.Substring(start, end - start) : "";

			start = html.PositionOf("<div class='ms_list_item'>");
			start = html.PositionOf("<a", start + 1) > -1 ? start : -1;
			while (start > -1)
			{
				start = html.PositionOf("<a", start + 1);
				end = html.PositionOf(">", start + 1);
				if (html.Substring(start, end - start).PositionOf("story.php") < 0)
					start = html.PositionOf("<a", start + 1);
				start = html.PositionOf("href=\"", start + 1) + 6;
				end = html.PositionOf("\"", start + 1);

				try
				{
					await Task.Delay(UtilityService.GetRandomNumber(123, 456), cancellationToken).ConfigureAwait(false);
					var book = await new Books.ISach().ParseAsync("https://isach.info" + html.Substring(start, end - start).Trim(), null, null, cancellationToken).ConfigureAwait(false);
					this.BookParsers.Add(book);
				}
				catch { }

				start = html.PositionOf("<div class='ms_list_item'>", start + 1);
			}

			/*
			start = html.PositionOf("story_content_list");
			start = start < 0 ? -1 : html.PositionOf("<div class='ms_list_item'>", start + 1);
			end = start < 0 ? -1 : html.PositionOf("<div class='paging_box_bottom'>", start + 1);
			html = start > 0 && end > 0 ? html.Substring(start, end - start) : "";

			start = html.PositionOf("<div class='ms_list_item'>");
			start = html.PositionOf("<a", start + 1) > -1 ? start : -1;
			while (start > -1)
			{
				var book = new Parsers.Books.ISach();

				start = html.PositionOf("<a", start + 1);
				end = html.PositionOf(">", start + 1);
				if (html.Substring(start, end - start).PositionOf("story.php") < 0)
					start = html.PositionOf("<a", start + 1);
				start = html.PositionOf("href=\"", start + 1) + 6;
				end = html.PositionOf("\"", start + 1);
				book.SourceUrl = "https://isach.info/mobile/" + html.Substring(start, end - start).Trim();

				start = html.PositionOf(">", start + 1) + 1;
				end = html.PositionOf("</a>", start + 1);
				book.Title = html.Substring(start, end - start).GetNormalized();

				start = html.PositionOf("<a", start + 1) + 1;
				end = html.PositionOf(">", start + 1);
				start = html.PositionOf("<span", start + 1);
				start = html.PositionOf(">", start + 1) + 1;
				end = html.PositionOf("<", start + 1);
				book.Category = html.Substring(start, end - start).GetCategory();

				start = html.PositionOf("<a", start + 1) + 1;
				start = html.PositionOf("<span", start + 1);
				start = html.PositionOf(">", start + 1) + 1;
				end = html.PositionOf("<", start + 1);
				book.Author = html.Substring(start, end - start).GetAuthor();

				this.BookParsers.Add(book);

				start = html.PositionOf("<div class='ms_list_item'>", start + 1);
			}
			*/
		}
	}
}