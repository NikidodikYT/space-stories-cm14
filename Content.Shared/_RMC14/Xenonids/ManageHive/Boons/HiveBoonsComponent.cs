using Content.Shared._RMC14.Xenonids.Warlock;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Xenonids.ManageHive.Boons;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(HiveBoonSystem), typeof(XenoWarlockSystem))]
public sealed partial class HiveBoonsComponent : Component
{
    [DataField, AutoNetworkedField]
    public int RoyalResin;

    [DataField, AutoNetworkedField]
    public int RoyalResinMax = 10;

    [DataField, AutoNetworkedField]
    public int PsyPoints;

    [DataField, AutoNetworkedField]
    public int PsyPointsMax = 30;

    [DataField, AutoNetworkedField]
    public Dictionary<EntProtoId<HiveBoonDefinitionComponent>, TimeSpan> UnlockAt = new();

    [DataField, AutoNetworkedField]
    public Dictionary<EntProtoId<HiveBoonDefinitionComponent>, TimeSpan> UsedAt = new();

    [DataField, AutoNetworkedField]
    public Dictionary<EntProtoId<HiveBoonDefinitionComponent>, EntityUid> Active = new();

    [DataField, AutoNetworkedField]
    public bool KingAnnounced;
}
