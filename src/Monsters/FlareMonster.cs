using ArknightsRoaringFlare.Audio;
using ArknightsRoaringFlare.Powers;
using ArknightsRoaringFlare.Vfx;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>带重生能力（<see cref="RebirthPowerBase"/>）的怪物：生命归零时切到重生招式，重生招式负责换形态并移除（或重挂）该能力。</summary>
public interface IFlareRebirth
{
	Task BeginRebirth();
}

/// <summary>
/// 第八章敌人的公共部分（基于 RitsuLib 的 ModMonsterTemplate），由苦难摇篮的 ReunionMonster 复制而来：PRTS Spine 场景路径、
/// 音效、起手偏移、随机出招与动画状态机的搭法。全部具体子类经 RitsuLib 注册（ModEntry），模型 id 形如
/// ARKNIGHTS_ROARING_FLARE_MONSTER_TALULAH，存档、联机和控制台都按它引用，不要改类名。
/// </summary>
public abstract class FlareMonster : ModMonsterTemplate
{
	public const string SceneRoot = $"res://{ModEntry.ModId}/scenes/creature_visuals/";

	// 音效槽：招式按槽要音效，各怪物在 SoundFor 里把槽对到明日方舟原作的音效片段（Audio.FlareSfx）。
	// 原版 FMOD 事件按模型 id 拼路径，模组没有对应事件，所以不走 FMOD。子类可以另加自己的槽名。
	protected const string MeleeHitSfx = "melee";
	protected const string RangedHitSfx = "ranged";
	protected const string HeavyHitSfx = "heavy";
	protected const string BombHitSfx = "bomb";
	protected const string ArtsHitSfx = "arts";

	/// <summary>
	/// 一个音效槽对应的原作片段：Start 在动作开始时（原作 ON_ABILITY_START），On 在攻击帧（ON_ABILITY_ON），
	/// Hit 在每段命中（ON_ABILITY_HIT / ON_PROJECTILE_HIT）。片段名见 tools/ak_audio/install_sfx.py。
	/// </summary>
	protected readonly record struct FlareSound(string? Start = null, string? On = null, string? Hit = null);

	/// <summary>
	/// 槽 → 原作音效。默认是原作的通用命中声；每个敌人照游戏数据 audio_data.json 里自己的 soundFXBanks 覆盖
	/// （battle.ON_ABILITY_*.&lt;敌人 id&gt;.combat 是近战、.attack 是远程或技能）。
	/// </summary>
	protected virtual FlareSound SoundFor(string slot) => slot switch
	{
		RangedHitSfx => new(Hit: "e_imp_arrow_n"),
		BombHitSfx => new(Hit: "e_imp_grenade_n"),
		ArtsHitSfx => new(Hit: "e_imp_magic_n"),
		_ => new(Hit: "e_imp_general_n"),
	};

	/// <summary>
	/// 一个招式槽对应的原作特效（Vfx.AkFx，从原作资源包转出）：Start 在招式开始时播在自己身上；Hit 每段命中播在目标身上；
	/// 给了 Projectile 时改为每段命中前从 ProjectileBone（枪口骨骼，没有就从受击点）射出投射物，飞到后才播 Hit 并结算伤害
	/// （原作远程攻击就是命中时结算）。速度是原作的世界单位/秒，Arc 是抛物线高度（世界单位）。
	/// </summary>
	protected readonly record struct FlareFx(
		FxCue[]? Start = null,
		FxCue[]? Hit = null,
		string? Projectile = null,
		int ProjectileBone = -1,
		float ProjectileSpeed = 10f,
		float ProjectileArc = 0f);

	/// <summary>槽 → 原作特效。各怪物照原作预制体里招式节点列出的特效覆盖（battle/enm_pfb_*，见 tools/ak_vfx/）。</summary>
	protected virtual FlareFx FxFor(string slot) => default;

