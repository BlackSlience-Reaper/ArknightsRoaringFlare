using ArknightsRoaringFlare.Monsters;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Combat.HealthBars;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>两个生命阈值各触发一次：击晕，获得活力，击晕后的下一回合突刺。</summary>
public sealed class WeaknessBreakPower : ModPowerTemplate, IHealthBarForecastSource
{
	private const int ThresholdCount = 2;
	private const int ThresholdDivisor = 3;
	private static readonly Color[] ThresholdColors = [new(0.05f, 0.85f, 1f), new(1f, 0.85f, 0.05f)];
	private int _triggeredThresholds;

	// 战斗内实例状态；原版不会序列化 PowerModel 的 SavedProperty。
	public int TriggeredThresholds
	{
		get => _triggeredThresholds;
		set
		{
			AssertMutable();
			_triggeredThresholds = value;
		}
	}

	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("last_stand_power");

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<VigorPower>(Amount)];

	protected override IEnumerable<DynamicVar> CanonicalVars =>
		[new ThresholdVar()];

	// 判定、能力说明与血条统一使用实际最大 HP，包含多人缩放；整数 HP 的“及以下”向下取整。
	private int ThresholdHp(int index) => (int)((long)Owner.MaxHp * (ThresholdCount - index) / ThresholdDivisor);

	private bool HasTriggered(int index) => (TriggeredThresholds & (1 << index)) != 0;

	public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
	{
		if (creature != Owner || delta >= 0 || Owner.IsDead || Owner.Monster is not ImperialVanguard)
		{
			return;
		}

		int crossed = 0;
		for (int index = 0; index < ThresholdCount; index++)
		{
			if (!HasTriggered(index) && Owner.CurrentHp <= ThresholdHp(index))
			{
				TriggeredThresholds |= 1 << index;
				crossed++;
			}
		}

		if (crossed == 0)
		{
			return;
		}

		// 先记录全部跨过的阈值，避免后续结算重入；同一击晕期间的奖励叠加，共用一次突刺。
		Flash();
		await CreatureCmd.Stun(Owner, VanguardBase.ThrustMoveId);
		await PowerCmd.Apply<VigorPower>(new ThrowingPlayerChoiceContext(), Owner, Amount * crossed, Owner, null);
	}

	public IEnumerable<HealthBarForecastSegment> GetHealthBarForecastSegments(HealthBarForecastContext context)
	{
		if (!IsMutable || context.Creature != Owner || Owner.IsDead)
		{
			yield break;
		}

		for (int index = 0; index < ThresholdCount; index++)
		{
			if (!HasTriggered(index))
			{
				yield return new HealthBarForecastSegment(ThresholdHp(index), ThresholdColors[index],
					HealthBarForecastGrowthDirection.FromLeft, index, null, null,
					HealthBarForecastLeftOriginLayout.OverlapFromOrigin, AffectsHpLabel: false);
			}
		}
	}

	// 在格式化时读取当前实例，最大 HP 变化或克隆后仍显示与判定相同的值。
	private sealed class ThresholdVar() : DynamicVar("Threshold", 0)
	{
		protected override decimal GetBaseValueForIConvertible() =>
			_owner is WeaknessBreakPower { IsMutable: true, Owner: not null } power
				? power.ThresholdHp(power.HasTriggered(0) ? 1 : 0)
				: 0;

		public override string ToString() => GetBaseValueForIConvertible().ToString();
	}
}
