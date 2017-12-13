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
		public int MaxPages { get; private set; }
		public List<string> Logs { get; private set; }
		void AddLogs(string log, Exception ex = null)
		{
			this.Logs.Add(log);
			if (ex != null)
			{
				this.Logs.Add(ex.Message + " [" + ex.GetType() + "]");
				this.Logs.Add(ex.StackTrace);
			}
		}

		internal void Start(Action<Book> onUpdate, Action<Crawler, long> onCompleted, Action<Crawler, Exception> onError, CancellationToken cancellationToken = default(CancellationToken))
		{
			this.Logs = new List<string>();
			this.MaxPages = UtilityService.GetAppSetting("BookCrawlerMaxPages", "2").CastAs<int>();
			this.AddLogs($"Total {this.MaxPages} page(s) of each site will be crawled");
			Task.Run(async () =>
			{
				await this.StartAsync(onUpdate, onCompleted, onError, cancellationToken).ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		async Task StartAsync(Action<Book> onUpdate, Action<Crawler, long> onCompleted, Action<Crawler, Exception> onError, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				var stopwatch = new Stopwatch();
				stopwatch.Start();

				await Task.WhenAll(
					this.StartVnThuQuanCrawlerAsync(onUpdate, cancellationToken),
					this.StartISachCrawlerAsync(onUpdate, cancellationToken)
				).ConfigureAwait(false);

				stopwatch.Stop();
				onCompleted?.Invoke(this, stopwatch.ElapsedMilliseconds);

				this._vnThuQuanBooks = null;
				this._vnThuQuanBookself = null;
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				onError?.Invoke(this, ex);
			}
		}

		#region Crawl books of VnThuQuan.net
		IBookselfParser _vnThuQuanBookself;
		List<IBookParser> _vnThuQuanBooks;

		async Task StartVnThuQuanCrawlerAsync(Action<Book> onUpdate, CancellationToken cancellationToken = default(CancellationToken))
		{
			// bookself
			this._vnThuQuanBooks = new List<IBookParser>();
			this._vnThuQuanBookself = new Parsers.Bookselfs.VnThuQuan().Initialize();
			while (this._vnThuQuanBookself.CurrentPage <= this.MaxPages)
			{
				await this.CrawlVnThuQuanBookselfAsync(cancellationToken).ConfigureAwait(false);
				this._vnThuQuanBooks = this._vnThuQuanBooks.Concat(this._vnThuQuanBookself.Books).ToList();
				this._vnThuQuanBookself.CurrentPage++;
			}

			// books
			this.AddLogs($"Start crawl {this._vnThuQuanBooks.Count} book(s) of VnThuQuan.net");
			var index = 0;
			var success = 0;
			while (index < this._vnThuQuanBooks.Count)
			{
				if (await Book.GetAsync<Book>((this._vnThuQuanBooks[index].Title + " - " + this._vnThuQuanBooks[index].Author).ToLower().GetMD5(), cancellationToken).ConfigureAwait(false) == null)
				{
					await this.CrawlVnThuQuanBookAsync(this._vnThuQuanBooks[index], Utility.FolderOfContributedFiles + @"\crawlers", cancellationToken).ConfigureAwait(false);
					await this.MoveAsync(this._vnThuQuanBooks[index].Title, this._vnThuQuanBooks[index].Author, Utility.FolderOfContributedFiles + @"\crawlers", onUpdate, cancellationToken).ConfigureAwait(false);
					success++;
				}
				index++;
			}
			this.AddLogs($"Total of {success} book(s) of VnThuQuan.net had been crawled");
		}

		async Task CrawlVnThuQuanBookselfAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			this.AddLogs($"Start crawl the page number {this._vnThuQuanBookself.CurrentPage} of VnThuQuan.net");
			await this._vnThuQuanBookself.ParseAsync(
				(p, times) => this.AddLogs($"Crawl the page number {this._vnThuQuanBookself.CurrentPage} of VnThuQuan.net is completed in {times.GetElapsedTimes()}"),
				(p, ex) => this.AddLogs($"Error occurred while crawling the page {this._vnThuQuanBookself.CurrentPage} of VnThuQuan.net", ex),
				cancellationToken
			).ConfigureAwait(false);
		}

		public async Task CrawlVnThuQuanBookAsync(IBookParser parser, string folder = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			folder = folder ?? Utility.FolderOfTempFiles;
			await parser.FetchAsync(
				parser.SourceUrl,
				(p) => this.AddLogs($"Start to fetch the book [{p.SourceUrl}]"),
				(p, times) => this.AddLogs($"The book is parsed [{p.Title} - {p.SourceUrl}] (Execution times: {times.GetElapsedTimes()}) - Start to fetch {p.Chapters.Count} chapter(s)"),
				(p, times) =>this.AddLogs($"The book is fetched [{p.Title}] - Execution times: {times.GetElapsedTimes()}"),
				(idx) => this.AddLogs($"Start to fetch the chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}]"),
				(idx, contents, times) => this.AddLogs($"The chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}] is fetched - Execution times: {times.GetElapsedTimes()}"),
				(idx, ex) => this.AddLogs($"Error occurred while fetching the chapter [{(idx < parser.TOCs.Count ? parser.TOCs[idx] : idx.ToString())}]", ex),
				folder + @"\" + Utility.MediaFolder,
				(p, uri) => this.AddLogs($"Start to download images [{uri}]"),
				(uri, path, times) => this.AddLogs($"Image is downloaded [{uri} - {path}] - Execution times: {times.GetElapsedTimes()}"),
				(uri, ex) => this.AddLogs($"Error occurred while downloading file [{uri}]", ex),
				UtilityService.GetAppSetting("BookCrawlVnThuQuanParalell", "true").CastAs<bool>(),
				cancellationToken
			).ConfigureAwait(false);
			await UtilityService.WriteTextFileAsync(folder + @"\" + UtilityService.GetNormalizedFilename(parser.Title + " - " + parser.Author) + ".json", parser.ToJson().ToString(Formatting.Indented), false).ConfigureAwait(false);
		}
		#endregion

		#region Crawl books of ISach.info
		Task StartISachCrawlerAsync(Action<Book> onUpdate, CancellationToken cancellationToken = default(CancellationToken))
		{
			return Task.CompletedTask;
		}
		#endregion

		#region Move files of book and update database
		async Task MoveAsync(string title, string author, string folder = null, Action<Book> onUpdate = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			folder = folder ?? Utility.FolderOfTempFiles;
			var filename = UtilityService.GetNormalizedFilename(title + " - " + author) + ".json";
			if (!File.Exists(folder + @"\" + filename))
				return;

			var json = JObject.Parse(await UtilityService.ReadTextFileAsync(folder + @"\" + filename).ConfigureAwait(false));
			var book = await Book.GetAsync<Book>((json["ID"] as JValue).Value.ToString(), cancellationToken).ConfigureAwait(false);
			if (book != null)
			{
				book.CopyFrom(json);
				book.LastUpdated = DateTime.Now;
				await Book.UpdateAsync(book, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				book = json.Copy<Book>();
				book.LastUpdated = DateTime.Now;
				await Book.CreateAsync(book, cancellationToken).ConfigureAwait(false);
			}

			var path = book.GetFolderPath();
			File.Copy(folder + @"\" + filename, path + @"\" + filename, true);

			var files = UtilityService.GetFiles(folder + @"\" + Utility.MediaFolder, book.PermanentID + "-*.*");
			files.ForEach(file => File.Copy(file.FullName, path + @"\" + Utility.MediaFolder + @"\" + file.Name, true));

			File.Delete(folder + @"\" + filename);
			files.ForEach(file => File.Delete(file.FullName));

			onUpdate?.Invoke(book);
		}
		#endregion

	}
}