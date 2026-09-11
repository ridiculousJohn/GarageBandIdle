# Slice D compliance status

The design defines the requirements. This checklist describes implementation and verification status.

| Area | Requirement | Implementation |
| --- | --- | --- |
| Chapter eligibility | Roadies are assignable only to unlocked chapters, including cleared chapters. | ChapterDefinition.unlock uses the Condition family. Chapter 1 defaults to Always; other chapters can require root completion flags. Import validates the condition. Switching, allocation and UI controls enforce it. |
| Registry validation | Referenced widget ids require both controller code and backing layouts. | Import and development boot call ModuleWidgetFactory.Validate(content, registry) directly. The check covers registry integrity, controller/layout matching and authored widget references. ContentValidator and CodeReferences do not receive the registry. |
| Automatic stories | Modal dialogs block automatic story cards. A waiting beat opens on the first refresh after the last overlay closes if still available. | ShowLiveOverlay skips the automatic walk while an overlay is open. The modal and any unsubmitted Roadie draft remain intact. Regression coverage includes settings, chapter selection, allocation and Encore. |
| Allocation pipeline | Allocation uses the queued root-context command pipeline. | RunCommand flushes pending time, checks and writes the map, conditionally sweeps and refreshes. The UI is reached from Live chapter controls; the command adds no phase guard. |
| Shared references | Shared content references carry shared validation checks. | Encore, BackstagePass and Roadies supply the three CodeReferences checks. Screens, managers and the session reuse those references. The factory/registry check is separate. |
| Roadie retroactivity | Design 8.2: present state (decided 2026-09-10). A dormant chapter's claim reads the allocation standing at switch-in; an already-computed offer keeps its lines. | Matches the code as landed: the allocation is applied at chapter entry to the newly computed offer, and a standing offer retains its stored lines. Closed. |

## Verification status

The most recent completed Unity EditMode run passed 738/738 tests with zero failures or skips. It includes the unlock and registry validation changes and all four modal-deferral cases: settings, chapter selection, Roadie allocation and Encore. This run preceded the asset reimport.

The retroactivity policy is closed as present state (design 8.2, 2026-09-10); no code change followed, since the landed code already took that stance. Passing tests alone do not establish design compliance.
