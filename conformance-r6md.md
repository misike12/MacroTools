# Macro Deck plugin conformance report

Suite version: `1.2.0`  
Plugin: `com.misu.r6md` `1.2.0`  
Conformant: **yes**  
Passed: 31 - Failed: 0 - Skipped: 18

| Id | Title | Category | Requirement | Outcome | Detail |
|---|---|---|---|---|---|
| MDC0101 | The plugin id is a valid reverse-domain package id | ManifestAndIdentifiers | Required | PASS |  |
| MDC0102 | Every declared capability's local id is a valid declared-kind identifier | ManifestAndIdentifiers | Required | PASS |  |
| MDC0103 | Every weather station instance id is a valid resource-kind identifier | ManifestAndIdentifiers | Required | SKIP | This subject does not declare the weather capability. |
| MDC0104 | The manifest declares a supported manifest version and a protocol range this suite satisfies | ManifestAndIdentifiers | Required | SKIP | This subject has no manifest - only an artifact subject does. |
| MDC0105 | The manifest's id equals the id the subject reports at /_macrodeck/info | ManifestAndIdentifiers | Required | SKIP | This subject has no manifest - only an artifact subject does. |
| MDC0106 | The manifest's name and version equal what the subject reports at /_macrodeck/info | ManifestAndIdentifiers | Required | SKIP | This subject has no manifest - only an artifact subject does. |
| MDC0107 | When the manifest declares an icon, icons/describe reports the media type its extension implies | ManifestAndIdentifiers | Required | SKIP | This subject has no manifest - only an artifact subject does. |
| MDC0201 | session.hello asserts the granted protocol version and session id, and session.welcome answers exactly once | RegistrationAndNegotiation | Required | PASS |  |
| MDC0202 | The handshake completes within ProtocolTimeouts.Handshake | RegistrationAndNegotiation | Required | PASS |  |
| MDC0203 | A host whose protocol range this subject cannot speak causes it to stop, not retry forever | RegistrationAndNegotiation | Required | PASS |  |
| MDC0204 | A self-registering subject registers once and reuses its persisted credentials on a later start | RegistrationAndNegotiation | Required | SKIP | This subject does not connect as a self-registering in-process plugin; state-directory sharing across two starts is not observable for an executable or artifact subject through this suite. |
| MDC0205 | A managed subject never calls the registration endpoint | RegistrationAndNegotiation | Required | PASS |  |
| MDC0206 | A self-registering subject with no enrollment token pairs interactively - proving possession of its own verifier rather than sending it twice - and reuses the persisted credential on a later start without pairing again | RegistrationAndNegotiation | Required | SKIP | This subject does not connect as a self-registering in-process plugin; interactive pairing is not observable for an executable or artifact subject through this suite. |
| MDC0301 | Every declared capability's kind is one of CapabilityKinds.All | CapabilitySerialization | Required | PASS |  |
| MDC0302 | Every declared capability's version range is valid and overlaps this host's supported range | CapabilitySerialization | Required | PASS |  |
| MDC0303 | A provider-shaped kind declares exactly one capability, at its documented local id | CapabilitySerialization | Required | PASS |  |
| MDC0304 | The declared capability count does not exceed MaxDeclaredCapabilities | CapabilitySerialization | Required | PASS |  |
| MDC0305 | actions/describe's reply stays within MaxMessageBytes and deserializes without loss | CapabilitySerialization | Required | PASS |  |
| MDC0306 | ui/describe reports a surface list whose every entry names a kind | CapabilitySerialization | Required | PASS |  |
| MDC0307 | Every surface ui/describe declares names a non-empty session mode | CapabilitySerialization | Required | PASS |  |
| MDC0308 | ui/describe's reply stays within MaxMessageBytes and deserializes without loss | CapabilitySerialization | Required | PASS |  |
| MDC0309 | Every state-provider action's state operation returns a well-formed snapshot | CapabilitySerialization | Required | SKIP | No declared action reports ProvidesState. |
| MDC0310 | Every state a state-provider action returns has an id that is a valid declared-kind identifier | CapabilitySerialization | Required | SKIP | No declared action reports ProvidesState. |
| MDC0311 | A variable provider reporting a catalog answers discover with a bounded, well-formed page | CapabilitySerialization | Required | SKIP | This subject's variable provider reports no catalog. |
| MDC0312 | Every icon-provider action's icon snapshot is internally consistent | CapabilitySerialization | Required | SKIP | No declared action reports ProvidesIcon. |
| MDC0313 | A snapshot naming no reference is answerable by icon.content, with AssetTooLarge the only allowed failure | CapabilitySerialization | Required | SKIP | No declared action reports ProvidesIcon. |
| MDC0314 | A variable that declares a write capability answers set with something other than NotWritable or NotFound | CapabilitySerialization | Required | SKIP | This subject declares no writable variable. |
| MDC0315 | The eager variable list stays within VariableLimits.MaxEagerVariablesPerProvider | CapabilitySerialization | Required | PASS |  |
| MDC0401 | No two declared capabilities share the same (kind, localId) pair | DuplicateIds | Required | PASS |  |
| MDC0402 | No two weather station instances share an instance id | DuplicateIds | Required | SKIP | This subject does not declare the weather capability. |
| MDC0403 | No two eager variables resolve to the same id | DuplicateIds | Required | PASS |  |
| MDC0501 | An invocation receives exactly one reply, never more | TimeoutAndCancellation | Required | PASS |  |
| MDC0502 | A deadline that elapses produces TIMEOUT, and nothing arrives afterward | TimeoutAndCancellation | Recommended | SKIP | No declared action ran long enough, under a 300 ms deadline, to observe deadline enforcement. |
| MDC0503 | Cancelling an unknown or already-answered correlation produces no message at all | TimeoutAndCancellation | Required | PASS |  |
| MDC0504 | Cancelling an in-flight invocation produces exactly one cancelled reply | TimeoutAndCancellation | Recommended | SKIP | No declared action stayed in flight long enough to be cancelled before it completed on its own. |
| MDC0505 | A burst beyond MaxConcurrentInvocations never exceeds the reported in-flight bound, and every invocation completes | TimeoutAndCancellation | Recommended | PASS |  |
| MDC0601 | After a non-fatal disconnect, the subject reconnects and becomes ready again | DisconnectAndReconnect | Required | PASS |  |
| MDC0602 | Reconnecting inside the resume window presents resumeSessionId and resumes with the same session id | DisconnectAndReconnect | Required | PASS |  |
| MDC0603 | A reconnect outside the resume window opens a fresh session and the subject becomes ready again after re-initializing | DisconnectAndReconnect | Required | PASS |  |
| MDC0604 | A close of SupervisorShutdown (4004) stops a managed subject but leaves a self-registering one running | DisconnectAndReconnect | Required | PASS |  |
| MDC0701 | /_macrodeck/health answers before any session exists; /_macrodeck/ready does not until one does | HealthEndpoint | Required | PASS |  |
| MDC0702 | /_macrodeck/info and /_macrodeck/diagnostics agree with what the host itself observed about this session | HealthEndpoint | Required | PASS |  |
| MDC0703 | An unmapped route under /_macrodeck/ answers 404 | HealthEndpoint | Required | PASS |  |
| MDC0704 | The subject serves its endpoints at the base address its launcher was told to expect | HealthEndpoint | Required | PASS |  |
| MDC0801 | Logging while draining is paused does not block, and queued traffic is not silently lost after resuming | BoundedQueues | Recommended | SKIP | No declared action produced any observable log output. |
| MDC0802 | Under a logging flood while paused, a trailing Error still survives and Dropped is reported honestly | BoundedQueues | Recommended | SKIP | No declared action produced a batch's worth of log traffic while draining was paused, so there was no flood for a trailing Error to have to survive. |
| MDC0803 | Every collected log event respects the protocol's structural field limits | BoundedQueues | Required | PASS |  |
| MDC0804 | Reconnecting does not replay a burst of previously published events | BoundedQueues | Required | PASS |  |
| MDC0805 | A burst of variables/get invocations beyond MaxConcurrentInvocations never exceeds the reported in-flight bound | BoundedQueues | Recommended | PASS |  |
