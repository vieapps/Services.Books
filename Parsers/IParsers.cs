#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Books
{
	public interface IBookParser
	{
		string ID { get; set; }
		string PermanentID { get; set; }
		string Title { get; set; }
		string Author { get; set; }
		string Category { get; set; }
		string Original { get; set; }
		string Translator { get; set; }
		string Publisher { get; set; }
		string Summary { get; set; }
		string Cover { get; set; }
		string Source { get; set; }
		string SourceUrl { get; set; }
		string Credits { get; set; }
		string Contributor { get; set; }
		string Tags { get; set; }
		string Language { get; set; }
		int TotalChapters { get; set; }
		string ReferUrl { get; set; }
		List<string> TOCs { get; set; }
		List<string> Chapters { get; set; }
		List<string> MediaFileUrls { get; set; }

		Task<IBookParser> ParseAsync(string url = null, Action<IBookParser, long> onCompleted = null, Action<IBookParser, Exception> onError = null, CancellationToken cancellationToken = default(CancellationToken));
		Task<IBookParser> FetchAsync(string url = null, Action<IBookParser> onStart = null, Action<IBookParser, long> onParsed = null, Action<IBookParser, long> onCompleted = null, Action<int> onStartFetchChapter = null, Action<int, List<string>, long> onFetchChapterCompleted = null, Action<int, Exception> onFetchChapterError = null, string folder = null, Action<IBookParser, string> onStartDownload = null, Action<string, string, long> onDownloadCompleted = null, Action<string, Exception> onDownloadError = null, bool parallelExecutions = true, CancellationToken cancellationToken = default(CancellationToken));
		Task<string> FetchChapterAsync(int chapterIndex, Action<int> onStart = null, Action<int, List<string>, long> onCompleted = null, Action<int, Exception> onError = null, CancellationToken cancellationToken = default(CancellationToken));
	}

	public interface IBookshelfParser
	{
		string UrlPattern { get; set; }
		List<string> UrlParameters { get; set; }
		int TotalPages { get; set; }
		int CurrentPage { get; set; }
		List<IBookParser> BookParsers { get; set; }
		string Category { get; set; }
		int CategoryIndex { get; set; }
		string Char { get; set; }
		string ReferUrl { get; set; }

		IBookshelfParser Initialize(string folder = null);
		IBookshelfParser FinaIize(string folder);
		IBookshelfParser Prepare();
		Task<IBookshelfParser> ParseAsync(Action<IBookshelfParser, long> onCompleted = null, Action<IBookshelfParser, Exception> onError = null, CancellationToken cancellationToken = default(CancellationToken));
	}

}