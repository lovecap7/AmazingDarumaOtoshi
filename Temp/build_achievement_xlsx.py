# -*- coding: utf-8 -*-
"""Build the achievement master list workbook from AchievementCatalog.cs data."""

from collections import Counter

from openpyxl import Workbook
from openpyxl.comments import Comment
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter
from openpyxl.worksheet.properties import PageSetupProperties

OUT = r"C:\AmazingDarumaOtoshi\Docs\実績一覧_2026-11-08.xlsx"

# ----------------------------------------------------------------------------
# palette (matches AchievementRarityInfo)
# ----------------------------------------------------------------------------
INK = "2B2B2B"
GRAY = "6B6257"
LIGHT_GRAY = "9A9186"
GREEN_DARK = "245C42"
GREEN = "2E6B4F"
GREEN_PALE = "E7F0E9"
CREAM = "FBF8F2"
BAND = "F7F3EA"
BORDER = "D8D0C4"
WHITE = "FFFFFF"

RARITY = {
    "銅":         {"start": "C18A60", "end": "87502F", "text": INK},
    "銀":         {"start": "DCE2EA", "end": "8795A7", "text": INK},
    "金":         {"start": "F2D16B", "end": "AD7C1F", "text": INK},
    "プラチナ":   {"start": "A6E3F0", "end": "428FAF", "text": INK},
    "ダイヤモンド": {"start": "C5A4FF", "end": "6840A8", "text": INK},
}

KIND_FILL = {
    "通常": WHITE,
    "レアリティ制覇": "F8E4D3",
    "最高位": "EADFFF",
    "シークレット": "E6E2DC",
}

THIN = Side(style="thin", color=BORDER)
MEDIUM = Side(style="medium", color="B9AE9D")
FULL = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)

BODY = Font(name="Meiryo", size=10, color=INK)
BODY_S = Font(name="Meiryo", size=9, color=INK)
BODY_XS = Font(name="Meiryo", size=8.5, color=GRAY)
BOLD = Font(name="Meiryo", size=10, bold=True, color=INK)
BOLD_W = Font(name="Meiryo", size=10, bold=True, color=WHITE)
MONO = Font(name="Consolas", size=9, color="4A4238")

WRAP_TOP = Alignment(horizontal="left", vertical="top", wrap_text=True)
WRAP_CENTER = Alignment(horizontal="center", vertical="center", wrap_text=True)
CENTER = Alignment(horizontal="center", vertical="center")
LEFT = Alignment(horizontal="left", vertical="center")
LEFT_WRAP = Alignment(horizontal="left", vertical="center", wrap_text=True)
DATA_ALIGN = Alignment(horizontal="left", vertical="center", wrap_text=True)


def fill(hex_color):
    return PatternFill("solid", start_color=hex_color, end_color=hex_color)


def style(cell, font=BODY, fill_color=None, align=WRAP_TOP, border=FULL, number_format=None):
    cell.font = font
    cell.alignment = align
    if fill_color:
        cell.fill = fill(fill_color)
    if border:
        cell.border = border
    if number_format:
        cell.number_format = number_format


