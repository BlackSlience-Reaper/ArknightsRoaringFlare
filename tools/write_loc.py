"""生成 assets/localization/{zhs,eng}/ 下的 acts / encounters / monsters / powers 表。

键名由类名经 RitsuLib 得出（ARKNIGHTS_ROARING_FLARE_<类别>_<类名大写下划线>），招式键去掉 _MOVE 后缀。
改文案改这里再跑一遍（python3 tools/write_loc.py），不要手改生成的 JSON。eng 是其余语言的回退，不能放中文。
"""
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent / 'assets' / 'localization'
PREFIX = 'ARKNIGHTS_ROARING_FLARE'


def slug(name: str) -> str:
    return re.sub(r'(?<=[a-z0-9])(?=[A-Z])', '_', name).upper()


ACT = {'title': ('怒号光明', 'Roaring Flare'), 'description': ('怒号光明', 'Roaring Flare')}

# 类名: (中文, 英文, 战败文案中文, 战败文案英文)
ENCOUNTERS = {
    'PatrolWeak': ('纠察队巡逻', 'Patrol', '{character}倒在了[gold]{encounter}[/gold]的警棍下。', '{character} fell to the batons of the [gold]{encounter}[/gold].'),
    'RavagersWeak': ('裂兽', 'Ravagers', '{character}被[gold]{encounter}[/gold]撕碎了。', '{character} was torn apart by [gold]{encounter}[/gold].'),
    'ArmoredCasterWeak': ('着铠术师', 'Armored Caster', '{character}倒在了[gold]{encounter}[/gold]的法术之下。', '{character} fell to the arts of an [gold]{encounter}[/gold].'),
    'HuskVillageSearchNormal': ('谷壳村搜捕', 'Husk Village Search', '{character}没能走出[gold]{encounter}[/gold]。', '{character} never made it out of the [gold]{encounter}[/gold].'),
    'RavagerPackNormal': ('裂兽群', 'Ravager Pack', '{character}被[gold]{encounter}[/gold]撕碎了。', '{character} was torn apart by a [gold]{encounter}[/gold].'),
    'CrossbowmenNormal': ('突袭弩手', 'Raid Crossbowmen', '{character}倒在了[gold]{encounter}[/gold]的弩箭下。', '{character} fell to the bolts of the [gold]{encounter}[/gold].'),
    'CasterSquadNormal': ('术师小队', 'Caster Squad', '{character}倒在了[gold]{encounter}[/gold]的法术之下。', '{character} fell to the arts of a [gold]{encounter}[/gold].'),
    'CommandoSquadNormal': ('突击者小队', 'Commando Squad', '{character}被[gold]{encounter}[/gold]冲垮了。', '{character} was overrun by a [gold]{encounter}[/gold].'),
    'GuidedArtilleryNormal': ('牵引的炮火', 'Guided Artillery', '{character}没能躲开[gold]{encounter}[/gold]。', '{character} could not escape the [gold]{encounter}[/gold].'),
    'SilentButcherNormal': ('沉默的屠夫', 'The Silent Butcher', '{character}倒在了[gold]{encounter}[/gold]面前。', '{character} fell before [gold]{encounter}[/gold].'),
    'HostCenturionNormal': ('宿主百夫长', 'Host Centurion', '{character}被[gold]{encounter}[/gold]吞噬了。', '{character} was devoured by the [gold]{encounter}[/gold].'),
    'CommandoCaptainNormal': ('突击者队长', 'Commando Captain', '{character}被[gold]{encounter}[/gold]冲垮了。', '{character} was overrun by a [gold]{encounter}[/gold].'),
    'EmperorsBladeElite': ('皇帝的利刃', "The Emperor's Blade", '{character}倒在了[gold]{encounter}[/gold]的国度之中。', "{character} fell within the dominion of [gold]{encounter}[/gold]."),
    'VanguardVeteranElite': ('百战精锐', 'Veteran Vanguard', '{character}倒在了[gold]{encounter}[/gold]面前。', '{character} fell before the [gold]{encounter}[/gold].'),
    'CoreHarbingerElite': ('中枢先兆者', 'Core Harbinger', '{character}没能躲开[gold]{encounter}[/gold]的炮火。', '{character} could not escape the barrage of the [gold]{encounter}[/gold].'),
    'TalulahBoss': ('“不死的黑蛇”', 'Deathless Black Snake', '{character}被[gold]{encounter}[/gold]的烈焰吞没了。', '{character} was consumed by the flames of [gold]{encounter}[/gold].'),
}

