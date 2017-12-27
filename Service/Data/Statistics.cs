#region Related components
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Books
{
	[Serializable]
	public class Statistics
	{
		public Statistics()
		{
			this.List = new List<StatisticInfo>();
		}

		#region Properties
		public int Count
		{
			get
			{
				return this.List.Count;
			}
		}

		public List<StatisticInfo> List { get; private set; }
		#endregion

		#region Methods
		public StatisticInfo this[string name]
		{
			get
			{
				var index = this.IndexOf(name);
				return index > -1 ? this.List[index] : null;
			}
			set
			{
				var index = this.IndexOf(name);
				if (index > -1)
					this.List[index] = value;
				else
					this.Add(value);
			}
		}

		public StatisticInfo this[int index]
		{
			get
			{
				return index > -1 && index < this.List.Count ? this.List[index] : null;
			}
			set
			{
				if (index > -1 && index < this.List.Count)
					this.List[index] = value;
				else
					this.Add(value);
			}
		}

		public void Add(StatisticInfo info)
		{
			this.List.Add(info);
		}

		public StatisticInfo FindFirst(string name)
		{
			return !string.IsNullOrWhiteSpace(name)
				? this.List.FirstOrDefault(item => item.Name.IsEquals(name))
				: null;
		}

		public int IndexOf(StatisticInfo info)
		{
			return this.List.IndexOf(info);
		}

		public int IndexOf(string name)
		{
			var item = this.FindFirst(name);
			return item == null
				? -1
				: this.IndexOf(item);
		}

		public IEnumerable<StatisticInfo> Find(string firstChar, string sortBy = "Name", string sortMode = "Ascending", int skip = 0, int take = 0)
		{
			var @char = string.IsNullOrWhiteSpace(firstChar)
				? ""
				: firstChar.GetFirstChar().ToUpper();

			var list = this.List.Where(item => item.FirstChar.IsEquals(@char));

			if (!string.IsNullOrWhiteSpace(sortBy))
			{
				if (sortBy.IsEquals("Name"))
					list = "Ascending".IsEquals(sortMode)
						? list.OrderBy(item => item.FirstChar)
							.ThenBy(item => item.Name)
							.ThenByDescending(item => item.Counters)
						: list.OrderByDescending(item => item.FirstChar)
							.ThenByDescending(item => item.Name)
							.ThenByDescending(item => item.Counters);

				else if (sortBy.IsEquals("Counters"))
					list = "Ascending".IsEquals(sortMode)
						? list.OrderBy(item => item.Counters)
							.ThenBy(item => item.FirstChar)
							.ThenBy(item => item.Name)
						: list.OrderByDescending(item => item.Counters)
							.ThenBy(item => item.FirstChar)
							.ThenBy(item => item.Name);
			}

			if (skip > 0)
				list = list.Skip(skip);

			if (take > 0)
				list = list.Take(take);

			return list;
		}

		public void Clear()
		{
			this.List.Clear();
		}

		public override string ToString()
		{
			return this.ToJson().ToString(Formatting.None);
		}

		public JToken ToJson()
		{
			return this.List.ToJArray();
		}
		#endregion

		#region Working with files
		internal void Load(string path, string filename, bool seperatedByFirstChar = false)
		{
			var filePath = (string.IsNullOrWhiteSpace(path) ? "" : path.Trim() + Path.DirectorySeparatorChar.ToString()) + filename;

			if (seperatedByFirstChar)
				Utility.Chars.ForEach(@char =>
				{
					if (File.Exists(string.Format(filePath, @char)))
						UtilityService.ReadTextFile(string.Format(filePath, @char))
							.FromJson<List<StatisticInfo>>()
							.ForEach(item => this.List.Add(new StatisticInfo()
							{
								Name = item.Name,
								Counters = item.Counters,
								FirstChar = item.Name.GetAuthorName().GetFirstChar().ToUpper()
							}));
				});

			else if (File.Exists(filePath))
				this.List.Append(UtilityService.ReadTextFile(filePath).FromJson<List<StatisticInfo>>());
		}

		internal void Save(string path, string filename, bool seperatedByFirstChar = false)
		{
			var filePath = (string.IsNullOrWhiteSpace(path) ? "" : path.Trim() + Path.DirectorySeparatorChar.ToString()) + filename;

			if (seperatedByFirstChar)
				Utility.Chars.ForEach(@char =>
				{
					var list = this.Find(@char);
					UtilityService.WriteTextFile(string.Format(filePath, @char), (list?.ToJArray() ?? new JArray()).ToString(Formatting.Indented));
				});

			else
				UtilityService.WriteTextFile(filePath, this.ToJson().ToString(Formatting.Indented));
		}
		#endregion

	}

	// -------------------------------------

	[Serializable]
	public class StatisticInfo
	{
		public StatisticInfo()
		{
			this.Name = "";
			this.Counters = 0;
		}

		#region Properties
		public string Name { get; set; }

		public int Counters { get; set; }

		[NonSerialized]
		string _FirstChar = null;

		[JsonIgnore]
		public string FirstChar
		{
			set
			{
				this._FirstChar = value;
			}
			get
			{
				if (this._FirstChar == null)
					this._FirstChar = this.Name.GetFirstChar().ToUpper();
				return this._FirstChar;
			}
		}
		#endregion

		#region Methods
		public override int GetHashCode()
		{
			return this.Name.ToLower().GetHashCode();
		}

		public override string ToString()
		{
			return this.ToJson().ToString(Formatting.None);
		}
		#endregion

	}

}