# ----------------------------------------------------------------------------
# master data (AchievementCatalog.CreateDefaults order = AchievementManager.Number)
# ----------------------------------------------------------------------------
# (name, rarity, kind, category, condition, poem, metric label, count style,
#  target, timing, id, secret, special, reward, note)
ROWS = [
    ("はじめの一歩", "銅", "通常", "試合",
     "初めて試合をプレイする",
     "だるま落としデビュー。まずは一戦、転がしてみよう。",
     "試合プレイ回数 (MatchPlayed)", "累計", 1, "試合開始時",
     "first_match", "—", "—", "—", "最初の1試合で解除"),
    ("常連さん", "銅", "通常", "試合",
     "累計10試合プレイする",
     "10試合をプレイ。道場の常連さん、今日も元気。",
     "試合プレイ回数 (MatchPlayed)", "累計", 10, "試合開始時",
     "play_10", "—", "—", "—", "累計カウント"),
    ("歴戦の猛者", "金", "通常", "試合",
     "累計50試合プレイする",
     "50試合をプレイ。積み木の山もあなたを見上げている。",
     "試合プレイ回数 (MatchPlayed)", "累計", 50, "試合開始時",
     "play_50", "—", "—", "—", "累計カウント"),
    ("初勝利！", "銅", "通常", "試合",
     "初めて試合に勝利する",
     "初めての勝利。この味、忘れない。",
     "勝利回数 (MatchWon)", "累計", 1, "試合終了時（勝利）",
     "first_win", "—", "—", "—", "引き分け・敗北では加算されない"),
    ("勝利の味", "金", "通常", "試合",
     "累計10回勝利する",
     "通算10勝。勝ち方はもう、体が覚えている。",
     "勝利回数 (MatchWon)", "累計", 10, "試合終了時（勝利）",
     "win_10", "—", "—", "—", "累計カウント"),
    ("初ヒット！", "銅", "通常", "攻撃",
     "初めて相手に攻撃を命中させる",
     "初めて相手に一撃。手ごたえ、あったでしょ？",
     "命中回数 (Hit)", "累計", 1, "命中時",
     "first_hit", "—", "—", "—", "最初の命中で解除"),
    ("百発百中？", "銀", "通常", "攻撃",
     "累計100回攻撃を命中させる",
     "通算100発命中。はずす気、ありますか？",
     "命中回数 (Hit)", "累計", 100, "命中時",
     "hit_100", "—", "—", "—", "累計カウント"),
    ("コンボの達人", "銀", "通常", "攻撃",
     "1試合中に10回攻撃を命中させる",
     "1試合で10回命中。もう止まらない。",
     "1試合の命中回数 (HitInMatch)", "単試合", 10, "命中時（試合開始でリセット）",
     "combo_10", "—", "—", "—", "試合をまたぐとカウントは0に戻る"),
    ("打ち返し入門", "銅", "通常", "攻撃",
     "初めてカウンター・打ち返しを成功させる",
     "飛んできた積み木を、ハンマーでおかえし。",
     "打ち返し回数 (Counter)", "累計", 1, "打ち返し成功時",
     "counter_first", "—", "—", "—", "最初の成功で解除"),
    ("反撃の達人", "金", "通常", "攻撃",
     "累計20回カウンター・打ち返しを成功させる",
     "通算20回の打ち返し。受け身はもう得意技。",
     "打ち返し回数 (Counter)", "累計", 20, "打ち返し成功時",
     "counter_20", "—", "—", "—", "累計カウント"),
    ("完璧な反撃", "プラチナ", "通常", "攻撃",
     "カウンター・打ち返しによって相手を撃破する",
     "打ち返した積み木で相手の頭を撃破。",
     "打ち返し撃破回数 (CounterKO)", "累計", 1, "打ち返しで撃破時",
     "counter_ko", "—", "—", "—", "撃破の瞬間に判定"),
    ("連鎖の始まり", "銅", "通常", "連鎖",
     "初めて連鎖を発生させる",
     "積み木が積み木を巻き込んだ。連鎖の幕開け。",
     "最大連鎖数 (ChainBest)", "最大記録", 1, "連鎖発生時",
     "chain_first", "—", "—", "—", "最初の連鎖で解除"),
    ("連鎖のプロ", "銅", "通常", "連鎖",
     "3個以上のブロックを連鎖させる",
     "3個以上の連鎖。もう職人の域。",
     "最大連鎖数 (ChainBest)", "最大記録", 3, "連鎖発生時",
     "chain_3", "—", "—", "—", "自己ベストで判定"),
    ("連鎖マスター", "銀", "通常", "連鎖",
     "5個以上のブロックを連鎖させる",
     "5個以上の連鎖。見ているだけで気持ちいい。",
     "最大連鎖数 (ChainBest)", "最大記録", 5, "連鎖発生時",
     "chain_5", "—", "—", "—", "自己ベストで判定"),
    ("大連鎖！", "プラチナ", "通常", "連鎖",
     "10個以上のブロックを連鎖させる",
     "10個以上の大連鎖。もはやお祭り。",
     "最大連鎖数 (ChainBest)", "最大記録", 10, "連鎖発生時",
     "chain_10", "—", "—", "—", "自己ベストで判定"),
    ("高く積み上げろ", "銅", "通常", "積み木",
     "自分のブロックを5個積み上げる",
     "自分の積み木を5個。いい調子！",
     "最大積み上げ数 (StackBest)", "最大記録", 5, "積み上げ時",
     "stack_5", "—", "—", "—", "自己ベストで判定"),
    ("天まで届け", "銀", "通常", "積み木",
     "自分のブロックを10個積み上げる",
     "自分の積み木を10個。天まで届け！",
     "最大積み上げ数 (StackBest)", "最大記録", 10, "積み上げ時",
     "stack_10", "—", "—", "—", "自己ベストで判定"),
    ("崖っぷちの勝利", "プラチナ", "通常", "積み木",
     "ブロックが1個以下の状態で試合に勝利する",
     "積み木1個以下で勝利。よくぞ耐えた！",
     "低積み木勝利回数 (WinWithLowStack)", "累計", 1, "試合終了時（勝利）",
     "clutch_win", "—", "—", "—", "勝利時の残り積み木で判定"),
    ("必殺技解禁！", "銅", "通常", "挑戦",
     "初めて必殺技を発動する",
     "初めて必殺技を発動。合図はボタンひとつ。",
     "必殺技発動回数 (SpecialActivated)", "累計", 1, "必殺技発動時",
     "first_special", "—", "—", "—", "最初の発動で解除"),
    ("みんなの力", "金", "通常", "挑戦",
     "全キャラクターを最低1回ずつ使用する",
     "全キャラクターを一度ずつ使用。だるまの輪、完成。",
     "使用キャラ種類数 (CharacterUsedDistinct)", "種類数", 7, "キャラ使用時",
     "all_characters", "—", "—", "—",
     "必要数は使用可能キャラ数に自動連動（現在7）。prefab を持つキャラのみ数える"),
    ("銅の覇者", "銅", "レアリティ制覇", "挑戦",
     "すべての銅実績を解除する",
     "銅の実績をぜんぶ制覇。最初の一歩を、ぜんぶ踏みしめた。",
     "同レアリティ制覇 (AllRarityUnlocked)", "自動判定", 12, "他の実績解除時に自動",
     "bronze_master", "—", "○", "—",
     "銅の通常実績12件が対象。レアリティ別集計からは除外"),
    ("銀の覇者", "銀", "レアリティ制覇", "挑戦",
     "すべての銀実績を解除する",
     "銀の実績をぜんぶ制覇。中級者の肩書き、しっかり手にした。",
     "同レアリティ制覇 (AllRarityUnlocked)", "自動判定", 9, "他の実績解除時に自動",
     "silver_master", "—", "○", "—",
     "銀の通常実績9件が対象。レアリティ別集計からは除外"),
    ("黄金の覇者", "金", "レアリティ制覇", "挑戦",
     "すべての金実績を解除する",
     "金の実績をぜんぶ制覇。黄金にふさわしい立ち回り、ここに極まれり。",
     "同レアリティ制覇 (AllRarityUnlocked)", "自動判定", 7, "他の実績解除時に自動",
     "gold_master", "—", "○", "—",
     "金の通常実績7件が対象。レアリティ別集計からは除外"),
    ("白金の覇者", "プラチナ", "レアリティ制覇", "挑戦",
     "すべてのプラチナ実績を解除する",
     "プラチナの実績をぜんぶ制覇。白銀の輝きは、熟練の証。",
     "同レアリティ制覇 (AllRarityUnlocked)", "自動判定", 6, "他の実績解除時に自動",
     "platinum_master", "—", "○", "—",
     "プラチナ6件（counter_ko, chain_10, clutch_win, multi_ko, no_damage_win, 千本ノック）が対象。レアリティ別集計からは除外"),
    ("完全なる制覇", "ダイヤモンド", "最高位", "挑戦",
     "すべての実績を解除する",
     "ほかの実績はぜんぶ制覇。最後に残ったのは、この紫の輝き。",
     "全実績制覇 (AllOtherUnlocked)", "自動判定", 38, "他の実績解除時に自動",
     "all_non_platinum", "—", "—", "だるきんぐ解放",
     "自分以外の全38件が対象。IDは旧仕様のまま変更しない"),
    ("千本ノック", "プラチナ", "シークレット", "攻撃",
     "累計1000回攻撃を命中させる",
     "千の一撃。打ち続けた者だけが辿り着く、静かな境地。",
     "命中回数 (Hit)", "累計", 1000, "命中時",
     "secret_1000_hits", "○", "—", "—",
     "未解除時は名前・条件・レアリティを伏せる。白金の覇者の対象に含まれる"),

    # ---- 追加13件（2026/11/08 追加実績案） ----
    ("転んでも大丈夫", "銅", "通常", "試合",
     "初めて試合に敗北する",
     "初めての敗北。ここからが、本当のだるま落とし。",
     "敗北回数 (MatchLost)", "累計", 1, "試合終了時（敗北）",
     "first_loss", "—", "—", "—",
     "CPU勝利の敗北で加算。引き分けは敗北に含めない"),
    ("個性、発動！", "銅", "通常", "挑戦",
     "初めてキャラクターのパッシブを発動する",
     "キャラの個性が目を覚ました。この子、ただ者じゃない。",
     "パッシブ発動 (PassiveActivated)", "初回", 1, "パッシブ初回発動時",
     "passive_first", "—", "—", "—",
     "キャラごとに初回のみ。赤・青は1個目の積み木で発動"),
    ("いろいろ積んでみた", "銅", "通常", "積み木",
     "1試合中に3種類以上のブロックを同時に積む",
     "色とりどりの積み木タワー。バランスも、気持ちも、カラフル。",
     "積み木の種類数 (BlockTypesInStack)", "単試合", 3, "積み上げ時",
     "stack_variety", "—", "—", "—",
     "自分の積み木の色種類を判定。3色そろった瞬間に解除"),
    ("返し名人", "銀", "通常", "攻撃",
     "累計5回カウンター・打ち返しを成功させる",
     "5回の打ち返し。ハンマーさばき、見えてきたね。",
     "打ち返し回数 (Counter)", "累計", 5, "打ち返し成功時",
     "counter_5", "—", "—", "—", "累計カウント"),
    ("三連勝！", "銀", "通常", "試合",
     "3試合連続で勝利する",
     "3連勝。流れは完全にあなたのもの。",
     "連勝数 (WinStreakBest)", "最大記録", 3, "試合終了時（勝利）",
     "win_streak_3", "—", "—", "—",
     "引き分けは連勝維持、敗北で0に戻る"),
    ("ピンポン名人", "銀", "通常", "攻撃",
     "同じ積み木を打ち返し合って3回続ける",
     "行って、返って、また来た。積み木が行き交う名勝負。",
     "ラリー回数 (RallyBest)", "最大記録", 3, "打ち返し成功時",
     "rally_3", "—", "—", "—",
     "同じ積み木の ReturnCount が3に到達"),
    ("漁夫の利", "銀", "通常", "攻撃",
     "他プレイヤーの攻撃を受けた直後（3秒以内）の相手を撃破する",
     "弱ったところに、すかさず一撃。勝てば官軍！",
     "漁夫の利 (OpportunistKO)", "累計", 1, "撃破時",
     "opportunist", "—", "—", "—",
     "直前の被弾から3.0秒以内。攻撃者が別プレイヤーの場合のみ"),
    ("素振り五百回", "金", "通常", "攻撃",
     "累計500回攻撃を命中させる",
     "通算500発命中。千の道のりも、半分まで来た。",
     "命中回数 (Hit)", "累計", 500, "命中時",
     "hit_500", "—", "—", "—", "累計カウント"),
    ("ドミノ倒し", "金", "通常", "連鎖",
     "連鎖によって相手を撃破する",
     "自分の一撃が、連鎖して相手を崩した。狙い通り！",
     "連鎖撃破回数 (ChainKO)", "累計", 1, "連鎖で撃破時",
     "chain_ko", "—", "—", "—",
     "連鎖元の積み木が直接相手を撃破。0.6秒以内"),
    ("一網打尽", "プラチナ", "通常", "連鎖",
     "1回の攻撃・連鎖で2人以上を同時に撃破する",
     "一撃で二人まとめて。会場が、どっと沸いた。",
     "同時撃破数 (MultiKOBest)", "最大記録", 2, "撃破時",
     "multi_ko", "—", "—", "—",
     "同一攻撃者が0.5秒以内に2人以上撃破"),
    ("無傷の王者", "プラチナ", "通常", "試合",
     "一度も被弾せずに試合に勝利する",
     "かすり傷ひとつなし。あなたの前に、道はなかった。",
     "無傷勝利回数 (NoDamageWin)", "累計", 1, "試合終了時（勝利）",
     "no_damage_win", "—", "—", "—",
     "被弾・KOが一度もないこと。無敵中のノーダメージは被弾に含めない"),
    ("空振り三振", "銀", "シークレット", "攻撃",
     "累計50回ハンマーを空振りする",
     "ブンッ、ブンッ。風だけが、あなたに応えてくれた。",
     "ハンマー空振り回数 (HammerMiss)", "累計", 50, "薙ぎ払い終了時",
     "secret_whiff", "○", "—", "—",
     "1回の薙ぎ払いで何にも当たらなかった場合。未解除時は伏せる"),
    ("電光石火", "金", "シークレット", "試合",
     "試合開始から30秒以内に勝利する",
     "瞬きの間の決着。観客はまだ席に着いていた。",
     "30秒以内勝利 (QuickWin)", "累計", 1, "試合終了時（勝利）",
     "secret_quick_win", "○", "—", "—",
     "試合開始から30.0秒以内。未解除時は伏せる"),
]

