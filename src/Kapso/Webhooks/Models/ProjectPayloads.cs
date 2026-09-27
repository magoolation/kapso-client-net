using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kapso.Webhooks.Models;

/// <summary>
/// Body of the <c>whatsapp.phone_number.*</c> events.
/// </summary>
/// <remarks>
/// <c>created</c> and <c>deleted</c> carry only the number, project and customer.
/// <c>offboarded</c>, <c>disconnected</c> and <c>reconnected</c> add the event id,
/// timestamp, connection type and Meta source, and are available only on
/// project webhooks using payload version v2.
/// </remarks>
public sealed record KapsoPhoneNumberPayload : KapsoWebhookPayload
{
    /// <summary>Stable event id, unique per number and underlying Meta event.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The event name, repeated in the body on the v2 events.</summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    /// <summary>When it happened.</summary>
    [JsonPropertyName("occurred_at")]
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>The affected number.</summary>
    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; init; }

    /// <summary>The project it belongs to.</summary>
    [JsonPropertyName("project")]
    public KapsoWebhookProjectRef? Project { get; init; }

    /// <summary>The owning customer. Absent when the number is not a customer's.</summary>
    [JsonPropertyName("customer")]
    public KapsoWebhookCustomerRef? Customer { get; init; }

    /// <summary><c>dedicated</c> or <c>coexistence</c>.</summary>
    [JsonPropertyName("connection_type")]
    public string? ConnectionType { get; init; }

    /// <summary>The Meta event behind this notification.</summary>
    [JsonPropertyName("source")]
    public KapsoWebhookSource? Source { get; init; }

    /// <summary>Why the number disconnected. Only on <c>whatsapp.phone_number.disconnected</c>.</summary>
    [JsonPropertyName("disconnection")]
    public KapsoDisconnection? Disconnection { get; init; }
}

/// <summary>
/// Body of the <c>whatsapp.account.*</c> enforcement events.
/// </summary>
/// <remarks>
/// Meta reports enforcement against the business account, not a single number, so
/// one delivery arrives per project and account with every affected number listed
/// in <see cref="PhoneNumbers"/>. Keys with no value are omitted.
/// </remarks>
public sealed record KapsoAccountPayload : KapsoWebhookPayload
{
    /// <summary>Stable event id, unique per project, account and Meta event.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The event name, repeated in the body.</summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    /// <summary>When it happened.</summary>
    [JsonPropertyName("occurred_at")]
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>The WhatsApp Business Account.</summary>
    [JsonPropertyName("business_account_id")]
    public string? BusinessAccountId { get; init; }

    /// <summary>The project.</summary>
    [JsonPropertyName("project")]
    public KapsoWebhookProjectRef? Project { get; init; }

    /// <summary>Every number in the project on that account.</summary>
    [JsonPropertyName("phone_numbers")]
    public IReadOnlyList<KapsoAffectedPhoneNumber>? PhoneNumbers { get; init; }

    /// <summary>Restrictions applied. Only on <c>whatsapp.account.restricted</c>.</summary>
    [JsonPropertyName("restrictions")]
    public IReadOnlyList<KapsoAccountRestriction>? Restrictions { get; init; }

    /// <summary>The violation. Only on <c>whatsapp.account.violation</c>.</summary>
    [JsonPropertyName("violation")]
    public KapsoAccountViolation? Violation { get; init; }

    /// <summary>Ban state. On <c>whatsapp.account.disabled</c> and <c>.reinstated</c>.</summary>
    [JsonPropertyName("ban")]
    public KapsoAccountBan? Ban { get; init; }

    /// <summary>The Meta event behind this notification.</summary>
    [JsonPropertyName("source")]
    public KapsoWebhookSource? Source { get; init; }
}

/// <summary>Body of <c>workflow.execution.handoff</c> and <c>.failed</c>.</summary>
public sealed record KapsoWorkflowExecutionPayload : KapsoWebhookPayload
{
    /// <summary>The event name, repeated in the body.</summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    /// <summary>When it happened.</summary>
    [JsonPropertyName("occurred_at")]
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>The project.</summary>
    [JsonPropertyName("project_id")]
    public string? ProjectId { get; init; }

    /// <summary>The workflow.</summary>
    [JsonPropertyName("workflow_id")]
    public string? WorkflowId { get; init; }

    /// <summary>The execution.</summary>
    [JsonPropertyName("workflow_execution_id")]
    public string? WorkflowExecutionId { get; init; }

    /// <summary><c>handoff</c> or <c>failed</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>Caller-supplied tracking id, when the run was started with one.</summary>
    [JsonPropertyName("tracking_id")]
    public string? TrackingId { get; init; }

    /// <summary>Channel the execution ran on, for example <c>whatsapp</c>.</summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; init; }

    /// <summary>The conversation, for WhatsApp executions.</summary>
    [JsonPropertyName("whatsapp_conversation_id")]
    public string? WhatsAppConversationId { get; init; }

    /// <summary>Handoff details. Only on <c>workflow.execution.handoff</c>.</summary>
    [JsonPropertyName("handoff")]
    public KapsoWorkflowHandoff? Handoff { get; init; }

    /// <summary>Failure details. Only on <c>workflow.execution.failed</c>.</summary>
    [JsonPropertyName("error")]
    public KapsoWorkflowError? Error { get; init; }
}

