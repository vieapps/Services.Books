﻿#region Related components
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
using net.vieapps.Components.Caching;
#endregion

namespace net.vieapps.Services.Books
{
	public class Crawler
	{

		#region Helpers
		public int ISachCounter { get; private set; }

		public int MaxPages { get; private set; }

		public string CorrelationID { get; set; }

		public Action<string, Exception, bool> UpdateLogs { get; set; }

		public List<string> Logs { get; private set; } = new List<string>();

		void AddLogs(string log, Exception ex = null, bool addLogs = true, bool updateCentralizedLogs = true)
		{
			this.UpdateLogs?.Invoke(log, ex, updateCentralizedLogs);
			if (addLogs)
			{
				this.Logs.Add(log);
				if (ex != null)
				{
					this.Logs.Add(ex.Message + " [" + ex.GetType() + "]");
					this.Logs.Add(ex.StackTrace);
				}
			}
		}
		#endregion

		internal void Start(Func<Book, CancellationToken, Task> onUpdate, Action<long> onCompleted, Action<Exception> onError, CancellationToken cancellationToken = default(CancellationToken))
		{
			this.MaxPages = UtilityService.GetAppSetting("BookCrawlerMaxPages", "1").CastAs<int>();
			this.Logs.Clear();
			this.AddLogs($"Total {this.MaxPages} page(s) of each site will be crawled");
			Task.Run(async () =>
			{
				await this.StartAsync(onUpdate, onCompleted, onError, cancellationToken).ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		async Task StartAsync(Func<Book, CancellationToken, Task> onUpdate, Action<long> onCompleted, Action<Exception> onError, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				var stopwatch = new Stopwatch();
				stopwatch.Start();

				await Task.WhenAll(
					this.RunCrawlerOfVnThuQuanAsync(onUpdate, cancellationToken),
					this.RunCrawlerOfISachAsync(onUpdate, cancellationToken)
				).ConfigureAwait(false);

				stopwatch.Stop();
				onCompleted?.Invoke(stopwatch.ElapsedMilliseconds);
			}
			catch (Exception ex)
			{
				if (ex is OperationCanceledException)
					this.UpdateLogs?.Invoke("......... Cancelled", ex, false);
				else
					this.UpdateLogs?.Invoke("Error occurred while crawling", ex, false);
				onError?.Invoke(ex);
			}
		}

		#region Crawl books of vnthuquan.net
		async Task RunCrawlerOfVnThuQuanAsync(Func<Book, CancellationToken, Task> onUpdate, CancellationToken cancellationToken = default(CancellationToken))
		{
			// prepare
			var folder = Path.Combine(Utility.FolderOfContributedFiles, "crawlers");
			var filePath = Path.Combine(folder, "vnthuquan.net.json");
			var bookParsers = new List<IBookParser>();
			if (File.Exists(filePath))
			{
				var jsonParsers = JArray.Parse(await UtilityService.ReadTextFileAsync(filePath).ConfigureAwait(false));
				foreach (JObject jsonParser in jsonParsers)
					bookParsers.Add(jsonParser.Copy<Parsers.Books.VnThuQuan>());
			}

			// crawl bookself
			else
			{
				var bookshelfParser = new Parsers.Bookshelfs.VnThuQuan().Initialize();
				while (bookshelfParser.CurrentPage <= this.MaxPages)
				{
					this.AddLogs($"Start to crawl the bookshelf of VnThuQuan.net - Page number: {bookshelfParser.CurrentPage}");
					await bookshelfParser.ParseAsync(
						(p, times) => this.AddLogs($"The page {p.CurrentPage} of VnThuQuan.net's bookshelf is completed - Execution times: {times.GetElapsedTimes()}"),
						(p, ex) => this.AddLogs($"Error occurred while crawling the bookshelf of VnThuQuan.net - Page number: {p.CurrentPage}", ex),
						cancellationToken
					).ConfigureAwait(false);
					bookParsers = bookParsers.Concat(bookshelfParser.BookParsers).ToList();
					bookshelfParser.Prepare();
				}
			}

			// crawl books
			this.AddLogs($"Start to crawl {bookParsers.Count} book(s) of VnThuQuan.net");
			var errorParsers = new List<IBookParser>();
			int index = 0, success = 0;
			while (index < bookParsers.Count)
			{
				var parser = bookParsers[index];
				if (!Utility.IsExisted(parser.Title, parser.Author))
					try
					{
						await this.CrawlAsync(parser, folder, onUpdate, UtilityService.GetAppSetting("BookCrawlVnThuQuanParalell", "true").CastAs<bool>(), cancellationToken).ConfigureAwait(false);
						success++;
					}
					catch (Exception ex)
					{
						this.AddLogs($"Error occurred while fetching book [{parser.Title} - {parser.SourceUrl}]", ex);
						errorParsers.Add(parser);
					}
				else
					this.AddLogs($"Bypass the existed book [{parser.Title}]");

				// next
				index++;
			}

			// cleanup
			if (errorParsers.Count > 0)
				await UtilityService.WriteTextFileAsync(filePath, errorParsers.ToJArray().ToString(Formatting.Indented)).ConfigureAwait(false);
			else if (File.Exists(filePath))
				File.Delete(filePath);

			// finalize
			this.AddLogs($"Total {success} book(s) of VnThuQuan.net had been crawled");
		}
		#endregion

		#region Crawl books of isach.info
		async Task RunCrawlerOfISachAsync(Func<Book, CancellationToken, Task> onUpdate, CancellationToken cancellationToken = default(CancellationToken))
		{
			// prepare
			try
			{
				await UtilityService.GetWebPageAsync("http://isach.info/robots.txt", null, UtilityService.SpiderUserAgent, cancellationToken).ConfigureAwait(false);
			}
			catch { }

			var folder = Path.Combine(Utility.FolderOfContributedFiles, "crawlers");
			var filePath = Path.Combine(folder, "isach.info.json");
			var bookParsers = new List<IBookParser>();
			if (File.Exists(filePath))
			{
				var jsonParsers = JArray.Parse(await UtilityService.ReadTextFileAsync(filePath).ConfigureAwait(false));
				foreach (JObject jsonParser in jsonParsers)
					bookParsers.Add(jsonParser.Copy<Parsers.Books.ISach>());
			}

			// crawl bookself
			else
			{
				var bookshelfParser = new Parsers.Bookshelfs.ISach().Initialize();
				while (bookshelfParser.UrlPattern != null)
				{
					// delay
					var delay = UtilityService.GetRandomNumber(1234, 2345);
					if (bookshelfParser.TotalPages > 10 && bookshelfParser.CurrentPage > 9 && bookshelfParser.CurrentPage % 10 == 0)
						delay = UtilityService.GetRandomNumber(4321, 5432);
					this.AddLogs($"... Wait for a few seconds [{delay}ms] to help iSach stay alive ...", null, false);
					await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

					// crawl
					this.AddLogs($"Start to crawl the bookshelf of ISach.info - Page number: {bookshelfParser.CurrentPage} ({bookshelfParser.Category + (bookshelfParser.Char != null ? " [" + bookshelfParser.Char + "]" : "")})");
					await bookshelfParser.ParseAsync(
						(p, times) => this.AddLogs($"The page {p.CurrentPage} of ISach.info ({p.Category + (p.Char != null ? " [" + p.Char + "]" : "")}) is completed - Execution times: {times.GetElapsedTimes()}"),
						(p, ex) => this.AddLogs($"Error occurred while crawling the page {p.CurrentPage} of ISach.info ({p.Category + (p.Char != null ? " [" + p.Char + "]" : "")})", ex),
						cancellationToken
					).ConfigureAwait(false);

					// update
					bookParsers = bookParsers.Concat(bookshelfParser.BookParsers).ToList();
					if (bookshelfParser.CurrentPage >= this.MaxPages)
						bookshelfParser.CurrentPage = bookshelfParser.TotalPages = 1;
					bookshelfParser.Prepare();
				}
			}

			// crawl books
			this.AddLogs($"Start crawl {bookParsers.Count} book(s) of ISach.info");
			this.ISachCounter = 0;
			var errorParsers = new List<IBookParser>();
			int index = 0, success = 0;
			while (index < bookParsers.Count)
			{
				var parser = bookParsers[index];
				if (!Utility.IsExisted(parser.Title, parser.Author))
					try
					{
						// delay
						var delay = this.ISachCounter > 4 && this.ISachCounter % 5 == 0 ? 3210 : 1500;
						if (this.ISachCounter > 12 && this.ISachCounter % 13 == 0)
							delay = UtilityService.GetRandomNumber(3456, 7000);
						this.AddLogs($"... Wait for a few seconds [{delay}ms] to help iSach stay alive ...", null, false);
						await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

						// crawl
						await this.CrawlAsync(parser, folder, onUpdate, UtilityService.GetAppSetting("BookCrawlISachParalell", "false").CastAs<bool>(), cancellationToken).ConfigureAwait(false);
						success++;
					}
					catch (Exception ex)
					{
						this.AddLogs($"Error occurred while fetching book [{parser.Title} - {parser.SourceUrl}]", ex);
						errorParsers.Add(parser);
					}
				else
					this.AddLogs($"Bypass the existed book [{parser.Title}]");

				// next
				this.ISachCounter++;
				index++;
			}

			// cleanup
			if (errorParsers.Count > 0)
				await UtilityService.WriteTextFileAsync(filePath, errorParsers.ToJArray().ToString(Formatting.Indented)).ConfigureAwait(false);
			else if (File.Exists(filePath))
				File.Delete(filePath);

			// finalize
			this.AddLogs($"Total {success} book(s) of ISach.info had been crawled");
		}
		#endregion

		#region Crawl a book
		public async Task<IBookParser> CrawlAsync(IBookParser parser, string folder = null, Func<Book, CancellationToken, Task> onUpdate = null, bool parallelExecutions = true, CancellationToken cancellationToken = default(CancellationToken), bool isRecrawl = false)
		{
			// prepare
			folder = folder ?? Utility.FolderOfTempFiles;

			// crawl
			await parser.FetchAsync(
				isRecrawl ? null : parser.SourceUrl,
				(p) => this.AddLogs($"Start to fetch the book [{p.SourceUrl}]"),
				(p, times) => this.AddLogs($"The book is parsed [{p.Title} - {p.SourceUrl}] (Execution times: {times.GetElapsedTimes()}) - Start to fetch {p.Chapters.Count} chapter(s)"),
				(p, times) => this.AddLogs($"The book is fetched [{p.Title}] - Execution times: {times.GetElapsedTimes()}"),
				(idx) => this.AddLogs($"Start to fetch the chapter [{(idx < parser.TOCs.Count && parser.Chapters[idx].IsStartsWith("http://") ? parser.TOCs[idx] + " - " + parser.Chapters[idx] : idx.ToString())}]"),
				(idx, contents, times) => this.AddLogs($"The chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}] is fetched - Execution times: {times.GetElapsedTimes()}"),
				(idx, ex) => this.AddLogs($"Error occurred while fetching the chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}]", ex),
				Path.Combine(folder, Definitions.MediaFolder),
				(p, uri) => this.AddLogs($"Start to download images [{uri}]"),
				(uri, path, times) => this.AddLogs($"Image is downloaded [{uri} - {path}] - Execution times: {times.GetElapsedTimes()}"),
				(uri, ex) => this.AddLogs($"Error occurred while downloading image file [{uri}]", ex),
				parallelExecutions,
				cancellationToken
			).ConfigureAwait(false);

			// write JSON file
			parser.TotalChapters = parser.Chapters.Count;
			await UtilityService.WriteTextFileAsync(Path.Combine(folder, UtilityService.GetNormalizedFilename(parser.Title + " - " + parser.Author) + ".json"), parser.ToJson().ToString(Formatting.Indented)).ConfigureAwait(false);

			// update & return
			await this.UpdateAsync(parser.Title, parser.Author, folder, onUpdate, cancellationToken).ConfigureAwait(false);
			return parser;
		}
		#endregion

		#region Update book with new data (JSON, images, ...)
		public async Task<Book> UpdateAsync(string title, string author, string folder, Func<Book, CancellationToken, Task> onCompleted = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			// check
			folder = folder ?? Utility.FolderOfTempFiles;
			var filename = UtilityService.GetNormalizedFilename(title + " - " + author) + ".json";
			if (!File.Exists(Path.Combine(folder, filename)))
				return null;

			// update database
			var json = JObject.Parse(await UtilityService.ReadTextFileAsync(Path.Combine(folder, filename)).ConfigureAwait(false));
			var id = json["ID"] != null
				? (json["ID"] as JValue).Value as string
				: null;
			var book = string.IsNullOrWhiteSpace(id) || !id.IsValidUUID()
				? await Book.GetAsync(title, author, cancellationToken).ConfigureAwait(false)
				: await Book.GetAsync<Book>(id, cancellationToken).ConfigureAwait(false);

			if (book != null)
			{
				book.CopyFrom(json, "ID".ToHashSet());
				book.LastUpdated = DateTime.Now;
				await Book.UpdateAsync(book, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				book = json.Copy<Book>();
				book.LastUpdated = DateTime.Now;
				await Book.CreateAsync(book, cancellationToken).ConfigureAwait(false);
			}

			// update files
			var path = book.GetFolderPath();
			File.Copy(Path.Combine(folder, filename), Path.Combine(path, filename), true);
			File.Delete(Path.Combine(folder, filename));
			UtilityService.GetFiles(Path.Combine(folder, Definitions.MediaFolder), book.PermanentID + "-*.*")
				.ForEach(file =>
				{
					File.Copy(file.FullName, Path.Combine(path, Definitions.MediaFolder, file.Name), true);
					File.Delete(file.FullName);
				});

			// send the updating messages
			if (onCompleted != null)
				try
				{
					await onCompleted(book, cancellationToken).ConfigureAwait(false);
				}
				catch { }

			// return the updated book
			return book;
		}
		#endregion

	}
}