# sanity checks
assert len(ROWS) == 39, len(ROWS)
assert len({r[10] for r in ROWS}) == 39
assert [r[0] for r in ROWS][-1] == "電光石火"
assert Counter(r[1] for r in ROWS if r[2] != "レアリティ制覇") == {
    "銅": 12, "銀": 9, "金": 7, "プラチナ": 6, "ダイヤモンド": 1,
}
assert sum(1 for r in ROWS if r[2] == "レアリティ制覇") == 4
assert sum(1 for r in ROWS if r[2] == "最高位") == 1
assert sum(1 for r in ROWS if r[2] == "シークレット") == 3
assert sum(1 for r in ROWS if r[7] == "自動判定") == 5


def section(ws, row, text, last_col, fill_color=GREEN_PALE, font_color=GREEN_DARK):
    ws.merge_cells(start_row=row, start_column=1, end_row=row, end_column=last_col)
    cell = ws.cell(row=row, column=1, value=text)
    style(cell, Font(name="Meiryo", size=12, bold=True, color=font_color),
          fill_color, LEFT, border=None)
    for c in range(2, last_col + 1):
        ws.cell(row=row, column=c).fill = fill(fill_color)
    ws.row_dimensions[row].height = 26


def note_row(ws, row, text, last_col):
    ws.merge_cells(start_row=row, start_column=1, end_row=row, end_column=last_col)
    cell = ws.cell(row=row, column=1, value=text)
    style(cell, BODY_XS, CREAM, Alignment(horizontal="left", vertical="center", wrap_text=True),
          border=None)
    for c in range(2, last_col + 1):
        ws.cell(row=row, column=c).fill = fill(CREAM)
    ws.row_dimensions[row].height = 26


wb = Workbook()
wb.properties.title = "実績一覧"
wb.properties.subject = "AmazingDarumaOtoshi 実績マスター"
wb.properties.creator = "AmazingDarumaOtoshi"
wb.properties.description = "AchievementCatalog.cs / AchievementManager.cs から生成した実績一覧"

# ============================================================================
# Sheet 1: 実績一覧
# ============================================================================
ws = wb.active
ws.title = "実績一覧"
ws.sheet_properties.tabColor = GREEN
ws.sheet_view.showGridLines = False
ws.sheet_view.zoomScale = 80

last_col = 14
widths = {"A": 10, "B": 21, "C": 16, "D": 17, "E": 10, "F": 40, "G": 42,
          "H": 31, "I": 10, "J": 9, "K": 23, "L": 22, "M": 17, "N": 48}
