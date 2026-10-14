using ArknightsRoaringFlare.Powers;
using ArknightsRoaringFlare.Vfx;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 着铠术师一族共用的外观、攻击音效与移动动画。
/// </summary>
public abstract class ArmoredCasterBase : FlareMonster
{
	// 原作音效（audio_data.json：enemy_1110_uamord.attack 出手 e_atk_magic_n、法术命中 e_imp_magic_n）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		_ => new(On: "e_atk_magic_n", Hit: "e_imp_magic_n"),
	};

	// 原作特效（enemy_1110_uamord：施法 enemy_uamord_attack_01_start 在枪口（第 28 根骨骼 Muzzle），法术弹
	// projectile_enemy_uamord 拖尾 enemy_uamord_attack_01_trail、命中 enemy_uamord_attack_01_hit）。_2 骨架的骨骼序号相同。
	protected override AkFxRig Rig => new(0.27f, new Godot.Vector2(0f, 1.6f));

	protected override FlareFx FxFor(string slot) => new(
		// 原作挂点组件：起手在单位根节点（脚下）；子弹从 Muzzle（本地 skel 28 号）射出。
		Start: [new("enemy_uamord_attack_01_start", FxAnchor.Feet)],
		Hit: [new("enemy_uamord_attack_01_hit")],
		Projectile: "enemy_uamord_attack_01_trail",
		ProjectileBone: 28);

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", AttackAnimation, "Die", "Move_Loop"];
	protected override float AttackHitDelay => 0.633f;

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		CreatureAnimator animator = BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
		animator.AddAnyState(ReinforcementEntrance.RunTrigger, new AnimState("Move_Loop", isLooping: true));
		return animator;
	}
}

/// <summary>咒法 → 火焰，随后仪式准备 → 仪式循环。</summary>
public sealed class UrsusArmoredCaster : ArmoredCasterBase
{
	public const string HexMoveId = "HEX_MOVE";
	public const string FlameMoveId = "FLAME_MOVE";
	public const string PrepareMoveId = "PREPARE_MOVE";
	public const string RitualMoveId = "RITUAL_MOVE";
	private const int ArmorAmount = 12;
	private const int InitialArtsAmount = 1;
	private const int RitualIncrease = 1;
	private const int RitualHits = 2;
	private int _artsAmount = InitialArtsAmount;

	public override string SceneName => "ursus_armored_caster";
	public override int MinInitialHp => ToughValue(62, 59);
	public override int MaxInitialHp => ToughValue(66, 63);
	private int RitualDamage => DeadlyValue(6, 5);

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<OriginiumArmorPower>([Creature], ArmorAmount);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState hex = new(HexMoveId, HexMove, new CardDebuffIntent());
		MoveState flame = new(FlameMoveId, FlameMove, new DebuffIntent());
		MoveState prepare = new(PrepareMoveId, _ => Task.CompletedTask, new UnknownIntent());
		MoveState ritual = new(RitualMoveId, RitualMove, new MultiAttackIntent(RitualDamage, RitualHits), new CardDebuffIntent(), new DebuffIntent());
		hex.FollowUpState = flame;
		flame.FollowUpState = prepare;
		prepare.FollowUpState = ritual;
		ritual.FollowUpState = prepare;
		return Machine([hex, flame, prepare, ritual], hex);
	}

	private async Task HexMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		foreach (Creature target in targets.Where(c => c.IsAlive))
		{
			int previous = target.GetPower<OriginiumHexPower>()?.ContributionOf(Creature) ?? 0;
			if (_artsAmount > previous)
			{
				await PowerCmd.Apply<OriginiumHexPower>(new ThrowingPlayerChoiceContext(), target, _artsAmount - previous, Creature, null);
			}
		}
	}

	private async Task FlameMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await ApplyPower<OriginiumFlamePower>(targets, _artsAmount + 1);
	}

	private async Task RitualMove(IReadOnlyList<Creature> targets)
	{
		await Strike(RitualDamage, RitualHits, ArtsVfx, ArtsHitSfx);
		if (Creature.IsDead)
		{
			return;
		}
		_artsAmount += RitualIncrease;
		await HexMove(targets);
		await FlameMove(targets);
	}
}

/// <summary>源石法术开场一次，随后法术连击 → 仪式准备 → 仪式循环。</summary>
public sealed class SeniorArmoredCaster : ArmoredCasterBase
{
	public const string ArtsBoltMoveId = "ARTS_BOLT_MOVE";
	public const string ArtsComboMoveId = "ARTS_COMBO_MOVE";
	public const string PrepareMoveId = "PREPARE_MOVE";
	public const string RitualMoveId = "RITUAL_MOVE";
	private const int ArmorAmount = 20;
	private const int FrostAmount = 1;
	private const int InitialFlameAmount = 2;
	private const int RitualFlameIncrease = 2;
	private const int ComboHits = 5;
	private const int ComboStrength = 1;
	private int _flameAmount = InitialFlameAmount;

	public override string SceneName => "senior_armored_caster";
	public override int MinInitialHp => ToughValue(143, 140);
	public override int MaxInitialHp => ToughValue(147, 144);
	private int ArtsComboDamage => DeadlyValue(4, 3);
	private int RitualDamage => DeadlyValue(20, 19);

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<OriginiumArmorPower>([Creature], ArmorAmount);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState bolt = new(ArtsBoltMoveId, ArtsBoltMove, new CardDebuffIntent(), new DebuffIntent());
		MoveState combo = new(ArtsComboMoveId, ArtsComboMove, new MultiAttackIntent(ArtsComboDamage, ComboHits), new BuffIntent());
		MoveState prepare = new(PrepareMoveId, _ => Task.CompletedTask, new UnknownIntent());
		MoveState ritual = new(RitualMoveId, RitualMove, new SingleAttackIntent(RitualDamage), new CardDebuffIntent(), new DebuffIntent());
		bolt.FollowUpState = combo;
		combo.FollowUpState = prepare;
		prepare.FollowUpState = ritual;
		ritual.FollowUpState = combo;
		return Machine([bolt, combo, prepare, ritual], bolt);
	}

	private async Task ArtsBoltMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await FreezeSkills(targets);
		await ApplyPower<OriginiumFlamePower>(targets, _flameAmount);
	}

	private async Task FreezeSkills(IReadOnlyList<Creature> targets)
	{
		foreach (Creature target in targets.Where(c => c.IsAlive))
		{
			if (target.GetPower<OriginiumFrostPower>() is { } frost)
			{
				await frost.FreezeAllCards(Creature);
			}
			else
			{
				await PowerCmd.Apply<OriginiumFrostPower>(new ThrowingPlayerChoiceContext(), target, FrostAmount, Creature, null);
			}
		}
	}

	private async Task ArtsComboMove(IReadOnlyList<Creature> targets)
	{
		await Strike(ArtsComboDamage, ComboHits, ArtsVfx, ArtsHitSfx);
		if (Creature.IsAlive)
		{
			await ApplyStrengthToSelf(ComboStrength);
		}
	}

	private async Task RitualMove(IReadOnlyList<Creature> targets)
	{
		await Strike(RitualDamage, vfx: ArtsVfx, sfx: ArtsHitSfx);
		if (Creature.IsAlive)
		{
			_flameAmount += RitualFlameIncrease;
			await FreezeSkills(targets);
			await ApplyPower<OriginiumFlamePower>(targets, _flameAmount);
		}
	}
}
