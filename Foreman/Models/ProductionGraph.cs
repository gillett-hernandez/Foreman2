﻿using Google.OrTools.LinearSolver;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Text;
using System.Xml.Linq;

namespace Foreman
{
	public enum NodeType { Supplier, Consumer, Passthrough, Recipe, Spoil, Plant }
	public enum LinkType { Input, Output }

	public class NodeEventArgs : EventArgs
	{
		public ReadOnlyBaseNode node;
		public NodeEventArgs(ReadOnlyBaseNode node) { this.node = node; }
	}
	public class NodeLinkEventArgs : EventArgs
	{
		public ReadOnlyNodeLink nodeLink;
		public NodeLinkEventArgs(ReadOnlyNodeLink nodeLink) { this.nodeLink = nodeLink; }
	}

	[Serializable]
	public partial class ProductionGraph : ISerializable
	{
		public class NewNodeCollection
		{
			public List<ReadOnlyBaseNode> newNodes { get; private set; }
			public List<ReadOnlyNodeLink> newLinks { get; private set; }
			public NewNodeCollection() { newNodes = new List<ReadOnlyBaseNode>(); newLinks = new List<ReadOnlyNodeLink>(); }
		}

		//public DataCache DCache { get; private set; }

		public enum RateUnit { Per1Sec, Per1Min, Per5Min, Per10Min, Per30Min, Per1Hour };//, Per6Hour, Per12Hour, Per24Hour }
		public static readonly string[] RateUnitNames = new string[] { "1 sec", "1 min", "5 min", "10 min", "30 min", "1 hour" }; //, "6 hours", "12 hours", "24 hours" };
		private static readonly float[] RateMultiplier = new float[] { 1f, 60f, 300f, 600f, 1800f, 3600f }; //, 21600f, 43200f, 86400f };

		public RateUnit SelectedRateUnit { get; set; }
		public float GetRateMultipler() { return RateMultiplier[(int)SelectedRateUnit]; } //the amount of assemblers required will be multipled by the rate multipler when displaying.
		public string GetRateName() { return RateUnitNames[(int)SelectedRateUnit]; }

		public NodeDirection DefaultNodeDirection { get; set; }
		public bool DefaultToSimplePassthroughNodes { get; set; }

		public const double MaxSetFlow = 1e7; //10 million (per second) item flow should be enough for pretty much everything with a generous helping of 'oh god thats way too much!'
		public const double MaxFactories = 1e6; //1 million factories should be good enough as well. NOTE: the auto values can go higher, you just cant set more than 1 million on the manual setting.
		public const double MaxTiles = 1e7; //10 million tiles for planting should be good enough
		public const double MaxInventorySlots = 1e6; // 1 million inventory slots for spoiling should be good enough
		private const int XBorder = 200;
		private const int YBorder = 200;

		public bool PauseUpdates { get; set; }
		public bool PullOutputNodes { get; set; } //if true, the solver will add a 'pull' for output nodes so as to prioritize them over lowering factory count. WARNING: this can lead to '0' solutions if there is any production path that can go to infinity (aka: ensure enough nodes are constrained!)
		public double PullOutputNodesPower { get; set; }
		public double LowPriorityPower { get; set; } //this is the multiplier of the factory cost function for low priority nodes. aka: low priority recipes will be picked if the alternative involves this much more factories (10,000 is a nice value here)
		public bool EnableExtraProductivityForNonMiners { get; set; }

		public AssemblerSelector AssemblerSelector { get; private set; }
		public ModuleSelector ModuleSelector { get; private set; }
		public FuelSelector FuelSelector { get; private set; }

		public IEnumerable<ReadOnlyBaseNode> Nodes { get { return nodes.Select(node => node.ReadOnlyNode); } }
		public IEnumerable<ReadOnlyNodeLink> NodeLinks { get { return nodeLinks.Select(link => link.ReadOnlyLink); } }
		public HashSet<int> SerializeNodeIdSet { get; set; } //if this isnt null then the serialized production graph will only contain these nodes (and links between them)

		//editing this value will require the entire graph to be updated as any recipe nodes on it will possibly change the number of products and possibly cause a cascade of removed links
		private uint maxQualitySteps;
		public uint MaxQualitySteps
		{
			get { return maxQualitySteps; }
			set
			{
				if (value != maxQualitySteps)
				{
					maxQualitySteps = value;
					foreach (BaseNode node in nodes)
					{
						if (node is RecipeNode rnode)
						{
							rnode.UpdateInputsAndOutputs();
						}
					}
				}
			}
		}

		public enum GraphOperation
		{
			CreateNode,
			DeleteNode,
			MoveNode,
			CreateLinks,
			DeleteLinks,
			PseudoStop
		}

		// if there were proper discriminated unions like in rust, i would use those
		// but unfortunately those are tricky and annoying to do in C# so instead we use Nullable where necessary to use nulls for data that the current variant can not fill

		// factorio 2.0 update, adding new variants for spoilResult and plantProcess

		public struct GraphOperationData
		{
			public GraphOperation operationType;

			public Point? priorLocation;
			public Point? location;

			public NodeType? nodeType;
			public ReadOnlyBaseNode node;
			public ItemQualityPair item;
			// implicitly nullable
			public Item spoilResult;
			public RecipeQualityPair recipe;

			public PlantProcess plantProcess;
			// if type is CreateLinks, then modifiedLinks is the list of created links
			// likewise if it's DeleteLinks, then modifiedLinks is the list of deleted links
			public List<ReadOnlyNodeLink> modifiedLinks;

			public GraphOperationData(GraphOperation optype, Point? priorlocation, Point? location, NodeType? nodetype, ItemQualityPair item, RecipeQualityPair recipe, Item spoilResult, PlantProcess plantProcess, ReadOnlyBaseNode node, List<ReadOnlyNodeLink> links)
			{
				this.operationType = optype;
				this.priorLocation = priorlocation;
				this.location = location;
				this.nodeType = nodetype;
				this.item = item;
				this.spoilResult = spoilResult;
				this.plantProcess = plantProcess;
				this.recipe = recipe;
				this.node = node;
				this.modifiedLinks = links;
			}
			public static GraphOperationData PseudoStop()
			{
				// god i hate this
				return new GraphOperationData(GraphOperation.PseudoStop, null, null, null, new ItemQualityPair(), new RecipeQualityPair(), null, null, null, null);
			}
		}

		public Quality DefaultAssemblerQuality { get; set; }

		public event EventHandler<NodeEventArgs> NodeAdded;
		public event EventHandler<NodeEventArgs> NodeDeleted;
		public event EventHandler<NodeLinkEventArgs> LinkAdded;
		public event EventHandler<NodeLinkEventArgs> LinkDeleted;
		public event EventHandler<EventArgs> NodeValuesUpdated;

		public Rectangle Bounds
		{
			get
			{
				if (nodes.Count == 0)
				{
					return new Rectangle(0, 0, 0, 0);
				}

				int xMin = int.MaxValue;
				int yMin = int.MaxValue;
				int xMax = int.MinValue;
				int yMax = int.MinValue;
				foreach (BaseNode node in nodes)
				{
					xMin = Math.Min(xMin, node.Location.X);
					xMax = Math.Max(xMax, node.Location.X);
					yMin = Math.Min(yMin, node.Location.Y);
					yMax = Math.Max(yMax, node.Location.Y);
				}

				return new Rectangle(xMin - XBorder, yMin - YBorder, xMax - xMin + (2 * XBorder), yMax - yMin + (2 * YBorder));
			}
		}

