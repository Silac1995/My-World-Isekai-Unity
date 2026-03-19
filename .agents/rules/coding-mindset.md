---
trigger: always_on
---

## Cognitive Discipline — Think Before You Code
Before writing any code or making any architectural decision, you must stop and think. Do not rush to implementation. Complexity in this project is almost always higher than it first appears.
Mandatory pre-implementation checklist:

What are all the systems this change could touch or break?
Does this introduce hidden coupling between components?
Will this still work correctly with 2+ players in the scene?
Am I solving the real problem, or just the visible symptom?

Rules:

Never underestimate. If a task feels simple, assume there is a non-obvious edge case. Look for it before proceeding.
Think out loud first. For any non-trivial task, briefly explain your reasoning and approach before writing a single line of code. State your assumptions explicitly.
Slow down on architecture. A bad structural decision costs 10x more to fix later than to get right now. Prefer the careful solution over the fast one.
No placeholders without warning. Never silently skip a complex part with a // TODO. If something is non-trivial to implement, flag it explicitly and explain why.
Admit uncertainty. If you are not sure how a system works in this project, ask. Do not invent a plausible-sounding answer.

The goal is not speed. The goal is correctness, maintainability, and zero technical debt.

SOLID principles are non-negotiable and apply at all times — when writing new code, fixing bugs, or touching existing code. Every class you write or modify must respect Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, and Dependency Inversion. If existing code you are touching violates SOLID, you are expected to refactor it as part of your change. If the scope of that refactor is significant, declare it upfront before implementing — but do not leave a SOLID violation in place simply because it was already there.

Refactoring Initiative — Fix the Root, Not Just the Symptom
When fixing a bug or resolving an issue, do not limit yourself to the minimal patch. Actively evaluate whether the issue reveals a deeper structural problem.
When fixing an issue, you must ask:

Is this bug a symptom of a class doing too much (Single Responsibility violation)?
Would a new class or abstraction make this fix cleaner and prevent the same issue from recurring elsewhere?
Is the fix introducing new coupling that a better structure would avoid?

Rules:

If a fix requires more than ~10 lines of logic in an existing class, consider whether that logic belongs in a dedicated class. Propose it explicitly before implementing.
When creating a new class to support a fix, justify its existence: name the responsibility it owns and explain why the existing class should not hold it.
Refactoring scope must be declared upfront. Before touching anything beyond the immediate fix, state clearly what you intend to restructure and why. Do not silently expand scope.
Do not remove or restructure existing, working code as part of a fix without explicit confirmation. Flag it as a separate recommendation.
A fix that leaves the architecture worse than it was is not an acceptable fix.

The standard: After the fix, the codebase should be more maintainable than before — not just functional.