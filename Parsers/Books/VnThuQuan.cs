#region Related components
using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Books.Parsers.Books
{
	public class VnThuQuan : IBookParser
	{

		#region Properties
		public string ID { get; set; }
		public string PermanentID { get; set; }
		public string Title { get; set; }
		public string Category { get; set; }
		public string Author { get; set; }
		public string Original { get; set; } = "";
		public string Translator { get; set; } = "";
		public string Publisher { get; set; } = "";
		public string Summary { get; set; } = "";
		public string Cover { get; set; }
		public string Source { get; set; } = "vnthuquan.net";
		public string SourceUrl { get; set; } = "";
		public string Credits { get; set; } = "";
		public string Contributor { get; set; } = "";
		public string Tags { get; set; } = "";
		public string Language { get; set; } = "vi";
		public int TotalChapters { get; set; } = 0;
		[JsonIgnore]
		public string ReferUrl { get; set; } = "https://vnthuquan.net/truyen/";
		public List<string> TOCs { get; set; } = new List<string>();
		public List<string> Chapters { get; set; } = new List<string>();
		[JsonIgnore]
		public List<string> MediaFileUrls { get; set; } = new List<string>();
		#endregion

		public async Task<IBookParser> ParseAsync(string url = null, Action<IBookParser, long> onCompleted = null, Action<IBookParser, Exception> onError = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				// get HTML of the book
				var stopwatch = Stopwatch.StartNew();
				this.SourceUrl = "https://vnthuquan.net/truyen/truyen.aspx?tid=" + (url ?? this.SourceUrl).GetIdentity();
				var html = await UtilityService.GetWebPageAsync(this.SourceUrl, this.ReferUrl, UtilityService.MobileUserAgent, cancellationToken).ConfigureAwait(false);

				// parse to get details
				await UtilityService.ExecuteTask(() => this.ParseBook(html), cancellationToken).ConfigureAwait(false);

				// permanent identity
				if (string.IsNullOrWhiteSpace(this.PermanentID) || !this.PermanentID.IsValidUUID())
					this.PermanentID = (this.Title + " - " + this.Author).Trim().ToLower().GetMD5();

				// callback when done
				stopwatch.Stop();
				onCompleted?.Invoke(this, stopwatch.ElapsedMilliseconds);
				return this;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				onError?.Invoke(this, ex);
				if (onError == null)
					throw ex;
				return this;
			}
		}

		public async Task<IBookParser> FetchAsync(string url = null,
			Action<IBookParser> onStart = null, Action<IBookParser, long> onParsed = null, Action<IBookParser, long> onCompleted = null,
			Action<int> onStartFetchChapter = null, Action<int, List<string>, long> onFetchChapterCompleted = null, Action<int, Exception> onFetchChapterError = null,
			string directoryOfImages = null, Action<IBookParser, string> onStartDownload = null, Action<string, string, long> onDownloadCompleted = null, Action<string, Exception> onDownloadError = null,
			bool parallelExecutions = true, CancellationToken cancellationToken = default(CancellationToken))
		{
			// prepare
			var stopwatch = Stopwatch.StartNew();

			// parse the book
			onStart?.Invoke(this);
			if (!string.IsNullOrWhiteSpace(url))
			{
				await this.ParseAsync(url, onParsed, null, cancellationToken).ConfigureAwait(false);

				// fetch the first chapter
				var first = await this.FetchChapterAsync(0, onStartFetchChapter, onFetchChapterCompleted, onFetchChapterError, cancellationToken).ConfigureAwait(false);

				// compute other information (original, translator, ...)
				if (string.IsNullOrWhiteSpace(this.Original))
					this.Original = this.GetOriginal(first);

				if (string.IsNullOrWhiteSpace(this.Translator))
					this.Translator = this.GetTranslator(first);

				if (string.IsNullOrWhiteSpace(this.Credits))
					this.Credits = this.GetCredits(first);

				if (first != null && first.Count > 1 && first[1].PositionOf("=\"anhbia\"") > 0)
				{
					var cover = this.GetCoverImage(first);
					if (string.IsNullOrWhiteSpace(this.Cover) || !this.Cover.IsEquals(Definitions.MediaURI + cover.GetFilename()))
					{
						this.MediaFileUrls.Add(cover);
						this.Cover = Definitions.MediaURI + cover.GetFilename();
					}
					if (this.Chapters[0].PositionOf(cover) > 0)
					{
						var start = this.Chapters[0].PositionOf("<img");
						var end = this.Chapters[0].PositionOf(">", start);
						this.Chapters[0] = this.Chapters[0].Remove(start, end - start + 1);
						this.Chapters[0] = this.Chapters[0].Replace("<p></p>", "").Replace(StringComparison.OrdinalIgnoreCase, "<p align=\"center\"></p>", "");
					}
				}
			}

			// fetch chapters
			if (this.Chapters.Count > (string.IsNullOrWhiteSpace(url) ? 0 : 1))
			{
				if (parallelExecutions)
				{
					var tasks = new List<Task<List<string>>>();
					for (var index = string.IsNullOrWhiteSpace(url) ? 0 : 1; index < this.Chapters.Count; index++)
						tasks.Add(this.Chapters[index].IsStartsWith("https://vnthuquan.net") || this.Chapters[index].IsStartsWith("http://vnthuquan.net")
							? this.FetchChapterAsync(index, onStartFetchChapter, onFetchChapterCompleted, onFetchChapterError, cancellationToken)
							: Task.FromResult<List<string>>(null)
						);
					await Task.WhenAll(tasks).ConfigureAwait(false);
				}
				else
					for (var index = string.IsNullOrWhiteSpace(url) ? 0 : 1; index < this.Chapters.Count; index++)
						if (this.Chapters[index].IsStartsWith("https://vnthuquan.net") || this.Chapters[index].IsStartsWith("http://vnthuquan.net"))
							await this.FetchChapterAsync(index, onStartFetchChapter, onFetchChapterCompleted, onFetchChapterError, cancellationToken).ConfigureAwait(false);
			}

			// download image files
			if (this.MediaFileUrls.Count > 0)
			{
				directoryOfImages = directoryOfImages ?? "temp";
				onStartDownload?.Invoke(this, directoryOfImages);
				await Task.WhenAll(this.MediaFileUrls.Select(uri => UtilityService.DownloadFileAsync(uri, Path.Combine(directoryOfImages, this.PermanentID + "-" + uri.GetFilename()), this.SourceUrl, onDownloadCompleted, onDownloadError, cancellationToken))).ConfigureAwait(false);
			}

			// normalize TOC
			this.NormalizeTOC();

			// assign identity
			if (string.IsNullOrWhiteSpace(this.ID) || !this.ID.IsValidUUID())
				this.ID = this.PermanentID;

			// done
			stopwatch.Stop();
			onCompleted?.Invoke(this, stopwatch.ElapsedMilliseconds);
			return this;
		}

		public async Task<List<string>> FetchChapterAsync(int chapterIndex, Action<int> onStart = null, Action<int, List<string>, long> onCompleted = null, Action<int, Exception> onError = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				// prepare
				var chapterUrl = chapterIndex < this.Chapters.Count ? this.Chapters[chapterIndex] : "";
				if (!chapterUrl.IsStartsWith("https://vnthuquan.net") && !chapterUrl.IsStartsWith("http://vnthuquan.net"))
					return null;

				// start
				onStart?.Invoke(chapterIndex);
				var stopwatch = Stopwatch.StartNew();

				// get the HTML of the chapter
				var contents = new List<string>();
				var url = chapterUrl.Substring(0, chapterUrl.IndexOf("?") + 1) + $"&rnd=92.{UtilityService.GetRandomNumber()}";
				var body = chapterUrl.Substring(chapterUrl.IndexOf("?") + 1);
				var html = await url.GetVnThuQuanHtmlAsync(url.IsContains("chuonghoi_moi.aspx") ? "POST" : "GET", this.SourceUrl, body, cancellationToken).ConfigureAwait(false);

				// parse the chapter
				using (cancellationToken.Register(() => { return; }))
				{
					var splitter = "--!!tach_noi_dung!!--";
					var start = html.PositionOf(splitter);
					while (start > 0)
					{
						contents.Add(html.Substring(0, start));
						html = html.Remove(0, start + splitter.Length);
						start = html.PositionOf(splitter);
					}
					contents.Add(html);

					var data = this.ParseChapter(contents);

					// no information
					if (data[0].Equals("") && data[1].Equals(""))
						this.Chapters[chapterIndex] = this.GetTOCItem(chapterIndex) + "--(empty)--";

					// got information
					else if (!data[0].Equals("") || !data[1].Equals(""))
					{
						// normalize title of the chapter
						var title = data[0];
						if (string.IsNullOrWhiteSpace(title) && this.TOCs != null && this.TOCs.Count > chapterIndex)
							title = this.GetTOCItem(chapterIndex);

						// normalize body of the chapter (image files)
						body = data[1].Equals("") ? "--(empty)--" : data[1].Trim();
						start = body.PositionOf("<img");
						var end = -1;
						while (start > -1)
						{
							start = body.PositionOf("src=", start + 1) + 5;
							end = body.PositionOf(body[start - 1].ToString(), start + 1);
							var image = body.Substring(start, end - start);
							if (!image.IsStartsWith(Definitions.MediaURI))
							{
								var filename = Path.GetFileName(image);
								image = (image.Left(image.Length - filename.Length) + filename).Replace(@"\", "/");
								if (!image.IsStartsWith("https://") && !image.IsStartsWith("http://"))
									image  = "https://vnthuquan.net" + image;
								if (this.MediaFileUrls.IndexOf(image) < 0)
									this.MediaFileUrls.Add(image);

								body = body.Remove(start, end - start);
								body = body.Insert(start, Definitions.MediaURI + image.GetFilename());
							}
							start = body.PositionOf("<img", start + 1);
						}

						// update
						this.Chapters[chapterIndex] = (!string.IsNullOrWhiteSpace(title) ? "<h1>" + title + "</h1>" : "") + body;
					}
				}

				// callback when done
				stopwatch.Stop();
				onCompleted?.Invoke(chapterIndex, contents, stopwatch.ElapsedMilliseconds);
				return contents;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				onError?.Invoke(chapterIndex, ex);
				if (onError == null)
					throw ex;
				return null;
			}
		}

		async Task<string> IBookParser.FetchChapterAsync(int chapterIndex, Action<int> onStart, Action<int, List<string>, long> onCompleted, Action<int, Exception> onError, CancellationToken cancellationToken)
		{
			await this.FetchChapterAsync(chapterIndex, onStart, onCompleted, onError, cancellationToken).ConfigureAwait(false);
			return chapterIndex > -1 && chapterIndex < this.Chapters.Count ? this.Chapters[chapterIndex] : null;
		}

		#region Parse a book
		void ParseBook(string html)
		{
			// title & meta (author & category)
			var start = html.PositionOf("<aside id=\"letrai\">");
			start = html.PositionOf("<h3>", start);
			var end = html.PositionOf("</h3>", start + 1);
			if (end < 0)
				end = html.PositionOf("<h3>", start + 1);

			if (start > 0 && end > 0)
			{
				var info = UtilityService.RemoveTag(html.Substring(start + 4, end - start - 4).Trim(), "span");
				var data = "";
				start = info.PositionOf("<br>");
				if (start > 0)
				{
					data = info.Substring(0, start);
					this.Category = string.IsNullOrWhiteSpace(data) ? this.Category : data.GetCategory();

					end = info.PositionOf("<br>", start + 1);
					data = info.Substring(start + 4, end - start - 4);
					this.Title = string.IsNullOrWhiteSpace(data) ? this.Title : data;
					data = info.Substring(end + 4).Trim().GetAuthor();
					this.Author = string.IsNullOrWhiteSpace(data) ? this.Author : data;

					var excludeds = "Đồng tác giả|Dịch giả|Người dịch|Dịch viện|Chuyển ngữ|Dịch ra|Anh dịch|Dịch thuật|Bản dịch|Hiệu đính|Biên Tập|Biên soạn|đánh máy bổ sung|Nguyên tác|Nguyên bản|Dịch theo|Dịch từ|Theo bản|Biên dịch|Tổng Hợp|Tủ Sách|Sách Xuất Bản Tại|Chủ biên|Chủ nhiệm".Split('|');
					foreach (var excluded in excludeds)
					{
						start = this.Title.PositionOf(excluded);
						if (start > -1)
						{
							end = this.Title.PositionOf("<br>", start, StringComparison.OrdinalIgnoreCase);
							if (end < 0)
								end = this.Title.Length - 4;
							this.Title = this.Title.Remove(start, end - start + 4).Trim();
						}

						start = this.Author.PositionOf(excluded);
						if (start > -1)
						{
							end = this.Author.PositionOf("<br>", start, StringComparison.OrdinalIgnoreCase);
							if (end < 0)
								end = this.Author.Length - 4;
							this.Author = this.Author.Remove(start, end - start + 4).Trim();
						}
					}
				}

				this.Title = UtilityService.RemoveTag(this.Title, "br").GetNormalized();
				if (this.Title.Equals(this.Title.ToUpper()))
					this.Title = this.Title.ToLower().GetNormalized();

				if (this.Author.PositionOf("<br>") > 0)
				{
					var datas = this.Author.Replace(StringComparison.OrdinalIgnoreCase, "<br>", "<br>").ToArray("<br>");
					for (var index = 0; index < datas.Length - 1; index++)
						this.Title += " " + datas[index];
					this.Author = datas.Last();
				}
			}

			// book ID
			var bookID = this.SourceUrl.GetIdentity();

			// chapters
			start = html.PositionOf("id=\"dd2");
			start = start < 0 ? -1 : html.PositionOf("<ul", start + 1, StringComparison.OrdinalIgnoreCase);
			start = start < 0 ? -1 : html.PositionOf(">", start + 1, StringComparison.OrdinalIgnoreCase);
			end = start < 0 ? -1 : html.PositionOf("</ul>", start + 1, StringComparison.OrdinalIgnoreCase);
			if (start > 0 && end > 0)
			{
				var info = html.Substring(start + 1, end - start - 1);
				start = info.PositionOf("<li");
				while (start > -1)
				{
					start = info.PositionOf("('", start + 1) + 2;
					end = info.PositionOf("')", start + 1);
					var url = "https://vnthuquan.net/truyen/chuonghoi_moi.aspx?" + info.Substring(start, end - start);

					start = info.PositionOf("<a", start + 1);
					start = info.PositionOf(">", start + 1) + 1;
					end = info.PositionOf("</a>", start + 1);
					var toc = info.Substring(start, end - start);

					this.Chapters.Add(url);
					this.TOCs.Add(toc.GetNormalized());

					start = info.PositionOf("</li>");
					info = info.Remove(0, start + 5).Trim();
					start = info.PositionOf("<li");
				}
			}
			else
			{
				var url = "https://vnthuquan.net/truyen/truyen.aspx?tid=" + bookID;
				start = html.PositionOf("id=\"tieude");
				start = start < 0 ? -1 : html.PositionOf("noidung1(", start);
				if (start > 0)
				{
					start = html.PositionOf("('", start + 1) + 2;
					end = html.PositionOf("')", start + 1);
					url = "https://vnthuquan.net/truyen/chuonghoi_moi.aspx?" + html.Substring(start, end - start);
				}
				this.Chapters.Add(url);
			}
		}
		#endregion

		#region Parse a chapter of the book
		List<string> ParseChapter(List<string> contents)
		{
			if (contents == null || contents.Count < 3)
				return null;

			var title = UtilityService.RemoveWhitespaces(contents[1].Trim()).Replace("\r", "").Replace("\n", "").Replace("\t", "");
			var start = title.PositionOf("<h4");
			start = title.PositionOf(">", start + 1);
			var end = title.PositionOf("</h4>", start + 1);
			if (start > 0 && end > 0)
			{
				title = title.Substring(start + 1, end - start - 1).Trim().Replace("♦", " ");
				"Đồng tác giả|Dịch giả|Người dịch|Dịch viện|Chuyển ngữ|Dịch ra|Anh dịch|Dịch thuật|Bản dịch|Hiệu đính|Biên Tập|Biên soạn|đánh máy bổ sung|Nguyên tác|Nguyên bản|Dịch theo|Dịch từ|Theo bản|Biên dịch|Tổng Hợp|Tủ Sách|Tuyển tập|Sách Xuất Bản Tại|Chủ biên|Chủ nhiệm".Split('|')
					.ForEach(excluded =>
					{
						start = title.IndexOf(excluded, StringComparison.OrdinalIgnoreCase);
						if (start > -1)
						{
							end = title.IndexOf("<br>", start, StringComparison.OrdinalIgnoreCase);
							if (end < 0)
								end = title.Length - 4;
							title = title.Remove(start, end - start + 4).Trim();
						}
					});

				while (title.IsStartsWith("<br>"))
					title = title.Substring(4).Trim();
				while (title.IsEndsWith("<br>"))
					title = title.Substring(0, title.Length - 4).Trim();

				title = title.Replace("<br>", ": ").Replace("<BR>", " - ").Trim();
			}

			start = title.PositionOf("<div class=\"hr");
			if (start > 0)
			{
				var tit = "";
				do
				{
					start = title.PositionOf("<span", start + 1);
					start = start < 0 ? -1 : title.PositionOf(">", start + 1) + 1;
					end = start < 0 ? -1 : title.PositionOf("</span>", start );
					var t = start > 0 && end > 0 ? title.Substring(start, end - start).Trim() : "";
					if (!t.Equals("") && !t.IsEquals(this.Title) && !t.IsEquals(this.Author))
						tit += (tit != "" ? "<br>" : "") + t;
				} while (start > 0 && end > 0);
				title = tit.Replace("<br>", ": ").Replace("<BR>", " - ").Trim();
			}

			title = UtilityService.ClearTag(title, "img").Trim();
			title = UtilityService.RemoveTag(title, "br").Trim();
			title = UtilityService.RemoveTag(title, "p").Trim();
			title = UtilityService.RemoveTag(title, "i").Trim();
			title = UtilityService.RemoveTag(title, "b").Trim();
			title = UtilityService.RemoveTag(title, "em").Trim();
			title = UtilityService.RemoveTag(title, "strong").Trim();

			while (title.IndexOf("  ") > 0)
				title = title.Replace("  ", " ");
			while (title.IndexOf("- -") > 0)
				title = title.Replace("- -", "-");
			while (title.IndexOf(": -") > 0)
				title = title.Replace(": -", ":");

			title = title.Trim().Replace("( ", "(").Replace(" )", ")").Replace("- (", "(").Replace(": :", ":").GetNormalized();

			while (title.StartsWith(")") || title.StartsWith("]"))
				title = title.Right(title.Length - 1).Trim();
			while (title.EndsWith("(") || title.EndsWith("["))
				title = title.Left(title.Length - 1).Trim();

			while (title.StartsWith(":"))
				title = title.Right(title.Length - 1).Trim();
			while (title.EndsWith(":"))
				title = title.Left(title.Length - 1).Trim();

			if (title.Equals(title.ToUpper()))
				title = title.ToLower().GetNormalized();

			var body = UtilityService.RemoveWhitespaces(contents[2].Trim()).Replace(StringComparison.OrdinalIgnoreCase, "\r", "").Replace(StringComparison.OrdinalIgnoreCase, "\n", "").Replace(StringComparison.OrdinalIgnoreCase, "\t", "");

			body = UtilityService.RemoveTagAttributes(body, "p");
			body = UtilityService.RemoveTagAttributes(body, "div");

			body = UtilityService.ClearTag(body, "script");
			body = UtilityService.ClearComments(body);
			body = UtilityService.RemoveMsOfficeTags(body);

			body = body.Replace(StringComparison.OrdinalIgnoreCase, "<div></div>", "</p><p>").Trim();
			if (body.IsStartsWith("<div") && !body.IsEndsWith("</div>"))
			{
				body = body.Remove(0, body.IndexOf(">") + 1);
				body = "<p>" + body + "</p>";
			}

			while (body.IsStartsWith("<div>"))
				body = body.Substring(5).Trim();
			while (body.IsEndsWith("</div>"))
				body = body.Substring(0, body.Length - 6).Trim();

			start = body.PositionOf("<?xml");
			while (start > -1)
			{
				end = body.PositionOf(">", start);
				body = body.Remove(start, end - start + 1);
				start = body.PositionOf("<?xml");
			}

			"strong|em|p|img".Split('|')
				.ForEach(tag => body = body.Replace(StringComparison.OrdinalIgnoreCase, "<" + tag, "<" + tag).Replace(StringComparison.OrdinalIgnoreCase, "</" + tag + ">", "</" + tag + ">"));

			body = body.Replace(StringComparison.OrdinalIgnoreCase, "<DIV class=\"truyen_text\"></DIV></STRONG>", "</STRONG>\n<p>");
			body = body.Replace(StringComparison.OrdinalIgnoreCase, "<DIV class=\"truyen_text\"></DIV></EM>", "</EM>\n<p>");

			var headingTags = "h1|h2|h3|h4|h5|h6".Split('|');
			headingTags.ForEach(tag =>
			{
				body = body.Replace(StringComparison.OrdinalIgnoreCase, "<" + tag + "><div class=\"truyen_text\"></div>", "<" + tag + "> ").Replace(StringComparison.OrdinalIgnoreCase, "<" + tag + "><div class=\"truyen_text\"> </div>", "<" + tag + ">");
				body = UtilityService.RemoveTagAttributes(body, tag);
			});

			body = body.Replace(StringComparison.OrdinalIgnoreCase, "<div class=\"truyen_text\"></div>", "</p><p>").Replace(StringComparison.OrdinalIgnoreCase, "<div class=\"truyen_text\"> </div>", "</p><p>");
			body = body.Replace(StringComparison.OrdinalIgnoreCase, "<div class=\"truyen_text\">", "<p>").Replace(StringComparison.OrdinalIgnoreCase, "<div", "<p").Replace(StringComparison.OrdinalIgnoreCase, "</div>", "</p>");
			body = body.Replace(StringComparison.OrdinalIgnoreCase, "</li></p>", "</li>").Replace(StringComparison.OrdinalIgnoreCase, "<p><li>", "<li>");
			body = body.Replace(StringComparison.OrdinalIgnoreCase, "<p></ul></p>", "</ul>").Replace(StringComparison.OrdinalIgnoreCase, "<p></ol></p>", "</ol>");

			body = body.Replace(StringComparison.OrdinalIgnoreCase, "<i class=\"calibre7\"", "<i").Replace(StringComparison.OrdinalIgnoreCase, "<img class=\"calibre1\"", "<img").Replace(StringComparison.OrdinalIgnoreCase, "<b class=\"calibre4\"", "<b");
			body = body.Replace(StringComparison.OrdinalIgnoreCase, "<p> <b>", "<p><b>").Replace(StringComparison.OrdinalIgnoreCase, ". </b>", ".</b> ").Replace(StringComparison.OrdinalIgnoreCase, ". </i>", ".</i> ");
			body = body.Replace(StringComparison.OrdinalIgnoreCase, "<p align=\"center\"> <", "<p align=\"center\"><").Replace(StringComparison.OrdinalIgnoreCase, "<p> <", "<p><").Replace(StringComparison.OrdinalIgnoreCase, "<p> ", "<p>");
			body = body.Replace(StringComparison.OrdinalIgnoreCase, "<p><p>", "<p>").Replace(StringComparison.OrdinalIgnoreCase, "</p></p>", "</p>").Replace(StringComparison.OrdinalIgnoreCase, ". </p> ", ".</p>");

			headingTags.ForEach(tag =>
			{
				body = body.Replace(StringComparison.OrdinalIgnoreCase, "<" + tag + "> <", "<" + tag + "><").Replace(StringComparison.OrdinalIgnoreCase, "> </" + tag + ">", "></" + tag + ">");
				body = body.Replace(StringComparison.OrdinalIgnoreCase, "<" + tag + "></" + tag + ">", "").Replace(StringComparison.OrdinalIgnoreCase, "<" + tag + "> </" + tag + ">", "");
				body = body.Replace(StringComparison.OrdinalIgnoreCase, "<" + tag + "></p>", "<" + tag + ">").Replace(StringComparison.OrdinalIgnoreCase, "<p></" + tag + ">", "</" + tag + ">");
				body = body.Replace(StringComparison.OrdinalIgnoreCase, "<" + tag + "><strong>", "<" + tag + ">").Replace(StringComparison.OrdinalIgnoreCase, "</strong></" + tag + ">", "</" + tag + ">");
				body = body.Replace(StringComparison.OrdinalIgnoreCase, "<" + tag + "><em>", "<" + tag + ">").Replace(StringComparison.OrdinalIgnoreCase, "</em></" + tag + ">", "</" + tag + ">");
				body = body.Replace(StringComparison.OrdinalIgnoreCase, "<p><" + tag + ">", "<" + tag + ">").Replace(StringComparison.OrdinalIgnoreCase, "</" + tag + "></p>", "</" + tag + ">").Replace(StringComparison.OrdinalIgnoreCase, "<" + tag + "></p>", "");
			});

			headingTags.ForEach(tag =>
			{
				body = body.Replace(StringComparison.OrdinalIgnoreCase, "<p><" + tag + ">", "<" + tag + ">").Replace(StringComparison.OrdinalIgnoreCase, "</" + tag + "></p>", "</" + tag + ">");
				start = body.PositionOf("<" + tag + ">");
				while (start > -1)
				{
					end = body.PositionOf("</" + tag + ">", start + 1);
					var heading = body.Substring(start + 4, end - start - 4);
					body = body.Remove(start, end - start + 5);

					var pos = heading.PositionOf("<");
					while (pos > -1)
					{
						end = heading.PositionOf(">", pos);
						if (end > 0)
							heading = heading.Remove(pos, end - pos + 1);
						pos = heading.PositionOf("<");
					}
					body = body.Insert(start, "<" + tag + ">" + heading + "</" + tag + ">");
					start = body.PositionOf("<" + tag + ">", start + 1);
				}
			});

			start = body.PositionOf("<p id=\"chuhoain\"");
			while (start > -1)
			{
				end = body.PositionOf("</span><p>", start);
				var img = body.PositionOf("<img", start);
				if (start > -1 && end > start && img > start)
				{
					var imgStart = body.PositionOf("src=\"", img) + 5;
					var imgEnd = -1;
					if (imgStart < 0)
					{
						imgStart = body.PositionOf("src='", img) + 5;
						imgEnd = body.PositionOf("'", imgStart);
					}
					else
						imgEnd = body.PositionOf("\"", imgStart);
					var imgChar = body.Substring(imgStart, imgEnd - imgStart);
					body = body.Remove(start, end - start + 10);
					body = body.Insert(start, "<p>" + this.GetImageCharacter(imgChar));
				}
				start = body.PositionOf("<p id=\"chuhoain\"", start + 1);
			}

			start = body.PositionOf("<img");
			while (start > -1)
			{
				end = body.PositionOf(">", start + 1);
				var img = body.PositionOf("src=\"https://vnthuquan.net/userfiles/images/chu%20cai/cotich", start);
				if (img < 0)
					img = body.PositionOf("src='https://vnthuquan.net/userfiles/images/chu%20cai/cotich", start);

				if (img > -1 && end > img)
				{
					end = body.PositionOf("\"", img + 5);
					if (end < 0)
						end = body.PositionOf("'", img + 5);
					var imgChar = body.Substring(img + 5, end - img + 5);
					end = body.PositionOf("<p>", start);
					if (end < 0)
						end = body.PositionOf(">", start) + 1;
					else
						end += 3;
					string str = body.Substring(start, end - start);
					body = body.Remove(start, end - start);
					body = body.Insert(start, this.GetImageCharacter(imgChar));
				}

				start = body.PositionOf("<img", start + 1);
			}

			if (body.Equals("</p><p>"))
				body = "";
			else
			{
				body = this.NormalizeChapterBody(body);
				body = body.Replace(StringComparison.OrdinalIgnoreCase, "<h1>", "<h2>").Replace(StringComparison.OrdinalIgnoreCase, "</h1>", "</h2>");
			}

			return new List<string> { title, body };
		}

		string GetImageCharacter(string imgChar)
		{
			var start = imgChar.PositionOf("_");
			if (start < 0)
				return "";
			var end = imgChar.PositionOf(".", start);
			if (end < 0)
				return "";

			var @char = imgChar.Substring(start + 1, end - start - 1).ToUpper();
			if (@char.Equals("DD"))
				@char = "Đ";
			else if (@char.Equals("AA"))
				@char = "Â";
			else if (@char.Equals("AW"))
				@char = "Ă";
			else if (@char.Equals("EE"))
				@char = "Ê";
			else if (@char.Equals("OW"))
				@char = "Ơ";
			else if (@char.Equals("OO"))
				@char = "Ô";
			return @char;
		}

		string GetValueOfTitle(List<string> contents, string[] indicators)
		{
			if (contents == null || contents.Count < 3)
				return "";

			var title = UtilityService.RemoveWhitespaces(contents[1].Trim()).Replace(StringComparison.OrdinalIgnoreCase, "\r", "").Replace(StringComparison.OrdinalIgnoreCase, "\n", "").Replace(StringComparison.OrdinalIgnoreCase, "\t", "");
			var start = title.PositionOf("<h4");
			start = title.PositionOf(">", start + 1);
			var end = title.PositionOf("</h4>", start + 1);
			if (start < 0 || end < 0)
				return "";

			title = title.Substring(start + 1, end - start - 1).Trim();

			foreach (string indicator in indicators)
			{
				start = title.PositionOf(indicator);
				if (start > -1)
				{
					end = title.PositionOf("<br", start);
					if (end < 0)
						end = title.Length;
					break;
				}
			}

			if (start < 0)
				return "";

			title = title.Substring(start, end - start).Trim();
			start = title.IndexOf(":");
			if (start > 0)
				title = title.Substring(start + 1).Trim();
			while (title.StartsWith(":"))
				title = title.Substring(1).Trim();

			return title.GetNormalized();
		}
		#endregion

		#region Normalize body of a chapter
		public string NormalizeChapterBody(string input)
		{
			var output = UtilityService.RemoveTag(input.Trim().Replace("\r", "").Replace("\n", "").Replace("\t", ""), "a");
			output = output.Replace(StringComparison.OrdinalIgnoreCase, "<em", "<i").Replace(StringComparison.OrdinalIgnoreCase, "</em>", "</i>");
			output = output.Replace(StringComparison.OrdinalIgnoreCase, "<I", "<i").Replace(StringComparison.OrdinalIgnoreCase, "</I>", "</i>");
			output = output.Replace(StringComparison.OrdinalIgnoreCase, "<strong", "<b").Replace(StringComparison.OrdinalIgnoreCase, "</strong>", "</b>");
			output = output.Replace(StringComparison.OrdinalIgnoreCase, "<B", "<b").Replace(StringComparison.OrdinalIgnoreCase, "</b>", "</b>");
			output = output.Replace(StringComparison.OrdinalIgnoreCase, "<U", "<u").Replace(StringComparison.OrdinalIgnoreCase, "</u>", "</u>");

			var formattingTags = "b|i|u".Split('|');
			output = UtilityService.RemoveMsOfficeTags(output);
			output = UtilityService.RemoveTagAttributes(output, "p");
			formattingTags.ForEach(tag => output = UtilityService.RemoveTagAttributes(output, tag));

			var replacements = new List<string[]>
			{
				new[] { "<p> ", "<p>" },
				new[] { "<p>-  ", "<p>- " },
				new[] { " </p>", "</p>" },
				new[] { "<p> ", "<p>" },
				new[] { "<p>-  ", "<p>- " },
				new[] { " </p>", "</p>" },
			};

			replacements.ForEach(replacement =>
			{
				int counter = 0;
				while (counter < 1000 && output.IndexOf(replacement[0]) > 0)
				{
					output = output.Replace(replacement[0], replacement[1]);
					counter++;
				}
			});

			var symbols = ".|,|!|?|;|:".Split('|');
			formattingTags.ForEach(tag =>
			{
				output = output.Replace("<" + tag + "><" + tag + ">", "<" + tag + ">").Replace("</" + tag + "></" + tag + ">", "</" + tag + ">");
				output = output.Replace("<" + tag + "></" + tag + ">", "").Replace("<" + tag + "> </" + tag + ">", "");
				symbols.ForEach(symbol =>
				{
					output = output.Replace("</" + tag + ">" + symbol + "</p>", symbol + "</" + tag + "></p>");
					output = output.Replace(symbol + "</" + tag + ">", symbol + "</" + tag + "> ");
					output = output.Replace("</" + tag + ">" + symbol, "</" + tag + ">" + symbol + " ");
				});
			});

			replacements.ForEach(replacement =>
			{
				var counter = 0;
				while (counter < 100 && output.IndexOf(replacement[0]) > 0)
				{
					output = output.Replace(replacement[0], replacement[1]);
					counter++;
				}
			});

			int start = -1, end = -1;
			if (!output.StartsWith("<p>"))
			{
				start = output.IndexOf("<p>", StringComparison.OrdinalIgnoreCase);
				end = output.IndexOf("</p>", StringComparison.OrdinalIgnoreCase);
				if (start > end)
					output = "<p>" + output;
			}

			output += !output.EndsWith("</p>") ? "</p>" : "";

			replacements = new List<string[]>();
			string[] beRemoved = "<p></p>|<p class='msg signature'></p>|<p><p align='center'></p>|<p align='left'></p>|<p style='text-align: left;'></p>|<p align='center'></p>|<p style='text-align: center;'></p>|<p align='right'></p>|<p style='text-align: right;'></p>|<p>.</p>|<h2>HẾT</h2>|<strong>HẾT</strong>".Split('|');
			foreach (string removed in beRemoved)
				replacements.Add(new string[] { removed, "" });
			foreach (string[] replacement in replacements)
			{
				output = output.Replace(StringComparison.OrdinalIgnoreCase, replacement[0], replacement[1]);
				if (replacement[0].IndexOf("'") > 0)
					output = output.Replace(StringComparison.OrdinalIgnoreCase, replacement[0].Replace("'", "\""), replacement[1]);
			}

			var headingTags = "h1|h2|h3|h4|h5|h6".Split('|');
			foreach (string tag in headingTags)
				output = output.Replace(StringComparison.OrdinalIgnoreCase, "<p><" + tag + ">", "<" + tag + ">").Replace(StringComparison.OrdinalIgnoreCase, "</" + tag + "></p>", "</" + tag + ">");

			output = this.ReformatParagraphs(output);
			output += !output.EndsWith("</p>") ? "</p>" : "";
			output = output.Replace("<p><p>", "<p>").Replace("</p></p>", "</p>").Replace("<p></p>", "");
			output = output.Replace(StringComparison.OrdinalIgnoreCase, "<p class=\"hr\"></p>", "<hr/>").Replace("<p><b><i></b></i></p>", "");

			return output.Trim();
		}

		public string ReformatParagraphs(string input)
		{
			var output = input.Trim();
			int start = -1, end = -1;
			"b|i|u".Split('|').ForEach(tag =>
			{
				start = output.IndexOf("<" + tag + ">");
				while (start > -1)
				{
					end = output.IndexOf("</" + tag + ">", start + 1);
					if (end > 0)
					{
						var paragraph = output.Substring(start, end - start + 3 + tag.Length);
						if (paragraph.IndexOf("<p>") > 0)
						{
							paragraph = paragraph.Replace("</p>", "</" + tag + "></p>").Replace("<p>", "<p><" + tag + ">");
							output = output.Remove(start, end - start + 3 + tag.Length);
							output = output.Insert(start, paragraph);
						}
					}
					else
					{
						end = output.IndexOf("</p>", start + 1);
						if (end > 0)
							output = output.Insert(end, "</" + tag + ">");
					}

					start = output.IndexOf("<" + tag + ">", start + 1);
				}
			});

			start = output.IndexOf("<p");
			while (start > -1)
			{
				end = output.IndexOf("</p>", start + 1);
				if (end > start)
				{
					var paragraph = output.Substring(start, end - start + 4);
					try
					{
						paragraph = this.ReformatParagraph(paragraph);
					}
					catch { }
					output = output.Remove(start, end - start + 4);
					output = output.Insert(start, paragraph);
				}

				start = start + 1 < output.Length ? output.IndexOf("<p", start + 1) : -1;
			}

			return output;
		}

		string ReformatParagraph(string input)
		{
			var output = UtilityService.RemoveTag(input, "span").Trim();
			if (output.Equals("") || output.Equals("<p></p>"))
				return "";

			var start = output.IndexOf(">") + 1;
			var end = output.IndexOf("</p>", start + 1);
			if (end < start)
				start = 3;
			output = output.Substring(start, end - start).Trim();

			"b|i|u".Split('|').ForEach(tag =>
			{
				if (output.IndexOf("<" + tag + ">") == 1)
				{
					var @char = output.Left(1);
					output = output.Right(output.Length - 1).Insert(tag.Length + 2, @char);
				}

				start = output.IndexOf("<" + tag + ">");
				if (start < 0)
				{
					if (output.IndexOf("</" + tag + ">") > 0)
						output = "<" + tag + ">" + output;
				}
				else
					while (start > -1)
					{
						var next = output.IndexOf("<" + tag + ">", start + 1);
						end = output.IndexOf("</" + tag + ">", start + 1);
						if (end < 0)
						{
							if (next < 0)
								output += "</" + tag + ">";
							else
								output = output.Insert(next, "</" + tag + ">");
						}
						else if (next > 0 && next < end)
							output = output.Insert(next, "</" + tag + ">");

						start = output.IndexOf("<" + tag + ">", start + 1);
					}
			});

			return "<p>" + output.Trim() + "</p>";
		}
		#endregion

		#region Get other meta information of the book (translator, original title, cover image, credits, TOC item, ...)
		string GetTranslator(List<string> contents)
		{
			return this.GetValueOfTitle(contents, "Dịch giả|Người dịch|Dịch viện|Chuyển ngữ|Dịch ra|Anh dịch|Dịch thuật|Biên dịch".Split('|')).ToLower().GetNormalized();
		}

		string GetOriginal(List<string> contents)
		{
			return this.GetValueOfTitle(contents, "Nguyên tác|Dịch theo|Dịch từ|Theo bản".Split('|')).ToLower().GetNormalized();
		}

		string GetCoverImage(List<string> contents)
		{
			if (contents == null || contents.Count < 2)
				return "";

			var data = contents[1];
			var start = data.PositionOf("<img");
			if (start < 0 && contents.Count > 2)
			{
				data = contents[2];
				start = data.PositionOf("<img");
			}

			if (start > 0)
			{
				start = data.PositionOf("src=\"", start + 1);
				start = data.PositionOf("\"", start + 1);
				var end = data.PositionOf("\"", start + 1);
				data = start > 0 && end > 0 ? data.Substring(start + 1, end - start - 1) : "";
			}
			else
				data = "";

			if (data.Equals(""))
			{
				data = contents[0];
				start = data.PositionOf("background:url(");
				if (start > 0)
				{
					start = data.PositionOf("(", start + 1);
					var end = data.PositionOf(")", start + 1);
					data = start > 0 && end > 0 ? data.Substring(start + 1, end - start - 1) : "";
				}
			}

			return data;
		}

		string GetCredits(List<string> contents)
		{
			if (contents == null || contents.Count < 4 || contents[3].IndexOf("<p") < 0)
				return "";

			var start = contents[3].PositionOf("<p");
			var end = contents[3].PositionOf("</p>");
			if (start < 0 || end < 0)
				return "";

			var space = "&nbsp;".HtmlDecode();
			var credits = UtilityService.RemoveTagAttributes(contents[3].Substring(start, end - start + 4).Trim(), "p");
			credits = UtilityService.RemoveWhitespaces(credits.Replace(StringComparison.OrdinalIgnoreCase, "<br>", " ").Replace("\r", "").Replace("\n", "").Replace("\t", ""));
			while (credits.IndexOf(space + space) > 0)
				credits = credits.Replace(space + space, " ");
			while (credits.IndexOf("  ") > 0)
				credits = credits.Replace("  ", " ");
			credits = credits.Replace(StringComparison.OrdinalIgnoreCase, "</p><p>", "</p>\n<p>").Trim();
			credits = credits.Replace("<p> ", "<p>").Replace(" :", ":");
			credits = credits.Replace("&", "&amp;").Replace("&amp;amp;", "&amp;");
			return credits;
		}

		string GetTOCItem(int chapterIndex)
		{
			var toc = this.TOCs != null && chapterIndex < this.TOCs.Count
				? this.TOCs[chapterIndex]
				: "";
			if (!string.IsNullOrWhiteSpace(toc))
			{
				toc = UtilityService.RemoveTag(toc, "a");
				toc = UtilityService.RemoveTag(toc, "p");
			}
			return string.IsNullOrWhiteSpace(toc)
				? (chapterIndex + 1).ToString()
				: toc.GetNormalized().Replace("{0}", (chapterIndex + 1).ToString());
		}
		#endregion

	}
}