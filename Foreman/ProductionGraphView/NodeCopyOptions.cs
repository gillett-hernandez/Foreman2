using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Foreman
{
	[Serializable]
	public class NodeCopyOptions : ISerializable
	{
		public readonly AssemblerQualityPair Assembler;
		public readonly IReadOnlyList<ModuleQualityPair> AssemblerModules;
		public readonly Item Fuel;
		public readonly double NeighbourCount;
		public readonly double ExtraProductivityBonus;

		public readonly BeaconQualityPair Beacon;
		public readonly IReadOnlyList<ModuleQualityPair> BeaconModules;
		public readonly double BeaconCount;
		public readonly double BeaconsPerAssembler;
		public readonly double BeaconsConst;

		public NodeCopyOptions(ReadOnlyRecipeNode node)
		{
			Assembler = node.SelectedAssembler;
			AssemblerModules = new List<ModuleQualityPair>(node.AssemblerModules);
			Fuel = node.Fuel;
			Beacon = node.SelectedBeacon;
			BeaconModules = new List<ModuleQualityPair>(node.BeaconModules);
			BeaconCount = node.BeaconCount;
			BeaconsPerAssembler = node.BeaconsPerAssembler;
			BeaconsConst = node.BeaconsConst;
			NeighbourCount = node.NeighbourCount;
			ExtraProductivityBonus = node.ExtraProductivity;
		}

		private NodeCopyOptions(AssemblerQualityPair assembler, List<ModuleQualityPair> assemblerModules, double neighbourCount, double extraProductivityBonus, Item fuel, BeaconQualityPair beacon, List<ModuleQualityPair> beaconModules, double beaconCount, double beaconsPerA, double beaconsCont)
		{
			Assembler = assembler;
			AssemblerModules = assemblerModules;
			Fuel = fuel;
			Beacon = beacon;
			BeaconModules = beaconModules;
			BeaconCount = beaconCount;
			BeaconsPerAssembler = beaconsPerA;
			BeaconsConst = beaconsCont;
			NeighbourCount = neighbourCount;
			ExtraProductivityBonus = extraProductivityBonus;
		}

		public static NodeCopyOptions GetNodeCopyOptions(string serialized, DataCache cache)
		{
			try { return GetNodeCopyOptions(JObject.Parse(serialized), cache); }
			catch { return null; }
		}

		public static NodeCopyOptions GetNodeCopyOptions(JToken json, DataCache cache)
		{
			if (json["Version"] == null || (int)json["Version"] != Properties.Settings.Default.ForemanVersion || json["Object"] == null || (string)json["Object"] != "NodeCopyOptions")
			{
				return null;
			}

			bool beacons = json["Beacon"] != null;
			Assembler assembler = cache.Assemblers.ContainsKey((string)json["Assembler"]) ? cache.Assemblers[(string)json["Assembler"]] : null;
			Quality assemblerQuality = cache.Qualities.ContainsKey((string)json["AssemblerQuality"]) ? cache.Qualities[(string)json["AssemblerQuality"]] : null;
			AssemblerQualityPair assemberQP = new AssemblerQualityPair(assembler, assemblerQuality ?? cache.DefaultQuality);

			Beacon beacon = (beacons && cache.Beacons.ContainsKey((string)json["Beacon"])) ? cache.Beacons[(string)json["Beacon"]] : null;
			Quality beaconQuality = (beacons && cache.Qualities.ContainsKey((string)json["BeaconQuality"])) ? cache.Qualities[(string)json["BeaconQuality"]] : null;
			BeaconQualityPair beaconQP = new BeaconQualityPair(beacon, beaconQuality ?? cache.DefaultQuality);

			List<ModuleQualityPair> aModules = new List<ModuleQualityPair>();
			foreach(JToken moduleToken in json["AModules"])
			{
				string moduleName = (string)moduleToken["Name"];
				string moduleQuality = (string)moduleToken["Quality"];
				Module module = cache.Modules.ContainsKey(moduleName) ? cache.Modules[moduleName] : null;
				Quality quality = cache.Qualities.ContainsKey(moduleQuality) ? cache.Qualities[moduleQuality] : cache.DefaultQuality;
				if (module != null)
					aModules.Add(new ModuleQualityPair(module, quality));
			}

            List<ModuleQualityPair> bModules = new List<ModuleQualityPair>();
            foreach (JToken moduleToken in json["BModules"])
            {
                string moduleName = (string)moduleToken["Name"];
                string moduleQuality = (string)moduleToken["Quality"];
                Module module = cache.Modules.ContainsKey(moduleName) ? cache.Modules[moduleName] : null;
                Quality quality = cache.Qualities.ContainsKey(moduleQuality) ? cache.Qualities[moduleQuality] : cache.DefaultQuality;
                if (module != null)
                    bModules.Add(new ModuleQualityPair(module, quality));
            }

			Item fuel = (json["Fuel"] != null && cache.Items.ContainsKey((string)json["Fuel"])) ? cache.Items[(string)json["Fuel"]] : null;

            NodeCopyOptions nco = new NodeCopyOptions(
				assemberQP,
				aModules,
				(double)json["Neighbours"],
				(double)json["ExtraProductivity"],
				fuel,
				beaconQP,
				bModules,
				beacons ? (double)json["BeaconCount"] : 0,
				beacons ? (double)json["BeaconsPA"] : 0,
				beacons ? (double)json["BeaconsC"] : 0);
			return nco;
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("Version", Properties.Settings.Default.ForemanVersion);
			info.AddValue("Object", "NodeCopyOptions");
			info.AddValue("Assembler", Assembler.Assembler.Name);
			info.AddValue("AssemblerQuality", Assembler.Quality.Name);

			info.AddValue("Neighbours", NeighbourCount);
			info.AddValue("ExtraProductivity", ExtraProductivityBonus);
			info.AddValue("AModules", AssemblerModules);
			info.AddValue("BModules", BeaconModules);

			if (Fuel != null)
			{
				info.AddValue("Fuel", Fuel.Name);
			}

			if (Beacon)
			{
				info.AddValue("Beacon", Beacon.Beacon.Name);
				info.AddValue("BeaconQuality", Beacon.Quality.Name);
				info.AddValue("BeaconCount", BeaconCount);
				info.AddValue("BeaconsPA", BeaconsPerAssembler);
				info.AddValue("BeaconsC", BeaconsConst);
			}
		}
	}
}
