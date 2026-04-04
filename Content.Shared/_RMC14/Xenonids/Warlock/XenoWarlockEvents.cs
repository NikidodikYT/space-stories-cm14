using Content.Shared._RMC14.Dialog;
using Content.Shared._RMC14.Xenonids.ManageHive.Boons;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Map;

namespace Content.Shared._RMC14.Xenonids.Warlock;

public sealed partial class XenoPsychicBlastActionEvent : WorldTargetActionEvent;

public sealed partial class XenoPsychicCrushActionEvent : WorldTargetActionEvent;

public sealed partial class XenoPsychicShieldActionEvent : WorldTargetActionEvent;

public sealed partial class XenoPsyDrainActionEvent : EntityTargetActionEvent;

public sealed partial class XenoWarlockMutationsActionEvent : InstantActionEvent;

public sealed partial class XenoWarlockToggleBlastModeActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class XenoPsychicBlastDoAfterEvent : DoAfterEvent
{
    [DataField(required: true)]
    public NetCoordinates Location { get; private set; }

    [DataField(required: true)]
    public XenoWarlockBlastMode Mode { get; private set; }

    private XenoPsychicBlastDoAfterEvent()
    {
    }

    public XenoPsychicBlastDoAfterEvent(NetCoordinates location, XenoWarlockBlastMode mode)
    {
        Location = location;
        Mode = mode;
    }

    public override DoAfterEvent Clone()
    {
        return this;
    }
}

[Serializable, NetSerializable]
public sealed partial class XenoPsyDrainDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed record XenoWarlockChooseMutationEvent(XenoWarlockMutationCategory Category);

[Serializable, NetSerializable]
public sealed record XenoWarlockChooseModeEvent(XenoWarlockBlastMode Mode);

[Serializable, NetSerializable]
public sealed partial class HiveBoonBuildMutationChamberEvent : HiveBoonEvent
{
    [DataField(required: true)]
    public XenoWarlockMutationCategory ChamberCategory;

    [DataField(required: true)]
    public EntProtoId ChamberPrototype;

    [DataField]
    public int MaxCount = 3;
}
