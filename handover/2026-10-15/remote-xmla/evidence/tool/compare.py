# Oracle compare: local SSAS results (pilot) vs remote XMLA results. Rule: numbers exact, decimals within 1e-9,
# column names compared as given, same query text. Usage: python -I compare.py ssas.json remote.json label out.md
import json, sys

def load(p):
    with open(p, encoding="utf-8-sig") as f:
        d = json.load(f)
    return {q["id"]: q for q in (d if isinstance(d, list) else [d])}

def same(a, b):
    if a is None or b is None:
        return a is None and b is None
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool):
        return a == b or abs(float(a) - float(b)) <= 1e-9
    return a == b

ssas, remote, label, out = load(sys.argv[1]), load(sys.argv[2]), sys.argv[3], sys.argv[4]
lines = [f"| Query | SSAS rows | {label} rows | Same query | Columns equal | Cells equal | Errors | Match |", "|---|---|---|---|---|---|---|---|"]
ok = 0
for qid in sorted(ssas):
    s, r = ssas[qid], remote.get(qid, {})
    sr, rr = s.get("rows") or [], r.get("rows") or []
    sr = [x if isinstance(x, list) else [x] for x in sr]
    rr = [x if isinstance(x, list) else [x] for x in rr]
    if len(sr) == 1 and len(sr[0]) > 1 and all(not isinstance(v, list) for v in sr[0]) and len(rr) == 1:
        pass
    sameq = s.get("query") == r.get("query")
    cols = list(s.get("columns") or []) == list(r.get("columns") or [])
    cells = len(sr) == len(rr) and all(len(a) == len(b) and all(same(x, y) for x, y in zip(a, b)) for a, b in zip(sr, rr))
    err = bool(s.get("error")) or bool(r.get("error"))
    m = sameq and cols and cells and not err
    ok += m
    lines.append(f"| {qid} | {len(sr)} | {len(rr)} | {sameq} | {cols} | {cells} | {err} | {'MATCH' if m else 'DIFF'} |")
verdict = "PASS" if ok == len(ssas) else "FAIL"
lines.append("")
lines.append(f"Result: {verdict} - {ok}/{len(ssas)} queries match")
with open(out, "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")
print("\n".join(lines))
