"""原版第三幕荣耀（Glory）全部遭遇战的开局伤害（口径见 sim_core.py），作为调难度的基准。

招式与数值逐个取自反编译的 MegaCrit.Sts2.Core.Models.Monsters.*（数值写 (A0, A10)），
遭遇战阵容取自 MegaCrit.Sts2.Core.Models.Encounters.*，怪物名用官方简中本地化。

近似（引擎表达不了或依赖玩家行为的机制）：
1. 虔诚雕刻师的仪式（每个敌方回合结束 +9 力量，挂上的那个回合跳过）折算成“野蛮”招式自带 +9 力量：
   第 2 回合起每回合都是野蛮，两种写法在前 4 回合完全等价。
2. 遗忘之物的“瘴气”偷敏捷，其“恐惧”伤害 = 基础 + 自身敏捷，敏捷只加在恐惧上，所以按力量记。
3. 电击机器人的高压电（每个敌方回合结束 +2 力量，召唤当回合结束时就会触发）写成
   “电击”基础伤害 +2、出手后再 +2 力量，召唤后第一次出手即 14+2 / 15+2，与原版逐回合一致。
4. 组装师一次“组装”同时造一台防御机器人（守护/噪音，不出伤）和一台进攻机器人（电击/戳刺），
   引擎一招只能召唤一种怪，这里把这一对合成一个占 2 个槽位、按进攻机器人出手的组合怪；
   能否继续组装（含自己存活 < 4）在条件分支里按槽位数自己数。进攻机器人种类按原版 _lastSpawned 规则展开：
   组装时防御机器人先造，进攻机器人在两种里等概率；“组装打击”只造进攻机器人，必为与上一台进攻机器人不同的那种
   （开局第一招就是组装打击时等概率）。机器人都不出改变他人出伤的效果，所以排在组装师前后不影响结果，统一排队尾。
5. 女王“为我燃尽”只给其他队友加力量，引擎的 team_str 含自己；女王前 4 回合不攻击，不影响结果。
   火炬头聚合体不死，女王始终停在“为我燃尽”，不会转入斩首/处决阶段。
6. 永世沙漏“渐强”每用一次力量增量 +1（3→4→…，A10 4→5→…），前 4 回合只用一次，按首次数值记。
7. 不计依赖玩家行为或非攻击的伤害：电球头的电流（玩家打出能力牌受伤）、实验体的激怒（玩家打技能牌加力量）、
   永世沙漏的凋零状态牌（回合结束在手里扣血）、机甲骑士塞的灼伤、咬人卷轴的纸割（扣最大生命）。
8. “被打才触发”的分支一律按不触发：青蛙骑士不会半血冲撞、实验体与巨斧机器人不会复活、
   活体盾的队友（高塔炮手）始终存活所以一直盾击。
"""
from sim_core import V, Move, Rand, Kind, cycle, simulate, summarize

# 组装师造出的机器人占几个槽位（组合怪 = 防御机器人 + 进攻机器人）。
_FAB_BOTS = {"电击机器人": 1, "戳刺机器人": 1, "防御+电击机器人": 2, "防御+戳刺机器人": 2}


def _fab_next(m, w):
    """组装师：含自己存活 ≥ 4 只就只能“解体”，否则组装 / 组装打击各一半。"""
    if 1 + sum(_FAB_BOTS.get(o.kind, 0) for o in w) >= 4:
        return "DISINTEGRATE"
    last = next((h for h in reversed(m.history) if h != "DISINTEGRATE"), None)
    if last is None:
        strike = (("STRIKE_Z", 0.25, 0), ("STRIKE_S", 0.25, 0))
    else:
        # 组装打击的进攻机器人排除上一台进攻机器人（每招最后造的都是进攻机器人）。
        strike = (("STRIKE_S" if last.endswith("_Z") else "STRIKE_Z", 0.5, 0),)
    return Rand((("FAB_Z", 0.25, 0), ("FAB_S", 0.25, 0)) + strike)


_zap = [("ZAP", Move(V(16, 17), 1, self_str=V(2)))]  # 见近似 3
_stab = [("STAB", Move(V(11, 12), 1))]

