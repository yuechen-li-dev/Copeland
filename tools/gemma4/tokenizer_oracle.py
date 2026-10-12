"""Offline tokenizer fixtures. Does not install packages or run in inference."""
import json
import pathlib
import random
from tokenizers import Tokenizer

model = pathlib.Path('artifacts/local/models/gemma4-12b')
raw = json.loads((model / 'tokenizer.json').read_text(encoding='utf8'))
# Installed tokenizers supports the earlier serialization of the same BPE
# merges. This changes their JSON representation, preserving order and pairs.
raw['model']['merges'] = [' '.join(pair) for pair in raw['model']['merges']]
tokenizer = Tokenizer.from_str(json.dumps(raw))
samples = ['Hello world!', '  space\n\tline',
           '<bos><|turn>user\nSay hello.<turn|>\n<|turn>model\n',
           '你好，世界 🌋', 'naïve café\nA\u0000B', '<|channel>thought\n',
           '\U0010ffff', ' <eos> <bos> ', '\r\n\r\n\t\t']
random_source = random.Random(4812)
alphabet = list(' abcdef0123!?\n\t') + ['é', '你', '界', '🌋', '\U0010ffff']
for _ in range(128):
    samples.append(''.join(random_source.choice(alphabet) for _ in range(random_source.randint(1, 80))))
fixtures = []
for text in samples:
    ids = tokenizer.encode(text).ids
    fixtures.append(dict(Text=text, Ids=ids, Decoded=tokenizer.decode(ids)))
output = pathlib.Path('artifacts/local/gemma4/tokenizer-golden.json')
output.write_text(json.dumps(fixtures, ensure_ascii=True), encoding='utf8')
print(f'{len(fixtures)} deterministic reference fixtures: {output}')
