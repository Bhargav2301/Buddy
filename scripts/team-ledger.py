#!/usr/bin/env python3
"""Read-only coordination gates. Never executes handoff commands or integrates files."""
import argparse
import fnmatch
import hashlib
import json
import re
import subprocess
from pathlib import Path, PurePosixPath

STATES = {'queued', 'running', 'review', 'accepted', 'validated', 'blocked', 'cancelled'}
READY = {'accepted', 'validated'}
BUILD_ROOTS = ('apps/', 'services/', 'tests/', 'scripts/', '.github/')

def validate(ledger):
    if ledger.get('schema') != 1:
        raise ValueError('Unsupported task ledger schema.')
    if ledger.get('executionMode', 'active') not in {'active', 'paused'}:
        raise ValueError('Invalid team execution mode.')
    tasks = ledger.get('tasks', [])
    by_id = {}
    for task in tasks:
        key = task['id']
        if key in by_id or not re.fullmatch(r'[A-Z]+-[0-9]+', key):
            raise ValueError('Duplicate or invalid stable task ID.')
        if task.get('status') not in STATES or not isinstance(task.get('attempt'), int) or task['attempt'] < 1:
            raise ValueError('Invalid task status or attempt.')
        if not re.fullmatch(r'[a-f0-9]{40}', task.get('baseRevision', '')):
            raise ValueError('A full base revision is required.')
        if not task.get('owner') or not task.get('writeScope') or not task.get('acceptance') or not task.get('deliverable'):
            raise ValueError('Task ownership, scope, acceptance and deliverable must be explicit.')
        by_id[key] = task
    visited, active = set(), set()
    def visit(key):
        if key in active:
            raise ValueError('Dependency cycle: ' + key)
        if key in visited:
            return
        active.add(key)
        for dep, attempt in by_id[key].get('dependencies', {}).items():
            if dep not in by_id or attempt != by_id[dep]['attempt']:
                raise ValueError('Missing or stale dependency: ' + dep)
            visit(dep)
        active.remove(key)
        visited.add(key)
    for key in by_id:
        visit(key)
    return by_id

def check_checkout(task, worker_root, integration_root):
    expected = Path(integration_root).resolve().parent/'workers'/task['worktree']
    if Path(worker_root).resolve() != expected.resolve():
        raise ValueError('Worker root does not match the assigned isolated checkout.')
    actual = subprocess.check_output(['git', '-c', 'safe.directory='+expected.as_posix(), '-C', str(expected), 'rev-parse', 'HEAD']).decode().strip()
    if actual != task['baseRevision']:
        raise ValueError('Actual worker HEAD differs from the assigned base revision.')

def build_snapshot(root):
    rows = snapshot(root)['files']
    # Include embedded JSON, OCR data and future assets, not just familiar code extensions.
    build_rows = [row for row in rows if row['path'].startswith(BUILD_ROOTS) or
                  row['path'].startswith('Directory.Build.') or row['path'] in {'VERSION', 'global.json', 'NuGet.Config'}]
    return hashlib.sha256(json.dumps(build_rows, separators=(',', ':'), sort_keys=True).encode()).hexdigest()

