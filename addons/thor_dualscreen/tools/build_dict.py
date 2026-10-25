"""Build the lower-screen Japanese dictionary (SQLite).

Sources (both CC BY-SA, attribution shown in the app):
  - JMdict_e (EDRDG) for headwords, readings and English glosses
  - Chinese Wiktionary Japanese entries (kaikki.org extracts of zh.wiktionary) for Chinese glosses

usage: build_dict.py <src dir> <out.db>
"""
import gzip, json, os, re, sqlite3, sys
import xml.etree.ElementTree as ET

try:
    # Chinese Wiktionary mixes traditional and simplified glosses; show them all in simplified
    from opencc import OpenCC  # pip install opencc-python-reimplemented
    _t2s = OpenCC('t2s').convert
except ImportError:
    print('opencc not installed: Chinese glosses are kept as written')
    _t2s = lambda s: s

src, out = sys.argv[1], sys.argv[2]
if os.path.exists(out):
    os.remove(out)

KANA = re.compile(r'^[぀-ヿー]+$')


def hira(s):
    return ''.join(chr(ord(c) - 0x60) if 'ァ' <= c <= 'ヶ' else c for c in s)


entries = []          # [head, reading, pos, zh, en, rank, uk, pin]
rebs_of = []          # every reading of each entry (hiragana)
keys = {}             # key -> set(entry index)


def add_key(k, idx):
    if k:
        keys.setdefault(k, set()).add(idx)


# ---- JMdict ------------------------------------------------------------------
POS_MAP = [('verb', '动词'), ('adjective', '形容词'), ('adverb', '副词'), ('noun', '名词'), ('pronoun', '代词'),
           ('conjunction', '接续词'), ('interjection', '感叹词'), ('particle', '助词'), ('expression', '惯用语'),
           ('counter', '量词'), ('prefix', '前缀'), ('suffix', '后缀'), ('auxiliary', '助动词')]


