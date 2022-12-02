local e_table = {}

G_global_index = 0
local function generate_index()
	G_global_index = G_global_index + 1
	return G_global_index - 1
end

local function ExportLocalisedString(lstring, index)
	-- as could be expected if lstring doesnt have a working translation then we get a beauty of a mess... so that needs to be cleaned up outside of json table
	localised_print('<#~#>')
	localised_print(lstring)
	localised_print('<#~#>')
end

local function ProcessTemperature(temperature)
	if temperature == nil then
		return nil
	elseif temperature == -math.huge then
		return -1e100
	elseif temperature == math.huge then
		return 1e100
	else
		return temperature
	end
end

local function ExportModList()
	local t_mods = {}
	table.insert(t_mods, { ['name'] = 'core', ['version'] = '1.0' })

	for name, version in pairs(game.active_mods) do
		table.insert(t_mods, { ['name'] = name, ['version'] = version })
	end
	e_table['mods'] = t_mods
end

local function ExportResearch()
	local t_technologies = {}
	for _, tech in pairs(game.technology_prototypes) do
		local t_tech = {}
		t_tech['name'] = tech.name
		t_tech['icon_name'] = 'icon.t.' .. tech.name
		t_tech['enabled'] = tech.enabled
		t_tech['hidden'] = tech.hidden

		t_tech['prerequisites'] = {}
		for pname, _ in pairs(tech.prerequisites) do
			table.insert(t_tech['prerequisites'], pname)
		end

		t_tech['recipes'] = {}
		for _, effect in pairs(tech.effects) do
			if (effect.type == 'unlock-recipe') then
				table.insert(t_tech['recipes'], effect.recipe)
			end
		end

		t_tech['research_unit_ingredients'] = {}
		for _, ingredient in pairs(tech.research_unit_ingredients) do
			local t_ingredient = {}
			t_ingredient['name'] = ingredient.name
			t_ingredient['amount'] = ingredient.amount
			table.insert(t_tech['research_unit_ingredients'], t_ingredient)
		end
		t_tech['research_unit_count'] = tech.research_unit_count

		local index = generate_index()
		t_tech['lid'] = '$' .. index
		ExportLocalisedString(tech.localised_name, index)


		table.insert(t_technologies, t_tech)
	end
	e_table['technologies'] = t_technologies
end

local function ExportRecipes()
	local t_recipes = {}
	for _, recipe in pairs(game.recipe_prototypes) do
		local t_recipe = {}
		t_recipe['name'] = recipe.name
		t_recipe['icon_name'] = 'icon.r.' .. recipe.name
		if recipe.main_product then
			t_recipe["icon_alt_name"] = 'icon.i.' .. recipe.main_product.name
		elseif recipe.products[1] then
			t_recipe["icon_alt_name"] = 'icon.i.' .. recipe.products[1].name
		else
			t_recipe["icon_alt_name"] = 'icon.r.' .. recipe.name
		end

		t_recipe['enabled'] = recipe.enabled
		t_recipe['category'] = recipe.category
		t_recipe['energy'] = recipe.energy
		t_recipe['order'] = recipe.order
		t_recipe['subgroup'] = recipe.subgroup.name

		t_recipe['ingredients'] = {}
		for _, ingredient in pairs(recipe.ingredients) do
			local t_ingredient = {}
			t_ingredient['name'] = ingredient.name
			t_ingredient['type'] = ingredient.type
			t_ingredient['amount'] = ingredient.amount
			if ingredient.type == 'fluid' and ingredient.minimum_temperature ~= nil then
				t_ingredient['minimum_temperature'] = ProcessTemperature(ingredient.minimum_temperature)
			end
			if ingredient.type == 'fluid' and ingredient.maximum_temperature ~= nil then
				t_ingredient['maximum_temperature'] = ProcessTemperature(ingredient.maximum_temperature)
			end
			table.insert(t_recipe['ingredients'], t_ingredient)
		end

		t_recipe['products'] = {}
		for _, product in pairs(recipe.products) do
			local t_product = {}
			t_product['name'] = product.name
			t_product['type'] = product.type

			local amount = (product.amount == nil) and ((product.amount_max + product.amount_min) / 2) or product.amount
			amount = amount * product.probability

			t_product['amount'] = amount
			t_product['p_amount'] = amount

			if (product.catalyst_amount ~= nil) then

				if product.amount ~= nil then
					t_product['p_amount'] = product.amount - math.max(0, math.min(product.amount, product.catalyst_amount))
				elseif product.catalyst_amount <= product.amount_min then
					t_product['p_amount'] = ((product.amount_max + product.amount_min) / 2) - math.max(0, product.catalyst_amount)
				else
					local catalyst_amount = math.min(product.amount_max, product.catalyst_amount)
					t_product['p_amount'] = ((product.amount_max - catalyst_amount) * (product.amount_max + 1 - catalyst_amount) / 2) /
						(product.amount_max + 1 - product.amount_min)
				end

				t_product['p_amount'] = t_product['p_amount'] * product.probability

			elseif product.amount ~= nil and product.probability == 1 then
				for _, ingredient in pairs(recipe.ingredients) do
					if ingredient.name == product.name then
						t_product['p_amount'] = math.max(0, product.amount - ingredient.amount)
					end
				end
			end

			if product.type == 'fluid' and product.temperature ~= nil then
				t_product['temperature'] = ProcessTemperature(product.temperature)
			end
			table.insert(t_recipe['products'], t_product)
		end

		local index = generate_index()
		t_recipe['lid'] = '$' .. index
		ExportLocalisedString(recipe.localised_name, index)

		table.insert(t_recipes, t_recipe)
	end
	e_table['recipes'] = t_recipes
