#region Related components
using System;
using System.Diagnostics;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Books
{
	[Serializable]
	[BsonIgnoreExtraElements]
	[DebuggerDisplay("Type = {Type}, Average = {Average}, Total = {Total}")]
	public class RatingPoint
	{

		public RatingPoint()
		{
			this.Type = "General";
			this.Points = 0.0d;
			this.Total = 0;
		}

		#region Properties
		public string Type { get; set; }

		public double Points { get; set; }

		public int Total { get; set; }

		[BsonIgnore]
		public double Average
		{
			get
			{
				return this.Total > 0 ? this.Points / this.Total : 0;
			}
		}
		#endregion

		#region Methods
		public JObject ToJson(bool removeType)
		{
			var json = this.ToJson<RatingPoint>() as JObject;
			if (removeType)
				json.Remove("Type");
			return json;
		}

		public JObject ToJson()
		{
			return this.ToJson(true);
		}

		internal static JObject ToJObject(List<RatingPoint> ratingPoints)
		{
			var json = new JObject();
			foreach (var ratingPoint in ratingPoints)
				json.Add(new JProperty(ratingPoint.Type, ratingPoint.ToJson()));
			return json;
		}

		internal static JArray ToJArray(List<RatingPoint> ratingPoints)
		{
			return ratingPoints.ToJArray();
		}

		internal static RatingPoint Find(List<RatingPoint> rating, string type)
		{
			if (rating != null && !string.IsNullOrWhiteSpace(type))
				for (var index = 0; index < rating.Count; index++)
					if (rating[index].Type.IsEquals(type))
						return rating[index];
			return null;
		}

		internal static RatingPoint Update(RatingPoint rating, int point)
		{
			if (point > 0 && point < 6)
			{
				rating.Total++;
				rating.Points += point;
			}
			return rating;
		}

		internal static RatingPoint Update(List<RatingPoint> rating, string type, int point)
		{
			if (rating != null && !string.IsNullOrWhiteSpace(type))
				for (var index = 0; index < rating.Count; index++)
					if (rating[index].Type.IsEquals(type))
						return RatingPoint.Update(rating[index], point);
			return null;
		}
		#endregion

	}
}