# 类名: ((中文名, 英文名), {招式键: (中文, 英文)})
MONSTERS = {
    'InfectedPatrolOfficer': (('感染者纠察官', 'Infected Patrol Officer'),
                              {'BATON': ('警棍', 'Baton'), 'SHOOT': ('射击', 'Shoot'), 'SEARCH': ('搜捕', 'Search')}),
    'SeniorPatrolOfficer': (('感染者高级纠察官', 'Senior Patrol Officer'),
                            {'ESCORT': ('押送', 'Escort'), 'HEAVY_BLOW': ('重击', 'Heavy Blow'), 'COMBO': ('连击', 'Combo')}),
    'UrsusRavager': (('乌萨斯裂兽', 'Ursus Ravager'),
                     {'BITE': ('撕咬', 'Bite'), 'ALERT': ('警戒', 'Alert'), 'POUNCE': ('扑击', 'Pounce')}),
    'RavagerMatriarch': (('乌萨斯裂兽族母', 'Ravager Matriarch'),
                         {'SUMMON': ('召集', 'Rally'), 'HEAL': ('治愈', 'Heal'), 'INSPIRE': ('鼓舞', 'Inspire'), 'INTIMIDATE': ('震慑', 'Intimidate')}),
    'UrsusCrossbowman': (('乌萨斯突袭弩手', 'Ursus Raid Crossbowman'),
                         {'WIND': ('上弦', 'Wind Up'), 'BOLT': ('弩箭', 'Bolt'), 'VOLLEY': ('速射', 'Volley')}),
    'UrsusArmoredCaster': (('乌萨斯着铠术师', 'Ursus Armored Caster'),
                           {'ARTS_BOLT': ('源石法术', 'Arts Bolt'), 'ARTS_COMBO': ('法术连击', 'Arts Combo'), 'ARMOR_UP': ('铠化', 'Armor Up')}),
    'SeniorArmoredCaster': (('乌萨斯高级着铠术师', 'Senior Armored Caster'),
                            {'ARTS_BOLT': ('源石法术', 'Arts Bolt'), 'ARTS_COMBO': ('法术连击', 'Arts Combo'), 'ARMOR_UP': ('铠化', 'Armor Up')}),
    'UrsusCommando': (('乌萨斯突击者', 'Ursus Commando'),
                      {'CHARGE': ('冲锋', 'Charge'), 'CLEAVE': ('重劈', 'Cleave'), 'HOLD_LINE': ('稳阵', 'Hold the Line')}),
    'CommandoCaptain': (('乌萨斯突击者队长', 'Commando Captain'),
                        {'CHARGE': ('冲锋', 'Charge'), 'CLEAVE': ('重劈', 'Cleave'), 'PRESS': ('压阵', 'Press On')}),
    'ArtilleryHarbinger': (('帝国炮火先兆者', 'Imperial Artillery Harbinger'),
                           {'MARK': ('标定', 'Mark Target'), 'BOMBARD': ('炮击', 'Bombard'), 'SCATTER': ('霰弹', 'Scattershot')}),
    'CoreHarbinger': (('帝国炮火中枢先兆者', 'Imperial Core Harbinger'),
                      {'MARK': ('标定', 'Mark Target'), 'BOMBARD': ('炮击', 'Bombard'), 'RAPID_FIRE': ('速射', 'Rapid Fire')}),
    'ImperialVanguard': (('帝国前锋精锐', 'Imperial Vanguard'),
                         {'LONG_SHOT': ('远射', 'Long Shot'), 'THRUST': ('突刺', 'Thrust'), 'RESOLVE': ('战意', 'Resolve')}),
    'VanguardVeteran': (('帝国前锋百战精锐', 'Imperial Vanguard Veteran'),
                        {'LONG_SHOT': ('远射', 'Long Shot'), 'THRUST': ('突刺', 'Thrust'), 'RESOLVE': ('战意', 'Resolve')}),
    'HostHeavySoldier': (('宿主重装士兵', 'Host Heavy Soldier'),
                         {'SHIELD_BASH': ('盾击', 'Shield Bash'), 'HOLD': ('坚守', 'Hold Fast'), 'CRUSH': ('重压', 'Crush')}),
    'SarkazHostCenturion': (('萨卡兹宿主百夫长', 'Sarkaz Host Centurion'),
                            {'SIPHON': ('汲取', 'Siphon'), 'DEVOUR': ('群噬', 'Devour'), 'CHANT': ('吟唱', 'Chant')}),
    'EmperorsBladeHunter': (('“皇帝的利刃”，追猎者', '"Emperor\'s Blade", Pursuer'),
                            {'PATROL': ('巡猎', 'Hunt'), 'SLASH': ('斩击', 'Slash'), 'DREAD': ('坍缩恐惧', 'Collapsing Dread'),
                             'REFORM': ('重整', 'Reform'), 'REND': ('近战连斩', 'Rending Blades')}),
    'Talulah': (('“不死的黑蛇”', 'Deathless Black Snake'),
                {'IGNITE': ('点燃', 'Ignite'), 'SNAKE_BITE': ('噬咬', 'Serpent Bite'), 'DETONATE': ('引爆', 'Detonate'),
                 'SCORCH': ('焦土', 'Scorched Earth'), 'UNDYING_FLAME': ('不息恶火', 'Deathless Inferno'),
                 'FIRE_RAIN': ('火雨', 'Rain of Fire')}),
    'Fireball': (('火球', 'Fireball'),
                 {'EXPLODE': ('爆炸', 'Explode')}),
}