K = {
    # —— 弱怪 ——
    "虔诚雕刻师": Kind({"FORBIDDEN_INCANTATION": Move(), "SAVAGE": Move(V(12, 15), 1, self_str=V(9))},
                  {"FORBIDDEN_INCANTATION": "SAVAGE", "SAVAGE": "SAVAGE"}),  # 见近似 1
    # 大嚼 → 长牙（+2 力量）→ 咀嚼 → 随机（大嚼不能连用，咀嚼最多连用 2 次）。
    "咬人卷轴": Kind({"CHOMP": Move(V(14, 16), 1), "CHEW": Move(V(5, 6), 2), "MORE_TEETH": Move(self_str=V(2))},
                 {"CHOMP": "MORE_TEETH", "MORE_TEETH": "CHEW", "CHEW": Rand((("CHOMP", 1.0, 1), ("CHEW", 1.0, 2)))}),
    "活体盾": cycle([("SHIELD_SLAM", Move(V(6), 1))]),
    "高塔炮手": cycle([("UNLOAD", Move(V(3, 4), 5)), ("UNLOAD_2", Move(V(3, 4), 5)), ("RELOAD", Move(self_str=V(1)))]),
    # —— 普通 ——
    # 遭遇战里的巨斧机器人没有库存覆盖值，从上勾拳起手（启动只有复活后的才用）。
    "巨斧机器人": cycle([("HAMMER_UPPERCUT", Move(V(14, 18), 1)), ("ONE_TWO", Move(V(10, 11), 2))]),
    "拳击构装体": cycle([("READY", Move()), ("FAST_PUNCH", Move(V(5, 6), 2)), ("STRONG_PUNCH", Move(V(14, 16), 1))]),
    "方柱构装体": Kind({"CHARGE_UP": Move(self_str=V(2)), "BLAST": Move(V(7, 8), 1, self_str=V(2)),
                   "BLAST_2": Move(V(7, 8), 1, self_str=V(2)), "EXPEL": Move(V(5, 6), 2)},
                  {"CHARGE_UP": "BLAST", "BLAST": "BLAST_2", "BLAST_2": "EXPEL", "EXPEL": "BLAST"}),
    "组装师": Kind({"FAB_Z": Move(summon="防御+电击机器人"), "FAB_S": Move(summon="防御+戳刺机器人"),
                 "STRIKE_Z": Move(V(18, 21), 1, summon="电击机器人"), "STRIKE_S": Move(V(18, 21), 1, summon="戳刺机器人"),
                 "DISINTEGRATE": Move(V(11, 13), 1)},
                {s: _fab_next for s in ("FAB_Z", "FAB_S", "STRIKE_Z", "STRIKE_S", "DISINTEGRATE")},
                summon_cap=99),  # 上限由 _fab_next 按槽位数控制
    "电击机器人": cycle(_zap),
    "戳刺机器人": cycle(_stab),
    "防御+电击机器人": cycle(_zap),
    "防御+戳刺机器人": cycle(_stab),
    # 舌鞭 → 诛邪 → 为了女王（+5 力量）→ 半血分支：玩家不打它，始终回到舌鞭。
    "青蛙骑士": cycle([("TONGUE_LASH", Move(V(13, 14), 1)), ("STRIKE_DOWN_EVIL", Move(V(21, 23), 1)),
                    ("FOR_THE_QUEEN", Move(self_str=V(5)))]),
    "电球头": cycle([("SHOCKING_SLAP", Move(V(13, 14), 1)), ("THUNDER_STRIKE", Move(V(6, 7), 3)),
                  ("GALVANIC_BURST", Move(V(16, 17), 1, self_str=V(2)))]),
    "猫头鹰法官": cycle([("SCRUTINY", Move(V(16, 17), 1)), ("PECK_ASSAULT", Move(V(4), 6)), ("JUDICIAL_FLIGHT", Move()),
                     ("VERDICT", Move(V(33, 36), 1, vuln=4))]),
    "史莱姆狂战士": cycle([("VOMIT_ICHOR", Move()), ("FURIOUS_PUMMELING", Move(V(4, 5), 4)),
                      ("LEECHING_HUG", Move(self_str=V(3))), ("SMOTHER", Move(V(30, 33), 1))]),
    "失落之物": cycle([("DEBILITATING_SMOG", Move(self_str=V(2))), ("EYE_LASERS", Move(V(4, 5), 2))]),
    "遗忘之物": cycle([("MIASMA", Move(self_str=V(2))), ("DREAD", Move(V(13, 15), 1))]),  # 见近似 2
    # —— 精英 ——
    "连枷骑士": Kind({"WAR_CHANT": Move(self_str=V(3)), "FLAIL": Move(V(9, 10), 2), "RAM": Move(V(15, 17), 1)}, {}),
    "幽灵骑士": Kind({"HEX": Move(), "SOUL_SLASH": Move(V(15, 17), 1), "SOUL_FLAME": Move(V(3, 4), 3)}, {}),
    "魔法骑士": Kind({"POWER_SHIELD": Move(V(6, 7), 1), "DAMPEN": Move(), "RAM": Move(V(10, 11), 1), "PREP": Move(),
                  "MAGIC_BOMB": Move(V(35, 40), 1)},
                 {"POWER_SHIELD": "DAMPEN", "DAMPEN": "RAM", "RAM": "PREP", "PREP": "MAGIC_BOMB", "MAGIC_BOMB": "RAM"}),
    "机甲骑士": Kind({"CHARGE": Move(V(25, 30), 1), "FLAMETHROWER": Move(V(8, 12), 1), "WINDUP": Move(self_str=V(5)),
                  "HEAVY_CLEAVE": Move(V(35, 40), 1)},
                 {"CHARGE": "FLAMETHROWER", "FLAMETHROWER": "WINDUP", "WINDUP": "HEAVY_CLEAVE", "HEAVY_CLEAVE": "FLAMETHROWER"}),
    "灵魂枢纽": Kind({"SOUL_BURN": Move(V(29, 31), 1), "MAELSTROM": Move(V(6, 7), 4), "DRAIN_LIFE": Move(V(18, 19), 1, vuln=2)}, {}),
    # —— Boss ——
    # 女王：提线 → 你是我的了（易伤/虚弱/脆弱各 99）→ 为我燃尽（其他队友 +1 力量）反复。见近似 5。
    "女王": Kind({"PUPPET_STRINGS": Move(), "YOU_ARE_MINE": Move(vuln=99), "BURN_BRIGHT_FOR_ME": Move(team_str=V(1))},
               {"PUPPET_STRINGS": "YOU_ARE_MINE", "YOU_ARE_MINE": "BURN_BRIGHT_FOR_ME", "BURN_BRIGHT_FOR_ME": "BURN_BRIGHT_FOR_ME"}),
    "火炬头聚合体": Kind({"STRONG_TACKLE": Move(V(26, 32), 1), "TACKLE_2": Move(V(18, 22), 1), "BEAM": Move(V(8), 3),
                     "TACKLE_3": Move(V(14, 16), 1), "TACKLE_4": Move(V(14, 16), 1)},
                    {"STRONG_TACKLE": "TACKLE_2", "TACKLE_2": "BEAM", "BEAM": "TACKLE_3", "TACKLE_3": "TACKLE_4", "TACKLE_4": "BEAM"}),
    # 实验体第一形态：撕咬 ↔ 颅击（1 层易伤）；不死就不转阶段。
    "实验体": cycle([("BITE", Move(V(20, 22), 1)), ("SKULL_BASH", Move(V(14, 16), 1, vuln=1))]),
    "永世沙漏": cycle([("EBB", Move(V(22, 26), 1)), ("EYE_LASERS", Move(V(11, 12), 2)),
                    ("INCREASING_INTENSITY", Move(self_str=V(3, 4)))]),  # 见近似 6
}
# 连枷骑士：冲撞起手，之后战歌不能连用、连枷/冲撞最多连用 2 次。
K["连枷骑士"].next = {s: Rand((("WAR_CHANT", 1.0, 1), ("FLAIL", 1.0, 2), ("RAM", 1.0, 2))) for s in K["连枷骑士"].moves}
# 幽灵骑士：咒术 → 灵魂斩 → 随机（灵魂斩最多连用 2 次，灵魂火焰不能连用）。
_spectral = Rand((("SOUL_SLASH", 1.0, 2), ("SOUL_FLAME", 1.0, 1)))
K["幽灵骑士"].next = {"HEX": "SOUL_SLASH", "SOUL_SLASH": _spectral, "SOUL_FLAME": _spectral}
# 灵魂枢纽：灵魂灼烧起手，之后三招等权、都不能连用。
_nexus = Rand((("SOUL_BURN", 1.0, 1), ("MAELSTROM", 1.0, 1), ("DRAIN_LIFE", 1.0, 1)))
K["灵魂枢纽"].next = {s: _nexus for s in K["灵魂枢纽"].moves}

