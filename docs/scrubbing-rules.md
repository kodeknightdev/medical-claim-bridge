# Scrubbing rules

The scrubber checks a claim before it reaches a payer. Payers reject or deny a claim for things that are cheap to catch
up front (a wrong check digit, a diagnosis that does not support the procedure, a modifier the payer stopped
accepting), and every one caught here saves a resubmission cycle of days or weeks.

## How a claim is scrubbed

The claims service calls the scrubber over gRPC ([`scrubber.proto`](../src/shared/protos/scrubber.proto)) in two
situations:

- **Preview.** While a user types a claim in the portal, the form calls `POST /api/claims/scrub-preview` 700 ms after
  the last change. Claims checks eligibility and the denial risk in parallel and calls `Scrub` with `persist = false`.
  Nothing is stored; the response goes straight back to the form as inline edits.
- **Final scrub.** After intake, [`ScrubStepHandler`](../src/services/claims/ClaimBridge.Claims/Lifecycle/ScrubStepHandler.cs)
  handles `claims.claim-received` and calls `Scrub` with `persist = true`. The scrubber stores a `ScrubRun` with every
  edit, and the claim moves to `ReadyToSubmit` or `ScrubFailed`.

[`ScrubEngine`](../src/services/scrubber/ClaimBridge.Scrubber/Rules/ScrubEngine.cs) runs the built-in rules, then
the payer's rules, and collects edits. An edit has a rule code, a severity, a message, the field it points at, the line
number and a fix suggestion. A claim passes when it has no `Error` edits; warnings are shown but never block.

Code sets (procedures, ICD-10-CM, modifiers, medical necessity pairs, timely filing limits) are loaded from the scrubber
database into a `CodeSetSnapshot` and cached for an hour in HybridCache (in-process plus Redis). Payer rules are cached
per payer for 15 minutes and invalidated when an admin changes one.

## Built-in rules

Each built-in rule is a class implementing [`IScrubRule`](../src/services/scrubber/ClaimBridge.Scrubber/Rules/IScrubRule.cs)
in the `Rules/BuiltIn` folder; they are registered by scanning the assembly, so adding a rule is adding a class.

| Code | Rule | File | Catches |
|---|---|---|---|
| CB001 | Required fields | [`ClaimLevelRules.cs`](../src/services/scrubber/ClaimBridge.Scrubber/Rules/BuiltIn/ClaimLevelRules.cs) | missing payer, billing NPI, member id, DOB, gender, diagnoses or lines; more than 50 lines |
| CB002 | NPI check digit | `ClaimLevelRules.cs` | billing or rendering NPI failing the Luhn check (with the `80840` prefix) |
| CB003 | ICD-10-CM diagnosis codes | [`CodingRules.cs`](../src/services/scrubber/ClaimBridge.Scrubber/Rules/BuiltIn/CodingRules.cs) | more than 12 diagnoses, bad format, unknown code, non-billable category header, repeats (warning) |
| CB004 | Diagnosis pointers | `CodingRules.cs` | line without a pointer, more than 4 pointers, pointer past the diagnosis list |
| CB005 | Procedure, units, charge, place of service | `CodingRules.cs` | unknown procedure, zero or fractional units, zero charge, unsupported POS |
| CB006 | Procedure and modifier combinations | `CodingRules.cs` | unknown modifiers, modifiers that do not apply to the procedure category, LT+RT on one line, 25 without a separate procedure (warning) |
| CB007 | Diagnosis supports procedure | `CodingRules.cs` | none of the line's pointed diagnoses match the medical necessity list for the procedure |
| CB008 | Date logic | [`DateRules.cs`](../src/services/scrubber/ClaimBridge.Scrubber/Rules/BuiltIn/DateRules.cs) | DOB in the future or over 120 years, service in the future or before birth |
| CB009 | Timely filing limit | `DateRules.cs` | filed after the payer's limit (error) or within 10 days of it (warning) |
| CB010 | Eligibility on date of service | `ClaimLevelRules.cs` | coverage not verified (warning), not active, or not active on a line's date |
| CB011 | Duplicate claim | `ClaimLevelRules.cs` | a new claim that matches an existing claim's fingerprint |
| CB012 | Duplicate service lines | `CodingRules.cs` | same procedure, date and modifiers twice (warning) |
| CB013 | Claim frequency and original reference | `ClaimLevelRules.cs` | unsupported frequency code, 7/8 without an original claim number |

