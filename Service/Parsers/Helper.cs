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

namespace net.vieapps.Services.Books.Parsers
{
	public static class Helper
	{
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

		static List<string> _DefaultCategories = null;

		public static List<string> DefaultCategories
		{
			get
			{
				if (Helper._DefaultCategories == null)
					Helper._DefaultCategories = @"
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
				return Helper._DefaultCategories;
			}
		}

		static Dictionary<string, string> _NormalizedCategories = null;

		public static Dictionary<string, string> NormalizedCategories
		{
			get
			{
				if (Helper._NormalizedCategories == null)
				{
					var categories = @"
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
					Tùy Bút|Tuỳ Bút - Tạp Văn".Replace("\t", "").Replace("\r", "").Trim().Split("\n".ToCharArray());

					Helper._NormalizedCategories = new Dictionary<string, string>();
					categories.ForEach(cat =>
					{
						var catInfo = cat.Split('|');
						catInfo[0].ToArray().ForEach(c => Helper._NormalizedCategories[c.ToLower()] = catInfo[1]);
					});
					Helper.DefaultCategories.ForEach(c => Helper._NormalizedCategories[c.ToLower()] = c);
				}
				return Helper._NormalizedCategories;
			}
		}

		public static string GetCategory(this string category)
		{
			if (!Helper.NormalizedCategories.TryGetValue(category.Trim().ToLower(), out string result))
				result = "Khác";
			return (Helper.DefaultCategories.IndexOf(result) < 0
				? "Khác"
				: result).GetNormalized();
		}

		public static string GetAuthor(this string author)
		{
			var result = string.IsNullOrWhiteSpace(author)
					|| author.StartsWith("không rõ", StringComparison.OrdinalIgnoreCase) || author.StartsWith("không xác định", StringComparison.OrdinalIgnoreCase)
					|| author.StartsWith("sưu tầm", StringComparison.OrdinalIgnoreCase) || author.Equals("vô danh", StringComparison.OrdinalIgnoreCase)
					|| author.StartsWith("truyện ma", StringComparison.OrdinalIgnoreCase) || author.StartsWith("kiếm hiệp", StringComparison.OrdinalIgnoreCase)
					|| author.StartsWith("dân gian", StringComparison.OrdinalIgnoreCase) || author.StartsWith("cổ tích", StringComparison.OrdinalIgnoreCase)
				? "Khuyết Danh"
				: author.Replace(StringComparison.OrdinalIgnoreCase, " và ", " - ").GetNormalized();

			result = result.Replace(StringComparison.OrdinalIgnoreCase, "(sưu tầm)", "").Replace(StringComparison.OrdinalIgnoreCase, "(dịch)", "").Trim();
			result = result.Replace(StringComparison.OrdinalIgnoreCase, "(phỏng dịch)", "").Replace(StringComparison.OrdinalIgnoreCase, "phỏng dịch", "").Trim();
			result = result.Replace(StringComparison.OrdinalIgnoreCase, "(phóng tác)", "").Replace(StringComparison.OrdinalIgnoreCase, "phóng tác", "").Trim();

			if (result.Equals("Andecxen", StringComparison.OrdinalIgnoreCase)
				|| (result.StartsWith("Hans", StringComparison.OrdinalIgnoreCase) && result.EndsWith("Andersen", StringComparison.OrdinalIgnoreCase)))
				result = "Hans Christian Andersen";
			else if (result.Equals(result.ToUpper()))
				result = result.ToLower().GetNormalized();

			return result;
		}

	}
}