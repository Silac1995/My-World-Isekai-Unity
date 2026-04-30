using System;
using UnityEngine;

namespace MWI.Cinematics
{
    [Serializable]
    public struct RoleSlot
    {
        [SerializeField] private string _roleId;
        [SerializeField] private string _displayName;
        [SerializeField] private RoleSelectorSO _selector;
        [SerializeField] private bool _isOptional;
        [SerializeField] private bool _isPrimaryActor;   // for OncePerNpc keying — Phase 2

        public ActorRoleId    RoleId         => new ActorRoleId(_roleId);
        public string         DisplayName    => string.IsNullOrEmpty(_displayName) ? _roleId : _displayName;
        public RoleSelectorSO Selector       => _selector;
        public bool           IsOptional     => _isOptional;
        public bool           IsPrimaryActor => _isPrimaryActor;
    }
}
