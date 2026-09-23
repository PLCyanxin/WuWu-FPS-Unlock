# Deferred update session checks

```powershell
dotnet run --project tests/Update.DeferredSessionTests -c Release
```

Exercises the production `DeferredUpdateSession` state machine with inert release metadata: one attempt per game exit, repeated notifications, cancellation/failure retention, manual retry completion, later game sessions, deliberate replacement, and session isolation.

No game, process, window, network request or updater is executed. These checks do not prove that the real process monitor emits an event at the right time or that Windows foreground activation succeeds; those require separate integration validation.
