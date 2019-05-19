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

		public override async Task ProcessRequestAsync(HttpContext context, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(FileHandlerExtensions.FilesPath))
			{
				FileHandlerExtensions.FilesPath = UtilityService.GetAppSetting("Path:Books");
				if (string.IsNullOrWhiteSpace(FileHandlerExtensions.FilesPath))
					FileHandlerExtensions.FilesPath = Path.Combine(Global.RootPath, "data-files", "books");
				if (!FileHandlerExtensions.FilesPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
					FileHandlerExtensions.FilesPath += Path.DirectorySeparatorChar.ToString();
			}

			if (context.Request.Method.IsEquals("GET") || context.Request.Method.IsEquals("HEAD"))
			{
				if (context.GetRequestUri().PathAndQuery.IsStartsWith("/books/download/"))
					await this.DownloadAsync(context, cancellationToken).ConfigureAwait(false);
				else
					await this.ShowAsync(context, cancellationToken).ConfigureAwait(false);
			}
			else if (context.Request.Method.IsEquals("POST"))
				await this.ReceiveAsync(context, cancellationToken).ConfigureAwait(false);
			else
				throw new MethodNotAllowedException(context.Request.Method);
		}

		async Task ShowAsync(HttpContext context, CancellationToken cancellationToken)
		{
			// check "If-Modified-Since" request to reduce traffict
			var requestUri = context.GetRequestUri();
			var eTag = "BookFile#" + $"{requestUri}".ToLower().GenerateUUID();
			if (eTag.IsEquals(context.GetHeaderParameter("If-None-Match")) && context.GetHeaderParameter("If-Modified-Since") != null)
			{
				context.SetResponseHeaders((int)HttpStatusCode.NotModified, eTag, 0, "public", context.GetCorrelationID());
				if (Global.IsDebugLogEnabled)
					context.WriteLogs(this.Logger, "Http.Books", $"Response to request with status code 304 to reduce traffic ({requestUri})");
				return;
			}

			// prepare
			var pathSegments = requestUri.GetRequestPathSegments();
			if (pathSegments.Length < 4)
				throw new InvalidRequestException();

			var name = "";
			try
			{
				name = pathSegments[2].Url64Decode();
			}
			catch (Exception ex)
			{
				throw new InvalidRequestException(ex);
			}

			var permanentID = pathSegments.Length > 3 ? pathSegments[3] : "";
			var filename = pathSegments.Length > 4 ? pathSegments[4] : "";

			if (!"no-media-file".IsEquals(name) && !permanentID.IsValidUUID())
				throw new InvalidRequestException();

			var filePath = "no-media-file".IsEquals(name)
				? Path.Combine(FileHandlerExtensions.FolderOfDataFiles, "no-image.png")
				: Path.Combine(FileHandlerExtensions.FolderOfDataFiles, name.GetFirstChar(), FileHandlerExtensions.MediaFolder, $"{permanentID}-{filename}");

			var fileInfo = new FileInfo(filePath);
			if (!fileInfo.Exists)
				throw new FileNotFoundException(pathSegments.Last() + " [" + name + "]");

			// response
			context.SetResponseHeaders((int)HttpStatusCode.OK, fileInfo.GetMimeType(), eTag, fileInfo.LastWriteTime.ToUnixTimestamp(), "public", TimeSpan.FromDays(7), context.GetCorrelationID());
			await context.WriteAsync(fileInfo, cancellationToken).ConfigureAwait(false);
			if (Global.IsDebugLogEnabled)
				await context.WriteLogsAsync(this.Logger, "Http.Books", $"Show file successful ({requestUri})").ConfigureAwait(false);
		}

		async Task DownloadAsync(HttpContext context, CancellationToken cancellationToken)
		{
			var requestUri = context.GetRequestUri();
			var requestUrl = requestUri.GetUrl(true, true);

			// check "If-Modified-Since" request to reduce traffict
			var eTag = $"BookFile#{requestUrl.GenerateUUID()}";
			if (eTag.IsEquals(context.GetHeaderParameter("If-None-Match")) && context.GetHeaderParameter("If-Modified-Since") != null)
			{
				context.SetResponseHeaders((int)HttpStatusCode.NotModified, eTag, 0, "public", context.GetCorrelationID());
				if (Global.IsDebugLogEnabled)
					context.WriteLogs(this.Logger, "Http.Books", $"Response to request with status code 304 to reduce traffic ({requestUri})");
				return;
			}

			// prepare
			var pathSegments = requestUri.GetRequestPathSegments();
			if (pathSegments.Length < 4)
				throw new InvalidRequestException();

			string name = "", bookID = "";
			try
			{
				name = pathSegments[2].Url64Decode();
				bookID = pathSegments[3].Url64Decode();
			}
			catch (Exception ex)
			{
				throw new InvalidRequestException(ex);
			}

			// check permissions
			if (!await context.CanDownloadAsync("Books", "Book", null, null, bookID, cancellationToken).ConfigureAwait(false))
				throw new AccessDeniedException();

			// check file
			var extension = requestUrl.IsEndsWith(".epub")
				? ".epub"
				: requestUrl.IsEndsWith(".mobi")
					? ".mobi"
					: ".json";

			var fileInfo = new FileInfo(name.GetFilePathOfBook() + extension);
			if (!fileInfo.Exists)
				throw new FileNotFoundException(pathSegments.Last() + $" [{name}]");

			// response
			var contentType = fileInfo.Name.IsEndsWith(".epub")
				? "epub+zip"
				: fileInfo.Name.IsEndsWith(".mobi")
					? "x-mobipocket-ebook"
					: fileInfo.Name.IsEndsWith(".json") ? "json" : "octet-stream";
			context.SetResponseHeaders((int)HttpStatusCode.OK, $"application/{contentType}", eTag, fileInfo.LastWriteTime.ToUnixTimestamp(), "public", TimeSpan.FromDays(7), context.GetCorrelationID());
			await context.WriteAsync(fileInfo, null, UtilityService.GetNormalizedFilename(name) + extension, eTag, cancellationToken).ConfigureAwait(false);

			await Task.WhenAll(
				context.CallServiceAsync(new RequestInfo(context.GetSession(), "Books", "Book")
				{
					Query = new Dictionary<string, string>
					{
						{ "object-identity", "counters" },
						{ "x-action", Components.Security.Action.Download.ToString() },
						{ "x-object-id", bookID },
						{ "x-user-id", context.User.Identity.Name }
					},
					CorrelationID = context.GetCorrelationID()
				}, cancellationToken, this.Logger, "Http.Books"),
				Global.IsDebugLogEnabled ? context.WriteLogsAsync(this.Logger, "Http.Books", $"Successfully download e-book file - User ID: {context.User.Identity.Name} - Book: {name}") : Task.CompletedTask
			).ConfigureAwait(false);
		}

		async Task ReceiveAsync(HttpContext context, CancellationToken cancellationToken)
		{
			// prepare
			var objectID = context.GetParameter("object-identity") ?? context.GetParameter("x-object-id") ?? context.GetParameter("object-id") ?? context.GetParameter("x-book-id") ?? context.GetParameter("id");
			if (string.IsNullOrWhiteSpace(objectID))
				throw new InvalidRequestException("Invalid object identity");

			var isTemporary = "true".IsEquals(context.GetParameter("is-temporary") ?? context.GetParameter("x-temporary"));
			if (!isTemporary && !await context.CanEditAsync("Books", "Book", null, null, objectID, cancellationToken).ConfigureAwait(false))
				throw new AccessDeniedException();

			var info = await context.CallServiceAsync(new RequestInfo(context.GetSession(), "Books", "Book", "GET")
			{
				Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "object-identity", "brief-info" },
					{ "x-object-id", objectID }
				}
			}, cancellationToken, this.Logger, "Http.Uploads").ConfigureAwait(false);

			var fileSize = 0;
			var filePath = Path.Combine(FileHandlerExtensions.FolderOfDataFiles, info.Get<string>("Title").GetFirstChar(), FileHandlerExtensions.MediaFolder, info.Get<string>("PermanentID") + "-");
			var fileName = (info.Get<string>("Title") + " - " + info.Get<string>("Author") + " - " + DateTime.Now.ToIsoString()).GenerateUUID();
			var content = new byte[0];
			var asBase64 = context.GetParameter("x-as-base64") != null;
			if (!Int32.TryParse(UtilityService.GetAppSetting("Limits:BookCover"), out var limitSize))
				limitSize = 1024;

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

				if (fileSize > limitSize * 1024)
				{
					context.SetResponseHeaders((int)HttpStatusCode.RequestEntityTooLarge, null, 0, "private", null);
					return;
				}
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

				if (fileSize > limitSize * 1024)
				{
					context.SetResponseHeaders((int)HttpStatusCode.RequestEntityTooLarge, null, 0, "private", null);
					return;
				}

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
			await context.WriteAsync(new JObject
			{
				{ "URI", FileHandlerExtensions.MediaURI + fileName }
			}, cancellationToken).ConfigureAwait(false);
			if (Global.IsDebugLogEnabled)
				await context.WriteLogsAsync(this.Logger, "Http.Uploads", $"New cover image ({(asBase64 ? "base64" : "file")}) has been uploaded ({filePath} - {fileSize:###,###,###,###,##0} bytes)").ConfigureAwait(false);
		}
	}

	internal static class FileHandlerExtensions
	{
		public static string FilesPath { get; set; }

		public static string FolderOfDataFiles => $"{FileHandlerExtensions.FilesPath}files";

		public static string MediaURI => "book://media/";

		public static string MediaFolder => "media-files";

		public static string GetFolderPathOfBook(this string name)
			=> Path.Combine(FileHandlerExtensions.FolderOfDataFiles, name.GetFirstChar().ToLower());

		public static string GetFilePathOfBook(this string name)
			=> Path.Combine(name.GetFolderPathOfBook(), UtilityService.GetNormalizedFilename(name));

		public static string GetFirstChar(this string @string, bool userLower = true)
		{
			var result = UtilityService.GetNormalizedFilename(@string).ConvertUnicodeToANSI().Trim();
			if (string.IsNullOrWhiteSpace(result))
				return "0";

			new[] { "-", ".", "'", "+", "&", "“", "”" }.ForEach(special =>
			{
				while (result.StartsWith(special))
					result = result.Right(result.Length - 1).Trim();
				while (result.EndsWith(special))
					result = result.Left(result.Length - 1).Trim();
			});
			if (string.IsNullOrWhiteSpace(result))
				return "0";

			var index = 0;
			var isCorrect = false;
			while (!isCorrect && index < result.Length)
			{
				var @char = result.ToUpper()[index];
				isCorrect = (@char >= '0' && @char <= '9') || (@char >= 'A' && @char <= 'Z');
				if (!isCorrect)
					index++;
			}

			var firstChar = index < result.Length
				? result[index]
				: '0';

			return (firstChar >= '0' && firstChar <= '9')
				? "0"
				: userLower
					? firstChar.ToString().ToLower()
					: firstChar.ToString().ToUpper();
		}
	}
}