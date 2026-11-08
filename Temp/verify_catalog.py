# -*- coding: utf-8 -*-
import re
from collections import Counter

p = r"C:\AmazingDarumaOtoshi\AmaizingDarumaOtoshi\Assets\Yurube\Achievements\AchievementCatalog.cs"
s = open(p, encoding="utf-8-sig").read()

pat = re.compile(
    r'new AchievementDefinition\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)",\s*'
    r'AchievementCategory\.(\w+),\s*"([^"]+)",\s*AchievementMetric\.(\w+),\s*(\d+),\s*'
    r'AchievementRarity\.(\w+)(?:,\s*(true|false))?(?:,\s*(true|false))?\)',
    re.S,
)
ms = pat.findall(s)
print("count", len(ms))
print("unique ids", len({m[0] for m in ms}))
for i, m in enumerate(ms, 1):
    print(i, m[0], m[1], m[7], m[5], m[6], "special=" + (m[8] or ""), "secret=" + (m[9] or ""))
print("rarity", Counter(m[7] for m in ms))
print("category", Counter(m[3] for m in ms))
print("metric", Counter(m[5] for m in ms))