		private HashSet<BaseNode> nodes;
		private HashSet<NodeLink> nodeLinks;
		private List<NodeType> nodeTypes;
		private Dictionary<int, ReadOnlyBaseNode> idToRONode;
		private Dictionary<ReadOnlyBaseNode, BaseNode> roToNode;
		private Dictionary<ReadOnlyNodeLink, NodeLink> roToLink;
		private int lastNodeID;
		public List<ReadOnlyBaseNode> movedNodes;

		private bool redoDirty = false;

		// push operations to the stack
		// pop operations and invert them to undo
		// delete elements from the bottom of the stack when the cardinality exceeds the limit (set in settings)
		// note, delete in blocks separated by pseduostops, so that we don't leave the graph in a weird invalid state.
		private List<GraphOperationData> undoOperationStack;

		// when undoing, push undone operations to the redo stack
		// to redo, pop from the stack and perform the inverse, adding that operation back to the undo stack
		// delete elements from the bottom of the stack when the cardinality exceeds the limit (set in settings)
		// note, delete in blocks separated by pseduostops, so that we don't leave the graph in a weird invalid state.
		private List<GraphOperationData> redoOperationStack;

		public ProductionGraph()
		{
			DefaultNodeDirection = NodeDirection.Up;
			PullOutputNodes = false;
			PullOutputNodesPower = 10;
			LowPriorityPower = 1e5;

			nodes = new HashSet<BaseNode>();
			nodeLinks = new HashSet<NodeLink>();
			roToNode = new Dictionary<ReadOnlyBaseNode, BaseNode>();
			roToLink = new Dictionary<ReadOnlyNodeLink, NodeLink>();
			idToRONode = new Dictionary<int, ReadOnlyBaseNode>();
			undoOperationStack = new List<GraphOperationData>();
			redoOperationStack = new List<GraphOperationData>();
			movedNodes = new List<ReadOnlyBaseNode>();
			nodeTypes = new List<NodeType>();
			lastNodeID = 0;

			AssemblerSelector = new AssemblerSelector();
			ModuleSelector = new ModuleSelector();
			FuelSelector = new FuelSelector();
		}

		public BaseNodeController RequestNodeController(ReadOnlyBaseNode node) { if (roToNode.ContainsKey(node)) { return roToNode[node].Controller; } return null; }

		private int GetNewNodeID()
		{
			return lastNodeID++;
		}

		public void SetUndoCheckpoint()
		{
			undoOperationStack.Add(GraphOperationData.PseudoStop());
		}

		public ReadOnlyConsumerNode CreateConsumerNode(ItemQualityPair item, Point location, int nodeID, bool addToUndo = true)
		{

			ConsumerNode node = new ConsumerNode(this, nodeID, item);
			if (addToUndo)
			{
				redoDirty = true;
				undoOperationStack.Add(new GraphOperationData(
					GraphOperation.CreateNode,
					null,
					location,
					NodeType.Consumer,
					item,
					new RecipeQualityPair(),
					null,
					null,
					node.ReadOnlyNode,
					new List<ReadOnlyNodeLink>()
				));
			}
			node.Location = location;
			node.NodeDirection = DefaultNodeDirection;
			idToRONode[nodeID] = node.ReadOnlyNode;
			nodes.Add(node);
			roToNode.Add(node.ReadOnlyNode, node);
			node.UpdateState();
			NodeAdded?.Invoke(this, new NodeEventArgs(node.ReadOnlyNode));
			return (ReadOnlyConsumerNode)node.ReadOnlyNode;
		}
		public ReadOnlyConsumerNode CreateConsumerNode(ItemQualityPair item, Point location)
		{

			nodeTypes.Add(NodeType.Consumer);
			return CreateConsumerNode(item, location, GetNewNodeID());
		}

		public ReadOnlySupplierNode CreateSupplierNode(ItemQualityPair item, Point location, int nodeID, bool addToUndo = true)
		{
			SupplierNode node = new SupplierNode(this, nodeID, item);
			if (addToUndo)
			{
				redoDirty = true;
				undoOperationStack.Add(new GraphOperationData(
					GraphOperation.CreateNode,
					null,
					location,
					NodeType.Supplier,
					item,
					new RecipeQualityPair(),
					null,
					null,
					node.ReadOnlyNode,
					new List<ReadOnlyNodeLink>()
				));
			}
			node.Location = location;
			node.NodeDirection = DefaultNodeDirection;
			idToRONode[nodeID] = node.ReadOnlyNode;
			nodes.Add(node);
			roToNode.Add(node.ReadOnlyNode, node);
			node.UpdateState();
			NodeAdded?.Invoke(this, new NodeEventArgs(node.ReadOnlyNode));
			return (ReadOnlySupplierNode)node.ReadOnlyNode;
		}
		public ReadOnlySupplierNode CreateSupplierNode(ItemQualityPair item, Point location)
		{
			nodeTypes.Add(NodeType.Supplier);
			return CreateSupplierNode(item, location, GetNewNodeID());
		}

		public ReadOnlyPassthroughNode CreatePassthroughNode(ItemQualityPair item, Point location, int nodeID, bool addToUndo = true)
		{
			PassthroughNode node = new PassthroughNode(this, nodeID, item);
			if (addToUndo)
			{
				redoDirty = true;
				undoOperationStack.Add(new GraphOperationData(
					GraphOperation.CreateNode,
					null,
					location,
					NodeType.Passthrough,
					item,
					new RecipeQualityPair(),
					null,
					null,
					node.ReadOnlyNode,
					new List<ReadOnlyNodeLink>()
				));
			}
			node.Location = location;
			node.NodeDirection = DefaultNodeDirection;
			node.SimpleDraw = DefaultToSimplePassthroughNodes;
			idToRONode[nodeID] = node.ReadOnlyNode;
			nodes.Add(node);
			roToNode.Add(node.ReadOnlyNode, node);
			node.UpdateState();
			NodeAdded?.Invoke(this, new NodeEventArgs(node.ReadOnlyNode));
			return (ReadOnlyPassthroughNode)node.ReadOnlyNode;
		}
		public ReadOnlyPassthroughNode CreatePassthroughNode(ItemQualityPair item, Point location)
		{
			nodeTypes.Add(NodeType.Passthrough);
			return CreatePassthroughNode(item, location, GetNewNodeID());
		}



		public ReadOnlySpoilNode CreateSpoilNode(ItemQualityPair inputItem, Item outputItem, Point location, int nodeID, bool addToUndo = true)
		{
			SpoilNode node = new SpoilNode(this, nodeID, inputItem, outputItem);
			if (addToUndo)
			{
				redoDirty = true;
				undoOperationStack.Add(new GraphOperationData(
					GraphOperation.CreateNode,
					null,
					location,
					NodeType.Spoil,
					inputItem,
					new RecipeQualityPair(),
					outputItem,
					null,
					node.ReadOnlyNode,
					new List<ReadOnlyNodeLink>()
				));
			}
			node.Location = location;
			node.NodeDirection = DefaultNodeDirection;
			idToRONode[nodeID] = node.ReadOnlyNode;
			nodes.Add(node);
			roToNode.Add(node.ReadOnlyNode, node);
			node.UpdateState();
			NodeAdded?.Invoke(this, new NodeEventArgs(node.ReadOnlyNode));
			return (ReadOnlySpoilNode)node.ReadOnlyNode;
		}
		public ReadOnlySpoilNode CreateSpoilNode(ItemQualityPair inputItem, Item outputItem, Point location)
		{
			nodeTypes.Add(NodeType.Spoil);
			return CreateSpoilNode(inputItem, outputItem, location, GetNewNodeID());
		}