end

local function ExportItems()
	local t_items = {}
	for _, item in pairs(game.item_prototypes) do
		local t_item = {}
		t_item['name'] = item.name
		t_item['icon_name'] = 'icon.i.' .. item.name
		t_item['order'] = item.order
		t_item['subgroup'] = item.subgroup.name
		t_item["stack"] = item.stackable and item.stack_size or 1

		if item.fuel_category ~= nil then
			t_item['fuel_category'] = item.fuel_category
			t_item['fuel_value'] = item.fuel_value
			t_item['pollution_multiplier'] = item.fuel_emissions_multiplier
		end

		if item.burnt_result ~= nil then
			t_item['burnt_result'] = item.burnt_result.name
		end

		if item.rocket_launch_products ~= nil and item.rocket_launch_products[1] ~= nil then
			t_item['launch_products'] = {}
			for _, product in pairs(item.rocket_launch_products) do
				local t_product = {}
				t_product['name'] = product.name
				t_product['type'] = product.type

				local amount = (product.amount == nil) and ((product.amount_max + product.amount_min) / 2) or product.amount
				amount = amount * ((product.probability == nil) and 1 or product.probability)

				t_product['amount'] = amount

				if product.type == 'fluid' and product.temperature ~= nil then
					t_product['temperature'] = ProcessTemperature(product.temperature)
				end
				table.insert(t_item['launch_products'], t_product)
			end
		end


		local index = generate_index()
		t_item['lid'] = '$' .. index
		ExportLocalisedString(item.localised_name, index)


		table.insert(t_items, t_item)
	end
	e_table['items'] = t_items
end

local function ExportFluids()
	local t_fluids = {}
	for _, fluid in pairs(game.fluid_prototypes) do
		local t_fluid = {}
		t_fluid['name'] = fluid.name
		t_fluid['icon_name'] = 'icon.i.' .. fluid.name
		t_fluid['order'] = fluid.order
		t_fluid['subgroup'] = fluid.subgroup.name
		t_fluid['default_temperature'] = ProcessTemperature(fluid.default_temperature)
		t_fluid['max_temperature'] = ProcessTemperature(fluid.max_temperature)
		t_fluid['heat_capacity'] = fluid.heat_capacity == nil and 0 or fluid.heat_capacity

		if fluid.fuel_value ~= 0 then
			t_fluid['fuel_value'] = fluid.fuel_value
			t_fluid['pollution_multiplier'] = fluid.emissions_multiplier
		end


		local index = generate_index()
		t_fluid['lid'] = '$' .. index
		ExportLocalisedString(fluid.localised_name, index)


		table.insert(t_fluids, t_fluid)
	end
	e_table['fluids'] = t_fluids
end

