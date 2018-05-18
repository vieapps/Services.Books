#region Related component
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json.Linq;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
#endregion

namespace net.vieapps.Services.Books
{
	public class FileHandler : FileHttpHandler
	{
		ILogger Logger { get; set; }
		Uri RequestUri { get; set; }

		public override async Task ProcessRequestAsync(HttpContext context, CancellationToken cancellationToken = default(CancellationToken))
		{
			this.Logger = Components.Utility.Logger.CreateLogger<FileHandler>();
			this.RequestUri = context.GetRequestUri();

			switch (context.Request.Method)
			{
				case "GET":
					if (this.RequestUri.PathAndQuery.IsStartsWith("/books/download/"))
						await this.DownloadBookFileAsync(context, cancellationToken).ConfigureAwait(false);
					else
						await this.ShowBookFileAsync(context, cancellationToken).ConfigureAwait(false);
					break;

				case "POST":
					await this.ReceiveBookCoverAsync(context, cancellationToken).ConfigureAwait(false);
					break;

				default:
					throw new MethodNotAllowedException(context.Request.Method);
			}
		}

		async Task ShowBookFileAsync(HttpContext context, CancellationToken cancellationToken)
		{
			// check "If-Modified-Since" request to reduce traffict
			var eTag = "BookFile#" + $"{this.RequestUri}".ToLower().GenerateUUID();
			if (eTag.IsEquals(context.Request.Headers["If-None-Match"].First()) && !context.Request.Headers["If-Modified-Since"].First().Equals(""))
			{
				context.SetResponseHeaders((int)HttpStatusCode.NotModified, eTag, 0, "public", context.GetCorrelationID());
				if (Global.IsDebugLogEnabled)
					context.WriteLogs(this.Logger, "BookFiles", $"Response to request with status code 304 to reduce traffic ({this.RequestUri})");
				return;
			}

			// prepare
			var requestInfo = this.RequestUri.GetRequestPathSegments();
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

			var filePath = "no-media-file".IsEquals(name)
				? Path.Combine(Utility.FolderOfDataFiles, "no-image.png")
				: Path.Combine(Utility.FolderOfDataFiles, name.GetFirstChar(), Definitions.MediaFolder, requestInfo[3] + "-" + requestInfo[4]);

			var fileInfo = new FileInfo(filePath);
			if (!fileInfo.Exists)
				throw new FileNotFoundException(requestInfo.Last() + " [" + name + "]");

			// response
			context.SetResponseHeaders((int)HttpStatusCode.OK, new Dictionary<string, string>
			{
				{ "Cache-Control", "public" },
				{ "Expires", DateTime.Now.AddDays(7).ToHttpString() }
			});

			string contentType = fileInfo.Name.IsEndsWith(".png")
				? "png"
				: fileInfo.Name.IsEndsWith(".jpg") || fileInfo.Name.IsEndsWith(".jpeg")
					? "jpeg"
					: fileInfo.Name.IsEndsWith(".gif")
						? "gif"
						: "bmp";
			await context.WriteAsync(fileInfo, "image/" + contentType, null, eTag, cancellationToken).ConfigureAwait(false);

			if (Global.IsDebugLogEnabled)
				context.WriteLogs(this.Logger, "BookFiles", $"Show file successful ({this.RequestUri})");
		}

