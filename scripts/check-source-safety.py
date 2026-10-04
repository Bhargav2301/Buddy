#!/usr/bin/env python3
"""Reject private/runtime payloads and recognizable secrets without printing their values."""
import argparse, hashlib, re, subprocess, sys
from pathlib import Path
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--staged',action='store_true');args=parser.parse_args()
git=['git','-c','safe.directory='+root.as_posix(),'-C',str(root)]
names=subprocess.check_output(git+['ls-files','-z']).decode().split('\0')
problems=[];checked=0;assets=[]
blocked=re.compile(r'(^|/)(preview-data|local-voice|SpeechModels|keys|logs|node_modules|bin|obj|baseline)(/|$)|\.(encrypted|pfx|p12|keystore|jks|onnx|gguf|safetensors|zip|exe|dll|wav|mp3|dmp)$|(^|/)ggml-[^/]+\.bin$|(^|/)(desktop\.json|\.buddy-rollback\.json|\.env)$',re.I)
secrets=[re.compile(rb'gh[opusr]_[A-Za-z0-9]{25,}'),re.compile(rb'github_pat_[A-Za-z0-9_]{40,}'),re.compile(rb'AKIA[0-9A-Z]{16}'),re.compile(rb'sk-[A-Za-z0-9_-]{30,}'),re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----')]
for name in names:
    if not name:continue
    if blocked.search(name):problems.append((name,'private/runtime/archive file'));continue
    data=subprocess.check_output(git+['show',':'+name]) if args.staged else (root/name).read_bytes();checked+=1
    if len(data)>5*1024*1024:problems.append((name,'unexpected file above 5 MiB'))
    if any(pattern.search(data) for pattern in secrets):problems.append((name,'credential-shaped literal; inspect privately'))
    if name.startswith('apps/windows/Buddy.Windows/Assets/Branding/') and name.endswith(('.png','.ico')):assets.append((name,hashlib.sha256(data).hexdigest()))
if not (root/'apps/windows/Buddy.Windows/Assets/Branding/README.md').is_file():problems.append(('branding','missing provenance notice'))
for name,reason in problems:print(f'FAIL: {name}: {reason}')
print(f'{checked} source files checked; {len(assets)} owner-supplied branding assets documented; no file contents printed.')
if problems:sys.exit(1)
print('PASS: source payload and recognizable-secret scan. This is a bounded scan, not proof that arbitrary secrets cannot exist.')
