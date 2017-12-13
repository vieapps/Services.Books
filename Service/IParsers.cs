#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Caching;
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
		string ReferUrl { get; set; }
		List<string> TOCs { get; set; }
		List<string> Chapters { get; set; }
		List<string> MediaFileUrls { get; set; }

		Task<IBookParser> ParseAsync(string url = null, Action<IBookParser, long> onCompleted = null, Action<IBookParser, Exception> onError = null, CancellationToken cancellationToken = default(CancellationToken));
		Task<IBookParser> FetchAsync(string url = null, Action<IBookParser> onStart = null, Action<IBookParser, long> onParsed = null, Action<IBookParser, long> onCompleted = null, Action<int> onStartFetchChapter = null, Action<int, List<string>, long> onFetchChapterCompleted = null, Action<int, Exception> onFetchChapterError = null, string folder = null, Action<IBookParser, string> onStartDownload = null, Action<string, string, long> onDownloadCompleted = null, Action<string, Exception> onDownloadError = null, bool parallelExecutions = true, CancellationToken cancellationToken = default(CancellationToken));
		Task<string> FetchChapterAsync(int chapterIndex, Action<int> onStart = null, Action<int, List<string>, long> onCompleted = null, Action<int, Exception> onError = null, CancellationToken cancellationToken = default(CancellationToken));
	}

	public interface IBookselfParser
	{
		string UrlPattern { get; set; }
		List<string> UrlParameters { get; set; }
		int TotalPages { get; set; }
		int CurrentPage { get; set; }
		List<IBookParser> Books { get; set; }
		int CategoryIndex { get; set; }
		string ReferUrl { get; set; }

		IBookselfParser Initialize(string folder = null);
		IBookselfParser FinaIize(string folder = null);
		Task<IBookselfParser> ParseAsync(Action<IBookselfParser, long> onCompleted = null, Action<IBookselfParser, Exception> onError = null, CancellationToken cancellationToken = default(CancellationToken));
	}

}