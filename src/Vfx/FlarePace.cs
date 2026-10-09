using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ArknightsRoaringFlare.Vfx;

/// <summary>
/// 本模组怪物出招的节奏：快速模式与普通模式一样，动作从头照原速播完，等到动作里的出手时刻（OnAttack）才结算。
/// 原版快速模式把出招等待压到 min(一半, 0.25 秒)、动画照原速播（CreatureCmd.TriggerAnim）：原版怪物的出手点本来就在 0.3 秒上下，
/// 看不出差别；明日方舟的动作 OnAttack 普遍在 0.6–1.2 秒，照原版会让伤害与命中特效落在蓄力动作上。
/// 试过的两种折中都被否决：动作加速（挥砍显得滑稽）、跳过动作开头的蓄力（用户要求与普通模式一致）。
/// 所以这里把攻击指令里的出手等待取出来，用不随快速模式缩短的 Cmd.Wait 等满；极速模式下 Cmd.Wait 本身不等，与原版一致。
/// 只是表现层的等待，不影响结算顺序与联机同步。
/// </summary>
internal static class FlarePace
{
	private static readonly AccessTools.FieldRef<AttackCommand, float> AnimDelay = AccessTools.FieldRefAccess<AttackCommand, float>("_attackerAnimDelay");
	private static readonly AccessTools.FieldRef<AttackCommand, string?> AnimName = AccessTools.FieldRefAccess<AttackCommand, string?>("_attackerAnimName");
	private static readonly AccessTools.FieldRef<AttackCommand, bool> PlaysAnim = AccessTools.FieldRefAccess<AttackCommand, bool>("_shouldPlayAnimation");

	/// <summary>
	/// 取出攻击指令里原版要等的出手时刻（OnAttack）并清零，交给调用方自己等；没有动画时返回 null。
	/// </summary>
	public static float? TakeHitDelay(AttackCommand attack)
	{
		if (AnimName(attack) is null || !PlaysAnim(attack))
		{
			return null;
		}

		float delay = AnimDelay(attack);
		AnimDelay(attack) = 0f;
		return delay;
	}

	/// <summary>非攻击招式：触发动画，等到 <paramref name="delay"/>（动作里的出手时刻）。</summary>
	public static async Task Animate(Creature creature, string trigger, float delay)
	{
		await CreatureCmd.TriggerAnim(creature, trigger, 0f);
		await Cmd.Wait(delay);
	}
}