local function ExportModules()
	local t_modules = {}
	for _, module in pairs(game.item_prototypes) do
		if module.module_effects ~= nil then
			local t_module = {}
			t_module['name'] = module.name
			t_module['icon_name'] = 'icon.e.' .. module.name
			t_module["icon_alt_name"] = 'icon.i.' .. module.name
			t_module['order'] = module.order
			t_module['category'] = module.category
			t_module['tier'] = module.tier

			t_module['module_effects_consumption'] = (module.module_effects.consumption == nil) and 0 or
				module.module_effects.consumption.bonus
			t_module['module_effects_speed'] = (module.module_effects.speed == nil) and 0 or module.module_effects.speed.bonus
			t_module['module_effects_productivity'] = (module.module_effects.productivity == nil) and 0 or
				module.module_effects.productivity.bonus
			t_module['module_effects_pollution'] = (module.module_effects.pollution == nil) and 0 or
				module.module_effects.pollution.bonus

			t_module['limitations'] = {}
			for _, recipe in pairs(module.limitations) do
				table.insert(t_module['limitations'], recipe)
			end

			local index = generate_index()
			t_module['lid'] = '$' .. index
			ExportLocalisedString(module.localised_name, index)


			table.insert(t_modules, t_module)
		end
	end
	e_table['modules'] = t_modules
end

