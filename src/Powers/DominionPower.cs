using System.Globalization;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>国度：每个生命阈值只触发一次，本回合提高所有玩家攻击牌的费用。</summary>
public sealed class DominionPower : ModPowerTemplate
{
	public const int CostIncrease = 1;
	private bool _secondForm;
	private int _crossed;

	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("dominion_power");
	public override PowerType Type => PowerType.Buff;
	public override PowerStackType StackType => PowerStackType.Single;
	public override bool ShouldPowerBeRemovedAfterOwnerDeath() => false;
	protected override string SmartDescriptionLocKey => Id.Entry +
		(IsMutable && _secondForm ? ".secondFormDescription" : ".smartDescription");
	protected override IEnumerable<DynamicVar> CanonicalVars =>
		[new DynamicVar("CostIncrease", CostIncrease),
			new ThresholdVar(0),
			new ThresholdVar(1),
			new ThresholdVar(2),
			new EnergyVar(1)
		];

	private int ThresholdCount => _secondForm ? 3 : 1;

	// 判定与说明共用实际最大 HP，含多人缩放；整数生命的“及以下”向下取整。
	private int ThresholdHp(int index) => (int)((long)Owner.MaxHp * (_secondForm ? 3 - index : 2) / 4);

	public void EnterSecondForm()
	{
		AssertMutable();
		_secondForm = true;
		_crossed = 0;
	}

	public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
	{
		if (creature != Owner || delta >= 0 || Owner.MaxHp <= 0 || Owner.CombatState is not { } combat)
		{
			return;
		}

		int crossed = Enumerable.Range(0, ThresholdCount).Count(index => Owner.CurrentHp <= ThresholdHp(index));
		if (crossed <= _crossed)
		{
			return;
		}

		int gained = crossed - _crossed;
		_crossed = crossed; // 先记录全部阈值，避免后续命令重入或治疗后重复触发。
		Flash();
		await PowerCmd.Apply<DominionCostPower>(new ThrowingPlayerChoiceContext(),
			combat.Players.Select(player => player.Creature).Where(target => target.IsAlive),
			gained * CostIncrease, Owner, null);
	}

	private sealed class ThresholdVar(int index) : DynamicVar($"Threshold{index + 1}", 0)
	{
		protected override decimal GetBaseValueForIConvertible() =>
			_owner is DominionPower { IsMutable: true, Owner: not null } power ? power.ThresholdHp(index) : 0;

		public override string ToString() => GetBaseValueForIConvertible().ToString();
	}
}