		public ReadOnlyPlantNode CreatePlantNode(PlantProcess plantProcess, Quality quality, Point location, int nodeID, bool addToUndo = true)
		{
			PlantNode node = new PlantNode(this, nodeID, plantProcess, quality);
			if (addToUndo)
			{
				redoDirty = true;
				undoOperationStack.Add(new GraphOperationData(
					GraphOperation.CreateNode,
					null,
					location,
					NodeType.Plant,
					new ItemQualityPair(plantProcess.Seed, quality),
					new RecipeQualityPair(),
					null,
					plantProcess,
					node.ReadOnlyNode,
					new List<ReadOnlyNodeLink>()
				));
			}
			node.Location = location;
			node.NodeDirection = DefaultNodeDirection;
			idToRONode[nodeID] = node.ReadOnlyNode;
			nodes.Add(node);
			roToNode.Add(node.ReadOnlyNode, node);
			node.UpdateState();
			NodeAdded?.Invoke(this, new NodeEventArgs(node.ReadOnlyNode));
			return (ReadOnlyPlantNode)node.ReadOnlyNode;
		}

		public ReadOnlyPlantNode CreatePlantNode(PlantProcess plantProcess, Quality quality, Point location)
		{
			nodeTypes.Add(NodeType.Plant);
			return CreatePlantNode(plantProcess, quality, location, GetNewNodeID());
		}

		public ReadOnlyRecipeNode CreateRecipeNode(RecipeQualityPair recipe, Point location, Action<RecipeNode> nodeSetupAction)
		{
			nodeTypes.Add(NodeType.Recipe);
			return CreateRecipeNode(recipe, location, nodeSetupAction, GetNewNodeID());
		}

		public ReadOnlyRecipeNode CreateRecipeNode(RecipeQualityPair recipe, Point location)
		{
			nodeTypes.Add(NodeType.Recipe);
			return CreateRecipeNode(recipe, location, null, GetNewNodeID());
		}

		public ReadOnlyRecipeNode CreateRecipeNode(RecipeQualityPair recipe, Point location, int nodeID, bool addToUndo = true) { return CreateRecipeNode(recipe, location, null, nodeID, addToUndo); }
		private ReadOnlyRecipeNode CreateRecipeNode(RecipeQualityPair recipe, Point location, Action<RecipeNode> nodeSetupAction, int nodeID, bool addToUndo = true) //node setup action is used to populate the node prior to informing everyone of its creation
		{
			RecipeNode node = new RecipeNode(this, nodeID, recipe, DefaultAssemblerQuality);
			if (addToUndo)
			{
				redoDirty = true;
				undoOperationStack.Add(new GraphOperationData(
					GraphOperation.CreateNode,
					null,
					location,
					NodeType.Recipe,
					new ItemQualityPair(),
					recipe,
					null,
					null,
					node.ReadOnlyNode,
					new List<ReadOnlyNodeLink>()
				));
			}
			node.Location = location;
			node.NodeDirection = DefaultNodeDirection;
			nodeSetupAction?.Invoke(node);
			if (nodeSetupAction == null)
			{
				RecipeNodeController rnController = (RecipeNodeController)node.Controller;
				rnController.AutoSetAssembler();
				rnController.AutoSetAssemblerModules();
			}
			nodes.Add(node);
			idToRONode[nodeID] = node.ReadOnlyNode;
			roToNode.Add(node.ReadOnlyNode, node);
			node.UpdateInputsAndOutputs();
			NodeAdded?.Invoke(this, new NodeEventArgs(node.ReadOnlyNode));
			return (ReadOnlyRecipeNode)node.ReadOnlyNode;
		}

		public ReadOnlyNodeLink CreateLink(ReadOnlyBaseNode supplier, ReadOnlyBaseNode consumer, ItemQualityPair item, bool addToUndo = true)
		{
			if (!roToNode.ContainsKey(supplier) || !roToNode.ContainsKey(consumer) || !supplier.Outputs.Contains(item) || !consumer.Inputs.Contains(item))
			{
				bool not_dict_contains_supplier = !roToNode.ContainsKey(supplier);
				bool not_dict_contains_consumer = !roToNode.ContainsKey(consumer);
				bool not_supplier_output_contains_item = !supplier.Outputs.Contains(item);
				bool not_consumer_inputs_contains_item = !consumer.Inputs.Contains(item);
				
				Trace.Fail(string.Format("Node link creation called with invalid parameters! consumer:{0}. supplier:{1}. item:{2}. specific conditions are {3}, {4}, {5}, {6}",
					consumer.ToString(),
					supplier.ToString(),
					item.ToString(),
					not_dict_contains_supplier,
					not_dict_contains_consumer,
					not_supplier_output_contains_item,
					not_consumer_inputs_contains_item
				));
			}

			if (supplier.OutputLinks.Any(l => l.Item == item && l.Consumer == consumer)) //check for an already existing connection
			{
				return supplier.OutputLinks.First(l => l.Item == item && l.Consumer == consumer);
			}

			BaseNode supplierNode = roToNode[supplier];
			BaseNode consumerNode = roToNode[consumer];

			NodeLink link = new NodeLink(this, supplierNode, consumerNode, item);

			if (addToUndo)
			{

				redoDirty = true;
				undoOperationStack.Add(new GraphOperationData(
					GraphOperation.CreateLinks,
					null,
					null,
					null,
					new ItemQualityPair(),
					new RecipeQualityPair(),
					null,
					null,
					null,
					new List<ReadOnlyNodeLink>
					{
						link.ReadOnlyLink
					}
				));
			}

			supplierNode.OutputLinks.Add(link);
			consumerNode.InputLinks.Add(link);
			LinkChangeUpdateImpactedNodeStates(link, LinkType.Input);
			LinkChangeUpdateImpactedNodeStates(link, LinkType.Output);

			nodeLinks.Add(link);
			roToLink.Add(link.ReadOnlyLink, link);
			LinkAdded?.Invoke(this, new NodeLinkEventArgs(link.ReadOnlyLink));
			return link.ReadOnlyLink;
		}

