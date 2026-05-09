---
name: skill-creator
description: Helps the agent create new Antigravity skills following official structural requirements and best practices.
---

# Skill Creator

This skill assists in the creation of specialized Antigravity skills. Use it when the user requests a new skill or when a repetitive complex task would benefit from structured automation.

## When to use this skill
- When the user explicitly requests: "Create a skill for [domain/task]".
- When performing a complex, repetitive task that requires structured documentation for future reuse.
- To organize project-specific knowledge in the `.agent/skills/` or `.gemini/antigravity/skills/` folders.

## How to use it

1.  **Identify the location**:
    -   Global: `~/.gemini/antigravity/skills/<skill-folder>/`
    -   Local (Workspace): `<workspace-root>/.agent/skills/<skill-folder>/`
2.  **Create the structure**:
    -   Create the `<skill-folder>` directory using kebab-case.
    -   Create the mandatory `SKILL.md` file inside it.
    -   Create `examples/` directory for code patterns and reference implementations. (Optional: `scripts/` or `resources/`).
3.  **Fill in `SKILL.md`**:
    -   Include the YAML frontmatter with `name` (kebab-case) and a concise `description`.
    -   Follow the mandatory header structure: `# [Skill Name]`, `## When to use this skill`, and `## The [Architecture/System]`.
4.  **Create Examples**:
    -   Add specific markdown files (e.g., `examples/patterns.md`) demonstrating structural code references, sequential logic, or usage patterns.

### SKILL.md Template

```markdown
---
name: [skill-name-kebab-case]
description: [Concise third-person summary of the skill's purpose for agent discovery]
---

# [Skill System Name]

[Broad overview of what the system does and its overarching philosophy in the project.]

## When to use this skill
- [Scenario 1 - e.g., When creating a new X]
- [Scenario 2 - e.g., When debugging Y]

## The [System Paradigm] Architecture
[Optional: Explain the architectural paradigm shift, contrasting the old way vs. the new way, or stating the core dependency pattern.]

### 1. [Core Concept / Entry Point]
[Explanation of how the central component works.]
**Rule:** [Explicit technical rule or constraint, e.g., "Always use Dependency Injection..."]
- [Detail 1]
- [Detail 2]

### 2. [Secondary Concept / Execution]
[Explanation of how the components interact or execute.]
[Code snippet showing the structure, if applicable.]

### 3. [Existing Components]
- [Component A] -> [Its Role/Action]
- [Component B] -> [Its Role/Action]

[Note: Put detailed code implementation patterns in `examples/pattern_name.md` instead of cluttering this file.]
```

## Tips for Skill Creation
- **Architecture First**: Clearly define the architecture or SOLID principles the system relies on before listing steps. Highlight strict **Rules**.
- **Discovery**: The `description` in the frontmatter is the only thing the agent sees initially. Make it keyword-rich and focused on "what" and "when".
- **Naming**: Folder names and the `name` field in frontmatter MUST use kebab-case.
- **Scope**: Keep skills focused on a single domain or task.
- **Examples in Subfolders**: ALWAYS include practical code examples. Do not put all code inside `SKILL.md`; instead create files like `examples/my_patterns.md` showing reference implementations.
