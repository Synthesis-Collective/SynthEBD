#!/usr/bin/env python3
"""Vetted, read-only inspector for a SynthEBD asset-pack config JSON.

Prints the subgroup tree with the fields that matter during a review pass (enabled/distribution
flags, race rules, required/excluded links, attributes, body-shape descriptors, and a shortened
Source/Destination per path). Pure stdlib, reads one file, writes nothing.

Usage:
    python inspect_config.py "<path to config>.json" [--paths] [--rules] [--grep <ID-or-name substring>]

    --paths   show each path's Source (shortened) and Destination
    --rules   show AllowedAttributes/DisallowedAttributes and body-shape descriptors
    --grep S  only print subgroups whose ID or Name contains S (case-insensitive)
"""
import json, sys, argparse

BS = chr(92)

def load(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)

def short_source(src):
    s = src.replace("/", BS)
    # collapse "...textures\<Prefix>\<...maybe FOMOD wrapper...>\...\actors\..." to the meaningful tail
    for marker in ("FOMOD" + BS, "textures" + BS):
        if marker in s:
            s = s.split(marker, 1)[1]
    return s

def short_dest(d):
    if "SkinTexture" in d:
        return "SkinTexture." + d.split("SkinTexture.", 1)[1]
    if "WorldModel" in d:
        return "WorldModel." + d.split("WorldModel.", 1)[1]
    if "AlternateTextures" in d:
        return "AltTextures..." + d.rsplit("].", 1)[-1]
    if "HeadTexture" in d:
        return "HeadTexture." + d.split("HeadTexture.", 1)[1]
    return d

def fmt_attrs(attr_list):
    out = []
    for attr in attr_list:
        parts = []
        for sa in attr.get("SubAttributes", []):
            t = sa.get("Type")
            if t == "Group":
                val = "Group(" + ",".join(sa.get("SelectedLabels", [])) + ")"
            else:
                val = t + "(" + ",".join(sa.get("FormKeys", [])) + ")"
            mode = sa.get("ForceMode", "")
            if sa.get("Not"):
                val = "NOT " + val
            parts.append(val + ":" + mode + (":w%s" % sa["Weighting"] if sa.get("Weighting", 1) != 1 else ""))
        out.append(" AND ".join(parts))
    return " OR ".join(out)

def fmt_descriptors(lst):
    return ",".join(f"{d.get('Category')}={d.get('Value')}" for d in lst)

def walk(sg, depth, args):
    show = True
    if args.grep:
        g = args.grep.lower()
        show = g in sg.get("ID", "").lower() or g in sg.get("Name", "").lower()
    ind = "  " * depth
    flags = ""
    if not sg.get("Enabled", True): flags += " [DISABLED]"
    if not sg.get("DistributionEnabled", True): flags += " [DIST-OFF]"
    rr = []
    if sg.get("AllowedRaceGroupings"): rr.append("allowRG=" + ",".join(sg["AllowedRaceGroupings"]))
    if sg.get("AllowedRaces"): rr.append("allowR#" + str(len(sg["AllowedRaces"])))
    if sg.get("DisallowedRaceGroupings"): rr.append("disRG=" + ",".join(sg["DisallowedRaceGroupings"]))
    if sg.get("DisallowedRaces"): rr.append("disR#" + str(len(sg["DisallowedRaces"])))
    if sg.get("RequiredSubgroups"): rr.append("REQ=" + ",".join(sg["RequiredSubgroups"]))
    if sg.get("ExcludedSubgroups"): rr.append("EXCL=" + ",".join(sg["ExcludedSubgroups"]))
    if sg.get("ProbabilityWeighting", 1.0) != 1.0: rr.append("w=" + str(sg["ProbabilityWeighting"]))
    if show:
        print(f"{ind}{sg.get('ID',''):20}{sg.get('Name','')[:34]:34}{flags}  {' '.join(rr)}")
        if args.rules:
            if sg.get("AllowedAttributes"): print(f"{ind}    +ATTR {fmt_attrs(sg['AllowedAttributes'])}")
            if sg.get("DisallowedAttributes"): print(f"{ind}    -ATTR {fmt_attrs(sg['DisallowedAttributes'])}")
            for key, lbl in (("AllowedBodySlideDescriptors","+BS"),("DisallowedBodySlideDescriptors","-BS"),
                             ("AllowedBodyGenDescriptors","+BG"),("DisallowedBodyGenDescriptors","-BG"),
                             ("PrioritizedBodySlideDescriptors","*BS")):
                if sg.get(key): print(f"{ind}    {lbl} {fmt_descriptors(sg[key])}")
        if args.paths:
            for p in sg.get("Paths", []):
                print(f"{ind}    {short_source(p['Source'])}  ->  {short_dest(p['Destination'])}")
    for c in sg.get("Subgroups", []):
        walk(c, depth + 1, args)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("config")
    ap.add_argument("--paths", action="store_true")
    ap.add_argument("--rules", action="store_true")
    ap.add_argument("--grep")
    args = ap.parse_args()
    cfg = load(args.config)
    print("GroupName:", cfg.get("GroupName"), "| ShortName:", cfg.get("ShortName"),
          "| Type:", cfg.get("ConfigType"), "| Gender:", cfg.get("Gender"))
    print("DefaultRecordTemplate:", cfg.get("DefaultRecordTemplate"))
    if cfg.get("AssociatedBodyGenConfigName"):
        print("AssociatedBodyGenConfigName:", cfg["AssociatedBodyGenConfigName"])
    rg = cfg.get("RaceGroupings", [])
    ag = cfg.get("AttributeGroups", [])
    print(f"Local RaceGroupings: {len(rg)}  Local AttributeGroups: {len(ag)}")
    dr = cfg.get("DistributionRules") or {}
    if dr and (dr.get("AllowedAttributes") or dr.get("AllowedRaceGroupings") or dr.get("AllowedBodySlideDescriptors")):
        print("Whole-config DistributionRules present (see --rules on root).")
    print("--- subgroup tree ---")
    for t in cfg.get("Subgroups", []):
        walk(t, 0, args)
        print()

if __name__ == "__main__":
    main()