		public void DeleteNode(ReadOnlyBaseNode node, bool addToUndo = true)
		{
			node = idToRONode[node.NodeID];
			if (!roToNode.ContainsKey(node))
			{
				Trace.Fail(string.Format("Node deletion called on a node ({0}) that isnt part of the graph!", node.ToString()));
			}


			foreach (ReadOnlyNodeLink link in node.InputLinks.ToList())
			{
				DeleteLink(link.Supplier, link.Consumer, link.Item, addToUndo);
			}

			foreach (ReadOnlyNodeLink link in node.OutputLinks.ToList())
			{
				DeleteLink(link.Supplier, link.Consumer, link.Item, addToUndo);
			}

			if (addToUndo)
			{

				redoDirty = true;
				NodeType nodetype = nodeTypes[node.NodeID];
				switch (nodetype)
				{
					case NodeType.Supplier:
						{
							undoOperationStack.Add(new GraphOperationData(
								GraphOperation.DeleteNode,
								null,
								node.Location,
								nodetype,
								((ReadOnlySupplierNode)node).SuppliedItem,
								new RecipeQualityPair(),
								null,
								null,
								node,
								new List<ReadOnlyNodeLink>()
							));
							break;
						}
					case NodeType.Consumer:
						{
							undoOperationStack.Add(new GraphOperationData(
								GraphOperation.DeleteNode,
								null,
								node.Location,
								nodetype,
								((ReadOnlyConsumerNode)node).ConsumedItem,
								new RecipeQualityPair(),
								null,
								null,
								node,
								new List<ReadOnlyNodeLink>()
							));
							break;
						}
					case NodeType.Passthrough:
						{
							undoOperationStack.Add(new GraphOperationData(
								GraphOperation.DeleteNode,
								null,
								node.Location,
								nodetype,
								((ReadOnlyPassthroughNode)node).PassthroughItem,
								new RecipeQualityPair(),
								null,
								null,
								node,
								new List<ReadOnlyNodeLink>()
							));
							break;
						}
					case NodeType.Recipe:
						{
							undoOperationStack.Add(new GraphOperationData(
								GraphOperation.DeleteNode,
								null,
								node.Location,
								nodetype,
								new ItemQualityPair(),
								((ReadOnlyRecipeNode)node).BaseRecipe,
								null,
								null,
								node,
								new List<ReadOnlyNodeLink>()
							));
							break;
						}
					case NodeType.Spoil:
						{
							undoOperationStack.Add(new GraphOperationData(
								GraphOperation.DeleteNode,
								null,
								node.Location,
								nodetype,
								((ReadOnlySpoilNode)node).InputItem,
								new RecipeQualityPair(),
								((ReadOnlySpoilNode)node).OutputItem.Item,
								null,
								node,
								new List<ReadOnlyNodeLink>()
							));
							break;
						}
					case NodeType.Plant:
						{
							undoOperationStack.Add(new GraphOperationData(
								GraphOperation.DeleteNode,
								null,
								node.Location,
								nodetype,
								((ReadOnlyPlantNode)node).Seed,
								new RecipeQualityPair(),
								null,
								((ReadOnlyPlantNode)node).SeedPlantProcess,
								node,
								new List<ReadOnlyNodeLink>()
							));
							break;
						}
				}

			}

			nodes.Remove(roToNode[node]);
			roToNode.Remove(node);
			NodeDeleted?.Invoke(this, new NodeEventArgs(node));
		}

		public void DeleteNodes(IEnumerable<ReadOnlyBaseNode> nodes, bool addToUndo = true)
		{
			SetUndoCheckpoint();
			foreach (ReadOnlyBaseNode node in nodes)
			{
				DeleteNode(node, addToUndo);
			}
		}

		public void DeleteLink(ReadOnlyNodeLink link, bool addToUndo = true)
		{
			DeleteLink(link.Supplier, link.Consumer, link.Item, addToUndo);
		}

		public void DeleteLink(ReadOnlyBaseNode supplier, ReadOnlyBaseNode consumer, ItemQualityPair item, bool addToUndo = true)
		{
			supplier = idToRONode[supplier.NodeID];
			consumer = idToRONode[consumer.NodeID];
			if (!roToNode.ContainsKey(consumer) || !roToNode.ContainsKey(supplier))
			{
				Trace.Fail(string.Format("Link deletion called with a link with a supplier or consumer that are not part of the graph. sup ({0}), con ({1})", supplier.ToString(), consumer.ToString()));
			}

			if (supplier.OutputLinks.Any(l => l.Item == item && l.Consumer == consumer)) //check for an already existing connection
			{
				ReadOnlyNodeLink link = supplier.OutputLinks.First(l => l.Item == item && l.Consumer == consumer);

				BaseNode supplierNode = roToNode[supplier];
				BaseNode consumerNode = roToNode[consumer];


				if (addToUndo)
				{
					redoDirty = true;
					undoOperationStack.Add(new GraphOperationData(
						GraphOperation.DeleteLinks,
						null,
						null,
						null,
						new ItemQualityPair(),
						new RecipeQualityPair(),
						null,
						null,
						null,
						new List<ReadOnlyNodeLink> { link }
					));
				}


				NodeLink nodeLink = roToLink[link];
				nodeLink.ConsumerNode.InputLinks.Remove(nodeLink);
				nodeLink.SupplierNode.OutputLinks.Remove(nodeLink);
				LinkChangeUpdateImpactedNodeStates(nodeLink, LinkType.Input);
				LinkChangeUpdateImpactedNodeStates(nodeLink, LinkType.Output);

				nodeLinks.Remove(nodeLink);
				roToLink.Remove(link);
				LinkDeleted?.Invoke(this, new NodeLinkEventArgs(link));
			}

		}

		public void RecordNodeMovement(int nodeID, Point start, Point end)
		{
			redoDirty = true;
			//BaseNodeElement node = idToRONode[nodeID];
			ReadOnlyBaseNode node = idToRONode[nodeID];
			//Point location = new Point(node.Location.X + end.X - start.X, node.Location.Y + end.Y - start.Y);
			undoOperationStack.Add(new GraphOperationData(GraphOperation.MoveNode, start, end, null, new ItemQualityPair(), new RecipeQualityPair(), null, null, node, null));
		}

		public void ResetHistory()
		{
			undoOperationStack.Clear();
			redoOperationStack.Clear();
		}

		public void ClearGraph()
		{
			foreach (BaseNode node in nodes.ToList())
			{
				DeleteNode(node.ReadOnlyNode);
			}

			ResetHistory();
			nodeTypes.Clear();
			idToRONode.Clear();

			SerializeNodeIdSet = null;
			lastNodeID = 0;
		}

		public void UndoOperations()
		{
			// [DONE] need to add flag to all common operations such that we can prevent re-recording stuff we do to Undo into the undo log
			// i.e. without a flag, when you undo an AddNode it will call DeleteNode, which will immediately add DeleteNode to the undo stack
			// [DONE] we want undos and redos to not affect the undo and redo stacks except by transferring between them
			// [DONE] also, when the user does any other action that isn't a undo or redo we need to clear the redo stack as it will become invalid.

			// how to handle aggregate actions? i.e. if the user deletes the graph and presses ctrl z, it should bring back everything

			if (redoDirty)
			{
				// since a new operation was recorded onto the undo
				redoOperationStack.Clear();
				redoDirty = false;
			}
			bool breakLoop = false;
			while (undoOperationStack.Count > 0 && !breakLoop)
			{
				// we have a valid operation to undo

				GraphOperationData last_operation = undoOperationStack.Last();
				undoOperationStack.RemoveAt(undoOperationStack.Count - 1);

				redoOperationStack.Add(last_operation);

				switch (last_operation.operationType)
				{
					case GraphOperation.PseudoStop:
						{
							breakLoop = true;
							break;
						}
					case GraphOperation.CreateNode:
						{
							// added a node, so delete that same node
							DeleteNode(last_operation.node, false);
							break;
						}
					case GraphOperation.DeleteNode:
						{
							// deleted a node, so add it back
							// also need to add back all affected links

							switch (last_operation.nodeType)
							{
								case NodeType.Supplier:
									{

										CreateSupplierNode(last_operation.item, last_operation.location.Value, last_operation.node.NodeID, false);

										break;
									}
								case NodeType.Consumer:
									{
										CreateConsumerNode(last_operation.item, last_operation.location.Value, last_operation.node.NodeID, false);

										break;
									}
								case NodeType.Passthrough:
									{
										CreatePassthroughNode(last_operation.item, last_operation.location.Value, last_operation.node.NodeID, false);

										break;
									}
								case NodeType.Recipe:
									{
										CreateRecipeNode(last_operation.recipe, last_operation.location.Value, last_operation.node.NodeID, false);

										break;
									}
								case NodeType.Plant:
									{
										CreatePlantNode(last_operation.plantProcess, last_operation.item.Quality, last_operation.location.Value, last_operation.node.NodeID, false);

										break;
									}
								case NodeType.Spoil:
									{
										CreateSpoilNode(last_operation.item, last_operation.spoilResult, last_operation.location.Value, last_operation.node.NodeID, false);

										break;
									}
							}

							//foreach (ReadOnlyNodeLink link in last_operation.modifiedLinks)
							//{
							//	CreateLink(link.Supplier, link.Consumer, link.Item, false);
							//}
							break;
						}
					case GraphOperation.MoveNode:
						{
							// undo move
							roToNode[idToRONode[last_operation.node.NodeID]].Location = last_operation.priorLocation.Value;
							movedNodes.Add(idToRONode[last_operation.node.NodeID]);
							break;
						}
					case GraphOperation.CreateLinks:
						{
							// undo create links
							foreach (ReadOnlyNodeLink link in last_operation.modifiedLinks)
							{
								DeleteLink(link, false);
							}
							break;
						}
					case GraphOperation.DeleteLinks:
						{
							// undo delete links
							foreach (ReadOnlyNodeLink link in last_operation.modifiedLinks)
							{
								CreateLink(idToRONode[link.Supplier.NodeID], idToRONode[link.Consumer.NodeID], link.Item, false);
							}
							break;
						}
				}
			}

			UpdateNodeValues();
		}

