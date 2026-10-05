# Local-agent protocol and owned IPC checks

The executable requires an explicit mode. Running without arguments only prints usage and exits 2. Compilation does not run any fixture.

- `--pure`: in-memory server/client serialization and strict response parsing, malformed fields, exact reply identity and Unicode, explicit listener consent, disposal before start, and a deterministic lifecycle publication interleaving. The lifecycle case injects a trusted fake task into the actual `Start`/`DisposeAsync` methods; no OS pipe is created.
- `--owned-ipc`: reserved for the root's explicitly authorized execution. Uses only random, ephemeral `Buddy.LocalAgent.<nonce>` current-user pipes and synthetic in-memory leases. No coding-product hooks, accounts, credentials, profiles, subprocess commands, UI, network or audio are touched.

Build offline using the existing cached packages, then run the self-contained Windows executable. The worker has compiled this mode but has **not executed `--owned-ipc`**.

```powershell
dotnet build tests/Buddy.LocalAgentPipe.Tests/Buddy.LocalAgentPipe.Tests.csproj -c Release -r win-x64 --self-contained true --no-restore
& ./tests/Buddy.LocalAgentPipe.Tests/bin/Release/net8.0/win-x64/Buddy.LocalAgentPipe.Tests.exe --pure
# Root only, after confirming the owned IPC scope:
& ./tests/Buddy.LocalAgentPipe.Tests/bin/Release/net8.0/win-x64/Buddy.LocalAgentPipe.Tests.exe --owned-ipc
```

The owned mode covers event/sequence/token/task checks; exact question and denial replies; single-use decisions; pre-dispatch cancellation; disconnect; malformed and oversized frames; a partial-frame timeout followed by recovery; client absent-server timeout/cancellation; malformed server replies; and disposal while reading an incomplete frame. It has a 45-second cancellation budget and 50-second outer wait. Each production exchange retains its three-second deadline; fixture waits are separately bounded and all owned pipe handles use disposal. Windows cancellation is cooperative, not a hard real-time guarantee.

These checks establish only the named local protocol behavior they actually run. They do not authenticate a coding-product identity or establish an installed external-agent integration. A canceled or failed exchange may have reached the broker before acknowledgement; do not infer absence of an effect or retry automatically. Decisions are consumed at most once, so a transport failure after consumption may lose delivery. No command execution or Allow relay exists.
