﻿namespace Daemon.Updater;

public class Updater {
	private const string UPDATER_URL = "";

	public Updater() {
	}

	public bool IsUpdateCompatible() {
		return true;
	}

	public bool HasUpdates() {
		return false;
	}

	public bool ExecuteUpdate() {
		return true;
	}
}