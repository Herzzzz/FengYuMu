"""Create Windows runtime/source release ZIPs and the domestic mirror manifest."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

root = Path(__file__).resolve().parents[1]
output = Path(sys.argv[1]).resolve()
output.mkdir(parents=True, exist_ok=True)
exe = root / '枫语幕.exe'
record = json.loads(Path(str(exe) + '.build.json').read_text(encoding='utf-8-sig'))
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()
assert sha(exe) == record['exeSha256']
assert all(sha(root / item['path']) == item['sha256'] for item in record['inputs'])
runtime = {
    name: root / name for name in (
        '枫语幕.exe', '枫语幕.exe.build.json', '枫语幕词库.tsv',
        '基础补充词库.tsv', '任务正文.tsv', '使用说明.txt', 'LICENSE',
    )
}
runtime.update({name: root / 'tools' / name for name in ('导出最近F8诊断.cmd', 'Export-LatestF8Diagnostics.ps1')})
def archive(name, files):
    path = output / name
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for relative, file in sorted(files.items()):
            z.write(file, relative)
        manifest = {relative: sha(file) for relative, file in sorted(files.items())}
        z.writestr('FILES-SHA256.json', json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    with zipfile.ZipFile(path) as z:
        assert z.testzip() is None
        for relative, digest in manifest.items():
            assert hashlib.sha256(z.read(relative)).hexdigest().upper() == digest
    path.with_suffix(path.suffix + '.sha256').write_text(f'{sha(path)}  {name}\n', encoding='ascii')
    return path

package = archive('FengYuMu_v4.0.zip', runtime)
# Git lists only committed files: no local screenshots, logs, configuration or caches.
import subprocess
tracked = subprocess.check_output(['git', '-C', str(root), 'ls-files', '-z']).decode().split('\0')
source = {}
for relative in tracked:
    if not relative:
        continue
    file = root / relative
    if file.is_file() and file.suffix.lower() not in ('.exe', '.dll', '.pdb', '.png', '.jpg', '.jpeg', '.bmp', '.gif', '.webp'):
        source[relative] = file
archive('FengYuMu_v4.0_Source.zip', source)
base = 'https://gitee.com/Herzzzz/maple-whisper-veil/raw/main/'
(output / 'latest.json').write_text(json.dumps({
    'tag': 'v4.0', 'zipUrl': base + package.name,
    'sha256Url': base + package.name + '.sha256', 'sha256': sha(package),
}, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'tag': 'v4.0', 'exeSha256': sha(exe), 'zipSha256': sha(package), 'zipBytes': package.stat().st_size}))
