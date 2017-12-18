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

		public List<string> Logs { get; private set; }

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

		internal void Start(Func<Book, Task> onUpdate, Action<long> onCompleted, Action<Exception> onError, CancellationToken cancellationToken = default(CancellationToken))
		{
			this.Logs = new List<string>();
			this.MaxPages = UtilityService.GetAppSetting("BookCrawlerMaxPages", "2").CastAs<int>();
			this.AddLogs($"Total {this.MaxPages} page(s) of each site will be crawled");
			Task.Run(async () =>
			{
				await this.StartAsync(onUpdate, onCompleted, onError, cancellationToken).ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		async Task StartAsync(Func<Book, Task> onUpdate, Action<long> onCompleted, Action<Exception> onError, CancellationToken cancellationToken = default(CancellationToken))
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

		#region vnthuquan.net
		async Task RunCrawlerOfVnThuQuanAsync(Func<Book, Task> onUpdate, CancellationToken cancellationToken = default(CancellationToken))
		{
			// bookself
			var bookParsers = new List<IBookParser>();
			var bookshelfParser = new Parsers.Bookshelfs.VnThuQuan().Initialize();
			while (bookshelfParser.CurrentPage <= this.MaxPages)
			{
				this.AddLogs($"Start crawl the page number {bookshelfParser.CurrentPage} of VnThuQuan.net");
				await bookshelfParser.ParseAsync(
					(p, times) => this.AddLogs($"Crawl the page number {p.CurrentPage} of VnThuQuan.net is completed in {times.GetElapsedTimes()}"),
					(p, ex) => this.AddLogs($"Error occurred while crawling the page {p.CurrentPage} of VnThuQuan.net", ex),
					cancellationToken
				).ConfigureAwait(false);
				bookParsers = bookParsers.Concat(bookshelfParser.Books).ToList();
				bookshelfParser.CurrentPage++;
			}

			// books
			this.AddLogs($"Start crawl {bookParsers.Count} book(s) of VnThuQuan.net");
			var folder = Utility.FolderOfContributedFiles + @"\crawlers";
			int index = 0, success = 0;
			while (index < bookParsers.Count)
			{
				var parser = bookParsers[index];
				if (!Utility.IsExisted(parser.Title, parser.Author))
				{
					await parser.FetchAsync(
						parser.SourceUrl,
						(p) => this.AddLogs($"Start to fetch the book [{p.SourceUrl}]"),
						(p, times) => this.AddLogs($"The book is parsed [{p.Title} - {p.SourceUrl}] (Execution times: {times.GetElapsedTimes()}) - Start to fetch {p.Chapters.Count} chapter(s)"),
						(p, times) => this.AddLogs($"The book is fetched [{p.Title}] - Execution times: {times.GetElapsedTimes()}"),
						(idx) => this.AddLogs($"Start to fetch the chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}]"),
						(idx, contents, times) => this.AddLogs($"The chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}] is fetched - Execution times: {times.GetElapsedTimes()}"),
						(idx, ex) => this.AddLogs($"Error occurred while fetching the chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}]", ex),
						folder + @"\" + Definitions.MediaFolder,
						(p, uri) => this.AddLogs($"Start to download images [{uri}]"),
						(uri, path, times) => this.AddLogs($"Image is downloaded [{uri} - {path}] - Execution times: {times.GetElapsedTimes()}"),
						(uri, ex) => this.AddLogs($"Error occurred while downloading image file [{uri}]", ex),
						UtilityService.GetAppSetting("BookCrawlVnThuQuanParalell", "true").CastAs<bool>(),
						cancellationToken
					).ConfigureAwait(false);
					await UtilityService.WriteTextFileAsync(folder + @"\" + UtilityService.GetNormalizedFilename(parser.Title + " - " + parser.Author) + ".json", parser.ToJson().ToString(Formatting.Indented)).ConfigureAwait(false);
					await this.UpdateAsync(parser.Title, parser.Author, folder, onUpdate, cancellationToken).ConfigureAwait(false);
					success++;
				}
				index++;
			}
			this.AddLogs($"Total {success} book(s) of VnThuQuan.net had been crawled");
		}
		#endregion

		#region isach.info
		async Task RunCrawlerOfISachAsync(Func<Book, Task> onUpdate, CancellationToken cancellationToken = default(CancellationToken))
		{
			// initialize
			var bookParsers = new List<IBookParser>();
			var bookshelfParser = new Parsers.Bookshelfs.ISach().Initialize();
			try
			{
				var robots = await UtilityService.GetWebPageAsync("http://isach.info/robots.txt", null, UtilityService.SpiderUserAgent, cancellationToken).ConfigureAwait(false);
			}
			catch { }

			// bookself
			while (bookshelfParser.UrlPattern != null)
			{
				// delay
				var delay = UtilityService.GetRandomNumber(1234, 2345);
				if (bookshelfParser.TotalPages > 10 && bookshelfParser.CurrentPage > 9 && bookshelfParser.CurrentPage % 10 == 0)
					delay = UtilityService.GetRandomNumber(4321, 5432);
				this.AddLogs($"... Wait for a few seconds [{delay}ms] to help iSach stay alive ...", null, false);
				await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

				// crawl
				this.AddLogs($"Start crawl the page number {bookshelfParser.CurrentPage} of ISach.info ({bookshelfParser.Category + (bookshelfParser.Char != null ? " [" + bookshelfParser.Char + "]" : "")})");
				await bookshelfParser.ParseAsync(
					(p, times) => this.AddLogs($"Crawl the page number {p.CurrentPage} of ISach.info ({p.Category + (p.Char != null ? " [" + p.Char + "]" : "")}) is completed in {times.GetElapsedTimes()}"),
					(p, ex) => this.AddLogs($"Error occurred while crawling the page {p.CurrentPage} of ISach.info ({p.Category + (p.Char != null ? " [" + p.Char + "]" : "")})", ex),
					cancellationToken
				).ConfigureAwait(false);

				// update
				bookParsers = bookParsers.Concat(bookshelfParser.Books).ToList();
				if (bookshelfParser.CurrentPage >= this.MaxPages)
					bookshelfParser.CurrentPage = bookshelfParser.TotalPages = 1;
				((Parsers.Bookshelfs.ISach)bookshelfParser).ReCompute();
			}

			// books
			this.AddLogs($"Start crawl {bookParsers.Count} book(s) of ISach.info");
			this.ISachCounter = 0;
			var folder = Utility.FolderOfContributedFiles + @"\crawlers";
			int index = 0, success = 0;
			while (index < bookParsers.Count)
			{
				var parser = bookParsers[index];
				if (!Utility.IsExisted(parser.Title, parser.Author))
				{
					// delay
					var delay = this.ISachCounter > 4 && this.ISachCounter % 5 == 0 ? 3210 : 1500;
					if (this.ISachCounter > 12 && this.ISachCounter % 13 == 0)
						delay = UtilityService.GetRandomNumber(3456, 7000);
					this.AddLogs($"... Wait for a few seconds [{delay}ms] to help iSach stay alive ...", null, false);
					await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

					// crawl
					await parser.FetchAsync(
						parser.SourceUrl,
						(p) => this.AddLogs($"Start to fetch the book [{p.SourceUrl}]"),
						(p, times) => this.AddLogs($"The book is parsed [{p.Title} - {p.SourceUrl}] (Execution times: {times.GetElapsedTimes()}) - Start to fetch {p.Chapters.Count} chapter(s)"),
						(p, times) => this.AddLogs($"The book is fetched [{p.Title}] - Execution times: {times.GetElapsedTimes()}"),
						(idx) => this.AddLogs($"Start to fetch the chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}]"),
						(idx, contents, times) => this.AddLogs($"The chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}] is fetched - Execution times: {times.GetElapsedTimes()}"),
						(idx, ex) => this.AddLogs($"Error occurred while fetching the chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}]", ex),
						folder + @"\" + Definitions.MediaFolder,
						(p, uri) => this.AddLogs($"Start to download images [{uri}]"),
						(uri, path, times) => this.AddLogs($"Image is downloaded [{uri} - {path}] - Execution times: {times.GetElapsedTimes()}"),
						(uri, ex) => this.AddLogs($"Error occurred while downloading image file [{uri}]", ex),
						UtilityService.GetAppSetting("BookCrawlISachParalell", "false").CastAs<bool>(),
						cancellationToken
					).ConfigureAwait(false);

					// update
					await UtilityService.WriteTextFileAsync(folder + @"\" + UtilityService.GetNormalizedFilename(parser.Title + " - " + parser.Author) + ".json", parser.ToJson().ToString(Formatting.Indented)).ConfigureAwait(false);
					await this.UpdateAsync(parser.Title, parser.Author, folder, onUpdate, cancellationToken).ConfigureAwait(false);
					this.ISachCounter++;
					success++;
				}
				index++;
			}
			this.AddLogs($"Total {success} book(s) of ISach.info had been crawled");
		}
		#endregion

		#region Update book with new data (JSON, images, ...)
		async Task UpdateAsync(string title, string author, string folder, Func<Book, Task> onUpdate = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			folder = folder ?? Utility.FolderOfTempFiles;
			var filename = UtilityService.GetNormalizedFilename(title + " - " + author) + ".json";
			if (!File.Exists(folder + @"\" + filename))
				return;

			var json = JObject.Parse(await UtilityService.ReadTextFileAsync(folder + @"\" + filename).ConfigureAwait(false));
			var book = await Book.GetAsync(title, author, cancellationToken).ConfigureAwait(false);
			if (book != null)
			{
				book.CopyFrom(json, "ID".ToHashSet());
				book.TotalChapters = (json["Chapters"] as JArray).Count;
				book.LastUpdated = DateTime.Now;
				await Book.UpdateAsync(book, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				book = json.Copy<Book>();
				book.TotalChapters = (json["Chapters"] as JArray).Count;
				book.LastUpdated = DateTime.Now;
				await Book.CreateAsync(book, cancellationToken).ConfigureAwait(false);
			}

			var path = book.GetFolderPath();
			File.Copy(folder + @"\" + filename, path + @"\" + filename, true);

			var files = UtilityService.GetFiles(folder + @"\" + Definitions.MediaFolder, book.PermanentID + "-*.*");
			files.ForEach(file => File.Copy(file.FullName, path + @"\" + Definitions.MediaFolder + @"\" + file.Name, true));

			File.Delete(folder + @"\" + filename);
			files.ForEach(file => File.Delete(file.FullName));

			if (onUpdate != null)
				try
				{
					await onUpdate(book).ConfigureAwait(false);
				}
				catch { }
		}
		#endregion

	}
}