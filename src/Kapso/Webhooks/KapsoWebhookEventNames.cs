namespace Kapso.Webhooks;

/// <summary>
/// The event names Kapso sends in the <c>X-Webhook-Event</c> header.
/// </summary>
/// <remarks>
/// Kapso adds events over time. An unrecognised name is not an error: it is read
/// as <see cref="Models.KapsoUnknownWebhookPayload"/>, keeping the raw JSON, so an
/// endpoint written today keeps working when Kapso ships something new.
/// </remarks>
public static class KapsoWebhookEventNames
{
    /// <summary>Events delivered to a phone number's webhook.</summary>
    public static class WhatsApp
    {
        /// <summary>A customer sent a message. Supports batching.</summary>
        public const string MessageReceived = "whatsapp.message.received";

        /// <summary>A message was accepted by WhatsApp.</summary>
        public const string MessageSent = "whatsapp.message.sent";

        /// <summary>A message reached the recipient's device.</summary>
        public const string MessageDelivered = "whatsapp.message.delivered";

        /// <summary>The recipient read a message.</summary>
        public const string MessageRead = "whatsapp.message.read";

        /// <summary>A message could not be delivered.</summary>
        public const string MessageFailed = "whatsapp.message.failed";

        /// <summary>A conversation started.</summary>
        public const string ConversationCreated = "whatsapp.conversation.created";

        /// <summary>A conversation ended, by agent action, closure or the 24 hour window.</summary>
        public const string ConversationEnded = "whatsapp.conversation.ended";

        /// <summary>No messages for the configured number of minutes.</summary>
        public const string ConversationInactive = "whatsapp.conversation.inactive";

        /// <summary>A contact was issued a new business-scoped user ID.</summary>
        public const string ContactIdentityChanged = "whatsapp.contact.identity_changed";

        /// <summary>A contact stopped or resumed marketing messages.</summary>
        public const string ContactMarketingPreferenceChanged = "whatsapp.contact.marketing_preference_changed";
    }

    /// <summary>Events delivered to a project webhook.</summary>
    public static class Project
    {
        /// <summary>A customer connected WhatsApp through a setup link.</summary>
        public const string PhoneNumberCreated = "whatsapp.phone_number.created";

        /// <summary>A phone number was removed from the project.</summary>
        public const string PhoneNumberDeleted = "whatsapp.phone_number.deleted";

        /// <summary>Meta offboarded a phone number from the Cloud API.</summary>
        public const string PhoneNumberOffboarded = "whatsapp.phone_number.offboarded";

        /// <summary>Partner access was removed or the app uninstalled.</summary>
        public const string PhoneNumberDisconnected = "whatsapp.phone_number.disconnected";

        /// <summary>The Cloud API connection was restored.</summary>
        public const string PhoneNumberReconnected = "whatsapp.phone_number.reconnected";

        /// <summary>Meta disabled the WhatsApp Business Account.</summary>
        public const string AccountDisabled = "whatsapp.account.disabled";

        /// <summary>Meta restricted capabilities on the account.</summary>
        public const string AccountRestricted = "whatsapp.account.restricted";

        /// <summary>Meta reinstated a previously disabled account.</summary>
        public const string AccountReinstated = "whatsapp.account.reinstated";

        /// <summary>Meta reported a policy violation on the account.</summary>
        public const string AccountViolation = "whatsapp.account.violation";

        /// <summary>A workflow handed off to a human agent.</summary>
        public const string WorkflowExecutionHandoff = "workflow.execution.handoff";

        /// <summary>A workflow execution failed.</summary>
        public const string WorkflowExecutionFailed = "workflow.execution.failed";

        /// <summary>A custom project event was emitted.</summary>
        public const string ProjectEvent = "project.event";

        /// <summary>An agent run needs a tool approval.</summary>
        public const string AgentRunApprovalRequired = "kapso_agent.run.approval_required";

        /// <summary>An agent run completed.</summary>
        public const string AgentRunCompleted = "kapso_agent.run.completed";

        /// <summary>An agent run failed.</summary>
        public const string AgentRunFailed = "kapso_agent.run.failed";

        /// <summary>An agent run was cancelled.</summary>
        public const string AgentRunCancelled = "kapso_agent.run.cancelled";
    }
}
