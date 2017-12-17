#region Related components
using System;
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
	public class ISach : IBookParser
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
		public string Source { get; set; } = "isach.info";
		public string SourceUrl { get; set; } = "";
		public string Credits { get; set; } = "";
		[JsonIgnore]
		public string ReferUrl { get; set; } = "http://isach.info/mobile/index.php";
		public List<string> TOCs { get; set; } = new List<string>();
		public List<string> Chapters { get; set; } = new List<string>();
		[JsonIgnore]
		public List<string> MediaFileUrls { get; set; } = new List<string>();
		#endregion

		public async Task<IBookParser> ParseAsync(string url = null, Action<IBookParser, long> onCompleted = null, Action<IBookParser, Exception> onError = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				// prepare
				var stopwatch = new Stopwatch();
				stopwatch.Start();

				this.ID = (url ?? this.SourceUrl).GetIdentity();
				this.SourceUrl = "http://isach.info/mobile/story.php?story=" + this.ID;

				// get HTML of the book
				var html = await UtilityService.GetWebPageAsync(this.SourceUrl, this.ReferUrl, UtilityService.SpiderUserAgent, cancellationToken).ConfigureAwait(false);

				// check permission
				if (html.PositionOf("Để đọc tác phẩm này, được yêu cầu phải đăng nhập") > 0)
					throw new InformationNotFoundException("Access Denied => Phải đăng nhập");

				// parse the book to get details
				await this.ParseBookAsync(html, cancellationToken);

				// permanent identity
				if (string.IsNullOrWhiteSpace(this.PermanentID))
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
			string folderOfImages = null, Action<IBookParser, string> onStartDownload = null, Action<string, string, long> onDownloadCompleted = null, Action<string, Exception> onDownloadError = null,
			bool parallelExecutions = false, CancellationToken cancellationToken = default(CancellationToken))
		{
			// prepare
			var stopwatch = new Stopwatch();
			stopwatch.Start();

			// parse the book
			onStart?.Invoke(this);
			await this.ParseAsync(url ?? this.SourceUrl, onParsed, null, cancellationToken).ConfigureAwait(false);

			// cover image
			if (!string.IsNullOrWhiteSpace(this.Cover) || !this.Cover.IsStartsWith(Definitions.MediaUri))
			{
				this.MediaFileUrls.Add(this.Cover);
				this.Cover = Definitions.MediaUri + this.Cover.GetFilename();
			}

			// fetch chapters
			Func<Task> fastCrawl = async () =>
			{
				var chaptersOfBigBook = 39;
				var normalDelayMin = 456;
				var normalDelayMax = 1234;
				var mediumDelayMin = 2345;
				var mediumDelayMax = 4321;
				var longDelayMin = 3456;
				var longDelayMax = 5678;

				var step = 7;
				var start = 0;
				var end = start + step;

				var isCompleted = false;
				while (!isCompleted)
				{
					var fetchingTasks = new List<Task>();
					for (var index = start; index < end; index++)
					{
						if (index >= this.Chapters.Count)
						{
							isCompleted = true;
							break;
						}

						var chapterUrl = this.Chapters[index];
						if (chapterUrl.Equals("") || !chapterUrl.StartsWith("http://isach.info"))
							continue;

						fetchingTasks.Add(Task.Run(async () =>
						{
							var delay = this.Chapters.Count > chaptersOfBigBook
								? UtilityService.GetRandomNumber(mediumDelayMin, mediumDelayMax)
								: UtilityService.GetRandomNumber(normalDelayMin, normalDelayMax);
							await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
							await this.FetchChapterAsync(index, onStartFetchChapter, onFetchChapterCompleted, onFetchChapterError, cancellationToken);
						}, cancellationToken));
					}
					await Task.WhenAll(fetchingTasks).ConfigureAwait(false);

					// go next
					if (!isCompleted)
					{
						start += step;
						end += step;
						if (end <= this.Chapters.Count)
							await Task.Delay(UtilityService.GetRandomNumber(longDelayMin, longDelayMax), cancellationToken).ConfigureAwait(false);
					}
				}
			};

			Func<Task> slowCrawl = async () =>
			{
				var chaptersOfLargeBook = 69;
				var mediumPausePointOfLargeBook = 6; 
				var longPausePointOfLargeBook = 29;
				var chaptersOfBigBook = 29; 
				var mediumPausePointOfBigBook = 3; 
				var longPausePointOfBigBook = 14;
				var normalDelayMin = 456; 
				var normalDelayMax = 890; 
				var mediumDelay = 4321; 
				var longDelayOfBigBook = 7890; 
				var longDelayOfLargeBook = 15431;

				var chapterCounter = 0; 
				var totalChapters = 0;
				for (var index = 0; index < this.Chapters.Count; index++)
					if (!this.Chapters[index].Equals("") && this.Chapters[index].StartsWith("http://isach.info"))
						totalChapters++;

				var chapterIndex = -1;
				while (chapterIndex < this.Chapters.Count)
				{
					chapterIndex++;
					var chapterUrl = chapterIndex < this.Chapters.Count ? this.Chapters[chapterIndex] : "";
					if (chapterUrl.Equals("") || !chapterUrl.StartsWith("http://isach.info"))
						continue;

					var number = totalChapters > chaptersOfBigBook ? mediumPausePointOfLargeBook : mediumPausePointOfBigBook;
					var delay = chapterCounter > (number - 1) && chapterCounter % number == 0 ? mediumDelay : UtilityService.GetRandomNumber(normalDelayMin, normalDelayMax);
					if (totalChapters > chaptersOfLargeBook)
					{
						if (chapterCounter > longPausePointOfLargeBook && chapterCounter % (longPausePointOfLargeBook + 1) == 0)
							delay = longDelayOfLargeBook;
					}
					else if (totalChapters > chaptersOfBigBook)
					{
						if (chapterCounter > longPausePointOfBigBook && chapterCounter % (longPausePointOfBigBook + 1) == 0)
							delay = longDelayOfBigBook;
					}
					await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
					await this.FetchChapterAsync(chapterIndex, onStartFetchChapter, onFetchChapterCompleted, onFetchChapterError, cancellationToken);

					chapterCounter++;
				}
			};

			if (this.Chapters.Count > 1)
			{
				if (parallelExecutions)
					await fastCrawl().ConfigureAwait(false);
				else
					await slowCrawl().ConfigureAwait(false);
			}

			// download image files
			if (this.MediaFileUrls.Count > 0)
			{
				folderOfImages = folderOfImages ?? "temp";
				onStartDownload?.Invoke(this, folderOfImages);
				await Task.WhenAll(this.MediaFileUrls.Select(uri => UtilityService.DownloadFileAsync(uri, folderOfImages + @"\" + this.PermanentID + "-" + uri.GetFilename(), this.SourceUrl, onDownloadCompleted, onDownloadError, cancellationToken))).ConfigureAwait(false);
			}

			// assign identity
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
				if (chapterUrl.Equals("") || !chapterUrl.IsStartsWith("http://isach.info"))
				{
					onCompleted?.Invoke(chapterIndex, null, 0);
					return null;
				}

				// start
				onStart?.Invoke(chapterIndex);
				var stopwatch = new Stopwatch();
				stopwatch.Start();

				// get the HTML of the chapter
				var html = await UtilityService.GetWebPageAsync(chapterUrl, this.SourceUrl, UtilityService.SpiderUserAgent, cancellationToken).ConfigureAwait(false);

				// parse the chapter
				List<string> contents;
				using (cancellationToken.Register(() => throw new OperationCanceledException(cancellationToken)))
				{
					contents = this.ParseChapter(html);
					var title = contents[0];
					if (string.IsNullOrWhiteSpace(title) && this.TOCs != null && this.TOCs.Count > chapterIndex)
						title = this.GetTOCItem(chapterIndex);
					var body = contents[1].Equals("") ? "--(empty)--" : contents[1].Trim();
					this.Chapters[chapterIndex] = (!string.IsNullOrWhiteSpace(title) ? "<h1>" + title + "</h1>" : "") + body;
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

		async Task<string> IBookParser.FetchChapterAsync(int chapterIndex, Action<int> onStart = null, Action<int, List<string>, long> onCompleted = null, Action<int, Exception> onError = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			await this.FetchChapterAsync(chapterIndex, onStart, onCompleted, onError, cancellationToken).ConfigureAwait(false);
			return chapterIndex > -1 && chapterIndex < this.Chapters.Count ? this.Chapters[chapterIndex] : null;
		}

		#region Parse a book
		async Task ParseBookAsync(string html, CancellationToken cancellationToken = default(CancellationToken))
		{
			// meta
			int start = -1, end = -1;
			using (cancellationToken.Register(() => throw new OperationCanceledException(cancellationToken)))
			{
				// title
				start = html.PositionOf("ms_title");
				start = start < 0 ? -1 : html.PositionOf("<a", start + 1);
				start = start < 0 ? -1 : html.PositionOf(">", start + 1);
				end = start < 0 ? -1 : html.PositionOf("<", start + 1);
				if (start > 0 && end > 0)
					this.Title = html.Substring(start + 1, end - start - 1).GetNormalized();

				// author
				start = html.PositionOf("Tác giả:");
				start = start < 0 ? -1 : html.PositionOf("<a", start + 1);
				start = start < 0 ? -1 : html.PositionOf(">", start + 1);
				end = start < 0 ? -1 : html.PositionOf("<", start + 1);
				if (start > 0 && end > 0)
					this.Author = html.Substring(start + 1, end - start - 1).GetAuthor();

				// category
				start = html.PositionOf("Thể loại:");
				start = start < 0 ? -1 : html.PositionOf("<a", start + 1);
				start = start < 0 ? -1 : html.PositionOf(">", start + 1);
				end = start < 0 ? -1 : html.PositionOf("<", start + 1);
				if (start > 0 && end > 0)
					this.Category = html.Substring(start + 1, end - start - 1).GetCategory();

				// original
				start = html.PositionOf("Nguyên tác:");
				end = start < 0 ? -1 : html.PositionOf("<", start + 1);
				if (start > 0 && end > 0)
					this.Original = html.Substring(start + 11, end - start - 11).Trim().GetNormalized();

				// translator
				start = html.PositionOf("Dịch giả:");
				start = start < 0 ? -1 : html.PositionOf("<a", start + 1);
				start = start < 0 ? -1 : html.PositionOf(">", start + 1);
				end = start < 0 ? -1 : html.PositionOf("<", start + 1);
				if (start > 0 && end > 0)
					this.Translator = html.Substring(start + 1, end - start - 1).Trim().GetNormalized();

				// cover image
				start = html.PositionOf("ms_image");
				start = start < 0 ? -1 : html.PositionOf("src='", start + 1);
				end = start < 0 ? -1 : html.PositionOf("'", start + 5);
				if (start > 0 && end > 0)
					this.Cover = "http://isach.info" + html.Substring(start + 5, end - start - 5).Trim();
			}

			// get HTML of chapters
			if (!string.IsNullOrWhiteSpace(this.Cover))
			{
				start = html.PositionOf("<a href='" + this.SourceUrl.Replace("http://isach.info", ""));
				end = start < 0 ? -1 : html.PositionOf("'", start + 9);
				if (start > -1 && end > -1)
				{
					var tocUrl = "http://isach.info" + html.Substring(start + 9, end - start - 9).Trim();
					await Task.Delay(UtilityService.GetRandomNumber(123, 432)).ConfigureAwait(false);
					html = await UtilityService.GetWebPageAsync(tocUrl, this.SourceUrl, UtilityService.SpiderUserAgent, cancellationToken).ConfigureAwait(false);
				}
			}

			// parse chapters
			using (cancellationToken.Register(() => throw new OperationCanceledException(cancellationToken)))
			{
				start = html.PositionOf("ms_chapter");
				if (start < 0)
					start = html.PositionOf("<div id='c0000");
				start = start < 0 ? -1 : html.PositionOf("<div", start + 1);
				end = start < 0 ? -1 : html.PositionOf("</form>", start + 1);

				if (start < 0 || end < 0)
				{
					var contents = this.ParseChapter(html);
					this.Chapters.Add((!string.IsNullOrWhiteSpace(contents[0]) ? "<h1>" + contents[0] + "</h1>" + "\n" : "") + contents[1]);
				}
				else
				{
					html = html.Substring(start, end - start).Trim();
					start = html.PositionOf("<a href='");
					while (start > -1)
					{
						end = html.PositionOf("'", start + 9);
						var chapterUrl = html.Substring(start + 9, end - start - 9).Trim();
						while (chapterUrl.StartsWith("/"))
							chapterUrl = chapterUrl.Right(chapterUrl.Length - 1);
						chapterUrl = (!chapterUrl.StartsWith("http://isach.info") ? "http://isach.info/mobile/" : "") + chapterUrl;
						if (chapterUrl.PositionOf("&chapter=") < 0)
							chapterUrl += "&chapter=0001";

						this.Chapters.Add(chapterUrl);

						start = html.PositionOf(">", start + 1) + 1;
						end = html.PositionOf("<", start + 1);
						this.TOCs.Add(html.Substring(start, end - start).GetNormalized());

						start = html.PositionOf("<a href='", start + 1);
					}
				}

				// special - only one chapter
				if (this.Chapters.Count < 1 || this.Chapters[0].Equals(""))
				{
					var contents = this.ParseChapter(html);
					this.Chapters.Add((!string.IsNullOrWhiteSpace(contents[0]) ? "<h1>" + contents[0] + "</h1>" + "\n" : "") + contents[1]);
				}
			}
		}
		#endregion

		#region Parse a chapter of the book
		List<string> ParseChapter(string html)
		{
			var start = html.PositionOf("<div class='chapter_navigator'>");
			if (start < 0)
				start = html.PositionOf("<div class='mobile_chapter_navigator'>") > 0
					? html.PositionOf("<div class='mobile_chapter_navigator'>")
					: html.PositionOf("<div id='story_detail'");
			start = html.PositionOf("ms_chapter", start + 1);
			start = start < 0 ? -1 : html.PositionOf(">", start + 1);
			var end = start < 0 ? -1 : html.PositionOf("</div>", start + 1);

			var title = (start > -1 && end > -1 ? html.Substring(start + 1, end - start - 1).Trim() : "").GetNormalized();
			while (title.PositionOf("  ") > -1)
				title = title.Replace("  ", " ");

			if (!title.Equals(""))
			{
				start = html.PositionOf("<div", start + 1);
				if (title.PositionOf("<div id='dropcap") > -1 || title.PositionOf("<div id ='dropcap") > -1)
					title = "";
				else if (title.ToLower().Equals("null"))
					title = "";
			}
			else
			{
				start = html.PositionOf("<span class='dropcap", start + 1);
				if (start < 0)
				{
					if (html.StartsWith("<div class='ms_text"))
						start = 0;
					else
					{
						start = html.PositionOf("ms_chapter", start + 1) > 0
							? html.PositionOf("ms_chapter", start + 1)
							: html.PositionOf("<div style='height: 50px;'></div>", end + 1) < html.PositionOf("<div class='ms_text'>", end + 1)
								? html.PositionOf("<div style='height: 50px;'></div>", end + 1)
								: -1;
						start = start < 0 ? html.PositionOf("<div class='ms_text'>", end + 1) : html.PositionOf("</div>", start + 1) + 6;
					}
				}
			}

			end = html.PositionOf("<div style='height: 50px;'></div>", start + 1);
			if (end < 0)
			{
				end = html.PositionOf("<div class='navigator_bottom'>", start + 1);
				if (end < 0)
					end = html.PositionOf("<div class='mobile_chapter_navigator'>", start + 1);
				if (end < 0)
					end = html.PositionOf("</form>", start + 1);
			}

			var body = start > -1 && end > -1 ? html.Substring(start, end - start).Trim() : "";
			body = body.Replace(StringComparison.OrdinalIgnoreCase, "<div class='ms_text'>", "<p>").Replace(StringComparison.OrdinalIgnoreCase, "<div", "<p").Replace(StringComparison.OrdinalIgnoreCase, "</div>", "</p>");

			if (body.StartsWith("<span class='dropcap"))
				body = "<p>" + body;

			start = body.PositionOf("<p");
			end = body.PositionOf("</p>", start + 1);
			while (start > -1 && end > -1)
			{
				var dropcap = body.PositionOf("'dropcap", start + 1);
				if (dropcap > -1 && dropcap < end)
				{
					string paragraph = body.Substring(start, end - start + 4);
					body = body.Remove(start, end - start + 4);

					string dropcapChar = "";
					dropcap = paragraph.PositionOf("class=");
					if (dropcap > 0)
					{
						dropcap += 7;
						dropcapChar = paragraph.Substring(dropcap - 1, 1);
						end = paragraph.PositionOf(dropcapChar, dropcap + 1);
						dropcapChar = paragraph.Substring(dropcap, end - dropcap);
						dropcapChar = dropcapChar[dropcapChar.Length - 1].ToString();
					}
					paragraph = UtilityService.RemoveTag(UtilityService.RemoveTag(paragraph, "p"), "span").Trim();
					if (paragraph.Equals(""))
						paragraph = dropcapChar;
					body = body.Insert(start, (body.StartsWith("<p>") ? "" : "<p>") + paragraph);
				}

				start = body.PositionOf("<p", start + 1);
				end = body.PositionOf("</p>", start + 1);
			}

			body = this.NormalizeBody(body.Replace(" \n", "").Replace("\r", "").Replace("\n", ""));
			body = body.Equals("") ? "--(empty)--" : body.Trim();

			if (title.Equals("") && (body.StartsWith("<p>Quyển ") || body.StartsWith("<p>Phần ") || body.StartsWith("<p>Chương ")))
			{
				start = 0;
				end = body.PositionOf("</p>") + 4;
				title = UtilityService.RemoveTag(body.Substring(0, end - start), "p").Trim();
				body = body.Remove(0, end - start);
			}

			start = body.PositionOf("<img");
			while (start > -1)
			{
				start = body.PositionOf("src=", start + 1) + 5;
				end = body.PositionOf(body[start - 1].ToString(), start + 1);
				var image = body.Substring(start, end - start);
				if (!image.IsStartsWith(Definitions.MediaUri))
				{
					var info = UtilityService.GetFileParts(image, false);
					image = (info.Item1 + "/" + info.Item2).Replace(@"\", "/");
					if (!image.IsStartsWith("http://"))
						image = "http://isach.info" + image;
					if (this.MediaFileUrls.IndexOf(image) < 0)
						this.MediaFileUrls.Add(image);

					body = body.Remove(start, end - start);
					body = body.Insert(start, Definitions.MediaUri + image.GetFilename());
				}
				start = body.PositionOf("<img", start + 1);
			}

			return new List<string>() { title, body };
		}
		#endregion

		#region Normalize body of a chapter
		string NormalizeBody(string input, int chapters = -1)
		{
			var output = UtilityService.RemoveTag(input.Trim().Replace("�", "").Replace("''", "\"").Replace("\r", "").Replace("\n", "").Replace("\t", ""), "a");

			new List<string[]>()
			{
				new string[] { "<p> ", "<p>" },
				new string[] { "<p>-  ", "<p>- " },
				new string[] { " </p>", "</p>" },
				new string[] { "<p> ", "<p>" },
				new string[] { "<p>-  ", "<p>- " },
				new string[] { " </p>", "</p>" },
			}.ForEach(replacement =>
			{
				var counter = 0;
				while (counter < 1000 && output.PositionOf(replacement[0]) > 0)
				{
					output = output.Replace(replacement[0], replacement[1]);
					counter++;
				}
			});

			new List<string[]>()
			{
				new string[] { "<p class='ms_focus'>", "<p>" },
				new string[] { "<p class='ms_note'>", "<p>" },
				new string[] { "<p class='ms_end_note'>", "<p style='font-style:italic'>" },
				new string[] { "<p class='story_author'>", "<p style='font-style:italic'>" },
				new string[] { "<p class='story_poem'>", "<p style='font-style:italic;margin-left:10px'>" },
				new string[] { "<p class='ms_break'>o O o</p>", "<hr/>" },
				new string[] { "<p class='poem_paragraph_break'></p>", "" },
				new string[] { "<p>o0o</p>", "" },
				new string[] { "<p>o0o", "<p>" },
				new string[] { "<p class='ms_text_b'>", "<p style='font-weight:bold'>" },
				new string[] { "<p class='ms_quote'>", "<p style='margin-left:20px'>" },
				new string[] { "<p class='ms_image'>", "<p style='text-align:center'>" },
				new string[] { "<p><p>", "<p>" },
				new string[] { "</p></p>", "</p>" },
				new string[] { "<p></p>", "" },
			}.ForEach(replacement =>
			{
				output = output.Replace(StringComparison.OrdinalIgnoreCase, replacement[0], replacement[1]);
				if (replacement[0].PositionOf("'") > 0)
					output = output.Replace(StringComparison.OrdinalIgnoreCase, replacement[0].Replace("'", "\""), replacement[1]);
			});

			var start = output.PositionOf("<h1>");
			var end = output.PositionOf("</h1>", start + 1);
			if (start > -1 && end > start)
			{
				var dropcap = output.PositionOf("='dropcap", start + 1);
				if (dropcap > start && end > dropcap)
					output = output.Remove(start, end - start + 5).Trim();
				else if (chapters.Equals(1))
					output = output.Replace(StringComparison.OrdinalIgnoreCase, "<h1>", "<p>").Replace(StringComparison.OrdinalIgnoreCase, "</h1>", "</p>");
			}

			return output.Trim();
		}
		#endregion

		#region Get TOC item
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