for col, w in widths.items():
    ws.column_dimensions[col].width = w

ws.merge_cells("A1:N1")
c = ws["A1"]
c.value = "実績一覧"
style(c, Font(name="Meiryo", size=20, bold=True, color=WHITE), GREEN_DARK,
      Alignment(horizontal="left", vertical="center"), border=None)
for i in range(2, last_col + 1):
    ws.cell(row=1, column=i).fill = fill(GREEN_DARK)
ws.row_dimensions[1].height = 40

ws.merge_cells("A2:N2")
c = ws["A2"]
c.value = "全39件 ／ 2026年11月8日時点 ／ AchievementCatalog.cs より生成 ／ 4行目の見出しでフィルタできます"
style(c, Font(name="Meiryo", size=10, color=GRAY), None,
      Alignment(horizontal="left", vertical="center"), border=None)
ws.row_dimensions[2].height = 22

ws.merge_cells("A3:N3")
c = ws["A3"]
c.value = "レアリティ色はゲーム内カードの開始色に対応。種別「レアリティ制覇」はレアリティ別集計から除外されます。No.027〜039は2026/11/08に追加した13件です。"
style(c, Font(name="Meiryo", size=9, color=LIGHT_GRAY), None,
      Alignment(horizontal="left", vertical="center"), border=None)
ws.row_dimensions[3].height = 18

headers = ["実績番号", "実績名", "レアリティ", "種別", "カテゴリ", "解除条件", "ポエム",
           "判定種別", "数え方", "必要数", "更新タイミング", "実績ID",
           "報酬・関連", "備考"]
for i, h in enumerate(headers, start=1):
    cell = ws.cell(row=4, column=i, value=h)
    style(cell, BOLD_W, GREEN, WRAP_CENTER,
          Border(left=THIN, right=THIN, top=THIN, bottom=MEDIUM))
ws.row_dimensions[4].height = 34

comments = {
    1: "カタログの並び順（1始まり）。ゲーム内では No.001 のように3桁で表示。途中に追加すると以降の番号が繰り上がります。",
    3: "銅・銀・金・プラチナ・ダイヤモンド。表示だけに使われ、解除条件には影響しません。",
    4: "通常／レアリティ制覇（特別実績）／最高位（ダイヤ）／シークレット。",
    8: "AchievementMetric の種類。同じ判定を複数の実績で共有しています。",
    10: "「自動判定」の実績は、他の実績数や使用可能キャラ数に連動して変動します。表の値は現在値です。",
    12: "セーブに使うID。変更すると解除済みが消えるため、一度出したら変えません。",
}
for col, text in comments.items():
    ws.cell(row=4, column=col).comment = Comment(text, "実績一覧", width=320, height=110)

for idx, row in enumerate(ROWS):
    r = 5 + idx
    (name, rarity, kind, category, condition, poem, metric_label, count_style,
     target, timing, ach_id, _secret, _special, reward, note) = row

    # 追加分はカテゴリ順が前後するため、カテゴリ帯ではなく行ごとの縞で塗る
    band = WHITE if idx % 2 == 0 else BAND

    values = [
        idx + 1, name, rarity, kind, category, condition, poem, metric_label,
        count_style, target, timing, ach_id, reward, note,
    ]
    for col, value in enumerate(values, start=1):
        cell = ws.cell(row=r, column=col, value=value)
        align = DATA_ALIGN
        fnt = BODY
        fill_color = band
        fmt = None

        if col == 1:
            align = CENTER
            fnt = Font(name="Meiryo", size=9, bold=True, color=GRAY)
            fmt = '"No."000'
        elif col == 2:
            fnt = Font(name="Meiryo", size=10.5, bold=True, color=INK)
        elif col == 3:
            info = RARITY[rarity]
            fill_color = info["start"]
            fnt = Font(name="Meiryo", size=9.5, bold=True, color=info["text"])
            align = CENTER
        elif col == 4:
            fill_color = KIND_FILL[kind]
            fnt = Font(name="Meiryo", size=9, bold=(kind != "通常"), color=INK)
            align = CENTER
        elif col == 5:
            align = CENTER
            fnt = Font(name="Meiryo", size=9.5, color=INK)
        elif col in (6, 7):
            fnt = BODY_S if col == 7 else BODY
        elif col == 8:
            fnt = Font(name="Meiryo", size=8.5, color=GRAY)
        elif col == 9:
            align = CENTER
            fnt = Font(name="Meiryo", size=9, color=INK)
        elif col == 10:
            align = CENTER
            fnt = Font(name="Meiryo", size=10, bold=True, color=INK)
        elif col == 11:
            fnt = Font(name="Meiryo", size=8.5, color=GRAY)
        elif col == 12:
            fnt = MONO
            align = Alignment(horizontal="left", vertical="center")
        elif col == 13:
            fnt = Font(name="Meiryo", size=9, bold=(reward != "—"),
                       color=GREEN_DARK if reward != "—" else "BDB4A7")
            align = LEFT
        elif col == 14:
            fnt = Font(name="Meiryo", size=8.5, color=GRAY)

        style(cell, fnt, fill_color, align, FULL, fmt)

    ws.row_dimensions[r].height = 56

# notes
note_row(ws, 45, "※ 実績番号はカタログの並び順（1始まり）です。実績を途中に挿入すると、以降の番号が繰り上がります。追加は末尾が安全です。", last_col)
note_row(ws, 46, "※ セーブは実績IDで管理されます。IDを変更すると解除済みが消えるため、一度出したIDは変えないでください。", last_col)
note_row(ws, 47, "※ 「必要数」が自動判定の実績は、実績の追加・削除や使用可能キャラクター数の変化に連動します。表の数値は2026/11/08時点の値です。", last_col)
note_row(ws, 48, "※ シークレット実績は未解除の間、ゲーム内で名前・条件・レアリティが伏せられます。このシートは内部管理用のため全情報を記載しています。", last_col)

ws.freeze_panes = "D5"
ws.auto_filter.ref = "A4:N43"
ws.print_area = "A1:N48"
ws.print_title_rows = "1:4"
ws.page_setup.orientation = "landscape"
ws.page_setup.paperSize = 8  # A3
ws.page_setup.fitToWidth = 1
ws.page_setup.fitToHeight = 0
ws.sheet_properties.pageSetUpPr = PageSetupProperties(fitToPage=True)
ws.print_options.horizontalCentered = True

# ============================================================================
# Sheet 2: 集計
# ============================================================================
ws2 = wb.create_sheet("集計")
ws2.sheet_properties.tabColor = "2F6F8F"
ws2.sheet_view.showGridLines = False
ws2.sheet_view.zoomScale = 90
for col, w in {"A": 26, "B": 16, "C": 10, "D": 17, "E": 17, "F": 20, "G": 10}.items():
    ws2.column_dimensions[col].width = w

