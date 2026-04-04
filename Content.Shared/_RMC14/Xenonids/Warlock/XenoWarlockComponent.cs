using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System.Numerics;

namespace Content.Shared._RMC14.Xenonids.Warlock;

[Serializable, NetSerializable]
public enum XenoWarlockBlastMode : byte
{
    PsychicBlast,
    PsychicDrain,
    PsychicLance,
}

[Serializable, NetSerializable]
public enum XenoWarlockMutationCategory : byte
{
    Shell,
    Spur,
    Veil,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoWarlockSystem))]
public sealed partial class XenoWarlockComponent : Component
{
    [DataField, AutoNetworkedField]
    public XenoWarlockBlastMode BlastMode = XenoWarlockBlastMode.PsychicBlast;

    [DataField, AutoNetworkedField]
    public bool CautiousMind;

    [DataField, AutoNetworkedField]
    public bool DrainingBlast;

    [DataField, AutoNetworkedField]
    public bool MobileMind;

    [DataField, AutoNetworkedField]
    public bool Primordial;

    [DataField]
    public EntityUid? ActiveShield;

    [DataField]
    public MapCoordinates? ActiveCrush;

    [DataField]
    public TimeSpan CrushStart;

    [DataField]
    public TimeSpan CrushResolveAt;

    [DataField]
    public TimeSpan NextCrushExpandAt;

    [DataField]
    public int CrushIterations;

    [DataField]
    public EntityUid? CrushOrb;

    [DataField]
    public EntityUid? BlastChargeEffect;

    public EntityUid? CrushGrid;
    public HashSet<Vector2i> CrushTiles = new();
    public HashSet<Vector2i> CrushFrontier = new();
    public List<EntityUid> CrushWarnings = new();
}

[RegisterComponent]
[Access(typeof(XenoWarlockSystem))]
public sealed partial class XenoWarlockPrimordialComponent : Component;

[RegisterComponent]
[Access(typeof(XenoWarlockSystem))]
public sealed partial class PsyDrainedCorpseComponent : Component;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoWarlockSystem))]
public sealed partial class XenoMutationChamberComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public XenoWarlockMutationCategory Category;
}

[RegisterComponent]
[Access(typeof(XenoWarlockSystem))]
public sealed partial class XenoWarlockShieldComponent : Component
{
    [DataField]
    public EntityUid Warlock;

    [DataField]
    public Angle Direction;

    [DataField]
    public TimeSpan ExpiresAt;

    [DataField]
    public bool Manual;

    [DataField]
    public bool Detonated;

    [DataField]
    public bool ProjectilesReleased;

    public List<XenoWarlockFrozenProjectile> FrozenProjectiles = new();
}

public sealed class XenoWarlockFrozenProjectile
{
    public EntityUid Projectile;
    public Vector2 LinearVelocity;
    public Angle Rotation;
    public EntityUid? Shooter;
    public EntityUid? Weapon;
}
