using ArknightsRoaringFlare.Vfx;
using ArknightsRoaringFlare.Powers;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 帝国前锋一族共用远射、近战的动画与音效；精锐使用弱点破坏，百战精锐使用身经百战与四招循环。
/// </summary>
public abstract class VanguardBase : FlareMonster
{
	public const string LongShotMoveId = "LONG_SHOT_MOVE";
	public const string ThrustMoveId = "THRUST_MOVE";
	public const string ResolveMoveId = "RESOLVE_MOVE";

	// 原作音效（audio_data.json：enemy_1113_empace.combat 出手 e_atk_bigaxe_n、命中 e_imp_bigaxe_n，.attack 起手 e_imp_mousek_n、远射命中 e_imp_reinforcement）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		RangedHitSfx => new(Start: "e_imp_mousek_n", Hit: "e_imp_reinforcement"),
		_ => new(On: "e_atk_bigaxe_n", Hit: "e_imp_bigaxe_n"),
	};

	// 原作特效（enemy_1113_empace：近战 Combat 起手 enemy_empace_attack_01_start、武器闪光 _start_02、命中 enemy_empace_attack_01_hit；
	// 远射 Attack 起手 enemy_empace_attack_02_start、子弹 projectile_enemy_empace 拖尾 enemy_empace_attack_02_trail；
	// 背水天赋的 enemy_empace_eye_buff 在眼睛）。Graphic 缩放 0.30。刺击与闪光对到出手瞬间（近战 Attack_2 的 OnAttack 1.033 秒、
	// 远射 Attack_1_Loop 的 0.5 秒）。挂点照原作挂点组件（tools/ak_vfx/attach.py）：两个起手在单位根节点（脚下），
	// _start_02 在 SpecialPoint0 即武器骨骼 C_L_Weapon_2，朝向取单位朝向（挂点组件 A=10，与根节点特效同一模式；跟着骨骼转时刺光反向朝后）；子弹从枪口 C_Muzzle 射出。两套骨架的骨骼序号不同，由子类给。
	protected override AkFxRig Rig => new(0.30f, new Godot.Vector2(0f, 1.6f));

	/// <summary>枪口骨骼 C_Muzzle 在本地 skel 里的序号。</summary>
	protected abstract int MuzzleBone { get; }

	/// <summary>武器骨骼 C_L_Weapon_2（原作 SpecialPoint0）在本地 skel 里的序号。</summary>
	protected abstract int WeaponBone { get; }

	/// <summary>眼睛骨骼 C_Eye 在本地 skel 里的序号。</summary>
	protected abstract int EyeBone { get; }

	protected override FlareFx FxFor(string slot) => slot switch
	{
		RangedHitSfx => new(
			Start: [new("enemy_empace_attack_02_start", FxAnchor.Feet, Delay: 0.45f), new("enemy_empace_attack_01_start_02", FxAnchor.Bone, WeaponBone, 0.45f, Spin: false)],
			Hit: [new("enemy_empace_attack_01_hit")],
			Projectile: "enemy_empace_attack_02_trail",
			ProjectileBone: MuzzleBone),
		_ => new(
			// 近战起手的刺光几何在 −x（刺尖 −0.38、爆点 −1.4），与同怪射击起手（火光在 +0.46）及其他怪的起手相反，镜像取反才在身前；
			// 原作数据里找不到区分两者的字段（都是挂点模式 A=10、带 rotation_y），是对照预览逐个定的。
			Start: [new("enemy_empace_attack_01_start", FxAnchor.Feet, Delay: 0.98f, Flip: true), new("enemy_empace_attack_01_start_02", FxAnchor.Bone, WeaponBone, 0.98f, Spin: false)],
			Hit: [new("enemy_empace_attack_01_hit")]),
	};

	protected override FxCue[]? BuffFx => [new("enemy_empace_eye_buff", FxAnchor.Bone, EyeBone)];

	// 前锋的骨骼没有 Attack：近战用单段的 Attack_2（OnAttack 1.033 秒），远射用 Attack_1_Loop（0.5 秒）。
	private const string ShotTrigger = "Shot";

	protected override string AttackAnimation => "Attack_2";

	protected override float AttackHitDelay => 1.033f;

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", AttackAnimation, "Attack_1_Loop", "Die"];

	protected abstract Task ApplyStartingPower();

	protected abstract int LongShotDamage { get; }

	protected virtual int LongShotHits => 1;

	protected abstract int ThrustDamage { get; }

	protected abstract MoveState CreateResolveMove();

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyStartingPower();
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState shot = new(LongShotMoveId, LongShotMove, new SingleAttackIntent(LongShotDamage));
		MoveState thrust = new(ThrustMoveId, targets => Strike(ThrustDamage, vfx: SlashVfx, sfx: HeavyHitSfx), new SingleAttackIntent(ThrustDamage));
		MoveState resolve = CreateResolveMove();
		shot.FollowUpState = thrust;
		thrust.FollowUpState = resolve;
		resolve.FollowUpState = shot;
		return Machine([shot, thrust, resolve], shot);
	}

	protected Task LongShotMove(IReadOnlyList<Creature> targets) =>
		Cued(DamageCmd.Attack(LongShotDamage).FromMonster(this)
			.WithHitCount(LongShotHits)
			.WithAttackerAnim(ShotTrigger, 0.5f)
			.WithHitFx(BluntVfx), RangedHitSfx)
			.Execute(null);

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation), (ShotTrigger, "Attack_1_Loop"));
}

