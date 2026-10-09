using ArknightsRoaringFlare.Monsters;
using MegaCrit.Sts2.Core.Entities.Encounters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Encounters;

/// <summary>
/// 第八章遭遇战的公共部分（基于 RitsuLib 的 ModEncounterTemplate），由苦难摇篮的 ReunionEncounter 复制而来。
/// 阵容顺序即站位顺序（不给槽位名时原版按顺序自动排开），站位顺序就是出手顺序，会给自己叠力量的头目站最右。
/// 战斗池里的遭遇经 RitsuLib 按幕注册（ModEntry.ActEncounterTypes），模型 id 形如 ARKNIGHTS_ROARING_FLARE_ENCOUNTER_&lt;类名&gt;；
/// 存档与 `fight` 命令都按 id 引用，不要改类名。
/// </summary>
public abstract class FlareEncounter : ModEncounterTemplate
{
	/// <summary>固定阵容。</summary>
	protected virtual IReadOnlyList<MonsterModel> Lineup => [];

	/// <summary>本场可能出现的全部怪物（原版按它预载资源）。</summary>
	protected virtual IEnumerable<MonsterModel> Candidates => Lineup;

	public override IEnumerable<MonsterModel> AllPossibleMonsters => Candidates.Distinct();

	/// <summary>
	/// 阵容各怪的站位槽名，与阵容等长。只有要中途召唤的遭遇战需要（照原版卵翼虫）：同时覆盖 Slots 与 AssetProfile 的
	/// EncounterScenePath（assets/scenes/encounters/ 下放同名 Marker2D 的场景）。
	/// 原版加入带槽位的怪物时按 Slots 顺序重排敌方列表（CombatManager.AddCreature → SortEnemiesBySlotName），
	/// 所以槽位顺序也决定召唤物的出手顺序。null 表示不用槽位，原版按判定框宽度自动排开。
	/// </summary>
	protected virtual IReadOnlyList<string>? LineupSlots => null;

	protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() => CreateMonstersWithSlots(Rng);

	/// <summary><see cref="CreateMonsters"/> 再配上 <see cref="LineupSlots"/>；自检也用它核对槽位。</summary>
	internal IReadOnlyList<(MonsterModel Monster, string? Slot)> CreateMonstersWithSlots(Rng rng)
	{
		IReadOnlyList<string>? slots = LineupSlots;
		return CreateMonsters(rng).Select((monster, i) => (monster, slots?[i])).ToList();
	}

	/// <summary>阵容 → 可变怪物 → <see cref="ConfigureMonsters"/>。自检直接调用它，不需要进行中的对局。</summary>
	internal IReadOnlyList<MonsterModel> CreateMonsters(Rng rng)
	{
		List<MonsterModel> monsters = Lineup.Select(monster => monster.ToMutable()).ToList();
		ConfigureMonsters(monsters);
		return monsters;
	}

	/// <summary>在状态机建立前调整可变怪物（错开同种怪的起手招式）。</summary>
	protected virtual void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters)
	{
	}

	/// <summary>按阵容顺序设置各怪的起手偏移，取值由 docs/tools/opening_sim.py --search 算出，并与那里的 OPENINGS 一致。</summary>
	protected static void SetOpenings(IReadOnlyList<MonsterModel> monsters, params int[] offsets)
	{
		for (int i = 0; i < offsets.Length; i++)
		{
			((FlareMonster)monsters[i]).OpeningOffset = offsets[i];
		}
	}

	protected static MonsterModel M<TMonster>()
		where TMonster : MonsterModel
	{
		return ModelDb.Monster<TMonster>();
	}
}

/// <summary>
/// 遭遇战标签：原版抽下一场时避开与上一场同标签的遭遇战（ActModel.AddWithoutRepeatingTags），弱怪版和普通版同一群怪打同一个标签。
/// 原版 EncounterTag 是封闭枚举，这里用枚举范围外的值（只做相等比较，不存档、不显示）；取 8000 段，与苦难摇篮的 7000 段错开。
/// </summary>
internal static class FlareEncounterTags
{
	public const EncounterTag Patrol = (EncounterTag)8001;
	public const EncounterTag Ravagers = (EncounterTag)8002;
	public const EncounterTag Casters = (EncounterTag)8003;
}
