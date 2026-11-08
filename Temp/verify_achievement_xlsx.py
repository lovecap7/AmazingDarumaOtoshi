# -*- coding: utf-8 -*-
"""Verify the 39-row achievement workbook against AchievementCatalog.cs."""
import re
from collections import Counter
from openpyxl import load_workbook

XLSX = r"C:\AmazingDarumaOtoshi\Docs\実績一覧_2026-11-08.xlsx"
CS = r"C:\AmazingDarumaOtoshi\AmaizingDarumaOtoshi\Assets\Yurube\Achievements\AchievementCatalog.cs"

RARITY_JP = {
    "Bronze": "銅",
    "Silver": "銀",
    "Gold": "金",
    "Platinum": "プラチナ",
    "Diamond": "ダイヤモンド",
}
CATEGORY_JP = {
    "Match": "試合",
    "Attack": "攻撃",
    "Chain": "連鎖",
    "Stack": "積み木",
    "Challenge": "挑戦",
}

source = open(CS, encoding="utf-8-sig").read()
pattern = re.compile(
    r'new AchievementDefinition\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)",\s*'
    r'AchievementCategory\.(\w+),\s*"([^"]+)",\s*AchievementMetric\.(\w+),\s*(\d+),\s*'
    r'AchievementRarity\.(\w+)(?:,\s*(true|false))?(?:,\s*(true|false))?\)',
    re.S,
)
catalog = pattern.findall(source)
assert len(catalog) == 39, len(catalog)
assert len({c[0] for c in catalog}) == 39

wb = load_workbook(XLSX, data_only=False)
print("sheets:", wb.sheetnames)
ws = wb["実績一覧"]
print("実績一覧 dimensions:", ws.dimensions, "filter:", ws.auto_filter.ref, "print:", ws.print_area)

rows = []
for r in range(5, 44):
    values = [ws.cell(row=r, column=c).value for c in range(1, 15)]
    rows.append(values)

assert len(rows) == 39
assert [r[0] for r in rows] == list(range(1, 40))
assert len({r[11] for r in rows}) == 39
assert rows[-1][11] == "secret_quick_win"
assert sum(1 for r in rows if r[3] == "レアリティ制覇") == 4
assert sum(1 for r in rows if r[3] == "最高位") == 1
assert sum(1 for r in rows if r[3] == "シークレット") == 3
assert Counter(r[2] for r in rows if r[3] != "レアリティ制覇") == {
    "銅": 12, "銀": 9, "金": 7, "プラチナ": 6, "ダイヤモンド": 1,
}

# Compare workbook rows with the C# catalog, in the same order.
for i, (wb_row, c_row) in enumerate(zip(rows, catalog), start=1):
    ach_id, name, poem, category, condition, metric, _target, rarity, special, secret = c_row
    assert wb_row[1] == name, (i, wb_row[1], name)
    assert wb_row[2] == RARITY_JP[rarity], (i, wb_row[2], rarity)
    assert wb_row[4] == CATEGORY_JP[category], (i, wb_row[4], category)
    assert wb_row[5] == condition, (i, wb_row[5], condition)
    assert wb_row[6] == poem, (i, wb_row[6], poem)
    assert wb_row[11] == ach_id, (i, wb_row[11], ach_id)
    assert ("シークレット" in wb_row[3]) == (secret == "true"), (i, wb_row[3], secret)

print("rows verified:", len(rows))
print("last:", rows[-1][0], rows[-1][1], rows[-1][11])
print("rarity (excluding metas):", Counter(r[2] for r in rows if r[3] != "レアリティ制覇"))
print("category:", Counter(r[4] for r in rows))

ws2 = wb["集計"]
print("集計 dimensions:", ws2.dimensions, "print:", ws2.print_area)

overview = {ws2.cell(row=r, column=1).value: ws2.cell(row=r, column=2).value for r in range(6, 13)}
print("overview:", overview)
assert overview["総実績数"] == 39
assert overview["通常実績"] == 31
assert overview["レアリティ制覇（特別）"] == 4
assert overview["最高位（ダイヤモンド）"] == 1
assert overview["シークレット"] == 3
assert overview["レアリティ集計の対象"] == 35
assert overview["報酬キャラクター"] == "だるきんぐ"

rarity = [(ws2.cell(row=r, column=1).value, ws2.cell(row=r, column=2).value, ws2.cell(row=r, column=7).value)
          for r in range(16, 21)]
print("rarity table:", rarity)
assert [x[1] for x in rarity] == [12, 9, 7, 6, 1]
assert [x[2] for x in rarity] == [12, 9, 7, 6, 38]
assert ws2["B21"].value == 35

cats = [(ws2.cell(row=r, column=1).value, ws2.cell(row=r, column=2).value) for r in range(26, 31)]
print("category table:", cats)
assert [x[1] for x in cats] == [9, 12, 6, 4, 8]
assert ws2["B31"].value == 39

metric_counts = [ws2.cell(row=r, column=2).value for r in range(35, 59)]
print("metric rows:", len(metric_counts), "sum:", sum(metric_counts))
assert len(metric_counts) == 24
assert sum(metric_counts) == 39
assert ws2["B59"].value == 39

meta = [(ws2.cell(row=r, column=1).value, ws2.cell(row=r, column=2).value) for r in range(63, 69)]
print("meta table:", meta)
assert [x[1] for x in meta] == [12, 9, 7, 6, 38, 7]
assert ws2["A61"].value == "5. メタ実績・自動目標"
assert ws2["A70"].value == "6. 注記"

ws3 = wb["補足・提案"]
print("補足 dimensions:", ws3.dimensions, "print:", ws3.print_area)
assert ws3["A45"].value == "4. 更新履歴"
assert ws3["A46"].value == "2026/11/08"
assert "全39件" in ws3["A2"].value
assert "レアリティ集計は35件" in ws3["A38"].value
assert "銅12／銀9／金7／白金6／完全38" in ws3["A39"].value
assert "追加13件の判定は仮決め" in ws3["A40"].value
assert "パッシブ発動は各キャラの初回" in ws3["A41"].value
assert "追加13件を反映して全39件版" in ws3["B48"].value

print("ALL CHECKS PASSED")
