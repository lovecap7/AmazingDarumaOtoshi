# -*- coding: utf-8 -*-
"""Verify the workbook against AchievementCatalog.cs."""

import re
from collections import Counter

import openpyxl

CS = r"C:\AmazingDarumaOtoshi\AmaizingDarumaOtoshi\Assets\Yurube\Achievements\AchievementCatalog.cs"
XLSX = r"C:\AmazingDarumaOtoshi\Docs\実績一覧_2026-11-08.xlsx"

src = open(CS, encoding="utf-8-sig").read()
pattern = re.compile(
    r'new AchievementDefinition\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)",\s*'
    r'AchievementCategory\.(\w+),\s*"([^"]+)",\s*AchievementMetric\.(\w+),\s*(\d+),\s*'
    r'AchievementRarity\.(\w+)(?:,\s*(true|false))?(?:,\s*(true|false))?\)',
    re.S,
)
matches = pattern.findall(src)
assert len(matches) == 26, len(matches)

CATEGORY = {"Match": "試合", "Attack": "攻撃", "Chain": "連鎖", "Stack": "積み木", "Challenge": "挑戦"}
RARITY = {"Bronze": "銅", "Silver": "銀", "Gold": "金", "Platinum": "プラチナ", "Diamond": "ダイヤモンド"}

wb = openpyxl.load_workbook(XLSX)
ws = wb["実績一覧"]
assert ws.max_column == 14

rows = []
for r in range(5, 31):
    rows.append([ws.cell(row=r, column=c).value for c in range(1, 15)])

errors = []
for i, (ach_id, name, poem, category, condition, metric, target, rarity, special, secret) in enumerate(matches):
    row = rows[i]
    num, x_name, x_rarity, x_kind, x_category, x_condition, x_poem, x_metric, x_count, x_target, x_timing, x_id, x_reward, x_note = row
    if num != i + 1:
        errors.append(f"{i+1}: number {num}")
    if x_id != ach_id:
        errors.append(f"{i+1}: id {x_id} != {ach_id}")
    if x_name != name:
        errors.append(f"{i+1}: name {x_name!r} != {name!r}")
    if x_poem != poem:
        errors.append(f"{i+1}: poem {x_poem!r} != {poem!r}")
    if x_condition != condition:
        errors.append(f"{i+1}: condition {x_condition!r} != {condition!r}")
    if x_category != CATEGORY[category]:
        errors.append(f"{i+1}: category {x_category} != {CATEGORY[category]}")
    if x_rarity != RARITY[rarity]:
        errors.append(f"{i+1}: rarity {x_rarity} != {RARITY[rarity]}")
    if x_metric.split(" (")[0] == "":
        errors.append(f"{i+1}: metric label empty")
    if f"({metric})" not in x_metric:
        errors.append(f"{i+1}: metric {x_metric!r} lacks {metric}")
    # dynamic targets
    if metric not in ("CharacterUsedDistinct", "AllRarityUnlocked", "AllOtherUnlocked"):
        if int(x_target) != int(target):
            errors.append(f"{i+1}: target {x_target} != {target}")
    # kind / flags
    if secret == "true":
        expected_kind = "シークレット"
    elif special == "true":
        expected_kind = "レアリティ制覇"
    elif rarity == "Diamond":
        expected_kind = "最高位"
    else:
        expected_kind = "通常"
    if x_kind != expected_kind:
        errors.append(f"{i+1}: kind {x_kind} != {expected_kind}")

# expected dynamic targets from the current roster / meta rules
expected_targets = {
    "all_characters": 7,
    "bronze_master": 9,
    "silver_master": 4,
    "gold_master": 4,
    "platinum_master": 4,
    "all_non_platinum": 25,
}
for i, row in enumerate(rows):
    ach_id = row[11]
    if ach_id in expected_targets and int(row[9]) != expected_targets[ach_id]:
        errors.append(f"{i+1}: dynamic target {row[9]} != {expected_targets[ach_id]}")

# summary counts
rarity_counts = Counter(row[2] for row in rows if row[3] != "レアリティ制覇")
assert rarity_counts == {"銅": 9, "銀": 4, "金": 4, "プラチナ": 4, "ダイヤモンド": 1}, rarity_counts
cat_counts = Counter(row[4] for row in rows)
assert cat_counts == {"試合": 5, "攻撃": 7, "連鎖": 4, "積み木": 3, "挑戦": 7}, cat_counts
assert Counter(row[3] for row in rows) == {"通常": 20, "レアリティ制覇": 4, "最高位": 1, "シークレット": 1}

print("rows", len(rows), "errors", len(errors))
for e in errors:
    print(" -", e)
assert not errors
print("OK")