/// <summary>Body of <c>project.event</c>, a custom event emitted by the project.</summary>
public sealed record KapsoProjectEventPayload : KapsoWebhookPayload
{
    /// <summary>Event id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Always <c>project.event</c>.</summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    /// <summary>The custom event's own name, such as <c>lead.qualified</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>When it was emitted.</summary>
    [JsonPropertyName("occurred_at")]
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>The project.</summary>
    [JsonPropertyName("project_id")]
    public string? ProjectId { get; init; }

    /// <summary>Set only when the event is tied to a WhatsApp conversation.</summary>
    [JsonPropertyName("conversation_id")]
    public string? ConversationId { get; init; }

    /// <summary>Whatever the emitter attached. Shape is the project's own.</summary>
    [JsonPropertyName("properties")]
    public JsonElement? Properties { get; init; }
}

/// <summary>
/// Body of the <c>kapso_agent.run.*</c> events.
/// </summary>
/// <remarks>Only runs started through the Agent API emit these.</remarks>
public sealed record KapsoAgentRunPayload : KapsoWebhookPayload
{
    /// <summary>Event id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The event name, repeated in the body.</summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    /// <summary>When the event was raised.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>The run. Null fields are omitted by the server.</summary>
    [JsonPropertyName("data")]
    public KapsoAgentRun? Data { get; init; }
}

/// <summary>An agent run, under <c>data</c>.</summary>
public sealed record KapsoAgentRun
{
    /// <summary>The run.</summary>
    [JsonPropertyName("run_id")]
    public string? RunId { get; init; }

    /// <summary>The session it belongs to.</summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    /// <summary><c>completed</c>, <c>failed</c>, <c>cancelled</c> or awaiting approval.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>Which agent ran.</summary>
    [JsonPropertyName("agent")]
    public JsonElement? Agent { get; init; }

    /// <summary>What it produced, on a completed run.</summary>
    [JsonPropertyName("result")]
    public JsonElement? Result { get; init; }

    /// <summary>The tool awaiting approval, on <c>kapso_agent.run.approval_required</c>.</summary>
    [JsonPropertyName("pending_approval")]
    public JsonElement? PendingApproval { get; init; }

    /// <summary>Failure details, with code <c>run_failed</c> or <c>run_cancelled</c>.</summary>
    [JsonPropertyName("error")]
    public KapsoAgentRunError? Error { get; init; }

    /// <summary>Caller-supplied metadata.</summary>
    [JsonPropertyName("metadata")]
    public JsonElement? Metadata { get; init; }

    /// <summary>When the run was created.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>When it started.</summary>
    [JsonPropertyName("started_at")]
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When it ended.</summary>
    [JsonPropertyName("finished_at")]
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Relative path to poll for status.</summary>
    [JsonPropertyName("status_url")]
    public string? StatusUrl { get; init; }
}

/// <summary>Why an agent run failed or was cancelled.</summary>
public sealed record KapsoAgentRunError
{
    /// <summary><c>run_failed</c> or <c>run_cancelled</c>.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /// <summary>Human-readable explanation.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>A reference to a project.</summary>
public sealed record KapsoWebhookProjectRef
{
    /// <summary>Project ID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

/// <summary>A reference to a customer.</summary>
public sealed record KapsoWebhookCustomerRef
{
    /// <summary>Kapso customer ID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Your own identifier for the customer.</summary>
    [JsonPropertyName("external_id")]
    public string? ExternalId { get; init; }
}

/// <summary>The upstream event behind a notification.</summary>
public sealed record KapsoWebhookSource
{
    /// <summary>Always <c>meta</c> today.</summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    /// <summary>Meta's raw event name, such as <c>ACCOUNT_OFFBOARDED</c>.</summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    /// <summary>The business account, when Meta names one.</summary>
    [JsonPropertyName("business_account_id")]
    public string? BusinessAccountId { get; init; }
}

/// <summary>Why a number disconnected. Values are lowercased Meta values, and may be null.</summary>
public sealed record KapsoDisconnection
{
    /// <summary>Meta's reason.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>Who initiated it.</summary>
    [JsonPropertyName("initiated_by")]
    public string? InitiatedBy { get; init; }
}

/// <summary>A number affected by an account-level enforcement event.</summary>
public sealed record KapsoAffectedPhoneNumber
{
    /// <summary>Phone number ID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The number as displayed.</summary>
    [JsonPropertyName("display_phone_number")]
    public string? DisplayPhoneNumber { get; init; }
}

/// <summary>A restriction Meta placed on the account.</summary>
public sealed record KapsoAccountRestriction
{
    /// <summary>Meta's raw restriction type.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>When it lifts. Omitted when the restriction has no expiry.</summary>
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>A policy violation Meta reported.</summary>
public sealed record KapsoAccountViolation
{
    /// <summary>Meta's raw violation type.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>What to do about it, when Meta says.</summary>
    [JsonPropertyName("remediation")]
    public string? Remediation { get; init; }
}

/// <summary>Ban state from Meta's <c>DISABLED_UPDATE</c>.</summary>
public sealed record KapsoAccountBan
{
    /// <summary><c>DISABLE</c> or <c>REINSTATE</c>.</summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }

    /// <summary>A date Meta sends already localized and formatted.</summary>
    [JsonPropertyName("date_label")]
    public string? DateLabel { get; init; }
}

/// <summary>Why a workflow handed off to a human.</summary>
public sealed record KapsoWorkflowHandoff
{
    /// <summary>Reason given at handoff, when there was one.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary><c>agent_tool</c> or <c>action_step</c>.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

/// <summary>Why a workflow execution failed.</summary>
public sealed record KapsoWorkflowError
{
    /// <summary>The failure.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