		public void RedoOperations()
		{
			if (redoDirty)
			{
				// since a new operation was recorded onto the undo
				redoOperationStack.Clear();
				redoDirty = false;
				// early return is just symbolic / to be explicit since the conditional will eval to false anyways
				return;
			}



			bool breakLoop = false;

			if (redoOperationStack.Count > 1 && redoOperationStack.Last().operationType == GraphOperation.PseudoStop)
			{
				// pseudostop on first entry
				// skip it
				GraphOperationData last_operation = redoOperationStack.Last();
				redoOperationStack.RemoveAt(redoOperationStack.Count - 1);
				undoOperationStack.Add(last_operation);
			}


			// proceed as normal


			while (redoOperationStack.Count > 0 && !breakLoop)
			{
				// we have a valid operation to redo
				GraphOperationData last_operation = redoOperationStack.Last();
				redoOperationStack.RemoveAt(redoOperationStack.Count - 1);
				undoOperationStack.Add(last_operation);

				switch (last_operation.operationType)
				{
					case GraphOperation.PseudoStop:
						{
							breakLoop = true;
							break;
						}
					case GraphOperation.DeleteNode:
						{
							// redo deleting a node
							DeleteNode(last_operation.node, false);
							break;
						}
					case GraphOperation.CreateNode:
						{
							// redo adding a node
							switch (last_operation.nodeType)
							{
								case NodeType.Supplier:
									{
										CreateSupplierNode(last_operation.item, last_operation.location.Value, last_operation.node.NodeID, false);

										break;
									}
								case NodeType.Consumer:
									{
										CreateConsumerNode(last_operation.item, last_operation.location.Value, last_operation.node.NodeID, false);

										break;
									}
								case NodeType.Passthrough:
									{
										CreatePassthroughNode(last_operation.item, last_operation.location.Value, last_operation.node.NodeID, false);

										break;
									}
								case NodeType.Recipe:
									{
										CreateRecipeNode(last_operation.recipe, last_operation.location.Value, last_operation.node.NodeID, false);

										break;
									}
								case NodeType.Plant:
									{
										CreatePlantNode(last_operation.plantProcess, last_operation.item.Quality, last_operation.location.Value, last_operation.node.NodeID, false);

										break;
									}
								case NodeType.Spoil:
									{
										CreateSpoilNode(last_operation.item, last_operation.spoilResult, last_operation.location.Value, last_operation.node.NodeID, false);

										break;
									}
							}

							//foreach (ReadOnlyNodeLink link in last_operation.modifiedLinks)
							//{
							//	CreateLink(link.Supplier, link.Consumer, link.Item, false);
							//}
							break;
						}
					case GraphOperation.MoveNode:
						{
							// redo move
							roToNode[idToRONode[last_operation.node.NodeID]].Location = last_operation.location.Value;
							movedNodes.Add(idToRONode[last_operation.node.NodeID]);
							break;
						}
					case GraphOperation.DeleteLinks:
						{
							// redo delete links
							foreach (ReadOnlyNodeLink link in last_operation.modifiedLinks)
							{
								DeleteLink(link, false);
							}
							break;
						}
					case GraphOperation.CreateLinks:
						{
							// redo create links
							foreach (ReadOnlyNodeLink link in last_operation.modifiedLinks)
							{
								CreateLink(idToRONode[link.Supplier.NodeID], idToRONode[link.Consumer.NodeID], link.Item, false);
							}
							break;
						}
				}
			}

			UpdateNodeValues();
		}

		public void UpdateNodeMaxQualities()
		{
			foreach (RecipeNode rnode in nodes.Where(n => n is RecipeNode).Cast<RecipeNode>())
			{
				rnode.UpdateInputsAndOutputs(true);
				rnode.UpdateState();
			}
		}

		public void UpdateNodeStates(bool markAllAsDirty)
		{
			foreach (BaseNode node in nodes)
			{
				node.UpdateState(markAllAsDirty);
			}
		}

		public IEnumerable<ReadOnlyBaseNode> GetSuppliers(ItemQualityPair item)
		{
			foreach (ReadOnlyBaseNode node in Nodes)
			{
				if (node.Outputs.Contains(item))
				{
					yield return node;
				}
			}
		}

		public IEnumerable<ReadOnlyBaseNode> GetConsumers(ItemQualityPair item)
		{
			foreach (ReadOnlyBaseNode node in Nodes)
			{
				if (node.Inputs.Contains(item))
				{
					yield return node;
				}
			}
		}

		public IEnumerable<IEnumerable<ReadOnlyBaseNode>> GetConnectedNodeGroups(bool includeCleanComponents)
		{
			foreach (IEnumerable<BaseNode> group in GetConnectedComponents(includeCleanComponents)) { yield return group.Select(node => node.ReadOnlyNode); }
		}

		private IEnumerable<IEnumerable<BaseNode>> GetConnectedComponents(bool includeCleanComponents) //used to break the graph into groups (in case there are multiple disconnected groups) for simpler solving. Clean components refer to node groups where all the nodes inside the group havent had any changes since last solve operation
		{
			//there is an optimized solution for connected components where we keep track of the various groups and modify them as each node/link is added/removed, but testing shows that this calculation below takes under 1ms even for larg 1000+ node graphs, so why bother.


			HashSet<BaseNode> unvisitedNodes = new HashSet<BaseNode>(nodes);

			List<HashSet<BaseNode>> connectedComponents = new List<HashSet<BaseNode>>();

			while (unvisitedNodes.Any())
			{
				HashSet<BaseNode> newSet = new HashSet<BaseNode>();
				bool allClean = true;

				HashSet<BaseNode> toVisitNext = new HashSet<BaseNode>();
				toVisitNext.Add(unvisitedNodes.First());

				while (toVisitNext.Any())
				{
					BaseNode currentNode = toVisitNext.First();
					allClean &= currentNode.IsClean;

					foreach (NodeLink link in currentNode.InputLinks)
					{
						if (unvisitedNodes.Contains(link.SupplierNode))
						{
							toVisitNext.Add(link.SupplierNode);
						}
					}

					foreach (NodeLink link in currentNode.OutputLinks)
					{
						if (unvisitedNodes.Contains(link.ConsumerNode))
						{
							toVisitNext.Add(link.ConsumerNode);
						}
					}

					newSet.Add(currentNode);
					toVisitNext.Remove(currentNode);
					unvisitedNodes.Remove(currentNode);
				}

				if (!allClean || includeCleanComponents)
				{
					connectedComponents.Add(newSet);
				}
			}
			return connectedComponents;
		}

