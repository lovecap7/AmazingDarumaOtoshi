#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ものがたり資料.docx を生成する。

- 参考フォーマット: story_script_v3.docx（Yu Gothic / Letter / 色分けされた台本）
- 台本の正: Assets/Yurube/Story/Resources/Story/*.json
"""

from pathlib import Path
import json

from docx import Document
from docx.enum.style import WD_STYLE_TYPE
from docx.enum.table import WD_ALIGN_VERTICAL
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Inches, Pt, RGBColor

ROOT = Path(__file__).resolve().parents[1]
STORY = ROOT / "AmaizingDarumaOtoshi/Assets/Yurube/Story/Resources/Story"
REFERENCE = Path(r"C:\Users\YurubeEito\Downloads\story_script_v3.docx")
OUTPUT = ROOT / "Docs/ものがたり資料.docx"

# 参考資料と同じ、紙面用に調整した話者色
SPEAKERS = {
    "akadaru": ("あかだる", "B91C1C"),
    "aodaru": ("あおだる", "1D4ED8"),
    "midaru": ("みだる", "15803D"),
    "kidaru": ("きだる", "B45309"),
    "iwadaru": ("いわだる", "57534E"),
    "hebidaru": ("へびだる", "6D28D9"),
    "daruking": ("だるきんぐ", "991B1B"),
    "unknown": ("？？？", "111827"),
}

INK = "1F2937"
MUTED = "4B5563"
NOTE = "6B7280"
NAVY = "1E3A5F"
RED = "8B1E1E"
SLATE = "2C3E50"
GOLD = "B45309"
BORDER = "D0D7DE"

CONDITION_JA = {
    "MoveAndJump": "左スティックで移動＋Aでジャンプ",
    "Stack": "積み木を1個積む",
    "Attack": "ハンマーかショットを1回",
    "DialogueDone": "読むだけ",
    "Reflect": "飛んできた積み木をハンマーで打ち返す",
    "Chain": "相手に当てたあと、積み木→積み木の連鎖を起こす",
}


def load(chapter_id):
    return json.loads((STORY / f"{chapter_id}.json").read_text(encoding="utf-8"))


# ---------------------------------------------------------------------------
# 低レベルの書式ヘルパー
# ---------------------------------------------------------------------------

def set_run(run, size=10, bold=False, italic=False, color=INK):
    run.font.name = "Yu Gothic"
    rpr = run._element.get_or_add_rPr()
    rfonts = rpr.get_or_add_rFonts()
    rfonts.set(qn("w:ascii"), "Yu Gothic")
    rfonts.set(qn("w:hAnsi"), "Yu Gothic")
    rfonts.set(qn("w:eastAsia"), "Yu Gothic")
    run.font.size = Pt(size)
    run.bold = bold
    run.italic = italic
    if color:
        run.font.color.rgb = RGBColor.from_string(color)


def ensure_style(doc, name, size=10, bold=False, italic=False, color=INK,
                 before=0, after=0, line=1.2, keep_next=False, indent=None):
    try:
        st = doc.styles[name]
    except KeyError:
        st = doc.styles.add_style(name, WD_STYLE_TYPE.PARAGRAPH)
    st.base_style = doc.styles["Normal"]
    st.font.name = "Yu Gothic"
    st.font.size = Pt(size)
    st.font.bold = bold
    st.font.italic = italic
    st.font.color.rgb = RGBColor.from_string(color)
    rpr = st.element.get_or_add_rPr()
    rfonts = rpr.get_or_add_rFonts()
    rfonts.set(qn("w:ascii"), "Yu Gothic")
    rfonts.set(qn("w:hAnsi"), "Yu Gothic")
    rfonts.set(qn("w:eastAsia"), "Yu Gothic")
    pf = st.paragraph_format
    pf.space_before = Pt(before)
    pf.space_after = Pt(after)
    pf.line_spacing = line
    pf.keep_with_next = keep_next
    if indent is not None:
        pf.left_indent = Inches(indent)
    return st


def add_bottom_border(style, color, size=8, space=4):
    ppr = style.element.get_or_add_pPr()
    old = ppr.find(qn("w:pBdr"))
    if old is not None:
        ppr.remove(old)
    pbdr = OxmlElement("w:pBdr")
    bottom = OxmlElement("w:bottom")
    bottom.set(qn("w:val"), "single")
    bottom.set(qn("w:sz"), str(size))
    bottom.set(qn("w:space"), str(space))
    bottom.set(qn("w:color"), color)
    pbdr.append(bottom)
    spacing = ppr.find(qn("w:spacing"))
    if spacing is not None:
        spacing.addprevious(pbdr)
    else:
        ppr.append(pbdr)


def add_spacer(doc, points=4):
    p = doc.add_paragraph()
    p.paragraph_format.space_before = Pt(0)
    p.paragraph_format.space_after = Pt(points)
    p.paragraph_format.line_spacing = 1.0
    r = p.add_run("")
    r.font.size = Pt(1)


def para(doc, text="", style="Body", page_break=False):
    p = doc.add_paragraph(style=style)
    if page_break:
        p.paragraph_format.page_break_before = True
    if text:
        r = p.add_run(text)
        set_run(r, size=doc.styles[style].font.size.pt if doc.styles[style].font.size else 10,
                bold=bool(doc.styles[style].font.bold),
                italic=bool(doc.styles[style].font.italic),
                color=str(doc.styles[style].font.color.rgb))
    return p


def add_scene(doc, title):
    p = doc.add_paragraph(style="Scene")
    p.add_run(f"【{title}】")
    return p


def add_direction(doc, text):
    if not (text.startswith("（") or text.startswith("(")):
        text = f"（{text}）"
    p = doc.add_paragraph(style="Direction")
    p.add_run(text)
    return p


def add_dialogue(doc, speaker, text):
    name, color = SPEAKERS.get(speaker, (speaker, INK))
    t = text.strip()
    if not (t.startswith("「") and t.endswith("」")):
        t = f"「{t}」"
    p = doc.add_paragraph(style="Dialogue")
    r1 = p.add_run(f"{name}：")
    set_run(r1, size=10, bold=True, color=color)
    r2 = p.add_run(t)
    set_run(r2, size=10, color=INK)
    return p


def add_lines(doc, lines):
    for line in lines or []:
        if line is None:
            continue
        if line.get("direction") or not line.get("speaker"):
            add_direction(doc, line.get("text", ""))
        else:
            add_dialogue(doc, line["speaker"], line.get("text", ""))


def add_note(doc, text):
    p = doc.add_paragraph(style="Note")
    p.add_run(text)
    return p


def add_note_rich(doc, lead, rest):
    p = doc.add_paragraph(style="Note")
    r1 = p.add_run(lead)
    set_run(r1, size=8.5, bold=True, color=RED)
    r2 = p.add_run(rest)
    set_run(r2, size=8.5, color=NOTE)
    return p


# ---------------------------------------------------------------------------
# 表ヘルパー
# ---------------------------------------------------------------------------

def shade(cell, fill):
    tcpr = cell._tc.get_or_add_tcPr()
    old = tcpr.find(qn("w:shd"))
    if old is not None:
        tcpr.remove(old)
    shd = OxmlElement("w:shd")
    shd.set(qn("w:val"), "clear")
    shd.set(qn("w:color"), "auto")
    shd.set(qn("w:fill"), fill)
    tcpr.insert_element_before(
        shd,
        "w:noWrap", "w:tcMar", "w:textDirection", "w:tcFitText", "w:vAlign",
        "w:hideMark", "w:headers", "w:cellIns", "w:cellDel", "w:cellMerge", "w:tcPrChange",
    )


def cell_margins(cell, top=70, bottom=70, left=100, right=100):
    tcpr = cell._tc.get_or_add_tcPr()
    old = tcpr.find(qn("w:tcMar"))
    if old is not None:
        tcpr.remove(old)
    mar = OxmlElement("w:tcMar")
    for side, value in (("top", top), ("bottom", bottom), ("left", left), ("right", right)):
        el = OxmlElement(f"w:{side}")
        el.set(qn("w:w"), str(value))
        el.set(qn("w:type"), "dxa")
        mar.append(el)
    tcpr.insert_element_before(
        mar,
        "w:textDirection", "w:tcFitText", "w:vAlign", "w:hideMark",
        "w:headers", "w:cellIns", "w:cellDel", "w:cellMerge", "w:tcPrChange",
    )


def cell_borders(cell, spec):
    tcpr = cell._tc.get_or_add_tcPr()
    old = tcpr.find(qn("w:tcBorders"))
    if old is not None:
        tcpr.remove(old)
    borders = OxmlElement("w:tcBorders")
    for side in ("top", "left", "bottom", "right"):
        if side not in spec:
            continue
        value, size, color = spec[side]
        el = OxmlElement(f"w:{side}")
        el.set(qn("w:val"), value)
        el.set(qn("w:sz"), str(size))
        el.set(qn("w:space"), "0")
        el.set(qn("w:color"), color)
        borders.append(el)
    tcpr.insert_element_before(
        borders,
        "w:shd", "w:noWrap", "w:tcMar", "w:textDirection", "w:tcFitText", "w:vAlign",
        "w:hideMark", "w:headers", "w:cellIns", "w:cellDel", "w:cellMerge", "w:tcPrChange",
    )


def table_borders(table, color=BORDER, size=4, none=False):
    tblpr = table._tbl.tblPr
    old = tblpr.find(qn("w:tblBorders"))
    if old is not None:
        tblpr.remove(old)
    borders = OxmlElement("w:tblBorders")
    for side in ("top", "left", "bottom", "right", "insideH", "insideV"):
        el = OxmlElement(f"w:{side}")
        if none:
            el.set(qn("w:val"), "nil")
        else:
            el.set(qn("w:val"), "single")
            el.set(qn("w:sz"), str(size))
            el.set(qn("w:space"), "0")
            el.set(qn("w:color"), color)
        borders.append(el)
    layout = tblpr.find(qn("w:tblLayout"))
    look = tblpr.find(qn("w:tblLook"))
    if layout is not None:
        layout.addprevious(borders)
    elif look is not None:
        look.addprevious(borders)
    else:
        tblpr.append(borders)


def table_width(table, width_in):
    tblpr = table._tbl.tblPr
    for tag in ("w:tblW", "w:jc", "w:tblLayout"):
        for el in tblpr.findall(qn(tag)):
            tblpr.remove(el)
    tblw = OxmlElement("w:tblW")
    tblw.set(qn("w:type"), "dxa")
    tblw.set(qn("w:w"), str(int(width_in * 1440)))
    tblpr.insert_element_before(
        tblw, "w:jc", "w:tblBorders", "w:shd", "w:tblLayout", "w:tblCellMar", "w:tblLook",
    )
    jc = OxmlElement("w:jc")
    jc.set(qn("w:val"), "center")
    tblpr.insert_element_before(
        jc, "w:tblBorders", "w:shd", "w:tblLayout", "w:tblCellMar", "w:tblLook",
    )
    layout = OxmlElement("w:tblLayout")
    layout.set(qn("w:type"), "fixed")
    tblpr.insert_element_before(layout, "w:tblCellMar", "w:tblLook")
    look = tblpr.find(qn("w:tblLook"))
    if look is not None:
        tblpr.remove(look)
        tblpr.append(look)


def set_row_flags(row, header=False):
    trpr = row._tr.get_or_add_trPr()
    cant = OxmlElement("w:cantSplit")
    trpr.append(cant)
    if header:
        hdr = OxmlElement("w:tblHeader")
        trpr.append(hdr)


def cell_write(cell, text, size=9, bold=False, color=INK, align=WD_ALIGN_PARAGRAPH.LEFT,
               space_after=0, line=1.15):
    lines = str(text).split("\n")
    p = cell.paragraphs[0]
    p.style = cell.part.document.styles["Table Text"]
    p.alignment = align
    p.paragraph_format.space_before = Pt(0)
    p.paragraph_format.space_after = Pt(space_after)
    p.paragraph_format.line_spacing = line
    for idx, line_text in enumerate(lines):
        if idx > 0:
            p = cell.add_paragraph(style="Table Text")
            p.alignment = align
            p.paragraph_format.space_before = Pt(0)
            p.paragraph_format.space_after = Pt(space_after)
            p.paragraph_format.line_spacing = line
        r = p.add_run(line_text)
        set_run(r, size=size, bold=bold, color=color)


def add_table(doc, headers, rows, widths, first_col_bold=False, name_colors=None,
              header_fill=NAVY, alt_fill="F8FAFC", body_size=9):
    table = doc.add_table(rows=1 + len(rows), cols=len(headers))
    table.autofit = False
    table_width(table, sum(widths))
    table_borders(table, BORDER, 4)
    for i, w in enumerate(widths):
        table.columns[i].width = Inches(w)
    for row in table.rows:
        for i, w in enumerate(widths):
            row.cells[i].width = Inches(w)
        for cell in row.cells:
            cell_margins(cell, 70, 70, 100, 100)
    # header
    hdr = table.rows[0]
    set_row_flags(hdr, header=True)
    for i, text in enumerate(headers):
        c = hdr.cells[i]
        shade(c, header_fill)
        c.vertical_alignment = WD_ALIGN_VERTICAL.CENTER
        cell_write(c, text, size=body_size, bold=True, color="FFFFFF")
    # body
    for ri, row_data in enumerate(rows):
        row = table.rows[ri + 1]
        set_row_flags(row, header=False)
        fill = alt_fill if ri % 2 == 1 else "FFFFFF"
        for ci, text in enumerate(row_data):
            c = row.cells[ci]
            shade(c, fill)
            c.vertical_alignment = WD_ALIGN_VERTICAL.TOP
            color = INK
            bold = first_col_bold and ci == 0
            if name_colors and ci == 0:
                color = name_colors[ri]
                bold = True
            cell_write(c, text, size=body_size, bold=bold, color=color)
    return table


def add_callout(doc, main, sub=None):
    table = doc.add_table(rows=1, cols=1)
    table.autofit = False
    table_width(table, 6.8)
    table_borders(table, none=True)
    table.columns[0].width = Inches(6.8)
    cell = table.rows[0].cells[0]
    cell.width = Inches(6.8)
    set_row_flags(table.rows[0], header=False)
    shade(cell, "FDF2F2")
    cell_margins(cell, 80, 80, 140, 140)
    p = cell.paragraphs[0]
    p.style = doc.styles["Callout"]
    p.paragraph_format.space_before = Pt(0)
    p.paragraph_format.space_after = Pt(2)
    r = p.add_run(main)
    set_run(r, size=9.5, bold=True, color=RED)
    if sub:
        for line in sub.split("\n"):
            p2 = cell.add_paragraph(style="CalloutSub")
            p2.paragraph_format.space_before = Pt(0)
            p2.paragraph_format.space_after = Pt(0)
            r2 = p2.add_run(line)
            set_run(r2, size=8.5, color=NOTE)
    return table


def add_news_box(doc, title, headline, body, caption, fill, subheader=None, subbody=None):
    table = doc.add_table(rows=1, cols=1)
    table.autofit = False
    table_width(table, 6.8)
    table_borders(table, none=True)
    table.columns[0].width = Inches(6.8)
    cell = table.rows[0].cells[0]
    cell.width = Inches(6.8)
    set_row_flags(table.rows[0], header=False)
    shade(cell, fill)
    cell_margins(cell, 140, 140, 200, 180)
    cell_borders(cell, {
        "top": ("single", 4, "E1E4E8"),
        "left": ("single", 24, GOLD),
        "bottom": ("single", 4, "E1E4E8"),
        "right": ("single", 4, "E1E4E8"),
    })
    p = cell.paragraphs[0]
    p.paragraph_format.space_before = Pt(0)
    p.paragraph_format.space_after = Pt(4)
    r = p.add_run(title)
    set_run(r, size=10.5, bold=True, color=GOLD)
    p = cell.add_paragraph()
    p.paragraph_format.space_before = Pt(2)
    p.paragraph_format.space_after = Pt(0)
    p.paragraph_format.line_spacing = 1.15
    r = p.add_run(f"『{headline}』")
    set_run(r, size=9.5, bold=True, color="24292F")
    if body:
        p = cell.add_paragraph()
        p.paragraph_format.space_before = Pt(2)
        p.paragraph_format.space_after = Pt(0)
        p.paragraph_format.line_spacing = 1.15
        r = p.add_run(body)
        set_run(r, size=9, color="374151")
    if caption:
        p = cell.add_paragraph()
        p.paragraph_format.space_before = Pt(2)
        p.paragraph_format.space_after = Pt(0)
        p.paragraph_format.line_spacing = 1.15
        r = p.add_run(f"写真：{caption}")
        set_run(r, size=8.5, italic=True, color=NOTE)
    if subheader:
        p = cell.add_paragraph()
        p.paragraph_format.space_before = Pt(8)
        p.paragraph_format.space_after = Pt(2)
        r = p.add_run(subheader)
        set_run(r, size=9.5, bold=True, color=GOLD)
    if subbody:
        p = cell.add_paragraph()
        p.paragraph_format.space_before = Pt(0)
        p.paragraph_format.space_after = Pt(0)
        p.paragraph_format.line_spacing = 1.15
        r = p.add_run(subbody)
        set_run(r, size=9, color="374151")
    return table


def add_roster_box(doc, lines):
    table = doc.add_table(rows=1, cols=1)
    table.autofit = False
    table_width(table, 6.8)
    table_borders(table, none=True)
    table.columns[0].width = Inches(6.8)
    cell = table.rows[0].cells[0]
    cell.width = Inches(6.8)
    set_row_flags(table.rows[0], header=False)
    shade(cell, "F8FAFC")
    cell_margins(cell, 120, 120, 180, 160)
    cell_borders(cell, {
        "top": ("single", 4, "E1E4E8"),
        "left": ("single", 18, RED),
        "bottom": ("single", 4, "E1E4E8"),
        "right": ("single", 4, "E1E4E8"),
    })
    p = cell.paragraphs[0]
    p.paragraph_format.space_before = Pt(0)
    p.paragraph_format.space_after = Pt(4)
    r = p.add_run("黒板の部員名簿")
    set_run(r, size=10.5, bold=True, color=RED)
    for line in lines:
        p = cell.add_paragraph()
        p.paragraph_format.space_before = Pt(0)
        p.paragraph_format.space_after = Pt(0)
        p.paragraph_format.line_spacing = 1.25
        r = p.add_run(line)
        set_run(r, size=9.5, color="24292F")
    return table


def add_flow_box(doc, lines):
    table = doc.add_table(rows=1, cols=1)
    table.autofit = False
    table_width(table, 6.8)
    table_borders(table, none=True)
    table.columns[0].width = Inches(6.8)
    cell = table.rows[0].cells[0]
    cell.width = Inches(6.8)
    set_row_flags(table.rows[0], header=False)
    shade(cell, "F8FAFC")
    cell_margins(cell, 120, 120, 180, 160)
    cell_borders(cell, {
        "top": ("single", 4, "E1E4E8"),
        "left": ("single", 18, NAVY),
        "bottom": ("single", 4, "E1E4E8"),
        "right": ("single", 4, "E1E4E8"),
    })
    p = cell.paragraphs[0]
    p.paragraph_format.space_before = Pt(0)
    p.paragraph_format.space_after = Pt(0)
    p.paragraph_format.line_spacing = 1.25
    first = True
    for line in lines:
        if not first:
            p = cell.add_paragraph()
            p.paragraph_format.space_before = Pt(0)
            p.paragraph_format.space_after = Pt(0)
            p.paragraph_format.line_spacing = 1.25
        first = False
        r = p.add_run(line)
        set_run(r, size=9, color="24292F")
    return table


def add_character_table(doc, groups):
    total = 1 + sum(1 + len(rows) for _, rows in groups)
    table = doc.add_table(rows=total, cols=4)
    table.autofit = False
    table_width(table, 6.8)
    table_borders(table, BORDER, 4)
    widths = [1.0, 1.9, 2.0, 1.9]
    for i, w in enumerate(widths):
        table.columns[i].width = Inches(w)
    for row in table.rows:
        for i, w in enumerate(widths):
            row.cells[i].width = Inches(w)
        for cell in row.cells:
            cell_margins(cell, 55, 55, 100, 100)

    hdr = table.rows[0]
    set_row_flags(hdr, header=True)
    for i, text in enumerate(["キャラ", "立場・口調", "パッシブ／必殺技", "物語上の役割"]):
        c = hdr.cells[i]
        shade(c, NAVY)
        c.vertical_alignment = WD_ALIGN_VERTICAL.CENTER
        cell_write(c, text, size=9, bold=True, color="FFFFFF")

    name_colors = {
        "あかだる": "B91C1C", "あおだる": "1D4ED8", "みだる": "15803D", "きだる": "B45309",
        "いわだる": "57534E", "へびだる": "6D28D9", "だるきんぐ": "991B1B", "？？？": "111827",
    }

    ri = 1
    for title, rows in groups:
        row = table.rows[ri]
        set_row_flags(row, header=False)
        merged = row.cells[0].merge(row.cells[3])
        shade(merged, "EEF2F7")
        merged.vertical_alignment = WD_ALIGN_VERTICAL.CENTER
        p = merged.paragraphs[0]
        p.style = doc.styles["Table Text"]
        p.paragraph_format.space_before = Pt(0)
        p.paragraph_format.space_after = Pt(0)
        r = p.add_run(title)
        set_run(r, size=9, bold=True, color=NAVY)
        ri += 1

        for data in rows:
            row = table.rows[ri]
            set_row_flags(row, header=False)
            for ci, text in enumerate(data):
                c = row.cells[ci]
                shade(c, "FFFFFF")
                c.vertical_alignment = WD_ALIGN_VERTICAL.TOP
                color = name_colors.get(data[0], INK) if ci == 0 else INK
                cell_write(c, text, size=9, bold=(ci == 0), color=color)
            ri += 1
    return table


# ---------------------------------------------------------------------------
# 文書の組み立て
# ---------------------------------------------------------------------------

doc = Document(str(REFERENCE))
body = doc.element.body
for child in list(body):
    if child.tag == qn("w:sectPr"):
        continue
    body.remove(child)

doc.core_properties.title = "ものがたり資料"
doc.core_properties.subject = "アメイジングだるま落とし ストーリーモード 台本・設定資料"
doc.core_properties.author = "Yurube"

ensure_style(doc, "Body", 10, False, False, "222222", 2, 3, 1.2)
ensure_style(doc, "Dialogue", 10, False, False, INK, 2, 3, 1.2)
ensure_style(doc, "Direction", 9.5, False, True, MUTED, 2, 3, 1.2, indent=0.15)
ensure_style(doc, "Scene", 11, True, False, SLATE, 10, 4, 1.15, keep_next=True)
ensure_style(doc, "Chapter", 13.5, True, False, RED, 14, 6, 1.15, keep_next=True)
ensure_style(doc, "Section", 13.5, True, False, NAVY, 14, 6, 1.15, keep_next=True)
ensure_style(doc, "Subhead", 11, True, False, SLATE, 10, 4, 1.15, keep_next=True)
ensure_style(doc, "Note", 8.5, False, False, NOTE, 1, 2, 1.15, indent=0.1)
ensure_style(doc, "Table Text", 9, False, False, "222222", 0, 0, 1.15)
ensure_style(doc, "Callout", 9.5, True, False, RED, 0, 2, 1.15)
ensure_style(doc, "CalloutSub", 8.5, False, False, NOTE, 0, 0, 1.15)
add_bottom_border(doc.styles["Chapter"], RED, size=12, space=4)
add_bottom_border(doc.styles["Section"], NAVY, size=8, space=4)

# ---------------------------------------------------------------------------
# 表紙
# ---------------------------------------------------------------------------

cover = doc.add_table(rows=1, cols=1)
cover.autofit = False
table_width(cover, 6.8)
table_borders(cover, none=True)
cover.columns[0].width = Inches(6.8)
cc = cover.rows[0].cells[0]
cc.width = Inches(6.8)
set_row_flags(cover.rows[0], header=False)
shade(cc, NAVY)
cell_margins(cc, 300, 300, 240, 240)

p = cc.paragraphs[0]
p.paragraph_format.space_before = Pt(0)
p.paragraph_format.space_after = Pt(4)
r = p.add_run("アメイジングだるま落とし  |  ものがたり資料")
set_run(r, size=9.5, color="F8E1E1")

p = cc.add_paragraph()
p.paragraph_format.space_before = Pt(0)
p.paragraph_format.space_after = Pt(6)
r = p.add_run("ストーリーモード 台本・設定資料")
set_run(r, size=20, bold=True, color="FFFFFF")

p = cc.add_paragraph()
p.paragraph_format.space_before = Pt(0)
p.paragraph_format.space_after = Pt(6)
r = p.add_run("『アメイジングだるま落とし』ものがたり 序章〜最終章・エピローグ／キャラクター・伏線・実装メモ")
set_run(r, size=11, color="FFF0F0")

p = cc.add_paragraph()
p.paragraph_format.space_before = Pt(4)
p.paragraph_format.space_after = Pt(0)
r = p.add_run("バージョン: 1.0  |  更新日: 2026年11月19日")
set_run(r, size=8.5, color="E0D0D0")

add_spacer(doc, 8)

# ---------------------------------------------------------------------------
# 全体構成
# ---------------------------------------------------------------------------

h = doc.add_paragraph(style="Section")
h.add_run("全体構成・進行フォーマット概要")

p = doc.add_paragraph(style="Note")
p.paragraph_format.space_before = Pt(0)
p.paragraph_format.space_after = Pt(6)
r = p.add_run("本資料は、現行のストーリーモード（序章〜最終章・エピローグ）の構成、全セリフ、キャラクター、伏線、実装メモをまとめたものです。台本の正は Assets/Yurube/Story/Resources/Story/*.json、挙動の正は StoryMatchDirector.cs / StoryMovie.cs / TutorialController.cs です。")
set_run(r, size=8.5, color=NOTE)

p = doc.add_paragraph(style="Note")
p.paragraph_format.space_before = Pt(0)
p.paragraph_format.space_after = Pt(8)
r = p.add_run("【】は場面、話者名：は色分け、（）はト書き・演出です。")
set_run(r, size=8.5, color=NOTE)

overview_rows = [
    ("序章", "はじまりは、おにぎり", "操作：きだる\n相手：みだる", "基本操作（移動・積む・攻撃・ジャンプ）／新入部員きだると、片目のだるま"),
    ("第1章", "窓ガラスと走り込み", "操作：あおだる\n相手：いわだる＋巌高校 選手2", "打ち返し（リフレクト）／飛ばすのが苦手なあおだるが、走って壁を越える"),
    ("第2章", "まっすぐじゃない", "操作：あかだる\n相手：へびだる＋蛇ノ目学園 選手2", "連鎖（チェーン）／曲がる弾と、まっすぐな一撃。へびだるの「落とし手」"),
    ("最終章", "二つで足りる王", "操作：あかだる\n相手：だるきんぐ＋いわ・へび", "総合戦／仲間が積んだものを一撃に。全国制覇と「？？？」の声"),
    ("エピローグ", "全国制覇の夜", "操作：なし\n相手：なし", "部員名簿・片目のだるま・小槌の音／次への布石"),
]
add_table(
    doc,
    ["章", "サブタイトル", "操作／対戦相手（CPU）", "チュートリアル・物語の焦点"],
    overview_rows,
    [1.05, 1.45, 2.05, 2.25],
    first_col_bold=True,
)

# ---------------------------------------------------------------------------
# ものがたりの流れ
# ---------------------------------------------------------------------------

h = doc.add_paragraph(style="Section")
h.paragraph_format.page_break_before = True
h.add_run("ものがたりの流れ")

h = doc.add_paragraph(style="Subhead")
h.add_run("あらすじ")

for text in [
    "七転高校だるま落とし部に、おにぎりにつられてきだるが入部する。主将あかだる、副主将あおだる、分析担当みだるに迎えられ、きだるはみだると練習試合をする。",
    "夏の地区大会決勝、あおだるはいわだると対戦。飛ばすのが苦手なあおだるは、打ち返しと走りで勝利する。祝勝のなか、いわだるは「去年の準決勝、会場にない壁が突然現れた」と語る。",
    "全国大会準決勝、あかだるは中学時代のチームメイト・へびだると再会。曲がる弾に苦しみながらも、まっすぐな一撃で破る。へびだるは「『落とし手』って言葉だけは覚えといて」と告げる。",
    "全国大会決勝。相手は王者だるきんぐ、そして先に待ついわだるとへびだる。あかだるは仲間が積んでくれたものを一撃に込め、全国制覇を成し遂げる。だるきんぐは「余に、おにぎりを一つ」とだけ答える。",
]:
    p = doc.add_paragraph(style="Body")
    r = p.add_run(text)
    set_run(r, size=9.5, color=INK)

h = doc.add_paragraph(style="Subhead")
h.add_run("共通の進行フロー")

add_flow_box(doc, [
    "章選択",
    "  ↓",
    "AllStages を読み込み、stageId のステージへ",
    "  ↓",
    "カットシーン（A：つぎへ／B：スキップ）",
    "  ↓",
    "会話（前）（A：つぎへ／B：スキップ）",
    "  ↓",
    "暗転 → カウントダウン",
    "  ↓",
    "チュートリアル（章による。無い章もある）",
    "  ↓",
    "試合",
    "  ├─ 勝利 → リザルト → 実績 → 会話（後） → 横断幕 → だるま新聞",
    "  │        → クリア保存 → クリア実績 → エピローグ（最終章のみ） → 章選択へ",
    "  └─ 敗北 → KO演出 → 敗北台詞 → 同じステージを読み直してリトライ",
])
add_spacer(doc, 6)

h = doc.add_paragraph(style="Subhead")
h.add_run("章選択")

for text in [
    "表示は上から序章→最終章（order の小さい順）。",
    "解放：序章は最初から／第1章は序章クリア／第2章は第1章クリア／最終章は第2章クリア。",
    "状態：ロック（前の章をクリアで解放）／あそべる／クリア済み（Aで再プレイ）。",
    "初期選択：最初の未クリア章。全章クリア済みなら最終章。",
    "最終章は未解放のあいだ「最終章「？？？」」と表示される。",
]:
    p = doc.add_paragraph(style="Body")
    r = p.add_run("・" + text)
    set_run(r, size=9.5, color=INK)

h = doc.add_paragraph(style="Subhead")
h.add_run("操作")

for text in [
    "カットシーン：Aで次のカットへ／Bでムービー全体をスキップ。章クリア済みの再プレイでBを押すと、会話（前）もスキップされる。",
    "会話：Aで文字送りを最後まで表示（同じ押下では次へ進まない）。もう一度Aで次の行へ。Bでその会話の残りをスキップ。",
    "試合中：左スティックで移動／Aでジャンプ／Xでハンマー／Bで積み木を飛ばす／L・Yで必殺技（ゲージ満タン時）。",
]:
    p = doc.add_paragraph(style="Body")
    r = p.add_run("・" + text)
    set_run(r, size=9.5, color=INK)

h = doc.add_paragraph(style="Subhead")
h.add_run("勝利・敗北・リトライ")

for text in [
    "勝利：リザルト文字を約2秒表示 → 解除された実績カード → 会話（後） → 横断幕（最終章） → だるま新聞 → 章クリア保存 → クリア実績 → エピローグ（最終章） → 章選択へ。",
    "敗北：赤い閃光＋「やられた…」 → 章データの defeat → 同じステージを読み直してリトライ。",
    "リトライ時はカットシーン・会話（前）を飛ばし、チュートリアルは到達ステップから再開する。一度見た初回イベントは繰り返さない。",
]:
    p = doc.add_paragraph(style="Body")
    r = p.add_run("・" + text)
    set_run(r, size=9.5, color=INK)

h = doc.add_paragraph(style="Subhead")
h.add_run("セーブ・解放・実績")

p = doc.add_paragraph(style="Body")
r = p.add_run("章クリアは「だるま新聞まで見た時点」で保存される（Story.Clear.<章ID>）。クリアすると次の章が自動で解放される（Story.Unlock.<章ID>）。章クリア実績は story_prologue / story_chapter1 / story_chapter2 / story_chapter3 の4つ（Bronze / Silver / Silver / Gold）。保存先は PlayerPrefs。")
set_run(r, size=9.5, color=INK)

# ---------------------------------------------------------------------------
# キャラクター紹介
# ---------------------------------------------------------------------------

h = doc.add_paragraph(style="Section")
h.paragraph_format.page_break_before = True
h.add_run("キャラクター紹介")

club_rows = [
    ("あかだる", "主将／「俺」／熱血・大声・断言",
     "パッシブ：赤い積み木の数だけ弾速UP。\n必殺技：終焉の軌跡（トロン）。",
     "第2章・最終章の操作キャラ。へびとの因縁、仲間の積み上げを背負う。"),
    ("あおだる", "副主将／「俺」／寡黙・短文",
     "パッシブ：青い積み木の数だけ移動速度UP。\n必殺技：六腕。",
     "第1章の操作キャラ。打ち返しと走りで勝つ。"),
    ("みだる", "分析担当／「私」／丁寧・分析的",
     "パッシブ：緑の積み木が敵を追尾。\n必殺技：ファンネル。",
     "チュートリアル役。12冊目のノートの記録者。"),
    ("きだる", "1年／「ぼく」／元気・敬語",
     "パッシブ：黄色を積むと同色が1個増える。\n必殺技：黄金化。",
     "序章の操作キャラ。プレイヤーの視点。"),
]
rival_rows = [
    ("いわだる", "巌高校主将／「オレ様」／豪快",
     "パッシブ：積み木を岩化。壁で砕ける。\n必殺技：いわだる崩し。",
     "第1章の相手。第2章は選手A、最終章は決勝の相手。"),
    ("へびだる", "蛇ノ目学園エース／「ボク」／冷静・皮肉",
     "パッシブ：ショットの積み木が蛇行。\n必殺技：反転毒沼。",
     "第2章の相手。あかだるの元チームメイト。「落とし手」を伝える。"),
    ("だるきんぐ", "王者／「余」／尊大・荘厳",
     "パッシブ：積み木は2個まで。\n必殺技：エクスカリバー。",
     "最終章の相手。仲間を持たない王。おにぎりを一つ求める。"),
    ("？？？", "不明／静か・短い",
     "―",
     "最終章エピローグのみ。小槌・片目・12冊目のノートに関わる。"),
]
add_character_table(doc, [
    ("七転高校だるま落とし部", club_rows),
    ("対戦相手", rival_rows),
])
add_spacer(doc, 6)

# ---------------------------------------------------------------------------
# 章別台本
# ---------------------------------------------------------------------------

def chapter_header(ch, place, stage_line, rule_line):
    h = doc.add_paragraph(style="Chapter")
    h.paragraph_format.page_break_before = True
    h.add_run(ch["title"])
    add_callout(
        doc,
        f"操作：{ 'きだる' if ch['playerCharacter']=='kidaru' else ('あおだる' if ch['playerCharacter']=='aodaru' else 'あかだる') }  ／  "
        f"対戦相手：{place}  ／  場所：{stage_line}",
        rule_line,
    )
    add_spacer(doc, 6)


def tutorial_block(ch, extra=None):
    for step in ch.get("tutorial", []) or []:
        cond = CONDITION_JA.get(step.get("condition"), step.get("condition", ""))
        add_direction(doc, f"（条件：{cond}）")
        if extra and step.get("condition") == extra[0]:
            add_direction(doc, extra[1])
        add_dialogue(doc, step.get("speaker", "midaru"), step.get("text", ""))


# ---- 序章 ----------------------------------------------------------------

ch = load("prologue")
chapter_header(
    ch,
    "みだる（CPU）",
    "七転高校 だるま落とし部・部室",
    "ステージ：DebugStage31（仮）  ／  ルール：ストック3・時間無制限・CPU必殺技なし\nCPU：みだる Lv1・ストック2\n解放：なし  ／  実績：story_prologue  ／  クリア記録：Story.Clear.prologue",
)

add_scene(doc, "カットシーン")
for line in [
    "（場所：七転高校 だるま落とし部・部室。テロップ「春 ― 七転高校 だるま落とし部」）",
    "（カット1：部室の全景）",
    "（カット2：窓辺の、片目のだるま）",
    "（カット3：回想。片目のだるまをセピア・スローで）",
    "（カット4：白板と練習メニュー）",
    "（カット5：部員たち）",
    "（カット6：暗転 → タイトル「序章」「はじまりは、おにぎり」）",
]:
    add_direction(doc, line)

add_scene(doc, "会話（前）")
add_lines(doc, ch["before"])

add_scene(doc, "チュートリアル：基本操作")
add_direction(doc, "（チュートリアルのあいだ、CPUのみだるは停止し、当たり判定も消える）")
tutorial_block(ch)

add_scene(doc, "試合前")
add_dialogue(doc, ch["matchStart"][0]["speaker"], ch["matchStart"][0]["text"])

add_scene(doc, "対戦中：必殺技ゲージ")
add_direction(doc, "（試合開始から約2.2秒後、小窓で）")
add_dialogue(doc, "akadaru", "新入り相手に必殺技は使わん！")
add_direction(doc, "（初めて積み木を積んだとき）")
add_dialogue(doc, "midaru", "積むと10pt入ります。100ptで必殺技です。")
add_direction(doc, "（初めてハンマーで直接ダメージを与えたとき）")
add_dialogue(doc, "midaru", "ハンマーで直接殴ると15pt。……データ通りです。")
add_direction(doc, "（積んだあと約15秒経過）")
add_dialogue(doc, "midaru", "近くに敵がいる間や、動いている間も少しずつ溜まります。戦うこと自体が、溜める行為です。")
add_direction(doc, "（ゲージが満タンになったとき）")
add_direction(doc, "（試合を止め、操作プロンプト「[Y]で必殺技！」を表示。使うまで待つ）")

add_scene(doc, "対戦中：黄色増殖")
add_direction(doc, "（きだるのパッシブで黄色い積み木が増えたとき、初回のみ）")
add_lines(doc, ch["yellowExtra"])

add_scene(doc, "対戦中：掛け合い")
add_direction(doc, "（みだるがダメージを与えたとき、初回のみ）")
add_lines(doc, ch["cpuTaunts"])

add_scene(doc, "参考：未使用の覚醒イベント")
add_note(doc, "※prologue.json の awakening に以下の3行があるが、現在の序章はゲージチュートリアル方式のため、この3行は再生されない。")
add_lines(doc, ch["awakening"])

add_scene(doc, "会話（後・勝利）")
add_direction(doc, "（会話の背景：夕方の部室。窓際のだるま人形）")
add_lines(doc, ch["after"])

add_scene(doc, "会話（後・敗北）")
add_direction(doc, "（敗北時。この後、同じステージを読み直してリトライ）")
add_lines(doc, ch["defeat"])

add_scene(doc, "だるま新聞")
n = ch["newspaper"]
add_news_box(doc, "だるま新聞（序章クリア号外）", n["headline"], n.get("body"), n.get("caption"), "FFFBEB")
add_spacer(doc, 4)

add_scene(doc, "メモ")
for note in [
    "チュートリアル中はCPUの積み木上限が6に制限される（cpuBlockLimit: 6）。",
    "ゲージは最初から表示されるが、操作チュートリアル中は溜まらず、練習試合開始後から溜まる。",
    "きだるの必殺技「黄金化」は、この章のゲージチュートリアルで初めて使う想定。",
    "片目のだるま、おにぎり、走り込みは以降の章でも繰り返し登場する。",
]:
    add_note(doc, "・" + note)

# ---- 第1章 --------------------------------------------------------------

ch = load("chapter1")
chapter_header(
    ch,
    "いわだる＋巌高校 選手A・B（CPU）",
    "地区大会 決勝",
    "ステージ：Stage23  ／  ルール：ストック3・時間無制限・CPU必殺技なし\nCPU：いわだる Lv2 ／ 巌高校 選手A Lv1 ／ 選手B Lv1（各ストック2）\n解放：序章クリア  ／  実績：story_chapter1  ／  クリア記録：Story.Clear.chapter1",
)

add_scene(doc, "カットシーン")
for line in [
    "（場所：地区大会 決勝。テロップ「夏 ― 地区大会 決勝」）",
    "（カット1：会場の全景／3.2秒）",
    "（カット2：走るあおだる／4.6秒）",
    "（カット3：相手のいわだる／3.0秒）",
]:
    add_direction(doc, line)

add_scene(doc, "会話（前）")
add_lines(doc, ch["before"])

add_scene(doc, "チュートリアル：打ち返し")
tutorial_block(ch, ("Reflect", "（このステップの間、いわだるだけが動く。打ち返しが起きないまま6秒経つと、補助の積み木が飛んでくる）"))

add_scene(doc, "対戦中：開始")
add_direction(doc, "（GO直後、小窓で「地区大会 決勝、開始！」）")

add_scene(doc, "対戦中：初回イベント")
add_direction(doc, "（初めて打ち返したとき）")
add_lines(doc, ch["reflectFirst"])
add_direction(doc, "（いわだるの積み木が壁で砕けたとき）")
add_lines(doc, ch["wallBreakFirst"])

add_scene(doc, "対戦中：掛け合い")
add_direction(doc, "（いわだるがダメージを与えたとき、初回のみ）")
add_lines(doc, ch["cpuTaunts"])

add_scene(doc, "会話（後・勝利）")
add_direction(doc, "（会話の背景：夕方のステージ）")
add_lines(doc, ch["after"])

add_scene(doc, "会話（後・敗北）")
add_direction(doc, "（敗北時。この後、同じステージを読み直してリトライ）")
add_lines(doc, ch["defeat"])

add_scene(doc, "だるま新聞")
n = ch["newspaper"]
add_news_box(doc, "だるま新聞（第1章クリア号外）", n["headline"], n.get("body"), n.get("caption"), "EFF6FF")
add_spacer(doc, 4)

add_scene(doc, "メモ")
for note in [
    "「窓ガラス」はあおだるの過去の失敗談。自腹で直したらしい。",
    "ラストの「突然現れた壁」は、後の「落とし手」に関わる可能性がある未回収の謎。",
    "へびだる・だるきんぐは「巌高校 選手A／B」の表示名で出場し、台詞はない。",
    "この章は gaugeFromStart: false かつ覚醒イベントが無いため、現状は必殺技ゲージが表示されず、必殺技も使えない（仕様として要確認）。",
]:
    add_note(doc, "・" + note)

# ---- 第2章 --------------------------------------------------------------

ch = load("chapter2")
chapter_header(
    ch,
    "へびだる＋蛇ノ目学園 選手A・B（CPU）",
    "全国大会 準決勝",
    "ステージ：DebugStage46（仮）  ／  ルール：ストック3・時間無制限・CPU必殺技なし\nCPU：へびだる Lv2 ／ 蛇ノ目学園 選手A Lv2 ／ 選手B Lv1（各ストック2）\n解放：第1章クリア  ／  実績：story_chapter2  ／  クリア記録：Story.Clear.chapter2",
)

add_scene(doc, "カットシーン")
for line in [
    "（場所：全国大会 準決勝。鳥居の前の会場セット。テロップ「全国大会 準決勝」）",
    "（カット1：会場の全景／3.4秒）",
    "（カット2：七転高校の4人／4.8秒。カットシーンではへびだるは映さない）",
]:
    add_direction(doc, line)

add_scene(doc, "会話（前）")
add_lines(doc, ch["before"])

add_scene(doc, "チュートリアル：連鎖")
tutorial_block(ch, ("Chain", "（このステップの間、CPUは全員動く。30秒でヒント、さらに45秒で連鎖補助）"))

add_scene(doc, "対戦中：開始")
add_direction(doc, "（GO直後、小窓で「全国大会 準決勝、開始！」）")

add_scene(doc, "対戦中：初回イベント")
add_direction(doc, "（相手に当てたあと、1.2秒以内に積み木→積み木の連鎖が起きたとき）")
add_lines(doc, ch["chainFirst"])

add_scene(doc, "対戦中：連鎖ヒント")
add_direction(doc, "（連鎖が起きないまま30秒経過）")
add_lines(doc, ch["chainHint"])

add_scene(doc, "対戦中：掛け合い")
add_direction(doc, "（へびだるがショットを撃ったとき、初回のみ）")
add_lines(doc, ch["cpuTaunts"])

add_scene(doc, "対戦中：必殺技イベント")
ev = ch["specialEvent"]
add_direction(doc, f"（条件：プレイヤーのストックが{ev['stockThreshold']}以下。初回のみ試合を止めて掛け合い→「{ev['prompt']}」を{ev['promptSeconds']}秒表示。リトライ後は補助のみ）")
add_lines(doc, ev["lines"])

add_scene(doc, "会話（後・勝利）")
add_direction(doc, "（会話の背景：夕方の全国大会会場）")
add_lines(doc, ch["after"])

add_scene(doc, "会話（後・敗北）")
add_direction(doc, "（敗北時。この後、同じステージを読み直してリトライ）")
add_lines(doc, ch["defeat"])

add_scene(doc, "だるま新聞")
n = ch["newspaper"]
add_news_box(doc, "だるま新聞（第2章クリア号外）", n["headline"], n.get("body"), n.get("caption"), "F5F3FF")
add_spacer(doc, 4)

add_scene(doc, "メモ")
for note in [
    "へびだるの「ボクにも事情がある」「落とし手」は最終章への伏線。",
    "「まっすぐ飛ばすなんてつまらない」は、曲がる弾を使うへびだると、まっすぐな一撃のあかだるの対比。",
    "いわだる・だるきんぐは「蛇ノ目学園 選手A／B」の表示名で出場し、台詞はない。",
    "連鎖チュートリアルは、相手の積み木を崩す楽しさを教える章。",
]:
    add_note(doc, "・" + note)

# ---- 最終章 -------------------------------------------------------------

ch = load("chapter3")
chapter_header(
    ch,
    "だるきんぐ＋いわだる＋へびだる（CPU）",
    "全国大会 決勝",
    "ステージ：DebugStage47（仮）  ／  ルール：ストック3・時間無制限・CPU必殺技なし\nCPU：だるきんぐ Lv3 ／ いわだる Lv3 ／ へびだる Lv3（各ストック2）\n解放：第2章クリア  ／  実績：story_chapter3  ／  クリア記録：Story.Clear.chapter3",
)

add_scene(doc, "カットシーン")
for line in [
    "（場所：全国大会 決勝。控室 → 決勝の舞台）",
    "（カット1：控室／3.0秒。テロップ「決勝前 ― 控室」）",
    "（カット2：ノートの寄り／3.4秒。テロップ「決勝前 ― 控室」）",
    "（カット3：相手2人（いわだる・へびだる）／3.4秒。テロップ「全国大会 決勝」）",
    "（カット4：決勝の舞台・全景／3.0秒。テロップ「全国大会 決勝」）",
]:
    add_direction(doc, line)

room = [l for l in ch["before"] if l.get("scene") != "stage"]
stage = [l for l in ch["before"] if l.get("scene") == "stage"]
add_scene(doc, "会話（前）：控室")
add_lines(doc, room)
add_scene(doc, "会話（前）：決勝の舞台")
add_lines(doc, stage)

add_scene(doc, "対戦中：開始")
add_direction(doc, "（チュートリアルなし。GO直後からCPUが動く）")
add_direction(doc, "（GO直後、小窓で「全国大会 決勝、開始！」）")

add_scene(doc, "対戦中：開始直後の掛け合い")
add_direction(doc, "（開始通知のあと、小窓で）")
add_lines(doc, ch["startLines"])

add_scene(doc, "対戦中：脱落")
for entry in ch["outLines"]:
    name, _ = SPEAKERS.get(entry["character"], (entry["character"], INK))
    add_direction(doc, f"（{name}が脱落）")
    add_lines(doc, entry["lines"])

add_scene(doc, "対戦中：残り2人")
add_direction(doc, "（残っているのが、あかだる＋だるきんぐの2人だけになったとき）")
add_lines(doc, ch["lastTwoLines"])

add_scene(doc, "対戦中：必殺技イベント")
ev = ch["specialEvent"]
add_direction(doc, f"（条件：プレイヤーのストックが{ev['stockThreshold']}以下。初回のみ試合を止めて掛け合い→「{ev['prompt']}」を{ev['promptSeconds']}秒表示。リトライ後は補助のみ）")
add_lines(doc, ev["lines"])

add_scene(doc, "会話（後・勝利）：決勝の舞台")
add_direction(doc, "（会話の背景：夕方の決勝の舞台）")
add_lines(doc, ch["after"])

add_scene(doc, "会話（後・敗北）")
add_direction(doc, "（敗北時。この後、同じステージを読み直してリトライ）")
add_lines(doc, ch["defeat"])

add_scene(doc, "だるま新聞")
n = ch["newspaper"]
add_news_box(
    doc,
    "だるま新聞（最終章クリア号外）",
    n["headline"],
    n.get("body"),
    n.get("caption"),
    "F3F4F6",
    n.get("subHeader"),
    n.get("subBody"),
)
add_spacer(doc, 4)

add_scene(doc, "エピローグ")
add_direction(doc, "（場所：全国制覇の夜 ― 部室。テロップ「全国制覇の夜 ― 部室」）")
for line in [
    "（カット1：ノート／3.4秒）",
    "（カット2：部員名簿／3.0秒）",
    "（カット3：だるま人形／3.2秒）",
    "（カット4：ロゴ／2.6秒）",
]:
    add_direction(doc, line)
add_lines(doc, ch["epilogue"]["lines"])
roster_lines = ch["epilogue"]["rosterText"].split("\n")
if roster_lines and roster_lines[0].strip() == "部員名簿":
    roster_lines = roster_lines[1:]
add_roster_box(doc, roster_lines)
add_spacer(doc, 4)
add_direction(doc, "（小槌の音の文字：コツン）")

add_scene(doc, "メモ")
for note in [
    "最終章はタイトルに hiddenTitle があり、未解放時は「最終章「？？？」」。",
    "だるきんぐの「二つ」は、王冠・積み木の2段・「二つで足りる」に重ねられている。",
    "エピローグの「？？？」は、物語の続きを示唆する声。正体は明かされていない。",
    "横断幕は、会話（後）の最後のト書きに合わせて掲げられる。",
]:
    add_note(doc, "・" + note)

# ---------------------------------------------------------------------------
# 伏線・未回収要素
# ---------------------------------------------------------------------------

h = doc.add_paragraph(style="Section")
h.add_run("伏線・未回収要素")

foreshadow_rows = [
    ("片目のだるま", "序章", "全国制覇したら、もう片方の目を入れる先代からの願掛け。", "最終章エピローグで人形の2つ目の目が入る演出あり。正体は不明。"),
    ("落とし手", "第2章", "大会の裏にいる者。へびだるは「断った」と言う。だるきんぐの王座も一度ぐらついた。", "名前と噂のみ。正体は未回収。"),
    ("12冊目のノート", "最終章", "みだるのノートの12冊目に、覚えのない一文「最後に、落としに行く」。", "エピローグで最後のページの文字が増える。未回収。"),
    ("小槌の音", "最終章", "新聞の囲み記事とエピローグの「コツン」。", "？？？の声と関連。未回収。"),
    ("だるきんぐの仲間", "最終章", "「積み上げる仲間も、おらぬからな」。", "おにぎりを求める描写で、今後につながる。"),
    ("へびだるの事情", "第2章", "「ボクにも事情がある」「ボクは断ったけどね」。", "具体的な事情は未回収。"),
    ("いわだるの壁", "第1章", "去年の準決勝、会場にない壁が突然現れた。", "「誰かが置いたのか」で章は終わる。未回収。"),
    ("あおだるの窓ガラス", "第1章", "窓を割った過去。自腹で直した。", "ギャグとして回収済み。"),
    ("きだるのおにぎり", "序章〜", "入部の動機。最終章でだるきんぐにも勧める。", "回収済み（だるきんぐが一つ求める）。"),
    ("あかだるとへびだるの中学時代", "第2章", "一緒に全国へ行こうとあかだるが叫んでいた。", "へびだるの「羨ましかった」で一旦回収。今後にも余地。"),
]
add_table(
    doc,
    ["要素", "初出", "内容", "現状"],
    foreshadow_rows,
    [1.25, 0.9, 2.4, 2.25],
    first_col_bold=True,
)

# ---------------------------------------------------------------------------
# 実装メモ
# ---------------------------------------------------------------------------

h = doc.add_paragraph(style="Section")
h.paragraph_format.page_break_before = True
h.add_run("実装メモ")

p = doc.add_paragraph(style="Note")
p.paragraph_format.space_before = Pt(0)
p.paragraph_format.space_after = Pt(8)
r = p.add_run("ここは制作・調整用の情報。ゲーム内には表示されない。")
set_run(r, size=8.5, color=NOTE)

h = doc.add_paragraph(style="Subhead")
h.add_run("章データの主な項目")

field_rows = [
    ("id / order / title / subtitle / hiddenTitle", "章の識別・並び・表示名。hiddenTitle は未解放時の伏せ字。"),
    ("requiredChapter", "解放条件の章ID。"),
    ("stageId / sceneName", "読み込むステージ。"),
    ("playerCharacter", "操作キャラのID。"),
    ("rules", "モード・ストック・時間・CPU必殺技・覚醒・ゲージ表示・CPU積み木上限。"),
    ("cpu", "相手キャラ・レベル・ストック・表示名。"),
    ("speakers", "話者ID・表示名・色。"),
]
add_table(doc, ["項目", "内容"], field_rows, [3.0, 3.8], first_col_bold=True)

h = doc.add_paragraph(style="Subhead")
h.add_run("台本・演出の項目")

script_rows = [
    ("before / after / defeat", "会話（前）・会話（後）・敗北時。"),
    ("tutorial", "対戦中のチュートリアル手順。"),
    ("matchStart / matchStartNotice", "試合前の一言・開始通知。"),
    ("reflectFirst / wallBreakFirst / chainFirst / chainHint", "初回イベント・ヒント。"),
    ("cutscene", "カットシーンの種類・テロップ・カット。"),
    ("awakening", "序章の覚醒イベント台詞（現在は未使用）。"),
    ("specialEvent", "第2章以降の「追い詰められたら撃つ」イベント。"),
    ("cpuTaunts / cpuTauntOnShot", "CPUの掛け合い。"),
    ("yellowExtra", "きだるの黄色増殖の掛け合い。"),
    ("startLines / outLines / lastTwoLines", "最終章の掛け合い。"),
    ("bannerText / bannerSeconds", "勝利後の横断幕。"),
    ("epilogue", "最終章のエピローグ。"),
    ("newspaper", "だるま新聞。"),
]
add_table(doc, ["項目", "内容"], script_rows, [3.0, 3.8], first_col_bold=True)
add_spacer(doc, 6)

h = doc.add_paragraph(style="Subhead")
h.add_run("章を追加するとき")

for i, text in enumerate([
    "Assets/Yurube/Story/Resources/Story/ に <章ID>.json を追加する。",
    "id・order・title・requiredChapter・stageId・playerCharacter を設定する。",
    "speakers に話者を定義する（既存キャラは流用可）。",
    "before / after / defeat / tutorial / cpu を書く。",
    "章選択は Resources/Story を全部読むので、UI側の追加作業は不要。",
    "章クリア実績を追加する場合は AchievementCatalog.cs に storyChapter を指定した定義を足す。",
], start=1):
    p = doc.add_paragraph(style="Body")
    r1 = p.add_run(f"{i}. ")
    set_run(r1, size=9.5, bold=True, color=NAVY)
    r2 = p.add_run(text)
    set_run(r2, size=9.5, color=INK)

h = doc.add_paragraph(style="Subhead")
h.add_run("チュートリアル条件")

cond_rows = list(CONDITION_JA.items())
add_table(doc, ["condition", "判定"], cond_rows, [1.7, 5.1], first_col_bold=True)
add_spacer(doc, 4)
add_note(doc, "・cpu にキャラIDを書くと、そのステップの間だけそのCPUが動く。all で全員、空で全員停止。")
add_note(doc, "・チュートリアル中の停止CPUは、当たり判定も消える。")

h = doc.add_paragraph(style="Subhead")
h.add_run("初回イベントのキー")

p = doc.add_paragraph(style="Body")
r = p.add_run("StorySession の HasEvent / MarkEvent で、章ID＋種類のキーを保存している。リトライしても同じ掛け合いは繰り返さない。")
set_run(r, size=9.5, color=INK)
for text in [
    "<章ID>.reflect：打ち返し初回",
    "<章ID>.wall：岩の壁破壊初回",
    "<章ID>.chainFirst：連鎖初回",
    "<章ID>.chainHint：連鎖ヒント",
    "<章ID>.taunt.<キャラID>：CPU掛け合い",
    "<章ID>.out.<キャラID>：脱落掛け合い",
    "<章ID>.lastTwo：残り2人の掛け合い",
    "<章ID>.startLines：開始直後の掛け合い",
]:
    add_note(doc, "・" + text)

h = doc.add_paragraph(style="Subhead")
h.add_run("現在の注意点・要確認")

for lead, rest in [
    ("1. 序章の awakening 3行は未使用 — ",
     "現在の序章はゲージチュートリアル（PrologueFullSpecialPrompt）で進むため、StoryMatchDirector.Awakening() が呼ばれない。prologue.json の awakening はレガシー。使うなら序章のフローに組み込む必要がある。"),
    ("2. 第1章は必殺技が使えない — ",
     "gaugeFromStart: false かつ覚醒イベントが無いため、SpecialInput.StoryLocked が解除されず、ゲージも表示されない。仕様として意図しているか要確認（「見せ場まで隠す」想定なら、第1章にも覚醒イベントが必要）。"),
    ("3. ステージIDが仮 — ",
     "DebugStage31 / DebugStage46 / DebugStage47 は仮。本番ステージに差し替える際は stageId を変更する。"),
    ("4. 操作は1人＋CPUのみ — ",
     "StoryLauncher は「プレイヤー1人＋章データのCPU」だけを登録し、rules.teams = false にしている。部員（あおだる・みだる・きだる）は会話に登場するが、試合には参加しない。第1章・第2章・最終章は、プレイヤー1人 vs CPU3体のフリーフォー。"),
    ("5. キャラデータの表記 — ",
     "あかだる・あおだる・いわだるの passiveName / specialName は「～～～」のまま。実際の能力名は Script/ 側のコメント・カットイン名で確認できる。"),
    ("6. 章選択の並び — ",
     "2026-11-19に「序章が上、最終章が下」へ変更。StoryChapterSelect.Build() の配置と Update() の上下入力をセットで直している。"),
]:
    add_note_rich(doc, lead, rest)

h = doc.add_paragraph(style="Subhead")
h.paragraph_format.page_break_before = True
h.add_run("会話カメラのショットID")

shot_rows = [
    ("S_WHITEBOARD", "ホワイトボードのアップ"),
    ("S_KIDARU", "きだるのバストアップ"),
    ("S_AKA", "あかだるのバストアップ"),
    ("S_AO", "あおだるのバストアップ"),
    ("S_MI", "みだるのバストアップ"),
    ("S_DOLL_FAR", "窓際のだるま人形（引き）"),
    ("S_DOLL_EYE", "だるま人形の片目のアップ"),
    ("S_EMPTY_ROOM", "誰もいない部室"),
]
add_table(doc, ["ID", "画"], shot_rows, [2.0, 4.8], first_col_bold=True)
add_spacer(doc, 6)

h = doc.add_paragraph(style="Subhead")
h.add_run("話者ID・色")

speaker_rows = [
    ("akadaru", "あかだる", "B91C1C"),
    ("aodaru", "あおだる", "1D4ED8"),
    ("midaru", "みだる", "15803D"),
    ("kidaru", "きだる", "B45309"),
    ("iwadaru", "いわだる", "57534E"),
    ("hebidaru", "へびだる", "6D28D9"),
    ("daruking", "だるきんぐ", "991B1B"),
    ("unknown", "？？？", "111827"),
]
st = add_table(doc, ["ID", "表示名", "色"], speaker_rows, [2.0, 2.4, 2.4], first_col_bold=True)
for ri, row in enumerate(st.rows[1:]):
    color = speaker_rows[ri][2]
    c = row.cells[2]
    shade(c, color)
    for p in c.paragraphs:
        for r in p.runs:
            r.font.color.rgb = RGBColor.from_string("FFFFFF")
            r.bold = True
add_spacer(doc, 6)

h = doc.add_paragraph(style="Subhead")
h.paragraph_format.page_break_before = True
h.add_run("StorySettings の主な値")

settings_rows = [
    ("文字送り速度", "34文字/秒"),
    ("小窓の自動クローズ", "3秒"),
    ("会話の背景の暗さ", "0.62"),
    ("覚醒イベントの保険", "90秒"),
    ("打ち返し補助", "6秒／18m/s"),
    ("連鎖ヒント", "30秒"),
    ("連鎖補助", "ヒントから45秒後（合計75秒）"),
    ("必殺技プロンプト", "10秒"),
    ("リトライ時の必殺技補助", "有効"),
]
add_table(doc, ["項目", "値"], settings_rows, [3.0, 3.8], first_col_bold=True)

# ---------------------------------------------------------------------------
# 改訂履歴
# ---------------------------------------------------------------------------

h = doc.add_paragraph(style="Section")
h.add_run("改訂履歴")

add_table(doc, ["日付", "内容"], [("2026-11-19", "初版作成。序章〜最終章の台本・キャラクター・伏線・実装メモを整理。")], [1.2, 5.6], first_col_bold=True)

doc.save(str(OUTPUT))
print(f"wrote {OUTPUT}")
