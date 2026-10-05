namespace Buddy.Server;

public sealed record ConnectorSetupStatus(string Provider, string State, string Scope, string RequiredSetup,
    bool CanRead, bool CanGrant, string NextStep);

/// <summary>Truthful setup inventory; constructing it never touches keys, account tokens or the network.</summary>
public static class ConnectorSetup
{
    public static IReadOnlyList<ConnectorSetupStatus> Statuses => Array.AsReadOnly(ConnectorCatalog.Capabilities.Select(x =>
        new ConnectorSetupStatus(x.Provider, "Registration and consent required", x.Scope, x.Setup, false, false,
            x.Provider == "gmail" ? "Register a Desktop OAuth client, then approve this exact metadata scope in a separately enabled connection flow."
            : "Register an approved OAuth broker and connection, then approve explicitly shared pages in a separately enabled connection flow.")).ToArray());
}
