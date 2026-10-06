using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Buddy.Windows;

// Display/reporting only: no prompt, exception stack, path, token or executable
// is retained. Unknown errors retain a useful type/code without exposing payloads.
internal sealed record RoutineLaunchReport(string Code, string Reason)
{
    internal string Detail => Code + ": " + Reason;
    internal static string Title(string alias) => "Open " + DisplayName(alias);
    private static string DisplayName(string alias) => alias switch {
        "calculator" => "Calculator", "notepad" => "Notepad", "explorer" => "File Explorer",
        "comet" => "Comet Browser", "camera" => "Camera", "spotify" => "Spotify",
        _ => throw new ArgumentException("A fixed supported app alias is required.", nameof(alias))
    };
    private static readonly IReadOnlyDictionary<string, string> KnownReasons = new Dictionary<string, string>(StringComparer.Ordinal) {
        ["Enable Agent in Settings before requesting an app launch."] = "AGENT_DISABLED",
        ["Agent was disabled; the app operation stopped."] = "AGENT_DISABLED",
        ["An app launch is already in progress; stop or wait for it first."] = "LAUNCH_BUSY",
        ["This app is in your privacy blocklist; no automatic launch or inspection is available."] = "PRIVACY_BLOCKED",
        ["Windows app publisher verification failed."] = "PUBLISHER_UNVERIFIED",
        ["The Windows app has an unexpected publisher."] = "PUBLISHER_MISMATCH",
        ["The supported Windows app was not found at its fixed installed location."] = "APP_NOT_FOUND",
        ["The app path contains a link; launch or verification refused."] = "APP_PATH_LINK",
        ["The installed app path contains a link; launch refused."] = "APP_PATH_LINK",
        ["The installed app file was not found."] = "APP_NOT_FOUND",
        ["The foreground window changed before launch; make a fresh request."] = "FOREGROUND_CHANGED",
        ["The foreground window changed; no launch ran."] = "FOREGROUND_CHANGED",
        ["No stable visible foreground window was observed; focus the intended window and retry."] = "FOREGROUND_UNAVAILABLE",
        ["No complete window identity was observed; focus the intended window and try again."] = "WINDOW_IDENTITY_UNAVAILABLE",
        ["The observed window or process changed before launch; request it again from the intended window."] = "WINDOW_IDENTITY_CHANGED",
        ["The window observation expired; make a fresh request."] = "OBSERVATION_EXPIRED",
        ["The launch checkpoint expired during app verification; make a fresh request."] = "CHECKPOINT_EXPIRED",
        ["The requested app registration is not a healthy signed package; nothing was launched."] = "PACKAGE_UNAVAILABLE",
        ["Multiple registered versions of the requested app were found; no app was chosen or launched."] = "PACKAGE_AMBIGUOUS",
        ["The requested app has no unique verified launch registration."] = "REGISTRATION_UNAVAILABLE",
        ["The signed package has no unique supported main application executable."] = "MAIN_EXECUTABLE_UNAVAILABLE",
        ["The registered app executable does not match its fixed alias."] = "EXECUTABLE_MISMATCH",
        ["The registered app is outside the verified package location."] = "PACKAGE_LOCATION_MISMATCH",
        ["The exact signed app registration is no longer available; nothing was launched."] = "REGISTRATION_CHANGED",
        ["The app registration or executable identity changed; no launch ran."] = "REGISTRATION_CHANGED",
        ["The installed app location changed; no launch ran."] = "INSTALLATION_CHANGED",
        ["More than one verified installation of this app was found. No installation was chosen or launched."] = "INSTALLATION_AMBIGUOUS",
        ["The launch was attempted but its result is uncertain; inspect the app before trying again."] = "LAUNCH_UNCERTAIN",
        ["The launch receipt did not match this request; no follow-up action ran."] = "RECEIPT_MISMATCH",
        ["The launch was attempted, but the requested app window could not be verified; no launch was repeated."] = "WINDOW_UNVERIFIED",
        ["The returned app window was stale or incomplete; no launch was repeated."] = "WINDOW_STALE",
        ["The launch was attempted but no requested foreground app window was verified; focus the app and inspect it before retrying."] = "FOREGROUND_UNVERIFIED",
        ["The launch was attempted, but the final check found no visible foreground window. No launch was repeated."] = "FOREGROUND_UNAVAILABLE",
        ["The launch was attempted, but the final foreground app did not match the request. No launch was repeated."] = "FOREGROUND_APP_MISMATCH",
        ["The launch was attempted, but the final Calculator frame could not be bound to its signed app process. No launch was repeated."] = "CALCULATOR_FRAME_UNVERIFIED",
        ["The launch was attempted, but the final app executable identity could not be verified. No launch was repeated."] = "EXECUTABLE_UNVERIFIED",
        ["The launch was attempted, but the installed app identity changed afterward. No launch was repeated."] = "INSTALLATION_CHANGED",
        ["The launch was attempted, but the final foreground process did not match the signed package's main app. No launch was repeated."] = "PACKAGE_PROCESS_MISMATCH",
        ["The launch was attempted, but the requested window changed during the final check. No launch was repeated."] = "WINDOW_IDENTITY_CHANGED",
        ["Windows denied the Calculator foreground request; no activation was repeated."] = "CALCULATOR_FOREGROUND_DENIED",
        ["Calculator activation could not be safely completed; no launch was repeated."] = "CALCULATOR_ACTIVATION_REFUSED",
        ["The Calculator activation target changed or disappeared; no replacement was selected."] = "CALCULATOR_TARGET_CHANGED",
        ["The Calculator window changed before foreground confirmation; no activation was repeated."] = "CALCULATOR_TARGET_CHANGED",
        ["Calculator did not reach a verified visible foreground state within the activation limit; no launch was repeated."] = "CALCULATOR_ACTIVATION_TIMEOUT"
    };
    internal static RoutineLaunchReport Failure(string alias, Exception error)
    {
        _ = DisplayName(alias);
        if (error is OperationCanceledException) return new("LAUNCH_STOPPED", "Stopped. An already dispatched launch cannot be undone; no automatic retry.");
        if (error is TimeoutException) return new("LAUNCH_TIMEOUT", "App launch verification timed out; the app may have opened. No automatic retry.");
        string? message = error.Message;
        if (message is { Length: <= LocalTaskJournal.MaxDetailLength } && KnownReasons.TryGetValue(message, out var code)) return new(code, message);
        string missing = "A verified " + DisplayName(alias) + " installation was not found. No other app or website was opened.";
        if (message == missing) return new("APP_NOT_FOUND", missing);
        if (error is UnauthorizedAccessException) return new("ACCESS_DENIED", "Windows denied access while checking the requested app. Nothing was retried.");
        if (error is Win32Exception native) return new("WINDOWS_ERROR_" + native.NativeErrorCode.ToString(System.Globalization.CultureInfo.InvariantCulture), "Windows could not complete the app operation. Inspect the app before retrying.");
        return new("APP_ERROR_" + unchecked((uint)error.HResult).ToString("X8"), "The app operation failed. Inspect the app before retrying; no automatic retry ran.");
    }
    internal static RoutineLaunchReport Result(string alias, ComputerUseResult result)
    {
        _ = DisplayName(alias);
        if (result.Verified && result.ActionDispatched && result.After is not null)
            return new("APP_VERIFIED", DisplayName(alias) + " was verified in a visible foreground window; no in-app action ran.");
        if (result.Message is { Length: <= LocalTaskJournal.MaxDetailLength } && KnownReasons.TryGetValue(result.Message, out var code)) return new(code, result.Message);
        return result.ActionDispatched
            ? new("LAUNCH_UNVERIFIED", "Launch was dispatched; the requested window was not verified. No automatic retry.")
            : new("NO_LAUNCH_VERIFIED", "No completed launch was verified. Review the result before retrying.");
    }
    internal static bool OpenSource(LocalTaskJournal journal, LocalTaskToken token, Action<LocalTaskRecord> show)
    {
        if (token.Source != "routine") return false;
        var record = journal.Snapshot.Tasks.FirstOrDefault(item => item.Token == token);
        if (record is null) return false;
        // The callback only receives the retained display record: never a query,
        // launch delegate, approval or replay instruction.
        show(record);
        return true;
    }
}