LAST2 = 7
ws2.merge_cells("A1:G1")
c = ws2["A1"]
c.value = "実績サマリー"
style(c, Font(name="Meiryo", size=20, bold=True, color=WHITE), GREEN_DARK,
      Alignment(horizontal="left", vertical="center"), border=None)
for i in range(2, LAST2 + 1):
    ws2.cell(row=1, column=i).fill = fill(GREEN_DARK)
ws2.row_dimensions[1].height = 40

ws2.merge_cells("A2:G2")
c = ws2["A2"]
c.value = "2026年11月8日時点 ／ 全39件（追加13件） ／ 数値はスナップショット（実績を追加すると変動します）"
style(c, Font(name="Meiryo", size=10, color=GRAY), None,
      Alignment(horizontal="left", vertical="center"), border=None)
ws2.row_dimensions[2].height = 22

section(ws2, 4, "1. 全体", LAST2)

table_header_fill = "3E7A59"
def sub_header(ws, row, labels, merges=None):
    for i, label in enumerate(labels, start=1):
        cell = ws.cell(row=row, column=i, value=label)
        style(cell, BOLD_W, table_header_fill, WRAP_CENTER, FULL)
    if merges:
        for start, end in merges:
            ws.merge_cells(start_row=row, start_column=start, end_row=row, end_column=end)
            for i in range(start + 1, end + 1):
                ws.cell(row=row, column=i).fill = fill(table_header_fill)
    ws.row_dimensions[row].height = 26

sub_header(ws2, 5, ["項目", "値", "補足", "", "", "", ""], merges=[(3, 7)])

overview = [
    ("総実績数", 39, "AchievementCatalog の定義数"),
    ("通常実績", 31, "特別4件・最高位1件・シークレット3件を除く"),
    ("レアリティ制覇（特別）", 4, "銅・銀・金・プラチナの各覇者"),
    ("最高位（ダイヤモンド）", 1, "完全なる制覇"),
    ("シークレット", 3, "千本ノック・空振り三振・電光石火"),
    ("レアリティ集計の対象", 35, "特別4件を除く（ゲーム内ダッシュボードと同じ）"),
    ("報酬キャラクター", "だるきんぐ", "全実績解除でキャラクターセレクトに追加"),
]
for i, (label, value, memo) in enumerate(overview):
    r = 6 + i
    style(ws2.cell(row=r, column=1, value=label), BOLD, WHITE, LEFT)
    style(ws2.cell(row=r, column=2, value=value),
          Font(name="Meiryo", size=10.5, bold=True, color=GREEN_DARK), WHITE,
          CENTER)
    ws2.merge_cells(start_row=r, start_column=3, end_row=r, end_column=7)
    style(ws2.cell(row=r, column=3, value=memo), BODY_S, WHITE,
          Alignment(horizontal="left", vertical="center", indent=1))
    for cidx in range(4, 8):
        ws2.cell(row=r, column=cidx).fill = fill(WHITE)
        ws2.cell(row=r, column=cidx).border = FULL
    ws2.row_dimensions[r].height = 24

section(ws2, 14, "2. レアリティ別（特別実績を除く）", LAST2)
sub_header(ws2, 15, ["レアリティ", "件数", "割合", "メタ実績", "", "", "必要数"], merges=[(4, 6)])
ws2.row_dimensions[15].height = 28

rarity_rows = [
    ("銅", 12, 12 / 35, "銅の覇者", 12),
    ("銀", 9, 9 / 35, "銀の覇者", 9),
    ("金", 7, 7 / 35, "黄金の覇者", 7),
    ("プラチナ", 6, 6 / 35, "白金の覇者", 6),
    ("ダイヤモンド", 1, 1 / 35, "完全なる制覇", 38),
]
for i, (rarity, count, ratio, meta, need) in enumerate(rarity_rows):
    r = 16 + i
    text_color = RARITY[rarity]["text"]
    style(ws2.cell(row=r, column=1, value=rarity),
          Font(name="Meiryo", size=10, bold=True, color=text_color),
          RARITY[rarity]["start"], CENTER)
    style(ws2.cell(row=r, column=2, value=count),
          Font(name="Meiryo", size=10, bold=True, color=INK), WHITE, CENTER)
    style(ws2.cell(row=r, column=3, value=ratio),
          Font(name="Meiryo", size=10, color=INK), WHITE, CENTER, FULL, "0.0%")
    ws2.merge_cells(start_row=r, start_column=4, end_row=r, end_column=6)
    style(ws2.cell(row=r, column=4, value=meta),
          Font(name="Meiryo", size=9, color=INK), WHITE, CENTER)
    for cidx in range(5, 7):
        ws2.cell(row=r, column=cidx).fill = fill(WHITE)
        ws2.cell(row=r, column=cidx).border = FULL
    style(ws2.cell(row=r, column=7, value=need),
          Font(name="Meiryo", size=10, bold=True, color=INK), WHITE, CENTER)
    ws2.row_dimensions[r].height = 24

r = 21
style(ws2.cell(row=r, column=1, value="合計"),
      Font(name="Meiryo", size=10, bold=True, color=WHITE), GREEN, CENTER)
style(ws2.cell(row=r, column=2, value=35),
      Font(name="Meiryo", size=10, bold=True, color=WHITE), GREEN, CENTER)
style(ws2.cell(row=r, column=3, value=1.0),
      Font(name="Meiryo", size=10, bold=True, color=WHITE), GREEN, CENTER, FULL, "0.0%")
ws2.merge_cells(start_row=r, start_column=4, end_row=r, end_column=7)
style(ws2.cell(row=r, column=4), BODY, GREEN, CENTER)
for cidx in range(5, 8):
    style(ws2.cell(row=r, column=cidx), BODY, GREEN, CENTER)
ws2.row_dimensions[r].height = 24

note_row(ws2, 22, "※ レアリティ制覇の4件は「特別実績」として別枠で集計されます。レアリティ別の合計は35件で、ダイヤモンドは特別扱いせずに含めています。", LAST2)
ws2.row_dimensions[22].height = 32

