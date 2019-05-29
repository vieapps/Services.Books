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
	public class VnThuQuan : IBookshelfParser
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
		public string Category { get; set; } = "";
		public int CategoryIndex { get; set; } = 0;
		public string Char { get; set; } = "0";
		[JsonIgnore]
		public string ReferUrl { get; set; } = "https://vnthuquan.net/mobil/";
		#endregion

		public IBookshelfParser Initialize(string folder = null)
		{
			var filePath = (!string.IsNullOrWhiteSpace(folder) ? folder + @"\" : "") + "vnthuquan.net.status.json";
			var json = File.Exists(filePath)
				? JObject.Parse(UtilityService.ReadTextFile(filePath))
				: new JObject
				{
					{ "TotalPages", 0 },
					{ "CurrentPage", 0 },
					{ "Category", "" },
					{ "CategoryIndex", 0 },
					{ "Char", "0" },
					{ "LastActivity", DateTime.Now },
				};

			this.TotalPages = (json["TotalPages"] as JValue).Value.CastAs<int>();
			this.CurrentPage = (json["CurrentPage"] as JValue).Value.CastAs<int>();

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
			UtilityService.WriteTextFile(folder + @"\vnthuquan.net.status.json", json.ToString(Formatting.Indented));

			return this;
		}

		public IBookshelfParser Prepare()
		{
			if (this.TotalPages < 1)
			{
				this.CurrentPage = 0;
				this.TotalPages = 0;
				this.UrlPattern = "https://vnthuquan.net/truyen/?tranghientai={0}";
			}
			else if (this.CurrentPage >= this.TotalPages)
				this.UrlPattern = null;

			this.CurrentPage++;

			this.UrlParameters = new List<string>
			{
				this.CurrentPage.ToString()
			};

			return this;
		}

		public async Task<IBookshelfParser> ParseAsync(Action<IBookshelfParser, long> onCompleted = null, Action<IBookshelfParser, Exception> onError = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			// prepare
			var stopwatch = Stopwatch.StartNew();
			var url = this.UrlPattern;
			this.UrlParameters.ForEach((parameter, index) => url = url.Replace("{" + index + "}", parameter));

			var html = "";
			try
			{
				html = await url.GetVnThuQuanHtmlAsync("GET", this.ReferUrl, null, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				onError?.Invoke(this, ex);
				if (onError == null)
					throw ex;
				return this;
			}

			// parse
			this.Parse(html);

			// callback when done
			stopwatch.Stop();
			onCompleted?.Invoke(this, stopwatch.ElapsedMilliseconds);
			return this;
		}

		void Parse(string html)
		{
			// pages
			int start = -1, end = -1;
			if (this.TotalPages < 1)
			{
				start = html.PositionOf("id=\"content\"");
				start = start < 0 ? -1 : html.PositionOf("<a target='_self'");
				end = start < 0 ? -1 : html.PositionOf("</h1>", start + 1);
				if (start > 0 && end > 0)
				{
					var info = html.Substring(start, end - start);
					var data = "";
					start = info.PositionOf("<a");
					while (start > -1)
					{
						start = info.PositionOf("href='", start + 1) + 6;
						end = info.PositionOf("'", start + 1);
						data = info.Substring(start, end - start);
						start = info.PositionOf("<a", start + 1);
					}
					data = data.Substring(data.PositionOf("=") + 1);
					this.TotalPages = data.Equals("#")
						? this.CurrentPage
						: Convert.ToInt32(data);
				}
			}

			// books
			this.BookParsers = new List<IBookParser>();

			start = html.PositionOf("id=\"content\"");
			start = start < 0 ? -1 : html.PositionOf("<ul", start + 1);
			start = start < 0 ? -1 : html.PositionOf(">", start + 1) + 1;
			end = start < 0 ? -1 : html.PositionOf("</ul>", start + 1);
			html = start > 0 && end > 0 ? html.Substring(start, end - start) : "";

			start = html.PositionOf("<span");
			while (start > -1)
			{
				var book = new Books.VnThuQuan();

				start = html.PositionOf("<a", start + 1);
				start = html.PositionOf(">", start + 1) + 1;
				end = html.PositionOf("<", start + 1);
				book.Category = html.Substring(start, end - start).Trim().GetCategory();

				start = html.PositionOf("<a", start + 1);
				start = html.PositionOf(">", start + 1) + 1;
				end = html.PositionOf("<", start + 1);
				var author = html.Substring(start, end - start).Trim();
				"Đồng tác giả|Dịch giả|Người dịch|Dịch viện|Chuyển ngữ|Dịch ra|Anh dịch|Dịch thuật|Bản dịch|Hiệu đính|Biên Tập|Biên soạn|đánh máy bổ sung|Nguyên tác|Nguyên bản|Dịch theo|Dịch từ|Theo bản|Biên dịch|Tổng Hợp|Tủ Sách|Sách Xuất Bản Tại|Chủ biên|Chủ nhiệm".Split('|')
					.ForEach(excluded =>
					{
						var pos = author.PositionOf(excluded);
						if (pos > -1)
							author = author.Remove(pos).GetNormalized();
					});
				book.Author = author.GetAuthor();

				start = html.PositionOf("<a", start + 1) + 1;
				start = html.PositionOf("href='", start + 1) + 6;
				end = html.PositionOf("'", start + 1);
				book.SourceUrl = "https://vnthuquan.net/truyen/" + html.Substring(start, end - start).Trim();

				start = html.PositionOf(">", start + 1) + 1;
				end = html.PositionOf("<", start + 1);
				book.Title = html.Substring(start, end - start).GetNormalized().Replace(StringComparison.OrdinalIgnoreCase, "<br>", " ");
				if (book.Title.Equals(book.Title.ToUpper()))
					book.Title = book.Title.ToLower().GetNormalized();

				if (!book.Title.IsEndsWith(" - Epub") && !book.Title.IsEndsWith(" - Pdf") && !book.Title.IsEndsWith(" - Audio"))
					this.BookParsers.Add(book);

				start = html.PositionOf("<span", start + 1);
			}
		}
	}
}