The duplicate fingerprint ([`ClaimFingerprint`](../src/services/claims/ClaimBridge.Claims/Domain/ClaimFingerprint.cs))
is computed by claims from the org, hashed member id, billing NPI, payer and the set of date/procedure/modifier
combinations; charges are left out so a re-keyed amount still matches. Claims
looks for a live claim with the same fingerprint and passes its number to the scrubber as `possible_duplicate_of`.

Timely filing limits come from the payer directory: Northwind (Payer A) 90 days, Bluepeak (Payer B) 180 days, Cascade
(Payer C) 365 days.

## Payer rules

Payers publish their own edits ("we pay at most 4 units of therapy per day", "no modifier 59"). These change often,
so they are data, not code. A payer rule is a row in the `PayerRules` table with a JSON condition, a severity, a
message and a fix suggestion. [`PayerRuleCompiler`](../src/services/scrubber/ClaimBridge.Scrubber/Rules/PayerRules/PayerRuleCompiler.cs)
turns the condition into a composite specification
([`Specification.cs`](../src/services/scrubber/ClaimBridge.Scrubber/Rules/Specifications/Specification.cs)) that is
evaluated per service line; a line that satisfies the condition gets the edit.

Operators:

| Operator | Value | True when |
|---|---|---|
| `all`, `any` | array of conditions | every / at least one child is true |
| `not` | condition | the child is false |
| `procedureIn` | array of codes | the line's procedure is in the list |
| `procedureCategory` | string (`EM`, `Lab`, `Imaging`, ...) | the procedure's category matches |
| `modifierPresent`, `modifierAbsent` | string | the line has / does not have the modifier |
| `unitsAbove`, `chargeAbove` | number | the line's units / charge exceed the value |
| `posIn` | array | the line's place of service is in the list |
| `hasPriorAuth` | bool | a prior authorisation number is / is not present |
| `dxPrefixAny` | array | any claim diagnosis starts with one of the prefixes |
| `frequencyIn` | array | the claim frequency code is in the list |
| `patientAgeBelow` | int | the patient is younger than the value on the date of service |

Seeded rules ([`PayerRuleSeeder`](../src/services/scrubber/ClaimBridge.Scrubber/Data/PayerRuleSeeder.cs)):

| Payer | Code | Condition | Severity |
|---|---|---|---|
| Northwind (A) | PA-A01 | `{"all":[{"procedureIn":["73721","72148"]},{"hasPriorAuth":false}]}` | Error |
| Northwind (A) | PA-A02 | `{"all":[{"modifierPresent":"25"},{"procedureCategory":"EM"}]}` | Warning |
| Bluepeak (B) | PA-B01 | `{"all":[{"procedureIn":["97110","97140"]},{"unitsAbove":4}]}` | Error |
| Bluepeak (B) | PA-B02 | `{"modifierPresent":"59"}` | Error |
| Cascade (C) | PA-C01 | `{"chargeAbove":5000}` | Warning |
| Cascade (C) | PA-C02 | `{"all":[{"posIn":["02","10"]},{"modifierAbsent":"95"}]}` | Error (disabled) |

Admins manage rules through `GET/POST /api/payer-rules` and `PUT /api/payer-rules/{id}` (enable, disable, change
severity). A new rule is compiled before it is saved, so a typo in the condition is a `400` with the reason rather
than a broken scrub later. Saving invalidates that payer's cached rules.

## Code sets

The code sets in [`CodeSets/`](../src/services/scrubber/ClaimBridge.Scrubber/CodeSets) are a small subset seeded into
the database on first start: 37 procedure codes with categories and short descriptions written for this project (no
licensed CPT descriptors), 62 ICD-10-CM codes, 12 modifiers and the medical necessity pairs. A real deployment would
load the full sets from licensed sources into the same tables.

## Metrics

The `ClaimBridge.Scrubber` meter counts `scrubber.runs` (by payer and outcome) and `scrubber.edits` (by rule code and
severity) for OpenTelemetry; analytics keeps its own counts from `claims.claim-scrubbed` for the "top edits" chart.
