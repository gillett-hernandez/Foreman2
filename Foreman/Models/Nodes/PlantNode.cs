﻿using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;

namespace Foreman
{
	public class PlantNode : BaseNode
	{
		public enum Errors
		{
			Clean = 0b_0000_0000_0000,
			ItemDoesntGrow = 0b_0000_0000_0001,
			InvalidGrowResult = 0b_0000_0000_0010,
			InputItemMissing = 0b_0000_0000_0100,
			PlantProcessMissing = 0b_0000_0000_1000,

			QualityMissing = 0b_0000_0001_0000,

			InvalidLinks = 0b_1000_0000_0000
		}
		public enum Warnings
		{
			Clean = 0b_0000_0000_0000,
			QualityIsDisabled = 0b_1000_0000_0000_0000,
		}
		public Errors ErrorSet { get; private set; }
		public Warnings WarningSet { get; private set; }

		private readonly BaseNodeController controller;
		public override BaseNodeController Controller { get { return controller; } }

		public ItemQualityPair Seed { get; private set; }
		public PlantProcess BasePlantProcess { get; internal set; }

		public override IEnumerable<ItemQualityPair> Inputs { get { yield return Seed; } }
		public override IEnumerable<ItemQualityPair> Outputs { get { foreach (Item product in BasePlantProcess.ProductList) yield return new ItemQualityPair(product, product.Owner.DefaultQuality); } }

		//for plant nodes, the SetValue is 'number of plant tiles'
		public override double ActualSetValue { get { return ActualRatePerSec * Seed.Item.PlantResult.GrowTime; } }
		public override double DesiredSetValue { get; set; }
		public override double MaxDesiredSetValue { get { return ProductionGraph.MaxTiles; } }
		public override string SetValueDescription { get { return "Number of farming tiles"; } }

		public override double DesiredRatePerSec { get { return DesiredSetValue / Seed.Item.PlantResult.GrowTime; } }

		public PlantNode(ProductionGraph graph, int nodeID, ItemQualityPair item) : this(graph, nodeID, item.Item.PlantResult, item.Quality) { }
		public PlantNode(ProductionGraph graph, int nodeID, PlantProcess plantProcess, Quality quality) : base(graph, nodeID)
		{
			BasePlantProcess = plantProcess;
			Seed = new ItemQualityPair(plantProcess.Seed, quality);
			controller = PlantNodeController.GetController(this);
			ReadOnlyNode = new ReadOnlyPlantNode(this);
		}

		internal override NodeState GetUpdatedState()
		{
			ErrorSet = Errors.Clean;

			if (Seed.Item.PlantResult == null)
			{
				ErrorSet |= Errors.ItemDoesntGrow;
			}
			if (Seed.Item.PlantResult != BasePlantProcess)
			{
				ErrorSet |= Errors.InvalidGrowResult;
			}
			if (Seed.Item.IsMissing)
			{
				ErrorSet |= Errors.InputItemMissing;
			}
			if (BasePlantProcess.IsMissing)
			{
				ErrorSet |= Errors.PlantProcessMissing;
			}
			if (Seed.Quality.IsMissing)
			{
				ErrorSet |= Errors.QualityMissing;
			}
			if (!AllLinksValid)
			{
				ErrorSet |= Errors.InvalidLinks;
			}

			if (ErrorSet != Errors.Clean) //warnings are NOT processed if error has been found. This makes sense (as an error is something that trumps warnings), plus guarantees we dont accidentally check statuses of missing objects (which rightfully dont exist in regular cache)
			{
				return NodeState.Error;
			}
			if (AllLinksConnected)
			{
				return NodeState.Clean;
			}
			return NodeState.MissingLink;
		}

		public override double GetConsumeRate(ItemQualityPair item) { return ActualRate; }
		public override double GetSupplyRate(ItemQualityPair item) { return ActualRate * outputRateFor(item); }

		internal override double inputRateFor(ItemQualityPair item) { return 1; }
		internal override double outputRateFor(ItemQualityPair item) { return BasePlantProcess.ProductSet[item.Item]; }

		public override void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			base.GetObjectData(info, context);