local function ExportEntities()
	local t_entities = {}
	for _, entity in pairs(game.entity_prototypes) do --select any entity with an energy source (or fluid -> offshore pump). we will sort them out later. BONUS: also grab the 'character' entity - for those hand-crafts
		if entity.type == 'boiler' or entity.type == 'generator' or entity.type == 'reactor' or entity.type == 'mining-drill'
			or entity.type == 'offshore-pump' or entity.type == 'furnace' or entity.type == 'assembling-machine' or
			entity.type == 'beacon' or entity.type == 'rocket-silo' or entity.type == 'burner-generator' or
			entity.type == "character" then
			local t_entity = {}
			t_entity['name'] = entity.name
			t_entity['icon_name'] = 'icon.e.' .. entity.name
			t_entity["icon_alt_name"] = 'icon.i.' .. entity.name
			t_entity['order'] = entity.order
			t_entity['type'] = entity.type

			if entity.next_upgrade ~= nil then t_entity['next_upgrade'] = entity.next_upgrade.name end

			if entity.crafting_speed ~= nil then t_entity['speed'] = entity.crafting_speed
			elseif entity.mining_speed ~= nil then t_entity['speed'] = entity.mining_speed
			elseif entity.pumping_speed ~= nil then t_entity['speed'] = entity.pumping_speed end

			if entity.fluid ~= nil then t_entity['fluid_product'] = entity.fluid.name end
			if entity.fluid_usage_per_tick ~= nil then t_entity['fluid_usage_per_tick'] = entity.fluid_usage_per_tick end

			if entity.module_inventory_size ~= nil then t_entity['module_inventory_size'] = entity.module_inventory_size end
			if entity.base_productivity ~= nil then t_entity['base_productivity'] = entity.base_productivity end
			if entity.distribution_effectivity ~= nil then t_entity['distribution_effectivity'] = entity.distribution_effectivity end
			if entity.neighbour_bonus ~= nil then t_entity['neighbour_bonus'] = entity.neighbour_bonus end
			--ingredient_count is depreciated

			t_entity['associated_items'] = {}
			if entity.items_to_place_this ~= nil then
				for _, item in pairs(entity.items_to_place_this) do
					if (type(item) == 'string') then
						table.insert(t_entity['associated_items'], item)
					else
						table.insert(t_entity['associated_items'], item['name'])
					end
				end
			end

			t_entity['allowed_effects'] = {}
			if entity.allowed_effects then
				for effect, allowed in pairs(entity.allowed_effects) do
					if allowed then
						table.insert(t_entity['allowed_effects'], effect)
					end
				end
			end

			if entity.crafting_categories ~= nil then
				t_entity['crafting_categories'] = {}
				for category, _ in pairs(entity.crafting_categories) do
					table.insert(t_entity['crafting_categories'], category)
				end
			end

			if entity.resource_categories ~= nil then
				t_entity['resource_categories'] = {}
				for category, _ in pairs(entity.resource_categories) do
					table.insert(t_entity['resource_categories'], category)
				end
			end

			--fluid boxes for input/output of boiler & generator need to be processed (almost guaranteed to be 'steam' and 'water', but... tests have shown that we can heat up whatever we want)
			--additinally we want count of fluid boxes in/out (for checking recipe validity)
			if entity.type == 'boiler' then
				t_entity['target_temperature'] = ProcessTemperature(entity.target_temperature)

				if entity.fluidbox_prototypes[1].filter ~= nil then
					t_entity['fluid_ingredient'] = entity.fluidbox_prototypes[1].filter.name
				end
				if entity.fluidbox_prototypes[2].filter ~= nil then
					t_entity['fluid_product'] = entity.fluidbox_prototypes[2].filter.name
				end
			elseif entity.type == 'generator' then
				t_entity['full_power_temperature'] = ProcessTemperature(entity.maximum_temperature)

				t_entity['minimum_temperature'] = ProcessTemperature(entity.fluidbox_prototypes[1].minimum_temperature)
				t_entity['maximum_temperature'] = ProcessTemperature(entity.fluidbox_prototypes[1].maximum_temperature)
				if entity.fluidbox_prototypes[1].filter ~= nil then
					t_entity['fluid_ingredient'] = entity.fluidbox_prototypes[1].filter.name
				end
			else
				local inPipes = 0
				local inPipeFilters = {}
				local ioPipes = 0
				local ioPipeFilters = {}
				local outPipes = 0
				local outPipeFilters = {}
				-- i will ignore temperature limitations for this. (this is for recipe checks)

				for _, fbox in pairs(entity.fluidbox_prototypes) do
					if fbox.production_type == 'input' then
						inPipes = inPipes + 1
						if fbox.filter ~= nil then table.insert(inPipeFilters, fbox.filter.name) end
					elseif fbox.production_type == 'output' then
						outPipes = outPipes + 1
						if fbox.filter ~= nil then table.insert(outPipeFilters, fbox.filter.name) end
					elseif fbox.production_type == 'input-output' then
						ioPipes = ioPipes + 1
						if fbox.filter ~= nil then table.insert(ioPipeFilters, fbox.filter.name) end
					end
				end
				t_entity['in_pipes'] = inPipes
				t_entity['in_pipe_filters'] = inPipeFilters
				t_entity['out_pipes'] = outPipes
				t_entity['out_pipe_filters'] = outPipeFilters
				t_entity['io_pipes'] = ioPipes
				t_entity['io_pipe_filters'] = ioPipeFilters
			end

			t_entity['max_energy_usage'] = (entity.max_energy_usage == nil) and 0 or entity.max_energy_usage
			t_entity['energy_usage'] = (entity.energy_usage == nil) and 0 or entity.energy_usage
			t_entity['energy_production'] = entity.max_energy_production

			if entity.burner_prototype ~= nil then
				t_entity['fuel_type'] = 'item'
				t_entity['fuel_effectivity'] = entity.burner_prototype.effectivity
				t_entity['pollution'] = entity.burner_prototype.emissions

				t_entity['fuel_categories'] = {}
				for fname, _ in pairs(entity.burner_prototype.fuel_categories) do
					table.insert(t_entity['fuel_categories'], fname)
				end

			elseif entity.fluid_energy_source_prototype then
				t_entity['fuel_type'] = 'fluid'
				t_entity['fuel_effectivity'] = entity.fluid_energy_source_prototype.effectivity
				t_entity['pollution'] = entity.fluid_energy_source_prototype.emissions
				t_entity['burns_fluid'] = entity.fluid_energy_source_prototype.burns_fluid

				--fluid limitations from fluid box:
				if entity.fluid_energy_source_prototype.fluid_box.filter ~= nil then
					t_entity['fuel_filter'] = entity.fluid_energy_source_prototype.fluid_box.filter.name
				end
				t_entity['minimum_fuel_temperature'] = ProcessTemperature(entity.fluid_energy_source_prototype.fluid_box.minimum_temperature) -- nil is accepted
				t_entity['maximum_fuel_temperature'] = ProcessTemperature(entity.fluid_energy_source_prototype.fluid_box.maximum_temperature) --nil is accepted

			elseif entity.electric_energy_source_prototype then
				t_entity['fuel_type'] = 'electricity'
				t_entity['fuel_effectivity'] = 1
				t_entity['drain'] = entity.electric_energy_source_prototype.drain
				t_entity['pollution'] = entity.electric_energy_source_prototype.emissions

			elseif entity.heat_energy_source_prototype then
				t_entity['fuel_type'] = 'heat'
				t_entity['fuel_effectivity'] = 1
				t_entity['pollution'] = entity.heat_energy_source_prototype.emissions

			elseif entity.void_energy_source_prototype then
				t_entity['fuel_type'] = 'void'
				t_entity['fuel_effectivity'] = 1
				t_entity['pollution'] = entity.void_energy_source_prototype.emissions
			else
				t_entity['fuel_type'] = 'void'
				t_entity['fuel_effectivity'] = 1
				t_entity['pollution'] = 0
			end


			local index = generate_index()
			t_entity['lid'] = '$' .. index
			ExportLocalisedString(entity.localised_name, index)


			table.insert(t_entities, t_entity)
		end
	end
	e_table['entities'] = t_entities
