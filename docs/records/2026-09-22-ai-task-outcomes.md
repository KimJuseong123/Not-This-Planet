# AI task outcome and display separation

S15P21B202-690 / MR !587, review gate on commit `62b36d19`.

## Confirmed cause

The previous hygiene/cleanup fixes passed. The new blocking review concerns
`AiPinTask.Status.Contains("완료")` and the follow controller's comparison with
`"곁에서 대기"`. These presentation strings were controlling queued commands.
For example, a cancellation message containing "완료되지 않음" could advance a
plan, while translating a successful message to English could prevent it.

## Correction

- `AiPinTask.Succeeded` is set only after observing the selected pin released.
  Cancellation, interruption and a new task clear it.
- `AiFollowController.Arrived` is updated from the navigation route, not text.
  Stop, new orders, formation changes and recovery invalidate it.
- Sequential pin work, approach/social actions and gathering consume these
  states. Gathering failure clears queued work rather than counting as arrival.
- `Status` remains presentation only. Existing player input, physics and network
  authority paths are unchanged.

## Regression coverage

- Actual physical pin pull produces success; cancellation clears it.
- Translating success/arrival text does not change outcome or movement intent.
- Cancellation with the word "완료" does not count as success.
- Queued follow runs only after successful pin work, independent of status text.
- Existing navigation, pin, command schema and multiplayer-host group tests are
  included in the targeted Unity run. This is not a two-PC runtime validation.

Non-blocking review advisories (navigation performance, resource packaging and
multi-PC lifecycle validation) remain separate from this focused correction.

## Result

Unity 6000.5.4f1: `AiNavigationTests|AiPinTaskTests|AiGroupOrdersTests|AiCommandPlanTests`
passed 16/16, failed 0. Local artifacts: `METAGAME/Logs/ai690-outcome-tests.xml`
and `.log`. Scene-cleanup guard passed for all 20 scene-opening test files.
Player build and two-PC tests were not rerun for this correction.
