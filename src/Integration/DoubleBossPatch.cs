using ArknightsRoaringFlare.Acts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace ArknightsRoaringFlare.Integration;

/// <summary>
/// A10 的“双 Boss”（AscensionLevel.DoubleBoss）：原版 RunManager.GenerateRooms 给最后一幕再抽一个第二 Boss，
/// 候选是“本幕 Boss 池 − 主 Boss”。本幕只有塔露拉一个 Boss，候选为空，Rng.NextItem 返回 null，第二 Boss 就没了。
/// 这里在 GenerateRooms 之后补上：从荣耀的 Boss 里随机抽一个当主 Boss（先打），塔露拉改成第二 Boss（地图上排在后面，最后打）。
/// 用的是同一个 UpFront 随机源，联机两端按同样的顺序消耗，结果一致；原版抽空时没有消耗随机数，补抽的这一次只在本幕是最后一幕时发生。
///
/// 只覆盖开局时本幕就在最后一幕的情况。ActLikeIt2 在第三幕开始前换幕时是否重新走这条路，要等实测确认（见设计文档）。
/// </summary>
internal static class DoubleBossPatch
{
	public static void Install(Harmony harmony)
	{
		harmony.Patch(
			AccessTools.Method(typeof(RunManager), nameof(RunManager.GenerateRooms)),
			postfix: new HarmonyMethod(typeof(DoubleBossPatch), nameof(AfterGenerateRooms)));
	}

	private static readonly Func<RunManager, RunState?> GetState =
		AccessTools.MethodDelegate<Func<RunManager, RunState?>>(AccessTools.PropertyGetter(typeof(RunManager), "State"));

	private static void AfterGenerateRooms(RunManager __instance)
	{
		// RunManager.State 是私有属性，按名字取 getter。
		RunState? state = GetState(__instance);
		if (state == null || state.Acts.Count == 0 || !__instance.HasAscension(AscensionLevel.DoubleBoss))
		{
			return;
		}

		if (Apply(state.Acts[^1], state.Rng.UpFront))
		{
			ActModel last = state.Acts[^1];
			Log.Info($"[{ModEntry.ModId}] Double boss: {last.BossEncounter.Id.Entry} first, then {last.SecondBossEncounter!.Id.Entry}.");
		}
	}

	/// <summary>本幕作为双 Boss 的最后一幕时补第二 Boss：荣耀的 Boss 先打、塔露拉后打。自检直接调用它。</summary>
	internal static bool Apply(ActModel last, Rng rng)
	{
		if (last is not RoaringFlareAct || last.HasSecondBoss)
		{
			return false;
		}

		EncounterModel talulah = last.BossEncounter;
		EncounterModel? first = rng.NextItem(ModelDb.Act<Glory>().AllBossEncounters);
		if (first == null)
		{
			return false;
		}

		last.SetBossEncounter(first);
		last.SetSecondBossEncounter(talulah);
		return true;
	}
}
