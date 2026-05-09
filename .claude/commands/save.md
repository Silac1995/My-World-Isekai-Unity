---
description: Capture a decision or gotcha from the current conversation into the wiki.
argument-hint: <kind> <short-title> — kind is either "decision" or "gotcha"
---

# /save — SAVE conversational knowledge

Capture the current-session decision or gotcha into the appropriate wiki folder.

Parse `$ARGUMENTS`:
- First token: `decision` or `gotcha`.
- Rest: short human title.

## Behavior

### If kind = `decision`
1. Find the next unused ADR number by scanning `wiki/decisions/` for `adr-NNNN-*.md`.
2. Create `wiki/decisions/adr-NNNN-<kebab-title>.md` from the `decision` template.
3. Fill as much as possible from the conversation:
   - Context, options considered, decision, consequences.
   - `decided_by:` — whoever made the call in the conversation.
   - `decision_date:` — today.
4. Set `status: accepted` unless the user said "proposed" explicitly.
5. Add wikilinks to every system, gotcha, or person mentioned.
6. Update every linked page's `related:` list for reciprocal backlinks.

### If kind = `gotcha`
1. Create `wiki/gotchas/<kebab-title>.md` from the `gotcha` template.
2. Fill from conversation: symptom, root cause, how to avoid, how to fix, affected systems.
3. Set `status: open` unless the conversation confirmed the fix ship.
4. Add wikilinks to affected systems and reciprocal backlinks.

## Rules

- If the conversation doesn't contain enough to fill the template, **stop and ask**.
- Always `created: <today>` and `updated: <today>`.
- Always add a source line pointing to this conversation with today's date.
- If this save crosses with an existing page (e.g. duplicate gotcha), surface that and ask whether to merge.

## Output
Summarize: page path created, what fields were filled, what remains open.