section(ws2, 24, "3. カテゴリ別", LAST2)
sub_header(ws2, 25, ["カテゴリ", "件数", "割合", "主な内容", "", "", ""], merges=[(4, 7)])
category_rows = [
    ("試合", 9, 9 / 39, "試合プレイ・勝利・敗北・連勝・無傷勝利"),
    ("攻撃", 12, 12 / 39, "命中・打ち返し・ラリー・漁夫の利・空振り"),
    ("連鎖", 6, 6 / 39, "連鎖数・連鎖撃破・同時撃破"),
    ("積み木", 4, 4 / 39, "積み上げ数・種類数・低積み木での勝利"),
    ("挑戦", 8, 8 / 39, "必殺技・パッシブ・全キャラ・レアリティ制覇・最高位"),
]
for i, (cat, count, ratio, desc) in enumerate(category_rows):
    r = 26 + i
    style(ws2.cell(row=r, column=1, value=cat),
          Font(name="Meiryo", size=10, bold=True, color=INK), WHITE, CENTER)
    style(ws2.cell(row=r, column=2, value=count),
          Font(name="Meiryo", size=10, bold=True, color=INK), WHITE, CENTER)
    style(ws2.cell(row=r, column=3, value=ratio),
          Font(name="Meiryo", size=10, color=INK), WHITE, CENTER, FULL, "0.0%")
    ws2.merge_cells(start_row=r, start_column=4, end_row=r, end_column=7)
    style(ws2.cell(row=r, column=4, value=desc), BODY_S, WHITE, LEFT)
    for cidx in range(5, 8):
        ws2.cell(row=r, column=cidx).fill = fill(WHITE)
        ws2.cell(row=r, column=cidx).border = FULL
    ws2.row_dimensions[r].height = 24

r = 31
style(ws2.cell(row=r, column=1, value="合計"),
      Font(name="Meiryo", size=10, bold=True, color=WHITE), GREEN, CENTER)
style(ws2.cell(row=r, column=2, value=39),
      Font(name="Meiryo", size=10, bold=True, color=WHITE), GREEN, CENTER)
style(ws2.cell(row=r, column=3, value=1.0),
      Font(name="Meiryo", size=10, bold=True, color=WHITE), GREEN, CENTER, FULL, "0.0%")
ws2.merge_cells(start_row=r, start_column=4, end_row=r, end_column=7)
style(ws2.cell(row=r, column=4), BODY, GREEN, CENTER)
for cidx in range(5, 8):
    style(ws2.cell(row=r, column=cidx), BODY, GREEN, CENTER)
ws2.row_dimensions[r].height = 24

section(ws2, 33, "4. 判定種別", LAST2)
sub_header(ws2, 34, ["判定種別", "件数", "数え方", "説明", "", "", ""], merges=[(4, 7)])
metric_rows = [
    ("試合プレイ回数", 3, "累計", "試合をプレイした回数。試合開始時に加算"),
    ("勝利回数", 2, "累計", "試合に勝利した回数。試合終了時に加算"),
    ("命中回数", 4, "累計", "攻撃を命中させた回数。千本ノック・素振り五百回を含む"),
    ("1試合の命中回数", 1, "単試合", "試合開始で0に戻る。コンボの達人用"),
    ("打ち返し回数", 3, "累計", "カウンター・打ち返しの成功回数"),
    ("打ち返し撃破回数", 1, "累計", "打ち返しによって相手を撃破した回数"),
    ("最大連鎖数", 4, "最大記録", "これまでで一番長い連鎖。自己ベスト"),
    ("最大積み上げ数", 2, "最大記録", "これまでで一番高く積んだ数。自己ベスト"),
    ("低積み木勝利回数", 1, "累計", "積み木1個以下で勝利した回数"),
    ("必殺技発動回数", 1, "累計", "必殺技を発動した回数"),
    ("使用キャラ種類数", 1, "種類数", "使用したことのあるキャラクターの種類数"),
    ("同レアリティ制覇", 4, "自動判定", "同じレアリティの実績をすべて解除"),
    ("全実績制覇", 1, "自動判定", "自分以外の実績をすべて解除"),
    ("試合敗北回数", 1, "累計", "CPU勝利の敗北回数。引き分けは含まない"),
    ("パッシブ発動", 1, "初回", "キャラごとに初回の発動だけを数える"),
    ("積み木の種類数", 1, "単試合", "1試合中に同時に積んだブロックの色種類"),
    ("連勝数", 1, "最大記録", "現在の連勝数。敗北で0、引き分けは維持"),
    ("ラリー回数", 1, "最大記録", "同じ積み木を打ち返した最大回数"),
    ("漁夫の利", 1, "累計", "他プレイヤーの被弾直後3.0秒以内の撃破"),
    ("連鎖撃破回数", 1, "累計", "連鎖元の積み木が直接相手を撃破した回数"),
    ("同時撃破数", 1, "最大記録", "同一攻撃者が0.5秒以内に撃破した最大人数"),
    ("無傷勝利回数", 1, "累計", "被弾・KOなしで勝利した回数"),
    ("ハンマー空振り回数", 1, "累計", "1回の薙ぎ払いで何にも当たらなかった回数"),
    ("30秒以内勝利", 1, "累計", "試合開始から30.0秒以内の勝利回数"),
]
for i, (metric, count, style_name, desc) in enumerate(metric_rows):
    r = 35 + i
    style(ws2.cell(row=r, column=1, value=metric),
          Font(name="Meiryo", size=9, bold=True, color=INK), WHITE, LEFT)
    style(ws2.cell(row=r, column=2, value=count),
          Font(name="Meiryo", size=10, bold=True, color=INK), WHITE, CENTER)
    style(ws2.cell(row=r, column=3, value=style_name),
          Font(name="Meiryo", size=9, color=GRAY), WHITE, CENTER)
    ws2.merge_cells(start_row=r, start_column=4, end_row=r, end_column=7)
    style(ws2.cell(row=r, column=4, value=desc), BODY_S, WHITE, LEFT)
    for cidx in range(5, 8):
        ws2.cell(row=r, column=cidx).fill = fill(WHITE)
        ws2.cell(row=r, column=cidx).border = FULL
    ws2.row_dimensions[r].height = 24

r = 59
style(ws2.cell(row=r, column=1, value="合計"),
      Font(name="Meiryo", size=10, bold=True, color=WHITE), GREEN, CENTER)
style(ws2.cell(row=r, column=2, value=39),
      Font(name="Meiryo", size=10, bold=True, color=WHITE), GREEN, CENTER)
style(ws2.cell(row=r, column=3, value=""), BODY, GREEN, CENTER)
ws2.merge_cells(start_row=r, start_column=4, end_row=r, end_column=7)
style(ws2.cell(row=r, column=4), BODY, GREEN, CENTER)
for cidx in range(5, 8):
    style(ws2.cell(row=r, column=cidx), BODY, GREEN, CENTER)
ws2.row_dimensions[r].height = 24