		public void UpdateNodeValues()
		{
			if (!PauseUpdates)
			{
				try { OptimizeGraphNodeValues(); }
				catch (OverflowException) { } //overflow can theoretically be possible for extremely unbalanced recipes, but with the limit of double and the artificial limit set on max throughput this should never happen.
			}
			NodeValuesUpdated?.Invoke(this, EventArgs.Empty); //called even if no changes have been made in order to re-draw the graph (since something required a node value update - link deletion? node addition? whatever)
		}

		private void LinkChangeUpdateImpactedNodeStates(NodeLink link, LinkType direction) //helper function to update all the impacted nodes after addition/removal of a given link. Basically we want to update any node connected to this link through passthrough nodes (or directly).
		{
			HashSet<NodeLink> visitedLinks = new HashSet<NodeLink>(); //to prevent a loop
			void Internal_UpdateLinkedNodes(NodeLink ilink)
			{
				if (visitedLinks.Contains(ilink))
				{
					return;
				}

				visitedLinks.Add(ilink);

				if (direction == LinkType.Output)
				{
					ilink.ConsumerNode.UpdateState();
					if (ilink.ConsumerNode is PassthroughNode)
					{
						foreach (NodeLink secondaryLink in ilink.ConsumerNode.OutputLinks)
						{
							Internal_UpdateLinkedNodes(secondaryLink);
						}
					}
				}
				else
				{
					ilink.SupplierNode.UpdateState();
					if (ilink.SupplierNode is PassthroughNode)
					{
						foreach (NodeLink secondaryLink in ilink.SupplierNode.InputLinks)
						{
							Internal_UpdateLinkedNodes(secondaryLink);
						}
					}
				}
			}

			Internal_UpdateLinkedNodes(link);
		}

		//----------------------------------------------Save/Load JSON functions

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			//collect the set of nodes and links to be saved (either entire set, or only that which is bound by the specified serialized node list)
			HashSet<BaseNode> includedNodes = nodes;
			HashSet<NodeLink> includedLinks = nodeLinks;
			if (SerializeNodeIdSet != null)
			{
				includedNodes = new HashSet<BaseNode>(nodes.Where(node => SerializeNodeIdSet.Contains(node.NodeID)));
				includedLinks = new HashSet<NodeLink>();
				foreach (NodeLink link in nodeLinks)
				{
					if (includedNodes.Contains(link.ConsumerNode) && includedNodes.Contains(link.SupplierNode))
					{
						includedLinks.Add(link);
					}
				}
			}

			//prepare list of items/assemblers/modules/beacons/recipes that are part of the saved set. Recipes have to include a missing component due to the possibility of different recipes having same name (ex: regular iron.recipe, missing iron.recipe, missing iron.recipe #2)
			HashSet<string> includedItems = new HashSet<string>();

			HashSet<string> includedAssemblers = new HashSet<string>();
			HashSet<string> includedModules = new HashSet<string>();
			HashSet<string> includedBeacons = new HashSet<string>();

			HashSet<Recipe> includedRecipes = new HashSet<Recipe>();
			HashSet<Recipe> includedMissingRecipes = new HashSet<Recipe>(new RecipeNaInPrComparer()); //compares by name, ingredients, and products (not amounts, just items)
			HashSet<PlantProcess> includedPlantProcesses = new HashSet<PlantProcess>();
			HashSet<PlantProcess> includedMissingPlantProcesses = new HashSet<PlantProcess>(new PlantNameIngredientProductComparer());

			HashSet<KeyValuePair<string, int>> includedQualities = new HashSet<KeyValuePair<string, int>>(); //name,level
			includedQualities.Add(new KeyValuePair<string, int>(DefaultAssemblerQuality.Name, DefaultAssemblerQuality.Level));

			foreach (BaseNode node in includedNodes)
			{
				switch (node)
				{
					case RecipeNode rnode:
						if (rnode.BaseRecipe.Recipe.IsMissing)
						{
							includedMissingRecipes.Add(rnode.BaseRecipe.Recipe);
						}
						else
						{
							includedRecipes.Add(rnode.BaseRecipe.Recipe);
						}

						includedAssemblers.Add(rnode.SelectedAssembler.Assembler.Name);

						if (rnode.SelectedBeacon.Beacon != null)
						{
							includedBeacons.Add(rnode.SelectedBeacon.Beacon.Name);
						}

						includedModules.UnionWith(rnode.AssemblerModules.Select(m => m.Module.Name));
						includedModules.UnionWith(rnode.BeaconModules.Select(m => m.Module.Name));

						includedQualities.Add(new KeyValuePair<string, int>(rnode.BaseRecipe.Quality.Name, rnode.BaseRecipe.Quality.Level));
						includedQualities.Add(new KeyValuePair<string, int>(rnode.SelectedAssembler.Quality.Name, rnode.SelectedAssembler.Quality.Level));

						if (rnode.SelectedBeacon.Beacon != null)
						{
							includedQualities.Add(new KeyValuePair<string, int>(rnode.BaseRecipe.Quality.Name, rnode.BaseRecipe.Quality.Level));
						}

						includedQualities.UnionWith(rnode.AssemblerModules.Select(m => new KeyValuePair<string, int>(m.Quality.Name, m.Quality.Level)));
						includedQualities.UnionWith(rnode.BeaconModules.Select(m => new KeyValuePair<string, int>(m.Quality.Name, m.Quality.Level)));
						break;
					case PlantNode pnode:
						if (pnode.BasePlantProcess.IsMissing)
						{
							includedMissingPlantProcesses.Add(pnode.BasePlantProcess);
						}
						else
						{
							includedPlantProcesses.Add(pnode.BasePlantProcess);
						}
						includedQualities.Add(new KeyValuePair<string, int>(pnode.Seed.Quality.Name, pnode.Seed.Quality.Level));
						break;
					case ConsumerNode cnode:
						includedQualities.Add(new KeyValuePair<string, int>(cnode.ConsumedItem.Quality.Name, cnode.ConsumedItem.Quality.Level));
						break;
					case SupplierNode snode:
						includedQualities.Add(new KeyValuePair<string, int>(snode.SuppliedItem.Quality.Name, snode.SuppliedItem.Quality.Level));
						break;
					case PassthroughNode passnode:
						includedQualities.Add(new KeyValuePair<string, int>(passnode.PassthroughItem.Quality.Name, passnode.PassthroughItem.Quality.Level));
						break;
					case SpoilNode spoilnode:
						includedQualities.Add(new KeyValuePair<string, int>(spoilnode.InputItem.Quality.Name, spoilnode.InputItem.Quality.Level));
						break;
				}

				//these will process all inputs/outputs -> so fuel/burnt items are included automatically!
				includedItems.UnionWith(node.Inputs.Select(i => i.Item.Name));
				includedItems.UnionWith(node.Outputs.Select(i => i.Item.Name));
			}
			List<RecipeShort> includedRecipeShorts = includedRecipes.Select(recipe => new RecipeShort(recipe)).ToList();
			includedRecipeShorts.AddRange(includedMissingRecipes.Select(recipe => new RecipeShort(recipe))); //add the missing after the regular, since when we compare saves to preset we will only check 1st recipe of its name (the non-missing kind then)
			List<PlantShort> includedPlantShorts = includedPlantProcesses.Select(pprocess => new PlantShort(pprocess)).ToList();
			includedPlantShorts.AddRange(includedMissingPlantProcesses.Select(pprocess => new PlantShort(pprocess))); //add the missing after the regular, since when we compare saves to preset we will only check 1st recipe of its name (the non-missing kind then)

