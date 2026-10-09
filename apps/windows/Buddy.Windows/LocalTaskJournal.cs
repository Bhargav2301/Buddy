using System;
using System.Collections.Generic;
using System.Linq;

namespace Buddy.Windows;

internal enum LocalTaskPhase { Running, WaitingForReview, Completed, Failed, Cancelled }

internal readonly record struct LocalTaskToken(Guid Id, string Source, long Generation);
internal sealed record LocalTaskStep(DateTimeOffset At, string Detail);
internal sealed record LocalTaskRecord(
    LocalTaskToken Token, string Title, string Detail, LocalTaskPhase Phase,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, IReadOnlyList<LocalTaskStep> ObservedSteps)
{
    internal string Source => Token.Source;
    internal bool IsTerminal => Phase is LocalTaskPhase.Completed or LocalTaskPhase.Failed or LocalTaskPhase.Cancelled;
}
internal sealed record LocalTaskSnapshot(long Revision, IReadOnlyList<LocalTaskRecord> Tasks);

/// <summary>
/// A bounded, memory-only display journal of explicit operation events. Callers supply generic
/// titles and redacted details; this type does not collect or redact prompts, answers or payloads.
/// It conveys no authority to navigate, approve, execute or cancel an operation.
/// </summary>
internal sealed class LocalTaskJournal
{
    internal const int MaxRecords = 8;
    internal const int MaxSteps = 8;
    internal const int MaxTitleLength = 80;
    internal const int MaxDetailLength = 240;
    internal const int MaxSourceLength = 32;
    private const string Replaced = "Replaced by a newer request. Any earlier dispatched effect may still need review.";
    private readonly object sync = new();
    private readonly List<LocalTaskRecord> records = [];
    private readonly Func<DateTimeOffset> clock;
    private long generation;
    private long revision;
    private DateTimeOffset lastAt = DateTimeOffset.MinValue;

    internal LocalTaskJournal(Func<DateTimeOffset>? clock = null) => this.clock = clock ?? (() => DateTimeOffset.UtcNow);

    // Newest operation first. Every returned collection is a detached, read-only snapshot.
    internal LocalTaskSnapshot Snapshot
    {
        get
        {
            lock (sync)
                return new(revision, Array.AsReadOnly(records.AsEnumerable().Reverse().ToArray()));
        }
    }

    internal LocalTaskToken Begin(string source, string title, string detail = "")
    {
        ValidateSource(source);
        title = Label(title, MaxTitleLength, nameof(title), allowEmpty: false);
        detail = Label(detail, MaxDetailLength, nameof(detail), allowEmpty: true);
        lock (sync)
        {
            var nextGeneration = checked(generation + 1);
            var nextRevision = checked(revision + 1);
            var at = Now();
            var token = new LocalTaskToken(Guid.NewGuid(), source, nextGeneration);
            for (var i = 0; i < records.Count; i++)
                if (records[i].Source == source && !records[i].IsTerminal)
                    records[i] = records[i] with { Phase = LocalTaskPhase.Cancelled, Detail = Replaced, UpdatedAt = at };

            if (records.Count == MaxRecords)
            {
                var oldestTerminal = records.FindIndex(r => r.IsTerminal);
                // Capacity expiry forgets an active display record without asserting a terminal
                // outcome or cancelling its actual work. Its old token can no longer update us.
                records.RemoveAt(oldestTerminal >= 0 ? oldestTerminal : 0);
            }
            records.Add(new(token, title, detail, LocalTaskPhase.Running, at, at,
                Array.AsReadOnly(Array.Empty<LocalTaskStep>())));
            generation = nextGeneration;
            revision = nextRevision;
            return token;
        }
    }

    internal bool Update(LocalTaskToken token, string detail,
        LocalTaskPhase phase = LocalTaskPhase.Running, bool observedStep = false)
    {
        if (phase is not (LocalTaskPhase.Running or LocalTaskPhase.WaitingForReview))
            throw new ArgumentOutOfRangeException(nameof(phase), "Update requires a nonterminal phase.");
        detail = Label(detail, MaxDetailLength, nameof(detail), allowEmpty: !observedStep);
        lock (sync)
        {
            var index = ActiveIndex(token);
            if (index < 0) return false;
            var previous = records[index];
            var append = observedStep && (previous.ObservedSteps.Count == 0 || previous.ObservedSteps[^1].Detail != detail);
            if (previous.Phase == phase && previous.Detail == detail && !append) return true;
            var nextRevision = checked(revision + 1);
            var at = Now();
            records[index] = previous with
            {
                Phase = phase, Detail = detail, UpdatedAt = at,
                ObservedSteps = append ? Append(previous.ObservedSteps, new(at, detail)) : previous.ObservedSteps
            };
            revision = nextRevision;
            return true;
        }
    }

    internal bool Finish(LocalTaskToken token, LocalTaskPhase terminalPhase, string detail, bool observedStep = false)
    {
        if (terminalPhase is not (LocalTaskPhase.Completed or LocalTaskPhase.Failed or LocalTaskPhase.Cancelled))
            throw new ArgumentOutOfRangeException(nameof(terminalPhase), "Finish requires an explicit terminal phase.");
        detail = Label(detail, MaxDetailLength, nameof(detail), allowEmpty: !observedStep);
        lock (sync)
        {
            var index = ActiveIndex(token);
            if (index < 0) return false;
            var previous = records[index];
            var append = observedStep && (previous.ObservedSteps.Count == 0 || previous.ObservedSteps[^1].Detail != detail);
            var nextRevision = checked(revision + 1);
            var at = Now();
            records[index] = previous with
            {
                Phase = terminalPhase, Detail = detail, UpdatedAt = at,
                ObservedSteps = append ? Append(previous.ObservedSteps, new(at, detail)) : previous.ObservedSteps
            };
            revision = nextRevision;
            return true;
        }
    }

    internal bool Dismiss(LocalTaskToken token)
    {
        lock (sync)
        {
            var index = records.FindIndex(r => r.Token == token);
            if (index < 0 || !records[index].IsTerminal) return false;
            var nextRevision = checked(revision + 1);
            records.RemoveAt(index);
            revision = nextRevision;
            return true;
        }
    }

    private int ActiveIndex(LocalTaskToken token) => records.FindIndex(r => r.Token == token && !r.IsTerminal);

    private DateTimeOffset Now()
    {
        var at = clock().ToUniversalTime();
        if (at > lastAt) lastAt = at;
        return lastAt;
    }

    private static IReadOnlyList<LocalTaskStep> Append(IReadOnlyList<LocalTaskStep> previous, LocalTaskStep step) =>
        Array.AsReadOnly(previous.Skip(Math.Max(0, previous.Count - MaxSteps + 1)).Append(step).ToArray());

    private static void ValidateSource(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length is < 1 or > MaxSourceLength || source[0] < 'a' || source[0] > 'z' ||
            source.Any(c => (c < 'a' || c > 'z') && (c < '0' || c > '9') && c != '-'))
            throw new ArgumentException("Source must be a short, fixed lowercase identifier.", nameof(source));
    }

    private static string Label(string value, int maximum, string parameter, bool allowEmpty)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        if (value.Length > maximum || value.Any(char.IsControl))
            throw new ArgumentException("Display text is too long or contains control characters.", parameter);
        value = value.Trim();
        if (!allowEmpty && value.Length == 0)
            throw new ArgumentException("Display text cannot be empty.", parameter);
        return value;
    }
}
