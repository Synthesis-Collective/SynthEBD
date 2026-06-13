#!/usr/bin/env python3
"""Open a side-by-side montage of a mod's preview images in the user's browser.

Use during the styling/finalization pass so the user can SEE the variant options (FOMOD option
previews, body shots, etc.) before deciding distribution rules. Builds a local HTML file and opens
it; reads nothing else and writes only that one HTML file.

Usage:
    # all images in a folder (labels = file names):
    python preview_montage.py "<FOMOD>/image assets"

    # explicit files:
    python preview_montage.py img1.png img2.png ...

    # with labels/descriptions from a JSON sidecar: [{"path": "...", "label": "...", "desc": "..."}]
    python preview_montage.py --manifest items.json

    # choose the output path (default: a temp file next to the first input):
    python preview_montage.py "<folder>" --out montage.html
"""
import os, sys, glob, json, argparse, urllib.parse, webbrowser

EXTS = (".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif")

def furl(path):
    return "file:///" + urllib.parse.quote(os.path.abspath(path).replace("\\", "/"))

def collect(inputs):
    items = []
    for inp in inputs:
        if os.path.isdir(inp):
            for f in sorted(os.listdir(inp)):
                if f.lower().endswith(EXTS):
                    items.append({"path": os.path.join(inp, f), "label": os.path.splitext(f)[0], "desc": ""})
        else:
            for f in sorted(glob.glob(inp)):
                if f.lower().endswith(EXTS):
                    items.append({"path": f, "label": os.path.splitext(os.path.basename(f))[0], "desc": ""})
    return items

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("inputs", nargs="*")
    ap.add_argument("--manifest")
    ap.add_argument("--out")
    ap.add_argument("--title", default="Mod variant previews")
    args = ap.parse_args()

    items = []
    if args.manifest:
        items = json.load(open(args.manifest, encoding="utf-8"))
    items += collect(args.inputs)
    items = [it for it in items if os.path.exists(it["path"])]
    if not items:
        print("No images found.", file=sys.stderr)
        sys.exit(1)

    cards = []
    for it in items:
        desc = it.get("desc", "")
        cards.append(
            '<figure style="margin:0;width:320px;display:inline-block;vertical-align:top;'
            'background:#222;border-radius:8px;padding:8px;">'
            f'<div style="color:#fff;font-weight:bold;font-size:14px;margin-bottom:4px;">{it.get("label","")}</div>'
            f'<img src="{furl(it["path"])}" style="width:100%;border-radius:4px;" />'
            + (f'<figcaption style="color:#ccc;font-size:12px;margin-top:6px;">{desc}</figcaption>' if desc else "")
            + "</figure>")

    html = (f'<!doctype html><html><head><meta charset="utf-8"><title>{args.title}</title></head>'
            f'<body style="background:#111;font-family:Segoe UI,Arial,sans-serif;padding:16px;">'
            f'<h2 style="color:#fff;">{args.title}</h2>'
            f'<div style="display:flex;flex-wrap:wrap;gap:12px;">{"".join(cards)}</div></body></html>')

    out = args.out or os.path.join(os.path.dirname(os.path.abspath(items[0]["path"])), "_preview_montage.html")
    with open(out, "w", encoding="utf-8") as f:
        f.write(html)
    print("wrote", out)
    webbrowser.open(furl(out))

if __name__ == "__main__":
    main()