end

local function ExportResources()
	local t_resources = {}
	for _, resource in pairs(game.entity_prototypes) do
		if resource.resource_category ~= nil then
			local t_resource = {}
			t_resource['name'] = resource.name
			t_resource['resource_category'] = resource.resource_category
			t_resource['mining_time'] = resource.mineable_properties.mining_time
			if resource.mineable_properties.required_fluid then
				t_resource['required_fluid'] = resource.mineable_properties.required_fluid
				t_resource['fluid_amount'] = resource.mineable_properties.fluid_amount / 10.0
			end
			t_resource['name'] = resource.name

			t_resource['products'] = {}

			-- temporarily fix #40.
			-- see https://github.com/pyanodon/pybugreports/issues/118
			-- and https://lua-api.factorio.com/latest/LuaEntityPrototype.html#LuaEntityPrototype.mineable_properties
			if resource.mineable_properties.products ~= nil then
				for _, product in pairs(resource.mineable_properties.products) do
					local tproduct = {}
					tproduct['name'] = product.name
					tproduct['type'] = product.type

					local amount = (product.amount == nil) and ((product.amount_max + product.amount_min) / 2) or product.amount
					amount = amount * ((product.probability == nil) and 1 or product.probability)
					tproduct['amount'] = amount

					if product.type == 'fluid' and product.temperature ~= nil then
						tproduct['temperature'] = ProcessTemperature(product.temperature)
					end
					table.insert(t_resource['products'], tproduct)
				end
			end

			local index = generate_index()
			t_resource['lid'] = '$' .. index
			ExportLocalisedString(resource.localised_name, index)


			table.insert(t_resources, t_resource)
		end
	end
	e_table['resources'] = t_resources
end

local function ExportGroups()
	local t_groups = {}
	for _, group in pairs(game.item_group_prototypes) do
		local t_group = {}
		t_group['name'] = group.name
		t_group['icon_name'] = 'icon.g.' .. group.name
		t_group['order'] = group.order

		t_group['subgroups'] = {}
		for _, subgroup in pairs(group.subgroups) do
			table.insert(t_group['subgroups'], subgroup.name)
		end

		local index = generate_index()
		t_group['lid'] = '$' .. index
		ExportLocalisedString(group.localised_name, index)


		table.insert(t_groups, t_group)
	end
	e_table['groups'] = t_groups
end

local function ExportSubGroups()
	local t_subgroups = {}
	for _, sgroup in pairs(game.item_subgroup_prototypes) do
		local t_subgroup = {}
		t_subgroup['name'] = sgroup.name
		t_subgroup['order'] = sgroup.order

		table.insert(t_subgroups, t_subgroup)
	end
	e_table['subgroups'] = t_subgroups
end

script.on_nth_tick(1,
	function()

		game.difficulty_settings.recipe_difficulty = defines.difficulty_settings.recipe_difficulty.normal
		game.difficulty_settings.technology_difficulty = defines.difficulty_settings.technology_difficulty.expensive
		e_table['difficulty'] = { 0, 1 }

		localised_print('<<<START-EXPORT-LN>>>')

		ExportModList()
		ExportResearch()
		ExportRecipes()
		ExportItems()
		ExportFluids()
		ExportModules()
		ExportEntities()
		ExportResources()
		ExportGroups()
		ExportSubGroups()

		localised_print('<<<END-EXPORT-LN>>>')

		localised_print('<<<START-EXPORT-P2>>>')
		localised_print(game.table_to_json(e_table))
		localised_print('<<<END-EXPORT-P2>>>')

		ENDEXPORTANDJUSTDIE() -- just the most safe way of ensuring that we export once and quit. Basically... there is no ENDEXPORTANDJUSTDIE function. so lua will throw an expection and the run will end here.
	end
)
