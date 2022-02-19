using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using JetBrains.Annotations;
using SkillManager;
using UnityEngine;

namespace Mining;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class Mining : BaseUnityPlugin
{
	private const string ModName = "Mining";
	private const string ModVersion = "1.0.0";
	private const string ModGUID = "org.bepinex.plugins.mining";

	private static readonly Skill mining = new("Mining", "mining.png");

	public void Awake()
	{
		mining.Description.English("Increases damage done while mining and item yield from ore deposits.");
		mining.Name.German("Bergbau");
		mining.Description.German("Erhöht den Schaden während Bergbau-Aktivitäten und erhöht die Ausbeute von Erzvorkommen.");
		mining.Configurable = true;

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);
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
				hitData.m_damage.m_pickaxe *= 1 + player.GetSkillFactor("Mining") * 2;
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
				float amount = 1 + SetMiningFlag.MiningFactor;

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
			IsMining = true;
			MiningFactor = hit.GetAttacker()?.m_nview.GetZDO()?.GetFloat("Mining Skill Factor") ?? 0;

			if (hit.m_toolTier >= __instance.m_minToolTier && hit.m_damage.m_pickaxe > 0 && __instance.m_nview.IsValid() && __instance.m_nview.IsOwner())
			{
				hit.GetAttacker().m_nview.InvokeRPC("Mining IncreaseSkill", 1);

				if (MiningFactor > 0.5 && Random.Range(0f, 1f) <= (MiningFactor - 0.4) * 0.02)
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
