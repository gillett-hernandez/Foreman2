using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman
{
	public class PlantShort : IEquatable<PlantShort>
	{
		public string Name { get; private set; }
		public long PlantID { get; private set; }
		public bool isMissing { get; private set; }
		public Dictionary<string, double> Products { get; private set; }

		public PlantShort(string name)
		{
			Name = name;
			PlantID = -1;
			isMissing = false;
			Products = new Dictionary<string, double>();
		}

		public PlantShort(PlantProcess plantProcess)
		{
			Name = plantProcess.Name;
			PlantID = plantProcess.PlantID;
			isMissing = plantProcess.IsMissing;

			Products = new Dictionary<string, double>();
			foreach (var kvp in plantProcess.ProductSet)
				Products.Add(kvp.Key.Name, kvp.Value);
		}

		public PlantShort(JToken plantProcess)
		{
			Name = (string)plantProcess["Name"];
			PlantID = (long)plantProcess["PlantID"];
			isMissing = (bool)plantProcess["isMissing"];

			Products = new Dictionary<string, double>();
			foreach (JProperty ingredient in plantProcess["Products"])
				Products.Add((string)ingredient.Name, (double)ingredient.Value);
		}

		public static List<PlantShort> GetSetFromJson(JToken jdata)
		{
			List<PlantShort> resultList = new List<PlantShort>();
			foreach (JToken recipe in jdata)
				resultList.Add(new PlantShort(recipe));
			return resultList;
		}

		public bool Equals(PlantShort other)
		{
			return this.Name == other.Name &&
				this.Products.Count == other.Products.Count && this.Products.SequenceEqual(other.Products);
		}

		public bool Equals(PlantProcess other)
		{
			bool similar = this.Name == other.Name && this.Products.Count == other.ProductList.Count;

			if (similar)
			{
				foreach (Item ingredient in other.ProductList)
					if (!this.Products.ContainsKey(ingredient.Name) || this.Products[ingredient.Name] != other.ProductSet[ingredient])
						return false;
			}
			return true;
		}
	}

	public class PlantShortNaInPrComparer : IEqualityComparer<PlantShort> //unlike the default plantshort comparer this one doesnt compare product quantities, just names
	{
		public bool Equals(PlantShort x, PlantShort y)
		{
			if (x == y)
				return true;

			if (x.Name != y.Name)
				return false;
			if (x.Products.Count != y.Products.Count)
				return false;

			foreach (string i in x.Products.Keys)
				if (!y.Products.ContainsKey(i))
					return false;

			return true;
		}

		public int GetHashCode(PlantShort obj)
		{
			return obj.GetHashCode();
		}

	}
}
