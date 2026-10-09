#!/usr/bin/env python3
"""Package only the standalone read-only probe; never install or overwrite it."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'integrations/readiness-probe'
FILES = ('README.md', 'manifest.json', 'popup.css', 'popup.html', 'popup.js', 'probe.js')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, help='New unpacked directory (existing paths are refused)')
    args = parser.parse_args()
    manifest = json.loads((SOURCE / 'manifest.json').read_text(encoding='utf-8'))
    version = manifest['version']
    if not re.fullmatch(r'\d+\.\d+\.\d+', version):
        raise SystemExit('Invalid probe version')
    if manifest['permissions'] != ['activeTab', 'scripting'] or any(k in manifest for k in ('background', 'host_permissions', 'content_scripts', 'externally_connectable')):
        raise SystemExit('Unexpected probe permissions or background entry point')
    output = (args.output or ROOT / 'dist' / ('Buddy-browser-readiness-' + version)).resolve()
    archive = output.parent / (output.name + '.zip')
    checksum = output.parent / (output.name + '.zip.sha256')
    if any(p.exists() for p in (output, archive, checksum)):
        raise SystemExit('Choose a fresh output directory; existing artifacts are preserved')
    payload = {name: (SOURCE / name).read_bytes() for name in FILES}
    hashes = ''.join(hashlib.sha256(data).hexdigest() + '  ' + name + '\n' for name, data in payload.items())
    payload['SHA256SUMS.txt'] = hashes.encode('ascii')
    output.mkdir(parents=True)
    for name, data in payload.items():
        (output / name).write_bytes(data)
    with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED) as zipped:
        for name, data in sorted(payload.items()):
            info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            zipped.writestr(info, data)
    with zipfile.ZipFile(archive) as zipped:
        assert set(zipped.namelist()) == set(payload)
        assert all(zipped.read(name) == data == (output / name).read_bytes() for name, data in payload.items())
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    checksum.write_text(digest + '  ' + archive.name + '\n', encoding='ascii')
    print(json.dumps({'version': version, 'directory': str(output), 'archive': str(archive),
                      'sha256': digest, 'files': len(payload), 'installed': False}, indent=2))

if __name__ == '__main__':
    main()
