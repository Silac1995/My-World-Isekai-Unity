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

    // Smooth visual transition
    private Vector3 _visualSize;
    private Vector3 _targetVisualSize;
    private bool _isAnimating;
    private const float RESIZE_SPEED = 3f;

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

        // Start tiny and lerp to full size
        _visualSize = Vector3.one * 0.5f;
        _targetVisualSize = box.size;
        _isAnimating = true;

        ApplyParticleSettings();
        DrawVisuals();
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

        // Collider + NavMesh snap immediately (gameplay)
        box.size = new Vector3(_baseBattleZoneSize.x * multiplier, _baseBattleZoneSize.y, _baseBattleZoneSize.z * multiplier);

        if (_battleZoneModifier != null)
        {
            _battleZoneModifier.size = box.size;
            _battleZoneModifier.center = box.center;
        }

        // Visuals lerp smoothly
        _targetVisualSize = box.size;
        _isAnimating = true;
    }

    /// <summary>
    /// Called every frame by BattleManager.Update(). Smoothly interpolates the visual outline
    /// and particle shape toward the target size.
    /// </summary>
    public void Tick()
    {
        if (!_isAnimating) return;

        _visualSize = Vector3.Lerp(_visualSize, _targetVisualSize, Time.deltaTime * RESIZE_SPEED);

        // Stop animating once close enough
        if (Vector3.Distance(_visualSize, _targetVisualSize) < 0.01f)
        {
            _visualSize = _targetVisualSize;
            _isAnimating = false;
        }

        DrawVisuals();
    }

    public void DrawBattleZoneOutline()
    {
        DrawVisuals();
    }

    private void DrawVisuals()
    {
        if (_battleZoneLine == null || _battleZone == null) return;

        _battleZoneLine.useWorldSpace = true;
        _battleZoneLine.loop = true;
        _battleZoneLine.positionCount = 4;

        Vector3 center = _battleZone.transform.position;
        float x = _visualSize.x / 2f;
        float z = _visualSize.z / 2f;

        Vector3[] corners = new Vector3[4]
        {
            center + new Vector3(-x, 0, -z),
            center + new Vector3(-x, 0, z),
            center + new Vector3(x, 0, z),
            center + new Vector3(x, 0, -z)
        };

        _battleZoneLine.SetPositions(corners);

        // Particle shape follows the visual size
        if (_particles != null)
        {
            var shape = _particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.boxThickness = new Vector3(1, 1, 1);
            shape.scale = new Vector3(_visualSize.x, 0.1f, _visualSize.z);
        }
    }
}
