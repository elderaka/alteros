# M3 activity Scenario fixtures

These three definitions are isolated Testbox fixtures, not published dialogue
catalogue content. Their `topicId` (`m3_testbox`) is a test-only host context and
must not be added to production Topic catalogues. A developer fixture launcher
must create a fresh run ID per execution and provide only the selected activity
capability plus `m3.activity_terminal` in the runtime snapshot.

The definitions use the current `ScenarioSession` schema and existing M3
capabilities. Come here and Drink water close their opening exchange before the
activity wait. Completion and failure dialogue steps have `closeExchange=false`
so the presentation layer can make them available on the next E interaction
without reopening the camera automatically. Follow remains active through its
wait step until Stop or a terminal failure; E exchange ownership is handled by
the activity host.

These files do not create NavMesh, furniture bindings, capabilities, hydration
state, or scene wiring. They are validated against a synthetic host snapshot by
focused Editor tests. Real ModelTest acceptance still requires Unity wiring and
Play Mode evidence.
