"""Build the lower-screen Japanese dictionary (SQLite).

Sources (both CC BY-SA, attribution shown in the app):
  - JMdict_e (EDRDG) for headwords, readings and English glosses
  - Chinese Wiktionary Japanese entries (kaikki.org extracts of zh.wiktionary) for Chinese glosses

usage: build_dict.py <src dir> <out.db>
"""
import gzip, json, os, re, sqlite3, sys
import xml.etree.ElementTree as ET

src, out = sys.argv[1], sys.argv[2]
if os.path.exists(out):
    os.remove(out)

KANA = re.compile(r'^[぀-ヿー]+$')


def hira(s):
    return ''.join(chr(ord(c) - 0x60) if 'ァ' <= c <= 'ヶ' else c for c in s)


entries = []          # [head, reading, pos, zh, en]
keys = {}             # key -> set(entry index)


def add_key(k, idx):
    if k:
        keys.setdefault(k, set()).add(idx)


# ---- JMdict ------------------------------------------------------------------
POS_MAP = [('verb', '动词'), ('adjective', '形容词'), ('adverb', '副词'), ('noun', '名词'), ('pronoun', '代词'),
           ('conjunction', '接续词'), ('interjection', '感叹词'), ('particle', '助词'), ('expression', '惯用语'),
           ('counter', '量词'), ('prefix', '前缀'), ('suffix', '后缀'), ('auxiliary', '助动词')]


CODE_MAP = [('aux', '助动词'), ('v', '动词'), ('adj', '形容词'), ('adv', '副词'), ('pn', '代词'), ('n', '名词'),
            ('conj', '接续词'), ('int', '感叹词'), ('prt', '助词'), ('exp', '惯用语'), ('ctr', '量词'), ('pref', '前缀'),
            ('suf', '后缀'), ('num', '数词')]


def pos_label(texts):
    # JMdict uses entity codes such as v1, v5r, adj-i, adj-na, n, adv
    for t in texts:
        code = t.strip()
        for k, v in CODE_MAP:
            if code == k or code.startswith(k + '-') or (k == 'v' and re.match(r'^v[0-9kszin]', code)):
                return v
    return ''


with gzip.open(os.path.join(src, 'JMdict_e.gz'), 'rb') as f:
    # entity references (&n; &v1; ...) would need the DTD; keep their names instead
    data = f.read().decode('utf-8')
data = re.sub(r'&([\w-]+);', r'\1', data)
data = re.sub(r'<!DOCTYPE JMdict \[.*?\]>', '', data, flags=re.S)
root = ET.fromstring(data)
del data
for e in root.iter('entry'):
    kebs = [k.text for k in e.findall('k_ele/keb')]
    rebs = [r.text for r in e.findall('r_ele/reb')]
    senses = []
    pos = ''
    for s in e.findall('sense'):
        gl = [g.text for g in s.findall('gloss') if g.text]
        if not pos:
            pos = pos_label([p.text or '' for p in s.findall('pos')])
        if gl:
            senses.append(', '.join(gl[:4]))
        if len(senses) >= 3:
            break
    if not senses:
        continue
    en = '; '.join(senses)
    if len(en) > 160:
        en = en[:157] + '…'
    idx = len(entries)
    entries.append([kebs[0] if kebs else rebs[0], rebs[0] if rebs else '', pos, '', en])
    for k in kebs + rebs:
        add_key(k, idx)
del root
print('jmdict entries', len(entries))

# ---- Chinese Wiktionary ----------------------------------------------------
ZH_POS = {'verb': '动词', 'adj': '形容词', 'adv': '副词', 'noun': '名词', 'name': '专有名词', 'pron': '代词',
          'intj': '感叹词', 'conj': '接续词', 'particle': '助词', 'phrase': '惯用语', 'suffix': '后缀', 'prefix': '前缀',
          'counter': '量词', 'num': '数词'}
FORM_OF = re.compile(r'的(舊字體|旧字体|異體|异体|另一種寫法|另一种写法)')
attached = created = 0
for fn in ('zh-ja-1.jsonl', 'zh-ja-2.jsonl'):
    for line in open(os.path.join(src, fn), encoding='utf-8'):
        d = json.loads(line)
        word = d.get('word') or ''
        if not word or d.get('pos') == 'character':
            continue
        glosses = []
        for s in d.get('senses', []):
            for g in s.get('glosses') or []:
                g = re.sub(r'\s*\n\s*', ' ', g.strip()).rstrip('。')
                if g and not FORM_OF.search(g) and g not in glosses:
                    glosses.append(g)
        if not glosses:
            continue
        zh = '；'.join(glosses[:3])
        if len(zh) > 120:
            zh = zh[:117] + '…'
        readings = set()
        for snd in d.get('sounds', []) or []:
            o = snd.get('other')
            if o and KANA.match(o):
                readings.add(hira(o))
        for fm in d.get('forms', []) or []:
            f = fm.get('form') or ''
            if KANA.match(f):
                readings.add(hira(f))
        if KANA.match(word):
            readings.add(hira(word))
        targets = keys.get(word, set())
        if readings:
            matched = {i for i in targets if hira(entries[i][1]) in readings}
            targets = matched or targets
        done = False
        for i in targets:
            if not entries[i][3]:
                entries[i][3] = zh
                done = True
        if done:
            attached += 1
        elif not targets:
            idx = len(entries)
            entries.append([word, sorted(readings)[0] if readings else '', ZH_POS.get(d.get('pos'), ''), zh, ''])
            add_key(word, idx)
            for r in readings:
                add_key(r, idx)
            created += 1
print('zh attached', attached, 'zh-only entries', created)

# ---- write --------------------------------------------------------------------
db = sqlite3.connect(out)
db.execute('CREATE TABLE entry (id INTEGER PRIMARY KEY, head TEXT, reading TEXT, pos TEXT, zh TEXT, en TEXT)')
db.execute('CREATE TABLE key (k TEXT, id INTEGER)')
db.executemany('INSERT INTO entry VALUES (?,?,?,?,?,?)', ((i, *e) for i, e in enumerate(entries)))
db.executemany('INSERT INTO key VALUES (?,?)', ((k, i) for k, ids in keys.items() for i in ids))
db.execute('CREATE INDEX key_k ON key (k)')
db.execute('CREATE TABLE meta (k TEXT, v TEXT)')
db.executemany('INSERT INTO meta VALUES (?,?)', [
    ('version', '1'),
    ('sources', 'JMdict (EDRDG, CC BY-SA 4.0); zh.wiktionary via kaikki.org (CC BY-SA)'),
])
db.commit()
db.execute('VACUUM')
db.close()
print('written', out, os.path.getsize(out))
