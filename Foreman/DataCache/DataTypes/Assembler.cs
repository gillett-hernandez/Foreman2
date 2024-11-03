using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman
{
	public interface Assembler : EntityObjectBase
	{
		IReadOnlyCollection<Recipe> Recipes { get; }
		double BaseSpeedBonus { get; }
		double BaseProductivityBonus { get; }
		double BaseConsumptionBonus { get; }
		double BasePollutionBonus { get; }
		double BaseQualityBonus { get; }

		bool AllowBeacons { get; }
		bool AllowModules { get; }
	}

	internal class AssemblerPrototype : EntityObjectBasePrototype, Assembler
	{
		public IReadOnlyCollection<Recipe> Recipes { get { return recipes; } }
		public double BaseSpeedBonus { get; set; }
		public double BaseProductivityBonus { get; set; }
		public double BaseConsumptionBonus { get; set; }
		public double BasePollutionBonus { get; set; }
		public double BaseQualityBonus { get; set; }

		public bool AllowBeacons { get; internal set; }
		public bool AllowModules { get; internal set; }

		internal HashSet<RecipePrototype> recipes { get; private set; }

		public AssemblerPrototype(DataCache dCache, string name, string friendlyName, EntityType type, EnergySource source, bool isMissing = false) : base(dCache, name, friendlyName, type, source, isMissing)
		{
			BaseSpeedBonus = 0;
			BaseProductivityBonus = 0;
			BaseConsumptionBonus = 0;
			BasePollutionBonus = 0;
			BaseQualityBonus = 0;

			AllowBeacons = false; //assumed to be default? no info in LUA
			AllowModules = false; //assumed to be default? no info in LUA

			recipes = new HashSet<RecipePrototype>();
		}

		public override string ToString()
		{
			return String.Format("Assembler: {0}", Name);
		}

		// public double GetRate(Recipe recipe, double beaconBonus, IEnumerable<Module> modules = null)
		// {
		// 	double finalSpeed = this.GetSpeed(recipe);
		// 	if (modules != null)
		// 	{
		// 		foreach (Module module in modules.Where(m => m != null))
		// 		{
		// 			finalSpeed += module.SpeedBonus * this.Speed;
		// 		}
		// 	}

		// 	finalSpeed += beaconBonus * this.Speed;
		// 	// fix #33, bringing the lowest possible speed, after all speed penalties and bonuses, to 0.2
		// 	finalSpeed = (double)(Math.Max(finalSpeed, 0.2));

		// 	double craftingTime = recipe.Time / finalSpeed;
		// 	craftingTime = (double)(Math.Ceiling(craftingTime * 60d) / 60d); //Machines have to wait for a new tick before starting a new item, so round up to the nearest tick

		// 	return (double)(1d / craftingTime);
		// }
	}
}