	/// <summary>进场时（第一次）预读全部原作特效，免得第一次出招时现场解析卡顿。</summary>
	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		AkFx.PreloadAll();
	}

	/// <summary>原作敌人预制体的 Graphic 缩放与 HitPoint（决定原作特效在本怪身上的比例与位置）。</summary>
	protected virtual AkFxRig Rig => new(AkFx.DefaultGraphicScale, new Godot.Vector2(0f, 1.6f));

	/// <summary>自检用：本怪各音效槽（默认形态）、强化与死亡用到的原作特效名。招式里直接写的演出特效由各怪物在 ExtraEffects 里列出。</summary>
	internal IEnumerable<string> AllEffectNames()
	{
		IEnumerable<FxCue> cues = new[] { MeleeHitSfx, RangedHitSfx, HeavyHitSfx, BombHitSfx, ArtsHitSfx }.Concat(ExtraSoundSlots)
			.Select(FxFor)
			.SelectMany(fx => (fx.Start ?? []).Concat(fx.Hit ?? []))
			.Concat(BuffFx ?? [])
			.Concat(DeathFx ?? []);
		foreach (FxCue cue in cues)
		{
			yield return cue.Name;
		}

		foreach (string name in ExtraEffects)
		{
			yield return name;
		}

		foreach (string? projectile in new[] { MeleeHitSfx, RangedHitSfx, HeavyHitSfx, BombHitSfx, ArtsHitSfx }.Concat(ExtraSoundSlots).Select(slot => FxFor(slot).Projectile))
		{
			if (projectile is not null)
			{
				yield return projectile;
			}
		}
	}

	/// <summary>招式里直接播的原作特效（不经音效槽），供自检核对。</summary>
	protected virtual IEnumerable<string> ExtraEffects => [];

	/// <summary>在自己身上播原作特效（非攻击招式用）。</summary>
	protected void PlayFx(params FxCue[]? cues)
	{
		foreach (FxCue cue in cues ?? [])
		{
			AkFx.OnCreature(Creature, Rig, cue);
		}
	}

	/// <summary>在别的生物身上播原作特效，比例按本怪换算（标定的准星、召唤落点等）。</summary>
	protected void PlayFxOn(Creature target, params FxCue[]? cues)
	{
		foreach (FxCue cue in cues ?? [])
		{
			AkFx.OnTarget(target, Creature, Rig.GraphicScale, cue);
		}
	}

	/// <summary>非攻击招式的出招动画，等到动作里的出手时刻（快速模式也等满，见 <see cref="FlarePace"/>）。</summary>
	protected Task PacedAnim(string trigger, float delay) => FlarePace.Animate(Creature, trigger, delay);

	/// <summary>强化类招式的音效（原作通用的 e_skill_buff）。</summary>
	protected virtual string? BuffClip => "e_skill_buff";

	/// <summary>真正死亡时的音效（原作 battle.ON_UNIT_DEAD.enemy）。会重生的怪倒下时不播，由各自的重生演出出声。</summary>
	protected virtual string? DeathClip => "b_enemy_dead_n";

	/// <summary>子类自己加的音效槽名（自检按它把全部槽的片段都核对一遍）。</summary>
	protected virtual IEnumerable<string> ExtraSoundSlots => [];

	/// <summary>自检用：本怪全部音效槽、强化与死亡用到的片段名。</summary>
	internal IEnumerable<string> AllSoundClips()
	{
		foreach (string slot in new[] { MeleeHitSfx, RangedHitSfx, HeavyHitSfx, BombHitSfx, ArtsHitSfx }.Concat(ExtraSoundSlots))
		{
			FlareSound sound = SoundFor(slot);
			foreach (string? clip in new[] { sound.Start, sound.On, sound.Hit })
			{
				if (!string.IsNullOrEmpty(clip))
				{
					yield return clip;
				}
			}
		}

		foreach (string? clip in new[] { BuffClip, DeathClip })
		{
			if (!string.IsNullOrEmpty(clip))
			{
				yield return clip;
			}
		}
	}

	protected const string SlashVfx = "vfx/vfx_attack_slash";
	protected const string BluntVfx = "vfx/vfx_attack_blunt";
	protected const string HeavyBluntVfx = "vfx/vfx_heavy_blunt";
	protected const string BiteVfx = "vfx/vfx_bite";
	protected const string ArtsVfx = "vfx/vfx_attack_lightning";

	/// <summary>creature_visuals 场景文件名（不含扩展名），与 <see cref="SceneRoot"/> 拼成完整路径。</summary>
	public abstract string SceneName { get; }

	/// <summary>本模型用到的全部 Spine 动画名，自检按它核对 skel 里是否真的存在。</summary>
	public abstract IReadOnlyList<string> RequiredAnimations { get; }

	public override MonsterAssetProfile AssetProfile => new(VisualsScenePath: CreatureScenePath);

	/// <summary>生物场景路径：RitsuLib 按 AssetProfile 接到原版 VisualsPath；中途才召来的怪物场景要列进遭遇战预载。</summary>
	internal string CreatureScenePath => SceneRoot + SceneName + ".tscn";

	public override bool HasDeathSfx => false;

	/// <summary>
	/// 灾厄击杀前原版 DoomPower.PlayVfx 先问这一项：为真就把生物节点淡出并销毁，之后才调 Kill。
	/// 会重生的怪（塔露拉、追猎者）死后留在战斗里要换阶段，节点被销毁就回不来，表现为灾厄打死后跳过后续阶段。
	/// 原版实验体用 ShouldDisappearFromDoom => Respawns >= 2；这里直接问“死后是否移出战斗”的钩子，与紧接着的 Kill 同一个判断。
	/// </summary>
	public override bool ShouldDisappearFromDoom =>
		Creature.CombatState is not { } combatState || Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(combatState, Creature);

	private int _openingOffset;

	/// <summary>
	/// 起手偏移：状态机建好后沿招式循环往后走这么多步作为第一招。由遭遇战在生成怪物时设置（取值见 docs/tools/opening_sim.py）。
	/// 状态机在怪物加入战斗时才建立，晚于遭遇战生成怪物，所以在 ConfigureMonsters 里设置即可生效。
	/// </summary>
	public int OpeningOffset
	{
		get => _openingOffset;
		set
		{
			AssertMutable();
			_openingOffset = value;
		}
	}

	/// <summary>最近一次建立状态机时的起手招式 id，只供自检核对起手偏移。</summary>
	internal string? OpeningStateId { get; private set; }

	/// <summary>各怪物用它代替 new MonsterMoveStateMachine，以应用 <see cref="OpeningOffset"/>。</summary>
	protected MonsterMoveStateMachine Machine(IEnumerable<MonsterState> states, MonsterState initialState)
	{
		MonsterState start = initialState;
		for (int step = 0; step < OpeningOffset && start is MoveState { FollowUpState: { } next }; step++)
		{
			start = next;
		}

		return Build(states, start);
	}

	/// <summary>
	/// 随机出招（原版外骨骼虫、猎杀者的写法）：每招之后从 <paramref name="moves"/> 里等权随机挑一招，不连用同一招。
	/// 第一招不随机，取第 <see cref="OpeningOffset"/> 招。随机数走原版 RandomBranchState 的怪物 AI 随机源，联机两端一致。
	/// </summary>
	protected MonsterMoveStateMachine RandomMachine(string branchId, IReadOnlyList<MoveState> moves)
	{
		RandomBranchState loop = new(branchId);
		foreach (MoveState move in moves)
		{
			loop.AddBranch(move, MoveRepeatType.CannotRepeat, 1f);
			move.FollowUpState = loop;
		}

		return Build([.. moves, loop], moves[OpeningOffset % moves.Count]);
	}

	protected MonsterMoveStateMachine Build(IEnumerable<MonsterState> states, MonsterState start)
	{
		OpeningStateId = start.Id;
		return new MonsterMoveStateMachine(states, start);
	}

	/// <summary>攻击动画里 OnAttack 事件的时刻（秒），伤害在这一刻结算；各怪按自己 skel 的 Attack 动画填。</summary>
	protected virtual float AttackHitDelay => 0.4f;

	/// <summary>Spine 里的攻击动画名（攻击触发器播它）。</summary>
	protected virtual string AttackAnimation => "Attack";

	/// <summary>普通攻击：播攻击动画，在 <see cref="AttackHitDelay"/> 结算，多段只播一次动画。</summary>
	protected Task<AttackCommand> Strike(int damage, int hits = 1, string? vfx = SlashVfx, string? sfx = MeleeHitSfx)
	{
		AttackCommand attack = DamageCmd.Attack(damage);
		if (hits > 1)
		{
			attack = attack.WithHitCount(hits).OnlyPlayAnimOnce();
		}

		attack = attack.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, AttackHitDelay)
			.WithHitFx(vfx);
		return Cued(attack, sfx ?? MeleeHitSfx).Execute(null);
	}

	/// <summary>
	/// 给攻击指令接上原作的音效与特效（槽见 <see cref="SoundFor"/> 与 <see cref="FxFor"/>）：音效的 Start 和出手特效立刻播
	/// （所以要在 Execute 之前紧挨着调用），音效的 On 在攻击帧，命中特效与音效的 Hit 在每段命中。
	/// 有原作命中特效时去掉原版的命中特效（WithHitFx）。原版的 BeforeDamage 只能设一个，这里统一设置，调用方不要再设。
	/// 投射物只是表现，等它飞到只多等飞行时间（Cmd.Wait），结算本身不变。
	/// 出手时刻的等待由这里接管（<see cref="FlarePace"/>）：原版快速模式只等 0.25 秒而动画照原速播，伤害会落在蓄力动作上；
	/// 这里快速模式也等到动作里的出手那一帧。
	/// </summary>
	protected AttackCommand Cued(AttackCommand attack, string slot)
	{
		FlareSound sound = SoundFor(slot);
		FlareFx fx = FxFor(slot);
		float? hitDelay = FlarePace.TakeHitDelay(attack);
		FlareSfx.Play(sound.Start);
		PlayFx(fx.Start);
		attack = attack.AfterAttackerAnim(async () =>
		{
			if (hitDelay is { } delay)
			{
				await Cmd.Wait(delay);
			}

			FlareSfx.Play(sound.On);
		});

		if (fx.Hit is { Length: > 0 } || fx.Projectile is not null)
		{
			attack = attack.WithHitFx(null, null, null);
		}

		if (fx.Projectile is null && fx.Hit is { Length: > 0 } hits)
		{
			attack = attack.WithHitVfxNode(target =>
			{
				PlayFxOn(target, hits);
				return Placeholder();
			});
		}

		if (sound.Hit is not null || fx.Projectile is not null)
		{
			attack = attack.BeforeDamage(async () =>
			{
				if (fx.Projectile is { } projectile)
				{
					float flight = 0f;
					foreach (Creature target in CombatState.GetOpponentsOf(Creature).Where(c => c.IsAlive))
					{
						flight = Math.Max(flight, AkFx.ProjectileTo(Creature, Rig, target, projectile, fx.ProjectileBone,
							fx.ProjectileSpeed, fx.ProjectileArc, fx.Hit));
					}

					if (flight > 0f)
					{
						await Cmd.Wait(flight);
					}
				}

				FlareSfx.Play(sound.Hit);
			});
		}

		return attack;
	}

	/// <summary>原版命中特效回调要求返回一个节点（加进目标的特效容器）；原作特效自己挂，这里给一个下一帧自行释放的空节点。</summary>
	private static Godot.Node2D Placeholder()
	{
		Godot.Node2D node = new();
		node.CallDeferred(Godot.Node.MethodName.QueueFree);
		return node;
	}

	/// <summary>非攻击招式按槽出声：Start 与 On 立刻播，Hit 在 <paramref name="hitDelay"/> 秒后。</summary>
	protected void PlaySound(string slot, float hitDelay = 0f)
	{
		FlareSound sound = SoundFor(slot);
		FlareSfx.Play(sound.Start);
		FlareSfx.Play(sound.On);
		FlareSfx.PlaySequence((sound.Hit ?? string.Empty, hitDelay));
	}

	/// <summary>强化类招式在自己身上放的原作特效（PRTS 骨骼大多没有专门的强化动画，有对应特效的怪覆盖它）。</summary>
	protected virtual FxCue[]? BuffFx => null;

	/// <summary>
	/// 大块贴地图案（范围圈、瞄准标记、熔岩裂地）的倾角：原作镜头近乎俯视（偏离竖直 30°），地面图案只压扁到 0.87，
	/// 在本模组的侧视镜头里像竖着贴在镜头上（用户对黑蛇的熔岩裂地提过）；转到 72°（压扁到约 0.31）才平躺在地面上。
	/// 往上窜的火焰、光柱仍然立着。在 65°/72°/78° 的预览里对照选的。
	/// </summary>
	protected static readonly FxTune GroundDecal = new(GroundTilt: 72f);

	/// <summary>
	/// 开发预览用（AkFxPreview 的 monster 模式）：按槽取本怪真实的出手/命中特效与 Rig；槽名 buff、death 取强化与死亡特效。
	/// 返回 Graphic 缩放、受击点、出手特效、命中特效、投射物（名字、骨骼、速度、弹道高）与普通攻击的出手时刻。
	/// </summary>
	/// <summary>开发预览用：招式以外的特效组（倒地、重整等），按名字给；默认没有。</summary>
	protected virtual FxCue[]? PreviewCues(string slot) => null;

	internal (float Graphic, Godot.Vector2 HitPoint, FxCue[] Start, FxCue[] Hit, string? Projectile, int Bone, float Speed, float Arc, float AttackDelay) PreviewFx(string slot)
	{
		FlareFx fx = slot switch
		{
			"buff" => new FlareFx(Start: BuffFx),
			"death" => new FlareFx(Start: DeathFx),
			_ when PreviewCues(slot) is { } cues => new FlareFx(Start: cues),
			_ => FxFor(slot),
		};
		return (Rig.GraphicScale, Rig.HitPoint, fx.Start ?? [], fx.Hit ?? [], fx.Projectile, fx.ProjectileBone, fx.ProjectileSpeed, fx.ProjectileArc, AttackHitDelay);
	}

	/// <summary>强化类招式：放音效和 <see cref="BuffFx"/>。</summary>
	protected void BuffCue()
	{
		FlareSfx.Play(BuffClip);
		PlayFx(BuffFx);
	}

	public override Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
	{
		// 重生能力还在 = 只是倒下（会重生），不播死亡声。
		if (creature == Creature && !wasRemovalPrevented && !Creature.Powers.OfType<RebirthPowerBase>().Any())
		{
			FlareSfx.Play(DeathClip);
			PlayFx(DeathFx);
		}

		return Task.CompletedTask;
	}

	/// <summary>真正死亡时在自己身上放的原作特效（Boss 与精英的退场演出）。</summary>
	protected virtual FxCue[]? DeathFx => null;

	/// <summary>往玩家弃牌堆塞状态牌（原版怪物的晕眩、伤口、灼伤都放弃牌堆）。</summary>
	protected static Task AddStatusCards<TCard>(IReadOnlyList<Creature> targets, int count)
		where TCard : CardModel
	{
		return CardPileCmd.AddToCombatAndPreview<TCard>(targets, PileType.Discard, count, null);
	}

	protected static int ToughValue(int ascension, int normal)
	{
		return AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, ascension, normal);
	}

	protected static int DeadlyValue(int ascension, int normal)
	{
		return AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ascension, normal);
	}

	protected Task ApplyStrengthToSelf(int amount)
	{
		return PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, amount, Creature, null);
	}

	protected Task GainBlock(int amount)
	{
		return CreatureCmd.GainBlock(Creature, amount, ValueProp.Move, null);
	}

	protected Task ApplyPower<TPower>(IEnumerable<Creature> targets, int amount)
		where TPower : PowerModel
	{
		return PowerCmd.Apply<TPower>(new ThrowingPlayerChoiceContext(), targets, amount, Creature, null);
	}

	/// <summary>给其他存活盟友格挡（不影响出伤，可以保留；给盟友加伤害的招式一律不做，见设计文档教训 3）。</summary>
	protected async Task GiveBlockToOthers(int amount)
	{
		foreach (Creature creature in CombatState.GetTeammatesOf(Creature).Where(c => c != Creature && c.IsAlive))
		{
			await CreatureCmd.GainBlock(creature, amount, ValueProp.Move, null);
		}
	}

	/// <summary>
	/// 搭一个“待机循环 + 若干一次性动作回到待机”的状态机。PRTS 的 Default 是空姿势，待机一律用 Idle 系列。
	/// </summary>
	protected static CreatureAnimator BuildAnimator(
		MegaSprite controller,
		string idleAnimation,
		string deathAnimation,
		params (string Trigger, string Animation)[] actions)
	{
		AnimState idle = new(idleAnimation, isLooping: true);
		CreatureAnimator animator = new(idle, controller);
		animator.AddAnyState(CreatureAnimator.idleTrigger, idle);
		foreach ((string trigger, string animation) in actions)
		{
			animator.AddAnyState(trigger, new AnimState(animation) { NextState = idle });
		}

		animator.AddAnyState(CreatureAnimator.deathTrigger, new AnimState(deathAnimation));
		return animator;
	}
}
