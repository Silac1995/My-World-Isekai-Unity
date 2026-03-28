using UnityEngine;

/// <summary>
/// Inspector-exposed particle overrides, passed from BattleManager serialized fields.
/// </summary>
public struct ZoneParticleSettings
{
    public float Rate;
    public Color Color;
    public Vector2 Size;
    public Vector2 Lifetime;
    public Vector2 DriftY;
}

public class BattleZoneController
{
    private BattleManager _manager;
    private Collider _battleZone;
    private Unity.AI.Navigation.NavMeshModifierVolume _battleZoneModifier;
    private LineRenderer _battleZoneLine;
    private ParticleSystem _particles;
    private ZoneParticleSettings _particleSettings;

    private Vector3 _baseBattleZoneSize;
    private float _perParticipantGrowthRate;
    private int _participantsPerTier;

    public BattleZoneController(BattleManager manager, Unity.AI.Navigation.NavMeshModifierVolume modifier, LineRenderer line, ParticleSystem particles, ZoneParticleSettings particleSettings, Vector3 baseSize, float growthRate, int participantsPerTier)
    {
        _manager = manager;
        _battleZoneModifier = modifier;
        _battleZoneLine = line;
        _particles = particles;
        _particleSettings = particleSettings;
        _baseBattleZoneSize = baseSize;
        _perParticipantGrowthRate = growthRate;
        _participantsPerTier = participantsPerTier;
    }

    public void CreateBattleZone(Character a, Character b)
    {
        _manager.transform.rotation = Quaternion.identity;
        _manager.transform.localScale = Vector3.one;

        var oldColliders = _manager.gameObject.GetComponents<BoxCollider>();
        foreach (var old in oldColliders) Object.Destroy(old);

        BoxCollider box = _manager.gameObject.AddComponent<BoxCollider>();
        _manager.gameObject.tag = "BattleZone";
        box.isTrigger = true;
        box.size = _baseBattleZoneSize;
        
        Vector3 center = (a.transform.position + b.transform.position) / 2f;
        _manager.transform.position = new Vector3(center.x, a.transform.position.y, center.z);

        ResolveZoneOverlap();

        _battleZone = box;

        if (_battleZoneModifier != null)
        {
            _battleZoneModifier.size = box.size;
            _battleZoneModifier.center = box.center;
        }

        ApplyParticleSettings();
        UpdateParticleShape();
        if (_particles != null && !_particles.isPlaying)
            _particles.Play();
    }

    private void ApplyParticleSettings()
    {
        if (_particles == null) return;

        var main = _particles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(_particleSettings.Lifetime.x, _particleSettings.Lifetime.y);
        main.startSize = new ParticleSystem.MinMaxCurve(_particleSettings.Size.x, _particleSettings.Size.y);
        main.startColor = _particleSettings.Color;

        var emission = _particles.emission;
        emission.rateOverTime = _particleSettings.Rate;

        var velocity = _particles.velocityOverLifetime;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(_particleSettings.DriftY.x, _particleSettings.DriftY.y);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
    }

    private void ResolveZoneOverlap()
    {
        int maxAttempts = 5;
        Vector3 halfExtents = _baseBattleZoneSize / 2f;

        for (int i = 0; i < maxAttempts; i++)
        {
            Collider[] overlaps = Physics.OverlapBox(_manager.transform.position, halfExtents, Quaternion.identity);
            bool foundOverlap = false;

            foreach (var other in overlaps)
            {
                if (other.gameObject != _manager.gameObject && other.CompareTag("BattleZone"))
                {
                    foundOverlap = true;
                    
                    Vector3 pushDir = (_manager.transform.position - other.transform.position);
                    pushDir.y = 0;
                    
                    if (pushDir.sqrMagnitude < 0.01f) 
                        pushDir = Vector3.right;
                    else
                        pushDir.Normalize();

                    float shiftAmount = _baseBattleZoneSize.x * 0.5f;
                    Vector3 targetPos = _manager.transform.position + pushDir * shiftAmount;

                    if (IsZoneValidOnNavMesh(targetPos))
                    {
                        _manager.transform.position = targetPos;
                    }
                    else
                    {
                        Vector3 altDir = Quaternion.Euler(0, 90, 0) * pushDir;
                        Vector3 altPos = _manager.transform.position + altDir * shiftAmount;

                        if (IsZoneValidOnNavMesh(altPos))
                        {
                            _manager.transform.position = altPos;
                        }
                        else
                        {
                            return;
                        }
                    }
                    break;
                }
            }

            if (!foundOverlap) break;
        }

        if (!IsZoneValidOnNavMesh(_manager.transform.position))
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(_manager.transform.position, out UnityEngine.AI.NavMeshHit hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                _manager.transform.position = hit.position;
            }
        }
    }

    private bool IsZoneValidOnNavMesh(Vector3 position)
    {
        int pointsOnNavMesh = 0;
        float halfX = _baseBattleZoneSize.x * 0.5f;
        float halfZ = _baseBattleZoneSize.z * 0.5f;

        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                Vector3 samplePoint = position + new Vector3(x * halfX * 0.8f, 0, z * halfZ * 0.8f);
                if (UnityEngine.AI.NavMesh.SamplePosition(samplePoint, out UnityEngine.AI.NavMeshHit hit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    pointsOnNavMesh++;
                }
            }
        }

        return pointsOnNavMesh >= 5;
    }

    public void UpdateBattleZoneWith(int participantCount)
    {
        if (_battleZone == null || participantCount == 0) return;
        BoxCollider box = _battleZone as BoxCollider;
        if (box == null) return;

        int tiers = (participantCount - 1) / _participantsPerTier;
        float multiplier = 1f + (tiers * _perParticipantGrowthRate);

        box.size = new Vector3(_baseBattleZoneSize.x * multiplier, _baseBattleZoneSize.y, _baseBattleZoneSize.z * multiplier);

        if (_battleZoneModifier != null)
        {
            _battleZoneModifier.size = box.size;
            _battleZoneModifier.center = box.center;
        }

        DrawBattleZoneOutline();
    }

    public void DrawBattleZoneOutline()
    {
        if (_battleZoneLine == null || _battleZone == null) return;
        BoxCollider box = _battleZone as BoxCollider;
        if (box == null) return;

        _battleZoneLine.useWorldSpace = true;
        _battleZoneLine.loop = true;
        _battleZoneLine.positionCount = 4;

        Vector3 center = box.transform.position;
        Vector3 size = box.size;

        float x = size.x / 2f;
        float z = size.z / 2f;

        Vector3[] corners = new Vector3[4]
        {
            center + new Vector3(-x, 0, -z),
            center + new Vector3(-x, 0, z),
            center + new Vector3(x, 0, z),
            center + new Vector3(x, 0, -z)
        };

        _battleZoneLine.SetPositions(corners);

        UpdateParticleShape();
    }

    private void UpdateParticleShape()
    {
        if (_particles == null || _battleZone == null) return;
        BoxCollider box = _battleZone as BoxCollider;
        if (box == null) return;

        var shape = _particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.boxThickness = new Vector3(1, 1, 1); // emit from edges only
        shape.scale = new Vector3(box.size.x, 0.1f, box.size.z);
    }
}
