# Explicit local refinement resources

The source-linked console fixture exercises the production reader without WPF, a model, network calls, user files, or installed settings:

```powershell
dotnet run --project tests/Buddy.RefinementResources.Tests -c Release -r win-x64 --self-contained true
```

`RefinementResourceReader.ReadAsync(selectedPath, cancellationToken)` returns `RefinementResourceReadResult(Source, ByteCount, Sha256)`. The caller obtains an explicit file selection and then previews the immutable result before separate Add consent. The helper itself has no picker, consent state, attachment, model, retrieval or storage behavior.

Only an absolute local drive path to a regular UTF-8 `.txt`/`.md` file is accepted. Both 64 KiB raw bytes and 20,000 UTF-16 text units are hard limits; oversized data is refused, never excerpted. An optional initial UTF-8 BOM is an encoding marker, with remaining whitespace/line endings/text unchanged. Empty/whitespace-only text, NUL content, invalid UTF-8 and misleading/oversized filenames are refused. Byte count and SHA256 cover raw bytes including any BOM.

The source contains an opaque ID that stays stable for that returned selection, filename-only title, document provenance, reference disposition, Required=false and Url=null. No absolute path is returned. A second selection gets a new ID. Later changes to the original file do not change the reviewed snapshot; no reread or background watch occurs. Resource contents remain untrusted, and existing server context policy may separately omit unverified links or present marked excerpts.

Windows disk handles are opened without following the final reparse point. Every ancestor is checked and held without write/delete sharing until the read ends, protecting the check/open interval against normal replacement or reparse mutation. Final resolved paths and volume identities are checked. UNC/device/relative/URI/alternate-stream paths, network drive types, reparse points, hard links and resolving aliases are refused. Unavailable, concurrently writable, redirected or unsupported filesystems may be conservatively refused. Synchronous local metadata calls run off the UI thread; cancellation is checked between calls and supplied to content reads, but cannot forcibly interrupt an individual Win32 metadata call already in progress.

Tests create only a dedicated temporary directory and owned files, hard links, a junction and (when Windows grants creation rights) symbolic links. A symbolic-link creation privilege limitation is reported as SKIP, never as a passing check; the junction/hard-link checks remain mandatory. Cleanup removes owned links themselves before recursive removal of the verified temporary root. No native UI/audio fixture runs. Production UI consent/revision binding and integrated desktop builds belong to the UI/integration owners.
