using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using SkillManager;
using UnityEngine;

namespace Mining;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class Mining : BaseUnityPlugin
{
	private const string ModName = "Mining";
	private const string ModVersion = "1.1.1";
	private const string ModGUID = "org.bepinex.plugins.mining";

	private static readonly ConfigSync configSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	private static ConfigEntry<float> miningDamageFactor = null!;
	private static ConfigEntry<float> miningItemYieldFactor = null!;
	private static ConfigEntry<KeyboardShortcut> explosionToggleHotkey = null!;
	private static ConfigEntry<int> explosionMinimumLevel = null!;
	private static ConfigEntry<float> explosionChance = null!;
	private static ConfigEntry<float> experienceGainedFactor = null!;

	private static bool explosiveMining = true;

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	private enum Toggle
	{
		On = 1,
		Off = 0
	}

	private class ConfigurationManagerAttributes
	{
		[UsedImplicitly] public bool? ShowRangeAsPercent;
	}
	
	private static readonly Skill mining = new("Mining", "mining.png");

	public void Awake()
	{
		mining.Description.English("Increases damage done while mining and item yield from ore deposits.");
		mining.Name.German("Bergbau");
		mining.Description.German("Erhöht den Schaden während Bergbau-Aktivitäten und erhöht die Ausbeute von Erzvorkommen.");
		mining.Configurable = false;
		
		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
		configSync.AddLockingConfigEntry(serverConfigLocked);
		miningDamageFactor = config("2 - Mining", "Mining Damage Factor", 3f, new ConfigDescription("Mining damage factor at skill level 100.", new AcceptableValueRange<float>(1f, 10f)));
		miningItemYieldFactor = config("2 - Mining", "Mining Yield Factor", 2f, new ConfigDescription("Mining yield factor at skill level 100.", new AcceptableValueRange<float>(1f, 5f)));
		explosionMinimumLevel = config("2 - Mining", "Mining Explosion Level Requirement", 50, new ConfigDescription("Minimum required skill level to have a chance for the deposit to explode. 0 is disabled.", new AcceptableValueRange<int>(0, 100), new ConfigurationManagerAttributes { ShowRangeAsPercent = false }));
		explosionChance = config("2 - Mining", "Mining Explosion Chance", 1f, new ConfigDescription("Mining explosion chance at skill level 100.", new AcceptableValueRange<float>(0, 100)));
		explosionToggleHotkey = config("2 - Mining", "Toggle Explosive Mining Hotkey", new KeyboardShortcut(KeyCode.T, KeyCode.LeftControl), new ConfigDescription("Shortcut to press to toggle explosion chance off and on. Please note that you have to stand still, to toggle this."), false);
		experienceGainedFactor = config("3 - Other", "Skill Experience Gain Factor", 1f, new ConfigDescription("Factor for experience gained for the mining skill.", new AcceptableValueRange<float>(0.01f, 5f)));
		experienceGainedFactor.SettingChanged += (_, _) => mining.SkillGainFactor = experienceGainedFactor.Value;
		mining.SkillGainFactor = experienceGainedFactor.Value;

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);
	}

	private void Update()
	{
		if (explosionToggleHotkey.Value.IsDown())
		{
			explosiveMining = !explosiveMining;
			Debug.Log($"ExplosiveMining: {explosiveMining}");
			Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, $"Explosive Mining: {explosiveMining}");
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
	public class PlayerAwake
	{
		private static void Postfix(Player __instance)
		{
			__instance.m_nview.Register("Mining IncreaseSkill", (long _, int factor) => __instance.RaiseSkill("Mining", factor));
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.Update))]
	public class PlayerUpdate
	{
		private static void Postfix(Player __instance)
		{
			if (__instance == Player.m_localPlayer)
			{
				__instance.m_nview.GetZDO().Set("Mining Skill Factor", __instance.GetSkillFactor("Mining"));
			}
		}
	}

	[HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyAttack))]
	private class IncreaseDamageDone
	{
		private static void Prefix(SEMan __instance, ref HitData hitData)
		{
			if (__instance.m_character is Player player)
			{
				hitData.m_damage.m_pickaxe *= 1 + player.GetSkillFactor("Mining") * (miningDamageFactor.Value - 1);
			}
		}
	}

	[HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList), typeof(int))]
	private class IncreaseItemYield
	{
		[UsedImplicitly]
		[HarmonyPriority(Priority.VeryLow)]
		private static void Postfix(ref List<GameObject> __result)
		{
			if (!SetMiningFlag.IsMining)
			{
				return;
			}

			List<GameObject> tmp = new();
			foreach (GameObject item in __result)
			{
				float amount = 1 + SetMiningFlag.MiningFactor * (miningItemYieldFactor.Value - 1);

				for (int num = Mathf.FloorToInt(amount + Random.Range(0f, 1f)); num > 0; --num)
				{
					tmp.Add(item);
				}
			}

			__result = tmp;
		}
	}

	[HarmonyPatch(typeof(MineRock5), nameof(MineRock5.RPC_Damage))]
	public static class SetMiningFlag
	{
		public static bool IsMining = false;
		public static float MiningFactor;

		private static bool Prefix(MineRock5 __instance, HitData hit)
		{
			if (hit.GetAttacker() is not Player player)
			{
				return true;
			}
			
			IsMining = true;
			MiningFactor = player.m_nview.GetZDO()?.GetFloat("Mining Skill Factor") ?? 0;

			if (hit.m_toolTier >= __instance.m_minToolTier && hit.m_damage.m_pickaxe > 0 && __instance.m_nview.IsValid() && __instance.m_nview.IsOwner())
			{
				player.m_nview.InvokeRPC("Mining IncreaseSkill", 1);

				if (explosiveMining && explosionMinimumLevel.Value > 0 && MiningFactor >= explosionMinimumLevel.Value / 100f && Random.Range(0f, 1f) <= (MiningFactor - (explosionMinimumLevel.Value - 10) / 100f) / (1 - (explosionMinimumLevel.Value - 10) / 100f) * explosionChance.Value / 100f)
				{
					for (int i = 0; i < __instance.m_hitAreas.Count; ++i)
					{
						MineRock5.HitArea hitArea = __instance.m_hitAreas[i];
						if (hitArea.m_health > 0)
						{
							__instance.DamageArea(i, new HitData
							{
								m_damage = { m_damage = __instance.m_health },
								m_point = hitArea.m_collider.bounds.center,
								m_toolTier = 100,
								m_attacker = hit.m_attacker
							});
						}
					}
					return false;
				}
			}

			return true;
		}

		private static void Finalizer() => IsMining = false;
	}
}