section(ws2, 61, "5. メタ実績・自動目標", LAST2)
sub_header(ws2, 62, ["実績", "必要数", "対象", "", "", "備考", ""], merges=[(3, 5), (6, 7)])
meta_rows = [
    ("銅の覇者", 12, "銅の通常実績12件", "レアリティ別の全解除で自動解除"),
    ("銀の覇者", 9, "銀の通常実績9件", "レアリティ別の全解除で自動解除"),
    ("黄金の覇者", 7, "金の通常実績7件", "レアリティ別の全解除で自動解除"),
    ("白金の覇者", 6, "プラチナ6件（千本ノックを含む）", "シークレットも含めて対象（意図通り）"),
    ("完全なる制覇", 38, "自分以外の全38件", "全解除でだるきんぐ解放"),
    ("みんなの力", 7, "prefab を持つ使用可能キャラ7体", "キャラ追加で必要数が変動"),
]
for i, (name, need, target, memo) in enumerate(meta_rows):
    r = 63 + i
    style(ws2.cell(row=r, column=1, value=name),
          Font(name="Meiryo", size=9, bold=True, color=INK), WHITE, LEFT)
    style(ws2.cell(row=r, column=2, value=need),
          Font(name="Meiryo", size=10, bold=True, color=GREEN_DARK), WHITE, CENTER)
    ws2.merge_cells(start_row=r, start_column=3, end_row=r, end_column=5)
    style(ws2.cell(row=r, column=3, value=target), BODY_S, WHITE, LEFT)
    for cidx in range(4, 6):
        ws2.cell(row=r, column=cidx).fill = fill(WHITE)
        ws2.cell(row=r, column=cidx).border = FULL
    ws2.merge_cells(start_row=r, start_column=6, end_row=r, end_column=7)
    style(ws2.cell(row=r, column=6, value=memo), Font(name="Meiryo", size=8.5, color=GRAY), WHITE, LEFT)
    ws2.cell(row=r, column=7).fill = fill(WHITE)
    ws2.cell(row=r, column=7).border = FULL
    ws2.row_dimensions[r].height = 24

section(ws2, 70, "6. 注記", LAST2)
notes2 = [
    "実績番号はカタログの並び順（1始まり）。ゲーム内では No.001 の3桁表示。途中に追加すると以降の番号が繰り上がるため、追加は末尾が安全。",
    "セーブは実績IDで管理。IDを変更すると解除済みが消えるので、一度出したら変えない。",
    "「必要数」が自動の実績（メタ実績・みんなの力）は、実績やキャラクターの追加で変動する。このシートの数値は2026/11/08時点。",
    "レアリティは表示だけに使われ、解除条件には影響しない。",
    "シークレットは未解除の間、名前・条件・レアリティを伏せる。解除後は通常の実績として表示される。",
    "解除日時はUTCで保存し、表示時にローカル時刻へ変換。日時がない古いセーブは「—」と表示される。",
]
for i, text in enumerate(notes2):
    note_row(ws2, 71 + i, "※ " + text, LAST2)

ws2.print_area = "A1:G76"
ws2.page_setup.orientation = "portrait"
ws2.page_setup.paperSize = 9  # A4
ws2.page_setup.fitToWidth = 1
ws2.page_setup.fitToHeight = 0
ws2.sheet_properties.pageSetUpPr = PageSetupProperties(fitToPage=True)
ws2.print_options.horizontalCentered = True

# ============================================================================
# Sheet 3: 補足・提案
# ============================================================================
ws3 = wb.create_sheet("補足・提案")
ws3.sheet_properties.tabColor = "C98A3E"
ws3.sheet_view.showGridLines = False
ws3.sheet_view.zoomScale = 90
for col, w in {"A": 16, "B": 34, "C": 62, "D": 44}.items():
    ws3.column_dimensions[col].width = w

LAST3 = 4
ws3.merge_cells("A1:D1")
c = ws3["A1"]
c.value = "補足・改善提案"
style(c, Font(name="Meiryo", size=20, bold=True, color=WHITE), "7A4A1E",
      Alignment(horizontal="left", vertical="center"), border=None)
for i in range(2, LAST3 + 1):
    ws3.cell(row=1, column=i).fill = fill("7A4A1E")
ws3.row_dimensions[1].height = 40

ws3.merge_cells("A2:D2")
c = ws3["A2"]
c.value = "全39件の実績一覧について、列の意味、追加すると運用しやすい項目、設計上の気づきをまとめています。"
style(c, Font(name="Meiryo", size=10, color=GRAY), None,
      Alignment(horizontal="left", vertical="center"), border=None)
ws3.row_dimensions[2].height = 22

section(ws3, 4, "1. 列の見方", LAST3, "F6EBDD", "7A4A1E")
sub_header(ws3, 5, ["列", "内容", "補足", ""], merges=[(3, 4)])
col_notes = [
    ("実績番号", "カタログの並び順（1始まり）", "ゲーム内では No.001 の3桁表示。途中挿入で繰り上がる"),
    ("実績名", "プレイヤーに見せる名前", "ゲーム内の一覧・通知・詳細で共通"),
    ("レアリティ", "銅／銀／金／プラチナ／ダイヤモンド", "表示のみ。カードの色・枠・光彩に対応"),
    ("種別", "通常／レアリティ制覇／最高位／シークレット", "レアリティ制覇は「特別実績」として別枠集計"),
    ("カテゴリ", "試合／攻撃／連鎖／積み木／挑戦", "一覧の分類・フィルタ用"),
    ("解除条件", "ゲーム内に出す条件文", "通知にもこの文が出る"),
    ("ポエム", "解除したときのひとこと", "メニューの詳細画面でのみ表示"),
    ("判定種別", "内部のカウント種別（AchievementMetric）", "同じ判定を複数の実績で共有"),
    ("数え方", "累計／単試合／最大記録／種類数／初回／自動判定", "単試合は試合開始でリセット。初回はキャラごとの1回目"),
    ("必要数", "解除に必要な数", "自動判定の実績は現在値。実績追加で変動"),
    ("更新タイミング", "判定が走るタイミング", "命中時・試合終了時・他の解除時など"),
    ("実績ID", "セーブに使う不変のID", "変更禁止。解除済みデータが消える"),
    ("報酬・関連", "解除で得られるもの", "現在は完全なる制覇→だるきんぐのみ"),
    ("備考", "判定の補足・注意点", "実装やQA時の確認メモ"),
]
for i, (col_name, desc, memo) in enumerate(col_notes):
    r = 6 + i
    style(ws3.cell(row=r, column=1, value=col_name),
          Font(name="Meiryo", size=9, bold=True, color=INK), WHITE, LEFT_WRAP)
    style(ws3.cell(row=r, column=2, value=desc), Font(name="Meiryo", size=8.5, color=INK), WHITE, LEFT_WRAP)
    ws3.merge_cells(start_row=r, start_column=3, end_row=r, end_column=4)
    style(ws3.cell(row=r, column=3, value=memo), Font(name="Meiryo", size=8.5, color=GRAY), WHITE, LEFT_WRAP)
    ws3.cell(row=r, column=4).fill = fill(WHITE)
    ws3.cell(row=r, column=4).border = FULL
    ws3.row_dimensions[r].height = 32

