namespace Codai.Core.Tools;

/// <summary>What the user decided when asked to allow a state-changing tool call.</summary>
public enum ApprovalDecision
{
    /// <summary>The call must not run.</summary>
    Deny,

    /// <summary>The call may run once.</summary>
    AllowOnce,

    /// <summary>The call may run, and later calls to the same tool need not ask again this session.</summary>
    AllowAlways,
}