			//serialize
			info.AddValue("Version", Properties.Settings.Default.ForemanVersion);
			info.AddValue("Object", "ProductionGraph");

			info.AddValue("EnableExtraProductivityForNonMiners", EnableExtraProductivityForNonMiners);
			info.AddValue("DefaultNodeDirection", (int)DefaultNodeDirection);
			info.AddValue("Solver_PullOutputNodes", PullOutputNodes);
			info.AddValue("Solver_PullOutputNodesPower", PullOutputNodesPower);
			info.AddValue("Solver_LowPriorityPower", LowPriorityPower);
			info.AddValue("MaxQualitySteps", MaxQualitySteps);
			info.AddValue("DefaultQuality", DefaultAssemblerQuality.Name);

			info.AddValue("IncludedItems", includedItems);
			info.AddValue("IncludedRecipes", includedRecipeShorts);
			info.AddValue("IncludedPlantProcesses", includedPlantShorts);
			info.AddValue("IncludedAssemblers", includedAssemblers);
			info.AddValue("IncludedModules", includedModules);
			info.AddValue("IncludedBeacons", includedBeacons);
			info.AddValue("IncludedQualities", includedQualities);

			info.AddValue("Nodes", includedNodes);
			info.AddValue("NodeLinks", includedLinks);
		}