# 咬人卷轴 StarterMoveIdx：0 大嚼 / 1 咀嚼 / 2 长牙。
SCROLL = ("CHOMP", "CHEW", "MORE_TEETH")


def lineups():
    """{(类型, 名称): [阵容, ...]}，阵容 = [(种类, 起手)]，列表里各阵容等概率。"""
    # 卷轴：第 1 只 Rng.NextInt(3)，后两只依次 +1、+2；普通版第 4 只固定长牙。
    scrolls3 = [[("咬人卷轴", SCROLL[(r + i) % 3]) for i in range(3)] for r in range(3)]
    return {
        ("弱", "虔诚雕刻师"): [[("虔诚雕刻师", "FORBIDDEN_INCANTATION")]],
        ("弱", "咬人卷轴 ×3"): scrolls3,
        ("弱", "高塔炮手"): [[("活体盾", "SHIELD_SLAM"), ("高塔炮手", "UNLOAD")]],
        ("普", "巨斧机器人"): [[("巨斧机器人", "HAMMER_UPPERCUT")]],
        ("普", "构装体"): [[("拳击构装体", "READY"), ("方柱构装体", "CHARGE_UP"), ("方柱构装体", "CHARGE_UP")]],
        # 起手是条件分支 → 随机：组装 / 组装打击各一半，进攻机器人种类再各一半，四种等概率。
        ("普", "组装师"): [[("组装师", s)] for s in ("FAB_Z", "FAB_S", "STRIKE_Z", "STRIKE_S")],
        ("普", "青蛙骑士"): [[("青蛙骑士", "TONGUE_LASH")]],
        ("普", "电球头"): [[("电球头", "SHOCKING_SLAP")]],
        ("普", "猫头鹰法官"): [[("猫头鹰法官", "SCRUTINY")]],
        ("普", "咬人卷轴 ×4"): [lu + [("咬人卷轴", "MORE_TEETH")] for lu in scrolls3],
        ("普", "史莱姆狂战士"): [[("史莱姆狂战士", "VOMIT_ICHOR")]],
        ("普", "失落与遗忘"): [[("失落之物", "DEBILITATING_SMOG"), ("遗忘之物", "MIASMA")]],
        ("精", "骑士三人组"): [[("连枷骑士", "RAM"), ("幽灵骑士", "HEX"), ("魔法骑士", "POWER_SHIELD")]],
        ("精", "机甲骑士"): [[("机甲骑士", "CHARGE")]],
        ("精", "灵魂枢纽"): [[("灵魂枢纽", "SOUL_BURN")]],
        # 槽位 amalgam 在 queen 前面。
        ("首", "女王"): [[("火炬头聚合体", "STRONG_TACKLE"), ("女王", "PUPPET_STRINGS")]],
        ("首", "实验体"): [[("实验体", "BITE")]],
        ("首", "永世沙漏"): [[("永世沙漏", "EBB")]],
    }


def run(asc: int) -> dict:
    return {key: summarize([simulate(K, lu, asc) for lu in lus]) for key, lus in lineups().items()}


def fmt(xs):
    return "/".join(f"{x:4.1f}".rstrip("0").rstrip(".") if isinstance(x, float) else str(x) for x in xs)


if __name__ == "__main__":
    for asc, label in ((0, "A0"), (1, "A10")):
        print(f"== 原版荣耀 {label}（前 4 个敌方回合：期望 | 最大 | 总伤期望/最大）")
        for (kind, name), s in run(asc).items():
            print(f"{kind} {name:10s} 期望 {fmt([round(x, 1) for x in s['mean']]):24s} 最大 {fmt(s['max']):16s} "
                  f"总 {s['total_mean']:5.1f}/{s['total_max']}")
