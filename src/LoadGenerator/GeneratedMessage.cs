namespace LoadGenerator;

/// <summary>
/// A message ready to publish. <see cref="Label"/> says what it is (for the operator and for the
/// <c>label</c> application property). <see cref="ExpectedDeadLetterReason"/> is the dead-letter reason the
/// handler should give it, or null when it is a well-formed load message that should be routed normally.
/// </summary>
public sealed record GeneratedMessage(string Body, string Label, string? ExpectedDeadLetterReason = null);
