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
	public class FileHandler : Services.FileHandler
	{
		public override ILogger Logger { get; } = Components.Utility.Logger.CreateLogger<FileHandler>();

		Uri RequestUri { get; set; }

		public override Task ProcessRequestAsync(HttpContext context, CancellationToken cancellationToken = default(CancellationToken))
		{
			this.RequestUri = context.GetRequestUri();
			switch (context.Request.Method)
			{
				case "GET":
					return this.RequestUri.PathAndQuery.IsStartsWith("/books/download/")
						? this.DownloadBookFileAsync(context, cancellationToken)
						: this.ShowBookFileAsync(context, cancellationToken);

				case "POST":
					return this.ReceiveBookCoverAsync(context, cancellationToken);

				default:
					return Task.FromException(new MethodNotAllowedException(context.Request.Method));
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
					context.WriteLogs(this.Logger, "Http.Books", $"Response to request with status code 304 to reduce traffic ({this.RequestUri})");
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

			await Task.WhenAll(
				context.WriteAsync(fileInfo, $"{fileInfo.GetMimeType()}; charset=utf-8", null, eTag, cancellationToken),
				!Global.IsDebugLogEnabled ? Task.CompletedTask : context.WriteLogsAsync(this.Logger, "Http.Books", $"Show file successful ({this.RequestUri})")
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
					context.WriteLogs(this.Logger, "Http.Books", $"Response to request with status code 304 to reduce traffic ({this.RequestUri})");
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
				{ "Expires", DateTime.Now.AddHours(13).ToHttpString() }
			});

			var contentType = fileInfo.Name.IsEndsWith(".epub")
				? "epub+zip"
				: fileInfo.Name.IsEndsWith(".mobi")
					? "x-mobipocket-ebook"
					: fileInfo.Name.IsEndsWith(".json")
						? "json"
						: "octet-stream";

			await Task.WhenAll(
				context.WriteAsync(fileInfo, $"application/{contentType}; charset=utf-8", UtilityService.GetNormalizedFilename(name) + ext, eTag, cancellationToken),
				new CommunicateMessage
				{
					ServiceName = "Books",
					Type = "Download",
					Data = new JObject
					{
						{ "UserID", context.User.Identity.Name },
						{ "BookID", requestInfo[3].Url64Decode() },
					}
				}.PublishAsync(this.Logger, "Http.Books"),
				!Global.IsDebugLogEnabled ? Task.CompletedTask : context.WriteLogsAsync(this.Logger, "Http.Books", $"Successfully download e-book file - User ID: {context.User.Identity.Name} - Book: {name}")
			).ConfigureAwait(false);
		}

		async Task ReceiveBookCoverAsync(HttpContext context, CancellationToken cancellationToken)
		{
			// check permissions
			if (!context.User.Identity.IsAuthenticated)
				throw new AccessDeniedException();

			var objectID = context.GetHeaderParameter("x-object-identity");
			if (string.IsNullOrWhiteSpace(objectID))
				throw new InvalidRequestException("Invalid object identity");

			var isTemporary = "true".IsEquals(context.GetHeaderParameter("x-temporary"));
			if (!isTemporary && !await context.CanEditAsync("Books", "Book", objectID).ConfigureAwait(false))
				throw new AccessDeniedException();

			// prepare
			var info = await context.CallServiceAsync(new RequestInfo(context.GetSession(), "Books", "Book", "GET")
			{
				Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "object-identity", "brief-info" },
					{ "id", objectID }
				}
			}, cancellationToken, this.Logger, "Http.Books").ConfigureAwait(false);

			var fileSize = 0;
			var filePath = Path.Combine(Utility.FolderOfDataFiles, info.Get<string>("Title").GetFirstChar(), Definitions.MediaFolder, info.Get<string>("PermanentID") + "-");
			var fileName = (info.Get<string>("Title") + " - " + info.Get<string>("Author") + " - " + DateTime.Now.ToIsoString()).GenerateUUID();
			var content = new byte[0];
			var asBase64 = context.GetHeaderParameter("x-as-base64") != null;

			// read content from base64 string
			if (asBase64)
			{
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
				fileName += "." + extension;

				content = data.Last().Base64ToBytes();
				fileSize = content.Length;
			}

			// read content from uploaded file of multipart/form-data
			else
			{
				// prepare
				var file = context.Request.Form.Files.Count > 0 ? context.Request.Form.Files[0] : null;
				if (file == null || file.Length < 1)
					throw new InvalidRequestException("No uploaded file is found");

				if (!file.ContentType.IsStartsWith("image/"))
					throw new InvalidRequestException("No uploaded image file is found");

				fileSize = (int)file.Length;
				fileName += Path.GetExtension(file.FileName);

				using (var stream = file.OpenReadStream())
				{
					content = new byte[file.Length];
					await stream.ReadAsync(content, 0, fileSize).ConfigureAwait(false);
				}
			}

			// write into file on the disc
			filePath += fileName;
			if (File.Exists(filePath))
				try
				{
					File.Delete(filePath);
				}
				catch { }
			await UtilityService.WriteBinaryFileAsync(filePath, content, cancellationToken).ConfigureAwait(false);

			// response
			var response = new JObject
			{
				{ "URI", Definitions.MediaURI + fileName }
			};
			await Task.WhenAll(
				context.WriteAsync(response, cancellationToken),
				!Global.IsDebugLogEnabled ? Task.CompletedTask : context.WriteLogsAsync(this.Logger, "Http.Books", $"New cover image ({(asBase64 ? "base64" : "file")}) has been uploaded ({filePath} - {fileSize:#,##0} bytes)")
			).ConfigureAwait(false);
		}
	}
}