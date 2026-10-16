"""Package integrity and confidentiality check for a PbiBench portable zip.
usage: python -I package_check.py <dist-dir> <expected-commit> <repo-guide-path> <out-log>
dist-dir holds the CI outputs: the zip, SHA256SUMS.txt, MANIFEST.txt, source-identity.json."""
import hashlib, io, json, os, re, sys, zipfile

dist, commit, guide, out = sys.argv[1:5]
lines, fails = [], []


def check(ok, label):
    lines.append(("PASS " if ok else "FAIL ") + label)
    if not ok:
        fails.append(label)


def sha(b):
    return hashlib.sha256(b).hexdigest()


zips = [f for f in os.listdir(dist) if f.endswith('.zip')]
check(len(zips) == 1, 'one zip in dist: %s' % zips)
zpath = os.path.join(dist, zips[0])
zbytes = open(zpath, 'rb').read()
sums = {}
for line in open(os.path.join(dist, 'SHA256SUMS.txt'), encoding='utf-8-sig'):
    parts = line.split()
    if len(parts) >= 2:
        sums[parts[-1].lstrip('*').replace('\\', '/')] = parts[0].lower()
check(sums.get(zips[0]) == sha(zbytes), 'zip sha256 = SHA256SUMS: ' + sha(zbytes))
z = zipfile.ZipFile(io.BytesIO(zbytes))
names = [n for n in z.namelist() if not n.endswith('/')]
check(all(not n.startswith('/') and '..' not in n.split('/') and ':' not in n for n in names), 'zip entries safe (no absolute/.. paths)')
manifest_outer = open(os.path.join(dist, 'MANIFEST.txt'), encoding='utf-8-sig').read()
inner_name = [n for n in names if n.split('/')[-1] == 'MANIFEST.txt']
check(len(inner_name) == 1, 'inner MANIFEST present')
inner = z.read(inner_name[0]).decode('utf-8-sig')
check(inner.replace('\r\n', '\n').strip() == manifest_outer.replace('\r\n', '\n').strip(), 'inner MANIFEST = outer MANIFEST')
mism, mlines = [], [l for l in inner.splitlines() if l.strip()]
root = inner_name[0][:-len('MANIFEST.txt')]
for l in mlines:
    m = re.match(r'^([0-9a-fA-F]{64})\s+\*?(.+)$', l.strip())
    if not m:
        continue
    p = root + m.group(2).replace('\\', '/')
    if p not in names or sha(z.read(p)) != m.group(1).lower():
        mism.append(m.group(2))
check(not mism, 'MANIFEST lines match extracted files: %d lines, %d files, mismatches=%s' % (len(mlines), len(names), mism[:5]))
for f in ['TabularEditor.exe', 'PbiBench.Core.dll', 'mcp/TE2Enhanced.Mcp.dll']:
    p = root + f
    if f in sums:
        check(p in names and sha(z.read(p)) == sums[f], f + ' sha = SHA256SUMS')
check(not any(n.lower().endswith('.pdb') for n in names), 'no .pdb')
guides = [n for n in names if n.split('/')[-1].upper().startswith('GETTING')]
if guides:
    check(z.read(guides[0]).replace(b'\r\n', b'\n') == open(guide, 'rb').read().replace(b'\r\n', b'\n'), 'GETTING-STARTED = repo docs/pbibench/GETTING_STARTED.md')
ident = json.load(open(os.path.join(dist, 'source-identity.json'), encoding='utf-8-sig'))
check(str(ident.get('sourceCommit', '')).startswith(commit), 'sourceCommit = %s (%s)' % (commit, ident.get('sourceCommit')))
check(ident.get('dirtyTrackedFiles') in (False, 'false', 'False'), 'dirtyTrackedFiles=false (%s)' % ident.get('dirtyTrackedFiles'))

patterns = {
    'gitlab token': rb'glpat-[0-9A-Za-z_\-]{20}',
    'github token': rb'gh[pousr]_[0-9A-Za-z]{30,}',
    'openai/anthropic key': rb'sk-(ant-api\d\d-|proj-)?[0-9A-Za-z]{32,}',
    'aws key': rb'AKIA[0-9A-Z]{16}',
    'private key': rb'-----BEGIN [A-Z ]*PRIVATE KEY-----',
    'connection secret': rb'(?i)(password|pwd)\s*=\s*(?!\*{4})[^;\s"\'{}<\x00-\x1f]{3,}',
    'user profile path': rb'(?i)c:\\users\\(?!public\\|default\\)[a-z0-9._-]+\\',
    'dev tree path': rb'(?i)d:\\proj\\',
    'personal email': rb'(?i)[a-z0-9._%+-]+@(gmail|outlook|hotmail)\.com',
    'FOIL private': rb'(?i)foil[-_ ]private',
    'transcript marker': rb'(?i)claude-code transcript|"type":\s*"assistant"',
}
known_fp = {('connection secret', 'mcp/TE2Enhanced.Mcp.dll', b'Password=hunter2'), ('connection secret', 'mcp/TE2Enhanced.Mcp.dll', b'PWD=abc')}
for label, pat in patterns.items():
    hits = []
    for n in names:
        data = z.read(n)
        for variant in (data, data.replace(b'\x00', b'')):
            for m in re.finditer(pat, variant):
                hit = (label, n[len(root):], m.group(0)[:16])
                if not any(hit[0] == k[0] and hit[1] == k[1] and m.group(0).startswith(k[2]) for k in known_fp):
                    hits.append((n[len(root):], m.group(0)[:40]))
    check(not hits, 'confidentiality: no %s %s' % (label, hits[:3] if hits else ''))
lines.append('INFO known false positive excluded: Password=hunter2 and PWD=abc credential-guard test strings of the MCP self-test (TE2Enhanced.Mcp/SelfTest.cs) in mcp/TE2Enhanced.Mcp.dll (triaged on e7a8b06/95fea60)')
summary = {'zip': zips[0], 'sha256': sha(zbytes), 'bytes': len(zbytes), 'files': len(names), 'sourceCommit': ident.get('sourceCommit'), 'fails': fails}
lines.append(json.dumps(summary))
open(out, 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
print('\n'.join(lines))
sys.exit(1 if fails else 0)
