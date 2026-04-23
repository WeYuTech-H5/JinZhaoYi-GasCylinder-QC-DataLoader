# Runtime Schedule And Backfill

Production resident mode should keep `Scheduler:RunOnce=false`.

When `Scheduler:UseDailySchedule=true`, the worker waits until `Scheduler:DailyWakeUpTime` and runs one import cycle per day. Normal mode uses `Scheduler:NormalTargetDayOffset`; the default `-1` means that on `2026-04-16` the target folder is `20260415`.

Use `RunOnce=true` only for manual verification. It runs immediately once and then stops the host, so it does not wait for `DailyWakeUpTime`.

Historical backfill is controlled by:

```json
"BackfillEnabled": true,
"BackfillTargetDate": "20251119"
```

`BackfillTargetDate` must use `yyyyMMdd`. When `BackfillEnabled=false`, `BackfillTargetDate` is ignored.

Formula guardrails: AVG, RPD and PPB return database `NULL` when required inputs are missing, a denominator is zero/non-positive, or an input area/RF value is negative. The importer does not write fallback `0` for invalid formulas, because `0` can be mistaken for a valid measurement.
