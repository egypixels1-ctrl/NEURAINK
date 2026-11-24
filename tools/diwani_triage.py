# -*- coding: utf-8 -*-
"""
أداة فرز وتنظيف مكتبة الديواني — المرحلة الأولى من مشروع الخطاط الرقمي.

ما تفعله:
  1. تمسح مجلد الديواني (ومجلداته الفرعية) وتتجاهل مجلدات التعليم (info / كراسة).
  2. تستخرج النص من اسم كل ملف وتنظفه (تحذف الأرقام و (2) وما شابه).
  3. تكتشف الملفات المكررة تمامًا والمتشابهة جدًا.
  4. تقيس جودة كل صورة وتصنفها:
       clean          جاهزة: خلفية فاتحة موحدة وحبر داكن
       needs_cleaning ورقة مصورة / خلفية غير موحدة — قابلة للتنظيف
       inverted       خط فاتح على خلفية داكنة موحدة — نقلبه
       complex        ألوان ومؤثرات وخلفيات مزدحمة — تُراجع يدويًا
       too_small      دقة منخفضة جدًا
       unnamed        اسم الملف لا يحتوي نصًا عربيًا
  5. تنتج نسخة نظيفة (أسود نقي على أبيض نقي، مقصوصة) لكل صالح،
     ونسخة متجهة SVG إن كانت مكتبة vtracer مثبتة.
  6. تكتب تقريرًا: report.csv (يفتح في Excel) و report.html (معرض صور للمراجعة).

لا تعدّل ولا تحذف أي ملف أصلي. كل الناتج يُكتب في مجلد الإخراج فقط.

الاستخدام:
  python diwani_triage.py "D:\\EGY studios\\Identity Production\\] Directions\\AR\\ديواني" "D:\\CalligraphyAI\\diwani"
"""
import sys, os, re, csv, json, hashlib, html, argparse, time
from pathlib import Path

import numpy as np
from PIL import Image, ImageOps
import cv2

try:
    import vtracer  # pip install vtracer
except Exception:
    vtracer = None

IMG_EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff"}
SKIP_DIR_MARKERS = ("info", "كراس", "كــراس")       # مجلدات التعليم والكراسات
QUALITY_MARKERS = {"best": ("]- Best",), "hall_of_fame": ("Hall of fame",)}
SOCIAL_RE = re.compile(r"(screenshot|fb_img|instasave|instagram|img_\d|messenger|whatsapp|^[0-9a-f]{20,})", re.I)
ARABIC_RE = re.compile(r"[\u0600-\u06FF]")
WORK_SIDE = 1600   # أقصى ضلع للمعالجة

# ---------------------------------------------------------------- الأسماء
def clean_name(stem: str) -> str:
    s = stem
    s = re.sub(r"\(\s*\d+\s*\)", " ", s)          # (2)
    s = re.sub(r"[\]\[~_\-.,،]+", " ", s)           # رموز ترتيب المجلدات
    s = re.sub(r"[0-9\u0660-\u0669]+", " ", s)     # أرقام عربية وإنجليزية
    s = re.sub(r"[A-Za-z]+", " ", s)
    s = s.replace("ـ", "")                          # الكشيدة الزخرفية في الأسماء
    s = re.sub(r"\s+", " ", s).strip()
    return s

# ---------------------------------------------------------------- القراءة
def load_rgb(path: Path):
    im = Image.open(path)
    im = ImageOps.exif_transpose(im)
    if im.mode in ("RGBA", "LA") or (im.mode == "P" and "transparency" in im.info):
        im = im.convert("RGBA")
        bg = Image.new("RGBA", im.size, (255, 255, 255, 255))
        im = Image.alpha_composite(bg, im)
    return np.array(im.convert("RGB"))

def resize_work(rgb):
    h, w = rgb.shape[:2]
    s = WORK_SIDE / max(h, w)
    if s < 1:
        rgb = cv2.resize(rgb, (int(w * s), int(h * s)), interpolation=cv2.INTER_AREA)
    return rgb