		public NewNodeCollection InsertNodesFromJson(DataCache cache, JObject json, bool loadSolverValues) //cache is necessary since we will possibly be adding to mssing items/recipes
		{
			if (json["Version"] == null || (int)json["Version"] != Properties.Settings.Default.ForemanVersion || json["Object"] == null || (string)json["Object"] != "ProductionGraph")
			{
				json = VersionUpdater.UpdateGraph(json, cache);
				if (json == null) //update failed
				{
					return new NewNodeCollection();
				}
			}

			NewNodeCollection newNodeCollection = new NewNodeCollection();
			Dictionary<int, ReadOnlyBaseNode> oldNodeIndices = new Dictionary<int, ReadOnlyBaseNode>(); //the links between the node index (as imported) and the newly created node (which will now have a different index). Used to link up nodes

			try
			{
				//check compliance on all items, assemblers, modules, beacons, and recipes (data-cache will take care of it) - this means add in any missing objects and handle multi-name recipes (there can be multiple versions of a missing recipe, each with identical names)
				cache.ProcessImportedItemsSet(json["IncludedItems"].Select(t => (string)t));
				Dictionary<string, Quality> qualityLinks = cache.ProcessImportedQualitiesSet(json["IncludedQualities"].Select(j => new KeyValuePair<string, int>((string)j["Key"], (int)j["Value"])));
				cache.ProcessImportedAssemblersSet(json["IncludedAssemblers"].Select(t => (string)t));
				cache.ProcessImportedModulesSet(json["IncludedModules"].Select(t => (string)t));
				cache.ProcessImportedBeaconsSet(json["IncludedBeacons"].Select(t => (string)t));
				Dictionary<long, Recipe> recipeLinks = cache.ProcessImportedRecipesSet(RecipeShort.GetSetFromJson(json["IncludedRecipes"]));
				Dictionary<long, PlantProcess> plantProcessLinks = cache.ProcessImportedPlantProcessesSet(PlantShort.GetSetFromJson(json["IncludedPlantProcesses"]));

				if (loadSolverValues)
				{
					EnableExtraProductivityForNonMiners = (bool)json["EnableExtraProductivityForNonMiners"];
					DefaultNodeDirection = (NodeDirection)(int)json["DefaultNodeDirection"];
					PullOutputNodes = (bool)json["Solver_PullOutputNodes"];
					PullOutputNodesPower = (double)json["Solver_PullOutputNodesPower"];
					LowPriorityPower = (double)json["Solver_LowPriorityPower"];
					MaxQualitySteps = (uint)json["MaxQualitySteps"];
					DefaultAssemblerQuality = qualityLinks[(string)json["DefaultQuality"]];
				}

				//add in all the graph nodes
				foreach (JToken nodeJToken in json["Nodes"].ToList())
				{
					BaseNode newNode = null;
					string[] locationString = ((string)nodeJToken["Location"]).Split(',');
					Point location = new Point(int.Parse(locationString[0]), int.Parse(locationString[1]));
					string itemName; //just an early define
					Quality quality; //early define

					switch ((NodeType)(int)nodeJToken["NodeType"])
					{
						case NodeType.Consumer:
							itemName = (string)nodeJToken["Item"];
							quality = qualityLinks[(string)nodeJToken["BaseQuality"]];
							if (cache.Items.ContainsKey(itemName))
							{
								newNode = roToNode[CreateConsumerNode(new ItemQualityPair(cache.Items[itemName], quality), location)];
							}
							else
							{
								newNode = roToNode[CreateConsumerNode(new ItemQualityPair(cache.MissingItems[itemName], quality), location)];
							}
							newNodeCollection.newNodes.Add(newNode.ReadOnlyNode);
							break;
						case NodeType.Supplier:
							itemName = (string)nodeJToken["Item"];
							quality = qualityLinks[(string)nodeJToken["BaseQuality"]];
							if (cache.Items.ContainsKey(itemName))
							{
								newNode = roToNode[CreateSupplierNode(new ItemQualityPair(cache.Items[itemName], quality), location)];
							}
							else
							{
								newNode = roToNode[CreateSupplierNode(new ItemQualityPair(cache.MissingItems[itemName], quality), location)];
							}
							newNodeCollection.newNodes.Add(newNode.ReadOnlyNode);
							break;
						case NodeType.Passthrough:
							itemName = (string)nodeJToken["Item"];
							quality = qualityLinks[(string)nodeJToken["BaseQuality"]];
							if (cache.Items.ContainsKey(itemName))
							{
								newNode = roToNode[CreatePassthroughNode(new ItemQualityPair(cache.Items[itemName], quality), location)];
							}
							else
							{
								newNode = roToNode[CreatePassthroughNode(new ItemQualityPair(cache.MissingItems[itemName], quality), location)];
							}
							((PassthroughNode)newNode).SimpleDraw = (bool)nodeJToken["SDraw"];
							newNodeCollection.newNodes.Add(newNode.ReadOnlyNode);
							break;
						case NodeType.Spoil:
							itemName = (string)nodeJToken["InputItem"];
							string outputItemName = (string)nodeJToken["OutputItem"];
							quality = qualityLinks[(string)nodeJToken["BaseQuality"]];
							Item inputItem = cache.Items.ContainsKey(itemName) ? cache.Items[itemName] : cache.MissingItems[itemName];
							Item outputItem = cache.Items.ContainsKey(outputItemName) ? cache.Items[outputItemName] : cache.MissingItems[outputItemName];
							newNode = roToNode[CreateSpoilNode(new ItemQualityPair(inputItem, quality), outputItem, location)];
							newNodeCollection.newNodes.Add(newNode.ReadOnlyNode);
							break;
						case NodeType.Plant:
							long pprocessID = (long)nodeJToken["PlantProcessID"];
							quality = qualityLinks[(string)nodeJToken["BaseQuality"]];
							newNode = roToNode[CreatePlantNode(plantProcessLinks[pprocessID], quality, location)];
							newNodeCollection.newNodes.Add(newNode.ReadOnlyNode);
							break;
						case NodeType.Recipe:
							long recipeID = (long)nodeJToken["RecipeID"];
							Quality recipeQuality = qualityLinks[(string)nodeJToken["RecipeQuality"]];
							newNode = roToNode[CreateRecipeNode(new RecipeQualityPair(recipeLinks[recipeID], recipeQuality), location, (rNode) =>
							{
								RecipeNodeController rNodeController = (RecipeNodeController)rNode.Controller;

								rNode.LowPriority = (nodeJToken["LowPriority"] != null);

								rNode.NeighbourCount = (double)nodeJToken["Neighbours"];
								rNode.ExtraProductivityBonus = (double)nodeJToken["ExtraProductivity"];

								string assemblerName = (string)nodeJToken["Assembler"];
								Quality assemblerQuality = qualityLinks[(string)nodeJToken["AssemblerQuality"]];
								if (cache.Assemblers.ContainsKey(assemblerName))
								{
									rNodeController.SetAssembler(new AssemblerQualityPair(cache.Assemblers[assemblerName], assemblerQuality));
								}
								else
								{
									rNodeController.SetAssembler(new AssemblerQualityPair(cache.MissingAssemblers[assemblerName], assemblerQuality));
								}

								foreach (JToken module in nodeJToken["AssemblerModules"])
								{
									string moduleName = (string)module["Name"];
									Quality moduleQuality = qualityLinks[(string)module["Quality"]];
									if (cache.Modules.ContainsKey(moduleName))
									{
										rNodeController.AddAssemblerModule(new ModuleQualityPair(cache.Modules[moduleName], moduleQuality));
									}
									else
									{
										rNodeController.AddAssemblerModule(new ModuleQualityPair(cache.MissingModules[moduleName], moduleQuality));
									}
								}

								if (nodeJToken["Fuel"] != null)
								{
									if (cache.Items.ContainsKey((string)nodeJToken["Fuel"]))
									{
										rNodeController.SetFuel(cache.Items[(string)nodeJToken["Fuel"]]);
									}
									else
									{
										rNodeController.SetFuel(cache.MissingItems[(string)nodeJToken["Fuel"]]);
									}
								}
								else if (rNode.SelectedAssembler.Assembler.IsBurner) //and fuel is null... well - its the import. set it as null (and consider it an error)
								{
									rNodeController.SetFuel(null);
								}

								if (nodeJToken["Burnt"] != null)
								{
									Item burntItem;
									if (cache.Items.ContainsKey((string)nodeJToken["Burnt"]))
									{
										burntItem = cache.Items[(string)nodeJToken["Burnt"]];
									}
									else
									{
										burntItem = cache.MissingItems[(string)nodeJToken["Burnt"]];
									}

									if (rNode.FuelRemains != burntItem)
									{
										rNode.SetBurntOverride(burntItem);
									}
								}
								else if (rNode.Fuel != null && rNode.Fuel.BurnResult != null) //same as above - there should be a burn result, but there isnt...
								{
									rNode.SetBurntOverride(null);
								}

								if (nodeJToken["Beacon"] != null)
								{
									string beaconName = (string)nodeJToken["Beacon"];
									Quality beaconQuality = qualityLinks[(string)nodeJToken["BeaconQuality"]];
									if (cache.Beacons.ContainsKey(beaconName))
										rNodeController.SetBeacon(new BeaconQualityPair(cache.Beacons[beaconName], beaconQuality));
									else
										rNodeController.SetBeacon(new BeaconQualityPair(cache.MissingBeacons[beaconName], beaconQuality));

									foreach (JToken module in nodeJToken["BeaconModules"])
									{
										string moduleName = (string)module["Name"];
										Quality moduleQuality = qualityLinks[(string)module["Quality"]];
										if (cache.Modules.ContainsKey(moduleName))
										{
											rNodeController.AddBeaconModule(new ModuleQualityPair(cache.Modules[moduleName], moduleQuality));
										}
										else
										{
											rNodeController.AddBeaconModule(new ModuleQualityPair(cache.MissingModules[moduleName], moduleQuality));
										}
									}

									rNode.BeaconCount = (double)nodeJToken["BeaconCount"];
									rNode.BeaconsPerAssembler = (double)nodeJToken["BeaconsPerAssembler"];
									rNode.BeaconsConst = (double)nodeJToken["BeaconsConst"];
								}

								newNodeCollection.newNodes.Add(rNode.ReadOnlyNode); //done last, so as to catch any errors above first.
							})];
							break;
						default:
							throw new Exception(); //we will catch it right away and delete all nodes added in thus far. Error was most likely in json read, in which case we count it as a corrupt json and not import anything.
					}

					newNode.RateType = (RateType)(int)nodeJToken["RateType"];
					if (newNode.RateType == RateType.Manual)
					{
						newNode.DesiredSetValue = (double)nodeJToken["DesiredSetValue"];
					}

					newNode.NodeDirection = (NodeDirection)(int)nodeJToken["Direction"];

					if (nodeJToken["KeyNode"] != null)
					{
						newNode.KeyNode = true;
						newNode.KeyNodeTitle = (string)nodeJToken["KeyNode"];
					}

					oldNodeIndices.Add((int)nodeJToken["NodeID"], newNode.ReadOnlyNode);
				}

				//link the new nodes
				foreach (JToken nodeLinkJToken in json["NodeLinks"].ToList())
				{
					ReadOnlyBaseNode supplier = oldNodeIndices[(int)nodeLinkJToken["SupplierID"]];
					ReadOnlyBaseNode consumer = oldNodeIndices[(int)nodeLinkJToken["ConsumerID"]];
					ItemQualityPair item;
					Quality quality = qualityLinks[(string)nodeLinkJToken["Quality"]];

					string itemName = (string)nodeLinkJToken["Item"];
					if (cache.Items.ContainsKey(itemName))
					{
						item = new ItemQualityPair(cache.Items[itemName], quality);
					}
					else
					{
						item = new ItemQualityPair(cache.MissingItems[itemName], quality);
					}
					if (LinkChecker.IsPossibleConnection(item, supplier, consumer)) //not necessary to test if connection is valid. It must be valid based on json
					{
						newNodeCollection.newLinks.Add(CreateLink(supplier, consumer, item));
					}
				}
			}
			catch (Exception e) //there was something wrong with the json (probably someone edited it by hand and it didnt link properly). Delete all added nodes and return empty
			{
				ErrorLogging.LogLine(string.Format("Error loading nodes into producton graph! ERROR: {0}", e));
				Console.WriteLine(e);
				DeleteNodes(newNodeCollection.newNodes);
				return new NewNodeCollection();
			}

			return newNodeCollection;
		}
	}
}