# 类名: (中文标题, 英文标题, 中文说明, 英文说明, 中文 smart, 英文 smart)
POWERS = {
    'RendPower': (
        '撕裂', 'Rend',
        '每次对你造成未被格挡的攻击伤害时，你将在下回合失去[blue]{Amount}[/blue]点[gold]力量[/gold]。',
        'Whenever it deals unblocked attack damage to you, lose [blue]{Amount}[/blue] [gold]Strength[/gold] next turn.',
        '每当[gold]{OwnerName}[/gold]对你造成未被格挡的攻击伤害时，你将在下回合失去[blue]{Amount}[/blue]点[gold]力量[/gold]。',
        'Whenever [gold]{OwnerName}[/gold] deals unblocked attack damage to you, lose [blue]{Amount}[/blue] [gold]Strength[/gold] next turn.'),
    'TraumaPower': (
        '创伤', 'Trauma',
        '本回合内失去[blue]{Amount}[/blue]点[gold]力量[/gold]。',
        'Lose [blue]{Amount}[/blue] [gold]Strength[/gold] this turn.',
        '本回合内失去[blue]{Amount}[/blue]点[gold]力量[/gold]。',
        'Lose [blue]{Amount}[/blue] [gold]Strength[/gold] this turn.'),
    'PackInstinctPower': (
        '族群意识', 'Pack Instinct',
        '每当一只乌萨斯裂兽死亡时，自身获得[gold]力量[/gold]。',
        'Whenever an allied Ursus Ravager dies, gains [gold]Strength[/gold].',
        '每当一只乌萨斯裂兽死亡时，自身获得[blue]{Amount}[/blue]点[gold]力量[/gold]。',
        'Whenever an allied Ursus Ravager dies, gains [blue]{Amount}[/blue] [gold]Strength[/gold].'),
    'FlareRebirthPower': (
        '重生', 'Rebirth',
        '被击败时不会立刻退场，之后进入下一阶段。',
        'When defeated, does not leave right away; it enters its next phase instead.',
        '被击败时不会立刻退场，之后进入下一阶段（还剩[blue]{Amount}[/blue]次）。',
        'When defeated, does not leave right away; it enters its next phase instead ([blue]{Amount}[/blue] left).'),
    # 句式照原版适者生存（ADAPTABLE_POWER）与接续（REATTACH_POWER）。
    'BlackSnakeBodyPower': (
        '黑蛇之躯', 'Body of the Black Snake',
        '当这个怪物将要被击败时，它会消失并释放[gold]不息恶火[/gold]，在[blue]3[/blue]回合后复活变得更加强大。',
        'When this creature would be defeated, it instead vanishes and casts [gold]Deathless Inferno[/gold], reviving even stronger in [blue]3[/blue] turns.',
        '当[gold]{OwnerName}[/gold]将要被击败时，它会消失并释放[gold]不息恶火[/gold]，在[blue]3[/blue]回合后复活变得更加强大。',
        'When [gold]{OwnerName}[/gold] would be defeated, it instead vanishes and casts [gold]Deathless Inferno[/gold], reviving even stronger in [blue]3[/blue] turns.'),
    # 隐藏的复活机制（不显示），文案只为防止缺键。
    'TalulahRebirthPower': (
        '重生', 'Rebirth',
        '被击败时不会立刻退场，之后进入下一阶段。',
        'When defeated, does not leave right away; it enters its next phase instead.',
        '被击败时不会立刻退场，之后进入下一阶段。',
        'When defeated, does not leave right away; it enters its next phase instead.'),
    'IgnitionPower': (
        '引燃', 'Ignition',
        '你打出的牌会被侵蚀为[gold]引燃[/gold]，每打出一次增加层数。',
        'Cards you play are afflicted with [gold]Ignited[/gold], gaining stacks each time they are played.',
        '你打出的牌会被侵蚀为[gold]引燃[/gold]，每打出一次增加[blue]{Amount}[/blue]层。',
        'Cards you play are afflicted with [gold]Ignited[/gold], gaining [blue]{Amount}[/blue] stacks each time they are played.'),
    'DominionPower': (
        '国度', 'Dominion',
        '[gold]坍缩恐惧[/gold]每层额外造成[blue]3[/blue]点伤害，并使目标[gold]缠绕[/gold]。',
        '[gold]Collapsing Dread[/gold] deals [blue]3[/blue] more damage per stack and [gold]Tangles[/gold] its targets.',
        '[gold]坍缩恐惧[/gold]额外造成[blue]{Amount}[/blue]×3点伤害，并使目标[gold]缠绕[/gold]。',
        '[gold]Collapsing Dread[/gold] deals [blue]{Amount}[/blue]×3 more damage and [gold]Tangles[/gold] its targets.'),
    'HunterSovereignPower': (
        '内卫', 'Imperial Guard',
        '生命越过阈值时获得1层[gold]国度[/gold]（一形态：50%；二形态：75%、50%、25%）。',
        'Gains 1 [gold]Dominion[/gold] each time its HP crosses a threshold (first form: 50%; second form: 75%, 50%, 25%).',
        '生命越过阈值时获得1层[gold]国度[/gold]（一形态：50%；二形态：75%、50%、25%）。',
        'Gains 1 [gold]Dominion[/gold] each time its HP crosses a threshold (first form: 50%; second form: 75%, 50%, 25%).'),
    'ArtilleryMarkPower': (
        '弹着点', 'Artillery Mark',
        '下个敌方回合，炮弹会对你造成伤害；即使先兆者已被击败，炮弹也会落地。',
        'Next enemy turn, a shell lands on you. It lands even if the Harbinger has been defeated.',
        '下个敌方回合，炮弹会对你造成[blue]{Amount}[/blue]点伤害；即使先兆者已被击败，炮弹也会落地。',
        'Next enemy turn, a shell deals [blue]{Amount}[/blue] damage to you. It lands even if the Harbinger has been defeated.'),
    'LastStandPower': (
        '背水', 'Last Stand',
        '生命第一次降到一半以下时，获得[gold]力量[/gold]。',
        'The first time its HP falls below half, gains [gold]Strength[/gold].',
        '生命第一次降到一半以下时，获得[blue]{Amount}[/blue]点[gold]力量[/gold]。',
        'The first time its HP falls below half, gains [blue]{Amount}[/blue] [gold]Strength[/gold].'),
    'PermanentRegenPower': (
        '永续再生', 'Endless Regeneration',
        '回合结束时回复[blue]3[/blue]点生命。',
        'At the end of its turn, heals [blue]3[/blue] HP.',
        '[gold]{OwnerName}[/gold]在回合结束时回复[blue]{Amount}[/blue]点生命。',
        '[gold]{OwnerName}[/gold] heals [blue]{Amount}[/blue] HP at the end of its turn.'),
    'SiphonPower': (
        '汲取', 'Siphon',
        '[gold]汲取[/gold]造成的未被格挡伤害，按[blue]150%[/blue]治疗自己。',
        '[gold]Siphon[/gold] heals it for [blue]150%[/blue] of the unblocked damage it deals.',
        '[gold]汲取[/gold]造成的未被格挡伤害，按[blue]{Amount}%[/blue]治疗自己。',
        '[gold]Siphon[/gold] heals it for [blue]{Amount}%[/blue] of the unblocked damage it deals.'),
}