/// <summary>帝国前锋精锐：远射开场，2→1 循环；弱点破坏插入击晕与突刺后续接常规循环。</summary>
public sealed class ImperialVanguard : VanguardBase
{
	private bool _resumeWithResolve;

	// 与原版怪物状态一致，只随当前战斗实例保留。
	public bool ResumeWithResolve
	{
		get => _resumeWithResolve;
		set
		{
			AssertMutable();
			_resumeWithResolve = value;
		}
	}

	public override string SceneName => "imperial_vanguard";

	protected override int MuzzleBone => 24;

	protected override int WeaponBone => 23;

	protected override int EyeBone => 36;

	public override int MinInitialHp => ToughValue(136, 133);

	public override int MaxInitialHp => ToughValue(140, 137);

	private int WeaknessBreakVigor => DeadlyValue(15, 10);

	protected override int LongShotDamage => DeadlyValue(4, 3);

	protected override int LongShotHits => 3;

	protected override int ThrustDamage => DeadlyValue(20, 19);

	private int ResolveBlock => DeadlyValue(12, 10);

	private int ResolveStrength => DeadlyValue(2, 1);

	protected override Task ApplyStartingPower() => ApplyPower<WeaknessBreakPower>([Creature], WeaknessBreakVigor);

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState resolve = CreateResolveMove();
		MoveState shot = new(LongShotMoveId, LongShotCycleMove, new MultiAttackIntent(LongShotDamage, LongShotHits));
		MoveState thrust = new(ThrustMoveId, targets => Strike(ThrustDamage, vfx: SlashVfx, sfx: HeavyHitSfx), new SingleAttackIntent(ThrustDamage));
		ConditionalBranchState resume = new("RESUME_REGULAR_MOVE");
		resume.AddState(resolve, () => ResumeWithResolve);
		resume.AddState(shot, () => !ResumeWithResolve);
		resolve.FollowUpState = shot;
		shot.FollowUpState = resolve;
		thrust.FollowUpState = resume;
		return Machine([resolve, shot, thrust, resume], shot);
	}

	private Task LongShotCycleMove(IReadOnlyList<Creature> targets)
	{
		// 出手时推进常规循环，击晕和突刺不推进；攻击中反伤触发也能续接下一招。
		ResumeWithResolve = true;
		return LongShotMove(targets);
	}

	protected override MoveState CreateResolveMove() =>
		new(ResolveMoveId, ResolveMove, new DefendIntent(), new BuffIntent());

	private async Task ResolveMove(IReadOnlyList<Creature> targets)
	{
		ResumeWithResolve = false;
		BuffCue();
		await GainBlock(ResolveBlock);
		await ApplyStrengthToSelf(ResolveStrength);
	}
}

