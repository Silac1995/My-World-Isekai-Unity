using System.Collections.Generic;
using UnityEngine;

public abstract class StatusEffectInstance
{
    public abstract void Apply();
    public abstract void Remove();

    /// <summary>
    /// Called every frame by the StatusManager.
    /// </summary>
    public virtual void Tick(float deltaTime) { }
    public virtual void Suspend() { }
    public virtual void Resume() { }
}