#region Related component
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.IO;
using System.Net;

using Newtonsoft.Json.Linq;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Services.Files;
#endregion

namespace net.vieapps.Services.Books
{
	public class FileHandler : AbstractHttpHandler
	{
		public override async Task ProcessRequestAsync(HttpContext context, CancellationToken cancellationToken = default(CancellationToken))
		{
			switch (context.Request.HttpMethod)
			{
				case "GET":
					if (context.Request.RawUrl.IsStartsWith("/books/download/"))
						await this.DownloadBookFileAsync(context, cancellationToken);
					else
						await this.ShowBookFileAsync(context, cancellationToken);
					break;

				case "POST":
					await this.ReceiveBookCoverAsync(context, cancellationToken);
					break;

				default:
					throw new MethodNotAllowedException(context.Request.HttpMethod);
			}
		}

		async Task ShowBookFileAsync(HttpContext context, CancellationToken cancellationToken)
		{
			// check "If-Modified-Since" request to reduce traffict
			var eTag = "BookMedia#" + context.Request.RawUrl.ToLower().GetMD5();
			if (context.Request.Headers["If-Modified-Since"] != null && eTag.Equals(context.Request.Headers["If-None-Match"]))
			{
				context.Response.Cache.SetCacheability(HttpCacheability.Public);
				context.Response.StatusCode = (int)HttpStatusCode.NotModified;
				context.Response.StatusDescription = "Not Modified";
				context.Response.Headers.Add("ETag", "\"" + eTag + "\"");
				return;
			}

			// prepare
			var requestUrl = context.Request.RawUrl.Substring(context.Request.ApplicationPath.Length);
			while (requestUrl.StartsWith("/"))
				requestUrl = requestUrl.Right(requestUrl.Length - 1);
			if (requestUrl.IndexOf("?") > 0)
				requestUrl = requestUrl.Left(requestUrl.IndexOf("?"));

			var requestInfo = requestUrl.ToArray('/', true);
			if (requestInfo.Length < 5 || !requestInfo[3].IsValidUUID())
				throw new InvalidRequestException();

			var name = "";
			try
			{
				name = requestInfo[2].Url64Decode();
			}
			catch (Exception ex)
			{
				throw new InvalidRequestException(ex);
			}

			var fileInfo = new FileInfo(Utility.FolderOfDataFiles + @"\" + name.GetFirstChar() + @"\" + Utility.MediaFolder + @"\" + requestInfo[3] + "-" + requestInfo[4]);
			if (!fileInfo.Exists)
				throw new FileNotFoundException(context.Request.RawUrl);

			// set cache policy
			context.Response.Cache.SetCacheability(HttpCacheability.Public);
			context.Response.Cache.SetExpires(DateTime.Now.AddDays(7));
			context.Response.Cache.SetSlidingExpiration(true);
			context.Response.Cache.SetOmitVaryStar(true);
			context.Response.Cache.SetValidUntilExpires(true);
			context.Response.Cache.SetLastModified(fileInfo.LastWriteTime);
			context.Response.Cache.SetETag(eTag);

			// write file to output stream
			string contentType = fileInfo.Name.IsEndsWith(".png")
				? "png"
				: fileInfo.Name.IsEndsWith(".jpg") || fileInfo.Name.IsEndsWith(".jpeg")
					? "jpeg"
					: fileInfo.Name.IsEndsWith(".gif")
						? "gif"
						: "bmp";
			await context.WriteFileToOutputAsync(fileInfo, "image/" + contentType, eTag, null, cancellationToken);
		}

		async Task DownloadBookFileAsync(HttpContext context, CancellationToken cancellationToken)
		{
			// check permissions
			if (context.User == null || !(context.User is UserIdentity) || !(context.User as UserIdentity).IsAuthenticated)
				throw new AccessDeniedException();

			// prepare
			var requestUrl = context.Request.RawUrl.Substring(context.Request.ApplicationPath.Length);
			while (requestUrl.StartsWith("/"))
				requestUrl = requestUrl.Right(requestUrl.Length - 1);
			if (requestUrl.IndexOf("?") > 0)
				requestUrl = requestUrl.Left(requestUrl.IndexOf("?"));

			// check "If-Modified-Since" request to reduce traffict
			var eTag = "BookFile#" + requestUrl.ToLower().GetMD5();
			if (context.Request.Headers["If-Modified-Since"] != null && eTag.Equals(context.Request.Headers["If-None-Match"]))
			{
				context.Response.Cache.SetCacheability(HttpCacheability.Public);
				context.Response.StatusCode = (int)HttpStatusCode.NotModified;
				context.Response.StatusDescription = "Not Modified";
				context.Response.Headers.Add("ETag", "\"" + eTag + "\"");
				return;
			}

			// parse
			var requestInfo = requestUrl.ToArray('/', true);
			if (requestInfo.Length < 4)
				throw new InvalidRequestException();

			string ext = requestUrl.IsEndsWith(".epub")
				? ".epub"
				: requestUrl.IsEndsWith(".mobi")
					? ".mobi"
					: ".json";

			var name = "";
			try
			{
				name = requestInfo[2].Url64Decode();
			}
			catch (Exception ex)
			{
				throw new InvalidRequestException(ex);
			}

			var fileInfo = new FileInfo(Utility.FolderOfDataFiles + @"\" + name.GetFirstChar() + @"\" + name + ext);
			if (!fileInfo.Exists)
				throw new FileNotFoundException(context.Request.RawUrl);

			// set cache policy
			context.Response.Cache.SetCacheability(HttpCacheability.Public);
			context.Response.Cache.SetExpires(DateTime.Now.AddDays(7));
			context.Response.Cache.SetSlidingExpiration(true);
			context.Response.Cache.SetOmitVaryStar(true);
			context.Response.Cache.SetValidUntilExpires(true);
			context.Response.Cache.SetLastModified(fileInfo.LastWriteTime);
			context.Response.Cache.SetETag(eTag);

			// write file to output stream
			string contentType = fileInfo.Name.IsEndsWith(".epub")
				? "epub+zip"
				: fileInfo.Name.IsEndsWith(".mobi")
					? "x-mobipocket-ebook"
					: fileInfo.Name.IsEndsWith(".json")
						?"json"
						: "octet-stream";
			await context.WriteFileToOutputAsync(fileInfo, "application/" + contentType, eTag, name, cancellationToken);

			// send inter-communicate message to track counters & logs
			var message = new CommunicateMessage()
			{
				ServiceName = "books",
				Type = "Book",
				Data = new JObject()
				{
					{ "Verb", "Download" },
					{ "UserID", (context.User as UserIdentity).ID },
					{ "Environment", new JObject()
						{
							{ "IP", context.Request.UserHostAddress },
							{ "UserAgent", context.Request.UserAgent },
							{ "UrlReferer", context.Request.UrlReferrer?.AbsoluteUri },
							{ "Query", new JValue(context.Request.QueryString.ToDictionary()) },
							{ "Header", new JValue(context.Request.Headers.ToDictionary()) }
						}
					}
				}
			};
			await this.SendInterCommunicateMessageAsync(message, cancellationToken);
		}

		async Task ReceiveBookCoverAsync(HttpContext context, CancellationToken cancellationToken)
		{
			await Task.Delay(0);
		}

	}
}