/// <summary>帝国前锋百战精锐：单体强敌，突刺→远射→战意→弹反；未格挡伤害延迟至自身回合结束。</summary>
public sealed class VanguardVeteran : VanguardBase
{
	public const string ParryMoveId = "PARRY_MOVE";
	private const int ThrustDazedPerPile = 2;
	private const int ResolveEnrage = 1;
	private const int ResolveVulnerable = 3;
	private const int ParryBlock = 15;
	private int _temporaryEnrage;

	// 与原版怪物状态一致，只随当前战斗实例保留。
	public int TemporaryEnrage
	{
		get => _temporaryEnrage;
		set
		{
			AssertMutable();
			_temporaryEnrage = value;
		}
	}

	public override string SceneName => "vanguard_veteran";

	protected override int MuzzleBone => 25;

	protected override int WeaponBone => 24;

	protected override int EyeBone => 8;

	public override int MinInitialHp => ToughValue(199, 196);

	public override int MaxInitialHp => ToughValue(203, 200);

	protected override Task ApplyStartingPower() => ApplyPower<BattleHardenedPower>([Creature], 1);

	protected override int LongShotDamage => DeadlyValue(10, 9);

	protected override int LongShotHits => 2;

	protected override int ThrustDamage => DeadlyValue(18, 16);

	private int ParryDamage => DeadlyValue(15, 14);

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState thrust = new(ThrustMoveId, ThrustMove, new SingleAttackIntent(ThrustDamage), new StatusIntent(ThrustDazedPerPile * 2));
		MoveState shot = new(LongShotMoveId, LongShotMove, new MultiAttackIntent(LongShotDamage, LongShotHits));
		MoveState resolve = CreateResolveMove();
		MoveState parry = new(ParryMoveId, ParryMove, new SingleAttackIntent(ParryDamage), new DefendIntent());
		thrust.FollowUpState = shot;
		shot.FollowUpState = resolve;
		resolve.FollowUpState = parry;
		parry.FollowUpState = thrust;
		return Machine([thrust, shot, resolve, parry], thrust);
	}

	protected override MoveState CreateResolveMove() =>
		new(ResolveMoveId, ResolveMove, new BuffIntent(), new DebuffIntent());

	private async Task ThrustMove(IReadOnlyList<Creature> targets)
	{
		await Strike(ThrustDamage, vfx: SlashVfx, sfx: HeavyHitSfx);
		await CardPileCmd.AddToCombatAndPreview<Dazed>(targets, PileType.Draw, ThrustDazedPerPile, null);
		await AddStatusCards<Dazed>(targets, ThrustDazedPerPile);
	}

	private async Task ResolveMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await ApplyPower<EnragePower>([Creature], ResolveEnrage);
		TemporaryEnrage += ResolveEnrage;
		await ApplyPower<VulnerablePower>(targets, ResolveVulnerable);
	}

	private async Task ParryMove(IReadOnlyList<Creature> targets)
	{
		await Strike(ParryDamage, vfx: SlashVfx, sfx: HeavyHitSfx);
		await GainBlock(ParryBlock);
	}

	public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		await base.AfterSideTurnEnd(choiceContext, side, participants);
		if (side != CombatSide.Player || TemporaryEnrage <= 0)
		{
			return;
		}

		int amount = TemporaryEnrage;
		TemporaryEnrage = 0;
		if (Creature.GetPower<EnragePower>() is { } enrage)
		{
			await PowerCmd.ModifyAmount(choiceContext, enrage, -Math.Min(amount, enrage.Amount), Creature, null);
		}
	}
}
