"""Regenerate pixel replay inputs from the user's original36-image ZIP.
Requires Pillow. No network, no app OCR model changes. Original images are not
included in source evidence; hashes/controlled geometry are preserved instead.
Usage: python prepare-skill-screenshot-fixtures-r15.py INPUT.zip OUTPUT_DIR
"""
from pathlib import Path
import hashlib, io, json, sys, zipfile
from PIL import Image
repo=Path(__file__).resolve().parents[1]
source=Path(sys.argv[1]);out=Path(sys.argv[2]);out.mkdir(parents=True,exist_ok=True)
proof=json.loads((repo/'tests/fixtures/skill-screenshot-geometry-r15.json').read_text())
with zipfile.ZipFile(source) as archive:
 entries={Path(x.filename).name:x for x in archive.infolist() if not x.is_dir() and x.filename.lower().endswith('.png')}
 assert len(entries)==proof['originalImages']
 fixtures=[]
 for case in proof['fixtures']:
  entry=entries[case['sourceName']];assert entry.file_size<=5_000_000
  raw=archive.read(entry);assert hashlib.sha256(raw).hexdigest()==case['sourceSha256']
  image=Image.open(io.BytesIO(raw)).convert('RGB').crop(case['crop']);canvas=Image.new('RGBA',(case['width'],case['height']),(30,35,55,255));canvas.paste(image,(30,30));pixels=canvas.tobytes('raw','BGRA');assert hashlib.sha256(pixels).hexdigest()==case['pixelSha256']
  path=out/('replay-'+case['case']+'.pixels');path.write_bytes(pixels);fixtures.append(dict(case,pixels=str(path.resolve())))
(out/'pixel-replay.json').write_text(json.dumps(fixtures,ensure_ascii=False,indent=2)+'\n')
print(str(out/'pixel-replay.json'))