section(ws3, 21, "2. 追加するとよい項目（優先度順）", LAST3, "F6EBDD", "7A4A1E")
sub_header(ws3, 22, ["優先度", "項目", "内容", "理由・効果"])
proposals = [
    ("A", "実装状態・担当・更新日", "実装済み／調整中／追加予定、担当者、最終更新日", "39件を複数人で管理するため。Excelが最新か一目で分かる"),
    ("A", "テスト手順", "ラボでの再現手順、確認ポイント、期待結果", "リグレッション確認を属人化させない"),
    ("A", "シークレットのヒント", "未解除時に出すヒント文（条件の一部など）", "現状は完全に隠れるため、存在に気づけない"),
    ("B", "トロフィー種別・ポイント", "PS5相当の種別（銅/銀/金/プラチナ）やスコア", "将来のプラットフォーム連携・実績画面の拡張用"),
    ("B", "英語名・英語説明", "ローカライズ用の列", "海外展開やPS5の表記用。今は日本語のみ"),
    ("B", "達成率テレメトリ", "実績ごとの解除率・平均解除時間", "難易度調整や「重すぎる/軽すぎる」の判断"),
    ("B", "アイコン・SE・演出", "実績ごとのアイコン、解除音、エフェクト", "現在はレアリティ共通の自動生成アイコン"),
    ("C", "難易度タグ", "ライト／コア／やり込み など", "バランス調整や説明文の指標"),
    ("C", "追加日・バージョン", "実績を追加した日、追加バージョン", "ID不変ルールとセットで履歴を残す"),
    ("C", "報酬一覧", "実績以外の報酬（称号・スキンなど）も一覧化", "現在はだるきんぐのみ。報酬を増やすときの台帳"),
]
for i, (prio, item, desc, reason) in enumerate(proposals):
    r = 23 + i
    prio_color = {"A": "F4D6C2", "B": "F7EBC8", "C": "E8E4DC"}[prio]
    style(ws3.cell(row=r, column=1, value=prio),
          Font(name="Meiryo", size=10, bold=True, color=INK), prio_color, CENTER)
    style(ws3.cell(row=r, column=2, value=item),
          Font(name="Meiryo", size=9, bold=True, color=INK), WHITE, LEFT_WRAP)
    style(ws3.cell(row=r, column=3, value=desc), BODY_S, WHITE, LEFT_WRAP)
    style(ws3.cell(row=r, column=4, value=reason), Font(name="Meiryo", size=8.5, color=GRAY), WHITE, LEFT_WRAP)
    ws3.row_dimensions[r].height = 48

section(ws3, 34, "3. 設計メモ・気づいた点", LAST3, "F6EBDD", "7A4A1E")
design_notes = [
    ("白金の覇者はシークレットも含む（仕様）", "白金の覇者はプラチナ6件（counter_ko / chain_10 / clutch_win / multi_ko / no_damage_win / 千本ノック）が対象。シークレットを含める仕様として確定済み。完全なる制覇・だるきんぐ解放には累計1000回命中も必要。"),
    ("「みんなの力」はプレハブ持ちだけ数える", "セレクトに並ぶプレースホルダー（prefab なし）は対象外。現在の必要数は7。キャラを実装すると自動で増える。"),
    ("「完全なる制覇」のIDは旧仕様のまま", "ID は all_non_platinum。セーブ互換のため変更しない。表示名だけダイヤモンドの「完全なる制覇」になっている。"),
    ("レアリティ集計は35件", "特別実績4件を除いた数。ゲーム内ダッシュボードのレアリティ別表示と一致する。"),
    ("メタ実績の必要数は自動計算", "実績を追加・削除すると銅12／銀9／金7／白金6／完全38の数値は変わる。Excelはスナップショットとして扱う。"),
    ("追加13件の判定は仮決め", "敗北・積み木3色・打ち返し5回・3連勝・ラリー3・漁夫の利3.0秒・命中500・連鎖撃破・同時撃破0.5秒・無傷勝利・空振り50・30秒以内勝利を追加。連勝は引き分け維持、敗北でリセット。"),
    ("パッシブ発動は各キャラの初回", "赤・青は1個目の積み木、緑はホーミング、岩は岩化、蛇は蛇行弾、黄は追加、だるきんぐは3段目超過で初回発動。提案メモの「赤3個以上」は現行仕様に合わせて赤1個目とした。"),
    ("実績番号は並び順", "途中に挿入すると以降が繰り上がる。追加は末尾が安全。セーブはIDなので番号が変わっても解除状態は消えない。"),
    ("解除日時はUTC保存", "表示時にローカル時刻へ変換。日時を持たない古いセーブは「—」表示になる。"),
]
for i, (title, body) in enumerate(design_notes):
    r = 35 + i
    ws3.merge_cells(start_row=r, start_column=1, end_row=r, end_column=4)
    cell = ws3.cell(row=r, column=1)
    cell.value = "● " + title + "：" + body
    style(cell, Font(name="Meiryo", size=9.5, color=INK), WHITE,
          Alignment(horizontal="left", vertical="center", wrap_text=True))
    for cidx in range(2, 5):
        ws3.cell(row=r, column=cidx).fill = fill(WHITE)
        ws3.cell(row=r, column=cidx).border = FULL
    ws3.row_dimensions[r].height = 38

section(ws3, 45, "4. 更新履歴", LAST3, "F6EBDD", "7A4A1E")
history = [
    ("2026/11/08", "初版作成。AchievementCatalog.cs / AchievementManager.cs / AchievementRarity.cs の内容を反映。"),
    ("2026/11/08", "白金の覇者がシークレット「千本ノック」を含む仕様を確定し、注記を更新。"),
    ("2026/11/08", "追加13件を反映して全39件版に更新。追加分の判定・メタ必要数・集計を更新。"),
]
for i, (date, text) in enumerate(history):
    r = 46 + i
    style(ws3.cell(row=r, column=1, value=date),
          Font(name="Meiryo", size=9, bold=True, color=INK), WHITE, WRAP_CENTER)
    ws3.merge_cells(start_row=r, start_column=2, end_row=r, end_column=4)
    style(ws3.cell(row=r, column=2, value=text), BODY_S, WHITE, LEFT_WRAP)
    for cidx in range(3, 5):
        ws3.cell(row=r, column=cidx).fill = fill(WHITE)
        ws3.cell(row=r, column=cidx).border = FULL
    ws3.row_dimensions[r].height = 26

ws3.print_area = "A1:D48"
ws3.page_setup.orientation = "landscape"
ws3.page_setup.paperSize = 8  # A3
ws3.page_setup.fitToWidth = 1
ws3.page_setup.fitToHeight = 0
ws3.sheet_properties.pageSetUpPr = PageSetupProperties(fitToPage=True)
ws3.print_options.horizontalCentered = True

wb.save(OUT)
print("saved", OUT)
