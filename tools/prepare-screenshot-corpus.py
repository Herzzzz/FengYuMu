"""Decode original fixtures to raw BGRA without resizing, enhancing or changing pixels.

The manifest binds each test input to its source-file SHA256. This tests the shared
strong OCR engine on Linux, not WinRT, GDI, live capture, hotkeys or paid AI services.
"""
import argparse, hashlib, json
from pathlib import Path
from PIL import Image

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--repo',type=Path,default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    args.output.mkdir(parents=True,exist_ok=True)
    fixtures=args.repo/'tests/fixtures'
    files=sorted((fixtures/'public-v3').glob('*.png'))
    files+=[fixtures/'character-info-pet-item-list-20260909.png']
    manifest=[]
    for source in files:
        with Image.open(source) as image:
            image=image.convert('RGBA')
            raw=image.tobytes('raw','BGRA')
            destination=args.output/(source.stem+'.bgra')
            destination.write_bytes(raw)
            manifest.append(dict(id=source.stem,source=str(source.relative_to(args.repo)),
                sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                rawSha256=hashlib.sha256(raw).hexdigest(),width=image.width,height=image.height,
                bgraPath=str(destination.resolve()),duplicateOf='quest-dr-kim' if source.stem=='quest-dr-kim-gyazo' else None))
    (args.output/'inputs.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
    print(f'{len(manifest)} source screenshots decoded; no image scaling or enhancement')

if __name__=='__main__':main()
