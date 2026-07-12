# GitHub workflow

Board Racing uses GitHub Issues as its sole work-tracking surface. We intentionally avoid Projects, milestones, bots, and custom workflow automation while the team is small.

## Status model

Status is inferred from native GitHub state:

| State | Meaning |
| --- | --- |
| Open issue | Outstanding work or an idea under discussion |
| Assigned issue | Actively owned |
| Linked open pull request | Implementation underway |
| Closed issue | Completed, declined, or obsolete |

## Issue lifecycle

### 1. Capture

Create an issue as soon as a bug, feature, or meaningful chore needs to persist beyond the current conversation. Use the Feature / Task or Bug form when applicable. Blank issues are available for early ideas and discussion.

Tiny typo-only documentation corrections may go directly to a pull request without a separate issue.

### 2. Clarify

Turn the issue into one independently verifiable outcome. Record:

- The problem or desired outcome.
- What is included and deliberately excluded.
- Observable acceptance criteria.
- Dependencies, hardware needs, or unresolved decisions.
- The relevant roadmap tranche and priority.

Split the issue when it contains outcomes that could be implemented, reviewed, or accepted independently.

### 3. Ready

An issue is ready when it has:

- A clear outcome.
- Bounded scope.
- Verifiable acceptance criteria.
- Known dependencies or an explicit statement that there are none.

Assign the issue to the person taking responsibility for it. Assignment means active ownership, not merely interest.

### 4. Implement

Create a branch from `main` using:

```text
issue-<number>-<short-description>
```

Example:

```text
issue-12-piece-reconnection
```

Keep changes focused on the issue. If implementation exposes additional work that is not required by the acceptance criteria, create a follow-up issue instead of silently expanding the pull request.

### 5. Pull request

Open one pull request for the issue in the normal case. Include a closing keyword in the pull-request description:

```text
Closes #12
```

The pull request should explain the change, record verification, state whether Board hardware or the simulator was used, and identify remaining risks or follow-ups.

Merge only after the issue's acceptance criteria are addressed and the relevant verification is recorded. Merging into the default branch closes the linked issue automatically.

### 6. Close

Completed issues close through their merged pull requests. If an issue is declined or obsolete, close it manually with a short comment explaining why so the decision remains discoverable.

## Labels and triage

Every actionable issue should receive one type label:

- `type: feature`
- `type: bug`
- `type: chore`
- `type: docs`

During triage, add one priority label:

- `priority: high` — time-sensitive or blocks important work.
- `priority: normal` — the default for planned work.

Use `tranche: 1` through `tranche: 6` to connect work to the [roadmap](roadmap.md). Early ideas that have not been assigned to a tranche may remain unlabeled until clarified.

Apply `blocked` only when progress requires an external decision, dependency, hardware access, or upstream fix. Add a comment stating the blocking condition and what will unblock the issue. Remove the label as soon as that condition clears.

## Scope changes and follow-ups

- Update the original issue when clarification changes how its existing outcome will be achieved.
- Create a follow-up issue when a newly discovered outcome can be delivered independently.
- Link follow-ups from the original issue or pull request.
- Do not use an open-ended issue as a container for an entire roadmap tranche.

## Hardware-dependent work

Board-specific behavior should say how it was verified:

- Not applicable.
- Mouse/keyboard fallback only.
- Board Unity SDK simulator.
- Physical Board hardware.

If hardware testing remains necessary after merge, create a follow-up issue rather than implying the behavior was fully validated.