		async Task DownloadBookFileAsync(HttpContext context, CancellationToken cancellationToken)
		{
			// prepare
			var requestUrl = $"{this.RequestUri}";
			while (requestUrl.StartsWith("/"))
				requestUrl = requestUrl.Right(requestUrl.Length - 1);
			if (requestUrl.IndexOf("?") > 0)
				requestUrl = requestUrl.Left(requestUrl.IndexOf("?"));

			// check "If-Modified-Since" request to reduce traffict
			var eTag = "BookFile#" + requestUrl.ToLower().GenerateUUID();
			if (eTag.IsEquals(context.Request.Headers["If-None-Match"].First()) && !context.Request.Headers["If-Modified-Since"].First().Equals(""))
			{
				context.SetResponseHeaders((int)HttpStatusCode.NotModified, eTag, 0, "public", context.GetCorrelationID());
				if (Global.IsDebugLogEnabled)
					context.WriteLogs(this.Logger, "BookFiles", $"Response to request with status code 304 to reduce traffic ({this.RequestUri})");
				return;
			}

			// check permissions
			if (context.User == null || context.User.Identity == null || !context.User.Identity.IsAuthenticated)
				throw new AccessDeniedException();

			// parse
			var requestInfo = this.RequestUri.GetRequestPathSegments();
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

			var fileInfo = new FileInfo(Utility.GetFilePathOfBook(name) + ext);
			if (!fileInfo.Exists)
				throw new FileNotFoundException(requestInfo.Last() + " [" + name + "]");

			// send inter-communicate message to track logs
			await this.SendInterCommunicateMessageAsync(new CommunicateMessage()
			{
				ServiceName = "Books",
				Type = "Download",
				Data = new JObject
				{
					{ "UserID", context.User.Identity.Name },
					{ "BookID", requestInfo[3].Url64Decode() },
				}
			}, cancellationToken).ConfigureAwait(false);

			// response
			context.SetResponseHeaders((int)HttpStatusCode.OK, new Dictionary<string, string>
			{
				{ "Cache-Control", "public" },
				{ "Expires", DateTime.Now.AddDays(7).ToHttpString() }
			});

			string contentType = fileInfo.Name.IsEndsWith(".epub")
				? "epub+zip"
				: fileInfo.Name.IsEndsWith(".mobi")
					? "x-mobipocket-ebook"
					: fileInfo.Name.IsEndsWith(".json")
						?"json"
						: "octet-stream";
			await context.WriteAsync(fileInfo, "application/" + contentType, UtilityService.GetNormalizedFilename(name) + ext, eTag, cancellationToken).ConfigureAwait(false);
		}

		async Task ReceiveBookCoverAsync(HttpContext context, CancellationToken cancellationToken)
		{
			// check permissions
			if (context.User == null || context.User.Identity == null || !context.User.Identity.IsAuthenticated)
				throw new AccessDeniedException();

			var id = context.Request.Headers["x-book-id"];
			if (!await (context.User.Identity as UserIdentity).CanEditAsync("books", "book", id).ConfigureAwait(false))
				throw new AccessDeniedException();

			// prepare
			var info = await context.CallServiceAsync("books", "book", "GET", new Dictionary<string, string>()
			{
				{ "object-identity", "brief-info" },
				{ "id", id }
			}).ConfigureAwait(false);

			var path = Path.Combine(Utility.FolderOfDataFiles, (info["Title"] as JValue).Value.ToString().GetFirstChar(), Definitions.MediaFolder, (info["PermanentID"] as JValue).Value as string + "-");
			var name = ((info["Title"] as JValue).Value as string + " - " + (info["Author"] as JValue).Value as string + " - " + DateTime.Now.ToIsoString()).GetMD5();
			var extension = "jpg";

			// base64 image
			if (!context.Request.Headers["x-as-base64"].First().Equals(""))
			{
				// parse
				var body = (await context.ReadTextAsync(cancellationToken).ConfigureAwait(false)).ToExpandoObject();
				var data = body.Get<string>("Data").ToArray();
				var content = data.Last().Base64ToBytes();
				extension = data.First().ToArray(";").First().ToArray(":").Last();
				extension = extension.IsEndsWith("png")
					? "png"
					: extension.IsEndsWith("bmp")
						? "bmp"
						: extension.IsEndsWith("gif")
							? "gif"
							: "jpg";

				// write to file
				await UtilityService.WriteBinaryFileAsync(path + name + "." + extension, content, cancellationToken).ConfigureAwait(false);
				if (Global.IsDebugLogEnabled)
					context.WriteLogs(this.Logger, "BookFiles", $"New cover (base64) has been uploaded ({path + name + "." + extension} - {content.Length:#,##0} bytes)");
			}

			// file
			else
			{
				var file = context.Request.Form.Files[0];
				extension = Path.GetExtension(file.FileName);
				using (var stream = new FileStream(path + name + "." + extension, FileMode.Create, FileAccess.Write, FileShare.None, TextFileReader.BufferSize, true))
				{
					await context.Request.Form.Files[0].CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
					if (Global.IsDebugLogEnabled)
						context.WriteLogs(this.Logger, "BookFiles", $"New cover (file) has been uploaded ({path + name + "." + extension} - {stream.Position:#,##0} bytes)");
				}
			}

			// response
			await context.WriteAsync(new JObject
			{
				{ "Uri", Definitions.MediaURI + name + "." + extension }
			}, cancellationToken).ConfigureAwait(false);
		}
	}
}