CODE_MAP = [('adj-pn', '连体词'), ('aux', '助动词'), ('v', '动词'), ('adj', '形容词'), ('adv', '副词'), ('pn', '代词'), ('n', '名词'),
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
PRI = {'ichi1': 4, 'news1': 4, 'spec1': 3, 'gai1': 2, 'ichi2': 1, 'news2': 1, 'spec2': 1, 'gai2': 1}


def rank_of(tags):
    # JMdict frequency markers: higher means more common
    r = 0
    for t in tags:
        if t in PRI:
            r += PRI[t]
        elif t.startswith('nf') and t[2:].isdigit():
            r += max(0, (49 - int(t[2:])) // 8)
    return r


for e in root.iter('entry'):
    kebs = [k.text for k in e.findall('k_ele/keb')]
    rebs = [r.text for r in e.findall('r_ele/reb')]
    # commonness of the entry = its most common written form (not a sum over forms)
    rank = max([rank_of([t.text for t in el.findall('ke_pri') + el.findall('re_pri') if t.text])
                for el in e.findall('k_ele') + e.findall('r_ele')] or [0])
    uk = int(any((m.text or '').strip() == 'uk' for m in e.findall('sense/misc')))
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
    entries.append([kebs[0] if kebs else rebs[0], rebs[0] if rebs else '', pos, '', en, rank, uk, 0])
    rebs_of.append({hira(r) for r in rebs})
    for k in kebs + rebs:
        add_key(k, idx)
del root
print('jmdict entries', len(entries))

# Everyday words written in kana match many rare kanji entries (いる: 居る 要る 鋳る 射る ...).
# Pin the sense a reader means; the zh gloss of the kana word attaches to that entry too.
PREFER = {
    'いる': '居る', 'ある': '有る', 'なる': '成る', 'する': '為る', 'くる': '来る', 'いう': '言う', 'みる': '見る',
    'おく': '置く', 'いく': '行く', 'しまう': '仕舞う', 'くれる': '呉れる', 'もらう': '貰う', 'あげる': '上げる',
    'やる': '遣る', 'できる': '出来る', 'いただく': '頂く', 'ください': '下さい', 'わかる': '分かる',
    'こと': '事', 'もの': '物', 'とき': '時', 'ところ': '所', 'ため': '為', 'よう': '様', 'わけ': '訳', 'はず': '筈',
    'ほう': '方', 'ここ': '此処', 'そこ': '其処', 'あそこ': '彼処', 'どこ': '何処', 'これ': '此れ', 'それ': '其れ',
    'あれ': '彼れ', 'どれ': '何れ', 'この': '此の', 'その': '其の', 'あの': '彼の', 'どの': '何の', 'こう': '斯う',
    'そう': '然う', 'どう': '如何', 'なに': '何', 'なん': '何', 'だれ': '誰', 'いつ': '何時', 'まだ': '未だ',
    'また': '又', 'ちょっと': '一寸', 'やはり': '矢張り', 'やっぱり': '矢っ張り', 'たぶん': '多分',
    'みんな': '皆', 'ひと': '人', 'かれ': '彼', 'わたし': '私', 'あなた': '貴方', 'いい': '良い', 'よい': '良い',
    'ない': '無い', 'すごい': '凄い', 'だめ': '駄目', 'ちがう': '違う', 'ほんとう': '本当', 'ほんと': '本当',
    'ぜんぶ': '全部', 'いま': '今', 'とても': '迚も', 'ほら': 'ほら', 'うん': 'うん', 'ええ': 'ええ',
}
prefer_idx = {}
for kana, kanji in PREFER.items():
    both = keys.get(kana, set()) & keys.get(kanji, set())
    if both:
        i = max(both, key=lambda i: (entries[i][0] == kanji, hira(entries[i][1]) == kana, entries[i][5]))
        entries[i][7] = 1
        prefer_idx[kana] = i
print('pinned', len(prefer_idx), 'of', len(PREFER), sorted(set(PREFER) - set(prefer_idx)))

# ---- Chinese Wiktionary ----------------------------------------------------
ZH_POS = {'verb': '动词', 'adj': '形容词', 'adv': '副词', 'noun': '名词', 'name': '专有名词', 'pron': '代词',
          'intj': '感叹词', 'conj': '接续词', 'particle': '助词', 'phrase': '惯用语', 'suffix': '后缀', 'prefix': '前缀',
          'counter': '量词', 'num': '数词', 'adnominal': '连体词', 'unknown': '其他'}
NOUNISH = {'名词', '代词', '数词', '专有名词', ''}


def pos_ok(a, b):
    # parts of speech from the two sources agree well enough to share a gloss
    return not a or not b or a == b or (a in NOUNISH and b in NOUNISH) or '惯用语' in (a, b)


KANA_ANY = re.compile(r'[ぁ-ゖァ-ヺ]')
DICT_HEAD = re.compile(r'\S*【[^】]*】')
CONJ = re.compile(r'[自他][动動]?(?:五|一|サ|カ|上一|下一|变|變)\S*')


def clean_gloss(g):
    # some glosses are pasted from a printed dictionary: 「立つ【たつ】 自五 1. 立，站。 二本足で立つ 用两条腿站着」
    if '【' not in g:
        return g
    g = CONJ.sub(' ', DICT_HEAD.sub(' ', g))
    g = re.sub(r'\d+\.\s*', ' ', g)
    parts = []
    for tok in g.split():
        if KANA_ANY.search(tok):
            break   # the example sentences start here
        tok = tok.strip('。；，')
        if tok:
            parts.append(tok)
    return '，'.join(parts)


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
                g = clean_gloss(re.sub(r'\s*\n\s*', ' ', g.strip())).rstrip('。')
                if g and not FORM_OF.search(g) and '尚未创建' not in g and '尚未創建' not in g and g not in glosses:
                    glosses.append(g)
        if not glosses:
            continue
        zh = _t2s('；'.join(glosses[:3]))
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
        zpos = ZH_POS.get(d.get('pos'), '')
        targets = keys.get(word, set())
        targets = {i for i in targets if pos_ok(entries[i][2], zpos)} or targets
        if readings:
            # an entry spelled the same but read differently is another word (一寸 いっすん / ちょっと)
            targets = {i for i in targets if i >= len(rebs_of) or rebs_of[i] & readings}
        exact = {i for i in targets if entries[i][0] == word}
        if word in prefer_idx:
            # the pinned sense, unless this gloss is another part of speech (ある as 或る)
            targets = {prefer_idx[word]} if pos_ok(entries[prefer_idx[word]][2], zpos) else set()
        elif exact:
            targets = exact
        elif KANA.match(word):
            # a kana headword (ここ, いる, ...) matches several kanji entries; do not guess,
            # keep it as its own entry, which a lookup of that exact kana then prefers
            targets = set()
        elif targets:
            best = max(targets, key=lambda i: (entries[i][5], entries[i][6]))
            targets = {best}
        done = False
        for i in targets:
            if not entries[i][3]:
                entries[i][3] = zh
                done = True
        if done:
            attached += 1
        elif not targets:
            idx = len(entries)
            reading = hira(word) if KANA.match(word) else (sorted(readings)[0] if readings else '')
            entries.append([word, reading, zpos, zh, '', 0, 0, 0])
            add_key(word, idx)
            for r in readings:
                add_key(r, idx)
            created += 1
print('zh attached', attached, 'zh-only entries', created)

# ---- write --------------------------------------------------------------------
db = sqlite3.connect(out)
db.execute('CREATE TABLE entry (id INTEGER PRIMARY KEY, head TEXT, reading TEXT, pos TEXT, zh TEXT, en TEXT, rank INTEGER, uk INTEGER, pin INTEGER)')
db.execute('CREATE TABLE key (k TEXT, id INTEGER)')
db.executemany('INSERT INTO entry VALUES (?,?,?,?,?,?,?,?,?)', ((i, *e) for i, e in enumerate(entries)))
db.executemany('INSERT INTO key VALUES (?,?)', ((k, i) for k, ids in keys.items() for i in ids))
db.execute('CREATE INDEX key_k ON key (k)')
db.execute('CREATE TABLE meta (k TEXT, v TEXT)')
db.executemany('INSERT INTO meta VALUES (?,?)', [
    ('version', '3'),
    ('sources', 'JMdict (EDRDG, CC BY-SA 4.0); zh.wiktionary via kaikki.org (CC BY-SA)'),
])
db.commit()
db.execute('VACUUM')
db.close()
print('written', out, os.path.getsize(out))
