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

Processed source folders are moved into an `archive` subfolder under the same source folder after a successful non-dry-run import. For example, `...\20260420\PORT 5\PORT 5[...].D` is moved to `...\20260420\PORT 5\archive\PORT 5[...].D`. The importer creates the `archive` folder automatically when it does not exist, and the scanner ignores anything already under `archive`.

When Query2 Excel export is enabled, the output workbook is written to the `QC` subfolder under the batch day folder. For example, importing `C:\Users\Andy\Downloads\Gas\20260420` writes `C:\Users\Andy\Downloads\Gas\20260420\QC\Cylinder_Qc[20260420].xlsx`. The exporter creates the `QC` folder automatically when it does not exist.
