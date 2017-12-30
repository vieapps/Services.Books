#region Related components
using System;
using System.Linq;
using System.Collections.Generic;

using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Books
{
	public static class UtilityExtensions
	{

		#region Normalize string (title, author, ...)
		public static string GetNormalized(this string @string)
		{
			int counter = -1;
			string result = @string.Trim();

			counter = 0;
			while (counter < 100 && (result.StartsWith("-") || result.StartsWith(".") || result.StartsWith(":")))
			{
				result = result.Right(result.Length - 1);
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.EndsWith("-") || result.EndsWith(".") || result.EndsWith(":")))
			{
				result = result.Left(result.Length - 1);
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.EndsWith("-") || result.EndsWith(".")))
			{
				result = result.Left(result.Length - 1);
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.IndexOf("( ") > -1))
			{
				result = result.Replace("( ", "(");
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.IndexOf(" )") > -1))
			{
				result = result.Replace(" )", ")");
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.IndexOf("( ") > -1))
			{
				result = result.Replace("( ", "(");
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.IndexOf(" )") > -1))
			{
				result = result.Replace(" )", ")");
				counter++;
			}

			counter = 0;
			while (counter < 100 && (result.IndexOf("  ") > -1))
			{
				result = result.Replace("  ", " ");
				counter++;
			}

			return result.Trim().GetCapitalizedWords();
		}
		#endregion

		#region Get identity
		public static string GetIdentity(this string url)
		{
			var identity = url;
			var pos = identity.PositionOf("story=");
			if (pos > 0)
				identity = identity.Substring(pos + 6);
			pos = identity.PositionOf("tid=");
			if (pos > 0)
				identity = identity.Substring(pos + 4);
			pos = identity.PositionOf("&");
			if (pos > 0)
				identity = identity.Substring(0, pos);
			pos = identity.PositionOf("#");
			if (pos > 0)
				identity = identity.Substring(0, pos);
			return identity;
		}

		public static string GetFilename(this string url)
		{
			var pos = -1;
			var start = url.PositionOf(".");
			while (start > -1)
			{
				pos = start;
				start = url.PositionOf(".", start + 1);
			}
			return url.Left(pos).ToLower().GetMD5() + url.Substring(pos);
		}
		#endregion

		#region Get category
		static List<string> _DefaultCategories = null;

		public static List<string> DefaultCategories
		{
			get
			{
				if (UtilityExtensions._DefaultCategories == null)
					UtilityExtensions._DefaultCategories = @"
						Lịch Sử
						Hồi Ký - Nhân Vật
						Chính Trị - Xã Hội
						Kinh Doanh - Quản Trị
						Kinh Tế - Tài Chính
						Khoa Học - Công Nghệ
						Tâm Linh - Huyền Bí - Giả Tưởng
						Văn Học Cổ Điển
						Cổ Văn Việt Nam
						Phát Triển Cá Nhân
						Tiếu Lâm
						Cổ Tích
						Tiểu Thuyết
						Trinh Thám
						Kiếm Hiệp
						Tiên Hiệp
						Tuổi Hoa
						Truyện Ngắn
						Tuỳ Bút - Tạp Văn
						Kinh Dị
						Trung Hoa
						Ngôn Tình
						Khác".Replace("\t", "").Replace("\r", "").Trim().Split("\n".ToCharArray()).Select(c => c).ToList();
				return UtilityExtensions._DefaultCategories;
			}
		}

		static Dictionary<string, string> _NormalizedCategories = null;

		public static Dictionary<string, string> NormalizedCategories
		{
			get
			{
				if (UtilityExtensions._NormalizedCategories == null)
				{
					UtilityExtensions._NormalizedCategories = new Dictionary<string, string>();
					@"
					truyện dài|Tiểu Thuyết
					Bài viết|Khác
					Khoa học|Khoa Học - Công Nghệ
					Kinh Dị, Ma quái|Kinh Dị
					Trinh Thám, Hình Sự|Trinh Thám
					Tập Truyện ngắn|Truyện Ngắn
					Suy ngẫm, Làm Người|Phát Triển Cá Nhân
					Kỹ Năng Sống|Phát Triển Cá Nhân
					Nghệ Thuật Sống|Phát Triển Cá Nhân
					Nhân Vật Lịch sử|Lịch Sử
					Triết Học, Kinh Tế|Chính Trị - Xã Hội
					Y Học, Sức Khỏe|Chính Trị - Xã Hội
					Tình Cảm, Xã Hội|Chính Trị - Xã Hội
					Phiêu Lưu, Mạo Hiểm|Tiểu Thuyết
					Hồi Ký, Tuỳ Bút|Tuỳ Bút - Tạp Văn
					VH cổ điển nước ngoài|Văn Học Cổ Điển
					Tôn giáo, Chính Trị|Chính Trị - Xã Hội
					Truyện Tranh|Khác
					Cuộc Chiến VN|Chính Trị - Xã Hội
					Kịch, Kịch bản|Khác
					Khoa học Huyền bí|Tâm Linh - Huyền Bí - Giả Tưởng
					Khoa học, giả tưởng|Tâm Linh - Huyền Bí - Giả Tưởng
					Truyện Cười|Tiếu Lâm
					Khoa Học - Kỹ Thuật|Khoa Học - Công Nghệ
					Kinh Tế|Kinh Tế - Tài Chính
					Tài Chính|Kinh Tế - Tài Chính
					Làm Giàu|Kinh Doanh - Quản Trị
					Tuổi Học Trò|Tuổi Hoa
					Tùy Bút|Tuỳ Bút - Tạp Văn".Replace("\t", "").Replace("\r", "").Trim()
						.ToArray("\n")
						.ForEach(cat =>
						{
							var catInfo = cat.Split('|');
							UtilityExtensions._NormalizedCategories[catInfo[0].ToLower()] = catInfo[1];
						});
					UtilityExtensions.DefaultCategories.ForEach(c => UtilityExtensions._NormalizedCategories[c.ToLower()] = c);
				}
				return UtilityExtensions._NormalizedCategories;
			}
		}

		public static string GetCategory(this string category)
		{
			if (!UtilityExtensions.NormalizedCategories.TryGetValue(category.Trim().ToLower(), out string result))
				result = "Khác";
			return (UtilityExtensions.DefaultCategories.IndexOf(result) < 0
				? "Khác"
				: result).GetNormalized();
		}
		#endregion

		#region Get author
		public static string GetAuthor(this string author)
		{
			var result
				= string.IsNullOrWhiteSpace(author)
					|| author.IsStartsWith("không rõ") || author.IsStartsWith("không xác định") || author.IsStartsWith("chưa biết") || author.Equals("vô danh")
					|| author.IsStartsWith("sưu tầm") || author.IsStartsWith("truyện ma") || author.IsStartsWith("kiếm hiệp")
					|| author.IsStartsWith("dân gian") || author.IsStartsWith("cổ tích") || author.IsEquals("n/a") || author.IsEquals("xxx")
				? "Khuyết Danh"
				: author.GetNormalized();

			result = result.Replace("(sưu tầm)", "").Replace("(dịch)", "").Trim();
			result = result.Replace("(phỏng dịch)", "").Replace("phỏng dịch", "").Trim();
			result = result.Replace("(phóng tác)", "").Replace("phóng tác", "").Trim();

			if (result.Equals("Andecxen")
				|| (result.IsStartsWith("Hans") && result.EndsWith("Andersen")))
				result = "Hans Christian Andersen";
			else if (result.Equals(result.ToUpper()))
				result = result.ToLower().GetNormalized();

			return result;
		}
		#endregion

		#region Normalize TOC
		public static void NormalizeTOC(this IBookParser parser)
		{
			if (parser.Chapters == null || parser.Chapters.Count < 2)
				return;

			var tocs = new List<string>();

			parser.Chapters.ForEach((chapter, index) =>
			{
				string title = null;
				if (chapter.IsStartsWith("<h1>"))
				{
					var pos = chapter.PositionOf("</h1>");
					title = (pos > 0 ? chapter.Substring(4, pos - 4) : "").Trim();
				}
				if (title == null && parser.TOCs != null && parser.TOCs.Count > index)
					tocs.Add(!string.IsNullOrWhiteSpace(parser.TOCs[index]) ? parser.TOCs[index] : (index + 1).ToString() + ".");
				else
					tocs.Add(!string.IsNullOrWhiteSpace(title) ? title : (index + 1).ToString() + ".");
			});

			parser.TOCs = tocs;
		}
		#endregion

	}
}