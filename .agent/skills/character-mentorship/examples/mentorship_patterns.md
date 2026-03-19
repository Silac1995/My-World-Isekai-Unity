# Mentorship Patterns

Below are key reference implementations for the Character Mentorship system.

## 1. Awarding XP to a Student (The Tick)
The learning progress goes through `CharacterMentorship.ReceiveLessonTick()`.
It scales XP based on the Mentor's `SkillTier`, checking bounds and unlocking the core skill/combat style if it reaches 100%.

```csharp
public void ReceiveLessonTick(ScriptableObject subject, SkillTier mentorTier, float baseXP)
{
    float finalXP = baseXP * mentorTier.GetMentorshipXPMultiplier();

    if (subject is SkillSO skillSO)
    {
        CharacterSkills skills = _character.CharacterSkills;
        if (skills != null && skills.HasSkill(skillSO))
        {
            SkillTier studentTier = SkillTierExtensions.GetTierForLevel(skills.GetSkillLevel(skillSO));
            
            // Student graduation check
            if ((int)studentTier >= (int)mentorTier - 1)
            {
                Graduate(skillSO);
                return;
            }
            skills.GainXP(skillSO, Mathf.CeilToInt(finalXP));
        }
        else
        {
            // Initial Learning Progression
            _learningProgress += finalXP; 
            if (_learningProgress >= 100f)
            {
                skills.AddSkill(skillSO, 1);
                _learningProgress = 0f;
            }
        }
    }
}
```

## 2. Dynamic Zone Instantiation (The Mentor)
Mentors actively scan their environment to spawn `MentorClassZone` efficiently close to them, ensuring NavMesh validity.

```csharp
private Vector3 FindValidClassPosition(float zoneSize)
{
    float targetDistance = (zoneSize / 2f) + 2f; 
    Vector3[] directions = { transform.forward, transform.right, -transform.right, -transform.forward };

    foreach (var dir in directions)
    {
        Vector3 testPos = transform.position + (dir * targetDistance);
        
        bool hitWall = NavMesh.Raycast(transform.position, testPos, out NavMeshHit edgeHit, NavMesh.AllAreas);
        if (!hitWall)
        {
            if (NavMesh.SamplePosition(testPos, out NavMeshHit validHit, 2.0f, NavMesh.AllAreas))
            {
                return validHit.position; // Found a valid open spot
            }
        }
    }
    // Fallback if cornered
    return transform.position;
}
```

## 3. Formations (The Student Spot)
The `MentorClassZone` handles calculating structural grid layouts dynamically based on student indices.

```csharp
public Vector3 GetStudentSlotPosition(Character student)
{
    int index = LinkedClass.EnrolledStudents.ToList().IndexOf(student);
    
    // Grid config
    int columns = 3; 
    float rowSpacing = 2.5f; 
    float colSpacing = 2.0f; 

    int row = index / columns;
    int col = index % columns;

    float colOffset = (col - ((columns - 1) / 2f)) * colSpacing;
    float zOffsetFromCenter = (_zoneCollider.size.z / 2f) - 1.5f - (row * rowSpacing);

    // Final layout application relative to zone orientation
    Vector3 slotPos = transform.position + (transform.forward * zOffsetFromCenter) + (transform.right * colOffset);
    slotPos.y = Mentor.transform.position.y;
    return slotPos;
}
```
