﻿using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Autofac;
using Daemon.Communication;
using Daemon.Shared.Basic;
using Daemon.Shared.Communication.Attributes;
using Daemon.Shared.Plugins;

namespace Daemon.Plugins;

/// <summary>
/// This is the plugin manager which handles the whole loading with the plugins
/// </summary>
public class PluginManager : BaseClass {
	private readonly List<DaemonPlugin> loadedPlugins;

	/// <summary>
	/// Constructor of the pluginmanager
	/// </summary>
	public PluginManager(ContainerBuilder containerBuilder) {
		this.loadedPlugins = new List<DaemonPlugin>();
		this.ContainerBuilder = containerBuilder;
	}

	public ContainerBuilder ContainerBuilder { get; set; }

	private byte[] GetHash(string file) {
		using MD5 md5 = MD5.Create();
		using FileStream stream = File.OpenRead(file);
		return md5.ComputeHash(stream);
	}
	
	/// <summary>
	/// This methods loads the plugins out of the plugins folder.
	/// </summary>
	public void LoadPlugins() {
		Dictionary<FileInfo, byte[]> assembliesToLoad = new Dictionary<FileInfo, byte[]>();
		//List<FileInfo> assembliesToLoad = new List<FileInfo>();
		List<Assembly> loadedAssemblies = new List<Assembly>();
		DirectoryInfo pluginDirectory = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "plugins"));

		if (!pluginDirectory.Exists) {
			this.Logger.Info("Creating Plugins folder...");
			pluginDirectory.Create();
		}

		foreach (FileInfo assemblyFile in pluginDirectory.GetFiles("*.plugin.dll", SearchOption.AllDirectories)) {
			byte[] hash = GetHash(assemblyFile.FullName);

			if (assembliesToLoad.ContainsValue(hash))
				continue;

			this.Logger.Debug("Found Plugin \"{0}\" [{1}]", assemblyFile.Name, BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant());

			assembliesToLoad.Add(assemblyFile, hash);
		}


		foreach (FileInfo foundDll in assembliesToLoad.Keys) {
			this.Logger.Debug("Try to load Assembly {0}", foundDll.Name);

			try {
				Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(foundDll.FullName);

				var types = assembly.GetTypes()
					.Where(t => t.GetCustomAttribute<EventTypeAttribute>() != null || t.BaseType == typeof(DaemonPlugin) ||
					            t.GetMethods().Any(x => x.GetCustomAttribute<OnEventAttribute>() != null));
				foreach (var type in types) {
					ContainerBuilder.RegisterType(type).AsSelf();
				}

				loadedAssemblies.Add(assembly);

				this.Logger.Debug("Successfully loaded the Assembly {0}", assembly.GetName());
			} catch (Exception e) {
				this.Logger.Fatal("During the load of assembly {0} an error occured", e, foundDll.Name);
			}
		}

		IContainer container = this.ContainerBuilder.Build();

		foreach (Assembly currentAssembly in loadedAssemblies) {
			this.Logger.Debug("Try to load Plugin {0}", currentAssembly.FullName);

			try {
				Type plugin = currentAssembly.GetTypes().FirstOrDefault(type => type.BaseType == typeof(DaemonPlugin), null);

				if (plugin == null) {
					continue;
				}

				this.Logger.Debug("Try to load Plugin \"{0}\"", currentAssembly.FullName);

				this.loadedPlugins.Add((DaemonPlugin)Activator.CreateInstance(plugin)!);

				this.Logger.Debug($"Try to load the events in {currentAssembly.FullName}");

				foreach (Type currentClass in currentAssembly.GetTypes()) {
					foreach (MethodInfo currentMethod in currentClass.GetMethods()) {
						if (currentMethod.CustomAttributes.All(x => x.AttributeType != typeof(OnEventAttribute)))
							continue;

						this.Logger.Debug($"Try to load Event {currentMethod.Name}");

						OnEventAttribute eventAttribute = ((OnEventAttribute)currentMethod.GetCustomAttribute(typeof(OnEventAttribute))!);

						if (!currentMethod.IsPublic || !currentMethod.IsStatic) {
							this.Logger.Warn($"Method {currentMethod.Name} is not public or static. Using Experminetal Loading!");

							if (!container.IsRegistered(typeof(EventService))) {
								this.Logger.Error("The EventService isn't registered");
								continue;
							}

							if (!container.IsRegistered(currentClass)) {
								this.Logger.Error($"The {currentClass.FullName} isnt registered!");
								continue;
							}

							container.Resolve<EventService>().RegisterEvent(eventAttribute.EventClass,
								(@event) => currentMethod.Invoke(container.Resolve(currentMethod.DeclaringType), new[] { @event }));
						} else {
							container.Resolve<EventService>().RegisterEvent(eventAttribute.EventClass,
								(@event) => currentMethod.Invoke(null, new[] { @event }));
						}

						this.Logger.Debug($"Successfully registered Event {currentMethod.Name}");
					}
				}

				this.Logger.Info("Successfully loaded Plugin {0}", currentAssembly.GetName(false));
			} catch (Exception e) {
				this.Logger.Fatal("During the load of plugin {0} an error occured", e, currentAssembly.FullName);
			}
		}

		foreach (DaemonPlugin daemonPlugin in this.loadedPlugins) {
			this.Logger.Debug("Try to execute the LoadMethod");

			try {
				daemonPlugin.OnPluginLoad(container);

				this.Logger.Info("Successfully started Plugin {0}", daemonPlugin.GetType().FullName ?? "Not Found");
			} catch (Exception e) {
				this.Logger.Fatal("During the start of plugin {0}, an error occured", e, daemonPlugin.GetType().FullName ?? "Not Found");
			}
		}

		this.Logger.Info("Loaded and started {0} plugins", this.loadedPlugins.Count);
	}

	/// <summary>
	/// This methods
	/// </summary>
	public void UnloadPlugins() {
		foreach (var currentPlugin in this.loadedPlugins) {
			currentPlugin.OnPluginDisable();
		}
	}
}