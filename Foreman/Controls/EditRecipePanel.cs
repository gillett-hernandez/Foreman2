using Google.OrTools.LinearSolver;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.Linq;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace Foreman
{
	public partial class EditRecipePanel : UserControl
	{
		private static readonly Color ErrorColor = Color.DarkRed;
		private static readonly Color SelectedColor = Color.DarkOrange;

		private List<Button> AssemblerOptions;
		private List<Button> FuelOptions;
		private List<Button> AssemblerModules;
		private List<Button> AModuleOptions;
		private List<Button> BeaconOptions;
		private List<Button> BeaconModules;
		private List<Button> BModuleOptions;

		private Dictionary<object, int> LastScrollY;

		private readonly ProductionGraphViewer myGraphViewer;
		private readonly RecipeNodeController nodeController;
		private readonly ReadOnlyRecipeNode nodeData;

		private double RateMultiplier { get { return myGraphViewer.Graph.GetRateMultipler(); } }
		private string RateName { get { return myGraphViewer.Graph.GetRateName(); } }

		private List<Quality> qualitySelectorIndexSet;

		public EditRecipePanel(ReadOnlyRecipeNode node, ProductionGraphViewer graphViewer)
		{
			nodeData = node;
			nodeController = (RecipeNodeController)graphViewer.Graph.RequestNodeController(node);
			myGraphViewer = graphViewer;
			qualitySelectorIndexSet = new List<Quality>();

			InitializeComponent();
			SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
			RateOptionsTable.AutoSize = false; //simplest way of ensuring the width of the panel remains constant (it needs to be autosized during initialization due to DPI & font scaling)

			KeyNodeCheckBox.Checked = nodeData.KeyNode;
			KeyNodeTitleLabel.Visible = nodeData.KeyNode;
			KeyNodeTitleInput.Visible = nodeData.KeyNode;
			KeyNodeTitleInput.Text = nodeData.KeyNodeTitle;

			LowPriorityCheckBox.Checked = nodeData.LowPriority;

			FixedAssemblerInput.Maximum = (decimal)(node.MaxDesiredSetValue);

			foreach (Quality quality in graphViewer.DCache.AvailableQualities.Where(q => q.Enabled))
			{
				QualitySelector.Items.Add(quality.FriendlyName);
				qualitySelectorIndexSet.Add(quality);
			}

            if (QualitySelector.Items.Count == 1)
                QualitySelector.Enabled = false;
			Quality defQuality = graphViewer.Graph.DefaultAssemblerQuality;
			QualitySelector.SelectedIndex = (qualitySelectorIndexSet.IndexOf(defQuality) == -1) ? 0 : qualitySelectorIndexSet.IndexOf(defQuality);

            if (nodeData.BeaconCount % 1 != 0) BeaconCountInput.DecimalPlaces = 1;
			BeaconCountInput.Value = Math.Min(BeaconCountInput.Maximum, (decimal)nodeData.BeaconCount);
			BeaconsPerAssemblerInput.Value = Math.Min(BeaconsPerAssemblerInput.Maximum, (decimal)nodeData.BeaconsPerAssembler);
			ConstantBeaconInput.Value = Math.Min(ConstantBeaconInput.Maximum, (decimal)nodeData.BeaconsConst);
			NeighbourInput.Value = Math.Min(NeighbourInput.Maximum, (decimal)nodeData.NeighbourCount);
			ExtraProductivityInput.Value = Math.Min(ExtraProductivityInput.Maximum, (decimal)(nodeData.ExtraProductivity * 100));

			AssemblerOptions = new List<Button>();
			FuelOptions = new List<Button>();
			AssemblerModules = new List<Button>();
			AModuleOptions = new List<Button>();
			BeaconOptions = new List<Button>();
			BeaconModules = new List<Button>();
			BModuleOptions = new List<Button>();

			//setup scrolling
			LastScrollY = new Dictionary<object, int>();
			LastScrollY.Add(AssemblerChoicePanel, 0);
			LastScrollY.Add(FuelOptionsPanel, 0);
			LastScrollY.Add(SelectedAModulesPanel, 0);
			LastScrollY.Add(AModulesChoicePanel, 0);
			LastScrollY.Add(BeaconChoicePanel, 0);
			LastScrollY.Add(SelectedBModulesPanel, 0);
			LastScrollY.Add(BModulesChoicePanel, 0);
			AssemblerChoicePanel.MouseWheel += new MouseEventHandler(OptionsPanel_MouseWheel);
			FuelOptionsPanel.MouseWheel += new MouseEventHandler(OptionsPanel_MouseWheel);
			SelectedAModulesPanel.MouseWheel += new MouseEventHandler(OptionsPanel_MouseWheel);
			AModulesChoicePanel.MouseWheel += new MouseEventHandler(OptionsPanel_MouseWheel);
			BeaconChoicePanel.MouseWheel += new MouseEventHandler(OptionsPanel_MouseWheel);
			SelectedBModulesPanel.MouseWheel += new MouseEventHandler(OptionsPanel_MouseWheel);
			BModulesChoicePanel.MouseWheel += new MouseEventHandler(OptionsPanel_MouseWheel);

			UpdateRowHeights(AssemblerChoiceTable);
			UpdateRowHeights(FuelOptionsTable);
			UpdateRowHeights(SelectedAModulesTable);
			UpdateRowHeights(AModulesChoiceTable);
			UpdateRowHeights(BeaconChoiceTable);
			UpdateRowHeights(SelectedBModulesTable);
			UpdateRowHeights(BModulesChoiceTable);

			InitializeRates();
			SetupAssemblerOptions();

			//set these event handlers last - after we have set up all the values / settings
			LowPriorityCheckBox.CheckedChanged += LowPriorityCheckBox_CheckedChanged;
			KeyNodeCheckBox.CheckedChanged += KeyNodeCheckBox_CheckedChanged;
			KeyNodeTitleInput.TextChanged += KeyNodeTitleInput_TextChanged;

			FixedAssemblersOption.CheckedChanged += FixedAssemblerOption_CheckedChanged;
			FixedAssemblerInput.ValueChanged += FixedAssemblerInput_ValueChanged;
			NeighbourInput.ValueChanged += NeighbourInput_ValueChanged;
			ExtraProductivityInput.ValueChanged += ExtraProductivityInput_ValueChanged;
			BeaconCountInput.ValueChanged += BeaconInput_ValueChanged;
			BeaconsPerAssemblerInput.ValueChanged += BeaconInput_ValueChanged;
			ConstantBeaconInput.ValueChanged += BeaconInput_ValueChanged;

            QualitySelector.SelectedIndexChanged += QualitySelector_SelectedIndexChanged;

        }

        private void OptionsPanel_MouseWheel(object sender, MouseEventArgs e)
		{
			//had to set up this slightly convoluted scrolling option to account for mouse wheel events being WAY too fast -> it would skip from start to end in a single tick, potentially missing out several lines worth of items.
			Panel sPanel = sender as Panel;

			if (e.Delta < 0 && LastScrollY[sender] < sPanel.Controls[0].Height - sPanel.Height + 5)
			{
				LastScrollY[sender] += sPanel.Height / 4;
			}
			else if (e.Delta > 0 && LastScrollY[sender] > 0)
			{
				LastScrollY[sender] -= sPanel.Height / 4;
			}

			sPanel.AutoScrollPosition = new Point(0, LastScrollY[sender]);
		}

		private void InitializeRates()
		{
			if (nodeData.RateType == RateType.Auto)
			{
				AutoAssemblersOption.Checked = true;
				FixedAssemblerInput.Enabled = false;
				FixedAssemblerInput.Value = Math.Min(FixedAssemblerInput.Maximum, (decimal)nodeData.ActualSetValue);
			}
			else
			{
				FixedAssemblersOption.Checked = true;
				FixedAssemblerInput.Enabled = true;
				FixedAssemblerInput.Value = Math.Min(FixedAssemblerInput.Maximum, (decimal)nodeData.DesiredSetValue);
			}
			UpdateFixedFlowInputDecimals(FixedAssemblerInput);
		}

		private void SetupAssemblerOptions()
		{
			CleanTable(AssemblerChoiceTable, nodeData.BaseRecipe.Recipe.Assemblers.Count(a => a.Enabled));

			AssemblerOptions.Clear();
			foreach (Assembler assembler in nodeData.BaseRecipe.Recipe.Assemblers.Where(a => a.Enabled))
			{
				Button button = InitializeBaseButton(assembler, qualitySelectorIndexSet[QualitySelector.SelectedIndex]);
				button.Click += new EventHandler(AssemblerButton_Click);

				AssemblerChoiceTable.Controls.Add(button, AssemblerOptions.Count % (AssemblerChoiceTable.ColumnCount - 1), AssemblerOptions.Count / (AssemblerChoiceTable.ColumnCount - 1));
				AssemblerOptions.Add(button);
			}

			UpdateAssembler();
		}

		private void UpdateAssembler()
		{
			//assembler button colors
			foreach (Button abutton in AssemblerOptions)
			{
				abutton.BackColor = ((Assembler)abutton.Tag == nodeData.SelectedAssembler.Assembler && qualitySelectorIndexSet[QualitySelector.SelectedIndex] == nodeData.SelectedAssembler.Quality) ? SelectedColor : (((Assembler)abutton.Tag).IsMissing || !((Assembler)abutton.Tag).Available) ? ErrorColor : AssemblerChoiceTable.BackColor;
			}
			//neighbour count panel
			if (nodeData.SelectedAssembler.Assembler.EntityType != EntityType.Reactor)
			{
				NeighbourInput.Visible = false;
				NeighboursLabel.Visible = false;
			}

			//extra productivity bonus panel
			if(nodeData.SelectedAssembler.Assembler.EntityType != EntityType.Miner && !myGraphViewer.Graph.EnableExtraProductivityForNonMiners)
			{
				ExtraProductivityInput.Visible = false;
				ExtraProductivityLabel.Visible = false;
			}

			//fuel panel
			FuelTitle.Visible = nodeData.SelectedAssembler.Assembler.IsBurner;
			SelectedFuelIcon.Visible = nodeData.SelectedAssembler.Assembler.IsBurner;
			FuelOptionsPanel.Visible = nodeData.SelectedAssembler.Assembler.IsBurner;
			SetupFuelOptions();

			//modules panel
			List<Module> moduleOptions = nodeData.BaseRecipe.Recipe.Modules.Intersect(nodeData.SelectedAssembler.Assembler.Modules).OrderBy(m => m.LFriendlyName).ToList();
			bool showModules = nodeData.SelectedAssembler.Assembler.ModuleSlots > 0 && moduleOptions.Count > 0;
			AModulesLabel.Visible = showModules;
			AModuleOptionsLabel.Visible = showModules;
			SelectedAModulesPanel.Visible = showModules;
			AModulesChoicePanel.Visible = showModules;
			SetupAssemblerModuleOptions();

			//beacon panel
			BeaconTable.Visible = moduleOptions.Count > 0 && myGraphViewer.DCache.Beacons.Count > 0;
			SetupBeaconOptions();
		}

		private void SetupFuelOptions()
		{

			List<Item> fuels = nodeData.SelectedAssembler.Assembler.Fuels.Where(f => f.ProductionRecipes.Any(r => r.Enabled && r.Assemblers.Any(a => a.Enabled))).ToList();

			CleanTable(FuelOptionsTable, fuels.Count);
			FuelOptionsPanel.Height = (int)(FuelOptionsTable.RowStyles[0].Height * (fuels.Count <= 13 ? 1.2 : 2.2));

			FuelOptions.Clear();
			foreach (Item fuel in fuels)
			{
				Button button = InitializeBaseButton(fuel, myGraphViewer.DCache.DefaultQuality);
				button.Click += new EventHandler(FuelButton_Click);

				FuelOptionsTable.Controls.Add(button, FuelOptions.Count % (FuelOptionsTable.ColumnCount - 1), FuelOptions.Count / (FuelOptionsTable.ColumnCount - 1));
				FuelOptions.Add(button);
			}

			UpdateFuel();
		}

		private void UpdateFuel()
		{
			foreach (Button fbutton in FuelOptions)
			{
				fbutton.BackColor = ((Item)fbutton.Tag == nodeData.Fuel) ? SelectedColor : (((Item)fbutton.Tag).IsMissing || !((Item)fbutton.Tag).Available || !((Item)fbutton.Tag).ProductionRecipes.Any(r => r.Available && r.Assemblers.Any(a => a.Available))) ? ErrorColor : FuelOptionsTable.BackColor;
			}
			FuelTitle.Text = string.Format("Fuel: {0}", nodeData.Fuel == null ? "-none-" : nodeData.Fuel.FriendlyName);
			SelectedFuelIcon.Image = nodeData.Fuel?.Icon;

			UpdateAssemblerInfo();
		}

		private void SetupAssemblerModuleOptions()
		{
			List<Module> moduleOptions = nodeData.BaseRecipe.Recipe.Modules.Intersect(nodeData.SelectedAssembler.Assembler.Modules).Where(m => m.Enabled).OrderBy(m => m.LFriendlyName).ToList();

			CleanTable(AModulesChoiceTable, moduleOptions.Count);
			AModuleOptions.Clear();
			for (int i = 0; i < moduleOptions.Count; i++)
			{
				Button button = InitializeBaseButton(moduleOptions[i], qualitySelectorIndexSet[QualitySelector.SelectedIndex]);
				if (!moduleOptions[i].Available)
				{
					button.BackColor = ErrorColor;
				}

				button.MouseUp += new MouseEventHandler(AModuleOptionButton_Click);

				AModulesChoiceTable.Controls.Add(button, AModuleOptions.Count % (AModulesChoiceTable.ColumnCount - 1), AModuleOptions.Count / (AModulesChoiceTable.ColumnCount - 1));
				AModuleOptions.Add(button);
			}

			UpdateAssemblerModules();
		}

		private void UpdateAssemblerModules()
		{
			foreach (Button mbutton in AModuleOptions) {
				mbutton.Enabled = nodeData.AssemblerModules.Count < nodeData.SelectedAssembler.Assembler.ModuleSlots;
			}
			List<Module> moduleOptions = nodeData.BaseRecipe.Recipe.Modules.Intersect(nodeData.SelectedAssembler.Assembler.Modules).OrderBy(m => m.LFriendlyName).ToList();

			CleanTable(SelectedAModulesTable, nodeData.AssemblerModules.Count);

			AssemblerModules.Clear();
			for (int i = 0; i < nodeData.AssemblerModules.Count; i++)
			{
				Button button = InitializeBaseButton(nodeData.AssemblerModules[i].Module, nodeData.AssemblerModules[i].Quality);
				if (nodeData.AssemblerModules[i].Module.IsMissing || !nodeData.AssemblerModules[i].Module.Available || !nodeData.AssemblerModules[i].Module.Enabled || !moduleOptions.Contains(nodeData.AssemblerModules[i].Module) || i >= nodeData.SelectedAssembler.Assembler.ModuleSlots) {
					button.BackColor = ErrorColor;
				}

				button.MouseUp += new MouseEventHandler(AModuleButton_Click);

				SelectedAModulesTable.Controls.Add(button, AssemblerModules.Count % (SelectedAModulesTable.ColumnCount - 1), AssemblerModules.Count / (SelectedAModulesTable.ColumnCount - 1));
				AssemblerModules.Add(button);
			}

			AModulesLabel.Text = string.Format("Modules ({0}/{1}):", nodeData.AssemblerModules.Count, nodeData.SelectedAssembler.Assembler.ModuleSlots);
			UpdateAssemblerInfo();
		}

		private void SetupBeaconOptions()
		{
			CleanTable(BeaconChoiceTable, myGraphViewer.DCache.Beacons.Values.Count(b => b.Enabled));

			BeaconOptions.Clear();
			foreach (Beacon beacon in myGraphViewer.DCache.Beacons.Values.Where(b => b.Enabled))
			{
				Button button = InitializeBaseButton(beacon, qualitySelectorIndexSet[QualitySelector.SelectedIndex]);
				button.Click += new EventHandler(BeaconButton_Click);

				BeaconChoiceTable.Controls.Add(button, BeaconOptions.Count % (BeaconChoiceTable.ColumnCount - 1), BeaconOptions.Count / (BeaconChoiceTable.ColumnCount - 1));
				BeaconOptions.Add(button);
			}

			UpdateBeacon();
		}

		private void UpdateBeacon()
		{
			foreach (Button bbutton in BeaconOptions){
				bbutton.BackColor = (((Beacon)bbutton.Tag) == nodeData.SelectedBeacon.Beacon && qualitySelectorIndexSet[QualitySelector.SelectedIndex] == nodeData.SelectedBeacon.Quality) ? SelectedColor : (((Beacon)bbutton.Tag).IsMissing || !((Beacon)bbutton.Tag).Available) ? ErrorColor : BeaconChoiceTable.BackColor;
			}
			//modules panel
			List<Module> moduleOptions = nodeData.SelectedBeacon.Beacon == null ? new List<Module>() : nodeData.BaseRecipe.Recipe.Modules.Intersect(nodeData.SelectedAssembler.Assembler.Modules).Intersect(nodeData.SelectedBeacon.Beacon.Modules).OrderBy(m => m.LFriendlyName).ToList();
			bool showModules = nodeData.SelectedBeacon.Beacon != null && nodeData.SelectedBeacon.Beacon.ModuleSlots > 0 && moduleOptions.Count > 0;

			BeaconValuesTable.Visible = nodeData.SelectedBeacon.Beacon != null;
			BeaconInfoTable.Visible = nodeData.SelectedBeacon.Beacon != null;

			BModulesLabel.Visible = showModules;
			BModuleOptionsLabel.Visible = showModules;
			SelectedBModulesPanel.Visible = showModules;
			BModulesChoicePanel.Visible = showModules;
			SetupBeaconModuleOptions();

			//beacon values
			if (nodeData.SelectedBeacon.Beacon != null) {
				SetBeaconValues(true);
			}
		}

		private void SetupBeaconModuleOptions()
		{
			List<Module> moduleOptions = nodeData.SelectedBeacon.Beacon == null ? new List<Module>() : nodeData.BaseRecipe.Recipe.Modules.Intersect(nodeData.SelectedAssembler.Assembler.Modules).Intersect(nodeData.SelectedBeacon.Beacon.Modules).Where(m => m.Enabled).OrderBy(m => m.LFriendlyName).ToList();
			int moduleSlots = nodeData.SelectedBeacon.Beacon == null ? 0 : nodeData.SelectedBeacon.Beacon.ModuleSlots;

			CleanTable(BModulesChoiceTable, moduleOptions.Count);
			BModuleOptions.Clear();
			for (int i = 0; i < moduleOptions.Count; i++)
			{
				Button button = InitializeBaseButton(moduleOptions[i], qualitySelectorIndexSet[QualitySelector.SelectedIndex]);
				if (!moduleOptions[i].Available)
				{
					button.BackColor = ErrorColor;
				}

				button.MouseUp += new MouseEventHandler(BModuleOptionButton_Click);

				BModulesChoiceTable.Controls.Add(button, BModuleOptions.Count % (BModulesChoiceTable.ColumnCount - 1), BModuleOptions.Count / (BModulesChoiceTable.ColumnCount - 1));
				BModuleOptions.Add(button);
			}

			UpdateBeaconModules();
		}

		private void UpdateBeaconModules()
		{
			foreach (Button mbutton in BModuleOptions){
				mbutton.Enabled = nodeData.BeaconModules.Count < nodeData.SelectedBeacon.Beacon.ModuleSlots;
			}
			List<Module> moduleOptions = nodeData.SelectedBeacon.Beacon == null ? new List<Module>() : nodeData.BaseRecipe.Recipe.Modules.Intersect(nodeData.SelectedAssembler.Assembler.Modules).Intersect(nodeData.SelectedBeacon.Beacon.Modules).OrderBy(m => m.LFriendlyName).ToList();
			int moduleSlots = nodeData.SelectedBeacon.Beacon == null ? 0 : nodeData.SelectedBeacon.Beacon.ModuleSlots;

			CleanTable(SelectedBModulesTable, nodeData.BeaconModules.Count);

			BeaconModules.Clear();
			for (int i = 0; i < nodeData.BeaconModules.Count; i++)
			{
				Button button = InitializeBaseButton(nodeData.BeaconModules[i].Module, nodeData.BeaconModules[i].Quality);
				if (nodeData.BeaconModules[i].Module.IsMissing || !nodeData.BeaconModules[i].Module.Available || !nodeData.BeaconModules[i].Module.Enabled || !moduleOptions.Contains(nodeData.BeaconModules[i].Module) || i >= moduleSlots) {
					button.BackColor = ErrorColor;
				}

				button.MouseUp += new MouseEventHandler(BModuleButton_Click);

				SelectedBModulesTable.Controls.Add(button, BeaconModules.Count % (SelectedBModulesTable.ColumnCount - 1), BeaconModules.Count / (SelectedBModulesTable.ColumnCount - 1));
				BeaconModules.Add(button);
			}

			BModulesLabel.Text = string.Format("Modules ({0}/{1}):", nodeData.BeaconModules.Count, moduleSlots);

			UpdateBeaconInfo();
			UpdateAssemblerInfo(); //for the impact of the beacon
		}

		private void UpdateAssemblerInfo()
		{
			AssemblerRateLabel.Text = string.Format("# of {0}:", nodeData.SelectedAssembler.Assembler.GetEntityTypeName(true));
			AssemblerTitle.Text = string.Format("{0}: {1}", nodeData.SelectedAssembler.Assembler.GetEntityTypeName(false), nodeData.SelectedAssembler.Assembler.FriendlyName);
			SelectedAssemblerIcon.Image = nodeData.SelectedAssembler.Icon;

			AssemblerEnergyPercentLabel.Text = nodeData.GetConsumptionMultiplier().ToString("P0");
			AssemblerSpeedPercentLabel.Text = nodeData.GetSpeedMultiplier().ToString("P0");
			AssemblerProductivityPercentLabel.Text = nodeData.GetProductivityMultiplier().ToString("P0");
			AssemblerPollutionPercentLabel.Text = nodeData.GetPollutionMultiplier().ToString("P0");
			AssemblerQualityPercentLabel.Text = nodeData.GetQualityMultiplier().ToString("P0");

			bool isAssembler = (nodeData.SelectedAssembler.Assembler.EntityType == EntityType.Assembler || nodeData.SelectedAssembler.Assembler.EntityType == EntityType.Miner || nodeData.SelectedAssembler.Assembler.EntityType == EntityType.OffshorePump);
			AssemblerSpeedTitleLabel.Visible = isAssembler;
			AssemblerSpeedLabel.Visible = isAssembler;
			AssemblerSpeedPercentLabel.Visible = isAssembler;
			AssemblerProductivityTitleLabel.Visible = isAssembler;
			AssemblerProductivityPercentLabel.Visible = isAssembler;
			AssemblerPollutionTitleLabel.Visible= isAssembler;
			AssemblerPollutionPercentLabel.Visible = isAssembler;
			AssemblerQualityTitleLabel.Visible = isAssembler;
			AssemblerQualityPercentLabel.Visible = isAssembler;

			bool isGenerator = nodeData.SelectedAssembler.Assembler.EntityType == EntityType.Generator;
			GeneratorTemperatureLabel.Visible = isGenerator;
			GeneratorTemperatureRangeLabel.Visible = isGenerator;

			AssemblerSpeedLabel.Text = string.Format("{0} ({1} crafts / {2})", nodeData.GetAssemblerSpeed().ToString("0.##"), nodeData.GetTotalCrafts().ToString("0.#"), RateName);

			if (nodeData.SelectedAssembler.Assembler.IsBurner && nodeData.Fuel != null) {
				AssemblerEnergyLabel.Text = string.Format("{0} ({1} fuel / {2})", GraphicsStuff.DoubleToEnergy(nodeData.GetAssemblerEnergyConsumption(), "W"), GraphicsStuff.DoubleToString(nodeData.GetTotalAssemblerFuelConsumption()), RateName);
			}
			else
			{
				AssemblerEnergyLabel.Text = GraphicsStuff.DoubleToEnergy(nodeData.GetAssemblerEnergyConsumption(), "W");
			}

			AssemblerPollutionLabel.Text = string.Format("{0} / min", (nodeData.GetAssemblerPollutionProduction() * 60).ToString("0.##"));

			if (isGenerator)
			{
				double minTemp = nodeData.GetGeneratorMinimumTemperature();
				double maxTemp = nodeData.GetGeneratorMaximumTemperature();
				double operationalTemp = nodeData.SelectedAssembler.Assembler.OperationTemperature;
				double effectivity = nodeData.GetGeneratorEffectivity();

				if (double.IsInfinity(maxTemp))
				{
					GeneratorTemperatureRangeLabel.Text = string.Format("min {0}°c  (optimal: {1}°c)", Math.Round(minTemp, 1).ToString("0.#"), Math.Round(operationalTemp, 1).ToString("0.#"));
				}
				else
				{
					GeneratorTemperatureRangeLabel.Text = string.Format("{0}-{1}°c  (optimal: {2}°c)", Math.Round(minTemp, 1).ToString("0.#"), Math.Round(maxTemp, 1).ToString("0.#"), Math.Round(operationalTemp, 1).ToString("0.#"));
				}

				AssemblerEnergyLabel.Text = GraphicsStuff.DoubleToEnergy(nodeData.GetGeneratorElectricalProduction(), "W");
				AssemblerEnergyPercentLabel.Text = effectivity.ToString("P0");
			}
		}

		private void UpdateBeaconInfo()
		{
			BeaconTitle.Text = string.Format("Beacon: {0}", nodeData.SelectedBeacon.Beacon == null ? "-none-" : nodeData.SelectedBeacon.Beacon.FriendlyName);
			SelectedBeaconIcon.Image = nodeData.SelectedBeacon.Icon;

			BeaconEnergyLabel.Text = nodeData.SelectedBeacon.Beacon == null ? "0J" : GraphicsStuff.DoubleToEnergy(nodeData.GetBeaconEnergyConsumption(), "W");
			BeaconModuleCountLabel.Text = nodeData.SelectedBeacon.Beacon == null ? "0" : nodeData.SelectedBeacon.Beacon.ModuleSlots.ToString();
			BeaconEfficiencyLabel.Text = nodeData.SelectedBeacon.Beacon == null ? "0%" : nodeData.SelectedBeacon.Beacon.GetBeaconEffectivity(nodeData.SelectedBeacon.Quality, nodeData.BeaconCount).ToString("P0");
			TotalBeaconsLabel.Text = nodeData.GetTotalBeacons().ToString();
			TotalBeaconEnergyLabel.Text = nodeData.SelectedBeacon.Beacon == null ? "0J" : GraphicsStuff.DoubleToEnergy(nodeData.GetTotalBeaconElectricalConsumption(), "W");
		}

		//------------------------------------------------------------------------------------------------------Helper functions

		private Button InitializeBaseButton(DataObjectBase obj, Quality quality)
		{
			NFButton button = new NFButton();
			//button.BackColor = RecipeNode.SelectedAssembler == assembler? Color.DarkOrange : assembler.Available? Color.Gray : Color.DarkRed;
			button.ForeColor = Color.Gray;
			button.BackgroundImageLayout = ImageLayout.Zoom;
			button.BackgroundImage = quality == myGraphViewer.DCache.DefaultQuality? obj.Icon : IconCacheProcessor.CombinedQualityIcon(obj.Icon, quality.Icon);
			button.UseVisualStyleBackColor = false;
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderSize = 0;
			button.FlatAppearance.BorderColor = Color.Black;
			button.TabStop = false;
			button.Margin = new Padding(0);
			button.Size = new Size(1, 1);
			button.Dock = DockStyle.Fill;
			button.Tag = obj;
			button.Enabled = true;

			button.MouseHover += new EventHandler(Button_MouseHover);
			button.MouseLeave += new EventHandler(Button_MouseLeave);
			return button;
		}

		private void CleanTable(TableLayoutPanel table, int newCellCount)
		{
			while (table.Controls.Count > 0)
			{
				table.Controls[0].Dispose();
			}

			while (table.RowStyles.Count > 1)
			{
				table.RowStyles.RemoveAt(0);
			}

			for (int i = 0; i < (newCellCount - 1) / (table.ColumnCount - 1); i++)
			{
				table.RowStyles.Add(new RowStyle(table.RowStyles[0].SizeType, table.RowStyles[0].Height));
			}

			table.RowCount = table.RowStyles.Count;
		}

		private void UpdateRowHeights(TableLayoutPanel table)
		{
			int height = (table.Width - (table.RowStyles.Count > 2 ? 20 : 0)) / (table.ColumnCount - 1);
			for (int i = 0; i < table.RowStyles.Count; i++)
			{
				table.RowStyles[i].Height = height;
			}
		}

		private void UpdateFixedFlowInputDecimals(NumericUpDown nud, int max = 4)
		{
			int decimals = MathDecimals.GetDecimals(nud.Value);
			decimals = Math.Min(decimals, max);
			nud.DecimalPlaces = decimals;
		}

		//------------------------------------------------------------------------------------------------------Button clicks

		private void AssemblerButton_Click(object sender, EventArgs e)
		{
			Assembler newAssembler = ((Button)sender).Tag as Assembler;
			Quality quality = qualitySelectorIndexSet[QualitySelector.SelectedIndex];
			nodeController.SetAssembler(new AssemblerQualityPair(newAssembler, quality));
			myGraphViewer.Graph.UpdateNodeValues();
			UpdateAssembler();

		}
		private void FuelButton_Click(object sender, EventArgs e)
		{
			Item newFuel = ((Button)sender).Tag as Item;
			nodeController.SetFuel(newFuel);
			myGraphViewer.Graph.UpdateNodeValues();
			UpdateFuel();
		}
		private void AModuleButton_Click(object sender, MouseEventArgs e)
		{
			if (!new Rectangle(new Point(0, 0), ((Button)sender).Size).Contains(e.Location))
			{
				return;
			}

			ToolTip.Hide((Control)sender);
			int index = AssemblerModules.IndexOf((Button)sender);

			if (e.Button == MouseButtons.Left)
			{
				nodeController.RemoveAssemblerModule(index);
			}
			else if (e.Button == MouseButtons.Right)
			{
				nodeController.RemoveAssemblerModules(nodeData.AssemblerModules[index]);
			}
			else
			{
				return;
			}

			myGraphViewer.Graph.UpdateNodeValues();
			UpdateAssemblerModules();
		}
		private void AModuleOptionButton_Click(object sender, MouseEventArgs e)
		{
			if (!new Rectangle(new Point(0, 0), ((Button)sender).Size).Contains(e.Location))
			{
				return;
			}

			Module newModule = ((Button)sender).Tag as Module;
            Quality quality = qualitySelectorIndexSet[QualitySelector.SelectedIndex];

            if (e.Button == MouseButtons.Left) {
				nodeController.AddAssemblerModule(new ModuleQualityPair(newModule, quality));
			} else if (e.Button == MouseButtons.Right) {
				nodeController.AddAssemblerModules(new ModuleQualityPair(newModule, quality));
			} else
			{
				return;
			}

			myGraphViewer.Graph.UpdateNodeValues();
			UpdateAssemblerModules();
		}
		private void BeaconButton_Click(object sender, EventArgs e)
		{
			Beacon newBeacon = ((Button)sender).Tag as Beacon;
            Quality quality = qualitySelectorIndexSet[QualitySelector.SelectedIndex];
            BeaconQualityPair newBeaconQP = new BeaconQualityPair(newBeacon, quality);

			if (nodeData.SelectedBeacon == newBeaconQP) {
				nodeController.ClearBeacon();
			} else {
				nodeController.SetBeacon(newBeaconQP);
			}
			myGraphViewer.Graph.UpdateNodeValues();
			UpdateBeacon();
		}
		private void BModuleButton_Click(object sender, MouseEventArgs e)
		{
			if (!new Rectangle(new Point(0, 0), ((Button)sender).Size).Contains(e.Location))
			{
				return;
			}

			ToolTip.Hide((Control)sender);
			int index = BeaconModules.IndexOf((Button)sender);

			if (e.Button == MouseButtons.Left)
			{
				nodeController.RemoveBeaconModule(index);
			}
			else if (e.Button == MouseButtons.Right)
			{
				nodeController.RemoveBeaconModules(nodeData.BeaconModules[index]);
			}
			else
			{
				return;
			}

			myGraphViewer.Graph.UpdateNodeValues();
			UpdateBeaconModules();
		}
		private void BModuleOptionButton_Click(object sender, MouseEventArgs e)
		{
			if (!new Rectangle(new Point(0, 0), ((Button)sender).Size).Contains(e.Location))
			{
				return;
			}

			Module newModule = ((Button)sender).Tag as Module;
            Quality quality = qualitySelectorIndexSet[QualitySelector.SelectedIndex];

            if (e.Button == MouseButtons.Left) {
				nodeController.AddBeaconModule(new ModuleQualityPair(newModule, quality));

			} else if (e.Button == MouseButtons.Right) {
				nodeController.AddBeaconModules(new ModuleQualityPair(newModule, quality));
			} else
			{
				return;
			}

			myGraphViewer.Graph.UpdateNodeValues();
			UpdateBeaconModules();
		}

		//------------------------------------------------------------------------------------------------------Button hovers

		private void Button_MouseHover(object sender, EventArgs e)
		{
			Control control = (Control)sender;
			if (control.Tag is Item fuel)
			{
				//the only items in this panel are fuels
				ToolTip.SetText(fuel.FriendlyName + "\nFuel value: " + GraphicsStuff.DoubleToEnergy(fuel.FuelValue, "J"));
				ToolTip.Show(this, Point.Add(PointToClient(Control.MousePosition), new Size(15, 5)));
			}
			else if (control.Tag is DataObjectBase dob)
			{
				ToolTip.SetText(dob.FriendlyName);
				ToolTip.Show(this, Point.Add(PointToClient(Control.MousePosition), new Size(15, 5)));
			}
		}

		private void Button_MouseLeave(object sender, EventArgs e)
		{
			ToolTip.Hide((Control)sender);
		}

		//------------------------------------------------------------------------------------------------------Priority Checkbox
		private void LowPriorityCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			nodeController.SetPriority(LowPriorityCheckBox.Checked);
			myGraphViewer.Graph.UpdateNodeValues();
		}

		//------------------------------------------------------------------------------------------------------Rate input & keynode events

		private void SetFixedRate()
		{
			if (nodeData.DesiredSetValue != (double)FixedAssemblerInput.Value)
			{
				nodeController.SetDesiredSetValue((double)FixedAssemblerInput.Value);
				myGraphViewer.Graph.UpdateNodeValues();

				UpdateAssemblerInfo();
				UpdateBeaconInfo();
			}
		}

		private void FixedAssemblerOption_CheckedChanged(object sender, EventArgs e)
		{
			FixedAssemblerInput.Enabled = FixedAssemblersOption.Checked;
			RateType updatedRateType = (FixedAssemblersOption.Checked) ? RateType.Manual : RateType.Auto;

			if (nodeData.RateType != updatedRateType)
			{
				nodeController.SetRateType(updatedRateType);
				nodeController.SetDesiredSetValue((double)FixedAssemblerInput.Value);
				myGraphViewer.Graph.UpdateNodeValues();

				UpdateAssemblerInfo();
				UpdateBeaconInfo();
			}
		}

		private void FixedAssemblerInput_ValueChanged(object sender, EventArgs e)
		{
			SetFixedRate();
			UpdateFixedFlowInputDecimals(sender as NumericUpDown, 2);
		}

		private void KeyNodeCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			nodeController.SetKeyNode(KeyNodeCheckBox.Checked);
			KeyNodeTitleLabel.Visible = nodeData.KeyNode;
			KeyNodeTitleInput.Visible = nodeData.KeyNode;
			KeyNodeTitleInput.Text = nodeData.KeyNodeTitle;
			myGraphViewer.Invalidate();
		}

		private void KeyNodeTitleInput_TextChanged(object sender, EventArgs e)
		{
			nodeController.SetKeyNodeTitle(KeyNodeTitleInput.Text);
		}

		//------------------------------------------------------------------------------------------------------assembler neighbour bonus input events

		private void SetNeighbourBonus()
		{
			if (nodeData.NeighbourCount != (double)NeighbourInput.Value)
			{
				nodeController.SetNeighbourCount((double)NeighbourInput.Value);
				myGraphViewer.Graph.UpdateNodeValues();

				UpdateAssemblerInfo();
			}
		}

		private void NeighbourInput_ValueChanged(object sender, EventArgs e)
		{
			SetNeighbourBonus();
			UpdateFixedFlowInputDecimals(sender as NumericUpDown, 2);
		}

		//------------------------------------------------------------------------------------------------------assembler extra productivity input events

		private void SetExtraProductivityBonus()
		{
			if (nodeData.ExtraProductivity != (double)ExtraProductivityInput.Value / 100)
			{
				nodeController.SetExtraProductivityBonus((double)ExtraProductivityInput.Value / 100);
				myGraphViewer.Graph.UpdateNodeValues();

				UpdateAssemblerInfo();
			}
		}

		private void ExtraProductivityInput_ValueChanged(object sender, EventArgs e)
		{
			SetExtraProductivityBonus();
		}

		//------------------------------------------------------------------------------------------------------beacon input events

		private void SetBeaconValues(bool graphUpdateRequired)
		{
			if (nodeData.BeaconCount != (double)BeaconCountInput.Value || nodeData.BeaconsPerAssembler != (double)BeaconsPerAssemblerInput.Value || nodeData.BeaconsConst != (double)ConstantBeaconInput.Value)
			{
				nodeController.SetBeaconCount((double)BeaconCountInput.Value);
				nodeController.SetBeaconsPerAssembler((double)BeaconsPerAssemblerInput.Value);
				nodeController.SetBeaconsCont((double)ConstantBeaconInput.Value);

				if (graphUpdateRequired)
				{
					myGraphViewer.Graph.UpdateNodeValues(); //only graph update worthy change is the # of beacons. the others arent as important
				}

				UpdateAssemblerInfo();
				UpdateBeaconInfo();
			}
		}

		private void BeaconInput_ValueChanged(object sender, EventArgs e)
		{
			SetBeaconValues(sender == BeaconCountInput);
			UpdateFixedFlowInputDecimals(sender as NumericUpDown, 2);
		}

        private void QualitySelector_SelectedIndexChanged(object sender, EventArgs e)
        {
			SetupAssemblerOptions();
			SetupAssemblerModuleOptions();
			SetupBeaconOptions();
			SetupBeaconModuleOptions();

			myGraphViewer.Graph.DefaultAssemblerQuality = qualitySelectorIndexSet[QualitySelector.SelectedIndex];
        }
    }
}