			info.AddValue("NodeType", NodeType.Plant);
			info.AddValue("PlantProcessID", BasePlantProcess.PlantID);
			info.AddValue("BaseQuality", Seed.Quality.Name);
		}

		public override string ToString() { return string.Format("Plant Growth node for: {0} ({1})", Seed.Item.Name, Seed.Quality.Name); }
	}

	public class ReadOnlyPlantNode : ReadOnlyBaseNode
	{
		public ItemQualityPair Seed => MyNode.Seed;
		public PlantProcess SeedPlantProcess => MyNode.BasePlantProcess;

		private readonly PlantNode MyNode;

		public ReadOnlyPlantNode(PlantNode node) : base(node) { MyNode = node; }

		public override List<string> GetErrors()
		{
			PlantNode.Errors ErrorSet = MyNode.ErrorSet;
			List<string> errors = new List<string>();

			if ((ErrorSet & PlantNode.Errors.InputItemMissing) != 0)
			{
				errors.Add(string.Format("> Item \"{0}\" doesnt exist in preset!", Seed.Item.FriendlyName));
			}
			if ((ErrorSet & PlantNode.Errors.PlantProcessMissing) != 0)
			{
				errors.Add(string.Format("> Growth process for item \"{0}\" doesnt exist in preset!", Seed.Item.FriendlyName));
			}
			if ((ErrorSet & PlantNode.Errors.ItemDoesntGrow) != 0)
			{
				errors.Add(string.Format("> Item \"{0}\" cant be planted!", Seed.Item.FriendlyName));
			}
			if ((ErrorSet & PlantNode.Errors.InvalidGrowResult) != 0)
			{
				errors.Add(string.Format("> Growth result for item \"{0}\" doesnt match preset!", Seed.Item.FriendlyName));
			}
			if ((ErrorSet & PlantNode.Errors.QualityMissing) != 0)
			{
				errors.Add(string.Format("> Quality \"{0}\" doesnt exist in preset!", Seed.Quality.FriendlyName));
			}
			if ((ErrorSet & PlantNode.Errors.InvalidLinks) != 0)
			{
				errors.Add("> Some links are invalid!");
			}
			return errors;
		}

		public override List<string> GetWarnings() { Trace.Fail("Spoil node never has the warning state!"); return null; }
	}

	public class PlantNodeController : BaseNodeController
	{
		private readonly PlantNode MyNode;

		protected PlantNodeController(PlantNode myNode) : base(myNode) { MyNode = myNode; }

		public static PlantNodeController GetController(PlantNode node)
		{
			if (node.Controller != null)
			{
				return (PlantNodeController)node.Controller;
			}
			return new PlantNodeController(node);
		}

		public void UpdatePlantResult()
		{
			if (MyNode.BasePlantProcess != MyNode.Seed.Item.PlantResult)
			{
				MyNode.BasePlantProcess = MyNode.Seed.Item.PlantResult;
				foreach (NodeLink link in MyNode.OutputLinks.Where(l => !MyNode.BasePlantProcess.ProductList.Contains(l.Item.Item)))
				{
					link.Controller.Delete();
				}
				MyNode.UpdateState();
			}
		}

		public override Dictionary<string, Action> GetErrorResolutions()
		{
			Dictionary<string, Action> resolutions = new Dictionary<string, Action>();
			if ((MyNode.ErrorSet & (PlantNode.Errors.InputItemMissing | PlantNode.Errors.PlantProcessMissing | PlantNode.Errors.ItemDoesntGrow)) != 0)
			{
				resolutions.Add("Delete node", new Action(() => this.Delete()));
			}
			if ((MyNode.ErrorSet & PlantNode.Errors.InvalidGrowResult) != 0)
			{
				resolutions.Add("Update plant results", new Action(() => UpdatePlantResult()));
			}
			else
			{
				foreach (KeyValuePair<string, Action> kvp in GetInvalidConnectionResolutions())
				{
					resolutions.Add(kvp.Key, kvp.Value);
				}
			}
			return resolutions;
		}

		public override Dictionary<string, Action> GetWarningResolutions() { Trace.Fail("Plant node never has the warning state!"); return null; }
	}
}
