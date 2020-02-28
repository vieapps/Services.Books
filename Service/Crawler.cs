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
using net.vieapps.Components.Caching;
using net.vieapps.Components.Security;
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
					if (ex is RemoteServerErrorException)
					{
						this.Logs.Add($"- URI: {(ex as RemoteServerErrorException).ResponseUri}");
						this.Logs.Add($"- Body: {(ex as RemoteServerErrorException).ResponseBody}");
					}
				}
			}
		}
		#endregion

		internal void Start(Func<Book, CancellationToken, Task> onUpdate, Action<long> onCompleted, Action<Exception> onError, CancellationToken cancellationToken = default)
		{
			this.MaxPages = UtilityService.GetAppSetting("Books:Crawler-MaxPages", "1").CastAs<int>();
			this.Logs.Clear();
			this.AddLogs($"Total {this.MaxPages} page(s) of each site will be crawled");
			Task.Run(() => this.StartAsync(onUpdate, onCompleted, onError, cancellationToken)).ConfigureAwait(false);
		}

		async Task StartAsync(Func<Book, CancellationToken, Task> onUpdate, Action<long> onCompleted, Action<Exception> onError, CancellationToken cancellationToken = default)
		{
			try
			{
				var stopwatch = Stopwatch.StartNew();
				await Task.WhenAll(
					"true".IsEquals(UtilityService.GetAppSetting("Books:Crawler-VnThuQuan", "true")) ? this.RunVnThuQuanCrawlerAsync(onUpdate, cancellationToken) : Task.CompletedTask,
					"true".IsEquals(UtilityService.GetAppSetting("Books:Crawler-ISach", "true")) ? this.RunISachCrawlerAsync(onUpdate, cancellationToken) : Task.CompletedTask
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
		async Task RunVnThuQuanCrawlerAsync(Func<Book, CancellationToken, Task> onUpdate, CancellationToken cancellationToken = default)
		{
			// prepare
			var directory = Path.Combine(Utility.DirectoryOfContributedFiles, "crawlers");
			var filePath = Path.Combine(directory, "vnthuquan.net.json");
			var bookParsers = new List<IBookParser>();
			if (File.Exists(filePath))
			{
				var jsonParsers = JArray.Parse(await UtilityService.ReadTextFileAsync(filePath, null, cancellationToken).ConfigureAwait(false));
				foreach (JObject jsonParser in jsonParsers)
					bookParsers.Add(jsonParser.Copy<Parsers.Books.VnThuQuan>());
			}

			// crawl bookself
			else
			{
				var bookshelfParser = new Parsers.Bookshelfs.VnThuQuan().Initialize();
				while (bookshelfParser.CurrentPage <= this.MaxPages)
				{
					this.AddLogs($"Start to crawl the bookshelf of VnThuQuan.net [{bookshelfParser.UrlPattern?.Replace("{0}", bookshelfParser.CurrentPage.ToString())}]");
					await bookshelfParser.ParseAsync(
						(parser, times) => this.AddLogs($"The page {parser.CurrentPage} of VnThuQuan.net's bookshelf is completed - Execution times: {times.GetElapsedTimes()}"),
						(parser, ex) => this.AddLogs($"Error occurred while crawling the bookshelf of VnThuQuan.net - Page number: {parser.CurrentPage}", ex),
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
				if (!await parser.ExistsAsync(cancellationToken).ConfigureAwait(false))
					try
					{
						await this.CrawlAsync(parser, directory, onUpdate, UtilityService.GetAppSetting("Books:Crawler-VnThuQuanParalell", "true").CastAs<bool>(), cancellationToken).ConfigureAwait(false);
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
		async Task RunISachCrawlerAsync(Func<Book, CancellationToken, Task> onUpdate, CancellationToken cancellationToken = default)
		{
			// prepare
			try
			{
				await UtilityService.GetWebPageAsync("https://isach.info/robots.txt", null, UtilityService.SpiderUserAgent, cancellationToken).ConfigureAwait(false);
			}
			catch { }

			var folder = Path.Combine(Utility.DirectoryOfContributedFiles, "crawlers");
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
					this.AddLogs($"Start to crawl the bookshelf of ISach.info [{bookshelfParser.UrlPattern?.Replace("{0}", bookshelfParser.CurrentPage.ToString())}]");
					await bookshelfParser.ParseAsync(
						(p, times) => this.AddLogs($"The page {p.CurrentPage} of ISach.info is completed - Execution times: {times.GetElapsedTimes()}"),
						(p, ex) => this.AddLogs($"Error occurred while crawling the page {p.CurrentPage} of ISach.info", ex),
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
				if (!await parser.ExistsAsync(cancellationToken).ConfigureAwait(false))
					try
					{
						// delay
						var delay = this.ISachCounter > 4 && this.ISachCounter % 5 == 0 ? 3210 : 1500;
						if (this.ISachCounter > 12 && this.ISachCounter % 13 == 0)
							delay = UtilityService.GetRandomNumber(3456, 7000);
						this.AddLogs($"... Wait for a few seconds [{delay}ms] to help iSach stay alive ...", null, false);
						await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

						// crawl
						await this.CrawlAsync(parser, folder, onUpdate, UtilityService.GetAppSetting("Books:Crawler-ISachParalell", "false").CastAs<bool>(), cancellationToken).ConfigureAwait(false);
						success++;
					}
					catch (AccessDeniedException ex)
					{
						this.AddLogs($"Error occurred while fetching book [{parser.Title} - {parser.SourceUrl}]", ex);
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
		public async Task<IBookParser> CrawlAsync(IBookParser parser, string directory = null, Func<Book, CancellationToken, Task> onUpdate = null, bool parallelExecutions = true, CancellationToken cancellationToken = default, bool isRecrawl = false)
		{
			// prepare
			directory = directory ?? Utility.DirectoryOfTempFiles;

			// crawl
			await parser.FetchAsync(
				isRecrawl ? null : parser.SourceUrl,
				(p) => this.AddLogs($"Start to fetch the book [{p.SourceUrl}]"),
				(p, times) => this.AddLogs($"The book is parsed [{p.Title} - {p.SourceUrl}] (Execution times: {times.GetElapsedTimes()}) - Start to fetch {p.Chapters.Count} chapter(s)"),
				(p, times) => this.AddLogs($"The book is fetched [{p.Title}] - Execution times: {times.GetElapsedTimes()}"),
				(idx) => this.AddLogs($"Start to fetch the chapter [{(idx < parser.TOCs.Count && parser.Chapters[idx].IsStartsWith("http://") ? parser.TOCs[idx] + " - " + parser.Chapters[idx] : idx.ToString())}]"),
				(idx, contents, times) => this.AddLogs($"The chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}] is fetched - Execution times: {times.GetElapsedTimes()}"),
				(idx, ex) => this.AddLogs($"Error occurred while fetching the chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}]", ex),
				Path.Combine(directory, Definitions.MediaDirectory),
				(p, uri) => this.AddLogs($"Start to download images [{uri}]"),
				(uri, path, times) => this.AddLogs($"Image is downloaded [{uri}] - Execution times: {times.GetElapsedTimes()}"),
				(uri, ex) => this.AddLogs($"Error occurred while downloading image file [{uri}]", ex),
				parallelExecutions,
				cancellationToken
			).ConfigureAwait(false);

			// write JSON file
			parser.TotalChapters = parser.Chapters.Count;
			await UtilityService.WriteTextFileAsync(Path.Combine(directory, UtilityService.GetNormalizedFilename(parser.Title + " - " + parser.Author) + ".json"), parser.ToJson().ToString(Formatting.Indented)).ConfigureAwait(false);

			// update & return
			await this.UpdateAsync(parser.Title, parser.Author, directory, onUpdate, cancellationToken).ConfigureAwait(false);
			return parser;
		}
		#endregion

		#region Update book with new data (JSON, images, ...)
		public async Task<Book> UpdateAsync(string title, string author, string folder, Func<Book, CancellationToken, Task> onCompleted = null, CancellationToken cancellationToken = default)
		{
			// check
			folder = folder ?? Utility.DirectoryOfTempFiles;
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
				await Book.UpdateAsync(book, UtilityService.GetAppSetting("Users:SystemAccountID", "VIEAppsNGX-MMXVII-System-Account"), cancellationToken).ConfigureAwait(false);
			}
			else
			{
				book = json.Copy<Book>();
				book.LastUpdated = DateTime.Now;
				await Book.CreateAsync(book, cancellationToken).ConfigureAwait(false);
			}

			// update files
			var path = book.GetBookDirectory();
			File.Copy(Path.Combine(folder, filename), Path.Combine(path, filename), true);
			File.Delete(Path.Combine(folder, filename));
			UtilityService.GetFiles(Path.Combine(folder, Definitions.MediaDirectory), book.PermanentID + "-*.*")
				.ForEach(file =>
				{
					File.Copy(file.FullName, Path.Combine(path, Definitions.MediaDirectory, file.Name), true);
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