def verify_handoff(ledger, handoff, worker_root, integration_root=None):
    tasks = validate(ledger)
    if ledger.get('executionMode', 'active') == 'paused':
        raise ValueError('Team is paused; reconcile live workers and source before resuming intake.')
    task = tasks.get(handoff.get('task'))
    if task is None:
        raise ValueError('Unknown task handoff.')
    for field in ('attempt', 'baseRevision', 'owner'):
        if handoff.get(field) != task[field]:
            raise ValueError('Stale or wrong-owner handoff: ' + field)
    if task['status'] not in {'running', 'review'}:
        raise ValueError('Task is not accepting worker output.')
    check_checkout(task, worker_root, integration_root or Path(__file__).resolve().parents[1])
    if handoff.get('dependencies', {}) != task.get('dependencies', {}):
        raise ValueError('Handoff dependency generations differ.')
    for dependency in task.get('dependencies', {}):
        if tasks[dependency]['status'] not in READY:
            raise ValueError('Dependency is not accepted: ' + dependency)
        digest = tasks[dependency].get('acceptedSnapshot')
        if not digest or handoff.get('dependencySnapshots', {}).get(dependency) != digest:
            raise ValueError('Accepted dependency content changed or is not identified: ' + dependency)
    if not handoff.get('summary') or not isinstance(handoff.get('limitations'), list):
        raise ValueError('Handoff needs behavior and remaining limitations.')
    rows = handoff.get('files', [])
    if not rows:
        raise ValueError('Handoff contains no files.')
    root = Path(worker_root).resolve()
    seen = set()
    for row in rows:
        name = row['path']
        relative = PurePosixPath(name)
        if relative.is_absolute() or '\\' in name or ':' in name or '..' in relative.parts or name in seen:
            raise ValueError('Unsafe or duplicate handoff path.')
        if not any(fnmatch.fnmatchcase(name, pattern) for pattern in task['writeScope']):
            raise ValueError('File is outside assigned ownership: ' + name)
        path = root.joinpath(*relative.parts).resolve()
        if not path.is_relative_to(root) or not path.is_file():
            raise ValueError('File escapes worker checkout or is missing.')
        if hashlib.sha256(path.read_bytes()).hexdigest() != row.get('sha256', '').lower():
            raise ValueError('Worker file changed after handoff: ' + name)
        seen.add(name)
    for result in handoff.get('tests', []):
        if result.get('status') not in {'passed', 'failed', 'blocked', 'not_run'}:
            raise ValueError('Evidence status is missing.')
        if not result.get('command') or not result.get('environment') or not result.get('snapshot'):
            raise ValueError('Evidence needs command, environment and snapshot.')
        if result['status'] in {'passed', 'failed'} and (not result.get('log') or not isinstance(result.get('exitCode'), int)):
            raise ValueError('Executed evidence needs exit code and log.')
        if result['status'] == 'passed' and result['exitCode'] != 0:
            raise ValueError('A failed command cannot be labelled passed.')
        if result['status'] == 'passed' and result['snapshot'] != build_snapshot(root):
            raise ValueError('Passed evidence does not match the current worker build inputs.')
    digest = hashlib.sha256(json.dumps(sorted(rows, key=lambda row: row['path']), sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    return {'result': 'verified_for_review', 'task': task['id'], 'attempt': task['attempt'], 'files': len(rows),
            'handoffSnapshot': digest, 'integrated': False, 'authorizationGranted': False}

def snapshot(root):
    root = Path(root).resolve()
    command = ['git', '-c', 'safe.directory=' + root.as_posix(), '-C', str(root)]
    names = subprocess.check_output(command + ['ls-files', '--cached', '--others', '--exclude-standard', '-z']).decode().split('\0')
    rows = []
    for name in sorted(set(names)):
        if name and (root/name).is_file():
            rows.append({'path': name, 'sha256': hashlib.sha256((root/name).read_bytes()).hexdigest()})
    encoded = json.dumps(rows, separators=(',', ':'), ensure_ascii=True).encode()
    return {'revision': subprocess.check_output(command + ['rev-parse', 'HEAD']).decode().strip(),
            'dirty': bool(subprocess.check_output(command + ['status', '--porcelain'])),
            'snapshot': hashlib.sha256(encoded).hexdigest(), 'files': rows}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['validate', 'status', 'snapshot', 'build-snapshot', 'verify-handoff'])
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--ledger', type=Path)
    parser.add_argument('--handoff', type=Path)
    parser.add_argument('--worker-root', type=Path)
    args = parser.parse_args()
    try:
        if args.action == 'build-snapshot':
            result = {'buildSnapshot': build_snapshot(args.root)}
        elif args.action == 'snapshot':
            result = snapshot(args.root)
        else:
            ledger = json.loads((args.ledger or args.root/'docs/development/tasks.json').read_text(encoding='utf-8'))
            tasks = validate(ledger)
            if args.action == 'verify-handoff':
                if not args.handoff or not args.worker_root:
                    raise ValueError('Provide handoff JSON and the assigned worker root.')
                result = verify_handoff(ledger, json.loads(args.handoff.read_text(encoding='utf-8')), args.worker_root, args.root)
            elif args.action == 'status':
                result = [{k: task[k] for k in ('id', 'attempt', 'owner', 'status')} for task in tasks.values()]
            else:
                result = {'result': 'passed', 'tasks': len(tasks), 'acyclicDependencies': True}
        print(json.dumps(result, indent=2))
    except (ValueError, KeyError, OSError) as error:
        parser.exit(1, 'BLOCKED: ' + str(error) + '\n')

if __name__ == '__main__':
    main()
