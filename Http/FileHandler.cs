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
			if (eTag.IsEquals(context.GetHeaderParameter("If-None-Match")) && context.GetHeaderParameter("If-Modified-Since") != null)
			{
				context.SetResponseHeaders((int)HttpStatusCode.NotModified, eTag, 0, "public", context.GetCorrelationID());
				if (Global.IsDebugLogEnabled)
					context.WriteLogs(this.Logger, "Books", $"Response to request with status code 304 to reduce traffic ({this.RequestUri})");
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

			var contentType = fileInfo.Name.IsEndsWith(".png")
				? "png"
				: fileInfo.Name.IsEndsWith(".jpg") || fileInfo.Name.IsEndsWith(".jpeg")
					? "jpeg"
					: fileInfo.Name.IsEndsWith(".gif")
						? "gif"
						: "bmp";

			await Task.WhenAll(
				context.WriteAsync(fileInfo, $"image/{contentType}", null, eTag, cancellationToken),
				!Global.IsDebugLogEnabled ? Task.CompletedTask : context.WriteLogsAsync(this.Logger, "Books", $"Show file successful ({this.RequestUri})")
			).ConfigureAwait(false);
		}

		async Task DownloadBookFileAsync(HttpContext context, CancellationToken cancellationToken)
		{
			var requestUrl = context.GetRequestUrl(true, true);

			// check "If-Modified-Since" request to reduce traffict
			var eTag = $"BookFile#{requestUrl.GenerateUUID()}";
			if (eTag.IsEquals(context.GetHeaderParameter("If-None-Match")) && context.GetHeaderParameter("If-Modified-Since") != null)
			{
				context.SetResponseHeaders((int)HttpStatusCode.NotModified, eTag, 0, "public", context.GetCorrelationID());
				if (Global.IsDebugLogEnabled)
					context.WriteLogs(this.Logger, "Books", $"Response to request with status code 304 to reduce traffic ({this.RequestUri})");
				return;
			}

			// check permissions
			if (!context.User.Identity.IsAuthenticated)
				throw new AccessDeniedException();

			// parse
			var requestInfo = this.RequestUri.GetRequestPathSegments();
			if (requestInfo.Length < 4)
				throw new InvalidRequestException();

			var ext = requestUrl.IsEndsWith(".epub")
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

			// response
			context.SetResponseHeaders((int)HttpStatusCode.OK, new Dictionary<string, string>
			{
				{ "Cache-Control", "public" },
				{ "Expires", DateTime.Now.AddDays(7).ToHttpString() }
			});

			var contentType = fileInfo.Name.IsEndsWith(".epub")
				? "epub+zip"
				: fileInfo.Name.IsEndsWith(".mobi")
					? "x-mobipocket-ebook"
					: fileInfo.Name.IsEndsWith(".json")
						? "json"
						: "octet-stream";

			await Task.WhenAll(
				context.WriteAsync(fileInfo, "application/" + contentType, UtilityService.GetNormalizedFilename(name) + ext, eTag, cancellationToken),
				new CommunicateMessage
				{
					ServiceName = "Books",
					Type = "Download",
					Data = new JObject
					{
						{ "UserID", context.User.Identity.Name },
						{ "BookID", requestInfo[3].Url64Decode() },
					}
				}.PublishAsync(this.Logger),
				!Global.IsDebugLogEnabled ? Task.CompletedTask : context.WriteLogsAsync(this.Logger, "Books", $"Successfully download e-book file - User ID: {context.User.Identity.Name} - Book: {name}")
			).ConfigureAwait(false);
		}

		async Task ReceiveBookCoverAsync(HttpContext context, CancellationToken cancellationToken)
		{
			// check permissions
			if (!context.User.Identity.IsAuthenticated)
				throw new AccessDeniedException();

			var id = context.GetHeaderParameter("x-book-id") ?? "";
			if (string.IsNullOrWhiteSpace(id))
				throw new InvalidRequestException("Invalid book identity");

			if (!await context.CanEditAsync("Books", "Book", id).ConfigureAwait(false))
				throw new AccessDeniedException();

			// prepare
			var info = await context.CallServiceAsync("Books", "Book", "GET", new Dictionary<string, string>()
			{
				{ "object-identity", "brief-info" },
				{ "id", id }
			}, null, this.Logger, cancellationToken).ConfigureAwait(false);

			var fileSize = 0;
			var filePath = Path.Combine(Utility.FolderOfDataFiles, info.Get<string>("Title").GetFirstChar(), Definitions.MediaFolder, info.Get<string>("PermanentID") + "-");
			var fileName = (info.Get<string>("Title") + " - " + info.Get<string>("Author") + " - " + DateTime.Now.ToIsoString()).GenerateUUID() + ".";

			// base64
			if (context.GetHeaderParameter("x-as-base64") != null)
			{
				// prepare
				var body = (await context.ReadTextAsync(cancellationToken).ConfigureAwait(false)).ToExpandoObject();
				var data = body.Get<string>("Data").ToArray();

				var extension = data.First().ToArray(";").First().ToArray(":").Last();
				extension = extension.IsEndsWith("png")
					? "png"
					: extension.IsEndsWith("bmp")
						? "bmp"
						: extension.IsEndsWith("gif")
							? "gif"
							: "jpg";

				fileName += extension;
				filePath += fileName;

				var content = data.Last().Base64ToBytes();
				fileSize = content.Length;

				if (File.Exists(filePath))
					try
					{
						File.Delete(filePath);
					}
					catch { }

				// write to file
				await UtilityService.WriteBinaryFileAsync(filePath, content, cancellationToken).ConfigureAwait(false);
			}

			// file
			else
			{
				// prepare
				if (context.Request.Form.Files.Count < 1)
					throw new InvalidRequestException("No uploaded file is found");

				var file = context.Request.Form.Files[0];
				if (file == null || file.Length < 1)
					throw new InvalidRequestException("No uploaded file is found");

				fileSize = (int)file.Length;
				fileName += Path.GetExtension(file.FileName);
				filePath += fileName;

				if (File.Exists(filePath))
					try
					{
						File.Delete(filePath);
					}
					catch { }

				using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, TextFileReader.BufferSize, true))
				{
					await file.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
				}
			}

			// response
			await Task.WhenAll(
				context.WriteAsync(new JObject
				{
					{ "Uri", Definitions.MediaURI + fileName }
				}, cancellationToken),
				!Global.IsDebugLogEnabled ? Task.CompletedTask : context.WriteLogsAsync(this.Logger, "Books", $"New cover image ({(context.GetHeaderParameter("x-as-base64") != null ? "base64" : "file")}) has been uploaded ({filePath} - {fileSize:#,##0} bytes)")
			).ConfigureAwait(false);
		}
	}
}