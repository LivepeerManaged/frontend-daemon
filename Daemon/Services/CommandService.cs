﻿using System.Reflection;
using System.Text.Json;
using Autofac;
using Castle.Core;
using Castle.Core.Internal;
using Daemon.Shared.Commands;
using Daemon.Shared.Entities;
using Daemon.Shared.Services;
using NLog;

namespace Daemon.Services;

public class CommandService : ICommandService {
	private IReflectionsService ReflectionsService { get; set; }
	private PluginService PluginService { get; set; }
	private IDaemonService DaemonService { get; set; }

	public object? TriggerCommand(string name, Dictionary<string, JsonElement> parameters) {
		Type? findCommandTypeByName = GetCommandTypeByName(name);

		if (findCommandTypeByName == null)
			throw new Exception("Command not found [replace this with real exception!]");

		ICommand instance = MainApp.Container.InjectUnsetProperties((Activator.CreateInstance(findCommandTypeByName) as ICommand)!);

		return TriggerCommand(BindCommandParameter(instance, parameters));
	}

	private ICommand BindCommandParameter(ICommand command, Dictionary<string, JsonElement> parameters) {
		Dictionary<PropertyInfo, CommandParameterAttribute> propertiesWithAttributes = ReflectionsService.GetPropertiesWithAttributes<CommandParameterAttribute>(command.GetType());

		foreach ((string key, JsonElement value) in parameters.Where(parameterPair => propertiesWithAttributes.Any(valuePair => valuePair.Value.Name == parameterPair.Key))) {
			PropertyInfo propertyInfo = propertiesWithAttributes.First(pair => pair.Value.Name == key).Key;
			propertyInfo.SetValue(command, value.Deserialize(propertyInfo.PropertyType));
		}

		return command;
	}

	public object? TriggerCommand(ICommand command) {
		return command.onCommand();
	}

	public CommandAttribute? GetCommandAttribute(Type type) {
		return ReflectionsService.GetAttributeOfType<CommandAttribute>(type);
	}

	public CommandParameterAttribute[] GetCommandParameters(Type type) {
		return type.GetProperties().Select(p => ReflectionsService.GetAttributeOfProperty<CommandParameterAttribute>(p)).Where(a => a != null).ToArray()!;
	}

	public Type? GetCommandTypeByName(string name) {
		return ReflectionsService.GetAllImplementationsOf<ICommand>().Find(command => ReflectionsService.GetAttributeOfType<CommandAttribute>(command)?.Name == name);
	}

	public KeyValuePair<CommandAttribute, CommandParameterAttribute[]> GetCommandInfoForType(Type type) {
		return new KeyValuePair<CommandAttribute, CommandParameterAttribute[]>(GetCommandAttribute(type), GetCommandParameters(type));
	}

	public Dictionary<AssemblyInfo, Dictionary<CommandAttribute, CommandParameterAttribute[]>> GetAllCommands() {
		Type[] commandTypes = ReflectionsService.GetAllImplementationsOf<ICommand>();
		Dictionary<AssemblyInfo, Dictionary<CommandAttribute, CommandParameterAttribute[]>> commandInfos = new();
		
		foreach (Type commandType in commandTypes) {
			AssemblyInfo assemblyInfo = ReflectionsService.GetAssemblyInfo(commandType.Assembly);
			commandInfos.Add(assemblyInfo, GetCommandsFromPluginAssembly(commandType.Assembly));
		}

		return commandInfos;
	}
	public Dictionary<CommandAttribute, CommandParameterAttribute[]> GetCommandsFromPluginAssembly(Assembly assembly) {
		Type[] commandTypes = ReflectionsService.GetAllImplementationsInAssemblyOf<ICommand>(assembly);
		Dictionary<CommandAttribute, CommandParameterAttribute[]> commandInfos = new();
		
		foreach (Type commandType in commandTypes) {
			commandInfos.Add(GetCommandAttribute(commandType), GetCommandParameters(commandType));
		}

		return commandInfos;
	}
}