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
						await this.DownloadBookFileAsync(context, cancellationToken).ConfigureAwait(false);
					else
						await this.ShowBookFileAsync(context, cancellationToken).ConfigureAwait(false);
					break;

				case "POST":
					await this.ReceiveBookCoverAsync(context, cancellationToken).ConfigureAwait(false);
					break;

				default:
					throw new MethodNotAllowedException(context.Request.HttpMethod);
			}
		}

		async Task ShowBookFileAsync(HttpContext context, CancellationToken cancellationToken)
		{
			// check "If-Modified-Since" request to reduce traffict
			var eTag = "BookFile#" + context.Request.RawUrl.ToLower().GetMD5();
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
			if (requestInfo.Length < 4)
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

			if (!"no-media-file".IsEquals(name) && (requestInfo.Length < 5 || !requestInfo[3].IsValidUUID()))
				throw new InvalidRequestException();

			var filePath = Utility.FolderOfDataFiles + Path.DirectorySeparatorChar.ToString()
				+ ("no-media-file".IsEquals(name)
					? "no-image.png"
					: name.GetFirstChar() + Path.DirectorySeparatorChar.ToString() + Definitions.MediaFolder + Path.DirectorySeparatorChar.ToString() + requestInfo[3] + "-" + requestInfo[4]);

			var fileInfo = new FileInfo(filePath);
			if (!fileInfo.Exists)
				throw new FileNotFoundException(requestInfo.Last() + " [" + name + "]");

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
			await context.WriteFileToOutputAsync(fileInfo, "image/" + contentType, eTag, null, cancellationToken).ConfigureAwait(false);
		}

		async Task DownloadBookFileAsync(HttpContext context, CancellationToken cancellationToken)
		{
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

			// check permissions
			if (context.User == null || !(context.User is UserPrincipal) || !(context.User as UserPrincipal).IsAuthenticated)
				throw new AccessDeniedException();

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

			var fileInfo = new FileInfo(Utility.FolderOfDataFiles + Path.DirectorySeparatorChar.ToString() + name.GetFirstChar() + Path.DirectorySeparatorChar.ToString() + UtilityService.GetNormalizedFilename(name) + ext);
			if (!fileInfo.Exists)
				throw new FileNotFoundException(requestInfo.Last() + " [" + name + "]");

			// send inter-communicate message to track logs
			var message = new CommunicateMessage()
			{
				ServiceName = "Books",
				Type = "Download",
				Data = new JObject()
				{
					{ "UserID", context.User.Identity.Name },
					{ "BookID", requestInfo[3].Url64Decode() },
				}
			};
			await this.SendInterCommunicateMessageAsync(message, cancellationToken).ConfigureAwait(false);

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
			await context.WriteFileToOutputAsync(fileInfo, "application/" + contentType, eTag, UtilityService.GetNormalizedFilename(name) + ext, cancellationToken).ConfigureAwait(false);
		}

		Task ReceiveBookCoverAsync(HttpContext context, CancellationToken cancellationToken)
		{
			throw new NotImplementedException();
		}

	}
}