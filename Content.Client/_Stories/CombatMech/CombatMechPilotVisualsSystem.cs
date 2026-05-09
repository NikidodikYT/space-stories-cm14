using System.Numerics;
using Content.Client._RMC14.Buckle;
using Content.Shared._RMC14.Sprite;
using Content.Shared._Stories.CombatMech;
using Robust.Client.GameObjects;
using Robust.Shared.Maths;
using DrawDepthType = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._Stories.CombatMech;

public sealed class CombatMechPilotVisualsSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private readonly HashSet<EntityUid> _visualPilots = new();
    private readonly HashSet<EntityUid> _seenPilots = new();
    private readonly List<EntityUid> _stalePilots = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<InsideCombatVehicleComponent, GetDrawDepthEvent>(
            OnInsideVehicleGetDrawDepth,
            after: [typeof(RMCBuckleVisualsSystem)]);
        SubscribeLocalEvent<CombatMechPilotVisualsComponent, ComponentStartup>(OnPilotVisualsStartup);
        SubscribeLocalEvent<CombatMechPilotVisualsComponent, AfterAutoHandleStateEvent>(OnPilotVisualsState);
        SubscribeLocalEvent<CombatMechPilotVisualsComponent, ComponentShutdown>(OnPilotVisualsShutdown);
    }

    private void OnInsideVehicleGetDrawDepth(Entity<InsideCombatVehicleComponent> ent, ref GetDrawDepthEvent args)
    {
        if (!HasComp<CombatMechComponent>(ent.Comp.Vehicle))
            return;

        args.DrawDepth = DrawDepthType.Mobs;
    }

    public override void Update(float frameTime)
    {
        _seenPilots.Clear();

        var query = EntityQueryEnumerator<InsideCombatVehicleComponent>();
        while (query.MoveNext(out var uid, out var inside))
        {
            _seenPilots.Add(uid);
            ApplyPilotVisuals((uid, inside));
        }

        _stalePilots.Clear();
        foreach (var pilot in _visualPilots)
        {
            if (!_seenPilots.Contains(pilot))
                _stalePilots.Add(pilot);
        }

        foreach (var pilot in _stalePilots)
        {
            RestorePilotVisuals(pilot);
        }
    }

    private void OnPilotVisualsStartup(Entity<CombatMechPilotVisualsComponent> ent, ref ComponentStartup args)
    {
        ApplyPilotVisuals(ent);
    }

    private void OnPilotVisualsState(Entity<CombatMechPilotVisualsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ApplyPilotVisuals(ent);
    }

    private void OnPilotVisualsShutdown(Entity<CombatMechPilotVisualsComponent> ent, ref ComponentShutdown args)
    {
        RestorePilotVisuals(ent.Owner);
    }

    private void ApplyPilotVisuals(Entity<InsideCombatVehicleComponent> pilot)
    {
        if (!TryComp(pilot.Comp.Vehicle, out CombatMechComponent? mech))
        {
            RestorePilotVisuals(pilot.Owner);
            return;
        }

        ApplyPilotVisuals(pilot.Owner, (pilot.Comp.Vehicle, mech));
    }

    private void ApplyPilotVisuals(Entity<CombatMechPilotVisualsComponent> pilot)
    {
        if (!TryComp(pilot.Owner, out SpriteComponent? sprite))
            return;

        sprite.RenderOrder = pilot.Comp.RenderOrder < 0 ? 0u : (uint) pilot.Comp.RenderOrder;
        _sprite.SetOffset((pilot.Owner, sprite), pilot.Comp.Offset);
        _sprite.SetDrawDepth((pilot.Owner, sprite), (int) DrawDepthType.Mobs);
    }

    private void ApplyPilotVisuals(EntityUid pilot, Entity<CombatMechComponent> mech)
    {
        if (!TryComp(pilot, out SpriteComponent? sprite))
            return;

        _visualPilots.Add(pilot);
        sprite.RenderOrder = mech.Comp.PilotRenderOrder < 0 ? 0u : (uint) mech.Comp.PilotRenderOrder;
        _sprite.SetOffset((pilot, sprite), GetPilotVisualOffset(pilot, mech));
        _sprite.SetDrawDepth((pilot, sprite), (int) DrawDepthType.Mobs);
    }

    private Vector2 GetPilotVisualOffset(EntityUid pilot, Entity<CombatMechComponent> mech)
    {
        return Transform(pilot).LocalRotation.GetCardinalDir() switch
        {
            Direction.North => mech.Comp.PilotVisualOffsetNorth,
            Direction.South => mech.Comp.PilotVisualOffsetSouth,
            Direction.East or Direction.West => mech.Comp.PilotVisualOffsetEastWest,
            _ => mech.Comp.PilotVisualOffsetEastWest,
        };
    }

    private void RestorePilotVisuals(EntityUid pilot)
    {
        if (!TryComp(pilot, out SpriteComponent? sprite))
            return;

        sprite.RenderOrder = 0;
        _sprite.SetOffset((pilot, sprite), Vector2.Zero);
        _sprite.SetDrawDepth((pilot, sprite), (int) GetPilotDrawDepth(pilot));
        _visualPilots.Remove(pilot);
    }

    private DrawDepthType GetPilotDrawDepth(EntityUid pilot)
    {
        var ev = new GetDrawDepthEvent(DrawDepthType.Mobs);
        RaiseLocalEvent(pilot, ref ev);
        return ev.DrawDepth;
    }
}
