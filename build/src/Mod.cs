using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Buttplug.Client;
using Buttplug.Core;
using Buttplug.Client.Connectors.WebsocketConnector;
using PlayHooky;
using Buttplug.Core.Messages;
using System.Threading.Tasks;

// The title of your mod, as displayed in menus
[assembly: AssemblyTitle("DuckGame Buttplug Client")]

// The author of the mod
[assembly: AssemblyCompany("Bytewave")]

// The description of the mod
[assembly: AssemblyDescription("Allows for Duck Game to control various vibrating sex toys using the Buttplug protocol.")]

// The mod's version
[assembly: AssemblyVersion("1.0.0.0")]

namespace DuckGame.DGButtplugClient {
	public class DGButtplugClient : Mod {
		private static ButtplugWebsocketConnector connector;
		private static ButtplugClient client;
		private static double currentVibePower = 0.0;
		private static bool shouldSendCommands = false;
		private static ButtplugClientDevice device;

		private static void AddVibePower() {
			Debug.Log("[DGBP] Adding vibe power...");
			currentVibePower += 0.2;
			if (currentVibePower > 1.0) {
				currentVibePower = 1.0;
			}
			SendPlugUpdate();
		}

		private static void RemoveVibePower() {
			Debug.Log("[DGBP] Removing vibe power...");
			currentVibePower -= 0.2;
			if (currentVibePower < 0.0) {
				currentVibePower = 0.0;
			}
			SendPlugUpdate();
		}

		private static void SendPlugUpdate() {
			if (!client.Connected || !shouldSendCommands)
				return;

			Debug.Log($"[DGBP] Setting plug vibe power to {currentVibePower}.");

			device.SendVibrateCmd(currentVibePower);
		}

		// The mod's priority; this property controls the load order of the mod.
		public override Priority priority {
			get {
				return Priority.Highest;
			}
		}

		// A lot of this code is ripped from DuckGame.DM.AddPoints
		// PlayHooky currently doesn't support calling the original method, and it's honestly a lot easier to patch this code than run before or after it anyway
		// Please don't sue me, Corptron or Adult Swim
		public static List<Profile> AddPointsHook(DM t) {
			List<Profile> profileList = new List<Profile>();
			List<Team> teamList = new List<Team>();
			List<Team> source = new List<Team>();
			foreach (Team team in Teams.all) {
				foreach (Profile activeProfile in team.activeProfiles) {
					if (activeProfile.duck != null && !activeProfile.duck.dead) {
						if (activeProfile.duck.converted != null && activeProfile.duck.converted.profile.team != activeProfile.team) {
							if (!source.Contains(activeProfile.duck.converted.profile.team))
								source.Add(activeProfile.duck.converted.profile.team);
							if (!teamList.Contains(activeProfile.duck.profile.team)) {
								teamList.Add(activeProfile.duck.profile.team);
								break;
							}
							break;
						}
						if (!source.Contains(team)) {
							source.Add(team);
							break;
						}
						break;
					}
				}
			}
			if (source.Count <= 1 && source.Count > 0) {
				source.AddRange((IEnumerable<Team>) teamList);
				byte winteam = 4;
				List<int> idxs = new List<int>();
				GameMode.lastWinners.Clear();
				bool hasSetVibePower = false;
				foreach (Team team in source) {
					foreach (Profile activeProfile in team.activeProfiles) {
						if (activeProfile.duck != null && !activeProfile.duck.dead) {
							FieldInfo fEditorTestMode = t.GetType().GetField("_editorTestMode", BindingFlags.NonPublic | BindingFlags.Instance);
							bool _editorTestMode = (bool) fEditorTestMode.GetValue(t);
							if (!_editorTestMode) {
								if (Teams.active.Count > 1 && Network.isActive && activeProfile.connection == DuckNetwork.localConnection) {
									DuckNetwork.GiveXP("Rounds Won", 1, 4, 4, 10, 20, 9999999);
									if (!hasSetVibePower) {
										Debug.Log("[DGBP] Local player won!");
										RemoveVibePower();
										hasSetVibePower = true;
									}
								}
								activeProfile.stats.lastWon = DateTime.Now;
								++activeProfile.stats.matchesWon;
							}
							profileList.Add(activeProfile);
							Profile p = activeProfile;
							if (activeProfile.duck.converted != null) {
								p = activeProfile.duck.converted.profile;
								winteam = p.networkIndex;
							}
							GameMode.lastWinners.Add(activeProfile);
							PlusOne plusOne = new PlusOne(0.0f, 0.0f, p, false);
							plusOne.anchor = (Anchor) ((Thing) activeProfile.duck);
							plusOne.anchor.offset = new Vec2(0.0f, -16f);
							idxs.Add((int) activeProfile.duck.netProfileIndex);
							Level.Add((Thing) plusOne);
						}
					}
				}
				if (Network.isActive && Network.isServer)
					Send.Message((NetMessage) new NMAssignWin(idxs, winteam));
				++source.First<Team>().score;
				if (!hasSetVibePower && Network.isActive) {
					Debug.Log("[DGBP] Remote player won!");
					AddVibePower();
				}
			}
			return profileList;
		}

		// This function is run before all mods are finished loading.
		protected override void OnPreInitialize() {
			base.OnPreInitialize();

			Debug.Log("[DGBP] PreInit");

			Assembly duckGame = typeof(Mod).Assembly; // Get the Duck Game assembly reference
			Type tGameMode = duckGame.GetType("DuckGame.DM"); // Get the DM class
			MethodInfo mDoAddPoints = tGameMode.GetMethod("AddPoints", BindingFlags.NonPublic | BindingFlags.Instance); // Find the AddPoints method
			HookManager manager = new HookManager();
			manager.Hook(mDoAddPoints, typeof(DGButtplugClient).GetMethod("AddPointsHook")); // Redirect DM.AddPoints to our method
			connector = new ButtplugWebsocketConnector(new Uri("ws://localhost:6969/buttplug"));
			client = new ButtplugClient("DG Buttplug", connector);

			try {
				client.ConnectAsync().Wait();
				client.StartScanningAsync().Wait();
				Task.Delay(5000).Wait();
				client.StopScanningAsync().Wait();
			} catch (Exception ex) {
				Debug.Log("[DGBP] Buttplug error. Restart your game if you want your plug to work!");
				Debug.Log($"[DGBP] {ex.Message}");
			}
		}

		// This function is run after all mods are loaded.
		protected override void OnPostInitialize() {
			base.OnPostInitialize();
			Debug.Log("[DGBP] PostInit");
			if (client.Connected) {
				if (client.Devices.Any()) {
					foreach (var dev in client.Devices) {
						foreach (var msgInfo in dev.AllowedMessages) {
							if (msgInfo.Key == typeof(VibrateCmd) && dev.Name != "XBox Compatible Gamepad (XInput)") {
								device = dev;
								shouldSendCommands = true;
								break;
							}
						}

						if (device != null)
							break;
					}
				} else {
					Debug.Log("[DGBP] No devices found.");
				}
			}
		}
	}
}