# 卡牌侵蚀，类名: (中文标题, 英文标题, 中文说明, 英文说明, 中文卡面附加文字, 英文卡面附加文字)
# 措辞照原版流电（GALVANIZED）：“打出这张牌时受到X点伤害。” / 卡面“受到X点伤害。”
AFFLICTIONS = {
    'Ignited': (
        '引燃', 'Ignited',
        '打出这张牌时受到[blue]{Amount}[/blue]点伤害。',
        'Take [blue]{Amount}[/blue] damage when this card is played.',
        '受到{Amount}点伤害。',
        'Take {Amount} damage.'),
}


def write(lang: str, table: str, data: dict) -> None:
    path = ROOT / lang / f'{table}.json'
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


for i, lang in enumerate(('zhs', 'eng')):
    act = f'{PREFIX}_ACT_ROARING_FLARE_ACT'
    write(lang, 'acts', {f'{act}.title': ACT['title'][i], f'{act}.description': ACT['description'][i]})

    encounters = {}
    for cls, texts in ENCOUNTERS.items():
        key = f'{PREFIX}_ENCOUNTER_{slug(cls)}'
        encounters[f'{key}.title'] = texts[i]
        encounters[f'{key}.loss'] = texts[2 + i]
    write(lang, 'encounters', encounters)

    monsters = {}
    for cls, (names, moves) in MONSTERS.items():
        key = f'{PREFIX}_MONSTER_{slug(cls)}'
        monsters[f'{key}.name'] = names[i]
        for move, titles in moves.items():
            monsters[f'{key}.moves.{move}.title'] = titles[i]
    write(lang, 'monsters', monsters)

    powers = {}
    for cls, texts in POWERS.items():
        key = f'{PREFIX}_POWER_{slug(cls)}'
        powers[f'{key}.title'] = texts[i]
        powers[f'{key}.description'] = texts[2 + i]
        powers[f'{key}.smartDescription'] = texts[4 + i]
    write(lang, 'powers', powers)

    afflictions = {}
    for cls, texts in AFFLICTIONS.items():
        key = f'{PREFIX}_AFFLICTION_{slug(cls)}'
        afflictions[f'{key}.title'] = texts[i]
        afflictions[f'{key}.description'] = texts[2 + i]
        afflictions[f'{key}.extraCardText'] = texts[4 + i]
    write(lang, 'afflictions', afflictions)

print('written', sorted(p.name for p in (ROOT / 'zhs').iterdir()))
