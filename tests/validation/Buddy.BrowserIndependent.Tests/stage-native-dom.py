"""Stage owned HTML/JS only. This script never starts a browser or a server."""
import argparse
import hashlib
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--driver', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    here = Path(__file__).resolve().parent
    driver = args.driver.resolve(strict=True)
    output = args.output.resolve()
    if not driver.is_file() or driver.name != 'dom-driver.js':
        raise ValueError('Select the exact production dom-driver.js.')
    if output.exists():
        raise ValueError('A fresh owned output directory is required; no overwrite.')
    inputs = {
        'dom-driver.js': driver.read_bytes(),
        'native-dom-fixture.html': (here / 'native-dom-fixture.html').read_bytes(),
        'native-dom-fixture.js': (here / 'native-dom-fixture.js').read_bytes(),
    }
    digest = hashlib.sha256(inputs['dom-driver.js']).hexdigest()
    inputs['native-dom-fixture.html'] = inputs['native-dom-fixture.html'].replace(b'DRIVER_SHA256_TO_BE_STAGED', digest.encode('ascii'))
    if driver.read_bytes() != inputs['dom-driver.js']:
        raise ValueError('Driver changed during staging.')
    output.mkdir(parents=True, exist_ok=False)
    for name, content in inputs.items():
        with (output / name).open('xb') as target:
            target.write(content)
    receipt = {'schema': 1, 'driver': str(driver), 'driverSha256': digest,
               'files': {name: hashlib.sha256(content).hexdigest() for name, content in inputs.items()},
               'browserStarted': False, 'network': False, 'ui': False,
               'scope': 'File staging only; root separately reviews and owns any headless browser run.'}
    with (output / 'stage-receipt.json').open('x', encoding='utf-8') as target:
        json.dump(receipt, target, indent=2)
        target.write('\n')
    print(json.dumps(receipt, indent=2))


if __name__ == '__main__':
    main()