def dhash(gray, size=16):
    small = cv2.resize(gray, (size + 1, size), interpolation=cv2.INTER_AREA)
    bits = (small[:, 1:] > small[:, :-1]).flatten()
    return int("".join("1" if b else "0" for b in bits), 2)

def hamming(a, b):
    return bin(a ^ b).count("1")

# ---------------------------------------------------------------- القياس
def measure(rgb):
    hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV)
    gray = cv2.cvtColor(rgb, cv2.COLOR_RGB2GRAY)
    h, w = gray.shape
    b = max(4, min(h, w) // 25)
    border = np.concatenate([gray[:b].ravel(), gray[-b:].ravel(), gray[:, :b].ravel(), gray[:, -b:].ravel()])
    sat = hsv[..., 1].astype(np.float32)
    # التشبع اللوني فقط في البكسلات غير الشديدة السواد/البياض
    v = hsv[..., 2]
    mid = (v > 40) & (v < 245)
    sat_mean = float(sat[mid].mean()) if mid.any() else 0.0
    _, otsu = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    ink_ratio = float((otsu == 0).mean())
    return {
        "border_mean": float(border.mean()),
        "border_std": float(border.std()),
        "sat_mean": sat_mean,
        "ink_ratio": ink_ratio,
        "gray": gray,
    }

def classify(m, w, h, named):
    if max(w, h) < 300:
        return "too_small"
    if not named:
        return "unnamed"
    bm, bs, sat = m["border_mean"], m["border_std"], m["sat_mean"]
    if sat > 70:
        return "complex"
    if bm < 80 and bs < 35:
        return "inverted"
    if bm < 120 or bs > 45 or m["ink_ratio"] > 0.6:
        return "complex"   # خلفية مزدحمة أو صورة فوتوغرافية
    if bm > 225 and bs < 18 and sat < 35 and 0.01 < m["ink_ratio"] < 0.6:
        return "clean"
    return "needs_cleaning"

# ---------------------------------------------------------------- التنظيف
def clean_image(gray, invert=False):
    g = 255 - gray if invert else gray.copy()
    # تصحيح الإضاءة: قسمة الصورة على خلفيتها المموهة
    k = max(31, (min(g.shape) // 12) | 1)
    bg = cv2.morphologyEx(g, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k)))
    norm = cv2.divide(g, bg, scale=255)
    norm = cv2.GaussianBlur(norm, (3, 3), 0)
    _, bw = cv2.threshold(norm, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    ink = (bw == 0).astype(np.uint8)
    # إزالة النقاط والشوائب الصغيرة جدًا (مع الإبقاء على نقاط الحروف والتشكيل)
    n, lab, stats, _ = cv2.connectedComponentsWithStats(ink, 8)
    area_min = max(12, int(ink.size * 0.00004))
    keep = np.zeros(n, bool)
    keep[1:] = stats[1:, cv2.CC_STAT_AREA] >= area_min
    ink = keep[lab].astype(np.uint8)
    ys, xs = np.nonzero(ink)
    if len(xs) == 0:
        return None
    pad = int(0.06 * max(xs.max() - xs.min(), ys.max() - ys.min())) + 8
    y0, y1 = max(0, ys.min() - pad), min(ink.shape[0], ys.max() + pad)
    x0, x1 = max(0, xs.min() - pad), min(ink.shape[1], xs.max() + pad)
    ink = ink[y0:y1, x0:x1]
    return np.where(ink > 0, 0, 255).astype(np.uint8)

def vectorize(png_path: Path, svg_path: Path):
    if vtracer is None:
        return False
    try:
        vtracer.convert_image_to_svg_py(
            str(png_path), str(svg_path),
            colormode="binary", mode="spline",
            filter_speckle=4, corner_threshold=60,
            length_threshold=4.0, splice_threshold=45, path_precision=3,
        )
        return svg_path.exists()
    except Exception as e:
        print("   vtracer:", e)
        return False

# ---------------------------------------------------------------- التشغيل
def safe_name(s):
    return re.sub(r'[\\/:*?"<>|]+', "_", s)[:80] or "x"

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("source")
    ap.add_argument("output")
    ap.add_argument("--no-svg", action="store_true")
    ap.add_argument("--limit", type=int, default=0, help="للتجربة: عدد ملفات محدود")
    a = ap.parse_args()

    src, out = Path(a.source), Path(a.output)
    for d in ("clean_png", "svg", "thumbs"):
        (out / d).mkdir(parents=True, exist_ok=True)

    files = []
    for p in src.rglob("*"):
        if not p.is_file() or p.suffix.lower() not in IMG_EXT:
            continue
        rel_parts = p.relative_to(src).parts[:-1]
        if any(any(mk in part for mk in SKIP_DIR_MARKERS) for part in rel_parts):
            continue
        files.append(p)
    files.sort()
    if a.limit:
        files = files[: a.limit]
    print(f"عدد الصور: {len(files)}")
    if vtracer is None and not a.no_svg:
        print("تنبيه: مكتبة vtracer غير مثبتة — لن تُنتج ملفات SVG (pip install vtracer)")

    rows, sha_seen, dh_list = [], {}, []
    t0 = time.time()
    for i, p in enumerate(files, 1):
        rel = str(p.relative_to(src))
        row = {"id": i, "file": rel, "folder": str(Path(rel).parent), "status": "", "text": "",
               "tier": "", "width": "", "height": "", "duplicate_of": "", "similar_to": "",
               "clean_png": "", "svg": "", "thumb": "", "note": ""}
        try:
            raw = p.read_bytes()
            sha = hashlib.sha1(raw).hexdigest()
            text = clean_name(p.stem)
            row["text"] = text
            row["tier"] = next((t for t, mks in QUALITY_MARKERS.items() if any(mk in rel for mk in mks)), "")
            if SOCIAL_RE.search(p.stem) and not ARABIC_RE.search(p.stem):
                row["note"] = "اسم من سوشيال ميديا"
            if sha in sha_seen:
                row["status"] = "duplicate"
                row["duplicate_of"] = sha_seen[sha]
                rows.append(row)
                continue
            sha_seen[sha] = rel

            rgb = load_rgb(p)
            h, w = rgb.shape[:2]
            row["width"], row["height"] = w, h
            work = resize_work(rgb)
            m = measure(work)
            dh = dhash(m["gray"])
            for other_dh, other_rel in dh_list:
                if hamming(dh, other_dh) <= 10:
                    row["similar_to"] = other_rel
                    break
            dh_list.append((dh, rel))

            status = classify(m, w, h, bool(ARABIC_RE.search(text)))
            row["status"] = status
            base = f"{i:05d}_{safe_name(text or p.stem)}"

            thumb = work.copy()
            th = Image.fromarray(thumb); th.thumbnail((260, 260))
            th.save(out / "thumbs" / f"{base}.jpg", quality=80)
            row["thumb"] = f"thumbs/{base}.jpg"

            if status in ("clean", "needs_cleaning", "inverted"):
                bw = clean_image(m["gray"], invert=(status == "inverted"))
                if bw is None:
                    row["note"] += " | لم يُعثر على حبر"
                else:
                    png = out / "clean_png" / f"{base}.png"
                    Image.fromarray(bw).convert("1").save(png)
                    row["clean_png"] = f"clean_png/{base}.png"
                    if not a.no_svg:
                        svg = out / "svg" / f"{base}.svg"
                        if vectorize(png, svg):
                            row["svg"] = f"svg/{base}.svg"
        except Exception as e:
            row["status"] = "error"
            row["note"] = str(e)[:200]
        rows.append(row)
        if i % 50 == 0 or i == len(files):
            el = time.time() - t0
            print(f"  {i}/{len(files)}  ({el/i:.2f} ث/صورة)")

    write_reports(rows, out)

def write_reports(rows, out: Path):
    keys = list(rows[0].keys()) if rows else []
    with open(out / "report.csv", "w", newline="", encoding="utf-8-sig") as f:
        wr = csv.DictWriter(f, fieldnames=keys); wr.writeheader(); wr.writerows(rows)

    from collections import Counter
    cnt = Counter(r["status"] for r in rows)
    texts = Counter(r["text"] for r in rows if r["status"] in ("clean", "needs_cleaning", "inverted") and r["text"])
    summary = {"total": len(rows), "by_status": dict(cnt),
               "usable": sum(cnt[s] for s in ("clean", "needs_cleaning", "inverted")),
               "distinct_texts_usable": len(texts),
               "top_texts": texts.most_common(30)}
    (out / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")

    labels = {"clean": "جاهزة", "needs_cleaning": "تحتاج تنظيفًا (تم تنظيفها)", "inverted": "خط فاتح على داكن (تم قلبها)",
              "complex": "معقدة — مراجعة يدوية", "too_small": "دقة منخفضة", "unnamed": "بدون اسم عربي",
              "duplicate": "مكرر", "error": "خطأ"}
    order = ["clean", "needs_cleaning", "inverted", "complex", "unnamed", "too_small", "duplicate", "error"]
    parts = [f"""<!doctype html><html lang="ar" dir="rtl"><meta charset="utf-8"><title>تقرير مكتبة الديواني</title>
<style>body{{font-family:Tahoma,Arial;background:#f4f4f2;margin:24px;color:#222}}
h1{{margin:0 0 6px}} .sum span{{display:inline-block;background:#fff;border:1px solid #ddd;border-radius:8px;padding:6px 12px;margin:4px}}
h2{{margin-top:36px;border-bottom:2px solid #222;padding-bottom:4px}} .grid{{display:flex;flex-wrap:wrap;gap:10px}}
.card{{background:#fff;border:1px solid #ddd;border-radius:8px;width:270px;padding:6px;font-size:12px}}
.pair{{display:flex;gap:4px}} .pair img{{width:130px;height:130px;object-fit:contain;background:#fff;border:1px solid #eee}}
.t{{font-weight:bold;font-size:14px;margin:4px 0}} .f{{color:#777;word-break:break-all}} .n{{color:#b33}}
details summary{{cursor:pointer;font-size:18px;font-weight:bold;margin-top:24px}}</style>
<h1>تقرير مكتبة الديواني</h1><div class="sum">
<span>الإجمالي: <b>{summary['total']}</b></span><span>صالح للمكتبة: <b>{summary['usable']}</b></span>
<span>نصوص مختلفة صالحة: <b>{summary['distinct_texts_usable']}</b></span>"""]
    for s in order:
        if cnt.get(s):
            parts.append(f"<span>{labels[s]}: <b>{cnt[s]}</b></span>")
    parts.append("</div>")
    for s in order:
        group = [r for r in rows if r["status"] == s]
        if not group:
            continue
        open_attr = " open" if s in ("needs_cleaning", "inverted") else ""
        parts.append(f"<details{open_attr}><summary>{labels[s]} ({len(group)})</summary><div class='grid'>")
        for r in group[:600]:
            left = f"<img loading='lazy' src='{html.escape(r['thumb'])}'>" if r["thumb"] else ""
            right = f"<img loading='lazy' src='{html.escape(r['clean_png'])}'>" if r["clean_png"] else ""
            extra = ""
            if r["tier"]: extra += f"<div>⭐ {r['tier']}</div>"
            if r["similar_to"]: extra += f"<div class='n'>يشبه: {html.escape(r['similar_to'])}</div>"
            if r["duplicate_of"]: extra += f"<div class='n'>نسخة من: {html.escape(r['duplicate_of'])}</div>"
            if r["note"]: extra += f"<div class='n'>{html.escape(r['note'])}</div>"
            parts.append(f"<div class='card'><div class='pair'>{right}{left}</div>"
                         f"<div class='t'>{html.escape(r['text'] or '—')}</div><div class='f'>{html.escape(r['file'])}</div>{extra}</div>")
        if len(group) > 600:
            parts.append(f"<p>… و {len(group)-600} أخرى في report.csv</p>")
        parts.append("</div></details>")
    (out / "report.html").write_text("".join(parts), encoding="utf-8")
    print("\nتم. افتح:", out / "report.html")
    print(json.dumps(summary["by_status"], ensure_ascii=False))

if __name__ == "__main__":
    main()
