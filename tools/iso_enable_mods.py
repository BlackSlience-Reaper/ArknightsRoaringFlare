"""隔离 HOME 的离线档 settings.save：标记已看过模组警告，只启用参数里列出的模组（其余存在的模组禁用）。"""
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
enabled = set(sys.argv[2:])
data = json.loads(path.read_text())
ms = data.get('mod_settings') or {}
data['mod_settings'] = ms
ms['mods_enabled'] = True
mods = {m['id']: m for m in ms.get('mod_list', [])}
for mod_id in enabled | set(mods):
    mods.setdefault(mod_id, {'id': mod_id, 'source': 'mods_directory'})
    mods[mod_id]['is_enabled'] = mod_id in enabled
ms['mod_list'] = list(mods.values())
path.write_text(json.dumps(data, indent=2))
print(json.dumps(ms, ensure_ascii=False))
